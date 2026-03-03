using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Insait_Edit_C_Sharp.ViewModels;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Diagnostics panel — displays errors, warnings, and info from Roslyn analysis.
/// Clicking an item fires NavigateToDiagnostic for the editor to navigate to.
/// </summary>
public partial class DiagnosticsPanel : UserControl
{
    private TextBlock _errorCountText = null!;
    private TextBlock _warningCountText = null!;
    private TextBlock _infoCountText = null!;
    private ListBox _diagList = null!;

    private readonly ObservableCollection<DiagnosticItem> _items = new();

    /// <summary>Fired when user clicks a diagnostic — navigate to file:line:col.</summary>
    public event EventHandler<DiagnosticNavigationEventArgs>? NavigateToDiagnostic;

    /// <summary>Fired when refresh is requested.</summary>
    public event EventHandler? RefreshRequested;

    public DiagnosticsPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _errorCountText = this.FindControl<TextBlock>("ErrorCountText")!;
        _warningCountText = this.FindControl<TextBlock>("WarningCountText")!;
        _infoCountText = this.FindControl<TextBlock>("InfoCountText")!;
        _diagList = this.FindControl<ListBox>("DiagList")!;
    }

    /// <summary>
    /// Sets the diagnostics to display.
    /// </summary>
    public void SetDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        _items.Clear();
        foreach (var d in diagnostics)
            _items.Add(d);

        RebuildList();
        UpdateCounts();
    }

    /// <summary>
    /// Adds diagnostics from inline analysis.
    /// </summary>
    public void AddFromDiagnosticSpans(string filePath, IEnumerable<DiagnosticSpan> spans)
    {
        // Remove old entries for this file
        var toRemove = _items.Where(i =>
            string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var r in toRemove) _items.Remove(r);

        // Add new ones
        foreach (var span in spans)
        {
            _items.Add(new DiagnosticItem
            {
                FilePath = filePath,
                FileName = System.IO.Path.GetFileName(filePath),
                Line = span.Line,
                Column = span.Column,
                Code = span.Code,
                Message = span.Message,
                Severity = span.Severity switch
                {
                    DiagnosticSeverityKind.Error => DiagnosticSeverity.Error,
                    DiagnosticSeverityKind.Warning => DiagnosticSeverity.Warning,
                    DiagnosticSeverityKind.Info => DiagnosticSeverity.Info,
                    _ => DiagnosticSeverity.Hint,
                }
            });
        }

        RebuildList();
        UpdateCounts();
    }

    public void Clear()
    {
        _items.Clear();
        _diagList.Items.Clear();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        _errorCountText.Text = _items.Count(i => i.Severity == DiagnosticSeverity.Error).ToString();
        _warningCountText.Text = _items.Count(i => i.Severity == DiagnosticSeverity.Warning).ToString();
        _infoCountText.Text = _items.Count(i => i.Severity == DiagnosticSeverity.Info ||
                                                  i.Severity == DiagnosticSeverity.Hint).ToString();
    }

    private void RebuildList()
    {
        _diagList.Items.Clear();

        // Sort: errors first, then warnings, then info
        var sorted = _items
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.FileName)
            .ThenBy(d => d.Line);

        foreach (var item in sorted)
        {
            _diagList.Items.Add(BuildRow(item));
        }
    }

    private Border BuildRow(DiagnosticItem item)
    {
        var severityColor = item.Severity switch
        {
            DiagnosticSeverity.Error => Color.Parse("#FFF38BA8"),
            DiagnosticSeverity.Warning => Color.Parse("#FFF5A623"),
            DiagnosticSeverity.Info => Color.Parse("#FF89B4FA"),
            _ => Color.Parse("#FFA6E3A1"),
        };

        var icon = new TextBlock
        {
            Text = item.SeverityIcon,
            FontSize = 12,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var code = new TextBlock
        {
            Text = item.Code,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(severityColor),
            Width = 60,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
        };

        var message = new TextBlock
        {
            Text = item.Message,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var location = new TextBlock
        {
            Text = item.Location,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var grid = new Grid { Margin = new Thickness(4, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(code, 1);
        Grid.SetColumn(message, 2);
        Grid.SetColumn(location, 3);
        grid.Children.Add(icon);
        grid.Children.Add(code);
        grid.Children.Add(message);
        grid.Children.Add(location);

        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 4),
            Tag = item,
            Child = grid,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };

        row.PointerPressed += (s, e) =>
        {
            if (s is Border b && b.Tag is DiagnosticItem di)
            {
                NavigateToDiagnostic?.Invoke(this, new DiagnosticNavigationEventArgs(
                    di.FilePath, di.Line, di.Column));
            }
        };

        return row;
    }

    private void OnDiagSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Navigation handled via PointerPressed on rows
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        Clear();
    }
}

/// <summary>Event args for diagnostic navigation.</summary>
public sealed class DiagnosticNavigationEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }

    public DiagnosticNavigationEventArgs(string filePath, int line, int column)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
    }
}

