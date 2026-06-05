using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Analyzer;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.ViewModels;
using Microsoft.CodeAnalysis;
using AppDiagnosticSeverity = Insait_Edit_C_Sharp.ViewModels.DiagnosticSeverity;

namespace Insait_Edit_C_Sharp.Services;

public class CodeAnalysisService
{
    public event EventHandler<AnalysisCompletedEventArgs>? AnalysisCompleted;
    public event EventHandler<AnalysisProgressEventArgs>? AnalysisProgress;

    private CancellationTokenSource? _analysisCts;
    private readonly object _lock = new();
    private readonly ProjectAnalysisService _analyzer = new();

    /// <summary>
    /// Full project analysis. Runs on thread pool; fully async.
    /// </summary>
    public Task<List<DiagnosticItem>> AnalyzeProjectAsync(
        string projectPath,
        CancellationToken ct = default)
    {
        var targets = ResolveTargets(projectPath);
        if (targets.Count == 0)
            return Task.FromResult(new List<DiagnosticItem>());

        return Task.Run(() => AnalyzeAllCoreAsync(targets, ct), ct);
    }

    // ── Core analysis ───────────────────────────────────────────────────────

    private async Task<List<DiagnosticItem>> AnalyzeAllCoreAsync(
        List<string> targets, CancellationToken ct)
    {
        var results = new List<DiagnosticItem>();
        var sdkRoot = GetSdkRoot();
        var isMultiTarget = targets.Count > 1;

        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var label = Path.GetFileName(targets[i]);
            ReportProgress($"Analysing {label}...", i, targets.Count);

            List<DiagnosticItem> items;
            if (string.Equals(Path.GetExtension(targets[i]), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                items = await AnalyzeOneFileAsync(targets[i], ct).ConfigureAwait(false);
            }
            else
            {
                items = await AnalyzeOneProjectAsync(targets[i], sdkRoot, ct).ConfigureAwait(false);
            }

            results.AddRange(items);
        }

        ReportProgress(isMultiTarget ? "Multi-target analysis complete" : "Code analysis complete",
            targets.Count, targets.Count);

        results = results
            .OrderBy(d => d.Severity == AppDiagnosticSeverity.Error ? 0
                       : d.Severity == AppDiagnosticSeverity.Warning ? 1 : 2)
            .ThenBy(d => d.FilePath)
            .ThenBy(d => d.Line)
            .ToList();
        return results;
    }

    private async Task<List<DiagnosticItem>> AnalyzeOneProjectAsync(
        string targetPath, string? sdkRoot, CancellationToken ct)
    {
        var projectDir = NuGetReferenceResolver.ResolveProjectDirectory(targetPath);
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return new List<DiagnosticItem>();

        try
        {
            ProjectAnalysisService.AnalysisProgressHandler? onProgress = null;
            if (AnalysisProgress != null)
            {
                var label = Path.GetFileName(targetPath);
                onProgress = (msg, cur, total) =>
                    AnalysisProgress?.Invoke(this,
                        new AnalysisProgressEventArgs($"{label}: {msg}", cur, total));
            }

            // Full analysis (build + compile + analyzers) delegated to Analyzer project
            var result = await _analyzer
                .AnalyzeProjectAsync(projectDir, sdkRoot, onProgress, ct)
                .ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            var mapped = new List<DiagnosticItem>(
                result.CompilationDiagnostics.Length + result.AnalyzerDiagnostics.Count);
            mapped.AddRange(MapDiags(result.CompilationDiagnostics, result.HasProjectReferences));
            mapped.AddRange(MapDiags(result.AnalyzerDiagnostics, result.HasProjectReferences));
            return mapped;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new List<DiagnosticItem>
            {
                new()
                {
                    Severity = AppDiagnosticSeverity.Error,
                    Message = ex.Message,
                    FilePath = targetPath,
                    FileName = Path.GetFileName(targetPath),
                    Line = 1, Column = 1, Code = "ANALYSIS_ERROR"
                }
            };
        }
    }

    // ── File-level analysis ─────────────────────────────────────────────────

