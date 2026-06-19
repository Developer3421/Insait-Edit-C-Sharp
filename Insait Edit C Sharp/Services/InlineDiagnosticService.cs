using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using Analyzer;
using Insait_Edit_C_Sharp.Controls;
using System.Diagnostics;

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
    private CancellationTokenSource?       _cts;

    // ── project context ──────────────────────────────────────────────────
    private string? _projectDir;
    private RoslynProjectBuild? _currentBuild;

    /// <summary>Whether NuGet references were successfully loaded for the current project.</summary>
    private bool _hasNuGetRefs;

    // Secondary full-project analysis pass, runs on a slower cadence
    // to catch cross-file diagnostics the incremental workspace might miss.
    private readonly FileSyntaxCheckerService _fileSyntaxChecker = new();
    private CancellationTokenSource? _fullCheckCts;
    private readonly object _fullCheckLock = new();

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

        Task.Delay(delayMs, ct).ContinueWith(_ =>
        {
            if (ct.IsCancellationRequested) return;
            Task.Run(() => RunAnalysisAsync(filePath, sourceCode, ct), ct);
        }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>Immediately run diagnostics (no delay).</summary>
    public async Task AnalyzeNowAsync(string filePath, string sourceCode, CancellationToken ct = default)
    {
        await RunAnalysisAsync(filePath, sourceCode, ct);
    }

    private async Task RunAnalysisAsync(string filePath, string sourceCode, CancellationToken ct)
    {
        var isCsharp = filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        List<DiagnosticSpan> spans;

        if (isCsharp)
        {
            spans = await AnalyzeCSharpAsync(filePath, sourceCode, ct);

            // Schedule a slower full-project check to catch cross-file diagnostics.
            // This runs at most once every few seconds, not on every keystroke.
            ScheduleFullCheck(filePath, sourceCode);
        }
        else
        {
            spans = new List<DiagnosticSpan>(); // F#/AXAML/etc. — no inline for now
        }

        DiagnosticsUpdated?.Invoke(this, new InlineDiagnosticsUpdatedEventArgs(filePath, spans));
    }

    /// <summary>
    /// Schedules the heavy FileSyntaxChecker pass on a slow cadence (4s debounce).
    /// This catches cross-file diagnostics that the incremental workspace might miss,
    /// without blocking the fast inline path.
    /// Fires diagnostics from the full check directly (no merge) — the full check
    /// has complete project context and produces the authoritative result.
    /// </summary>
    private void ScheduleFullCheck(string filePath, string sourceCode)
    {
        lock (_fullCheckLock)
        {
            _fullCheckCts?.Cancel();
            _fullCheckCts = new CancellationTokenSource();
            var ct = _fullCheckCts.Token;

            Task.Delay(4000, ct).ContinueWith(async _ =>
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var syntaxResult = await _fileSyntaxChecker.CheckTextAsync(
                        filePath, sourceCode, _projectDir, ct).ConfigureAwait(false);

                    if (syntaxResult.Error is not null || ct.IsCancellationRequested)
                        return;

                    var resultSpans = new List<DiagnosticSpan>(syntaxResult.Diagnostics.Count);
                    foreach (var fd in syntaxResult.Diagnostics)
                    {
                        if (ct.IsCancellationRequested) return;
                        resultSpans.Add(new DiagnosticSpan
                        {
                            StartOffset = fd.StartOffset,
                            EndOffset   = fd.EndOffset,
                            Line        = fd.Line,
                            Column      = fd.Column,
                            Message     = fd.Message,
                            Code        = fd.Code,
                            Severity    = fd.Severity == "error"
                                ? DiagnosticSeverityKind.Error
                                : fd.Severity == "warning"
                                    ? DiagnosticSeverityKind.Warning
                                    : DiagnosticSeverityKind.Info,
                        });
                    }

                    DiagnosticsUpdated?.Invoke(this,
                        new InlineDiagnosticsUpdatedEventArgs(filePath, resultSpans));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InlineDiag] Full pass: {ex.Message}");
                }
            }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }
    }

    private async Task<List<DiagnosticSpan>> AnalyzeCSharpAsync(
        string filePath, string sourceCode, CancellationToken ct)
    {
        var spans = new List<DiagnosticSpan>();

        // Sync the document into the workspace
        Document document;
        try   { document = SyncDocument(filePath, sourceCode); }
        catch { return spans; }

        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation == null) return spans;

        var build = _currentBuild;
        var allDiagnostics = (await CollectDiagnosticsAsync(document.Project, compilation, ct).ConfigureAwait(false))
            .Where(d => build?.BelongsToFile(d, filePath) ?? false)
            .ToList();

        foreach (var diag in allDiagnostics)
        {
            ct.ThrowIfCancellationRequested();

            // Resolve effective severity (null → suppress)
            var kind = ResolveKind(diag);
            if (kind is null) continue;

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
                    ct).ConfigureAwait(false);
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
                Severity    = kind.Value,   // resolved severity from matrix
                Fixes       = fixes,
                // Mark only those symbols that Roslyn can resolve from a known
                // NuGet package or an existing namespace (using directive).
                // Unknown symbols without any resolvable fix do NOT get the box.
                HasResolvablePackageFix = fixes.Any(f =>
                    f.Kind == QuickFixKind.InstallNuGet ||
                    f.Kind == QuickFixKind.AddUsing),
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
        _trackedFilePath = null; // force rebuild
        _currentBuild = null;
        _quickFixService.SetProjectContext(projectDir);

        System.Diagnostics.Debug.WriteLine(
            $"[InlineDiag] Project context: {projectDir}, fallback mode: {!_hasNuGetRefs}");
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

        var build = RoslynProjectFactory.CreateBuild(_projectDir, _refs, filePath, sourceCode);
        var sol = _workspace.CurrentSolution.AddProject(build.ProjectInfo);

        // Add .editorconfig files as analyzer config documents
        foreach (var ecPath in build.EditorConfigPaths)
        {
            try
            {
                var sourceText = SourceText.From(File.ReadAllText(ecPath), Encoding.UTF8);
                sol = sol.AddAnalyzerConfigDocument(
                    DocumentId.CreateNewId(build.ProjectInfo.Id),
                    name: ".editorconfig",
                    text: sourceText,
                    filePath: ecPath);
            }
            catch { }
        }

        _workspace.TryApplyChanges(sol);

        _currentBuild    = build;
        _hasNuGetRefs    = build.HasProjectMetadataReferences;
        _projectId       = build.ProjectInfo.Id;
        _documentId      = build.ActiveDocumentId;
        _trackedFilePath = filePath;
    }

    // ── Suppression / severity resolution ─────────────────────────────────
    // All logic is delegated to DiagnosticSeverityMatrix so the two services
    // stay in sync automatically.

    private DiagnosticSeverityKind? ResolveKind(Diagnostic d)
        => DiagnosticSeverityMatrix.ResolveInline(d.Id, d.Severity, _hasNuGetRefs);


    private static async Task<List<Diagnostic>> CollectDiagnosticsAsync(Project project, Compilation compilation, CancellationToken ct)
    {
        // GetDiagnostics is synchronous CPU-bound work — run on thread pool
        var diagnostics = await Task.Run(() => compilation.GetDiagnostics(ct).ToList(), ct).ConfigureAwait(false);

        // Merge project-level NuGet analyzers with the built-in IDE analyzers
        // so we get IDE0001…IDE1006 diagnostics (Info/Hint level) in addition
        // to the standard CS compiler diagnostics.
        var projectAnalyzers = project.AnalyzerReferences
            .SelectMany(r => SafeGetAnalyzers(r, project.Language));

        var analyzers = BuiltInAnalyzerProvider.Merge(projectAnalyzers);

        if (analyzers.Length == 0)
            return diagnostics;

        var additionalTexts = GetProjectAdditionalTexts(project);
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(additionalTexts),
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);

        try
        {
            var analyzerDiagnostics = await compilation
                .WithAnalyzers(analyzers, options)
                .GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
            diagnostics.AddRange(analyzerDiagnostics);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InlineDiag] Analyzer run failed: {ex.Message}");
        }

        // Deduplicate (same code + location + message)
        return diagnostics
            .GroupBy(static d => new
            {
                d.Id,
                FilePath = d.Location.SourceTree?.FilePath ?? d.Location.GetLineSpan().Path,
                d.Location.SourceSpan.Start,
                d.Location.SourceSpan.Length,
                Message = d.GetMessage(),
                d.Severity,
            })
            .Select(static g => g.First())
            .ToList();
    }

    /// <summary>
    /// Collect additional texts from the project's additional documents so
    /// analyzers can read them (fixes false positives when analyzers depend
    /// on <AdditionalFile> contents like .editorconfig or custom config).
    /// </summary>
    private static ImmutableArray<AdditionalText> GetProjectAdditionalTexts(Project project)
    {
        try
        {
            var docs = project.AdditionalDocuments?.ToList();
            if (docs is null || docs.Count == 0)
                return ImmutableArray<AdditionalText>.Empty;

            var texts = new List<AdditionalText>(docs.Count);
            foreach (var doc in docs)
            {
                try
                {
                    var sourceText = doc.GetTextAsync(CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (sourceText is not null)
                        texts.Add(new CustomAdditionalText(doc.FilePath ?? doc.Name, sourceText));
                }
                catch { }
            }

            return texts.ToImmutableArray();
        }
        catch
        {
            return ImmutableArray<AdditionalText>.Empty;
        }
    }

    /// <summary>
    /// Minimal <see cref="AdditionalText"/> implementation wrapping a path and source text.
    /// </summary>
    private sealed class CustomAdditionalText : AdditionalText
    {
        private readonly string _path;
        private readonly SourceText _text;

        public CustomAdditionalText(string path, SourceText text)
        {
            _path = path;
            _text = text;
        }

        public override string Path => _path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
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

