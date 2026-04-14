using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Small dialog shown before analysis when the solution contains 2+ projects.
/// The user picks which project to analyze.
/// </summary>
public partial class SelectProjectWindow : Window
{
    /// <summary>
    /// Full path to the selected .csproj (or project directory).
    /// Null when the user cancelled.
    /// </summary>
    public string? SelectedProjectPath { get; private set; }

    private readonly List<(string Label, string Path)> _projects;

    public SelectProjectWindow(List<(string Label, string Path)> projects)
    {
        AvaloniaXamlLoader.Load(this);
        _projects = projects;

        var listBox = this.FindControl<ListBox>("ProjectListBox");
        if (listBox != null)
        {
            foreach (var (label, path) in projects)
            {
                listBox.Items.Add(new ListBoxItem
                {
                    Content = $"📦  {label}",
                    Tag     = path
                });
            }

            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedProjectPath = null;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedProjectPath = null;
        Close();
    }

    private void AnalyzeButton_Click(object? sender, RoutedEventArgs e) => AcceptSelection();

    private void ProjectList_DoubleTapped(object? sender, TappedEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        var listBox = this.FindControl<ListBox>("ProjectListBox");
        if (listBox?.SelectedItem is ListBoxItem item && item.Tag is string path)
        {
            SelectedProjectPath = path;
            Close();
        }
    }
}
