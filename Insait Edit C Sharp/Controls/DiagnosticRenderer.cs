using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Draws JetBrains Rider-style squiggly underlines for diagnostics.
/// Pure Avalonia — no AvaloniaEdit dependency.
///
/// Two usage modes:
///   1. Owned by a <see cref="Control"/>: call <see cref="SetOwner"/> once,
///      then <see cref="SetDiagnostics"/>. The control will be invalidated
///      automatically so its <c>Render</c> can delegate to <see cref="DrawLineUnderlines"/>.
///   2. Static helpers: call <see cref="DrawSquigglyLine"/> or
///      <see cref="DrawDiagnosticUnderline"/> directly from any
///      <c>Render</c>/<c>Draw</c> override.
/// </summary>
public sealed class DiagnosticRenderer
{
    private Control? _owner;
    private List<DiagnosticSpan> _spans = new();

    // ── Layout parameters (set by owner) ─────────────────────────────────
    private double _charWidth  = 8.0;
    private double _lineHeight = 18.0;
    private double _gutterWidth;
    private int    _scrollLeft;
    private int    _scrollTop;

    // Colours matching JetBrains Rider dark theme
    public static readonly Color ErrorColor   = Color.Parse("#FFF38BA8");   // red
    public static readonly Color WarningColor = Color.Parse("#FFF5A623");   // amber
    public static readonly Color InfoColor    = Color.Parse("#FF89B4FA");   // blue
    public static readonly Color HintColor    = Color.Parse("#FFA6E3A1");   // green

    /// <summary>Parameterless constructor for standalone / static usage.</summary>
    public DiagnosticRenderer() { }

    /// <summary>Bind to a control so <see cref="SetDiagnostics"/> can invalidate it.</summary>
    public void SetOwner(Control owner) => _owner = owner;

    /// <summary>
    /// Update layout metrics used to map offsets → screen coordinates.
    /// Call this whenever font size, gutter width, or scroll position changes.
    /// </summary>
    public void UpdateMetrics(double charWidth, double lineHeight,
                              double gutterWidth, int scrollLeft, int scrollTop)
    {
        _charWidth  = charWidth;
        _lineHeight = lineHeight;
        _gutterWidth = gutterWidth;
        _scrollLeft = scrollLeft;
        _scrollTop  = scrollTop;
    }

    /// <summary>Push fresh diagnostics for this file.</summary>
    public void SetDiagnostics(IEnumerable<DiagnosticSpan> spans)
    {
        _spans = spans.ToList();
        _owner?.InvalidateVisual();
    }

    public void ClearDiagnostics()
    {
        _spans.Clear();
        _owner?.InvalidateVisual();
    }

    public IReadOnlyList<DiagnosticSpan> Spans => _spans;

    // ═══════════════════════════════════════════════════════════════════════
    //  Instance drawing — renders all current diagnostics for a given line
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw diagnostic squiggly underlines for a specific line.
    /// Call this from the owner control's <c>Render</c> override, once per
    /// visible line.
    /// </summary>
    /// <param name="ctx">Drawing context.</param>
    /// <param name="lineIndex">0-based line index in the document.</param>
    /// <param name="lineText">The text content of the line.</param>
    /// <param name="lineOffset">Character offset of the line start in the full text.</param>
    /// <param name="y">Y coordinate of the line top on screen.</param>
    public void DrawLineUnderlines(DrawingContext ctx, int lineIndex,
                                   string lineText, int lineOffset, double y)
    {
        if (_spans.Count == 0) return;
        int lineEnd = lineOffset + lineText.Length;

        foreach (var d in _spans)
        {
            if (d.StartOffset >= lineEnd || d.EndOffset <= lineOffset)
                continue;

            int sc = Math.Max(d.StartOffset - lineOffset, 0);
            int ec = Math.Min(d.EndOffset - lineOffset, lineText.Length);
            if (sc >= ec) continue;

            double x1 = _gutterWidth + (sc - _scrollLeft) * _charWidth;
            double x2 = _gutterWidth + (ec - _scrollLeft) * _charWidth;

            var color = SeverityToColor(d.Severity);
            DrawSquigglyLine(ctx, x1, x2, y + _lineHeight - 3, color);
        }
    }

