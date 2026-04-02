using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Window that shows recently opened projects (JetBrains Rider style).
/// Returns the selected project path via <see cref="SelectedProjectPath"/>.
/// </summary>
public partial class RecentProjectsWindow : Window
{
    private readonly RecentProjectsService _recentProjectsService;
    private ObservableCollection<RecentProjectItem> _allProjects;
    private ObservableCollection<RecentProjectItem> _filtered;

    private static string L(string key) => LocalizationService.Get(key);

    /// <summary>
    /// Set to the path chosen by the user, or null if cancelled / window closed.
    /// </summary>
    public string? SelectedProjectPath { get; private set; }

    public RecentProjectsWindow()
    {
        InitializeComponent();

        _recentProjectsService = new RecentProjectsService();
        _allProjects = new ObservableCollection<RecentProjectItem>();
        _filtered   = new ObservableCollection<RecentProjectItem>();

        ApplyLocalization();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        Closed += OnWindowClosed;

        LoadProjects();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void ApplyLocalization()
    {
        Title = L("RecentProjects");
        UpdateCountLabel();
    }

    // ── Data ────────────────────────────────────────────────────────────────

    private void LoadProjects()
    {
        _allProjects.Clear();
        foreach (var p in _recentProjectsService.GetRecentProjects())
            _allProjects.Add(p);

        ApplyFilter(this.FindControl<TextBox>("SearchBox")?.Text);
    }

    private void ApplyFilter(string? query)
    {
        _filtered.Clear();

        var items = string.IsNullOrWhiteSpace(query)
            ? _allProjects
            : _allProjects.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var p in items)
            _filtered.Add(p);

        var list = this.FindControl<ItemsControl>("RecentProjectsList");
        if (list != null)
            list.ItemsSource = _filtered;

        UpdateEmptyState();
        UpdateCountLabel();
    }

    private void UpdateEmptyState()
    {
        var empty = this.FindControl<StackPanel>("EmptyState");
        var list  = this.FindControl<ItemsControl>("RecentProjectsList");

        if (empty == null || list == null) return;

        var isEmpty = !_filtered.Any();
        empty.IsVisible  = isEmpty;
        list.IsVisible   = !isEmpty;
    }

    private void UpdateCountLabel()
    {
        var label = this.FindControl<TextBlock>("CountLabel");
        if (label == null) return;
        label.Text = _filtered.Count > 0
            ? string.Format(L("RecentProjectsWindow.Count"), _filtered.Count)
            : string.Empty;
    }

    // ── Title Bar ───────────────────────────────────────────────────────────

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // ── Events ──────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter((sender as TextBox)?.Text);
    }

    private void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        _recentProjectsService.ClearRecentProjects();
        LoadProjects();
    }

    private void RecentProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                SelectedProjectPath = path;
                Close();
            }
            else
            {
                // Path no longer exists — remove and refresh
                _recentProjectsService.RemoveRecentProject(path);
                LoadProjects();
            }
        }
    }

    private void RemoveProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            _recentProjectsService.RemoveRecentProject(path);
            LoadProjects();
        }
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("RecentProjectsWindow.OpenPickerTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(L("RecentProjectsWindow.FileType.Solution")) { Patterns = new[] { "*.sln", "*.slnx" } },
                new FilePickerFileType(L("RecentProjectsWindow.FileType.Project"))  { Patterns = new[] { "*.csproj" } },
                new FilePickerFileType(L("RecentProjectsWindow.FileType.All"))      { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            SelectedProjectPath = files[0].Path.LocalPath;
            Close();
        }
    }
}

