using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Independent purple Window for Roslyn quick-fix suggestions.
/// ShowActivated=false — never steals focus from editor.
/// </summary>
public partial class RoslynQuickFixWindow : Window
{
    private static readonly Color BgSel = Color.Parse("#FF5B3A8A");
    private static readonly Color FgPri = Color.Parse("#FFF0E8F4");

    private readonly TextBlock _headerText;
    private readonly ListBox   _fixList;

    private List<QuickFixSuggestion> _fixes = new();
    private List<Border> _rows = new();
    private bool _closing;

    /// <summary>Fired when a fix is chosen.</summary>
    public event EventHandler<QuickFixSuggestion>? FixChosen;

    public RoslynQuickFixWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _headerText = this.FindControl<TextBlock>("HeaderText")!;
        _fixList    = this.FindControl<ListBox>("FixList")!;
        Focusable = false;
    }

    public void SetFixes(IEnumerable<QuickFixSuggestion> fixes, string diagnosticCode = "")
    {
        _fixes = fixes.ToList();
        _headerText.Text = string.IsNullOrEmpty(diagnosticCode)
            ? "Quick Fix" : $"Quick Fix — {diagnosticCode}";
        RebuildRows();
    }

    public bool HasItems => _rows.Count > 0;
    public void SelectNext() => SelectAt(Math.Min(_fixList.SelectedIndex + 1, _fixList.ItemCount - 1));
    public void SelectPrev() => SelectAt(Math.Max(_fixList.SelectedIndex - 1, 0));

    public void CommitSelected()
    {
        if (_closing) return;
        if (_fixList.SelectedIndex >= 0 && _fixList.SelectedIndex < _rows.Count &&
            _rows[_fixList.SelectedIndex].Tag is QuickFixSuggestion fix)
            FixChosen?.Invoke(this, fix);
    }

    public void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); } catch { }
    }

    private void SelectAt(int idx)
    {
        if (_closing || idx < 0 || idx >= _fixList.ItemCount) return;
        _fixList.SelectedIndex = idx;
        UpdateHighlights();
        try { _fixList.ScrollIntoView(_fixList.Items[idx]!); } catch { }
    }

    private void RebuildRows()
    {
        _rows = _fixes.Select(BuildRow).ToList();
        _fixList.ItemsSource = _rows;
        if (_rows.Count > 0) { _fixList.SelectedIndex = 0; UpdateHighlights(); }
    }

    private Border BuildRow(QuickFixSuggestion fix)
    {
        var icon = fix.Kind switch
        {
            QuickFixKind.AddUsing     => "📦",
            QuickFixKind.InstallNuGet => "⬇",
            QuickFixKind.InsertCode   => "✏",
            QuickFixKind.RemoveCode   => "🗑",
            QuickFixKind.RoslynFix    => "🔧",
            _                         => "💡",
        };

        var iconTb = new TextBlock
        {
            Text = icon, FontSize = 14, Width = 26,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
        };

        var titleTb = new TextBlock
        {
            Text = fix.Title, FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(FgPri),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetColumn(iconTb, 0);
        Grid.SetColumn(titleTb, 1);
        grid.Children.Add(iconTb);
        grid.Children.Add(titleTb);

        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 4),
            Tag = fix,
            Child = grid,
        };

        row.PointerEntered += (s, _) =>
        {
            if (_closing || s is not Border b) return;
            var i = _rows.IndexOf(b);
            if (i >= 0) { _fixList.SelectedIndex = i; UpdateHighlights(); }
        };

        row.PointerPressed += (s, e) =>
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed || _closing) return;
            if (s is Border b) { var i = _rows.IndexOf(b); if (i >= 0) _fixList.SelectedIndex = i; }
            CommitSelected();
            e.Handled = true;
        };

        return row;
    }

    private void UpdateHighlights()
    {
        if (_closing) return;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool sel = i == _fixList.SelectedIndex;
            _rows[i].Background = sel ? new SolidColorBrush(BgSel) : Brushes.Transparent;
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _fixList.SelectionChanged += (_, _) => UpdateHighlights();
    }
}

