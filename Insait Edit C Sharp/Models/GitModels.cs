using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Insait_Edit_C_Sharp.Models;

#region Enums

/// <summary>
/// Git file status
/// </summary>
public enum GitFileStatus
{
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    Unmerged,
    Untracked,
    Ignored,
    Unknown
}

#endregion

#region Status

/// <summary>
/// Repository status
/// </summary>
public class GitStatus : INotifyPropertyChanged
{
    private string _currentBranch = "main";
    private int _aheadCount;
    private int _behindCount;
    private ObservableCollection<GitFileChange> _stagedChanges = new();
    private ObservableCollection<GitFileChange> _unstagedChanges = new();

    public string CurrentBranch
    {
        get => _currentBranch;
        set => SetProperty(ref _currentBranch, value);
    }

    public int AheadCount
    {
        get => _aheadCount;
        set => SetProperty(ref _aheadCount, value);
    }

    public int BehindCount
    {
        get => _behindCount;
        set => SetProperty(ref _behindCount, value);
    }

    public ObservableCollection<GitFileChange> StagedChanges
    {
        get => _stagedChanges;
        set => SetProperty(ref _stagedChanges, value);
    }

    public ObservableCollection<GitFileChange> UnstagedChanges
    {
        get => _unstagedChanges;
        set => SetProperty(ref _unstagedChanges, value);
    }

