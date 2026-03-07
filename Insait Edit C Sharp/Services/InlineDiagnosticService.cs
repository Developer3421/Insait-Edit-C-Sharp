using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System.IO;
using System.Reflection;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Real-time Roslyn diagnostics service — the core of JetBrains-quality
/// inline error/warning highlighting.  
///
/// On every document edit (debounced 500 ms) it:
///  1. Re-compiles the document in a background AdhocWorkspace
///  2. Converts Roslyn diagnostics to DiagnosticSpan objects (with offsets)
///  3. Fires DiagnosticsUpdated so AvaloniaEditor can repaint squiggly lines
///  4. Also fires for quick-fix suggestions
/// </summary>
public sealed class InlineDiagnosticService : IDisposable
{
    private readonly AdhocWorkspace        _workspace;
    private readonly List<MetadataReference> _refs;
    private ProjectId?  _projectId;
    private DocumentId? _documentId;
    private string?     _trackedFilePath;

    private readonly QuickFixService       _quickFixService;
    private readonly NuGetReferenceResolver _nugetResolver = new();
    private CancellationTokenSource?       _cts;

    // ── project context ──────────────────────────────────────────────────
    private string? _projectDir;
    private List<string>? _projectCsFiles;
    private List<MetadataReference>? _nugetRefs;

