using System;
using System.Linq;
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
    private static readonly Color BgColor    = Color.Parse("#FF1F1A24");
    private static readonly Color BdColor    = Color.Parse("#FF6C3FAA");
    private static readonly Color ErrorFg    = Color.Parse("#FFF38BA8");
    private static readonly Color WarningFg  = Color.Parse("#FFF5A623");
    private static readonly Color InfoFg     = Color.Parse("#FF89B4FA");
    private static readonly Color HintFg     = Color.Parse("#FFA6E3A1");
    private static readonly Color TextFg     = Color.Parse("#FFF0E8F4");
    private static readonly Color DimFg      = Color.Parse("#FF9E90B0");
    private static readonly Color FixHover   = Color.Parse("#FF3E3050");
    private static readonly Color CodeFg     = Color.Parse("#FFDCC4FF");
    private static readonly Color NuGetAccent = Color.Parse("#FFDCC4FF");
    private static readonly Color NuGetBg     = Color.Parse("#20DCC4FF");

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

        // ── NuGet / Using resolvable section ────────────────────────────
        // Prominently show "Install package" / "Add using" for diagnostics
        // that Roslyn can resolve from a known NuGet package.
        if (span.HasResolvablePackageFix)
        {
            var nugetFixes = span.Fixes.Where(f =>
                f.Kind == QuickFixKind.InstallNuGet || f.Kind == QuickFixKind.AddUsing).ToList();

            if (nugetFixes.Count > 0)
            {
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(NuGetAccent) { Opacity = 0.3 },
                    Margin = new Thickness(0, 4, 0, 0),
                });

                var nugetHeader = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing     = 6,
                    Margin      = new Thickness(10, 4, 10, 2),
                };
                nugetHeader.Children.Add(new TextBlock
                {
                    Text       = "📦",
                    FontSize   = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                nugetHeader.Children.Add(new TextBlock
                {
                    Text       = LocalizationService.Get("Diag.ResolvablePackage"),
                    FontSize   = 10,
                    FontWeight = FontWeight.SemiBold,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    Foreground = new SolidColorBrush(NuGetAccent),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                stack.Children.Add(nugetHeader);

                foreach (var nugetFix in nugetFixes)
                {
                    var nugetRow = BuildNuGetFixRow(nugetFix, span);
                    stack.Children.Add(nugetRow);
                }
            }
        }

        if (span.Fixes.Count > 0)
        {
            stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(BdColor), Margin = new Thickness(0, 2, 0, 0) });
            stack.Children.Add(new TextBlock
            {
                Text       = LocalizationService.Get("Diag.QuickFixes"),
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
            Text       = string.Format(LocalizationService.Get("Diag.LineCol"), span.Line, span.Column),
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
            QuickFixKind.AddUsing       => "→ ",
            QuickFixKind.InstallNuGet   => "📦 ",
            QuickFixKind.InsertCode     => "✏ ",
            QuickFixKind.RemoveCode     => "✂ ",
            QuickFixKind.GenerateType   => "⚡ ",
            QuickFixKind.GenerateMember => "🔧 ",
            _                           => "💡 ",
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

    /// <summary>
    /// Builds a prominent NuGet fix row with accent background — visually
    /// distinct from generic fixes so the user immediately sees the "install package" option.
    /// </summary>
    private Border BuildNuGetFixRow(QuickFixSuggestion fix, DiagnosticSpan span)
    {
        var icon = fix.Kind == QuickFixKind.InstallNuGet ? "⬇ " : "→ ";

        var contentStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
        };
        contentStack.Children.Add(new TextBlock
        {
            Text              = icon,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        contentStack.Children.Add(new TextBlock
        {
            Text         = fix.Title,
            FontSize     = 12,
            FontFamily   = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground   = new SolidColorBrush(TextFg),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 420,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var row = new Border
        {
            Child           = contentStack,
            Padding         = new Thickness(10, 5, 10, 5),
            Margin          = new Thickness(6, 1, 6, 1),
            CornerRadius    = new CornerRadius(4),
            Background      = new SolidColorBrush(NuGetBg),
            BorderBrush     = new SolidColorBrush(NuGetAccent) { Opacity = 0.4 },
            BorderThickness = new Thickness(1),
            Cursor          = new Cursor(StandardCursorType.Hand),
        };

        row.PointerEntered += (_, _) =>
        {
            row.Background  = new SolidColorBrush(NuGetAccent) { Opacity = 0.15 };
            row.BorderBrush = new SolidColorBrush(NuGetAccent) { Opacity = 0.7 };
        };
        row.PointerExited += (_, _) =>
        {
            row.Background  = new SolidColorBrush(NuGetBg);
            row.BorderBrush = new SolidColorBrush(NuGetAccent) { Opacity = 0.4 };
        };
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

