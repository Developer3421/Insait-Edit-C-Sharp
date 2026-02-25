using System;
using System.Collections.ObjectModel;

namespace Insait_Edit_C_Sharp.Models;

/// <summary>
/// Represents a project in the IDE
/// </summary>
public class Project
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? SolutionPath { get; set; }
    public ProjectType Type { get; set; } = ProjectType.Console;
    public ObservableCollection<ProjectFile> Files { get; set; } = new();
    public ObservableCollection<string> References { get; set; } = new();
    public DateTime LastOpened { get; set; } = DateTime.Now;
    public bool IsDirty { get; set; }
}

public enum ProjectType
{
    Console,
    WinForms,
    WPF,
    Avalonia,
    AspNet,
    Library,
    NanoFramework,
    NanoFrameworkLib,
    Unknown
}

/// <summary>
/// Represents a file in the project
/// </summary>
public class ProjectFile
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public FileType Type { get; set; } = FileType.Unknown;
    public bool IsDirectory { get; set; }
    public bool IsExpanded { get; set; }
    public ObservableCollection<ProjectFile> Children { get; set; } = new();
}

public enum FileType
{
    CSharp,
    Xaml,
    Json,
    Xml,
    Config,
    Solution,
    Project,
    NanoProject,
    Text,
    Markdown,
    Unknown
}

