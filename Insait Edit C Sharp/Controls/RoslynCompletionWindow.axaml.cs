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
/// Independent purple Window for Roslyn autocompletion.
/// ShowActivated=false so it never steals focus from the editor.
/// All items come from <see cref="RoslynCompletionEngine"/> (real Roslyn factory).
/// private-prefixed items are sorted first.
/// </summary>
public partial class RoslynCompletionWindow : Window
{
    // ── Palette ─────────────────────────────────────────────────────────
    private static readonly Color BgSelected   = Color.Parse("#FF5B3A8A");
    private static readonly Color FgPrimary    = Color.Parse("#FFF0E8F4");
    private static readonly Color FgSelected   = Color.Parse("#FFFDF8FF");
    private static readonly Color FgDim        = Color.Parse("#FF9E90B0");

    // ── AXAML controls ──────────────────────────────────────────────────
    private readonly ListBox   _itemsList;
    private readonly TextBlock _headerText;
    private readonly TextBlock _footerText;

    // ── State ───────────────────────────────────────────────────────────
    private List<RoslynCompletionItem> _items = new();
    private List<Border> _rows = new();
    private bool _closing;

    /// <summary>Fired when an item is committed (click / Enter / Tab).</summary>
    public event EventHandler<RoslynCompletionItem>? ItemCommitted;

    /// <summary>Fired when the user clicks the close button.</summary>
    public event EventHandler? CloseRequested;

    public RoslynCompletionWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _itemsList  = this.FindControl<ListBox>("ItemsList")!;
        _headerText = this.FindControl<TextBlock>("HeaderText")!;
        _footerText = this.FindControl<TextBlock>("FooterText")!;
        Focusable = false;

