using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class WelcomeWindow : Window
{
    private readonly RecentProjectsService _recentProjectsService;
    private ObservableCollection<RecentProjectItem> _recentProjects;
    private ObservableCollection<RecentProjectItem> _filteredProjects;

    public WelcomeWindow()
    {
        InitializeComponent();
        
        _recentProjectsService = new RecentProjectsService();
        _recentProjects = new ObservableCollection<RecentProjectItem>();
        _filteredProjects = new ObservableCollection<RecentProjectItem>();
        
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        _recentProjects.Clear();
        var projects = _recentProjectsService.GetRecentProjects();
        
        foreach (var project in projects)
        {
            _recentProjects.Add(project);
        }

        UpdateFilteredProjects();
        UpdateEmptyState();
    }

    private void UpdateFilteredProjects(string? filter = null)
    {
        _filteredProjects.Clear();
        
        var projects = string.IsNullOrWhiteSpace(filter) 
            ? _recentProjects 
            : _recentProjects.Where(p => 
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var project in projects)
        {
            _filteredProjects.Add(project);
        }

        var list = this.FindControl<ItemsControl>("RecentProjectsList");
        if (list != null)
        {
            list.ItemsSource = _filteredProjects;
        }
        
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var emptyState = this.FindControl<StackPanel>("EmptyState");
        var list = this.FindControl<ItemsControl>("RecentProjectsList");
        
        if (emptyState != null && list != null)
        {
            var isEmpty = !_filteredProjects.Any();
            emptyState.IsVisible = isEmpty;
            list.IsVisible = !isEmpty;
        }
    }

    #region Title Bar

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion

    #region Action Buttons

    private async void NewSolution_Click(object? sender, RoutedEventArgs e)
    {
        var newSolutionWindow = new NewSolutionWindow();
        var result = await newSolutionWindow.ShowDialog<string?>(this);
        
        if (!string.IsNullOrEmpty(result))
        {
            OpenProjectAndShowMainWindow(result);
        }
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        var newProjectWindow = new NewProjectWindow();
        var result = await newProjectWindow.ShowDialog<string?>(this);
        
        if (!string.IsNullOrEmpty(result))
        {
            OpenProjectAndShowMainWindow(result);
        }
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Project or Solution",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("C# Solution") { Patterns = new[] { "*.sln", "*.slnx" } },
                new FilePickerFileType("C# Project") { Patterns = new[] { "*.csproj" } },
                new FilePickerFileType("nanoFramework Project") { Patterns = new[] { "*.nfproj" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var filePath = files[0].Path.LocalPath;
            OpenProjectAndShowMainWindow(filePath);
        }
    }

    private async void CloneRepository_Click(object? sender, RoutedEventArgs e)
    {
        var cloneWindow = new CloneRepositoryWindow();
        var result = await cloneWindow.ShowDialog<string?>(this);
        
        if (!string.IsNullOrEmpty(result))
        {
            // Find .sln or .csproj in cloned directory
            var projectFile = FindProjectFile(result);
            if (!string.IsNullOrEmpty(projectFile))
            {
                OpenProjectAndShowMainWindow(projectFile);
            }
            else
            {
                // Open folder directly
                OpenProjectAndShowMainWindow(result);
            }
        }
    }


    private string? FindProjectFile(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        // First look for .sln
        var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length > 0) return slnFiles[0];

        // Then look for .csproj
        var csprojFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length > 0) return csprojFiles[0];

        // Then look for .nfproj (nanoFramework)
        var nfprojFiles = Directory.GetFiles(directory, "*.nfproj", SearchOption.AllDirectories);
        if (nfprojFiles.Length > 0) return nfprojFiles[0];

        return null;
    }

    #endregion

    #region Recent Projects

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var searchBox = sender as TextBox;
        UpdateFilteredProjects(searchBox?.Text);
    }

    private void ClearRecent_Click(object? sender, RoutedEventArgs e)
    {
        _recentProjectsService.ClearRecentProjects();
        LoadRecentProjects();
    }

    private void RecentProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                OpenProjectAndShowMainWindow(path);
            }
            else
            {
                // Project no longer exists - ask to remove
                // For now, just remove it
                _recentProjectsService.RemoveRecentProject(path);
                LoadRecentProjects();
            }
        }
    }

    #endregion

    #region Footer Links

    private void Documentation_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/insait-edit/docs");
    }

    private void GitHub_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/insait-edit/insait-edit-csharp");
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Open settings window
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening URL
        }
    }

    #endregion

    #region Helpers

    private void OpenProjectAndShowMainWindow(string projectPath)
    {
        // Add to recent projects
        _recentProjectsService.AddRecentProject(projectPath);
        
        // Open main window with project
        var mainWindow = new MainWindow(projectPath);
        mainWindow.Show();
        
        // Close welcome window
        Close();
    }

    #endregion
}

/// <summary>
/// Represents a recent project item for display
/// </summary>
public class RecentProjectItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public IBrush IconBackground { get; set; } = new SolidColorBrush(Color.Parse("#30CBA6F7"));
    public string LastOpened { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
}

