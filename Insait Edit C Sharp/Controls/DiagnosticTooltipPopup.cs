using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// JetBrains Rider-style tooltip popup that shows error message and quick-fix actions.
/// </summary>
public sealed class DiagnosticTooltipPopup : Popup
{
    private static readonly Color BgColor    = Color.Parse("#FF1E2030");
    private static readonly Color BdColor    = Color.Parse("#FF3E4257");
    private static readonly Color ErrorFg    = Color.Parse("#FFF38BA8");
    private static readonly Color WarningFg  = Color.Parse("#FFF5A623");
    private static readonly Color InfoFg     = Color.Parse("#FF89B4FA");
    private static readonly Color HintFg     = Color.Parse("#FFA6E3A1");
    private static readonly Color TextFg     = Color.Parse("#FFCDD6F4");
    private static readonly Color DimFg      = Color.Parse("#FF7F849C");
    private static readonly Color FixHover   = Color.Parse("#FF45475A");
    private static readonly Color CodeFg     = Color.Parse("#FF89DCEB");

    public event EventHandler<QuickFixEventArgs>? FixRequested;

    public DiagnosticTooltipPopup()
    {
        IsLightDismissEnabled = true;
        Placement             = PlacementMode.Pointer;
    }

    public void ShowForDiagnostic(DiagnosticSpan span, Visual relativeTo)
    {
        PlacementTarget = relativeTo as Control;
        Child           = BuildContent(span);
        IsOpen          = true;
    }

    private Border BuildContent(DiagnosticSpan span)
    {
        var stack = new StackPanel { Spacing = 0 };

        var (icon, fg) = span.Severity switch
        {
            DiagnosticSeverityKind.Error   => ("⛔ ", ErrorFg),
            DiagnosticSeverityKind.Warning => ("⚠ ", WarningFg),
            DiagnosticSeverityKind.Info    => ("ℹ ", InfoFg),
            _                              => ("💡 ", HintFg),
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
            Margin      = new Thickness(10, 8, 10, 4),
        };
        header.Children.Add(new TextBlock
        {
            Text              = icon,
            FontSize          = 13,
            Foreground        = new SolidColorBrush(fg),
            VerticalAlignment = VerticalAlignment.Top,
        });

        var msgStack = new StackPanel { Spacing = 2 };
        msgStack.Children.Add(new TextBlock
        {
            Text         = span.Message,
            FontSize     = 12,
            FontFamily   = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground   = new SolidColorBrush(TextFg),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 460,
        });
        if (!string.IsNullOrEmpty(span.Code))
            msgStack.Children.Add(new TextBlock
            {
                Text       = span.Code,
                FontSize   = 10,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(CodeFg),
            });

        header.Children.Add(msgStack);
        stack.Children.Add(header);

        if (span.Fixes.Count > 0)
        {
            stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(BdColor), Margin = new Thickness(0, 2, 0, 0) });
            stack.Children.Add(new TextBlock
            {
                Text       = "Quick Fixes:",
                FontSize   = 10,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(DimFg),
                Margin     = new Thickness(10, 4, 10, 2),
            });
            foreach (var fix in span.Fixes)
                stack.Children.Add(BuildFixRow(fix, span));
        }

        stack.Children.Add(new TextBlock
        {
            Text       = $"Line {span.Line}, Col {span.Column}",
            FontSize   = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(DimFg),
            Margin     = new Thickness(10, 4, 10, 6),
        });

        return new Border
        {
            Background      = new SolidColorBrush(BgColor),
            BorderBrush     = new SolidColorBrush(BdColor),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Child           = stack,
            MaxWidth        = 520,
        };
    }

    private Border BuildFixRow(QuickFixSuggestion fix, DiagnosticSpan span)
    {
        var fixIcon = fix.Kind switch
        {
            QuickFixKind.AddUsing     => "→ ",
            QuickFixKind.InstallNuGet => "📦 ",
            QuickFixKind.InsertCode   => "✏ ",
            QuickFixKind.RemoveCode   => "✂ ",
            _                         => "⚡ ",
        };

        var text = new TextBlock
        {
            Text       = fixIcon + fix.Title,
            FontSize   = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(TextFg),
            Padding    = new Thickness(10, 3, 10, 3),
        };
        var row = new Border { Child = text, Cursor = new Cursor(StandardCursorType.Hand) };

        row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(FixHover);
        row.PointerExited  += (_, _) => row.Background = null;
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                IsOpen = false;
                FixRequested?.Invoke(this, new QuickFixEventArgs(fix, span));
                e.Handled = true;
            }
        };
        return row;
    }
}

public sealed class QuickFixEventArgs : EventArgs
{
    public QuickFixSuggestion Fix  { get; }
    public DiagnosticSpan     Span { get; }
    public QuickFixEventArgs(QuickFixSuggestion fix, DiagnosticSpan span)
    {
        Fix  = fix;
        Span = span;
    }
}

