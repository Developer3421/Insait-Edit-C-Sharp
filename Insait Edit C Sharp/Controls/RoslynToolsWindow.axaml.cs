using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Independent purple Window for Roslyn tools:
///  • Go to Definition (F12)
///  • Find References (Shift+F12)
///  • Hover / Quick Info (Ctrl+Shift+H)
///  • Rename Symbol (F2)
/// </summary>
public partial class RoslynToolsWindow : Window
{
    private static readonly Color FgPri   = Color.Parse("#FFF0E8F4");
    private static readonly Color FgDim   = Color.Parse("#FF9E90B0");
    private static readonly Color FgGreen = Color.Parse("#FFA6E3A1");

    private readonly List<NavigationEntry> _entries = new();

    /// <summary>Fired when user double-clicks a location to navigate.</summary>
    public event EventHandler<NavigationEntry>? NavigateRequested;

    public RoslynToolsWindow()
    {
        InitializeComponent();
        ResultsList.DoubleTapped += OnItemDoubleTapped;
        ResultsList.KeyDown += OnListKeyDown;
    }

    // ── Show Definition results ─────────────────────────────────────────

    public void ShowDefinition(DefinitionResult result)
    {
        TitleText.Text = "📍 Go to Definition";
        SymbolText.Text = result.Symbol;
        SymbolKindText.Text = $"({result.Kind})";
        _entries.Clear();

        foreach (var loc in result.Locations)
            _entries.Add(new NavigationEntry
            {
                FilePath = loc.FilePath, Line = loc.StartLine, Column = loc.StartColumn,
                IsMetadata = loc.IsMetadata,
                DisplayText = loc.IsMetadata
                    ? $"📦  {loc.MetadataDisplayName ?? loc.FilePath}"
                    : $"📄  {Path.GetFileName(loc.FilePath)} — Ln {loc.StartLine}, Col {loc.StartColumn}",
            });

        RebuildList();
        StatusText.Text = $"{_entries.Count} location(s)";
    }

    // ── Show References results ─────────────────────────────────────────

    public void ShowReferences(string symbol, List<ReferenceInfo> refs)
    {
        TitleText.Text = "🔎 Find References";
        SymbolText.Text = symbol;
        SymbolKindText.Text = $"{refs.Count} reference(s)";
        _entries.Clear();

        foreach (var r in refs)
            _entries.Add(new NavigationEntry
            {
                FilePath = r.FilePath, Line = r.Line, Column = r.Column,
                DisplayText = $"{(r.IsDefinition ? "✦ " : "  ")}{Path.GetFileName(r.FilePath)} — Ln {r.Line}, Col {r.Column}",
            });

        RebuildList();
        StatusText.Text = $"{_entries.Count} reference(s)";
    }

    // ── Show Hover / Quick Info ─────────────────────────────────────────

    public void ShowQuickInfo(QuickInfoResult info)
    {
        TitleText.Text = "ℹ Quick Info";
        SymbolText.Text = info.Sections.FirstOrDefault()?.Text ?? "";
        SymbolKindText.Text = "";
        _entries.Clear();

        foreach (var s in info.Sections.Skip(1))
            _entries.Add(new NavigationEntry { DisplayText = s.Text, IsInfo = true });

        RebuildList();
        StatusText.Text = $"{info.Sections.Count} section(s)";
    }

    // ── List building ───────────────────────────────────────────────────

    private void RebuildList()
    {
        ResultsList.Items.Clear();
        foreach (var entry in _entries)
        {
            var fg = entry.IsMetadata ? FgDim : entry.IsInfo ? FgPri : FgGreen;
            var tb = new TextBlock
            {
                Text = entry.DisplayText,
                FontSize = 12,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(fg),
                Padding = new Thickness(10, 5),
                TextWrapping = TextWrapping.Wrap,
                Tag = entry,
            };
            ResultsList.Items.Add(tb);
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        TryNavigateSelected();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            TryNavigateSelected();
            e.Handled = true;
        }
    }

    private void TryNavigateSelected()
    {
        if (ResultsList.SelectedItem is TextBlock tb && tb.Tag is NavigationEntry entry
            && !entry.IsMetadata && !entry.IsInfo && !string.IsNullOrEmpty(entry.FilePath))
        {
            NavigateRequested?.Invoke(this, entry);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

/// <summary>A navigable location entry.</summary>
public sealed class NavigationEntry
{
    public string FilePath    { get; init; } = string.Empty;
    public int    Line        { get; init; }
    public int    Column      { get; init; }
    public bool   IsMetadata  { get; init; }
    public bool   IsInfo      { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

