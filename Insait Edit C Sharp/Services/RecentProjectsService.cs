using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for managing recent projects
/// </summary>
public class RecentProjectsService
{
    private const int MaxRecentProjects = 20;
    private readonly string _recentProjectsPath;
    private List<RecentProjectData> _recentProjects;

    public RecentProjectsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InsaitEdit");
        
        Directory.CreateDirectory(appDataPath);
        _recentProjectsPath = Path.Combine(appDataPath, "recent_projects.json");
        
        _recentProjects = LoadFromFile();
    }

    /// <summary>
    /// Gets all recent projects
    /// </summary>
    public IEnumerable<RecentProjectItem> GetRecentProjects()
    {
        // Clean up non-existent projects
        _recentProjects = _recentProjects
            .Where(p => File.Exists(p.Path) || Directory.Exists(p.Path))
            .ToList();
        
        SaveToFile();

        return _recentProjects
            .OrderByDescending(p => p.LastOpened)
            .Select(ConvertToDisplayItem);
    }

    /// <summary>
    /// Adds a project to recent list
    /// </summary>
    public void AddRecentProject(string path)
    {
        // Remove if already exists
        _recentProjects.RemoveAll(p => 
            p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        // Add to beginning
        _recentProjects.Insert(0, new RecentProjectData
        {
            Path = path,
            LastOpened = DateTime.Now
        });

        // Trim to max
        if (_recentProjects.Count > MaxRecentProjects)
        {
            _recentProjects = _recentProjects.Take(MaxRecentProjects).ToList();
        }

        SaveToFile();
    }

    /// <summary>
    /// Removes a project from recent list
    /// </summary>
    public void RemoveRecentProject(string path)
    {
        _recentProjects.RemoveAll(p => 
            p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        SaveToFile();
    }

    /// <summary>
    /// Clears all recent projects
    /// </summary>
    public void ClearRecentProjects()
    {
        _recentProjects.Clear();
        SaveToFile();
    }

    private List<RecentProjectData> LoadFromFile()
    {
        try
        {
            if (File.Exists(_recentProjectsPath))
            {
                var json = File.ReadAllText(_recentProjectsPath);
                return JsonSerializer.Deserialize<List<RecentProjectData>>(json) ?? new List<RecentProjectData>();
            }
        }
        catch
        {
            // Ignore errors loading file
        }

        return new List<RecentProjectData>();
    }

    private void SaveToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_recentProjects, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_recentProjectsPath, json);
        }
        catch
        {
            // Ignore errors saving file
        }
    }

    private RecentProjectItem ConvertToDisplayItem(RecentProjectData data)
    {
        var name = Path.GetFileNameWithoutExtension(data.Path);
        var extension = Path.GetExtension(data.Path).ToLowerInvariant();
        var projectType = GetProjectType(extension);
        var (icon, color) = GetIconAndColor(extension);

        return new RecentProjectItem
        {
            Name = name,
            Path = data.Path,
            Icon = icon,
            IconBackground = new SolidColorBrush(Color.Parse(color)),
            LastOpened = FormatLastOpened(data.LastOpened),
            ProjectType = projectType
        };
    }

    private string GetProjectType(string extension)
    {
        return extension switch
        {
            ".sln" => "Solution",
            ".csproj" => "C# Project",
            ".fsproj" => "F# Project",
            ".vbproj" => "VB.NET Project",
            _ => Directory.Exists(extension) ? "Folder" : "File"
        };
    }

    private (string icon, string color) GetIconAndColor(string extension)
    {
        return extension switch
        {
            ".sln" => ("🗂️", "#30CBA6F7"),      // Purple tint
            ".csproj" => ("⚡", "#30A6E3A1"),    // Green tint
            ".fsproj" => ("🔷", "#3089B4FA"),    // Blue tint
            ".vbproj" => ("🔶", "#30FAB387"),    // Orange tint
            _ => ("📁", "#30CBA6F7")
        };
    }

    private string FormatLastOpened(DateTime lastOpened)
    {
        var diff = DateTime.Now - lastOpened;

        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
        if (diff.TotalDays < 365) return lastOpened.ToString("MMM d");
        
        return lastOpened.ToString("MMM d, yyyy");
    }
}

/// <summary>
/// Data model for storing recent project info
/// </summary>
public class RecentProjectData
{
    public string Path { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
}

