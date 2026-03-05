using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    
    // Codes that are ALWAYS suppressed (genuinely irrelevant noise)
    private static readonly HashSet<string> _alwaysSuppressedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS1591", // Missing XML comment for publicly visible type or member
        "CS8019", // Unnecessary using directive
    };
    
    // Codes to suppress ONLY when NuGet references were NOT loaded (fallback mode).
    // When we have proper package references, these errors are legitimate.
    private static readonly HashSet<string> _fallbackSuppressedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS0103", // The name 'X' does not exist in the current context
        "CS0246", // The type or namespace name 'X' could not be found
        "CS0234", // The type or namespace name 'X' does not exist in namespace 'Y'
        "CS1061", // 'X' does not contain a definition for 'Y'
        "CS0117", // 'X' does not contain a definition for 'Y'
        "CS0121", // Ambiguous call
        "CS7036", // No argument given that corresponds to required parameter
        "CS0012", // The type 'X' is defined in an assembly that is not referenced
        "CS0616", // 'X' is not an attribute class
        "CS0433", // The type 'X' exists in both assemblies
        "CS0518", // Predefined type 'System.X' is not defined or imported
        "CS1729", // 'X' does not contain a constructor that takes N arguments
        "CS0535", // 'X' does not implement interface member 'Y'
        "CS0122", // 'X' is inaccessible due to its protection level
        "CS0305", // Using the generic type requires N type arguments
        "CS1503", // Argument N: cannot convert from 'X' to 'Y'
        "CS0029", // Cannot implicitly convert type 'X' to 'Y'
        "CS0311", // The type 'X' cannot be used as type parameter
    };
    

    public CodeAnalysisService()
    {
        _defaultReferences = GetDefaultReferences();
    }
    
    /// <summary>
    /// Check if a diagnostic should be suppressed.
    /// When NuGet references are loaded, only genuine noise is suppressed.
    /// When refs are missing (fallback mode), type-not-found errors are also suppressed.
    /// </summary>
    private static bool ShouldSuppressDiagnostic(Diagnostic diagnostic, bool hasNuGetRefs)
    {
        // Always suppress noise codes
        if (_alwaysSuppressedCodes.Contains(diagnostic.Id))
            return true;
        
        // When NuGet refs are NOT loaded, suppress type-not-found errors
        if (!hasNuGetRefs && _fallbackSuppressedCodes.Contains(diagnostic.Id))
            return true;
        
        return false;
    }

    /// <summary>
    /// Get default assembly references for compilation
    /// </summary>
    private List<MetadataReference> GetDefaultReferences()
    {
        var references = new List<MetadataReference>();

        try
        {
            // Get the runtime directory
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null)
            {
                var coreAssemblies = new[]
                {
                    "System.Runtime.dll",
                    "System.Console.dll",
                    "System.Collections.dll",
                    "System.Linq.dll",
                    "System.Threading.Tasks.dll",
                    "System.IO.dll",
                    "System.Text.RegularExpressions.dll",
                    "netstandard.dll",
                    "System.Private.CoreLib.dll"
                };

                foreach (var assembly in coreAssemblies)
                {
                    var path = Path.Combine(runtimeDir, assembly);
                    if (File.Exists(path))
                    {
                        try
                        {
                            references.Add(MetadataReference.CreateFromFile(path));
                        }
                        catch { }
                    }
                }
            }

            // Add mscorlib or System.Runtime
            var mscorlibPath = typeof(object).Assembly.Location;
            if (File.Exists(mscorlibPath))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(mscorlibPath));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading default references: {ex.Message}");
        }

        return references;
    }
    
    /// <summary>
    /// Load additional references from NuGet packages and project output directory.
    /// Returns (references, hasNuGetRefs) where hasNuGetRefs indicates if NuGet packages were found.
    /// </summary>
    private (List<MetadataReference> refs, bool hasNuGetRefs) GetProjectReferences(string projectPath)
    {
        var references = new List<MetadataReference>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add default refs paths to avoid duplicates
        foreach (var r in _defaultReferences)
            if (r.Display != null)
                addedPaths.Add(r.Display);

        string? projectDir = null;
        
        if (File.Exists(projectPath))
        {
            projectDir = Path.GetDirectoryName(projectPath);
        }
        else if (Directory.Exists(projectPath))
        {
            projectDir = projectPath;
        }
        
        if (projectDir == null)
            return (references, false);
        
        // Use NuGetReferenceResolver to get package references
        var nugetRefs = _nugetResolver.Resolve(projectDir);
        bool hasNuGetRefs = nugetRefs.Count > 0;
        
        foreach (var nugetRef in nugetRefs)
        {
            if (nugetRef.Display != null && addedPaths.Add(nugetRef.Display))
                references.Add(nugetRef);
        }
        
        System.Diagnostics.Debug.WriteLine(
            $"[CodeAnalysis] Project references: {references.Count} NuGet refs loaded, fallback mode: {!hasNuGetRefs}");
        
        return (references, hasNuGetRefs);
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
            var syntaxTree = CSharpSyntaxTree.ParseText(content, path: filePath);

            // Create compilation
            var compilation = CSharpCompilation.Create(
                "TempAnalysis",
                new[] { syntaxTree },
                _defaultReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // Get diagnostics
            var roslynDiagnostics = compilation.GetDiagnostics();

            foreach (var diagnostic in roslynDiagnostics)
            {
                // Skip suppressed diagnostics (fallback mode — no NuGet refs for single file)
                if (ShouldSuppressDiagnostic(diagnostic, false))
                    continue;
                    
                var lineSpan = diagnostic.Location.GetLineSpan();
                diagnostics.Add(new DiagnosticItem
                {
                    Severity = ConvertSeverity(diagnostic.Severity),
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
            // Find all C# files
            string[] csFiles;
            
            if (File.Exists(projectPath))
            {
                var ext = Path.GetExtension(projectPath).ToLowerInvariant();
                if (ext is ".sln" or ".slnx" or ".csproj" or ".fsproj" or ".vbproj" or ".nfproj")
                {
                    // Solution or project file — resolve to directory and search for .cs files
                    var dir = Path.GetDirectoryName(projectPath);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                        return allDiagnostics;
                    
                    csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                                   !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                                   !f.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar))
                        .ToArray();
                }
                else if (ext == ".cs")
                {
                    // Single .cs file
                    csFiles = new[] { projectPath };
                }
                else
                {
                    // Not a supported file type for analysis
                    return allDiagnostics;
                }
            }
            else if (Directory.Exists(projectPath))
            {
                // Directory - find all .cs files, excluding bin, obj, and other generated folders
                csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                               !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                               !f.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar))
                    .ToArray();
            }
            else
            {
                return allDiagnostics;
            }

            var totalFiles = csFiles.Length;
            var processedFiles = 0;

            // Parse all files into syntax trees
            var syntaxTrees = new List<SyntaxTree>();
            var filePathMap = new Dictionary<SyntaxTree, string>();

            OnProgress($"Parsing {totalFiles} C# files...", 0, totalFiles);

            foreach (var file in csFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    var syntaxTree = CSharpSyntaxTree.ParseText(
                        content,
                        path: file,
                        cancellationToken: cancellationToken);
                    
                    syntaxTrees.Add(syntaxTree);
                    filePathMap[syntaxTree] = file;
                }
                catch (Exception ex)
                {
                    allDiagnostics.Add(new DiagnosticItem
                    {
                        Severity = AppDiagnosticSeverity.Error,
                        Message = $"Failed to parse file: {ex.Message}",
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Line = 1,
                        Column = 1,
                        Code = "PARSE_ERROR"
                    });
                }

                processedFiles++;
                OnProgress($"Parsing: {Path.GetFileName(file)}", processedFiles, totalFiles);
            }

            if (syntaxTrees.Count == 0)
            {
                return allDiagnostics;
            }

            OnProgress("Running code analysis...", totalFiles, totalFiles);

            // Get project-specific references (NuGet packages + bin/ folder)
            var (projectReferences, hasNuGetRefs) = GetProjectReferences(projectPath);
            var allReferences = _defaultReferences.Concat(projectReferences).ToList();

            // Create compilation with all syntax trees
            var compilation = CSharpCompilation.Create(
                "ProjectAnalysis",
                syntaxTrees,
                allReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithNullableContextOptions(NullableContextOptions.Enable));

            // Get all diagnostics
            var diagnostics = compilation.GetDiagnostics(cancellationToken);

            foreach (var diagnostic in diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip hidden diagnostics
                if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden)
                    continue;
                
                // Skip suppressed diagnostics (context-aware based on NuGet refs)
                if (ShouldSuppressDiagnostic(diagnostic, hasNuGetRefs))
                    continue;

                var location = diagnostic.Location;
                var filePath = location.SourceTree != null && filePathMap.TryGetValue(location.SourceTree, out var path)
                    ? path
                    : location.SourceTree?.FilePath ?? "Unknown";

                var lineSpan = location.GetLineSpan();

                allDiagnostics.Add(new DiagnosticItem
                {
                    Severity = ConvertSeverity(diagnostic.Severity),
                    Message = diagnostic.GetMessage(),
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    Code = diagnostic.Id
                });
            }

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

                var severity = severityStr switch
                {
                    "error" => AppDiagnosticSeverity.Error,
                    "warning" => AppDiagnosticSeverity.Warning,
                    _ => AppDiagnosticSeverity.Info
                };

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

    private AppDiagnosticSeverity ConvertSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity severity)
    {
        return severity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => AppDiagnosticSeverity.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => AppDiagnosticSeverity.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => AppDiagnosticSeverity.Info,
            _ => AppDiagnosticSeverity.Hint
        };
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