    // ── Noise suppression ─────────────────────────────────────────────────
    // Only suppress codes that are genuinely irrelevant noise.
    // With NuGet references loaded, most type-not-found errors are now legitimate.
    private static readonly HashSet<string> _alwaysSuppressedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS1591", // Missing XML comment
        "CS8019", // Unnecessary using directive
    };
    
    // Codes to suppress ONLY when NuGet references were NOT loaded (fallback mode).
    // When we have proper references, these errors are legitimate.
    private static readonly HashSet<string> _fallbackSuppressedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS0103", "CS0246", "CS0234", "CS1061", "CS0117",
        "CS0121", "CS7036", "CS0012",
        "CS0616", "CS0433", "CS0518", "CS1729",
        "CS0535", "CS0122", "CS0305", "CS1503",
        "CS0029", "CS0311",
    };
    
    /// <summary>Whether NuGet references were successfully loaded for the current project.</summary>
    private bool _hasNuGetRefs;
    

    public event EventHandler<InlineDiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

    public InlineDiagnosticService()
    {
        var host = MefHostServices.Create(BuildMefAssemblies());
        _workspace = new AdhocWorkspace(host);
        _refs      = RoslynCompletionEngine.CollectPublicDefaultReferences();
        _quickFixService = new QuickFixService();
    }

    /// <summary>
    /// Schedules a diagnostic run for the given file+source after a short delay.
    /// Any previously scheduled run is cancelled.
    /// </summary>
    public void ScheduleAnalysis(string filePath, string sourceCode, int delayMs = 600)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Task.Delay(delayMs, ct).ContinueWith(async _ =>
        {
            if (ct.IsCancellationRequested) return;
            try { await RunAnalysisAsync(filePath, sourceCode, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"InlineDiag: {ex.Message}"); }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>Immediately run diagnostics (no delay).</summary>
    public async Task AnalyzeNowAsync(string filePath, string sourceCode, CancellationToken ct = default)
    {
        await RunAnalysisAsync(filePath, sourceCode, ct);
    }

    private async Task RunAnalysisAsync(string filePath, string sourceCode, CancellationToken ct)
    {
        var isCsharp = filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                       !filePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase);

        List<DiagnosticSpan> spans;

        if (isCsharp)
            spans = await AnalyzeCSharpAsync(filePath, sourceCode, ct);
        else
            spans = new List<DiagnosticSpan>(); // F#/AXAML/etc. — no inline for now

        DiagnosticsUpdated?.Invoke(this, new InlineDiagnosticsUpdatedEventArgs(filePath, spans));
    }

    private async Task<List<DiagnosticSpan>> AnalyzeCSharpAsync(
        string filePath, string sourceCode, CancellationToken ct)
    {
        var spans = new List<DiagnosticSpan>();

        // Sync the document into the workspace
        Document document;
        try   { document = SyncDocument(filePath, sourceCode); }
        catch { return spans; }

        // Get semantic diagnostics
        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel == null) return spans;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct);
        if (syntaxTree == null) return spans;

        // Collect both syntax AND semantic diagnostics
        var allDiagnostics = semanticModel.GetDiagnostics(cancellationToken: ct)
            .Concat(syntaxTree.GetDiagnostics(ct))
            .Where(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden)
            .Where(d => !ShouldSuppress(d))
            .ToList();

        foreach (var diag in allDiagnostics)
        {
            ct.ThrowIfCancellationRequested();

            var loc   = diag.Location;
            if (!loc.IsInSource) continue;

            var span  = loc.SourceSpan;
            var line  = loc.GetLineSpan().StartLinePosition;

            // Compute quick fixes asynchronously
            var fixes = new List<QuickFixSuggestion>();
            try
            {
                fixes = await _quickFixService.GetFixesAsync(
                    filePath, sourceCode,
                    span.Start, span.End,
                    diag.Id, diag.GetMessage(),
                    ct);
            }
            catch { /* best-effort */ }

            spans.Add(new DiagnosticSpan
            {
                StartOffset = span.Start,
                EndOffset   = Math.Max(span.End, span.Start + 1),
                Line        = line.Line + 1,
                Column      = line.Character + 1,
                Message     = diag.GetMessage(),
                Code        = diag.Id,
                Severity    = ConvertSeverity(diag.Severity),
                Fixes       = fixes,
            });
        }

        return spans;
    }

    /// <summary>
    /// Sets the project directory for cross-file diagnostics context.
    /// Also resolves NuGet package references for accurate diagnostics.
    /// </summary>
    public void SetProjectContext(string? projectDir)
    {
        if (string.Equals(_projectDir, projectDir, StringComparison.OrdinalIgnoreCase))
            return;
        _projectDir = projectDir;
        _projectCsFiles = null;
        _trackedFilePath = null; // force rebuild
        
        // Resolve NuGet package references
        _nugetResolver.InvalidateCache();
        _nugetRefs = _nugetResolver.Resolve(projectDir);
        _hasNuGetRefs = _nugetRefs.Count > 0;
        
        System.Diagnostics.Debug.WriteLine(
            $"[InlineDiag] Project context: {projectDir}, NuGet refs: {_nugetRefs.Count}, fallback mode: {!_hasNuGetRefs}");
    }

    private List<string> GetProjectCsFiles()
    {
        if (_projectCsFiles != null) return _projectCsFiles;
        _projectCsFiles = new List<string>();
        if (string.IsNullOrEmpty(_projectDir) || !Directory.Exists(_projectDir))
            return _projectCsFiles;
        try
        {
            foreach (var f in Directory.GetFiles(_projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                    continue;
                _projectCsFiles.Add(f);
            }
        }
        catch { }
        return _projectCsFiles;
    }

    // ── Workspace management ───────────────────────────────────────────────

    private Document SyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFilePath != filePath)
        {
            RebuildProject(filePath, sourceCode);
        }
        else
        {
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc != null)
            {
                var updated = doc.WithText(SourceText.From(sourceCode));
                if (!_workspace.TryApplyChanges(updated.Project.Solution))
                {
                    // Workspace desynchronized — rebuild from scratch
                    RebuildProject(filePath, sourceCode);
                }
            }
            else
            {
                // Document was lost — rebuild
                RebuildProject(filePath, sourceCode);
            }
        }
        return _workspace.CurrentSolution.GetDocument(_documentId!)!;
    }

    private void RebuildProject(string filePath, string sourceCode)
    {
        if (_projectId != null)
            _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(_projectId));

        var projectId  = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        // Combine default refs + NuGet package refs
        var allRefs = new List<MetadataReference>(_refs);
        if (_nugetRefs != null && _nugetRefs.Count > 0)
        {
            var existingPaths = new HashSet<string>(
                _refs.Select(r => r.Display ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var nugetRef in _nugetRefs)
            {
                if (nugetRef.Display != null && existingPaths.Add(nugetRef.Display))
                    allRefs.Add(nugetRef);
            }
        }

        var info = ProjectInfo.Create(
            projectId, VersionStamp.Create(),
            "InlineDiag", "InlineDiag", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: allRefs);

        var sol = _workspace.CurrentSolution.AddProject(info);
        // Use the full path as document name to avoid collisions when multiple
        // files share the same short filename (e.g. Program.cs in sub-projects).
        var docInfo = DocumentInfo.Create(
            documentId,
            filePath,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceCode), VersionStamp.Create())),
            filePath: filePath);

        sol = sol.AddDocument(docInfo);

        // Add other project .cs files as context
        var contextFiles = GetProjectCsFiles();
        foreach (var csFile in contextFiles)
        {
            if (string.Equals(csFile, filePath, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var auxDid = DocumentId.CreateNewId(projectId);
                var auxText = File.ReadAllText(csFile);
                sol = sol.AddDocument(DocumentInfo.Create(auxDid, csFile,
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(auxText), VersionStamp.Create())),
                    filePath: csFile));
            }
            catch { /* skip unreadable files */ }
        }

        _workspace.TryApplyChanges(sol);

        _projectId       = projectId;
        _documentId      = documentId;
        _trackedFilePath = filePath;
    }

    // ── Suppression ────────────────────────────────────────────────────────

    private bool ShouldSuppress(Diagnostic d)
    {
        // Always suppress noise codes
        if (_alwaysSuppressedCodes.Contains(d.Id))
            return true;
        
        // When NuGet refs are NOT loaded, suppress type-not-found errors
        // (they're almost certainly false positives from missing assemblies)
        if (!_hasNuGetRefs && _fallbackSuppressedCodes.Contains(d.Id))
            return true;
        
        return false;
    }

    private static DiagnosticSeverityKind ConvertSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity s)
        => s switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error   => DiagnosticSeverityKind.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => DiagnosticSeverityKind.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info    => DiagnosticSeverityKind.Info,
            _                                                  => DiagnosticSeverityKind.Hint,
        };

    // ── MEF ───────────────────────────────────────────────────────────────

    private static IEnumerable<Assembly> BuildMefAssemblies()
    {
        var set = new HashSet<Assembly>(MefHostServices.DefaultAssemblies);
        foreach (var name in new[]
        {
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Features",
            "Microsoft.CodeAnalysis.Workspaces.Common",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
        })
        { try { set.Add(Assembly.Load(name)); } catch { } }
        return set;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _workspace.Dispose();
        _quickFixService.Dispose();
    }
}

/// <summary>
/// Event args: diagnostics updated for a specific file.
/// </summary>
public sealed class InlineDiagnosticsUpdatedEventArgs : EventArgs
{
    public string               FilePath    { get; }
    public List<DiagnosticSpan> Diagnostics { get; }

    public InlineDiagnosticsUpdatedEventArgs(string filePath, List<DiagnosticSpan> diagnostics)
    {
        FilePath    = filePath;
        Diagnostics = diagnostics;
    }
}