    private async Task<List<DiagnosticItem>> AnalyzeOneFileAsync(
        string filePath, CancellationToken ct)
    {
        if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return new List<DiagnosticItem>();

        try
        {
            var projectDir = NuGetReferenceResolver.ResolveProjectDirectory(filePath)
                             ?? Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
                return new List<DiagnosticItem> { MakeError(filePath, "No project directory found") };

            var result = await _analyzer
                .AnalyzeProjectAsync(projectDir, GetSdkRoot(), null, ct)
                .ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            return MapDiags(result.CompilationDiagnostics, result.HasProjectReferences)
                .Concat(MapDiags(result.AnalyzerDiagnostics, result.HasProjectReferences))
                .Where(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new List<DiagnosticItem> { MakeError(filePath, $"Analysis failed: {ex.Message}") };
        }
    }

    public async Task<List<DiagnosticItem>> AnalyzeFileAsync(string filePath)
    {
        return await AnalyzeOneFileAsync(filePath, CancellationToken.None)
            .ConfigureAwait(false);
    }

    // ── Mapping ─────────────────────────────────────────────────────────────

    private static List<DiagnosticItem> MapDiags(
        IEnumerable<Diagnostic> source, bool hasProjectRefs)
    {
        return source
            .Select(d =>
            {
                string path;
                int line = 1, column = 1;
                if (d.Location.IsInSource)
                {
                    var span = d.Location.GetLineSpan();
                    path = d.Location.SourceTree?.FilePath ?? span.Path ?? string.Empty;
                    line = span.StartLinePosition.Line + 1;
                    column = span.StartLinePosition.Character + 1;
                }
                else
                {
                    path = string.Empty;
                }

                var sev = DiagnosticSeverityMatrix.ResolveApp(d.Id, d.Severity, hasProjectRefs);
                if (sev is null) return null;
                return new DiagnosticItem
                {
                    Severity = sev.Value,
                    Message = d.GetMessage(),
                    FilePath = path,
                    FileName = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path),
                    Line = line,
                    Column = column,
                    Code = d.Id,
                };
            })
            .Where(d => d is not null)
            .Cast<DiagnosticItem>()
            .DistinctBy(d => (d.Code, d.FilePath, d.Line, d.Column, d.Message))
            .ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<string> ResolveTargets(string projectPath)
    {
        var targets = new List<string>();
        if (File.Exists(projectPath))
        {
            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            switch (ext)
            {
                case ".cs":     targets.Add(projectPath); return targets;
                case ".csproj": targets.Add(projectPath); return targets;
                case ".sln": case ".slnx":
                    targets.AddRange(NuGetReferenceResolver.FindProjectFiles(
                        Path.GetDirectoryName(projectPath)));
                    return targets;
                default: return targets;
            }
        }
        if (Directory.Exists(projectPath))
        {
            targets.AddRange(NuGetReferenceResolver.FindProjectFiles(projectPath));
            if (targets.Count == 0) targets.Add(projectPath);
        }
        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? GetSdkRoot()
        => SettingsPanelControl.GetDotNetSdkPath();

    private static DiagnosticItem MakeError(string path, string msg)
        => new()
        {
            Severity = AppDiagnosticSeverity.Error,
            Message = msg,
            FilePath = path,
            FileName = Path.GetFileName(path),
            Line = 1, Column = 1, Code = "ANALYSIS_ERROR"
        };

    private void ReportProgress(string msg, int cur, int total)
        => AnalysisProgress?.Invoke(this, new AnalysisProgressEventArgs(msg, cur, total));

    // ── Public API: callback-based analysis (called from UI button) ─────────

    public async Task AnalyzeProjectWithCallbackAsync(string projectPath)
    {
        CancellationToken token;
        lock (_lock)
        {
            _analysisCts?.Cancel();
            _analysisCts = new CancellationTokenSource();
            token = _analysisCts.Token;
        }

        try
        {
            var diagnostics = await AnalyzeProjectAsync(projectPath, token)
                .ConfigureAwait(false);
            AnalysisCompleted?.Invoke(this,
                new AnalysisCompletedEventArgs(diagnostics, true, null));
        }
        catch (OperationCanceledException)
        {
            AnalysisCompleted?.Invoke(this,
                new AnalysisCompletedEventArgs(
                    new List<DiagnosticItem>(), false, "Analysis cancelled"));
        }
        catch (Exception ex)
        {
            AnalysisCompleted?.Invoke(this,
                new AnalysisCompletedEventArgs(
                    new List<DiagnosticItem>(), false, ex.Message));
        }
    }

    public void CancelAnalysis()
    {
        lock (_lock) { _analysisCts?.Cancel(); }
    }

    // ── Build output parser ─────────────────────────────────────────────────

    public List<DiagnosticItem> ParseBuildOutput(string buildOutput)
    {
        var diagnostics = new List<DiagnosticItem>();
        if (string.IsNullOrEmpty(buildOutput)) return diagnostics;

        foreach (var line in buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(
                line.Trim(),
                @"^(.+?)\((\d+),(\d+)\):\s*(error|warning|info)\s+(\w+):\s*(.+)$",
                RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var native = m.Groups[4].Value.ToLower() switch
            {
                "error" => Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
                "warning" => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
                _ => Microsoft.CodeAnalysis.DiagnosticSeverity.Info,
            };
            var severity = DiagnosticSeverityMatrix.ResolveApp(
                m.Groups[5].Value, native, hasNuGetRefs: true)
                           ?? AppDiagnosticSeverity.Info;

            diagnostics.Add(new DiagnosticItem
            {
                Severity = severity,
                Message = m.Groups[6].Value,
                FilePath = m.Groups[1].Value,
                FileName = Path.GetFileName(m.Groups[1].Value),
                Line = int.Parse(m.Groups[2].Value),
                Column = int.Parse(m.Groups[3].Value),
                Code = m.Groups[5].Value,
            });
        }
        return diagnostics;
    }
}

public class AnalysisCompletedEventArgs : EventArgs
{
    public List<DiagnosticItem> Diagnostics { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public AnalysisCompletedEventArgs(List<DiagnosticItem> d, bool s, string? e)
    { Diagnostics = d; Success = s; ErrorMessage = e; }
}

public class AnalysisProgressEventArgs : EventArgs
{
    public string Message { get; }
    public int Current { get; }
    public int Total { get; }
    public double Progress => Total > 0 ? (double)Current / Total * 100 : 0;
    public AnalysisProgressEventArgs(string msg, int cur, int total)
    { Message = msg; Current = cur; Total = total; }
}
