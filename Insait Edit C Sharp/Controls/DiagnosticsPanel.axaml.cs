using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Insait_Edit_C_Sharp.ViewModels;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Diagnostics panel — displays errors, warnings, and info from Roslyn analysis.
/// Shows "All Errors" and "Current File" tabs. Errors are easily copyable.
/// </summary>
public partial class DiagnosticsPanel : UserControl
{
    private TextBlock _errorCountText = null!;
    private TextBlock _warningCountText = null!;
    private TextBlock _infoCountText = null!;
    private ListBox _diagList = null!;
    private Button _tabAll = null!;
    private Button _tabCurrentFile = null!;
    private TextBlock _currentFileLabel = null!;

    private readonly ObservableCollection<DiagnosticItem> _items = new();

    /// <summary>Current active tab: true = All, false = Current File</summary>
    private bool _showAll = true;

    /// <summary>Path of the currently open file in the editor.</summary>
    private string? _currentFilePath;

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
        _errorCountText   = this.FindControl<TextBlock>("ErrorCountText")!;
        _warningCountText = this.FindControl<TextBlock>("WarningCountText")!;
        _infoCountText    = this.FindControl<TextBlock>("InfoCountText")!;
        _diagList         = this.FindControl<ListBox>("DiagList")!;
        _tabAll           = this.FindControl<Button>("TabAll")!;
        _tabCurrentFile   = this.FindControl<Button>("TabCurrentFile")!;
        _currentFileLabel = this.FindControl<TextBlock>("CurrentFileLabel")!;
    }

    /// <summary>
    /// Sets the path of the currently active file in the editor.
    /// Updates the "Current File" tab display.
    /// </summary>
    public void SetCurrentFile(string? filePath)
    {
        _currentFilePath = filePath;
        _currentFileLabel.Text = string.IsNullOrEmpty(filePath)
            ? ""
            : System.IO.Path.GetFileName(filePath);

        if (!_showAll)
            RebuildList();

        UpdateCounts();
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

    private IEnumerable<DiagnosticItem> GetVisibleItems()
    {
        IEnumerable<DiagnosticItem> source = _items;

        if (!_showAll && !string.IsNullOrEmpty(_currentFilePath))
        {
            source = source.Where(i =>
                string.Equals(i.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase));
        }

        return source
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.FileName)
            .ThenBy(d => d.Line);
    }

    private void UpdateCounts()
    {
        var source = GetVisibleItems().ToList();
        _errorCountText.Text = source.Count(i => i.Severity == DiagnosticSeverity.Error).ToString();
        _warningCountText.Text = source.Count(i => i.Severity == DiagnosticSeverity.Warning).ToString();
        _infoCountText.Text = source.Count(i => i.Severity == DiagnosticSeverity.Info ||
                                                  i.Severity == DiagnosticSeverity.Hint).ToString();
    }

    private void RebuildList()
    {
        _diagList.Items.Clear();

        foreach (var item in GetVisibleItems())
        {
            _diagList.Items.Add(BuildRow(item));
        }
    }

    private void UpdateTabStyles()
    {
        var activeBg  = new SolidColorBrush(Color.Parse("#FF3E3050"));
        var inactiveBg = Brushes.Transparent;
        var activeFg  = new SolidColorBrush(Color.Parse("#FFF0E8F4"));
        var inactiveFg = new SolidColorBrush(Color.Parse("#FF9E90B0"));

        _tabAll.Background = _showAll ? activeBg : inactiveBg;
        _tabAll.Foreground = _showAll ? activeFg : inactiveFg;
        _tabCurrentFile.Background = !_showAll ? activeBg : inactiveBg;
        _tabCurrentFile.Foreground = !_showAll ? activeFg : inactiveFg;
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
            Width = 70,
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

        // Copy button for individual error
        var copyBtn = new Button
        {
            Content = "📋",
            FontSize = 10,
            Width = 24,
            Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0")),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Tag = item,
        };
        ToolTip.SetTip(copyBtn, "Copy error to clipboard");
        copyBtn.Click += OnCopySingleDiag;

        var grid = new Grid { Margin = new Thickness(4, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // code
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));   // message
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // location
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // copy btn
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(code, 1);
        Grid.SetColumn(message, 2);
        Grid.SetColumn(location, 3);
        Grid.SetColumn(copyBtn, 4);
        grid.Children.Add(icon);
        grid.Children.Add(code);
        grid.Children.Add(message);
        grid.Children.Add(location);
        grid.Children.Add(copyBtn);

        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 4),
            Tag = item,
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
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

    private static string FormatDiagnosticForClipboard(DiagnosticItem item)
    {
        var severity = item.Severity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            DiagnosticSeverity.Info => "Info",
            _ => "Hint",
        };
        return $"{severity} {item.Code}: {item.Message} [{item.Location}]";
    }

    private async void OnCopySingleDiag(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DiagnosticItem di)
        {
            var text = FormatDiagnosticForClipboard(di);
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(text);
            }
            catch { /* clipboard may not be available */ }
        }
    }

    private async void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        var visible = GetVisibleItems().ToList();
        if (visible.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in visible)
            sb.AppendLine(FormatDiagnosticForClipboard(item));

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(sb.ToString());
        }
        catch { /* clipboard may not be available */ }
    }

    private void OnTabAll(object? sender, RoutedEventArgs e)
    {
        _showAll = true;
        UpdateTabStyles();
        RebuildList();
        UpdateCounts();
    }

    private void OnTabCurrentFile(object? sender, RoutedEventArgs e)
    {
        _showAll = false;
        UpdateTabStyles();
        RebuildList();
        UpdateCounts();
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

