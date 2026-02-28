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
    private CancellationTokenSource?       _cts;

    // ── Avalonia-noise suppression (same logic as CodeAnalysisService) ─────
    private static readonly HashSet<string> _suppressedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS0103", "CS0246", "CS0234", "CS1061", "CS0117",
        "CS0121", "CS7036", "CS0012",
    };
    private static readonly string[] _suppressedPatterns =
    {
        "InitializeComponent","Avalonia","AvaloniaProperty","StyledProperty",
        "DirectProperty","FindControl","GetObservable","axaml","AXAML",
        "IStyleable","StyledElement","TemplatedControl","UserControl","Window",
        "Panel","Grid","StackPanel","DockPanel","Canvas","Border","Button",
        "TextBlock","TextBox","ListBox","ComboBox","CheckBox","RadioButton",
        "TreeView","TabControl","ScrollViewer","ItemsControl","ContentControl",
        "MenuItem","Menu","ContextMenu","Image","Popup","ToolTip","Expander",
        "Slider","ProgressBar","DataGrid",
    };

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

    // ── Workspace management ───────────────────────────────────────────────

    private Document SyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFilePath != filePath)
            RebuildProject(filePath, sourceCode);
        else
        {
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc != null)
            {
                var updated = doc.WithText(SourceText.From(sourceCode));
                _workspace.TryApplyChanges(updated.Project.Solution);
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

        var info = ProjectInfo.Create(
            projectId, VersionStamp.Create(),
            "InlineDiag", "InlineDiag", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: _refs);

        var sol = _workspace.CurrentSolution.AddProject(info);
        var docInfo = DocumentInfo.Create(
            documentId,
            Path.GetFileName(filePath),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceCode), VersionStamp.Create())),
            filePath: filePath);

        sol = sol.AddDocument(docInfo);
        _workspace.TryApplyChanges(sol);

        _projectId       = projectId;
        _documentId      = documentId;
        _trackedFilePath = filePath;
    }

    // ── Suppression (matches CodeAnalysisService logic) ───────────────────

    private static bool ShouldSuppress(Diagnostic d)
    {
        var filePath = d.Location.SourceTree?.FilePath;
        if (!string.IsNullOrEmpty(filePath) &&
            filePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase) &&
            _suppressedCodes.Contains(d.Id))
            return true;

        if (!_suppressedCodes.Contains(d.Id)) return false;

        var msg = d.GetMessage();
        foreach (var p in _suppressedPatterns)
            if (msg.Contains(p, StringComparison.OrdinalIgnoreCase))
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

