using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Analyzer;

/// <summary>
/// Checks a specific .cs file for syntax and semantic errors
/// using the Roslyn analyzer. Designed for real-time checking
/// during typing — call <see cref="CheckFileAsync"/> with the
/// file path or <see cref="CheckTextAsync"/> with inline source.
/// </summary>
public sealed class FileSyntaxCheckerService
{
    // Preprocessor CS diagnostic codes that fall outside the CS1xxx
    // range but are still syntax-level issues (not semantic).
    private static readonly HashSet<string> PreprocessorSyntaxCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS8001",  // #nullable only available in C# 8.0+
        "CS8026",  // Feature not available for #nullable
        "CS8054",  // Unexpected preprocessor directive (single-line)
        "CS8632",  // #nullable annotations context
        "CS9010",  // #pragma warning after first token on line
        "CS9012",  // #line with file name requires C# 10+
        "CS9025",  // #pragma warning expects a warning code
        "CS9042",  // #pragma warning after parameter list
        "CS9056",  // #pragma warning in invalid context
    };

    private readonly ProjectAnalysisService _service = new();
    private string? _lastProjectDir;
    private ProjectAnalysisResult? _lastResult;

    /// <summary>
    /// Check a .cs file on disk for syntax and semantic errors.
    /// Automatically locates the project directory.
    /// </summary>
    public async Task<FileCheckResult> CheckFileAsync(
        string filePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));
        if (!File.Exists(filePath))
            return new FileCheckResult(filePath, Array.Empty<FileDiagnostic>(),
                $"File not found: {filePath}");

        var projectDir = FindProjectDir(filePath);
        if (projectDir is null)
            return new FileCheckResult(filePath, Array.Empty<FileDiagnostic>(),
                $"Could not find project directory for: {filePath}");

        return await RunCheckAsync(filePath, projectDir, ct);
    }

    /// <summary>
    /// Check inline source code as if it were the given file.
    /// The project directory is used for context (references, other files).
    /// </summary>
    public async Task<FileCheckResult> CheckTextAsync(
        string filePath,
        string sourceCode,
        string? projectDirHint = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        var projectDir = projectDirHint ?? FindProjectDir(filePath);
        if (projectDir is null)
            return new FileCheckResult(filePath, Array.Empty<FileDiagnostic>(),
                "Could not determine project directory. Provide --project or place the file in a project.");

        // Write source to a temp file so the analyzer can read it as part of the project
        var tempDir = Path.Combine(Path.GetTempPath(), "RoslynFileChecker");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, Path.GetFileName(filePath));
        await File.WriteAllTextAsync(tempFile, sourceCode, ct);

        try
        {
            return await RunCheckAsync(tempFile, projectDir, ct);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private async Task<FileCheckResult> RunCheckAsync(
        string filePath, string projectDir, CancellationToken ct)
    {
        try
        {
            // Reuse last result if project dir hasn't changed
            ProjectAnalysisResult result;
            if (string.Equals(_lastProjectDir, projectDir, StringComparison.OrdinalIgnoreCase) &&
                _lastResult is not null)
            {
                result = _lastResult;
            }
            else
            {
                result = await _service.AnalyzeProjectAsync(projectDir, ct: ct);
                _lastProjectDir = projectDir;
                _lastResult = result;
            }

            var diagnostics = new List<FileDiagnostic>();

            CollectDiagnostics(result.CompilationDiagnostics, filePath, diagnostics);
            CollectDiagnostics(result.AnalyzerDiagnostics, filePath, diagnostics);

            return new FileCheckResult(filePath, diagnostics, null);
        }
        catch (OperationCanceledException)
        {
            return new FileCheckResult(filePath, Array.Empty<FileDiagnostic>(), "Cancelled");
        }
        catch (Exception ex)
        {
            return new FileCheckResult(filePath, Array.Empty<FileDiagnostic>(), ex.Message);
        }
    }

    private static void CollectDiagnostics(
        IEnumerable<Diagnostic> source, string filePath, List<FileDiagnostic> target)
    {
        var normalizedTarget = NormalizePath(filePath);

        foreach (var diag in source)
        {
            if (!diag.Location.IsInSource) continue;

            var path = diag.Location.SourceTree?.FilePath;
            if (string.IsNullOrEmpty(path)) continue;

            if (!string.Equals(NormalizePath(path), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                continue;

            var span = diag.Location.SourceSpan;
            var line = diag.Location.GetLineSpan().StartLinePosition;
            var isError = diag.Severity == DiagnosticSeverity.Error;
            var isSyntaxError = diag.Id.StartsWith("CS") &&
                int.TryParse(diag.Id.AsSpan(2), out var code) &&
                (code is >= 1001 and <= 1999 || PreprocessorSyntaxCodes.Contains(diag.Id));

            target.Add(new FileDiagnostic(
                diag.Id,
                diag.GetMessage(),
                diag.Severity.ToString().ToLowerInvariant(),
                line.Line + 1,
                line.Character + 1,
                span.Start,
                Math.Max(span.End, span.Start + 1),
                isError,
                isSyntaxError));
        }
    }

    private static string? FindProjectDir(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0)
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(dir, parent, StringComparison.OrdinalIgnoreCase)) break;
            dir = parent;
        }
        return null;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path; }
    }
}

/// <summary>
/// Result of checking a single file for syntax/semantic errors.
/// </summary>
public sealed record FileCheckResult(
    string FilePath,
    IReadOnlyList<FileDiagnostic> Diagnostics,
    string? Error)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);
    public bool HasSyntaxErrors => Diagnostics.Any(d => d.IsSyntaxError);
    public bool HasSemanticErrors => Diagnostics.Any(d => d.IsError && !d.IsSyntaxError);
}

/// <summary>
/// A single diagnostic found in a file.
/// </summary>
public sealed record FileDiagnostic(
    string Code,
    string Message,
    string Severity,
    int Line,
    int Column,
    int StartOffset,
    int EndOffset,
    bool IsError,
    bool IsSyntaxError)
{
    public bool IsSemanticError => IsError && !IsSyntaxError;
}
