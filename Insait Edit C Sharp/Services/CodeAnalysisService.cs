using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Insait_Edit_C_Sharp.ViewModels;

// Alias to avoid ambiguity with Microsoft.CodeAnalysis.DiagnosticSeverity
using AppDiagnosticSeverity = Insait_Edit_C_Sharp.ViewModels.DiagnosticSeverity;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for analyzing C# code using Roslyn
/// </summary>
public class CodeAnalysisService
{
    public event EventHandler<AnalysisCompletedEventArgs>? AnalysisCompleted;
    public event EventHandler<AnalysisProgressEventArgs>? AnalysisProgress;

    private CancellationTokenSource? _analysisCts;
    private readonly List<MetadataReference> _defaultReferences;
    private readonly NuGetReferenceResolver _nugetResolver = new();

    public CodeAnalysisService()
    {
        _defaultReferences = GetDefaultReferences();
    }

    // All severity resolution is delegated to DiagnosticSeverityMatrix so that
    // both InlineDiagnosticService and CodeAnalysisService stay in sync.

    /// <summary>
    /// Get default assembly references for compilation
    /// </summary>
    private List<MetadataReference> GetDefaultReferences()
    {
        return RoslynCompletionEngine.CollectPublicDefaultReferences();
    }
    
    /// <summary>
    /// Analyze a single C# file
    /// </summary>
    public async Task<List<DiagnosticItem>> AnalyzeFileAsync(string filePath, string? content = null)
    {
        var diagnostics = new List<DiagnosticItem>();

        // Only analyze .cs files
        if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return diagnostics;

        try
        {
            content ??= await File.ReadAllTextAsync(filePath);
            var build = RoslynProjectFactory.CreateBuild(Path.GetDirectoryName(filePath), _defaultReferences, filePath, content);

            using var workspace = new AdhocWorkspace(RoslynWorkspaceService.Instance.Host);
            var solution = workspace.CurrentSolution.AddProject(build.ProjectInfo);
            if (!workspace.TryApplyChanges(solution))
                throw new InvalidOperationException("Failed to initialize Roslyn workspace for file analysis.");

            var project = workspace.CurrentSolution.GetProject(build.ProjectInfo.Id);
            if (project == null)
                return diagnostics;

            var roslynDiagnostics = await CollectProjectDiagnosticsAsync(project, CancellationToken.None);

            foreach (var diagnostic in roslynDiagnostics)
            {
                if (!build.BelongsToFile(diagnostic, filePath))
                    continue;

                // Resolve effective severity via the central matrix (null = suppress)
                var severity = DiagnosticSeverityMatrix.ResolveApp(
                    diagnostic.Id, diagnostic.Severity, build.HasProjectMetadataReferences);
                if (severity is null)
                    continue;
                    
                var lineSpan = diagnostic.Location.GetLineSpan();
                diagnostics.Add(new DiagnosticItem
                {
                    Severity = severity.Value,
                    Message = diagnostic.GetMessage(),
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    Code = diagnostic.Id
                });
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new DiagnosticItem
            {
                Severity = AppDiagnosticSeverity.Error,
                Message = $"Analysis failed: {ex.Message}",
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Line = 1,
                Column = 1,
                Code = "ANALYSIS_ERROR"
            });
        }

        return diagnostics;
    }

    /// <summary>
    /// Analyze all C# files in a project folder
    /// </summary>
    public async Task<List<DiagnosticItem>> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var allDiagnostics = new List<DiagnosticItem>();

        try
        {
            var targets = ResolveAnalysisTargets(projectPath);
            if (targets.Count == 0)
                return allDiagnostics;

            var targetIndex = 0;
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                targetIndex++;

                OnProgress($"Analysing {Path.GetFileName(target)}...", targetIndex - 1, targets.Count);

                if (string.Equals(Path.GetExtension(target), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    allDiagnostics.AddRange(await AnalyzeFileAsync(target));
                    continue;
                }

                allDiagnostics.AddRange(await AnalyzeProjectTargetAsync(target, cancellationToken));
            }

            OnProgress("Code analysis complete", targets.Count, targets.Count);

            // Sort by severity (errors first), then by file, then by line
            allDiagnostics = allDiagnostics
                .OrderBy(d => d.Severity == AppDiagnosticSeverity.Error ? 0 : d.Severity == AppDiagnosticSeverity.Warning ? 1 : 2)
                .ThenBy(d => d.FilePath)
                .ThenBy(d => d.Line)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            allDiagnostics.Add(new DiagnosticItem
            {
                Severity = AppDiagnosticSeverity.Error,
                Message = $"Project analysis failed: {ex.Message}",
                FilePath = projectPath,
                FileName = Path.GetFileName(projectPath),
                Line = 1,
                Column = 1,
                Code = "ANALYSIS_ERROR"
            });
        }

