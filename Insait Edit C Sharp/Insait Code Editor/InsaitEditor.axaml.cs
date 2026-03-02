using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.InsaitCodeEditor;

// ═══════════════════════════════════════════════════════════════════════════
//  InsaitEditor — code-behind
//  UI визначений в InsaitEditor.axaml, тут тільки логіка:
//    • з'єднання з InsaitEditorSurface
//    • Roslyn autocompletion, signature help
//    • Roslyn diagnostics + quick fixes
//    • keyboard shortcuts для popup-навігації
// ═══════════════════════════════════════════════════════════════════════════

public partial class InsaitEditor : UserControl
{
    // ── AXAML controls (x:Name) ──────────────────────────────────────────
    private readonly Border    _readyBadge;
    private readonly ListBox   _completionList;
    private readonly Border    _completionPopup;
    private readonly ListBox   _quickFixList;
    private readonly Border    _quickFixPopup;
    private readonly TextBlock _signatureText;
    private readonly Border    _signaturePopup;
    private readonly TextBlock _tooltipText;
    private readonly Border    _tooltipPopup;
    private readonly ScrollBar _vScroll;
    private readonly ScrollBar _hScroll;

    // ── Surface (custom render, додається в code-behind) ─────────────────
    private readonly InsaitEditorSurface _surface;

    // ── Editor state ─────────────────────────────────────────────────────
    private string _currentFilePath = "untitled.cs";
    private bool   _isDirty;

    // ── Roslyn services ──────────────────────────────────────────────────
    private readonly InlineDiagnosticService _diagService      = new();
    private readonly RoslynCompletionEngine  _completionEngine = new();
    private readonly QuickFixService         _quickFixService  = new();
    private List<DiagnosticSpan>             _diagnosticSpans  = new();
    private CancellationTokenSource?         _completionCts;
    private CancellationTokenSource?         _signatureCts;
    private bool                             _suppressCompletion;

    // ── Public events ────────────────────────────────────────────────────
    public event EventHandler?                                 EditorReady;
    public event EventHandler?                                 ContentChanged;
    public event EventHandler<ContentChangedEventArgs>?        ContentChangedWithValue;
    public event EventHandler<CursorPositionChangedEventArgs>? CursorPositionChanged;
    public event EventHandler<NuGetInstallRequestedEventArgs>? NuGetInstallRequested;

    // ── Public properties ────────────────────────────────────────────────
    public bool   IsDirty        => _isDirty;
    public bool   CanUndo        => _surface.CanUndo;
    public bool   CanRedo        => _surface.CanRedo;
    public string CurrentContent => _surface.Text;
    public (int line, int col) CursorPosition => _surface.CursorPosition;
    public UndoRedoManager UndoRedoManager => _surface.UndoRedoMgr;
    public IReadOnlyList<DiagnosticSpan> Diagnostics => _diagnosticSpans;

