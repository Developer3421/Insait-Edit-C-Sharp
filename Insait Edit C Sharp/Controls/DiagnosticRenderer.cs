using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Draws JetBrains Rider-style squiggly underlines for diagnostics in the editor.
/// Red = Error, Yellow = Warning, Blue = Info/Hint
/// </summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private List<DiagnosticSpan> _spans = new();

    // Colours matching JetBrains Rider dark theme
    private static readonly Color ErrorColor   = Color.Parse("#FFF38BA8");   // red
    private static readonly Color WarningColor = Color.Parse("#FFF5A623");   // amber
    private static readonly Color InfoColor    = Color.Parse("#FF89B4FA");   // blue
    private static readonly Color HintColor    = Color.Parse("#FFA6E3A1");   // green

    public DiagnosticRenderer(TextView textView)
    {
        _textView = textView;
    }

    /// <summary>Called by AvaloniaEditor to push fresh diagnostics for this file.</summary>
    public void SetDiagnostics(IEnumerable<DiagnosticSpan> spans)
    {
        _spans = spans.ToList();
        _textView.InvalidateLayer(KnownLayer.Background);
    }

    public void ClearDiagnostics()
    {
        _spans.Clear();
        _textView.InvalidateLayer(KnownLayer.Background);
    }

    // Must be ABOVE the selection layer so lines are visible but BELOW the caret layer
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_spans.Count == 0) return;
        var document = textView.Document;
        if (document == null) return;

        foreach (var span in _spans)
        {
            try
            {
                // Clamp to document bounds
                var startOff = Math.Clamp(span.StartOffset, 0, document.TextLength);
                var endOff   = Math.Clamp(span.EndOffset,   startOff, document.TextLength);
                if (startOff >= endOff) continue;

                var color = span.Severity switch
                {
                    DiagnosticSeverityKind.Error   => ErrorColor,
                    DiagnosticSeverityKind.Warning => WarningColor,
                    DiagnosticSeverityKind.Info    => InfoColor,
                    _                              => HintColor,
                };

                // Use BackgroundGeometryBuilder to get the screen rects for the span
                var builder = new BackgroundGeometryBuilder
                {
                    AlignToWholePixels = true,
                };
                builder.AddSegment(textView, new AvaloniaEdit.Document.TextSegment
                {
                    StartOffset = startOff,
                    EndOffset   = endOff,
                });
                var geo = builder.CreateGeometry();
                if (geo == null) continue;

                // Draw squiggly line at bottom of each rect
                var bounds = geo.Bounds;
                DrawSquigglyInRect(drawingContext, bounds, color);
            }
            catch { /* defensive */ }
        }
    }

    private static void DrawSquigglyInRect(DrawingContext dc, Rect rect, Color color)
    {
        var brush  = new SolidColorBrush(color);
        var pen    = new Pen(brush, 1.2);
        double x   = rect.Left;
        double y   = rect.Bottom - 1.5;
        double w   = rect.Width;
        double amp = 1.5;
        double per = 4.0;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(x, y), false);
            bool up = false;
            for (double xi = 0; xi < w; xi += per / 2)
            {
                double px = Math.Min(x + xi + per / 2, x + w);
                double py = up ? y - amp : y + amp;
                ctx.QuadraticBezierTo(new Point(x + xi + per / 4, py), new Point(px, y));
                up = !up;
            }
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, geom);
    }
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