        return allDiagnostics;
    }

    private List<string> ResolveAnalysisTargets(string projectPath)
    {
        var targets = new List<string>();

        if (File.Exists(projectPath))
        {
            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            switch (ext)
            {
                case ".cs":
                    targets.Add(projectPath);
                    break;
                case ".csproj":
                    targets.Add(projectPath);
                    break;
                case ".sln":
                case ".slnx":
                    targets.AddRange(NuGetReferenceResolver.FindProjectFiles(Path.GetDirectoryName(projectPath)));
                    break;
            }
        }
        else if (Directory.Exists(projectPath))
        {
            targets.AddRange(NuGetReferenceResolver.FindProjectFiles(projectPath));
            if (targets.Count == 0)
                targets.Add(projectPath);
        }

        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<DiagnosticItem>> AnalyzeProjectTargetAsync(string targetPath, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticItem>();

        var projectDir = NuGetReferenceResolver.ResolveProjectDirectory(targetPath);
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return diagnostics;

        string activeFilePath;
        string activeSource;
        try
        {
            activeFilePath = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .First(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            activeSource = await File.ReadAllTextAsync(activeFilePath, cancellationToken);
        }
        catch
        {
            return diagnostics;
        }

        var build = RoslynProjectFactory.CreateBuild(projectDir, _defaultReferences, activeFilePath, activeSource);
        using var workspace = new AdhocWorkspace(RoslynWorkspaceService.Instance.Host);
        var solution = workspace.CurrentSolution.AddProject(build.ProjectInfo);
        if (!workspace.TryApplyChanges(solution))
        {
            diagnostics.Add(new DiagnosticItem
            {
                Severity = AppDiagnosticSeverity.Error,
                Message = "Failed to initialize Roslyn workspace for project analysis.",
                FilePath = targetPath,
                FileName = Path.GetFileName(targetPath),
                Line = 1,
                Column = 1,
                Code = "ANALYSIS_ERROR"
            });
            return diagnostics;
        }

        var project = workspace.CurrentSolution.GetProject(build.ProjectInfo.Id);
        if (project == null)
            return diagnostics;

        foreach (var diagnostic in await CollectProjectDiagnosticsAsync(project, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!build.ShouldIncludeDiagnostic(diagnostic))
                continue;

            // Resolve effective severity via the central matrix (null = suppress)
            var severity = DiagnosticSeverityMatrix.ResolveApp(
                diagnostic.Id, diagnostic.Severity, build.HasProjectMetadataReferences);
            if (severity is null)
                continue;

            var location = diagnostic.Location;
            var filePath = location.SourceTree?.FilePath ?? location.GetLineSpan().Path;
            var lineSpan = location.GetLineSpan();
            diagnostics.Add(new DiagnosticItem
            {
                Severity = severity.Value,
                Message = diagnostic.GetMessage(),
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                Code = diagnostic.Id
            });
        }

        return diagnostics;
    }

    private static async Task<List<Diagnostic>> CollectProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
            return new List<Diagnostic>();

        var diagnostics = compilation.GetDiagnostics(cancellationToken).ToList();

        // Merge project NuGet analyzers with built-in IDE analyzers (IDE0001…IDE1006)
        var projectAnalyzers = project.AnalyzerReferences
            .SelectMany(reference => SafeGetAnalyzers(reference, project.Language));
        var analyzers = BuiltInAnalyzerProvider.Merge(projectAnalyzers);

        if (analyzers.Length == 0)
            return diagnostics;

        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);

        try
        {
            var analyzerDiagnostics = await compilation
                .WithAnalyzers(analyzers, options)
                .GetAnalyzerDiagnosticsAsync(cancellationToken);
            diagnostics.AddRange(analyzerDiagnostics);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CodeAnalysis] Analyzer run failed: {ex.Message}");
        }

        return diagnostics
            .GroupBy(static diagnostic => new
            {
                diagnostic.Id,
                FilePath = diagnostic.Location.SourceTree?.FilePath ?? diagnostic.Location.GetLineSpan().Path,
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length,
                Message = diagnostic.GetMessage(),
                diagnostic.Severity,
            })
            .Select(static group => group.First())
            .ToList();
    }

    private static IEnumerable<DiagnosticAnalyzer> SafeGetAnalyzers(AnalyzerReference reference, string language)
    {
        try
        {
            return reference.GetAnalyzers(language);
        }
        catch
        {
            return Enumerable.Empty<DiagnosticAnalyzer>();
        }
    }

    /// <summary>
    /// Analyze project and fire completion event
    /// </summary>
    public async Task AnalyzeProjectWithCallbackAsync(string projectPath)
    {
        // Cancel any previous analysis
        _analysisCts?.Cancel();
        _analysisCts = new CancellationTokenSource();

        try
        {
            var diagnostics = await AnalyzeProjectAsync(projectPath, _analysisCts.Token);
            AnalysisCompleted?.Invoke(this, new AnalysisCompletedEventArgs(diagnostics, true, null));
        }
        catch (OperationCanceledException)
        {
            AnalysisCompleted?.Invoke(this, new AnalysisCompletedEventArgs(new List<DiagnosticItem>(), false, "Analysis cancelled"));
        }
        catch (Exception ex)
        {
            AnalysisCompleted?.Invoke(this, new AnalysisCompletedEventArgs(new List<DiagnosticItem>(), false, ex.Message));
        }
    }

    /// <summary>
    /// Cancel any running analysis
    /// </summary>
    public void CancelAnalysis()
    {
        _analysisCts?.Cancel();
    }

    /// <summary>
    /// Parse build output and extract errors/warnings
    /// </summary>
    public List<DiagnosticItem> ParseBuildOutput(string buildOutput)
    {
        var diagnostics = new List<DiagnosticItem>();

        if (string.IsNullOrEmpty(buildOutput))
            return diagnostics;

        var lines = buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Match patterns like:
            // C:\path\file.cs(10,5): error CS1002: ; expected
            // C:\path\file.cs(10,5): warning CS0168: The variable 'x' is declared but never used
            
            var errorMatch = System.Text.RegularExpressions.Regex.Match(
                trimmedLine,
                @"^(.+?)\((\d+),(\d+)\):\s*(error|warning|info)\s+(\w+):\s*(.+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (errorMatch.Success)
            {
                var filePath = errorMatch.Groups[1].Value;
                var line_num = int.Parse(errorMatch.Groups[2].Value);
                var column = int.Parse(errorMatch.Groups[3].Value);
                var severityStr = errorMatch.Groups[4].Value.ToLower();
                var code = errorMatch.Groups[5].Value;
                var message = errorMatch.Groups[6].Value;

                // Apply matrix reclassification for build output too
                // (e.g. CS8019 from build output → Info, not Warning)
                var nativeRoslyn = severityStr switch
                {
                    "error"   => Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
                    "warning" => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
                    _         => Microsoft.CodeAnalysis.DiagnosticSeverity.Info,
                };
                var severity = DiagnosticSeverityMatrix.ResolveApp(code, nativeRoslyn, hasNuGetRefs: true)
                               ?? AppDiagnosticSeverity.Info;

                diagnostics.Add(new DiagnosticItem
                {
                    Severity = severity,
                    Message = message,
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Line = line_num,
                    Column = column,
                    Code = code
                });
            }
        }

        return diagnostics;
    }


    private void OnProgress(string message, int current, int total)
    {
        AnalysisProgress?.Invoke(this, new AnalysisProgressEventArgs(message, current, total));
    }
}

/// <summary>
/// Event args for analysis completion
/// </summary>
public class AnalysisCompletedEventArgs : EventArgs
{
    public List<DiagnosticItem> Diagnostics { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }

    public AnalysisCompletedEventArgs(List<DiagnosticItem> diagnostics, bool success, string? errorMessage)
    {
        Diagnostics = diagnostics;
        Success = success;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Event args for analysis progress
/// </summary>
public class AnalysisProgressEventArgs : EventArgs
{
    public string Message { get; }
    public int Current { get; }
    public int Total { get; }
    public double Progress => Total > 0 ? (double)Current / Total * 100 : 0;

    public AnalysisProgressEventArgs(string message, int current, int total)
    {
        Message = message;
        Current = current;
        Total = total;
    }
}