    /// <summary>
    /// Draw all diagnostics whose screen rects fall within the given visible
    /// area. Requires line data to map offsets to line/column positions.
    /// </summary>
    /// <param name="ctx">Drawing context.</param>
    /// <param name="lines">All lines in the document.</param>
    /// <param name="lineOffsets">Cumulative character offset of each line start.</param>
    /// <param name="firstVisibleLine">0-based index of the first visible line.</param>
    /// <param name="lastVisibleLine">0-based index of the last visible line.</param>
    public void DrawVisibleDiagnostics(DrawingContext ctx,
                                       IReadOnlyList<string> lines,
                                       IReadOnlyList<int> lineOffsets,
                                       int firstVisibleLine,
                                       int lastVisibleLine)
    {
        if (_spans.Count == 0 || lines.Count == 0) return;

        int first = Math.Max(0, firstVisibleLine);
        int last  = Math.Min(lines.Count - 1, lastVisibleLine);

        for (int li = first; li <= last; li++)
        {
            int off  = lineOffsets[li];
            double y = (li - _scrollTop) * _lineHeight;
            DrawLineUnderlines(ctx, li, lines[li], off, y);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Static drawing helpers — usable from any Render override
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draw a single diagnostic squiggly underline from screen position
    /// (<paramref name="x1"/>) to (<paramref name="x2"/>) at vertical
    /// position <paramref name="y"/>.
    /// </summary>
    public static void DrawSquigglyLine(DrawingContext ctx,
                                        double x1, double x2, double y,
                                        Color color,
                                        double amplitude = 2.0,
                                        double frequency = 6.0,
                                        double thickness = 1.5)
    {
        if (x2 <= x1) return;

        var pen = new Pen(new SolidColorBrush(color), thickness);
        var geo = new StreamGeometry();
        using var gctx = geo.Open();
        bool first = true;
        for (double x = x1; x <= x2; x += 1.0)
        {
            double yy = y + Math.Sin((x - x1) / frequency * Math.PI) * amplitude;
            if (first) { gctx.BeginFigure(new Point(x, yy), false); first = false; }
            else gctx.LineTo(new Point(x, yy));
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    /// <summary>
    /// Draw a diagnostic underline inside a given rectangle
    /// (e.g. the bounding box of a text segment).
    /// The squiggle is drawn at the bottom edge of the rect.
    /// </summary>
    public static void DrawDiagnosticUnderline(DrawingContext ctx, Rect rect,
                                               Color color,
                                               double thickness = 1.2)
    {
        double x   = rect.Left;
        double y   = rect.Bottom - 1.5;
        double w   = rect.Width;
        const double amp = 1.5;
        const double per = 4.0;

        if (w <= 0) return;

        var pen  = new Pen(new SolidColorBrush(color), thickness);
        var geom = new StreamGeometry();
        using (var gctx = geom.Open())
        {
            gctx.BeginFigure(new Point(x, y), false);
            bool up = false;
            for (double xi = 0; xi < w; xi += per / 2)
            {
                double px = Math.Min(x + xi + per / 2, x + w);
                double py = up ? y - amp : y + amp;
                gctx.QuadraticBezierTo(new Point(x + xi + per / 4, py),
                                       new Point(px, y));
                up = !up;
            }
            gctx.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geom);
    }

    /// <summary>
    /// Draw a straight dotted underline (for hints / info level diagnostics).
    /// </summary>
    public static void DrawDottedUnderline(DrawingContext ctx,
                                           double x1, double x2, double y,
                                           Color color, double thickness = 1.0)
    {
        if (x2 <= x1) return;
        var pen = new Pen(new SolidColorBrush(color), thickness)
        {
            DashStyle = DashStyle.Dot,
        };
        ctx.DrawLine(pen, new Point(x1, y), new Point(x2, y));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Utility
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Map severity to the standard colour.</summary>
    public static Color SeverityToColor(DiagnosticSeverityKind severity)
        => severity switch
        {
            DiagnosticSeverityKind.Error   => ErrorColor,
            DiagnosticSeverityKind.Warning => WarningColor,
            DiagnosticSeverityKind.Info    => InfoColor,
            _                              => HintColor,
        };

    /// <summary>Map severity to an icon string.</summary>
    public static string SeverityToIcon(DiagnosticSeverityKind severity)
        => severity switch
        {
            DiagnosticSeverityKind.Error   => "●",
            DiagnosticSeverityKind.Warning => "▲",
            DiagnosticSeverityKind.Info    => "◆",
            _                              => "○",
        };
}

/// <summary>
/// Represents a single diagnostic range in the source text.
/// </summary>
public sealed class DiagnosticSpan
{
    public int                   StartOffset { get; set; }
    public int                   EndOffset   { get; set; }
    public int                   Line        { get; set; }
    public int                   Column      { get; set; }
    public string                Message     { get; set; } = string.Empty;
    public string                Code        { get; set; } = string.Empty;
    public DiagnosticSeverityKind Severity   { get; set; }
    public List<QuickFixSuggestion> Fixes    { get; set; } = new();
}

public enum DiagnosticSeverityKind
{
    Error,
    Warning,
    Info,
    Hint
}
