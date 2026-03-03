using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

public partial class GoToDefinitionWindow : Window
{
    private TextBlock _symbolNameText = null!;
    private TextBlock _symbolKindText = null!;
    private ListBox _locationsList = null!;
    private readonly List<LocationInfo> _locations = new();

    public event EventHandler<GoToDefinitionEventArgs>? NavigateRequested;

    public GoToDefinitionWindow()
    {
        InitializeComponent();
    }

    public GoToDefinitionWindow(DefinitionResult result) : this()
    {
        _symbolNameText.Text = result.Symbol;
        _symbolKindText.Text = $"({result.Kind})";
        _locations.AddRange(result.Locations);
        BuildLocationsList();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _symbolNameText = this.FindControl<TextBlock>("SymbolNameText")!;
        _symbolKindText = this.FindControl<TextBlock>("SymbolKindText")!;
        _locationsList = this.FindControl<ListBox>("LocationsList")!;
    }

    private void BuildLocationsList()
    {
        _locationsList.Items.Clear();
        foreach (var loc in _locations)
        {
            var icon = loc.IsMetadata ? "📦" : "📄";
            var display = loc.IsMetadata
                ? $"{icon}  {loc.MetadataDisplayName ?? loc.FilePath}"
                : $"{icon}  {Path.GetFileName(loc.FilePath)} — Ln {loc.StartLine}, Col {loc.StartColumn}";
            var border = new Border
            {
                Background = Brushes.Transparent,
                Tag = loc,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = display, FontSize = 12,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    Foreground = new SolidColorBrush(loc.IsMetadata
                        ? Color.Parse("#FF9E90B0") : Color.Parse("#FFA6E3A1")),
                    Padding = new Thickness(8, 4),
                }
            };
            border.PointerPressed += (s, _) =>
            {
                if (s is Border b && b.Tag is LocationInfo li && !li.IsMetadata)
                {
                    NavigateRequested?.Invoke(this, new GoToDefinitionEventArgs(li.FilePath, li.StartLine, li.StartColumn));
                    Close();
                }
            };
            _locationsList.Items.Add(border);
        }
    }

    private void OnLocationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_locationsList.SelectedItem is Border b && b.Tag is LocationInfo loc && !loc.IsMetadata)
        {
            NavigateRequested?.Invoke(this, new GoToDefinitionEventArgs(loc.FilePath, loc.StartLine, loc.StartColumn));
            Close();
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

public sealed class GoToDefinitionEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public GoToDefinitionEventArgs(string filePath, int line, int column)
    { FilePath = filePath; Line = line; Column = column; }
}