        var closeBtn = this.FindControl<Button>("CloseCompletionBtn");
        if (closeBtn != null)
            closeBtn.Click += (_, _) => { CloseRequested?.Invoke(this, EventArgs.Empty); SafeClose(); };
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets completion items from Roslyn engine.
    /// Items starting with "private" are sorted first.
    /// </summary>
    public void SetItems(IReadOnlyList<RoslynCompletionItem> items, string? prefix = null)
    {
        // private-first sort
        var sorted = items.OrderBy(i =>
            i.Label.StartsWith("private", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.SortText ?? i.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _items = sorted;
        _footerText.Text = $"{_items.Count} items";
        RebuildRows(prefix);
    }

    public void Refilter(string? prefix)
    {
        if (_closing) return;
        RebuildRows(prefix);
    }

    public bool HasItems => _rows.Count > 0;

    public void SelectNext()     => SelectAt(Math.Min(_itemsList.SelectedIndex + 1, _itemsList.ItemCount - 1));
    public void SelectPrev()     => SelectAt(Math.Max(_itemsList.SelectedIndex - 1, 0));
    public void SelectNextPage() => SelectAt(Math.Min(_itemsList.SelectedIndex + 8, _itemsList.ItemCount - 1));
    public void SelectPrevPage() => SelectAt(Math.Max(_itemsList.SelectedIndex - 8, 0));

    public void CommitSelected()
    {
        if (_closing) return;
        if (_itemsList.SelectedIndex >= 0 && _itemsList.SelectedIndex < _rows.Count)
        {
            if (_rows[_itemsList.SelectedIndex].Tag is RoslynCompletionItem item)
                ItemCommitted?.Invoke(this, item);
        }
    }

    public RoslynCompletionItem? SelectedItem =>
        (_itemsList.SelectedIndex >= 0 && _itemsList.SelectedIndex < _rows.Count &&
         _rows[_itemsList.SelectedIndex].Tag is RoslynCompletionItem r) ? r : null;

    /// <summary>Safe close — prevents double-close crashes.</summary>
    public void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); } catch { }
    }

    // ── Build rows ──────────────────────────────────────────────────────

    private void SelectAt(int idx)
    {
        if (_closing || idx < 0 || idx >= _itemsList.ItemCount) return;
        _itemsList.SelectedIndex = idx;
        UpdateHighlights();
        try { _itemsList.ScrollIntoView(_itemsList.Items[idx]!); } catch { }
    }

    private void RebuildRows(string? prefix)
    {
        if (_closing) return;

        IEnumerable<RoslynCompletionItem> filtered = _items;
        if (!string.IsNullOrEmpty(prefix))
        {
            // Primary filter: show only items that START with the prefix
            var startsWithItems = _items.Where(i =>
                (i.FilterText ?? i.Label).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (startsWithItems.Count > 0)
            {
                filtered = startsWithItems;
            }
            else
            {
                // Fallback: show items that contain the prefix (fuzzy match)
                filtered = _items.Where(i =>
                    (i.FilterText ?? i.Label).Contains(prefix, StringComparison.OrdinalIgnoreCase));
            }
        }

        _rows = filtered.Select(BuildRow).ToList();
        _itemsList.ItemsSource = _rows;
        _footerText.Text = $"{_rows.Count}/{_items.Count}";

        if (_rows.Count == 0) return;

        // Best match
        if (!string.IsNullOrEmpty(prefix))
        {
            int best = -1, bestIdx = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Tag is RoslynCompletionItem ci)
                {
                    int s = Score(ci.FilterText ?? ci.Label, prefix);
                    if (s > best) { best = s; bestIdx = i; }
                }
            }
            _itemsList.SelectedIndex = bestIdx;
        }
        else _itemsList.SelectedIndex = 0;

        UpdateHighlights();
        try { _itemsList.ScrollIntoView(_itemsList.Items[_itemsList.SelectedIndex]!); } catch { }
    }

    private Border BuildRow(RoslynCompletionItem item)
    {
        var iconColor = IconColorFor(item.Kind);
        var letter    = LetterFor(item.Kind);

        var iconBadge = new Border
        {
            Width = 22, Height = 22,
            CornerRadius    = new CornerRadius(4),
            Background      = new SolidColorBrush(Color.FromArgb(38, iconColor.R, iconColor.G, iconColor.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(100, iconColor.R, iconColor.G, iconColor.B)),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = letter, FontSize = 10, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(iconColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            }
        };

        var labelTb = new TextBlock
        {
            Text              = item.Label,
            FontSize          = 13,
            FontFamily        = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground        = new SolidColorBrush(FgPrimary),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var kindTb = new TextBlock
        {
            Text              = item.Kind.ToLowerInvariant(),
            FontSize          = 10,
            Foreground        = new SolidColorBrush(FgDim),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(12, 0, 8, 0),
        };

        var grid = new Grid { Margin = new Thickness(0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(iconBadge, 0);
        Grid.SetColumn(labelTb, 1);
        Grid.SetColumn(kindTb, 2);
        grid.Children.Add(iconBadge);
        grid.Children.Add(labelTb);
        grid.Children.Add(kindTb);

        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding    = new Thickness(0, 3),
            Tag        = item,
            Child      = grid,
        };

        row.PointerEntered += (s, _) =>
        {
            if (_closing) return;
            if (s is Border b) { var i = _rows.IndexOf(b); if (i >= 0) { _itemsList.SelectedIndex = i; UpdateHighlights(); } }
        };

        row.PointerPressed += (s, e) =>
        {
            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            if (_closing) return;
            if (s is Border b) { var i = _rows.IndexOf(b); if (i >= 0) _itemsList.SelectedIndex = i; }
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
            bool sel = (i == _itemsList.SelectedIndex);
            _rows[i].Background = sel
                ? new SolidColorBrush(BgSelected)
                : Brushes.Transparent;
            if (_rows[i].Child is Grid g)
                foreach (var c in g.Children)
                    if (c is TextBlock tb && tb.FontSize > 11)
                        tb.Foreground = new SolidColorBrush(sel ? FgSelected : FgPrimary);
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _itemsList.SelectionChanged += (_, _) => UpdateHighlights();
    }

    // ── Scoring & icons ─────────────────────────────────────────────────
    private static int Score(string c, string p)
    {
        if (c.Equals(p, StringComparison.Ordinal))              return 100;
        if (c.StartsWith(p, StringComparison.Ordinal))           return 80;
        if (c.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return 60;
        if (c.Contains(p, StringComparison.OrdinalIgnoreCase))   return 30;
        return 10;
    }

    private static Color IconColorFor(string kind) => kind switch
    {
        "Class"         => Color.Parse("#FF4EC9B0"),
        "Struct"        => Color.Parse("#FF86C691"),
        "Interface"     => Color.Parse("#FFB8D7A3"),
        "Enum"          => Color.Parse("#FFB5CEA8"),
        "EnumMember"    => Color.Parse("#FFFFE066"),
        "Method"        => Color.Parse("#FFCBA1F4"),
        "Property"      => Color.Parse("#FF9CDCFE"),
        "Field"         => Color.Parse("#FF9CDCFE"),
        "Event"         => Color.Parse("#FFFFE066"),
        "Variable"      => Color.Parse("#FF9CDCFE"),
        "Constant"      => Color.Parse("#FF4FC1FF"),
        "Keyword"       => Color.Parse("#FF569CD6"),
        "Snippet"       => Color.Parse("#FFFF8C00"),
        "Module"        => Color.Parse("#FFD7BA7D"),
        "TypeParameter" => Color.Parse("#FF4EC9B0"),
        "Function"      => Color.Parse("#FFCBA1F4"),
        _               => Color.Parse("#FF787A7E"),
    };

    private static string LetterFor(string kind) => kind switch
    {
        "Class"    => "C",  "Struct"     => "S",  "Interface"    => "I",
        "Enum"     => "E",  "EnumMember" => "e",  "Method"       => "M",
        "Property" => "P",  "Field"      => "F",  "Event"        => "Ev",
        "Variable" => "V",  "Constant"   => "Ct", "Keyword"      => "K",
        "Snippet"  => "sn", "Module"     => "N",  "TypeParameter"=> "T",
        "Function" => "λ",  _            => "·",
    };
}

