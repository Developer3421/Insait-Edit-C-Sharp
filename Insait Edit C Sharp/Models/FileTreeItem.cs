using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Insait_Edit_C_Sharp.Models;

/// <summary>
/// Represents a file or folder in the file tree
/// </summary>
public class FileTreeItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _fullPath = string.Empty;
    private bool _isDirectory;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservableCollection<FileTreeItem> _children;
    private bool _isLoaded;

    public FileTreeItem()
    {
        _children = new ObservableCollection<FileTreeItem>();
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string FullPath
    {
        get => _fullPath;
        set => SetProperty(ref _fullPath, value);
    }

    public bool IsDirectory
    {
        get => _isDirectory;
        set => SetProperty(ref _isDirectory, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                // Load children when expanding for the first time
                if (value && IsDirectory && !_isLoaded)
                {
                    LoadChildren();
                }
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ObservableCollection<FileTreeItem> Children
    {
        get => _children;
        set => SetProperty(ref _children, value);
    }

    /// <summary>
    /// Gets the display icon for this item
    /// </summary>
    public string Icon => IsDirectory ? "📁" : GetFileIcon();

    /// <summary>
    /// Gets file extension for syntax highlighting indicators
    /// </summary>
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();

    private string GetFileIcon()
    {
        return Extension switch
        {
            ".cs" => "📄",
            ".axaml" or ".xaml" => "📄",
            ".json" => "📄",
            ".xml" => "📄",
            ".html" or ".htm" => "📄",
            ".css" => "📄",
            ".js" => "📄",
            ".ts" => "📄",
            ".md" => "📄",
            ".txt" => "📄",
            ".sln" => "📦",
            ".csproj" => "📦",
            _ => "📄"
        };
    }

    /// <summary>
    /// Loads children directories and files
    /// </summary>
    public void LoadChildren()
    {
        if (!IsDirectory || _isLoaded) return;

        try
        {
            _isLoaded = true;
            Children.Clear();

            // Add directories first
            var directories = Directory.GetDirectories(FullPath);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                // Skip hidden folders and common excluded folders
                if (dirName.StartsWith(".") || 
                    dirName == "bin" || 
                    dirName == "obj" || 
                    dirName == "node_modules" ||
                    dirName == ".git" ||
                    dirName == ".vs")
                {
                    continue;
                }

                Children.Add(new FileTreeItem
                {
                    Name = dirName,
                    FullPath = dir,
                    IsDirectory = true
                });
            }

            // Add files
            var files = Directory.GetFiles(FullPath);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                // Skip hidden files
                if (fileName.StartsWith(".")) continue;

                Children.Add(new FileTreeItem
                {
                    Name = fileName,
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading directory contents: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the children of this directory
    /// </summary>
    public void Refresh()
    {
        if (!IsDirectory) return;
        _isLoaded = false;
        Children.Clear();
        if (IsExpanded)
        {
            LoadChildren();
        }
    }

    /// <summary>
    /// Creates a FileTreeItem from a directory path
    /// </summary>
    public static FileTreeItem FromDirectory(string path, bool loadChildren = false)
    {
        var item = new FileTreeItem
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            IsDirectory = true
        };

        if (loadChildren)
        {
            item.LoadChildren();
        }

        return item;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
