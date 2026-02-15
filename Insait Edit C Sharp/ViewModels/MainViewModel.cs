using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Insait_Edit_C_Sharp.Models;

namespace Insait_Edit_C_Sharp.ViewModels;

/// <summary>
/// Main view model for the IDE
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private Project? _currentProject;
    private EditorTab? _activeTab;
    private string _statusText = "Ready";
    private bool _isBuildInProgress;
    private string _searchQuery = string.Empty;
    private string? _currentProjectPath;

    public MainViewModel()
    {
        Tabs = new ObservableCollection<EditorTab>();
        RecentFiles = new ObservableCollection<string>();
        Problems = new ObservableCollection<DiagnosticItem>();
        OutputLines = new ObservableCollection<string>();
        FileTreeItems = new ObservableCollection<FileTreeItem>();
    }

    #region Properties

    public Project? CurrentProject
    {
        get => _currentProject;
        set => SetProperty(ref _currentProject, value);
    }

    public ObservableCollection<EditorTab> Tabs { get; }

    public EditorTab? ActiveTab
    {
        get => _activeTab;
        set => SetProperty(ref _activeTab, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsBuildInProgress
    {
        get => _isBuildInProgress;
        set => SetProperty(ref _isBuildInProgress, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string? CurrentProjectPath
    {
        get => _currentProjectPath;
        set => SetProperty(ref _currentProjectPath, value);
    }

    public ObservableCollection<string> RecentFiles { get; }
    public ObservableCollection<DiagnosticItem> Problems { get; }
    public ObservableCollection<string> OutputLines { get; }
    public ObservableCollection<FileTreeItem> FileTreeItems { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Load a project folder into the file tree
    /// </summary>
    public void LoadProjectFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        CurrentProjectPath = folderPath;
        FileTreeItems.Clear();

        var rootItem = FileTreeItem.FromDirectory(folderPath, loadChildren: true);
        rootItem.IsExpanded = true;
        FileTreeItems.Add(rootItem);

        StatusText = $"Opened folder: {Path.GetFileName(folderPath)}";
    }

    /// <summary>
    /// Load a solution or project file
    /// </summary>
    public void LoadSolutionOrProject(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            LoadProjectFolder(directory);
        }

        StatusText = $"Loaded: {Path.GetFileName(filePath)}";
    }

    public void OpenFile(string filePath)
    {
        // Check if file is already open
        var existingTab = FindTabByPath(filePath);
        if (existingTab != null)
        {
            ActiveTab = existingTab;
            return;
        }

        // Create new tab
        var tab = new EditorTab
        {
            FileName = System.IO.Path.GetFileName(filePath),
            FilePath = filePath,
            Language = EditorTab.GetLanguageFromExtension(filePath),
        };

        // Read file content
        if (System.IO.File.Exists(filePath))
        {
            tab.Content = System.IO.File.ReadAllText(filePath);
        }

        Tabs.Add(tab);
        ActiveTab = tab;
        
        // Update recent files
        if (!RecentFiles.Contains(filePath))
        {
            RecentFiles.Insert(0, filePath);
            if (RecentFiles.Count > 10)
            {
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }
        }

        StatusText = $"Opened: {tab.FileName}";
    }

    public void CloseTab(EditorTab tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (ActiveTab == tab && Tabs.Count > 0)
        {
            ActiveTab = Tabs[System.Math.Max(0, index - 1)];
        }
        else if (Tabs.Count == 0)
        {
            ActiveTab = null;
        }
    }

    public void SaveActiveTab()
    {
        if (ActiveTab == null || string.IsNullOrEmpty(ActiveTab.FilePath))
            return;

        System.IO.File.WriteAllText(ActiveTab.FilePath, ActiveTab.Content);
        ActiveTab.IsDirty = false;
        StatusText = $"Saved: {ActiveTab.FileName}";
    }

    public void SaveAllTabs()
    {
        foreach (var tab in Tabs)
        {
            if (tab.IsDirty && !string.IsNullOrEmpty(tab.FilePath))
            {
                System.IO.File.WriteAllText(tab.FilePath, tab.Content);
                tab.IsDirty = false;
            }
        }
        StatusText = "All files saved";
    }

    public EditorTab? FindTabByPath(string filePath)
    {
        foreach (var tab in Tabs)
        {
            if (tab.FilePath.Equals(filePath, System.StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }
        return null;
    }

    public void AddOutput(string message)
    {
        OutputLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void ClearOutput()
    {
        OutputLines.Clear();
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

/// <summary>
/// Represents a diagnostic/problem item
/// </summary>
public class DiagnosticItem
{
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Code { get; set; } = string.Empty;
}

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info,
    Hint
}