    public bool HasStagedChanges => StagedChanges.Count > 0;
    public bool HasUnstagedChanges => UnstagedChanges.Count > 0;
    public bool HasChanges => HasStagedChanges || HasUnstagedChanges;
    public int TotalChanges => StagedChanges.Count + UnstagedChanges.Count;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

#endregion

#region File Changes

/// <summary>
/// Represents a changed file in git
/// </summary>
public class GitFileChange : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fullPath = string.Empty;
    private GitFileStatus _indexStatus = GitFileStatus.Unmodified;
    private GitFileStatus _workTreeStatus = GitFileStatus.Unmodified;
    private bool _isSelected;

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(Directory));
            }
        }
    }

    public string FullPath
    {
        get => _fullPath;
        set => SetProperty(ref _fullPath, value);
    }

    public GitFileStatus IndexStatus
    {
        get => _indexStatus;
        set
        {
            if (SetProperty(ref _indexStatus, value))
            {
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public GitFileStatus WorkTreeStatus
    {
        get => _workTreeStatus;
        set
        {
            if (SetProperty(ref _workTreeStatus, value))
            {
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty;

    public string StatusIcon
    {
        get
        {
            var status = WorkTreeStatus != GitFileStatus.Unmodified ? WorkTreeStatus : IndexStatus;
            return status switch
            {
                GitFileStatus.Modified => "M",
                GitFileStatus.Added => "A",
                GitFileStatus.Deleted => "D",
                GitFileStatus.Renamed => "R",
                GitFileStatus.Copied => "C",
                GitFileStatus.Unmerged => "U",
                GitFileStatus.Untracked => "?",
                GitFileStatus.Ignored => "!",
                _ => " "
            };
        }
    }

    public IBrush StatusColor
    {
        get
        {
            var status = WorkTreeStatus != GitFileStatus.Unmodified ? WorkTreeStatus : IndexStatus;
            return status switch
            {
                GitFileStatus.Modified => SolidColorBrush.Parse("#FFF9E2AF"),      // Yellow/Orange
                GitFileStatus.Added => SolidColorBrush.Parse("#FFA6E3A1"),         // Green
                GitFileStatus.Deleted => SolidColorBrush.Parse("#FFF38BA8"),       // Red
                GitFileStatus.Renamed => SolidColorBrush.Parse("#FF89B4FA"),       // Blue
                GitFileStatus.Copied => SolidColorBrush.Parse("#FF89B4FA"),        // Blue
                GitFileStatus.Unmerged => SolidColorBrush.Parse("#FFCBA6F7"),      // Purple
                GitFileStatus.Untracked => SolidColorBrush.Parse("#FF9399B2"),     // Gray
                _ => SolidColorBrush.Parse("#FFCDD6F4")                            // Default
            };
        }
    }

    public string StatusText
    {
        get
        {
            var status = WorkTreeStatus != GitFileStatus.Unmodified ? WorkTreeStatus : IndexStatus;
            return status switch
            {
                GitFileStatus.Modified => "Modified",
                GitFileStatus.Added => "Added",
                GitFileStatus.Deleted => "Deleted",
                GitFileStatus.Renamed => "Renamed",
                GitFileStatus.Copied => "Copied",
                GitFileStatus.Unmerged => "Unmerged",
                GitFileStatus.Untracked => "Untracked",
                GitFileStatus.Ignored => "Ignored",
                _ => ""
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

#endregion

#region Commit

/// <summary>
/// Represents a git commit
/// </summary>
public class GitCommit
{
    public string Hash { get; set; } = string.Empty;
    public string ShortHash { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Body { get; set; }
    public List<GitFileChange> ChangedFiles { get; set; } = new();

    public string DateFormatted => Date.ToString("MMM dd, yyyy HH:mm");
    public string RelativeDate
    {
        get
        {
            var diff = DateTime.Now - Date;
            if (diff.TotalMinutes < 1) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
            if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)} weeks ago";
            if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)} months ago";
            return $"{(int)(diff.TotalDays / 365)} years ago";
        }
    }
    
    public string AuthorInitials
    {
        get
        {
            var parts = AuthorName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            if (parts.Length == 1 && parts[0].Length >= 2)
                return parts[0].Substring(0, 2).ToUpper();
            return "??";
        }
    }
}

#endregion

#region Branch

/// <summary>
/// Represents a git branch
/// </summary>
public class GitBranch : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isCurrent;
    private bool _isRemote;
    private string _lastCommitHash = string.Empty;
    private string _lastCommitMessage = string.Empty;
    private string? _trackingBranch;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ShortName));
            }
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public bool IsRemote
    {
        get => _isRemote;
        set => SetProperty(ref _isRemote, value);
    }

    public string LastCommitHash
    {
        get => _lastCommitHash;
        set => SetProperty(ref _lastCommitHash, value);
    }

    public string LastCommitMessage
    {
        get => _lastCommitMessage;
        set => SetProperty(ref _lastCommitMessage, value);
    }

    public string? TrackingBranch
    {
        get => _trackingBranch;
        set => SetProperty(ref _trackingBranch, value);
    }

    public string DisplayName => IsRemote ? Name.Replace("origin/", "⬇ ") : Name;
    public string ShortName => Name.Replace("origin/", "");

    public string Icon => IsCurrent ? "●" : IsRemote ? "☁" : "⎇";
    public IBrush IconColor => IsCurrent 
        ? SolidColorBrush.Parse("#FFA6E3A1") 
        : IsRemote 
            ? SolidColorBrush.Parse("#FF89B4FA") 
            : SolidColorBrush.Parse("#FFCDD6F4");

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

#endregion

#region Remote

/// <summary>
/// Represents a git remote
/// </summary>
public class GitRemote
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    public string Provider
    {
        get
        {
            if (Url.Contains("github.com")) return "GitHub";
            if (Url.Contains("gitlab.com")) return "GitLab";
            if (Url.Contains("bitbucket.org")) return "Bitbucket";
            if (Url.Contains("azure.com") || Url.Contains("visualstudio.com")) return "Azure DevOps";
            return "Git";
        }
    }

    public string Icon
    {
        get
        {
            return Provider switch
            {
                "GitHub" => "🐙",
                "GitLab" => "🦊",
                "Bitbucket" => "🪣",
                "Azure DevOps" => "☁",
                _ => "🌐"
            };
        }
    }
}

#endregion

#region Stash

/// <summary>
/// Represents a git stash entry
/// </summary>
public class GitStash
{
    public string Name { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return $"{Name}: WIP on {Branch}";
            return $"{Name}: {Message}";
        }
    }
}

#endregion

#region Tag

/// <summary>
/// Represents a git tag
/// </summary>
public class GitTag
{
    public string Name { get; set; } = string.Empty;
    public string CommitHash { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string Message { get; set; } = string.Empty;

    public string DateFormatted => Date?.ToString("MMM dd, yyyy") ?? "";
}

#endregion

