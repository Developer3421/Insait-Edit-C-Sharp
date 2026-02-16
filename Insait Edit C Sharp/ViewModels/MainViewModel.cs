using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Timers;
using Insait_Edit_C_Sharp.Models;

namespace Insait_Edit_C_Sharp.ViewModels;

/// <summary>
/// Info about a project in a solution
/// </summary>
public class SolutionProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public string TypeGuid { get; set; } = string.Empty;
}

/// <summary>
/// Main view model for the IDE
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private Project? _currentProject;
    private EditorTab? _activeTab;
    private string _statusText = "Ready";
    private bool _isBuildInProgress;
    private string _searchQuery = string.Empty;
    private string? _currentProjectPath;
    private int _errorsCount;
    private int _warningsCount;
    
    // File system watcher for automatic refresh
    private FileSystemWatcher? _fileWatcher;
    private Timer? _refreshDebounceTimer;
    private bool _refreshPending;
    private readonly object _refreshLock = new object();
    
    // Action to invoke UI refresh on dispatcher
    public Action? RefreshTreeAction { get; set; }

    public MainViewModel()
    {
        Tabs = new ObservableCollection<EditorTab>();
        RecentFiles = new ObservableCollection<string>();
        Problems = new ObservableCollection<DiagnosticItem>();
        OutputLines = new ObservableCollection<string>();
        FileTreeItems = new ObservableCollection<FileTreeItem>();
        
        // Subscribe to Problems collection changes
        Problems.CollectionChanged += (s, e) => UpdateProblemsCounts();
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
    
    public int ErrorsCount
    {
        get => _errorsCount;
        set => SetProperty(ref _errorsCount, value);
    }
    
    public int WarningsCount
    {
        get => _warningsCount;
        set => SetProperty(ref _warningsCount, value);
    }
    
    public int ProblemsCount => ErrorsCount + WarningsCount;
    
    public bool HasProblems => ProblemsCount > 0;

    public ObservableCollection<string> RecentFiles { get; }
    public ObservableCollection<DiagnosticItem> Problems { get; }
    public ObservableCollection<string> OutputLines { get; }
    public ObservableCollection<FileTreeItem> FileTreeItems { get; }
    
    private void UpdateProblemsCounts()
    {
        ErrorsCount = 0;
        WarningsCount = 0;
        foreach (var problem in Problems)
        {
            if (problem.Severity == DiagnosticSeverity.Error)
                ErrorsCount++;
            else if (problem.Severity == DiagnosticSeverity.Warning)
                WarningsCount++;
        }
        OnPropertyChanged(nameof(ProblemsCount));
        OnPropertyChanged(nameof(HasProblems));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Load a project folder into the file tree
    /// </summary>
    public void LoadProjectFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            // Try to get directory from file path
            if (File.Exists(folderPath))
            {
                folderPath = Path.GetDirectoryName(folderPath) ?? folderPath;
            }
            else
            {
                StatusText = $"Directory not found: {folderPath}";
                return;
            }
        }

        CurrentProjectPath = folderPath;
        FileTreeItems.Clear();

        // Check if there's a solution file in the folder
        var solutionFile = FindSolutionFileInDirectory(folderPath);
        
        if (!string.IsNullOrEmpty(solutionFile))
        {
            // Load as solution with projects (Rider-style)
            LoadSolutionStructure(folderPath, solutionFile);
        }
        else
        {
            // Check for project file
            var projectFile = FindProjectFileInDirectory(folderPath);
            if (!string.IsNullOrEmpty(projectFile))
            {
                // Load as single project
                LoadProjectStructure(folderPath, projectFile);
            }
            else
            {
                // Load as regular folder
                var rootItem = FileTreeItem.FromDirectory(folderPath, loadChildren: true);
                rootItem.IsExpanded = true;
                FileTreeItems.Add(rootItem);
            }
        }
        
        // Initialize file watcher
        InitializeFileWatcher(folderPath);

        StatusText = $"Opened folder: {Path.GetFileName(folderPath)}";
    }

    /// <summary>
    /// Find solution file in directory
    /// </summary>
    private string? FindSolutionFileInDirectory(string directory)
    {
        try
        {
            // First look for .slnx files (new format)
            var slnxFiles = Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly);
            if (slnxFiles.Length > 0) return slnxFiles[0];

            // Then look for .sln files (legacy format)
            var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0) return slnFiles[0];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error finding solution file: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Find project file in directory
    /// </summary>
    private string? FindProjectFileInDirectory(string directory)
    {
        try
        {
            var projectFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projectFiles.Length > 0) return projectFiles[0];

            projectFiles = Directory.GetFiles(directory, "*.fsproj", SearchOption.TopDirectoryOnly);
            if (projectFiles.Length > 0) return projectFiles[0];

            projectFiles = Directory.GetFiles(directory, "*.vbproj", SearchOption.TopDirectoryOnly);
            if (projectFiles.Length > 0) return projectFiles[0];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error finding project file: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Load solution structure with projects (like JetBrains Rider)
    /// </summary>
    private void LoadSolutionStructure(string folderPath, string solutionFile)
    {
        var solutionName = Path.GetFileNameWithoutExtension(solutionFile);
        
        // Create solution root node
        var solutionItem = new FileTreeItem
        {
            Name = solutionName,
            FullPath = solutionFile,
            IsDirectory = false,  // Solution file itself
            ItemType = FileTreeItemType.Solution,
            IsSolutionItem = true,
            IsExpanded = true
        };

        // Parse solution file to get projects
        var projects = ParseSolutionFile(solutionFile);
        
        // Add each project as child of solution
        foreach (var projectInfo in projects)
        {
            var projectPath = Path.GetFullPath(Path.Combine(folderPath, projectInfo.RelativePath));
            if (!File.Exists(projectPath)) continue;

            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir)) continue;

            var projectItem = new FileTreeItem
            {
                Name = projectInfo.Name,
                FullPath = projectDir,
                IsDirectory = true,
                ItemType = FileTreeItemType.Project,
                IsSolutionItem = true,
                ProjectGuid = projectInfo.Guid,
                Description = GetProjectDescription(projectPath),
                IsExpanded = false
            };

            // Load project children (source files)
            LoadProjectContents(projectItem, projectDir);
            
            solutionItem.Children.Add(projectItem);
        }

        // Add solution-level items (files in solution directory but not in projects)
        AddSolutionLevelItems(solutionItem, folderPath, projects);

        FileTreeItems.Add(solutionItem);
        StatusText = $"Loaded solution: {solutionName} ({projects.Count} projects)";
    }

    /// <summary>
    /// Load single project structure
    /// </summary>
    private void LoadProjectStructure(string folderPath, string projectFile)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        
        var projectItem = new FileTreeItem
        {
            Name = projectName,
            FullPath = folderPath,
            IsDirectory = true,
            ItemType = FileTreeItemType.Project,
            IsSolutionItem = false,
            Description = GetProjectDescription(projectFile),
            IsExpanded = true
        };

        LoadProjectContents(projectItem, folderPath);
        
        FileTreeItems.Add(projectItem);
        StatusText = $"Loaded project: {projectName}";
    }

    /// <summary>
    /// Parse solution file to extract projects
    /// </summary>
    private List<SolutionProjectInfo> ParseSolutionFile(string solutionPath)
    {
        var projects = new List<SolutionProjectInfo>();
        
        try
        {
            var lines = File.ReadAllLines(solutionPath);
            
            // Regex for parsing Project lines in .sln files
            // Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ProjectName", "Path\ProjectName.csproj", "{GUID}"
            var projectRegex = new System.Text.RegularExpressions.Regex(
                @"^Project\(""(?<TypeGuid>[^""]+)""\)\s*=\s*""(?<Name>[^""]+)""\s*,\s*""(?<Path>[^""]+)""\s*,\s*""(?<Guid>[^""]+)""",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            
            // GUID for C# projects
            var csharpTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
            var newCsharpTypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}"; // SDK-style
            // GUID for solution folders (we skip these)
            var solutionFolderGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";
            
            foreach (var line in lines)
            {
                var match = projectRegex.Match(line);
                if (match.Success)
                {
                    var typeGuid = match.Groups["TypeGuid"].Value;
                    
                    // Skip solution folders
                    if (typeGuid.Equals(solutionFolderGuid, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    var name = match.Groups["Name"].Value;
                    var path = match.Groups["Path"].Value;
                    var guid = match.Groups["Guid"].Value;
                    
                    // Only include actual project files
                    if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                    {
                        projects.Add(new SolutionProjectInfo
                        {
                            Name = name,
                            RelativePath = path.Replace("/", "\\"),
                            Guid = guid,
                            TypeGuid = typeGuid
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing solution file: {ex.Message}");
        }
        
        return projects;
    }

    /// <summary>
    /// Get project description from csproj file
    /// </summary>
    private string? GetProjectDescription(string projectPath)
    {
        try
        {
            var content = File.ReadAllText(projectPath);
            
            // Try to find TargetFramework
            var tfMatch = System.Text.RegularExpressions.Regex.Match(content, @"<TargetFramework>([^<]+)</TargetFramework>");
            if (tfMatch.Success)
            {
                return tfMatch.Groups[1].Value;
            }
            
            // Try TargetFrameworks (multiple)
            tfMatch = System.Text.RegularExpressions.Regex.Match(content, @"<TargetFrameworks>([^<]+)</TargetFrameworks>");
            if (tfMatch.Success)
            {
                return tfMatch.Groups[1].Value;
            }
            
            // Try to find OutputType
            var outputMatch = System.Text.RegularExpressions.Regex.Match(content, @"<OutputType>([^<]+)</OutputType>");
            if (outputMatch.Success)
            {
                return outputMatch.Groups[1].Value;
            }
        }
        catch { }
        
        return null;
    }

    /// <summary>
    /// Load contents of a project directory
    /// </summary>
    private void LoadProjectContents(FileTreeItem projectItem, string projectDir)
    {
        try
        {
            // First add special folders (Dependencies, Properties, etc.)
            var dependenciesFolder = new FileTreeItem
            {
                Name = "Dependencies",
                FullPath = projectDir,  // Virtual folder
                IsDirectory = true,
                ItemType = FileTreeItemType.DependenciesFolder,
                IsSolutionItem = true
            };
            projectItem.Children.Add(dependenciesFolder);

            // Add Properties folder if exists
            var propertiesDir = Path.Combine(projectDir, "Properties");
            if (Directory.Exists(propertiesDir))
            {
                var propertiesItem = FileTreeItem.FromDirectory(propertiesDir, loadChildren: true);
                propertiesItem.ItemType = FileTreeItemType.SpecialFolder;
                projectItem.Children.Add(propertiesItem);
            }

            // Add all other directories (excluding bin, obj, .vs, etc.)
            foreach (var dir in Directory.GetDirectories(projectDir))
            {
                var dirName = Path.GetFileName(dir);
                if (ShouldExcludeDirectory(dirName)) continue;
                if (dirName.Equals("Properties", StringComparison.OrdinalIgnoreCase)) continue;

                var dirItem = FileTreeItem.FromDirectory(dir, loadChildren: true);
                projectItem.Children.Add(dirItem);
            }

            // Add files (grouped by relation - .cs, .axaml + .axaml.cs, etc.)
            var files = Directory.GetFiles(projectDir);
            var groupedFiles = GroupRelatedFiles(files);
            
            foreach (var group in groupedFiles.OrderBy(g => GetFileSortOrder(g.Key)))
            {
                var mainFile = group.Value.First();
                var fileItem = FileTreeItem.FromFile(mainFile);
                
                // Add related files as children
                foreach (var relatedFile in group.Value.Skip(1))
                {
                    var relatedItem = FileTreeItem.FromFile(relatedFile);
                    relatedItem.IsCodeBehind = true;
                    relatedItem.AssociatedFile = mainFile;
                    fileItem.Children.Add(relatedItem);
                }
                
                projectItem.Children.Add(fileItem);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading project contents: {ex.Message}");
        }
    }

    /// <summary>
    /// Add solution-level items (files not in any project)
    /// </summary>
    private void AddSolutionLevelItems(FileTreeItem solutionItem, string folderPath, List<SolutionProjectInfo> projects)
    {
        try
        {
            // Get project directories
            var projectDirs = projects
                .Select(p => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(folderPath, p.RelativePath))))
                .Where(d => !string.IsNullOrEmpty(d))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Add solution-level files
            foreach (var file in Directory.GetFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                
                // Skip solution file itself (already added as root)
                if (fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip hidden files
                if (fileName.StartsWith(".")) continue;

                var fileItem = FileTreeItem.FromFile(file);
                solutionItem.Children.Add(fileItem);
            }

            // Add directories that are not projects
            foreach (var dir in Directory.GetDirectories(folderPath))
            {
                var dirName = Path.GetFileName(dir);
                
                // Skip hidden and excluded directories
                if (ShouldExcludeDirectory(dirName)) continue;
                
                // Skip project directories
                if (projectDirs.Contains(dir)) continue;

                var dirItem = FileTreeItem.FromDirectory(dir, loadChildren: true);
                solutionItem.Children.Add(dirItem);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding solution-level items: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if directory should be excluded
    /// </summary>
    private bool ShouldExcludeDirectory(string dirName)
    {
        return dirName.StartsWith(".") ||
               dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
               dirName.Equals(".idea", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Group related files (e.g., .axaml + .axaml.cs)
    /// </summary>
    private Dictionary<string, List<string>> GroupRelatedFiles(string[] files)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var file in files.OrderBy(f => f))
        {
            var fileName = Path.GetFileName(file);
            
            // Skip hidden files
            if (fileName.StartsWith(".")) continue;

            // Determine the group key
            var groupKey = GetFileGroupKey(fileName);
            
            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new List<string>();
            }
            groups[groupKey].Add(file);
        }

        // Sort files within each group (main file first)
        foreach (var group in groups.Values)
        {
            group.Sort((a, b) => GetFileSortOrder(Path.GetFileName(a)).CompareTo(GetFileSortOrder(Path.GetFileName(b))));
        }

        return groups;
    }

    /// <summary>
    /// Get group key for a file
    /// </summary>
    private string GetFileGroupKey(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        
        // AXAML grouping
        if (lower.EndsWith(".axaml.cs"))
            return fileName.Substring(0, fileName.Length - 3); // Remove .cs
        if (lower.EndsWith(".axaml"))
            return fileName;

        // XAML grouping
        if (lower.EndsWith(".xaml.cs"))
            return fileName.Substring(0, fileName.Length - 3);
        if (lower.EndsWith(".xaml"))
            return fileName;

        // Razor grouping
        if (lower.EndsWith(".razor.cs") || lower.EndsWith(".razor.css"))
            return fileName.Substring(0, fileName.LastIndexOf('.'));
        if (lower.EndsWith(".razor"))
            return fileName;

        // Designer files
        if (lower.EndsWith(".designer.cs"))
            return fileName.Substring(0, fileName.Length - 12) + ".cs";

        return fileName;
    }

    /// <summary>
    /// Get sort order for files (lower = first)
    /// </summary>
    private int GetFileSortOrder(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        
        // Project files first
        if (lower.EndsWith(".csproj") || lower.EndsWith(".fsproj") || lower.EndsWith(".vbproj"))
            return 0;
        
        // Config files
        if (lower == "appsettings.json" || lower == "app.config" || lower == "web.config")
            return 1;
        
        // Main UI files
        if (lower.EndsWith(".axaml") && !lower.EndsWith(".axaml.cs"))
            return 2;
        if (lower.EndsWith(".razor") && !lower.EndsWith(".razor.cs"))
            return 2;
        
        // CSS files
        if (lower.EndsWith(".razor.css"))
            return 3;
        
        // Code-behind
        if (lower.EndsWith(".axaml.cs") || lower.EndsWith(".xaml.cs") || lower.EndsWith(".razor.cs"))
            return 4;
        
        // Regular C# files
        if (lower.EndsWith(".cs"))
            return 5;
        
        return 99;
    }

    /// <summary>
    /// Initialize file system watcher for automatic tree refresh
    /// </summary>
    private void InitializeFileWatcher(string folderPath)
    {
        // Dispose existing watcher
        _fileWatcher?.Dispose();
        _refreshDebounceTimer?.Dispose();
        
        try
        {
            _fileWatcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                               NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            
            // Setup debounce timer (500ms delay to batch multiple changes)
            _refreshDebounceTimer = new Timer(500);
            _refreshDebounceTimer.AutoReset = false;
            _refreshDebounceTimer.Elapsed += OnRefreshDebounceTimerElapsed;
            
            // Subscribe to events
            _fileWatcher.Created += OnFileSystemChanged;
            _fileWatcher.Deleted += OnFileSystemChanged;
            _fileWatcher.Renamed += OnFileSystemRenamed;
            
            _fileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing file watcher: {ex.Message}");
        }
    }
    
    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Skip changes in excluded directories
        if (ShouldIgnorePath(e.FullPath)) return;
        
        RequestDebouncedRefresh();
    }
    
    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        // Skip changes in excluded directories
        if (ShouldIgnorePath(e.FullPath) && ShouldIgnorePath(e.OldFullPath)) return;
        
        RequestDebouncedRefresh();
    }
    
    private bool ShouldIgnorePath(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        return lowerPath.Contains("\\bin\\") || 
               lowerPath.Contains("\\obj\\") || 
               lowerPath.Contains("\\.git\\") ||
               lowerPath.Contains("\\.vs\\") ||
               lowerPath.Contains("\\.idea\\") ||
               lowerPath.Contains("\\node_modules\\");
    }
    
    private void RequestDebouncedRefresh()
    {
        lock (_refreshLock)
        {
            _refreshPending = true;
            _refreshDebounceTimer?.Stop();
            _refreshDebounceTimer?.Start();
        }
    }
    
    private void OnRefreshDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_refreshLock)
        {
            if (_refreshPending)
            {
                _refreshPending = false;
                // Invoke refresh on UI thread
                RefreshTreeAction?.Invoke();
            }
        }
    }

    /// <summary>
    /// Refresh the file tree, preserving expanded state
    /// </summary>
    public void RefreshFileTree()
    {
        if (string.IsNullOrEmpty(CurrentProjectPath) || !Directory.Exists(CurrentProjectPath))
        {
            StatusText = "No project folder to refresh";
            return;
        }

        // Save expanded state
        var expandedPaths = new HashSet<string>();
        CollectExpandedPaths(FileTreeItems, expandedPaths);
        
        // Reload the tree using solution-aware logic
        LoadProjectFolder(CurrentProjectPath);
        
        // Restore expanded state
        RestoreExpandedPaths(FileTreeItems, expandedPaths);
        
        StatusText = "File tree refreshed";
    }
    
    private void CollectExpandedPaths(ObservableCollection<FileTreeItem> items, HashSet<string> expandedPaths)
    {
        foreach (var item in items)
        {
            if (item.IsExpanded && item.IsDirectory)
            {
                expandedPaths.Add(item.FullPath);
                CollectExpandedPaths(item.Children, expandedPaths);
            }
        }
    }
    
    private void RestoreExpandedPaths(ObservableCollection<FileTreeItem> items, HashSet<string> expandedPaths)
    {
        foreach (var item in items)
        {
            if (item.IsDirectory && expandedPaths.Contains(item.FullPath))
            {
                // Load children first if not already loaded
                item.LoadChildren();
                item.IsExpanded = true;
                RestoreExpandedPaths(item.Children, expandedPaths);
            }
        }
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

    public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
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
    
    #region IDisposable
    
    private bool _disposed;
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            _fileWatcher?.Dispose();
            _refreshDebounceTimer?.Dispose();
        }
        
        _disposed = true;
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
    public string FileName { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Icon for severity
    /// </summary>
    public string SeverityIcon => Severity switch
    {
        DiagnosticSeverity.Error => "⛔",
        DiagnosticSeverity.Warning => "⚠",
        DiagnosticSeverity.Info => "ℹ",
        _ => "💡"
    };
    
    /// <summary>
    /// Color for severity
    /// </summary>
    public string SeverityColor => Severity switch
    {
        DiagnosticSeverity.Error => "#FFF38BA8",
        DiagnosticSeverity.Warning => "#FFF5A623",
        DiagnosticSeverity.Info => "#FF89B4FA",
        _ => "#FFA6E3A1"
    };
    
    /// <summary>
    /// Location display string
    /// </summary>
    public string Location => $"{FileName}({Line},{Column})";
}

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info,
    Hint
}