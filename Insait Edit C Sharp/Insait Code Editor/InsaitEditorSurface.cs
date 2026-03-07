using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.InsaitCodeEditor;

// ═══════════════════════════════════════════════════════════════════════════
//  InsaitEditorSurface — custom Control that renders:
//    • gutter with diagnostic icons and line numbers
//    • text with Roslyn Classification syntax highlighting
//    • squiggly underlines for errors / warnings
//    • cursor + selection
//  Handles keyboard, mouse, undo/redo, auto-brackets, clipboard.
// ═══════════════════════════════════════════════════════════════════════════

internal sealed class InsaitEditorSurface : Control
{
    // ── Constants ────────────────────────────────────────────────────────
    private const double FontSizeDefault = 14.0;
    private const string FontName        = "Cascadia Code, Consolas, Courier New, monospace";
    private const double GutterPad       = 8.0;
    private const double LinePad         = 2.0;
    private const double TabWidth        = 4;
    private const double DiagGutterWidth = 18.0;

    // ── Text State ───────────────────────────────────────────────────────
    private readonly List<string> _lines = new() { string.Empty };
    private string  _fullText = string.Empty;
    public  string  Text      => _fullText;
    public  string? FilePath;
    public  bool    IsReadOnly;
    public  new double FontSize = FontSizeDefault;

    // ── Cursor & Selection ───────────────────────────────────────────────
    private int  _cursorLine = 0;
    private int  _cursorCol  = 0;
    private int  _selStartLine = -1, _selStartCol = -1;
    private int  _selEndLine   = -1, _selEndCol   = -1;
    private bool HasSelection => _selStartLine >= 0 && _selEndLine >= 0;

    public (int line, int col) CursorPosition => (_cursorLine + 1, _cursorCol + 1);

    // ── Scrolling ────────────────────────────────────────────────────────
    private int    _scrollTop;
    private int    _scrollLeft;
    private double _lineHeight;
    private double _charWidth;
    private double _gutterWidth;

    public int ScrollTop
    {
        get => _scrollTop;
        set { _scrollTop = Math.Max(0, value); InvalidateVisual(); }
    }
    public int ScrollLeft
    {
        get => _scrollLeft;
        set { _scrollLeft = Math.Max(0, value); InvalidateVisual(); }
    }
    public int    TotalLines   => _lines.Count;
    public int    VisibleLines => (int)(Bounds.Height / (_lineHeight > 0 ? _lineHeight : 20));
    public double MaxLineWidth => _lines.Count > 0 ? _lines.Max(l => l.Length) * _charWidth : 0;
    public double ViewportWidth => Math.Max(0, Bounds.Width - _gutterWidth - 12);

    /// <summary>Returns the text of a line (0-based index). Empty string if out of range.</summary>
    public string GetLineText(int lineIndex) =>
        lineIndex >= 0 && lineIndex < _lines.Count ? _lines[lineIndex] : string.Empty;

    // ── Roslyn Highlighting ──────────────────────────────────────────────
    private List<ClassifiedSpan>     _classifiedSpans = new();
    private AdhocWorkspace?          _workspace;
    private ProjectId?               _projectId;
    private DocumentId?              _documentId;
    private string?                  _trackedPath;
    private CancellationTokenSource? _highlightCts;
    private readonly List<MetadataReference> _refs = CollectRefs();

    // ── Diagnostics ──────────────────────────────────────────────────────
    private List<DiagnosticSpan> _diagnostics = new();

    // ── Breakpoints ──────────────────────────────────────────────────────
    private const double BreakpointColWidth = 16.0;
    private static readonly Color BreakpointColor       = Color.Parse("#FFF38BA8");
    private static readonly Color BreakpointActiveColor = Color.Parse("#FFFF5555");

    // ── Live Template Session ──────────────────────────────────────────
    private LiveTemplateSession? _templateSession;

    /// <summary>Whether a live template session is currently active.</summary>
    public bool IsTemplateSessionActive => _templateSession?.IsActive == true;

    // ── Undo / Redo ──────────────────────────────────────────────────────
    public readonly UndoRedoManager UndoRedoMgr = new();
    public bool CanUndo => UndoRedoMgr.CanUndo;
    public bool CanRedo => UndoRedoMgr.CanRedo;

    // ── Cursor blink ─────────────────────────────────────────────────────
    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorVisible = true;

    // ── Events ───────────────────────────────────────────────────────────
    public event EventHandler?                 TextChanged;
    public event EventHandler<(int, int)>?     CursorMoved;
    public event EventHandler?                 RequestCompletion;
    public event EventHandler?                 RequestSignature;
    public event EventHandler?                 ScrollChanged;
    public event EventHandler?                 ViewportChanged;
    public event EventHandler<DiagnosticSpan>? HoverDiagnostic;
    public event EventHandler?                 HoverCleared;
    public event EventHandler<DiagnosticSpan>? RequestQuickFix;
    /// <summary>Fired on Ctrl+Click — cursor position is set, caller should invoke Go to Definition.</summary>
    public event EventHandler?                 CtrlClickGoToDefinition;
    /// <summary>Fired when a non-identifier character is typed (space, ;, etc.) — completion should close.</summary>
    public event EventHandler?                 CompletionDismissChar;
    /// <summary>Fired when a live template session starts — caller can update UI state.</summary>
    public event EventHandler?                 TemplateSessionStarted;
    /// <summary>Fired when the live template session ends.</summary>
    public event EventHandler?                 TemplateSessionEnded;

    // ══════════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════════
    public InsaitEditorSurface()
    {
        Focusable    = true;
        ClipToBounds = true;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) => { _cursorVisible = !_cursorVisible; InvalidateVisual(); };
        _cursorTimer.Start();

        // Redraw when any breakpoint changes (keeps multi-file state in sync)
        Services.BreakpointService.BreakpointsChanged += (_, _) =>
            Dispatcher.UIThread.Post(InvalidateVisual);

