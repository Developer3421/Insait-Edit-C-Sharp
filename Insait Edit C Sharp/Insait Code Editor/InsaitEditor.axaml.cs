using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.InsaitCodeEditor;

// ═══════════════════════════════════════════════════════════════════════════
//  InsaitEditor — code-behind
//  Completion, QuickFix, GoTo, Rename, Hover → separate independent windows
//  Context menu on right-click
//  All completion via real Roslyn factory (RoslynCompletionEngine)
// ═══════════════════════════════════════════════════════════════════════════

public partial class InsaitEditor : UserControl
{
    // ── Minimal AXAML overlays ───────────────────────────────────────────
    private readonly Border    _readyBadge;
    private readonly TextBlock _signatureText;
    private readonly Border    _signaturePopup;
    private readonly TextBlock _tooltipText;
    private readonly Border    _tooltipPopup;
    private readonly ScrollBar _vScroll;
    private readonly ScrollBar _hScroll;

    // ── Surface ──────────────────────────────────────────────────────────
    private readonly InsaitEditorSurface _surface;

    // ── Independent windows (created on demand, reused) ──────────────────
    private RoslynCompletionWindow? _completionWin;
    private RoslynQuickFixWindow?   _quickFixWin;

    // ── Editor state ─────────────────────────────────────────────────────
    private string _currentFilePath = "untitled.cs";
    private bool   _isDirty;

    // ── Roslyn services ──────────────────────────────────────────────────
    private readonly InlineDiagnosticService _diagService      = new();
    private readonly RoslynCompletionEngine  _completionEngine = new();
    private readonly AxamlCompletionEngine   _axamlEngine      = new();
    private readonly QuickFixService         _quickFixService  = new();
    private readonly CSharpCompletionService _csharpService    = new();
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
    public event EventHandler<GoToDefinitionRequestedEventArgs>? GoToDefinitionRequested;
    public event EventHandler<RenameCompletedEventArgs>?       RenameCompleted;

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

        _readyBadge     = this.FindControl<Border>("ReadyBadge")!;
        _signatureText  = this.FindControl<TextBlock>("SignatureText")!;
        _signaturePopup = this.FindControl<Border>("SignaturePopup")!;
        _tooltipText    = this.FindControl<TextBlock>("TooltipText")!;
        _tooltipPopup   = this.FindControl<Border>("TooltipPopup")!;
        _vScroll        = this.FindControl<ScrollBar>("VScroll")!;
        _hScroll        = this.FindControl<ScrollBar>("HScroll")!;

        _surface = new InsaitEditorSurface
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        this.FindControl<Border>("SurfaceHost")!.Child = _surface;

        // ── Wire surface events ─────────────────────────────────────────
        _surface.TextChanged       += OnSurfaceTextChanged;
        _surface.CursorMoved       += OnSurfaceCursorMoved;
        _surface.RequestCompletion += (_, _) => { if (!_suppressCompletion) _ = ShowCompletionAsync(); };
        _surface.RequestSignature  += (_, _) => _ = ShowSignatureHelpAsync();
        _surface.HoverDiagnostic   += OnHoverDiagnostic;
        _surface.HoverCleared      += (_, _) => HideTooltip();
        _surface.RequestQuickFix   += (_, d) => _ = ShowQuickFixAsync(d);
        _surface.CtrlClickGoToDefinition += (_, _) => _ = GoToDefinitionAsync();
        _surface.CompletionDismissChar   += (_, _) => HideCompletionWindow();

        // ── Scrollbars ──────────────────────────────────────────────────
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

        // ── Right-click context menu ────────────────────────────────────
        BuildContextMenu();

        // ── Diagnostics ─────────────────────────────────────────────────
        _diagService.DiagnosticsUpdated += OnDiagnosticsUpdated;