    // ═══════════════════════════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════════════════════════
    public InsaitEditor()
    {
        AvaloniaXamlLoader.Load(this);

        // resolve AXAML elements
        _readyBadge     = this.FindControl<Border>("ReadyBadge")!;
        _completionList = this.FindControl<ListBox>("CompletionList")!;
        _completionPopup= this.FindControl<Border>("CompletionPopup")!;
        _quickFixList   = this.FindControl<ListBox>("QuickFixList")!;
        _quickFixPopup  = this.FindControl<Border>("QuickFixPopup")!;
        _signatureText  = this.FindControl<TextBlock>("SignatureText")!;
        _signaturePopup = this.FindControl<Border>("SignaturePopup")!;
        _tooltipText    = this.FindControl<TextBlock>("TooltipText")!;
        _tooltipPopup   = this.FindControl<Border>("TooltipPopup")!;
        _vScroll        = this.FindControl<ScrollBar>("VScroll")!;
        _hScroll        = this.FindControl<ScrollBar>("HScroll")!;

        // create surface and place into host
        _surface = new InsaitEditorSurface
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        var host = this.FindControl<Border>("SurfaceHost")!;
        host.Child = _surface;

        // wire surface events
        _surface.TextChanged       += OnSurfaceTextChanged;
        _surface.CursorMoved       += OnSurfaceCursorMoved;
        _surface.RequestCompletion += (_, _) => { if (!_suppressCompletion) _ = ShowCompletionAsync(); };
        _surface.RequestSignature  += (_, _) => _ = ShowSignatureHelpAsync();
        _surface.HoverDiagnostic   += OnHoverDiagnostic;
        _surface.HoverCleared      += (_, _) => HideTooltip();
        _surface.RequestQuickFix   += (_, d) => _ = ShowQuickFixAsync(d);

        // wire scrollbars
        _vScroll.Scroll += (_, e) => _surface.ScrollTop = (int)e.NewValue;
        _hScroll.Scroll += (_, e) => _surface.ScrollLeft = (int)e.NewValue;
        _surface.ScrollChanged += (_, _) =>
        {
            _vScroll.Value = _surface.ScrollTop;
            _hScroll.Value = _surface.ScrollLeft;
        };
        _surface.ViewportChanged += (_, _) =>
        {
            _vScroll.Maximum     = Math.Max(0, _surface.TotalLines - _surface.VisibleLines);
            _vScroll.LargeChange = Math.Max(1, _surface.VisibleLines);
            _hScroll.Maximum     = Math.Max(0, _surface.MaxLineWidth - _surface.ViewportWidth);
            _hScroll.LargeChange = Math.Max(10, _surface.ViewportWidth);
        };

        // wire completion list
        _completionList.SelectionChanged += OnCompletionSelected;
        _quickFixList.SelectionChanged   += OnQuickFixSelected;

        // diagnostics service
        _diagService.DiagnosticsUpdated += OnDiagnosticsUpdated;

        // show ready badge
        Dispatcher.UIThread.Post(() => _ = ShowReadyBadgeAsync(), DispatcherPriority.Loaded);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Public API (сумісний з AmberFluentEditor / AvaloniaEditor)
    // ═══════════════════════════════════════════════════════════════════════

    public void SetContent(string content, string language = "csharp")
    {
        _surface.SetText(content ?? string.Empty);
        _isDirty = false;
        HideAllPopups();
        ScheduleDiagnostics();
    }

    public void SetFilePath(string path)
    {
        _currentFilePath = path;
        _surface.FilePath = path;
    }

    public Task<string> GetContentAsync() => Task.FromResult(_surface.Text);
    public void MarkAsSaved()          => _isDirty = false;
    public void SetReadOnly(bool v)    => _surface.IsReadOnly = v;
    public void SetFontSize(int sz)    => _surface.FontSize = sz;
    public void FocusEditor()          => _surface.Focus();
    public void Undo()                 => _surface.Undo();
    public void Redo()                 => _surface.Redo();
    public void Find()                 { /* TODO */ }
    public void Replace()              { /* TODO */ }
    public void FormatDocument()       => _ = FormatAsync();
    public void SetLanguage(string _)  { }
    public void GoToLine(int line, int col = 1) => _surface.GoToLine(line, col);
    public void TriggerCompletion()    => _ = ShowCompletionAsync();
    public void LoadProjectReferences(string path) { /* TODO */ }

    /// <summary>
    /// Sets the project directory for full-project Roslyn context
    /// (cross-file namespace resolution, completion, etc.)
    /// </summary>
    public void SetProjectContext(string? projectDir)
    {
        _surface.SetProjectContext(projectDir);
        _completionEngine.SetProjectContext(projectDir);
        _diagService.SetProjectContext(projectDir);
    }

    public void ApplyExternalDiagnostics(IEnumerable<DiagnosticSpan> spans)
    {
        _diagnosticSpans = spans.ToList();
        _surface.SetDiagnostics(_diagnosticSpans);
    }

    public void ClearDiagnostics()
    {
        _diagnosticSpans.Clear();
        _surface.SetDiagnostics(_diagnosticSpans);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Format
    // ═══════════════════════════════════════════════════════════════════════
    private async Task FormatAsync()
    {
        try
        {
            var formatted = await _completionEngine.FormatDocumentAsync(
                _currentFilePath, _surface.Text);
            if (formatted != null)
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _surface.SetText(formatted, preserveCursor: true));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Format: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Diagnostics
    // ═══════════════════════════════════════════════════════════════════════
    private void ScheduleDiagnostics()
    {
        // Only run Roslyn diagnostics for pure C# source files (not .axaml.cs code-behind)
        if (!_currentFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
             _currentFilePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase) ||
             _currentFilePath.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
            return;
        _diagService.ScheduleAnalysis(_currentFilePath, _surface.Text);
    }

    private void OnDiagnosticsUpdated(object? sender, InlineDiagnosticsUpdatedEventArgs e)
    {
        if (!string.Equals(e.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            return;
        Dispatcher.UIThread.Post(() =>
        {
            _diagnosticSpans = e.Diagnostics;
            _surface.SetDiagnostics(_diagnosticSpans);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Surface events
    // ═══════════════════════════════════════════════════════════════════════
    private void OnSurfaceTextChanged(object? sender, EventArgs e)
    {
        _isDirty = true;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        ContentChangedWithValue?.Invoke(this, new ContentChangedEventArgs(_surface.Text));
        ScheduleDiagnostics();
        HideCompletion();
        HideQuickFix();
    }

    private void OnSurfaceCursorMoved(object? sender, (int line, int col) pos)
    {
        CursorPositionChanged?.Invoke(this,
            new CursorPositionChangedEventArgs(pos.line, pos.col));
        HideTooltip();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Hover diagnostics
    // ═══════════════════════════════════════════════════════════════════════
    private void OnHoverDiagnostic(object? sender, DiagnosticSpan diag)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var sev = diag.Severity switch
            {
                DiagnosticSeverityKind.Error   => "❌ Error",
                DiagnosticSeverityKind.Warning => "⚠ Warning",
                DiagnosticSeverityKind.Info    => "ℹ Info",
                _                              => "💡 Hint",
            };
            _tooltipText.Text = $"{sev} {diag.Code}: {diag.Message}";
            var r = _surface.GetCursorRectForPos(diag.Line - 1, diag.Column - 1);
            Canvas.SetLeft(_tooltipPopup, r.X + 60);
            Canvas.SetTop(_tooltipPopup, r.Bottom + 4);
            _tooltipPopup.IsVisible = true;
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Quick Fix
    // ═══════════════════════════════════════════════════════════════════════
    private async Task ShowQuickFixAsync(DiagnosticSpan diag)
    {
        try
        {
            var fixes = await _quickFixService.GetFixesAsync(
                _currentFilePath, _surface.Text,
                diag.StartOffset, diag.EndOffset,
                diag.Code, diag.Message);
            if (!fixes.Any()) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _quickFixList.Items.Clear();
                foreach (var f in fixes.Take(10))
                    _quickFixList.Items.Add(new InsaitQuickFixItem(f, diag));
                _quickFixList.SelectedIndex = 0;
                var r = _surface.GetCursorRectForPos(diag.Line - 1, diag.Column - 1);
                Canvas.SetLeft(_quickFixPopup, r.X + 20);
                Canvas.SetTop(_quickFixPopup, r.Bottom + 4);
                _quickFixPopup.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] QuickFix: {ex.Message}");
        }
    }

    private void OnQuickFixSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_quickFixList.SelectedItem is InsaitQuickFixItem item)
        {
            ApplyQuickFix(item.Suggestion, item.SourceDiagnostic);
            HideQuickFix();
        }
    }

    private void ApplyQuickFix(QuickFixSuggestion fix, DiagnosticSpan diag)
    {
        switch (fix.Kind)
        {
            case QuickFixKind.AddUsing when !string.IsNullOrEmpty(fix.NamespaceName):
                _surface.InsertTextAt(0, $"using {fix.NamespaceName};\n");
                break;
            case QuickFixKind.InsertCode when !string.IsNullOrEmpty(fix.InsertText):
                _surface.InsertTextAt(fix.InsertOffset > 0 ? fix.InsertOffset : diag.EndOffset, fix.InsertText);
                break;
            case QuickFixKind.RemoveCode:
                _surface.RemoveTextRange(diag.StartOffset, diag.EndOffset);
                break;
            case QuickFixKind.InstallNuGet when !string.IsNullOrEmpty(fix.NuGetPackage):
                NuGetInstallRequested?.Invoke(this, new NuGetInstallRequestedEventArgs(fix.NuGetPackage));
                break;
            default:
                if (!string.IsNullOrEmpty(fix.InsertText))
                    _surface.InsertTextAt(diag.StartOffset, fix.InsertText);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Autocompletion
    // ═══════════════════════════════════════════════════════════════════════
    private async Task ShowCompletionAsync()
    {
        // Roslyn completion only works for C# files
        if (!_currentFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            _currentFilePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase))
            return;

        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var ct = _completionCts.Token;
        try
        {
            var (line, col) = _surface.CursorPosition;
            var items = await _completionEngine.GetCompletionsAsync(
                _currentFilePath, _surface.Text, line, col, ct);
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!items.Any()) { HideCompletion(); return; }
                _completionList.Items.Clear();
                foreach (var item in items.Take(60))
                    _completionList.Items.Add(new InsaitCompletionItem(item));
                _completionList.SelectedIndex = 0;

                var r = _surface.GetCursorRect();
                Canvas.SetLeft(_completionPopup, r.X + 60);
                Canvas.SetTop(_completionPopup, r.Bottom + 2);
                _completionPopup.IsVisible = true;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Completion: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Signature Help
    // ═══════════════════════════════════════════════════════════════════════
    private async Task ShowSignatureHelpAsync()
    {
        // Roslyn signature help only works for C# files
        if (!_currentFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            _currentFilePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase))
            return;

        _signatureCts?.Cancel();
        _signatureCts = new CancellationTokenSource();
        var ct = _signatureCts.Token;
        try
        {
            int offset = _surface.GetCursorOffset();
            var info = await _completionEngine.GetSignatureHelpAsync(
                _currentFilePath, _surface.Text, offset, ct);
            if (ct.IsCancellationRequested || info == null) { HideSignature(); return; }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var sig = info.Signatures.ElementAtOrDefault(info.ActiveSignature);
                if (sig == null) { HideSignature(); return; }

                var sb = new StringBuilder();
                sb.Append(sig.Label);
                if (sig.Parameters.Count > 0 && info.ActiveParameter < sig.Parameters.Count)
                {
                    sb.Append("\n▸ ");
                    sb.Append(sig.Parameters[info.ActiveParameter].Label);
                    if (!string.IsNullOrEmpty(sig.Parameters[info.ActiveParameter].Documentation))
                    {
                        sb.Append(" — ");
                        sb.Append(sig.Parameters[info.ActiveParameter].Documentation);
                    }
                }
                _signatureText.Text = sb.ToString();
                var r = _surface.GetCursorRect();
                Canvas.SetLeft(_signaturePopup, r.X + 60);
                Canvas.SetTop(_signaturePopup, r.Top - 40);
                _signaturePopup.IsVisible = true;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Signature: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Popup helpers
    // ═══════════════════════════════════════════════════════════════════════
    private void HideCompletion() => _completionPopup.IsVisible = false;
    private void HideQuickFix()   => _quickFixPopup.IsVisible   = false;
    private void HideSignature()  => _signaturePopup.IsVisible  = false;
    private void HideTooltip()    => _tooltipPopup.IsVisible    = false;
    private void HideAllPopups()  { HideCompletion(); HideQuickFix(); HideSignature(); HideTooltip(); }

    /// <summary>
    /// Commits the selected completion item. If Roslyn provides a
    /// CompletionChange (including snippet expansions), we apply it;
    /// otherwise we fall back to simple text insertion.
    /// </summary>
    private async Task CommitCompletionAsync(InsaitCompletionItem ci)
    {
        HideCompletion();
        _suppressCompletion = true;
        try
        {
            // Ask Roslyn for the real text change (handles snippets, overrides, etc.)
            var change = await _completionEngine.GetCompletionChangeAsync(
                ci.Source, _currentFilePath, _surface.Text);
            if (change != null)
            {
                _surface.ApplyCompletionChange(
                    change.SpanStart, change.SpanLength,
                    change.NewText, change.IsSnippet);
            }
            else
            {
                // Fallback: simple prefix-based insertion
                _surface.InsertCompletion(ci.InsertText);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] CommitCompletion (change): {ex.Message}");
            _surface.InsertCompletion(ci.InsertText);
        }
        finally
        {
            _suppressCompletion = false;
        }
    }

    private void OnCompletionSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Do NOT auto-commit on selection change — the user navigates with
        // Up/Down and commits explicitly with Tab/Enter.
    }

    private async Task ShowReadyBadgeAsync()
    {
        _readyBadge.IsVisible = true;
        EditorReady?.Invoke(this, EventArgs.Empty);
        await Task.Delay(3000);
        _readyBadge.IsVisible = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Keyboard: popup navigation + shortcuts
    // ═══════════════════════════════════════════════════════════════════════
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // ── Completion ───────────────────────────────────────────────────
        if (_completionPopup.IsVisible)
        {
            switch (e.Key)
            {
                case Key.Escape: HideCompletion(); e.Handled = true; return;
                case Key.Tab or Key.Return:
                    if (_completionList.SelectedItem is InsaitCompletionItem ci)
                    { _ = CommitCompletionAsync(ci); e.Handled = true; return; }
                    break;
                case Key.Down:
                    _completionList.SelectedIndex = Math.Min(_completionList.SelectedIndex + 1, _completionList.Items.Count - 1);
                    e.Handled = true; return;
                case Key.Up:
                    _completionList.SelectedIndex = Math.Max(_completionList.SelectedIndex - 1, 0);
                    e.Handled = true; return;
            }
        }

        // ── Quick fix ────────────────────────────────────────────────────
        if (_quickFixPopup.IsVisible)
        {
            switch (e.Key)
            {
                case Key.Escape: HideQuickFix(); e.Handled = true; return;
                case Key.Return:
                    if (_quickFixList.SelectedItem is InsaitQuickFixItem qi)
                    { ApplyQuickFix(qi.Suggestion, qi.SourceDiagnostic); HideQuickFix(); e.Handled = true; return; }
                    break;
                case Key.Down:
                    _quickFixList.SelectedIndex = Math.Min(_quickFixList.SelectedIndex + 1, _quickFixList.Items.Count - 1);
                    e.Handled = true; return;
                case Key.Up:
                    _quickFixList.SelectedIndex = Math.Max(_quickFixList.SelectedIndex - 1, 0);
                    e.Handled = true; return;
            }
        }

        // Alt+Enter / Ctrl+. → quick fix at cursor
        if ((e.Key == Key.Return && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) ||
            (e.Key == Key.OemPeriod && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            var diag = _surface.GetDiagnosticAtCursor();
            if (diag != null) { _ = ShowQuickFixAsync(diag); e.Handled = true; return; }
        }

        // F9 → toggle breakpoint at current line
        if (e.Key == Key.F9)
        {
            var (bpLine, _) = _surface.CursorPosition;
            var bpFile = _currentFilePath;
            if (!string.IsNullOrEmpty(bpFile))
                BreakpointService.Toggle(bpFile, bpLine);
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+I → format
        if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Control)
                           && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { FormatDocument(); e.Handled = true; return; }

        base.OnKeyDown(e);
    }
}

/// <summary>Fired when user selects "Install NuGet package" in quick fix popup.</summary>
public sealed class NuGetInstallRequestedEventArgs : EventArgs
{
    public string PackageName { get; }
    public NuGetInstallRequestedEventArgs(string packageName) => PackageName = packageName;
}