        InitWorkspace();
    }

    private void InitWorkspace()
    {
        try
        {
            var set = new HashSet<Assembly>(MefHostServices.DefaultAssemblies);
            foreach (var name in new[]
            {
                "Microsoft.CodeAnalysis.Features",
                "Microsoft.CodeAnalysis.CSharp.Features",
                "Microsoft.CodeAnalysis.Workspaces.Common",
                "Microsoft.CodeAnalysis.CSharp.Workspaces",
            })
            {
                try { set.Add(Assembly.Load(name)); } catch { }
            }
            var host = MefHostServices.Create(set);
            _workspace = new AdhocWorkspace(host);
        }
        catch { /* workspace optional */ }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Public helpers
    // ══════════════════════════════════════════════════════════════════════

    public void SetText(string text, bool preserveCursor = false)
    {
        _fullText = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        _lines.Clear();
        _lines.AddRange(_fullText.Split('\n'));
        if (_lines.Count == 0) _lines.Add(string.Empty);
        if (!preserveCursor)
        {
            _cursorLine = 0; _cursorCol = 0;
            // Reset scroll position when loading new content so the new file starts at top.
            // Stale scroll values from the previous tab are meaningless for a different file.
            _scrollTop  = 0;
            _scrollLeft = 0;
        }
        // Always clear any lingering selection. Stale selection indices can be
        // out-of-bounds for a shorter new file, causing the editor to freeze or
        // stop responding to pointer/keyboard events after a tab switch.
        ClearSelection();
        ClampCursor();
        _classifiedSpans.Clear();
        ScheduleHighlight();
        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void GoToLine(int line, int col = 1)
    {
        _cursorLine = Math.Clamp(line - 1, 0, _lines.Count - 1);
        _cursorCol  = Math.Clamp(col  - 1, 0, _lines[_cursorLine].Length);
        EnsureCursorVisible();
        InvalidateVisual();
    }

    public void SetDiagnostics(List<DiagnosticSpan> spans)
    {
        _diagnostics = spans;
        _lastHoveredDiag = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Adjusts all diagnostic offsets after an edit so underlines stay in
    /// approximately correct positions until the next re-analysis completes.
    /// </summary>
    public void AdjustDiagnosticOffsets(int editOffset, int delta)
    {
        if (delta == 0 || _diagnostics.Count == 0) return;
        foreach (var d in _diagnostics)
        {
            if (d.StartOffset > editOffset)
            {
                d.StartOffset = Math.Max(0, d.StartOffset + delta);
                d.EndOffset   = Math.Max(d.StartOffset + 1, d.EndOffset + delta);
            }
            else if (d.EndOffset > editOffset)
            {
                // Edit is inside the diagnostic span — expand/shrink it
                d.EndOffset = Math.Max(d.StartOffset + 1, d.EndOffset + delta);
            }
        }
    }

    public Rect GetCursorRect()
    {
        double x = _gutterWidth + (_cursorCol - _scrollLeft) * _charWidth;
        double y = (_cursorLine - _scrollTop) * _lineHeight;
        return new Rect(x, y, _charWidth, _lineHeight);
    }

    public Rect GetCursorRectForPos(int line, int col)
    {
        double x = _gutterWidth + (col - _scrollLeft) * _charWidth;
        double y = (line - _scrollTop) * _lineHeight;
        return new Rect(x, y, _charWidth, _lineHeight);
    }

    public int GetCursorOffset() => OffsetForPos(_cursorLine, _cursorCol);

    public DiagnosticSpan? GetDiagnosticAtCursor()
    {
        int off = GetCursorOffset();
        return _diagnostics.FirstOrDefault(d => off >= d.StartOffset && off <= d.EndOffset);
    }

    public void InsertCompletion(string word)
    {
        var line = _lines[_cursorLine];
        int start = _cursorCol;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_'))
            start--;
        var prefix = line[start.._cursorCol];
        var toInsert = word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? word[prefix.Length..] : word;
        InsertTextAtCursor(toInsert);
    }

    public void InsertTextAt(int offset, string text)
    {
        offset = Math.Clamp(offset, 0, _fullText.Length);
        RecordUndo(offset, string.Empty, text);
        var sb = new StringBuilder(_fullText);
        sb.Insert(offset, text);
        _fullText = sb.ToString();
        RebuildLines();
        AdjustDiagnosticOffsets(offset, text.Length);
        ScheduleHighlight();
        TextChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Returns the word prefix at the current cursor position (for completion filtering).
    /// </summary>
    public string GetCurrentWordPrefix()
    {
        if (_cursorLine < 0 || _cursorLine >= _lines.Count) return string.Empty;
        var line = _lines[_cursorLine];
        int start = _cursorCol;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_'))
            start--;
        return start < _cursorCol ? line[start.._cursorCol] : string.Empty;
    }

    public void RemoveTextRange(int start, int end)
    {
        start = Math.Clamp(start, 0, _fullText.Length);
        end   = Math.Clamp(end, start, _fullText.Length);
        var removed = _fullText[start..end];
        RecordUndo(start, removed, string.Empty);
        var sb = new StringBuilder(_fullText);
        sb.Remove(start, end - start);
        _fullText = sb.ToString();
        RebuildLines();
        AdjustDiagnosticOffsets(start, -(end - start));
        SetCursorFromOffset(start);
        ScheduleHighlight();
        TextChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Applies a Roslyn completion change: replaces [spanStart .. spanStart+spanLength)
    /// with <paramref name="newText"/> and positions the cursor at the end.
    /// For snippet items whose newText contains VS-style placeholders ($0, ${1:...}),
    /// the placeholders are expanded and the cursor is placed at $0.
    /// </summary>
    public void ApplyCompletionChange(int spanStart, int spanLength, string newText, bool isSnippet)
    {
        spanStart  = Math.Clamp(spanStart, 0, _fullText.Length);
        spanLength = Math.Clamp(spanLength, 0, _fullText.Length - spanStart);

        var removed = _fullText.Substring(spanStart, spanLength);

        string textToInsert;
        int cursorAfter;

        if (isSnippet)
        {
            // Determine current-line indent at the span start
            int lineIdx = 0, off = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (off + _lines[i].Length >= spanStart) { lineIdx = i; break; }
                off += _lines[i].Length + 1;
            }
            var lineText = _lines[lineIdx];
            var indent = new string(' ', lineText.Length - lineText.TrimStart().Length);

            var (expanded, cursorOff) = Services.CSharpSnippetProvider.ExpandSnippetBody(newText, indent);
            textToInsert = expanded;
            cursorAfter = spanStart + cursorOff;
        }
        else
        {
            textToInsert = newText;
            cursorAfter = spanStart + newText.Length;
        }

        RecordUndo(spanStart, removed, textToInsert);
        var sb = new StringBuilder(_fullText);
        sb.Remove(spanStart, spanLength);
        sb.Insert(spanStart, textToInsert);
        _fullText = sb.ToString();
        RebuildLines();
        AdjustDiagnosticOffsets(spanStart, textToInsert.Length - spanLength);
        SetCursorFromOffset(Math.Clamp(cursorAfter, 0, _fullText.Length));
        ScheduleHighlight(); ClearSelection(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty); CursorMoved?.Invoke(this, CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Live Template Session — expand template and navigate tab-stops
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts a live template session: replaces the trigger word at the cursor
    /// with the expanded template body and activates tab-stop navigation.
    /// </summary>
    /// <param name="spanStart">Start offset of the trigger word to replace.</param>
    /// <param name="spanLength">Length of the trigger word to replace.</param>
    /// <param name="templateBody">Template body with VS-style placeholders ($1, ${1:text}, $0).</param>
    public void StartLiveTemplate(int spanStart, int spanLength, string templateBody)
    {
        // End any existing session
        EndTemplateSession();

        spanStart  = Math.Clamp(spanStart, 0, _fullText.Length);
        spanLength = Math.Clamp(spanLength, 0, _fullText.Length - spanStart);

        // Determine current-line indent
        int lineIdx = 0, off = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (off + _lines[i].Length >= spanStart) { lineIdx = i; break; }
            off += _lines[i].Length + 1;
        }
        var lineText = _lines[lineIdx];
        var indent = new string(' ', lineText.Length - lineText.TrimStart().Length);

        // Create and expand the template session
        _templateSession = new LiveTemplateSession();
        var expandedText = _templateSession.Expand(templateBody, spanStart, indent);

        // Remove trigger word and insert expanded text
        var removed = _fullText.Substring(spanStart, spanLength);
        RecordUndo(spanStart, removed, expandedText);
        var sb = new StringBuilder(_fullText);
        sb.Remove(spanStart, spanLength);
        sb.Insert(spanStart, expandedText);
        _fullText = sb.ToString();
        RebuildLines();

        // Navigate to the first tab-stop and select its text
        SelectCurrentTabStop();

        ScheduleHighlight(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty);
        CursorMoved?.Invoke(this, CursorPosition);
        TemplateSessionStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Advances to the next tab-stop in the active template session.
    /// Returns true if the session is still active, false if it ended.
    /// </summary>
    public bool TemplateTabNext()
    {
        if (_templateSession == null || !_templateSession.IsActive) return false;

        if (_templateSession.MoveNext())
        {
            SelectCurrentTabStop();
            InvalidateVisual();
            return true;
        }

        // Session ended — place cursor at $0 position
        var finalOffset = _templateSession.GetCursorOffset();
        EndTemplateSession();
        SetCursorFromOffset(Math.Clamp(finalOffset, 0, _fullText.Length));
        ClearSelection();
        EnsureCursorVisible();
        InvalidateVisual();
        return false;
    }

    /// <summary>
    /// Moves to the previous tab-stop in the active template session.
    /// Returns true if moved successfully.
    /// </summary>
    public bool TemplateTabPrevious()
    {
        if (_templateSession == null || !_templateSession.IsActive) return false;

        if (_templateSession.MovePrevious())
        {
            SelectCurrentTabStop();
            InvalidateVisual();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Ends the current live template session (e.g. on Escape or typing outside).
    /// </summary>
    public void EndTemplateSession()
    {
        if (_templateSession != null)
        {
            _templateSession.End();
            _templateSession = null;
            TemplateSessionEnded?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Returns the active template session (for rendering highlights), or null.
    /// </summary>
    public LiveTemplateSession? GetTemplateSession() => _templateSession;

    /// <summary>
    /// Selects the text of the current tab-stop in the active session.
    /// </summary>
    private void SelectCurrentTabStop()
    {
        if (_templateSession?.CurrentStop == null) return;

        var (selOffset, selLength) = _templateSession.GetCurrentSelection();
        SetCursorFromOffset(Math.Clamp(selOffset, 0, _fullText.Length));

        if (selLength > 0)
        {
            // Select the placeholder text
            int startOff = Math.Clamp(selOffset, 0, _fullText.Length);
            int endOff   = Math.Clamp(selOffset + selLength, 0, _fullText.Length);

            // Convert offsets to line/col for selection
            OffsetToLineCol(startOff, out int sl, out int sc);
            OffsetToLineCol(endOff,   out int el, out int ec);

            _selStartLine = sl; _selStartCol = sc;
            _selEndLine   = el; _selEndCol   = ec;
            _cursorLine   = el; _cursorCol   = ec;
        }
        else
        {
            ClearSelection();
        }

        EnsureCursorVisible();
        CursorMoved?.Invoke(this, CursorPosition);
    }

    /// <summary>
    /// Converts an absolute offset to (line, col) — both 0-based.
    /// </summary>
    private void OffsetToLineCol(int offset, out int line, out int col)
    {
        int o = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (o + _lines[i].Length >= offset || i == _lines.Count - 1)
            {
                line = i;
                col  = offset - o;
                return;
            }
            o += _lines[i].Length + 1;
        }
        line = _lines.Count - 1;
        col  = _lines[line].Length;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Render
    // ══════════════════════════════════════════════════════════════════════
    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var typeface = new Typeface(FontName);
        var testText = new FormattedText("W",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, FontSize, Brushes.Black);
        _charWidth  = testText.Width;
        _lineHeight = testText.Height + LinePad * 2;

        int gutterDigits = Math.Max(3, _lines.Count.ToString().Length);
        _gutterWidth = gutterDigits * _charWidth + GutterPad * 2 + DiagGutterWidth + BreakpointColWidth;

        // background
        ctx.FillRectangle(new SolidColorBrush(InsaitEditorColors.Background), bounds);

        // gutter
        ctx.FillRectangle(new SolidColorBrush(InsaitEditorColors.GutterBg),
            new Rect(0, 0, _gutterWidth, bounds.Height));
        ctx.DrawLine(new Pen(new SolidColorBrush(InsaitEditorColors.GutterBorder), 1),
            new Point(_gutterWidth, 0), new Point(_gutterWidth, bounds.Height));

        int firstVis = _scrollTop;
        int lastVis  = Math.Min(_lines.Count - 1, _scrollTop + VisibleLines + 1);

        // diagnostic icons per line
        var diagLineSet = new Dictionary<int, DiagnosticSeverityKind>();
        foreach (var d in _diagnostics)
        {
            int dLine = d.Line - 1;
            if (!diagLineSet.ContainsKey(dLine) || d.Severity < diagLineSet[dLine])
                diagLineSet[dLine] = d.Severity;
        }

        for (int li = firstVis; li <= lastVis; li++)
        {
            double y = (li - _scrollTop) * _lineHeight;

            // current-line highlight
            if (li == _cursorLine)
                ctx.FillRectangle(new SolidColorBrush(InsaitEditorColors.CurrentLine),
                    new Rect(0, y, bounds.Width, _lineHeight));

            DrawSelection(ctx, li, y);

            // breakpoint circle
            var bpFilePath = FilePath ?? string.Empty;
            if (!string.IsNullOrEmpty(bpFilePath) && Services.BreakpointService.Has(bpFilePath, li + 1))
            {
                double bpCx = BreakpointColWidth / 2.0;
                double bpCy = y + _lineHeight / 2.0;
                double bpR  = Math.Min(BreakpointColWidth, _lineHeight) / 2.0 - 2;
                var bpBrush = new SolidColorBrush(BreakpointActiveColor);
                ctx.FillRectangle(bpBrush, new Rect(bpCx - bpR, bpCy - bpR, bpR * 2, bpR * 2));
                // draw red filled circle via ellipse geometry
                var eg = new EllipseGeometry(new Rect(bpCx - bpR, bpCy - bpR, bpR * 2, bpR * 2));
                ctx.DrawGeometry(bpBrush, null, eg);
            }

            // gutter diagnostic icon
            if (diagLineSet.TryGetValue(li, out var sev))
            {
                string icon = sev switch
                {
                    DiagnosticSeverityKind.Error   => "●",
                    DiagnosticSeverityKind.Warning => "▲",
                    _                              => "◆",
                };
                var iconClr = sev switch
                {
                    DiagnosticSeverityKind.Error   => InsaitEditorColors.DiagError,
                    DiagnosticSeverityKind.Warning => InsaitEditorColors.DiagWarning,
                    _                              => InsaitEditorColors.DiagInfo,
                };
                var ft = new FormattedText(icon,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, FontSize - 2,
                    new SolidColorBrush(iconClr));
                ctx.DrawText(ft, new Point(BreakpointColWidth + 3, y + LinePad + 1));
            }

            // line number
            var numBrush = new SolidColorBrush(
                li == _cursorLine ? InsaitEditorColors.Cursor : InsaitEditorColors.GutterFg);
            var numText = new FormattedText((li + 1).ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, FontSize - 1, numBrush);
            ctx.DrawText(numText,
                new Point(_gutterWidth - numText.Width - GutterPad - 2, y + LinePad));

            DrawLine(ctx, typeface, li, y);
            DrawDiagnosticUnderlines(ctx, li, y);

            // Live template tab-stop highlights
            DrawTemplateHighlights(ctx, li, y);
        }

        // cursor
        if (_cursorVisible && IsFocused)
        {
            double cy = (_cursorLine - _scrollTop) * _lineHeight;
            double cx = _gutterWidth + (_cursorCol - _scrollLeft) * _charWidth;
            ctx.DrawLine(new Pen(new SolidColorBrush(InsaitEditorColors.Cursor), 2),
                new Point(cx, cy + 1), new Point(cx, cy + _lineHeight - 1));
        }
    }

    // ── render helpers ───────────────────────────────────────────────────

    private void DrawSelection(DrawingContext ctx, int li, double y)
    {
        if (!HasSelection) return;
        int sl = Math.Min(_selStartLine, _selEndLine);
        int el = Math.Max(_selStartLine, _selEndLine);
        int sc = _selStartLine <= _selEndLine ? _selStartCol : _selEndCol;
        int ec = _selStartLine <= _selEndLine ? _selEndCol   : _selStartCol;
        if (li < sl || li > el) return;

        double x1 = _gutterWidth + (li == sl ? (sc - _scrollLeft) * _charWidth : 0);
        double x2 = li == el
            ? _gutterWidth + (ec - _scrollLeft) * _charWidth
            : Bounds.Width;
        if (x2 > x1)
            ctx.FillRectangle(new SolidColorBrush(InsaitEditorColors.Selection),
                new Rect(x1, y, x2 - x1, _lineHeight));
    }

    private void DrawLine(DrawingContext ctx, Typeface typeface, int li, double y)
    {
        var lineText = _lines[li];
        if (lineText.Length == 0) return;

        double xBase = _gutterWidth - _scrollLeft * _charWidth;
        int lineOff  = LineOffset(li);

        var spans = _classifiedSpans
            .Where(s => s.TextSpan.Start < lineOff + lineText.Length &&
                        s.TextSpan.End   > lineOff)
            .OrderBy(s => s.TextSpan.Start)
            .ToList();

        if (spans.Count == 0)
        {
            var ft = new FormattedText(lineText,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, FontSize,
                new SolidColorBrush(InsaitEditorColors.DefaultText));
            ctx.DrawText(ft, new Point(xBase, y + LinePad));
            return;
        }

        int pos = lineOff;
        int end = lineOff + lineText.Length;

        void Seg(int from, int to, Color color, bool bold = false)
        {
            int lf = Math.Max(from - lineOff, 0);
            int lt = Math.Min(to - lineOff, lineText.Length);
            if (lt <= lf) return;
            var seg = lineText[lf..lt];
            var tf = bold
                ? new Typeface(typeface.FontFamily, typeface.Style, FontWeight.Bold)
                : typeface;
            var ft = new FormattedText(seg,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, FontSize, new SolidColorBrush(color));
            ctx.DrawText(ft, new Point(xBase + lf * _charWidth, y + LinePad));
        }

        foreach (var span in spans)
        {
            if (span.TextSpan.Start > pos)
                Seg(pos, span.TextSpan.Start, InsaitEditorColors.DefaultText);
            var color = InsaitEditorColors.GetTokenColor(span.ClassificationType);
            bool bold = span.ClassificationType is ClassificationTypeNames.Keyword
                or ClassificationTypeNames.ControlKeyword
                or ClassificationTypeNames.ClassName
                or ClassificationTypeNames.RecordClassName;
            Seg(span.TextSpan.Start, span.TextSpan.End, color, bold);
            pos = span.TextSpan.End;
        }
        if (pos < end) Seg(pos, end, InsaitEditorColors.DefaultText);
    }

    private void DrawDiagnosticUnderlines(DrawingContext ctx, int li, double y)
    {
        if (_diagnostics.Count == 0) return;
        int lineOff = LineOffset(li);
        int lineEnd = lineOff + _lines[li].Length;

        foreach (var d in _diagnostics)
        {
            if (d.StartOffset >= lineEnd || d.EndOffset <= lineOff) continue;
            int sc = Math.Max(d.StartOffset - lineOff, 0);
            int ec = Math.Min(d.EndOffset - lineOff, _lines[li].Length);
            double x1 = _gutterWidth + (sc - _scrollLeft) * _charWidth;
            double x2 = _gutterWidth + (ec - _scrollLeft) * _charWidth;
            var color = d.Severity switch
            {
                DiagnosticSeverityKind.Error   => InsaitEditorColors.DiagError,
                DiagnosticSeverityKind.Warning => InsaitEditorColors.DiagWarning,
                _                              => InsaitEditorColors.DiagInfo,
            };
            DrawSquiggly(ctx, x1, x2, y + _lineHeight - 3, color);
        }
    }

    private static void DrawSquiggly(DrawingContext ctx, double x1, double x2,
        double y, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.5);
        const double amp = 2.0, freq = 6.0;
        var geo = new StreamGeometry();
        using var gctx = geo.Open();
        bool first = true;
        for (double x = x1; x <= x2; x += 1)
        {
            double yy = y + Math.Sin((x - x1) / freq * Math.PI) * amp;
            if (first) { gctx.BeginFigure(new Point(x, yy), false); first = false; }
            else gctx.LineTo(new Point(x, yy));
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    /// <summary>
    /// Draws highlight rectangles for live template tab-stops on the given line.
    /// Active group is highlighted with a brighter color; inactive stops get a subtle border.
    /// </summary>
    private void DrawTemplateHighlights(DrawingContext ctx, int li, double y)
    {
        if (_templateSession == null || !_templateSession.IsActive) return;

        int lineOff = LineOffset(li);
        int lineEnd = lineOff + _lines[li].Length;
        int activeGroup = _templateSession.CurrentGroupNumber;

        foreach (var stop in _templateSession.Stops)
        {
            // Skip $0 (final cursor) — no visual highlight for it
            if (stop.Number == 0) continue;

            int stopEnd = stop.Offset + stop.Length;
            // Check if this stop overlaps the current line
            if (stop.Offset >= lineEnd || stopEnd <= lineOff) continue;

            int sc = Math.Max(stop.Offset - lineOff, 0);
            int ec = Math.Min(stopEnd - lineOff, _lines[li].Length);
            double x1 = _gutterWidth + (sc - _scrollLeft) * _charWidth;
            double x2 = _gutterWidth + (ec - _scrollLeft) * _charWidth;

            if (stop.Number == activeGroup)
            {
                // Active tab-stop: highlight background + border
                var activeBg = new SolidColorBrush(Color.Parse("#30A0D0FF"));
                var activeBorder = new Pen(new SolidColorBrush(Color.Parse("#80A0D0FF")), 1.5);
                var rect = new Rect(x1, y, Math.Max(x2 - x1, _charWidth), _lineHeight);
                ctx.FillRectangle(activeBg, rect);
                ctx.DrawRectangle(null, activeBorder, rect);
            }
            else
            {
                // Inactive tab-stop: subtle dashed border
                var inactiveBorder = new Pen(new SolidColorBrush(Color.Parse("#40808080")), 1.0);
                var rect = new Rect(x1, y, Math.Max(x2 - x1, _charWidth), _lineHeight);
                ctx.DrawRectangle(null, inactiveBorder, rect);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Keyboard
    // ══════════════════════════════════════════════════════════════════════
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsReadOnly && e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down
            or Key.Home or Key.End or Key.PageUp or Key.PageDown))
        { base.OnKeyDown(e); return; }

        bool ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Left:     if (ctrl) MoveCursorWordLeft(shift); else MoveCursor(0, -1, shift); e.Handled = true; break;
            case Key.Right:    if (ctrl) MoveCursorWordRight(shift); else MoveCursor(0, 1, shift); e.Handled = true; break;
            case Key.Up:       if (ctrl) ScrollTop = Math.Max(0, _scrollTop - 1); else MoveCursor(-1, 0, shift); e.Handled = true; break;
            case Key.Down:     if (ctrl) ScrollTop = _scrollTop + 1; else MoveCursor(1, 0, shift); e.Handled = true; break;
            case Key.Home:     MoveHome(shift, ctrl); e.Handled = true; break;
            case Key.End:      MoveEnd(shift, ctrl);  e.Handled = true; break;
            case Key.PageUp:   MoveCursor(-VisibleLines, 0, shift); e.Handled = true; break;
            case Key.PageDown: MoveCursor( VisibleLines, 0, shift); e.Handled = true; break;

            case Key.Back:   if (!IsReadOnly) DeleteBack();    e.Handled = true; break;
            case Key.Delete: if (!IsReadOnly) DeleteForward(); e.Handled = true; break;
            case Key.Return:
                if (!IsReadOnly)
                {
                    // Enter ends the template session and inserts a newline
                    if (IsTemplateSessionActive) EndTemplateSession();
                    InsertNewLine();
                }
                e.Handled = true; break;
            case Key.Tab:
                if (!IsReadOnly)
                {
                    // Live template tab-stop navigation takes priority
                    if (IsTemplateSessionActive)
                    {
                        if (shift) TemplateTabPrevious();
                        else       TemplateTabNext();
                    }
                    else
                    {
                        InsertTab(shift);
                    }
                }
                e.Handled = true; break;
            case Key.Escape:
                if (IsTemplateSessionActive) { EndTemplateSession(); e.Handled = true; break; }
                break;

            case Key.Z when ctrl && !shift: Undo(); e.Handled = true; break;
            case Key.Z when ctrl &&  shift: Redo(); e.Handled = true; break;
            case Key.Y when ctrl:           Redo(); e.Handled = true; break;

            case Key.A when ctrl: SelectAll();       e.Handled = true; break;
            case Key.C when ctrl: _ = CopyAsync();   e.Handled = true; break;
            case Key.X when ctrl: _ = CutAsync();    e.Handled = true; break;
            case Key.V when ctrl: _ = PasteAsync();  e.Handled = true; break;
            case Key.D when ctrl: DuplicateLine();   e.Handled = true; break;

            case Key.Space when ctrl:
                RequestCompletion?.Invoke(this, EventArgs.Empty);
                e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (IsReadOnly || string.IsNullOrEmpty(e.Text)) { base.OnTextInput(e); return; }

        // If a template session is active, check if cursor is within a tab-stop
        // and end the session if it's not (user typed outside the template area)
        if (IsTemplateSessionActive)
        {
            int curOff = GetCursorOffset();
            var session = _templateSession!;
            bool insideTemplate = curOff >= session.InsertionOffset &&
                                  curOff <= session.InsertionOffset + session.TotalLength;
            if (!insideTemplate)
                EndTemplateSession();
        }

        if (HasSelection) DeleteSelection();

        char ch = e.Text![0];
        char paired = ch switch { '(' => ')', '{' => '}', '[' => ']', '"' => '"', '\'' => '\'', _ => '\0' };
        InsertTextAtCursor(e.Text);
        if (paired != '\0')
        {
            var line = _lines[_cursorLine];
            char next = _cursorCol < line.Length ? line[_cursorCol] : '\0';
            if (next != paired) InsertTextAtCursorNoCursor(paired.ToString());
        }
        if (char.IsLetter(ch) || ch == '.' || ch == '_') RequestCompletion?.Invoke(this, EventArgs.Empty);
        else if (IsAxamlFile(FilePath) && (ch == '<' || ch == '{' || ch == ' ' || ch == ':' || ch == '/'))
            RequestCompletion?.Invoke(this, EventArgs.Empty);
        else if (!char.IsDigit(ch)) CompletionDismissChar?.Invoke(this, EventArgs.Empty);
        if (ch is '(' or ',') RequestSignature?.Invoke(this, EventArgs.Empty);
        base.OnTextInput(e);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Mouse
    // ══════════════════════════════════════════════════════════════════════
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var pt = e.GetPosition(this);

        // ── Click in breakpoint column (leftmost BreakpointColWidth px) ──
        if (pt.X < BreakpointColWidth)
        {
            PositionFromPoint(pt, out int bpLi, out _);
            var bpFile = FilePath ?? string.Empty;
            if (!string.IsNullOrEmpty(bpFile))
            {
                Services.BreakpointService.Toggle(bpFile, bpLi + 1);
                InvalidateVisual();
            }
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        if (pt.X < DiagGutterWidth + BreakpointColWidth + 4)
        {
            PositionFromPoint(pt, out int gli, out _);
            var gd = _diagnostics.FirstOrDefault(d => d.Line - 1 == gli);
            if (gd != null) { RequestQuickFix?.Invoke(this, gd); e.Handled = true; base.OnPointerPressed(e); return; }
        }

        PositionFromPoint(pt, out int li, out int ci);

        // End live template session on mouse click — user navigated away
        if (IsTemplateSessionActive) EndTemplateSession();

        _cursorLine = li; _cursorCol = ci;
        _selStartLine = li; _selStartCol = ci;
        _selEndLine = -1; _selEndCol = -1;
        InvalidateVisual(); e.Handled = true;

        // Ctrl+Click → Go to Definition
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CtrlClickGoToDefinition?.Invoke(this, EventArgs.Empty);
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pt = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            PositionFromPoint(pt, out int li, out int ci);
            _selEndLine = li; _selEndCol = ci; _cursorLine = li; _cursorCol = ci;
            InvalidateVisual();
        }
        else
        {
            PositionFromPoint(pt, out int hli, out int hci);
            int off = OffsetForPos(hli, hci);
            var hd = _diagnostics.FirstOrDefault(d => off >= d.StartOffset && off <= d.EndOffset);
            if (hd != null)
            {
                // Only fire if we moved to a different diagnostic
                if (!ReferenceEquals(hd, _lastHoveredDiag))
                {
                    _lastHoveredDiag = hd;
                    HoverDiagnostic?.Invoke(this, hd);
                }
            }
            else
            {
                if (_lastHoveredDiag != null)
                {
                    _lastHoveredDiag = null;
                    HoverCleared?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        base.OnPointerMoved(e);
    }

    private DiagnosticSpan? _lastHoveredDiag;

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _lastHoveredDiag = null;
        HoverCleared?.Invoke(this, EventArgs.Empty);
        base.OnPointerExited(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ScrollTop = Math.Max(0, _scrollTop - (int)(e.Delta.Y * 3));
        ScrollChanged?.Invoke(this, EventArgs.Empty);
        base.OnPointerWheelChanged(e);
    }

    private void PositionFromPoint(Point pt, out int lineIdx, out int colIdx)
    {
        lineIdx = Math.Clamp((int)(pt.Y / (_lineHeight > 0 ? _lineHeight : 20)) + _scrollTop, 0, _lines.Count - 1);
        colIdx  = Math.Clamp((int)((pt.X - _gutterWidth) / (_charWidth > 0 ? _charWidth : 8)) + _scrollLeft, 0, _lines[lineIdx].Length);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Text operations
    // ══════════════════════════════════════════════════════════════════════

    private void InsertTextAtCursor(string text)
    {
        int offset = GetCursorOffset();
        RecordUndo(offset, string.Empty, text);
        _lines[_cursorLine] = _lines[_cursorLine][.._cursorCol] + text + _lines[_cursorLine][_cursorCol..];
        _cursorCol += text.Length;
        AdjustDiagnosticOffsets(offset, text.Length);
        RebuildFullText(); ScheduleHighlight(); ClearSelection(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty); CursorMoved?.Invoke(this, CursorPosition);
    }

    private void InsertTextAtCursorNoCursor(string text)
    {
        _lines[_cursorLine] = _lines[_cursorLine][.._cursorCol] + text + _lines[_cursorLine][_cursorCol..];
        RebuildFullText();
    }

    private void InsertNewLine()
    {
        if (HasSelection) DeleteSelection();
        int offset = GetCursorOffset();
        RecordUndo(offset, string.Empty, "\n");

        var current = _lines[_cursorLine];
        var tail    = current[_cursorCol..];
        var indent  = new string(' ', current.Length - current.TrimStart().Length);
        if (_cursorCol > 0)
        {
            char last = current[.._cursorCol].TrimEnd().LastOrDefault();
            if (last is '{' or ':') indent += new string(' ', (int)TabWidth);
        }
        _lines[_cursorLine] = current[.._cursorCol];
        _lines.Insert(_cursorLine + 1, indent + tail);
        _cursorLine++; _cursorCol = indent.Length;
        // +1 for the newline, + indent.Length for the auto-indent
        AdjustDiagnosticOffsets(offset, 1 + indent.Length);
        RebuildFullText(); ScheduleHighlight(); ClearSelection(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty); CursorMoved?.Invoke(this, CursorPosition);
    }

    private void InsertTab(bool shift)
    {
        if (shift)
        {
            var line = _lines[_cursorLine];
            int spaces = 0;
            while (spaces < (int)TabWidth && spaces < line.Length && line[spaces] == ' ') spaces++;
            if (spaces > 0)
            {
                int off = LineOffset(_cursorLine);
                RecordUndo(off, line[..spaces], string.Empty);
                _lines[_cursorLine] = line[spaces..];
                _cursorCol = Math.Max(0, _cursorCol - spaces);
                AdjustDiagnosticOffsets(off, -spaces);
                RebuildFullText(); ScheduleHighlight();
                TextChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else InsertTextAtCursor(new string(' ', (int)TabWidth));
    }

    private void DeleteBack()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (_cursorCol > 0)
        {
            int off = GetCursorOffset();
            RecordUndo(off - 1, _lines[_cursorLine][_cursorCol - 1].ToString(), string.Empty);
            _lines[_cursorLine] = _lines[_cursorLine].Remove(_cursorCol - 1, 1);
            _cursorCol--;
            AdjustDiagnosticOffsets(off - 1, -1);
        }
        else if (_cursorLine > 0)
        {
            int off = GetCursorOffset();
            int prevLen = _lines[_cursorLine - 1].Length;
            RecordUndo(off - 1, "\n", string.Empty);
            _lines[_cursorLine - 1] += _lines[_cursorLine];
            _lines.RemoveAt(_cursorLine);
            _cursorLine--; _cursorCol = prevLen;
            AdjustDiagnosticOffsets(off - 1, -1);
        }
        RebuildFullText(); ScheduleHighlight(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty); CursorMoved?.Invoke(this, CursorPosition);
    }

    private void DeleteForward()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (_cursorCol < _lines[_cursorLine].Length)
        {
            int off = GetCursorOffset();
            RecordUndo(off, _lines[_cursorLine][_cursorCol].ToString(), string.Empty);
            _lines[_cursorLine] = _lines[_cursorLine].Remove(_cursorCol, 1);
            AdjustDiagnosticOffsets(off, -1);
        }
        else if (_cursorLine < _lines.Count - 1)
        {
            int off = GetCursorOffset();
            RecordUndo(off, "\n", string.Empty);
            _lines[_cursorLine] += _lines[_cursorLine + 1];
            _lines.RemoveAt(_cursorLine + 1);
            AdjustDiagnosticOffsets(off, -1);
        }
        RebuildFullText(); ScheduleHighlight(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DuplicateLine()
    {
        var line = _lines[_cursorLine];
        int off = LineOffset(_cursorLine) + line.Length;
        RecordUndo(off, string.Empty, "\n" + line);
        _lines.Insert(_cursorLine + 1, line);
        _cursorLine++;
        AdjustDiagnosticOffsets(off, 1 + line.Length);
        RebuildFullText(); ScheduleHighlight(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty); CursorMoved?.Invoke(this, CursorPosition);
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        int sl = Math.Min(_selStartLine, _selEndLine);
        int el = Math.Max(_selStartLine, _selEndLine);
        int sc, ec;
        if (_selStartLine < _selEndLine)
        {
            sc = _selStartCol;
            ec = _selEndCol;
        }
        else if (_selStartLine > _selEndLine)
        {
            sc = _selEndCol;
            ec = _selStartCol;
        }
        else
        {
            // Same line — always use min/max for columns
            sc = Math.Min(_selStartCol, _selEndCol);
            ec = Math.Max(_selStartCol, _selEndCol);
        }

        // Clamp columns to actual line lengths to prevent out-of-range
        sc = Math.Clamp(sc, 0, sl < _lines.Count ? _lines[sl].Length : 0);
        ec = Math.Clamp(ec, 0, el < _lines.Count ? _lines[el].Length : 0);

        int so = OffsetForPos(sl, sc), eo = OffsetForPos(el, ec);
        if (so >= eo) { ClearSelection(); return; } // nothing to delete
        RecordUndo(so, _fullText[so..eo], string.Empty);
        if (sl == el) _lines[sl] = _lines[sl].Remove(sc, ec - sc);
        else { _lines[sl] = _lines[sl][..sc] + _lines[el][ec..]; _lines.RemoveRange(sl + 1, el - sl); }
        _cursorLine = sl; _cursorCol = sc;
        AdjustDiagnosticOffsets(so, -(eo - so));
        ClearSelection(); RebuildFullText(); ScheduleHighlight(); EnsureCursorVisible();
        TextChanged?.Invoke(this, EventArgs.Empty); CursorMoved?.Invoke(this, CursorPosition);
    }

    // ── Clipboard ────────────────────────────────────────────────────────
    private async Task CopyAsync()
    {
        var text = GetSelectedText();
        if (string.IsNullOrEmpty(text)) return;
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb != null) await cb.SetTextAsync(text);
    }
    private async Task CutAsync() { await CopyAsync(); if (HasSelection) DeleteSelection(); }
    private async Task PasteAsync()
    {
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb == null) return;
        var text = await cb.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        if (HasSelection) DeleteSelection();
        var parts = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            InsertTextAtCursor(parts[i]);
            if (i < parts.Length - 1) InsertNewLine();
        }
    }

    // ── Public clipboard wrappers (for context menu) ─────────────────────
    public void DoCut()   => _ = CutAsync();
    public void DoCopy()  => _ = CopyAsync();
    public void DoPaste() => _ = PasteAsync();
    private string GetSelectedText()
    {
        if (!HasSelection) return string.Empty;
        int sl = Math.Min(_selStartLine, _selEndLine), el = Math.Max(_selStartLine, _selEndLine);
        int sc, ec;
        if (_selStartLine < _selEndLine)
        {
            sc = _selStartCol;
            ec = _selEndCol;
        }
        else if (_selStartLine > _selEndLine)
        {
            sc = _selEndCol;
            ec = _selStartCol;
        }
        else
        {
            sc = Math.Min(_selStartCol, _selEndCol);
            ec = Math.Max(_selStartCol, _selEndCol);
        }
        sc = Math.Clamp(sc, 0, sl < _lines.Count ? _lines[sl].Length : 0);
        ec = Math.Clamp(ec, 0, el < _lines.Count ? _lines[el].Length : 0);
        int so = OffsetForPos(sl, sc), eo = OffsetForPos(el, ec);
        return so < eo ? _fullText[so..eo] : string.Empty;
    }
    private void SelectAll()
    {
        _selStartLine = 0; _selStartCol = 0;
        _selEndLine = _lines.Count - 1; _selEndCol = _lines[_selEndLine].Length;
        _cursorLine = _selEndLine; _cursorCol = _selEndCol;
        InvalidateVisual();
    }

    // ── Cursor ───────────────────────────────────────────────────────────
    private void MoveCursor(int dLine, int dCol, bool select)
    {
        if (!select) ClearSelection();
        else if (_selStartLine < 0) { _selStartLine = _cursorLine; _selStartCol = _cursorCol; }
        _cursorLine = Math.Clamp(_cursorLine + dLine, 0, _lines.Count - 1);
        if (dCol != 0)
        {
            _cursorCol += dCol;
            if (_cursorCol < 0) { if (_cursorLine > 0) { _cursorLine--; _cursorCol = _lines[_cursorLine].Length; } else _cursorCol = 0; }
            else if (_cursorCol > _lines[_cursorLine].Length) { if (_cursorLine < _lines.Count - 1) { _cursorLine++; _cursorCol = 0; } else _cursorCol = _lines[_cursorLine].Length; }
        }
        else _cursorCol = Math.Clamp(_cursorCol, 0, _lines[_cursorLine].Length);
        if (select) { _selEndLine = _cursorLine; _selEndCol = _cursorCol; }
        EnsureCursorVisible(); CursorMoved?.Invoke(this, CursorPosition); InvalidateVisual();
    }
    private void MoveCursorWordLeft(bool sel)
    {
        if (!sel) ClearSelection(); else if (_selStartLine < 0) { _selStartLine = _cursorLine; _selStartCol = _cursorCol; }
        if (_cursorCol > 0) { var l = _lines[_cursorLine]; int c = _cursorCol - 1; while (c > 0 && !char.IsLetterOrDigit(l[c-1]) && l[c-1]!='_') c--; while (c > 0 && (char.IsLetterOrDigit(l[c-1])||l[c-1]=='_')) c--; _cursorCol = c; }
        else if (_cursorLine > 0) { _cursorLine--; _cursorCol = _lines[_cursorLine].Length; }
        if (sel) { _selEndLine = _cursorLine; _selEndCol = _cursorCol; }
        EnsureCursorVisible(); InvalidateVisual();
    }
    private void MoveCursorWordRight(bool sel)
    {
        if (!sel) ClearSelection(); else if (_selStartLine < 0) { _selStartLine = _cursorLine; _selStartCol = _cursorCol; }
        var l = _lines[_cursorLine];
        if (_cursorCol < l.Length) { int c = _cursorCol; while (c < l.Length && !char.IsLetterOrDigit(l[c]) && l[c]!='_') c++; while (c < l.Length && (char.IsLetterOrDigit(l[c])||l[c]=='_')) c++; _cursorCol = c; }
        else if (_cursorLine < _lines.Count - 1) { _cursorLine++; _cursorCol = 0; }
        if (sel) { _selEndLine = _cursorLine; _selEndCol = _cursorCol; }
        EnsureCursorVisible(); InvalidateVisual();
    }
    private void MoveHome(bool sel, bool ctrl)
    {
        if (!sel) ClearSelection(); else if (_selStartLine < 0) { _selStartLine = _cursorLine; _selStartCol = _cursorCol; }
        if (ctrl) { _cursorLine = 0; _cursorCol = 0; }
        else { int t = _lines[_cursorLine].Length - _lines[_cursorLine].TrimStart().Length; _cursorCol = _cursorCol == t ? 0 : t; }
        if (sel) { _selEndLine = _cursorLine; _selEndCol = _cursorCol; }
        EnsureCursorVisible(); InvalidateVisual();
    }
    private void MoveEnd(bool sel, bool ctrl)
    {
        if (!sel) ClearSelection(); else if (_selStartLine < 0) { _selStartLine = _cursorLine; _selStartCol = _cursorCol; }
        if (ctrl) { _cursorLine = _lines.Count - 1; _cursorCol = _lines[_cursorLine].Length; }
        else _cursorCol = _lines[_cursorLine].Length;
        if (sel) { _selEndLine = _cursorLine; _selEndCol = _cursorCol; }
        EnsureCursorVisible(); InvalidateVisual();
    }
    private void ClearSelection() { _selStartLine = -1; _selStartCol = -1; _selEndLine = -1; _selEndCol = -1; }
    private void ClampCursor() { _cursorLine = Math.Clamp(_cursorLine, 0, _lines.Count - 1); _cursorCol = Math.Clamp(_cursorCol, 0, _lines[_cursorLine].Length); }
    private void EnsureCursorVisible()
    {
        if (_cursorLine < _scrollTop) _scrollTop = _cursorLine;
        if (_cursorLine >= _scrollTop + VisibleLines) _scrollTop = _cursorLine - VisibleLines + 1;
        _scrollTop = Math.Max(0, _scrollTop);
        ScrollChanged?.Invoke(this, EventArgs.Empty); ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Undo / Redo ──────────────────────────────────────────────────────
    private void RecordUndo(int off, string rem, string ins) => UndoRedoMgr.RecordAction(off, rem, ins);
    public void Undo() { var a = UndoRedoMgr.Undo(); if (a == null) return; ApplyDelta(a.Offset, a.InsertedText.Length, a.RemovedText); SetCursorFromOffset(a.Offset + a.RemovedText.Length); }
    public void Redo() { var a = UndoRedoMgr.Redo(); if (a == null) return; ApplyDelta(a.Offset, a.RemovedText.Length, a.InsertedText); SetCursorFromOffset(a.Offset + a.InsertedText.Length); }
    private void ApplyDelta(int off, int remLen, string ins)
    {
        if (off < 0 || off > _fullText.Length) return;
        var sb = new StringBuilder(_fullText);
        int safe = Math.Min(remLen, _fullText.Length - off);
        if (safe > 0) sb.Remove(off, safe);
        sb.Insert(off, ins);
        _fullText = sb.ToString();
        RebuildLines(); ScheduleHighlight();
        TextChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
    }
    internal void SetCursorFromOffset(int offset)
    {
        int o = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (o + _lines[i].Length >= offset) { _cursorLine = i; _cursorCol = offset - o; break; }
            o += _lines[i].Length + 1;
        }
        ClampCursor(); EnsureCursorVisible();
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private void RebuildFullText() { _fullText = string.Join("\n", _lines); ViewportChanged?.Invoke(this, EventArgs.Empty); }
    private void RebuildLines() { _fullText = _fullText.Replace("\r\n", "\n").Replace("\r", "\n"); _lines.Clear(); _lines.AddRange(_fullText.Split('\n')); if (_lines.Count == 0) _lines.Add(string.Empty); ClampCursor(); ViewportChanged?.Invoke(this, EventArgs.Empty); }
    private int OffsetForPos(int line, int col) { int o = 0; for (int i = 0; i < line && i < _lines.Count; i++) o += _lines[i].Length + 1; return o + Math.Min(col, line < _lines.Count ? _lines[line].Length : 0); }
    private int LineOffset(int line) { int o = 0; for (int i = 0; i < line && i < _lines.Count; i++) o += _lines[i].Length + 1; return o; }

    // ══════════════════════════════════════════════════════════════════════
    //  Roslyn / XML Classification (async, debounced)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Whether the current file is a C# source file (eligible for Roslyn).</summary>
    private static bool IsCSharpFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        // Exclude .axaml.cs code-behind from Roslyn Classification issues
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the current file is an AXAML/XAML file (eligible for XAML completion).</summary>
    private static bool IsAxamlFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the current file is XML/XAML.</summary>
    private static bool IsXmlFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xml" or ".axaml" or ".xaml" or ".csproj" or ".fsproj" or ".vbproj"
            or ".nfproj" or ".props" or ".targets" or ".nuspec" or ".config"
            or ".slnx";
    }

    // ── Project context: paths to additional .cs files to include ────────
    private string? _projectDir;
    private List<string>? _projectCsFiles;

    /// <summary>
    /// Sets the project directory so that all .cs files can be loaded into
    /// the Roslyn workspace for full-project intellisense / classification.
    /// </summary>
    public void SetProjectContext(string? projectDir)
    {
        _projectDir = projectDir;
        _projectCsFiles = null; // force re-scan
        _trackedPath = null;    // force workspace rebuild
    }

    private List<string> GetProjectCsFiles()
    {
        if (_projectCsFiles != null) return _projectCsFiles;
        _projectCsFiles = new List<string>();
        if (string.IsNullOrEmpty(_projectDir) || !Directory.Exists(_projectDir))
            return _projectCsFiles;
        try
        {
            var files = Directory.GetFiles(_projectDir, "*.cs", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                // Skip bin/obj/generated
                if (f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                    continue;
                _projectCsFiles.Add(f);
            }
        }
        catch { /* access denied, etc. */ }
        return _projectCsFiles;
    }

    private void ScheduleHighlight()
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        var ct = _highlightCts.Token;
        var text = _fullText;
        var path = FilePath ?? "untitled.cs";

        Task.Delay(120, ct).ContinueWith((Action<Task>)(async _ =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                List<ClassifiedSpan> spans;

                if (IsCSharpFile(path))
                    spans = await ClassifyCSharpAsync(path, text, ct);
                else if (IsXmlFile(path))
                    spans = ClassifyXml(text);
                else
                    spans = new List<ClassifiedSpan>(); // plaintext — no highlighting

                if (ct.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() => { _classifiedSpans = spans; InvalidateVisual(); });
            }
            catch { /* swallow */ }
        }), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    // ── C# classification (Roslyn) ──────────────────────────────────────
    private async Task<List<ClassifiedSpan>> ClassifyCSharpAsync(string path, string text, CancellationToken ct)
    {
        if (_workspace == null) return new();
        try
        {
            if (_trackedPath != path) RebuildWorkspaceDoc(path, text); else UpdateWorkspaceDoc(text);
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc == null) return new();
            var src = await doc.GetTextAsync(ct);
            return (await Classifier.GetClassifiedSpansAsync(doc, TextSpan.FromBounds(0, src.Length), ct)).ToList();
        }
        catch { return new(); }
    }

    // ── XML/XAML classification (regex-based) ───────────────────────────
    // XML classification type names — we reuse Roslyn's names where possible
    // so InsaitEditorColors.GetTokenColor works automatically
    private static readonly Regex _xmlTokenRegex = new(
        @"(<!--[\s\S]*?-->)" +                      // XML comment
        @"|(<!\[CDATA\[[\s\S]*?\]\]>)" +             // CDATA
        @"|(""[^""]*"")" +                           // attribute value in double quotes
        @"|('([^']*)')" +                            // attribute value in single quotes
        @"|(</?[\w:.\-]+)" +                          // tag open  <Name or </Name
        @"|(/?>)" +                                   // tag close  > or />
        @"|([\w:.\-]+)(?=\s*=)",                      // attribute name
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static List<ClassifiedSpan> ClassifyXml(string text)
    {
        var spans = new List<ClassifiedSpan>();
        foreach (Match m in _xmlTokenRegex.Matches(text))
        {
            string type;
            if (m.Groups[1].Success)      // comment
                type = ClassificationTypeNames.Comment;
            else if (m.Groups[2].Success) // CDATA
                type = ClassificationTypeNames.StringLiteral;
            else if (m.Groups[3].Success) // attribute value ""
                type = ClassificationTypeNames.StringLiteral;
            else if (m.Groups[4].Success) // attribute value ''
                type = ClassificationTypeNames.StringLiteral;
            else if (m.Groups[6].Success) // tag name  <Tag or </Tag
                type = ClassificationTypeNames.Keyword;
            else if (m.Groups[7].Success) // > or />
                type = ClassificationTypeNames.Punctuation;
            else if (m.Groups[8].Success) // attribute name
                type = ClassificationTypeNames.PropertyName;
            else
                continue;

            spans.Add(new ClassifiedSpan(type,
                TextSpan.FromBounds(m.Index, m.Index + m.Length)));
        }
        return spans;
    }

    // ── Roslyn workspace for C# ─────────────────────────────────────────
    private void RebuildWorkspaceDoc(string path, string text)
    {
        if (_projectId != null) _workspace!.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(_projectId));

        // Only create a C# Roslyn project for C# files
        if (!IsCSharpFile(path))
        {
            _projectId = null; _documentId = null; _trackedPath = path;
            return;
        }

        var pid = ProjectId.CreateNewId(); var did = DocumentId.CreateNewId(pid);
        var pi = ProjectInfo.Create(pid, VersionStamp.Create(), "InsaitEdit", "InsaitEdit", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest), metadataReferences: _refs);
        var sol = _workspace!.CurrentSolution.AddProject(pi);

        // Add the active document — use full path as name to avoid collisions.
        sol = sol.AddDocument(DocumentInfo.Create(did, path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())), filePath: path));

        // Add other project .cs files as context (for cross-file namespace resolution)
        var contextFiles = GetProjectCsFiles();
        foreach (var csFile in contextFiles)
        {
            if (string.Equals(csFile, path, StringComparison.OrdinalIgnoreCase))
                continue; // already added as the active document
            try
            {
                var auxDid = DocumentId.CreateNewId(pid);
                var auxText = File.ReadAllText(csFile);
                sol = sol.AddDocument(DocumentInfo.Create(auxDid, csFile,
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(auxText), VersionStamp.Create())),
                    filePath: csFile));
            }
            catch { /* skip unreadable files */ }
        }

        _workspace.TryApplyChanges(sol);
        _projectId = pid; _documentId = did; _trackedPath = path;
    }
    private void UpdateWorkspaceDoc(string text)
    {
        if (_documentId == null) return;
        var doc = _workspace!.CurrentSolution.GetDocument(_documentId!);
        if (doc == null) return;
        _workspace.TryApplyChanges(doc.WithText(SourceText.From(text)).Project.Solution);
    }
    private static List<MetadataReference> CollectRefs()
    {
        var refs = new List<MetadataReference>();
        var dir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
        foreach (var n in new[] { "System.Runtime.dll","System.Collections.dll","System.Linq.dll","System.Private.CoreLib.dll","netstandard.dll","System.Threading.Tasks.dll","System.Console.dll","System.IO.dll","System.Text.RegularExpressions.dll" })
        { var p = Path.Combine(dir, n); if (File.Exists(p)) try { refs.Add(MetadataReference.CreateFromFile(p)); } catch { } }
        try { refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location)); } catch { }
        return refs;
    }
}