        Dispatcher.UIThread.Post(() => _ = ShowReadyBadgeAsync(), DispatcherPriority.Loaded);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Context Menu (right-click)
    // ═══════════════════════════════════════════════════════════════════════
    private void BuildContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.Parse("#FF201A2E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF6C3FAA")),
            BorderThickness = new Thickness(1),
        };

        var miGoTo     = new MenuItem { Header = "📍  Go to Definition        F12" };
        var miRefs     = new MenuItem { Header = "🔎  Find References      Shift+F12" };
        var miRename   = new MenuItem { Header = "✏  Rename Symbol             F2" };
        var miHover    = new MenuItem { Header = "ℹ  Quick Info         Ctrl+Shift+H" };
        var sep1       = new Separator();
        var miComplete = new MenuItem { Header = "✦  Trigger IntelliSense   Ctrl+Space" };
        var miQuickFix = new MenuItem { Header = "💡  Quick Fix             Alt+Enter" };
        var miFormat   = new MenuItem { Header = "🔧  Format Document    Ctrl+Shift+I" };
        var sep2       = new Separator();
        var miCut      = new MenuItem { Header = "✂  Cut                     Ctrl+X" };
        var miCopy     = new MenuItem { Header = "📋  Copy                    Ctrl+C" };
        var miPaste    = new MenuItem { Header = "📌  Paste                   Ctrl+V" };

        miGoTo.Click     += (_, _) => _ = GoToDefinitionAsync();
        miRefs.Click     += (_, _) => _ = FindReferencesAsync();
        miRename.Click   += (_, _) => _ = RenameSymbolAsync();
        miHover.Click    += (_, _) => _ = ShowHoverInfoAsync();
        miComplete.Click += (_, _) => _ = ShowCompletionAsync();
        miQuickFix.Click += (_, _) =>
        {
            var d = _surface.GetDiagnosticAtCursor();
            if (d != null) _ = ShowQuickFixAsync(d);
        };
        miFormat.Click   += (_, _) => FormatDocument();
        miCut.Click      += (_, _) => _surface.DoCut();
        miCopy.Click     += (_, _) => _surface.DoCopy();
        miPaste.Click    += (_, _) => _surface.DoPaste();

        foreach (var mi in new object[] { miGoTo, miRefs, miRename, miHover, sep1,
                                          miComplete, miQuickFix, miFormat, sep2,
                                          miCut, miCopy, miPaste })
            menu.Items.Add(mi);

        _surface.ContextMenu = menu;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Public API
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
    public void MarkAsSaved()           => _isDirty = false;
    public void SetReadOnly(bool v)     => _surface.IsReadOnly = v;
    public void SetFontSize(int sz)     => _surface.FontSize = sz;
    public void FocusEditor()           => _surface.Focus();
    public void Undo()                  => _surface.Undo();
    public void Redo()                  => _surface.Redo();
    public void Find()                  { /* TODO */ }
    public void Replace()               { /* TODO */ }
    public void FormatDocument()        => _ = FormatAsync();
    public void SetLanguage(string _)   { }
    public void GoToLine(int line, int col = 1) => _surface.GoToLine(line, col);
    public void TriggerCompletion()     => _ = ShowCompletionAsync();
    public void LoadProjectReferences(string path) { /* TODO */ }

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
        HideQuickFixWindow();
        // Don't kill completion — let it live-update via RequestCompletion
    }

    private void OnSurfaceCursorMoved(object? sender, (int line, int col) pos)
    {
        CursorPositionChanged?.Invoke(this,
            new CursorPositionChangedEventArgs(pos.line, pos.col));
        HideTooltip();

        // Close completion if cursor moved to a different line
        // (typing on the same line keeps it open — RequestCompletion updates it)
        if (_completionWin != null && _completionWin.IsVisible)
        {
            // If no completion is in progress (cursor moved by arrow/click, not typing),
            // close the window. The surface fires CursorMoved AFTER TextChanged+RequestCompletion,
            // so we only close on non-typing moves.
        }
    }

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
    //  Completion — independent window via Roslyn factory, live-update
    // ═══════════════════════════════════════════════════════════════════════
    private int _completionTriggerCol; // column where completion was first triggered

    private async Task ShowCompletionAsync()
    {
        bool isCSharp = IsCSharpFile();
        bool isAxaml  = IsAxamlFile();
        if (!isCSharp && !isAxaml) return;

        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var ct = _completionCts.Token;
        try
        {
            var (line, col) = _surface.CursorPosition;

            // Extract the current typing prefix (word being typed)
            var currentLine = _surface.GetLineText(line - 1);
            int wordStart = Math.Min(col - 1, currentLine.Length);
            if (isAxaml)
            {
                // For AXAML: include . and : as part of the prefix word
                while (wordStart > 0 && (char.IsLetterOrDigit(currentLine[wordStart - 1])
                    || currentLine[wordStart - 1] == '_'
                    || currentLine[wordStart - 1] == '.'
                    || currentLine[wordStart - 1] == ':'))
                    wordStart--;
            }
            else
            {
                while (wordStart > 0 && (char.IsLetterOrDigit(currentLine[wordStart - 1]) || currentLine[wordStart - 1] == '_'))
                    wordStart--;
            }
            var typingPrefix = currentLine[wordStart..Math.Min(col - 1, currentLine.Length)];

            // Fetch completions from the appropriate engine
            IReadOnlyList<RoslynCompletionItem> freshItems;
            if (isAxaml)
                freshItems = await _axamlEngine.GetCompletionsAsync(
                    _currentFilePath, _surface.Text, line, col, ct);
            else
            {
                var roslynItems = await _completionEngine.GetCompletionsAsync(
                    _currentFilePath, _surface.Text, line, col, ct);

                // Merge live template items for C# files
                if (!string.IsNullOrEmpty(typingPrefix))
                {
                    var templateItems = RoslynLiveTemplateService.ToCompletionItems(typingPrefix);
                    if (templateItems.Count > 0)
                    {
                        var merged = new List<RoslynCompletionItem>(roslynItems);
                        merged.AddRange(templateItems);
                        freshItems = merged;
                    }
                    else
                    {
                        freshItems = roslynItems;
                    }
                }
                else
                {
                    freshItems = roslynItems;
                }
            }
            if (ct.IsCancellationRequested) return;

            // If the window is already open, update items and refilter
            if (_completionWin != null && _completionWin.IsVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (freshItems.Any())
                    {
                        _completionWin.SetItems(freshItems, typingPrefix);
                        // Reposition
                        var r = _surface.GetCursorRect();
                        var pos = _surface.PointToScreen(new Point(r.X, r.Bottom + 4));
                        _completionWin.Position = new PixelPoint(pos.X, pos.Y);

                        if (!_completionWin.HasItems)
                            HideCompletionWindow();
                    }
                    else
                    {
                        HideCompletionWindow();
                    }
                });
                return;
            }

            if (!freshItems.Any()) { HideCompletionWindow(); return; }

            _completionTriggerCol = wordStart + 1;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var parentWin = TopLevel.GetTopLevel(this) as Window;
                if (parentWin == null) return;

                var r = _surface.GetCursorRect();
                var surfacePos = _surface.PointToScreen(new Point(r.X, r.Bottom + 4));

                _completionWin = new RoslynCompletionWindow();
                _completionWin.ItemCommitted += OnCompletionItemCommitted;
                _completionWin.CloseRequested += (_, _) => HideCompletionWindow();
                _completionWin.Closed += (_, _) => _completionWin = null;

                _completionWin.SetItems(freshItems, typingPrefix);
                _completionWin.Position = new PixelPoint(surfacePos.X, surfacePos.Y);
                _completionWin.Show(parentWin);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Completion: {ex.Message}");
        }
    }

    private async void OnCompletionItemCommitted(object? sender, RoslynCompletionItem item)
    {
        HideCompletionWindow();
        _suppressCompletion = true;
        try
        {
            // Check if this is a live template item (from RoslynLiveTemplateService)
            if (item.Kind == "Snippet" && item.RoslynItem == null)
            {
                // Find the template by shortcut
                var template = RoslynLiveTemplateService.FindByShortcut(item.Label);
                if (template != null)
                {
                    // Calculate the span to replace (the typed trigger word)
                    var (line, col) = _surface.CursorPosition;
                    var currentLine = _surface.GetLineText(line - 1);
                    int wordEnd = Math.Min(col - 1, currentLine.Length);
                    int wordStart = wordEnd;
                    while (wordStart > 0 && (char.IsLetterOrDigit(currentLine[wordStart - 1]) || currentLine[wordStart - 1] == '_'))
                        wordStart--;

                    // Convert to absolute offset
                    int lineOffset = 0;
                    for (int i = 0; i < line - 1 && i < _surface.TotalLines; i++)
                        lineOffset += _surface.GetLineText(i).Length + 1;

                    int spanStart = lineOffset + wordStart;
                    int spanLength = wordEnd - wordStart;

                    _surface.StartLiveTemplate(spanStart, spanLength, template.Body);
                    return;
                }
            }

            // AXAML items don't have RoslynItem — just insert directly
            if (IsAxamlFile() || item.RoslynItem == null)
            {
                _surface.InsertCompletion(item.InsertText);
            }
            else
            {
                var change = await _completionEngine.GetCompletionChangeAsync(
                    item, _currentFilePath, _surface.Text);
                if (change != null)
                    _surface.ApplyCompletionChange(change.SpanStart, change.SpanLength,
                        change.NewText, change.IsSnippet);
                else
                    _surface.InsertCompletion(item.InsertText);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] CommitCompletion: {ex.Message}");
            _surface.InsertCompletion(item.InsertText);
        }
        finally { _suppressCompletion = false; }
    }

    private void HideCompletionWindow()
    {
        try { _completionWin?.SafeClose(); } catch { }
        _completionWin = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Quick Fix — independent window
    // ═══════════════════════════════════════════════════════════════════════
    private DiagnosticSpan? _activeQuickFixDiag;

    private async Task ShowQuickFixAsync(DiagnosticSpan diag)
    {
        try
        {
            var fixes = await _quickFixService.GetFixesAsync(
                _currentFilePath, _surface.Text,
                diag.StartOffset, diag.EndOffset,
                diag.Code, diag.Message);
            if (!fixes.Any()) return;

            _activeQuickFixDiag = diag;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var parentWin = TopLevel.GetTopLevel(this) as Window;
                if (parentWin == null) return;

                var r = _surface.GetCursorRectForPos(diag.Line - 1, diag.Column - 1);
                var screenPos = _surface.PointToScreen(new Point(r.X, r.Bottom + 4));

                if (_quickFixWin == null || !_quickFixWin.IsVisible)
                {
                    _quickFixWin = new RoslynQuickFixWindow();
                    _quickFixWin.FixChosen += OnQuickFixChosen;
                    _quickFixWin.Closed += (_, _) => _quickFixWin = null;
                }

                _quickFixWin.SetFixes(fixes, diag.Code);
                _quickFixWin.Position = new PixelPoint(screenPos.X, screenPos.Y);

                if (!_quickFixWin.IsVisible)
                    _quickFixWin.Show(parentWin);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InsaitEditor] QuickFix: {ex.Message}");
        }
    }

    private void OnQuickFixChosen(object? sender, QuickFixSuggestion fix)
    {
        HideQuickFixWindow();
        if (_activeQuickFixDiag == null) return;
        var diag = _activeQuickFixDiag;
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

    private void HideQuickFixWindow()
    {
        try { _quickFixWin?.SafeClose(); } catch { }
        _quickFixWin = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Signature Help (lightweight Canvas overlay — stays inline)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task ShowSignatureHelpAsync()
    {
        if (!IsCSharpFile()) return;
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
    //  Go to Definition — opens RoslynToolsWindow
    // ═══════════════════════════════════════════════════════════════════════
    private async Task GoToDefinitionAsync()
    {
        if (!IsCSharpFile()) return;
        try
        {
            int offset = _surface.GetCursorOffset();
            var result = await _csharpService.GetDefinitionAsync(
                _currentFilePath, _surface.Text, offset);
            if (result == null || !result.Locations.Any()) return;

            // Single in-source location — navigate directly
            var srcLocs = result.Locations.Where(l => !l.IsMetadata).ToList();
            if (srcLocs.Count == 1)
            {
                var loc = srcLocs[0];
                if (string.Equals(loc.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                    _surface.GoToLine(loc.StartLine, loc.StartColumn);
                else
                    GoToDefinitionRequested?.Invoke(this,
                        new GoToDefinitionRequestedEventArgs(loc.FilePath, loc.StartLine, loc.StartColumn));
                return;
            }

            // Multiple — show tools window
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var win = new RoslynToolsWindow();
                win.ShowDefinition(result);
                win.NavigateRequested += (_, entry) =>
                {
                    if (string.Equals(entry.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                        _surface.GoToLine(entry.Line, entry.Column);
                    else
                        GoToDefinitionRequested?.Invoke(this,
                            new GoToDefinitionRequestedEventArgs(entry.FilePath, entry.Line, entry.Column));
                    win.Close();
                };
                ShowToolWindow(win);
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[InsaitEditor] GoTo: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Find References — opens RoslynToolsWindow
    // ═══════════════════════════════════════════════════════════════════════
    private async Task FindReferencesAsync()
    {
        if (!IsCSharpFile()) return;
        try
        {
            int offset = _surface.GetCursorOffset();
            var refs = await _csharpService.FindReferencesAsync(
                _currentFilePath, _surface.Text, offset);
            if (!refs.Any()) return;

            var wordAtCursor = GetWordAtOffset(offset);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var win = new RoslynToolsWindow();
                win.ShowReferences(wordAtCursor, refs);
                win.NavigateRequested += (_, entry) =>
                {
                    if (string.Equals(entry.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                        _surface.GoToLine(entry.Line, entry.Column);
                    else
                        GoToDefinitionRequested?.Invoke(this,
                            new GoToDefinitionRequestedEventArgs(entry.FilePath, entry.Line, entry.Column));
                };
                ShowToolWindow(win);
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Refs: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Rename Symbol — opens RenameSymbolDialog
    // ═══════════════════════════════════════════════════════════════════════
    private async Task RenameSymbolAsync()
    {
        if (!IsCSharpFile()) return;
        try
        {
            int offset = _surface.GetCursorOffset();
            var oldName = GetWordAtOffset(offset);
            if (string.IsNullOrEmpty(oldName)) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dialog = new RenameSymbolDialog(
                    _csharpService, _currentFilePath, _surface.Text, offset, oldName);
                dialog.RenameConfirmed += (_, result) =>
                {
                    var changes = result.Changes
                        .Where(c => string.Equals(c.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(c => c.StartPosition).ToList();
                    var sb = new StringBuilder(_surface.Text);
                    foreach (var ch in changes)
                    {
                        sb.Remove(ch.StartPosition, ch.EndPosition - ch.StartPosition);
                        sb.Insert(ch.StartPosition, ch.NewText);
                    }
                    _surface.SetText(sb.ToString(), preserveCursor: true);
                    RenameCompleted?.Invoke(this, new RenameCompletedEventArgs(result));
                };
                ShowToolWindow(dialog);
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Rename: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Hover / Quick Info — opens RoslynToolsWindow
    // ═══════════════════════════════════════════════════════════════════════
    private async Task ShowHoverInfoAsync()
    {
        if (!IsCSharpFile()) return;
        try
        {
            int offset = _surface.GetCursorOffset();
            var qi = await _completionEngine.GetQuickInfoAsync(
                _currentFilePath, _surface.Text, offset);
            if (qi == null || !qi.Sections.Any()) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var win = new RoslynToolsWindow();
                win.ShowQuickInfo(qi);
                ShowToolWindow(win);
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[InsaitEditor] Hover: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Hide helpers
    // ═══════════════════════════════════════════════════════════════════════
    private void HideSignature()  => _signaturePopup.IsVisible = false;
    private void HideTooltip()    => _tooltipPopup.IsVisible = false;
    private void HideAllPopups()  { HideCompletionWindow(); HideQuickFixWindow(); HideSignature(); HideTooltip(); }

    private void ShowToolWindow(Window win)
    {
        var parent = TopLevel.GetTopLevel(this) as Window;
        if (parent != null) win.ShowDialog(parent);
        else win.Show();
    }

    private async Task ShowReadyBadgeAsync()
    {
        _readyBadge.IsVisible = true;
        EditorReady?.Invoke(this, EventArgs.Empty);
        await Task.Delay(3000);
        _readyBadge.IsVisible = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Keyboard
    // ═══════════════════════════════════════════════════════════════════════
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // ── Completion window navigation ─────────────────────────────────
        if (_completionWin != null && _completionWin.IsVisible)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    HideCompletionWindow(); e.Handled = true; return;
                case Key.Tab or Key.Return:
                    _completionWin.CommitSelected(); e.Handled = true; return;
                case Key.Down:
                    _completionWin.SelectNext(); e.Handled = true; return;
                case Key.Up:
                    _completionWin.SelectPrev(); e.Handled = true; return;
                case Key.PageDown:
                    _completionWin.SelectNextPage(); e.Handled = true; return;
                case Key.PageUp:
                    _completionWin.SelectPrevPage(); e.Handled = true; return;
            }
        }

        // ── Quick fix window navigation ──────────────────────────────────
        if (_quickFixWin != null && _quickFixWin.IsVisible)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    HideQuickFixWindow(); e.Handled = true; return;
                case Key.Return:
                    _quickFixWin.CommitSelected(); e.Handled = true; return;
                case Key.Down:
                    _quickFixWin.SelectNext(); e.Handled = true; return;
                case Key.Up:
                    _quickFixWin.SelectPrev(); e.Handled = true; return;
            }
        }

        // Alt+Enter / Ctrl+. → quick fix at cursor
        if ((e.Key == Key.Return && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) ||
            (e.Key == Key.OemPeriod && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            var diag = _surface.GetDiagnosticAtCursor();
            if (diag != null) { _ = ShowQuickFixAsync(diag); e.Handled = true; return; }
        }

        if (e.Key == Key.F9)
        {
            var (bpLine, _) = _surface.CursorPosition;
            if (!string.IsNullOrEmpty(_currentFilePath))
                BreakpointService.Toggle(_currentFilePath, bpLine);
            e.Handled = true; return;
        }

        if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Control)
                           && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { FormatDocument(); e.Handled = true; return; }

        if (e.Key == Key.F12)
        { _ = GoToDefinitionAsync(); e.Handled = true; return; }

        if (e.Key == Key.F12 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { _ = FindReferencesAsync(); e.Handled = true; return; }

        if (e.Key == Key.F2 ||
            (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        { _ = RenameSymbolAsync(); e.Handled = true; return; }

        if (e.Key == Key.H && e.KeyModifiers.HasFlag(KeyModifiers.Control)
                           && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { _ = ShowHoverInfoAsync(); e.Handled = true; return; }

        base.OnKeyDown(e);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════
    private bool IsCSharpFile() =>
        _currentFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !_currentFilePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase);

    private bool IsAxamlFile() =>
        _currentFilePath.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
        _currentFilePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);

    private string GetWordAtOffset(int offset)
    {
        var text = _surface.Text;
        int s = offset, e = offset;
        while (s > 0 && (char.IsLetterOrDigit(text[s - 1]) || text[s - 1] == '_')) s--;
        while (e < text.Length && (char.IsLetterOrDigit(text[e]) || text[e] == '_')) e++;
        return text[s..e];
    }
}

// ── Event args ──────────────────────────────────────────────────────────

public sealed class NuGetInstallRequestedEventArgs : EventArgs
{
    public string PackageName { get; }
    public NuGetInstallRequestedEventArgs(string packageName) => PackageName = packageName;
}

public sealed class GoToDefinitionRequestedEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public GoToDefinitionRequestedEventArgs(string filePath, int line, int column)
    { FilePath = filePath; Line = line; Column = column; }
}

public sealed class RenameCompletedEventArgs : EventArgs
{
    public RenameResult Result { get; }
    public RenameCompletedEventArgs(RenameResult result) => Result = result;
}

