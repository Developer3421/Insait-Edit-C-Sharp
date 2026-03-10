using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
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
    public string? SolutionFolder { get; set; }
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
    /// Load a project folder into the file tree (fire and forget - for backward compatibility)
    /// </summary>
    public void LoadProjectFolder(string folderPath)
    {
        // Fire and forget - call async version
        _ = LoadProjectFolderAsync(folderPath);
    }

    /// <summary>
    /// Load a project folder into the file tree asynchronously
    /// </summary>
    public async Task LoadProjectFolderAsync(string folderPath)
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
                await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Directory not found: {folderPath}");
                return;
            }
        }

        folderPath = Path.GetFullPath(folderPath);
        FileTreeItem.SetAllowedRootPaths(new[] { folderPath });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentProjectPath = folderPath;
            FileTreeItems.Clear();
            StatusText = "Loading...";
        });

        // Check if there's a solution file in the folder
        var solutionFile = await Task.Run(() => FindSolutionFileInDirectory(folderPath));

        System.Diagnostics.Debug.WriteLine($"LoadProjectFolderAsync: folderPath={folderPath}, solutionFile={solutionFile ?? "null"}");

        if (!string.IsNullOrEmpty(solutionFile))
        {
            System.Diagnostics.Debug.WriteLine($"LoadProjectFolderAsync: Loading as solution");
            // Load as solution with projects (Rider-style)
            await LoadSolutionStructureAsync(folderPath, solutionFile);
        }
        else
        {
            // Check for project file
            var projectFile = await Task.Run(() => FindProjectFileInDirectory(folderPath));
            if (!string.IsNullOrEmpty(projectFile))
            {
                // Load as single project
                await LoadProjectStructureAsync(folderPath, projectFile);
            }
            else
            {
                // Load as regular folder
                var rootItem = await Task.Run(() => FileTreeItem.FromDirectory(folderPath, loadChildren: true));
                rootItem.IsExpanded = true;
                await Dispatcher.UIThread.InvokeAsync(() => FileTreeItems.Add(rootItem));
            }
        }

        // Initialize file watcher
        InitializeFileWatcher(folderPath);

        await Dispatcher.UIThread.InvokeAsync(() => StatusText = $"Opened folder: {Path.GetFileName(folderPath)}");
    }

    /// <summary>
    /// Find solution file in directory
    /// </summary>
    private string? FindSolutionFileInDirectory(string directory)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: Searching in '{directory}'");
            System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: Directory.Exists = {Directory.Exists(directory)}");

            // List all files first for debugging
            try
            {
                var allFiles = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly);
                System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: All files: {string.Join(", ", allFiles.Select(f => Path.GetFileName(f)))}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: Error listing all files: {ex.Message}");
            }

            // First look for .slnx files (new format) - use enumeration for better performance
            foreach (var file in Directory.EnumerateFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly))
            {
                System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: Found .slnx file: {file}");
                return file;
            }

            // Then look for .sln files (legacy format)
            foreach (var file in Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly))
            {
                System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: Found .sln file: {file}");
                return file;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: Error: {ex.Message}");
        }
        System.Diagnostics.Debug.WriteLine($"FindSolutionFileInDirectory: No solution file found, returning null");
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

            projectFiles = Directory.GetFiles(directory, "*.nfproj", SearchOption.TopDirectoryOnly);
            if (projectFiles.Length > 0) return projectFiles[0];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error finding project file: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Load solution structure with projects (like JetBrains Rider) - async version
    /// </summary>
    private async Task LoadSolutionStructureAsync(string folderPath, string solutionFile)
    {
        System.Diagnostics.Debug.WriteLine($"LoadSolutionStructureAsync: START - folderPath={folderPath}, solutionFile={solutionFile}");

        // Force re-read the file from disk
        System.Diagnostics.Debug.WriteLine($"LoadSolutionStructureAsync: Reading solution file content...");
        var solutionContent = await File.ReadAllTextAsync(solutionFile);
        System.Diagnostics.Debug.WriteLine($"LoadSolutionStructureAsync: Solution file content:\n{solutionContent}");

        var solutionName = Path.GetFileNameWithoutExtension(solutionFile);

        // Parse solution file on background thread
        var projects = await Task.Run(() => ParseSolutionFile(solutionFile));
        System.Diagnostics.Debug.WriteLine($"LoadSolutionStructureAsync: Found {projects.Count} projects in solution");

        foreach (var proj in projects)
        {
            System.Diagnostics.Debug.WriteLine($"LoadSolutionStructureAsync: Project - Name={proj.Name}, Path={proj.RelativePath}");
        }

        var allowedRoots = projects
            .Select(project => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(folderPath, project.RelativePath))))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Append(folderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        FileTreeItem.SetAllowedRootPaths(allowedRoots!);

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

        System.Diagnostics.Debug.WriteLine($"LoadSolutionStructureAsync: Created solutionItem - Name={solutionItem.Name}, IsDirectory={solutionItem.IsDirectory}, ItemType={solutionItem.ItemType}, Icon={solutionItem.Icon}");

        // Load project items on background thread
        var projectItems = await Task.Run(() =>
        {
            var items = new List<FileTreeItem>();

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
                    ItemType = DetermineProjectItemType(projectPath),
                    IsSolutionItem = true,
                    ProjectGuid = projectInfo.Guid,
                    Description = GetProjectDescription(projectPath),
                    IsExpanded = false
                };

                // Load project children (source files)
                LoadProjectContents(projectItem, projectDir);

                items.Add(projectItem);
            }

            return items;
        });

        // Add project items to solution
        foreach (var projectItem in projectItems)
        {
            solutionItem.Children.Add(projectItem);
        }

        // Add solution-level items on background thread
        await Task.Run(() => AddSolutionLevelItems(solutionItem, folderPath, projects));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            FileTreeItems.Add(solutionItem);
            StatusText = $"Loaded solution: {solutionName} ({projects.Count} projects)";
        });
    }

    /// <summary>
    /// Load single project structure - async version
    /// </summary>
    private async Task LoadProjectStructureAsync(string folderPath, string projectFile)
    {
        FileTreeItem.SetAllowedRootPaths(new[] { folderPath });

        var projectName = Path.GetFileNameWithoutExtension(projectFile);

        var projectItem = await Task.Run(() =>
        {
            var item = new FileTreeItem
            {
                Name = projectName,
                FullPath = folderPath,
                IsDirectory = true,
                ItemType = DetermineProjectItemType(projectFile),
                IsSolutionItem = false,
                Description = GetProjectDescription(projectFile),
                IsExpanded = true
            };

            LoadProjectContents(item, folderPath);
            return item;
        });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            FileTreeItems.Add(projectItem);
            StatusText = $"Loaded project: {projectName}";
        });
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
                ItemType = DetermineProjectItemType(projectPath),
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
            ItemType = DetermineProjectItemType(projectFile),
            IsSolutionItem = false,
            Description = GetProjectDescription(projectFile),
            IsExpanded = true
        };

        LoadProjectContents(projectItem, folderPath);

        FileTreeItems.Add(projectItem);
        StatusText = $"Loaded project: {projectName}";
    }

    /// <summary>
    /// Parse solution file to extract projects (supports .sln and .slnx)
    /// </summary>
    private List<SolutionProjectInfo> ParseSolutionFile(string solutionPath)
    {
        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();

        if (ext == ".slnx")
        {
            return ParseSlnxFile(solutionPath);
        }
        else
        {
            return ParseSlnFile(solutionPath);
        }
    }

    /// <summary>
    /// Parse slnx file (XML format)
    /// </summary>
    private List<SolutionProjectInfo> ParseSlnxFile(string solutionPath)
    {
        var projects = new List<SolutionProjectInfo>();

        try
        {
            var content = File.ReadAllText(solutionPath);
            var doc = System.Xml.Linq.XDocument.Parse(content);
            var root = doc.Root;
            if (root == null) return projects;

            // Parse projects from root level
            foreach (var projectElement in root.Elements("Project"))
            {
                var pathAttr = projectElement.Attribute("Path")?.Value;
                if (string.IsNullOrEmpty(pathAttr)) continue;

                // Normalize path separators
                var normalizedPath = pathAttr.Replace("/", "\\");
                var projectName = Path.GetFileNameWithoutExtension(normalizedPath);

                projects.Add(new SolutionProjectInfo
                {
                    Name = projectName,
                    RelativePath = normalizedPath,
                    Guid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
                    TypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}" // SDK-style
                });
            }

            // Parse projects from folders
            foreach (var folder in root.Elements("Folder"))
            {
                var folderName = folder.Attribute("Name")?.Value ?? "Folder";

                foreach (var projectElement in folder.Elements("Project"))
                {
                    var pathAttr = projectElement.Attribute("Path")?.Value;
                    if (string.IsNullOrEmpty(pathAttr)) continue;

                    var normalizedPath = pathAttr.Replace("/", "\\");
                    var projectName = Path.GetFileNameWithoutExtension(normalizedPath);

                    projects.Add(new SolutionProjectInfo
                    {
                        Name = projectName,
                        RelativePath = normalizedPath,
                        Guid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
                        TypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}",
                        SolutionFolder = folderName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing slnx file: {ex.Message}");
        }

        return projects;
    }

    /// <summary>
    /// Parse sln file (legacy format)
    /// </summary>
    private List<SolutionProjectInfo> ParseSlnFile(string solutionPath)
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
                        path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase))
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
            System.Diagnostics.Debug.WriteLine($"Error parsing sln file: {ex.Message}");
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

    private static FileTreeItemType DetermineProjectItemType(string projectFilePath)
    {
        return FileTreeItemType.Project;
    }

    /// <summary>
    /// Load contents of a project directory
    /// </summary>
    private void LoadProjectContents(FileTreeItem projectItem, string projectDir)
    {
        try
        {
            // Clear any existing children first to prevent duplicates
            projectItem.Children.Clear();

            // Find the project file to parse NuGet packages
            var projectFile = FindProjectFileInDirectory(projectDir);

            // First add special folders (Dependencies, Properties, etc.)
            var dependenciesFolder = new FileTreeItem
            {
                Name = "Dependencies",
                FullPath = projectDir,  // Virtual folder
                IsDirectory = true,
                ItemType = FileTreeItemType.DependenciesFolder,
                IsSolutionItem = true,
                IsLoaded = true  // Mark as loaded to prevent auto-loading files
            };

            // Populate NuGet packages if project file exists
            if (!string.IsNullOrEmpty(projectFile))
            {
                var packages = ParseNuGetPackages(projectFile);
                foreach (var package in packages.OrderBy(p => p.Name))
                {
                    var packageItem = new FileTreeItem
                    {
                        Name = package.Name,
                        FullPath = projectFile, // Reference to project file
                        IsDirectory = false,
                        ItemType = FileTreeItemType.NuGetPackage,
                        Description = package.Version
                    };
                    dependenciesFolder.Children.Add(packageItem);
                }
            }

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

            // Mark project as loaded to prevent duplicate loading when expanded
            projectItem.IsLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading project contents: {ex.Message}");
        }
    }

    /// <summary>
    /// Represents a NuGet package reference
    /// </summary>
    private class NuGetPackageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parse NuGet package references from a project file (.csproj or .nfproj)
    /// </summary>
    private List<NuGetPackageInfo> ParseNuGetPackages(string projectFile)
    {
        var packages = new List<NuGetPackageInfo>();

        try
        {
            // For .nfproj files, parse packages.config instead
            if (projectFile.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase))
            {
                var projectDir = Path.GetDirectoryName(projectFile);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    var packagesConfigPath = Path.Combine(projectDir, "packages.config");
                    if (File.Exists(packagesConfigPath))
                    {
                        var configContent = File.ReadAllText(packagesConfigPath);
                        var pkgRegex = new System.Text.RegularExpressions.Regex(
                            @"<package\s+id=""(?<Name>[^""]+)""\s+version=""(?<Version>[^""]+)""",
                            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        foreach (System.Text.RegularExpressions.Match match in pkgRegex.Matches(configContent))
                        {
                            packages.Add(new NuGetPackageInfo
                            {
                                Name = match.Groups["Name"].Value,
                                Version = match.Groups["Version"].Value
                            });
                        }
                    }

                    // Also check for Reference hints in nfproj (HintPath references)
                    var nfContent = File.ReadAllText(projectFile);
                    var refRegex = new System.Text.RegularExpressions.Regex(
                        @"<Reference\s+Include=""(?<Name>[^""]+)""",
                        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    foreach (System.Text.RegularExpressions.Match match in refRegex.Matches(nfContent))
                    {
                        var name = match.Groups["Name"].Value;
                        if (!packages.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            packages.Add(new NuGetPackageInfo
                            {
                                Name = name,
                                Version = ""
                            });
                        }
                    }
                }
                return packages;
            }

            var content = File.ReadAllText(projectFile);

            // Use regex to find all PackageReference elements
            var packageRegex = new System.Text.RegularExpressions.Regex(
                @"<PackageReference\s+Include=""(?<Name>[^""]+)""\s+Version=""(?<Version>[^""]+)""",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in packageRegex.Matches(content))
            {
                packages.Add(new NuGetPackageInfo
                {
                    Name = match.Groups["Name"].Value,
                    Version = match.Groups["Version"].Value
                });
            }

            // Also try format with Version as child element
            var altPackageRegex = new System.Text.RegularExpressions.Regex(
                @"<PackageReference\s+Include=""(?<Name>[^""]+)""[^>]*>\s*<Version>(?<Version>[^<]+)</Version>",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match match in altPackageRegex.Matches(content))
            {
                var name = match.Groups["Name"].Value;
                // Only add if not already in list
                if (!packages.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    packages.Add(new NuGetPackageInfo
                    {
                        Name = name,
                        Version = match.Groups["Version"].Value
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing NuGet packages: {ex.Message}");
        }

        return packages;
    }

    /// <summary>
    /// Add solution-level items (files not in any project)
    /// </summary>
    private void AddSolutionLevelItems(FileTreeItem solutionItem, string folderPath, List<SolutionProjectInfo> projects)
    {
        // Intentionally left empty.
        // In solution mode, the tree should only show the solution node and its projects,
        // without surfacing arbitrary repository/root files next to project contents.
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

    public void StopFileWatcher()
    {
        try
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }

            if (_refreshDebounceTimer != null)
            {
                _refreshDebounceTimer.Stop();
                _refreshDebounceTimer.Dispose();
                _refreshDebounceTimer = null;
            }

            lock (_refreshLock)
            {
                _refreshPending = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping file watcher: {ex.Message}");
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
        // Fire and forget async version
        _ = RefreshFileTreeAsync();
    }

    /// <summary>
    /// Refresh the file tree asynchronously, preserving expanded state
    /// </summary>
    public async Task RefreshFileTreeAsync()
    {
        if (string.IsNullOrEmpty(CurrentProjectPath) || !Directory.Exists(CurrentProjectPath))
        {
            FileTreeItem.SetAllowedRootPaths(null);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FileTreeItems.Clear();
                StatusText = "No project folder to refresh";
            });
            return;
        }

        // Save expanded state
        var expandedPaths = new HashSet<string>();
        await Dispatcher.UIThread.InvokeAsync(() => CollectExpandedPaths(FileTreeItems, expandedPaths));

        // Reload the tree using solution-aware logic
        await LoadProjectFolderAsync(CurrentProjectPath);

        // Restore expanded state
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RestoreExpandedPaths(FileTreeItems, expandedPaths);
            StatusText = "File tree refreshed";
        });
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
            // Fire and forget async version
            _ = LoadProjectFolderAsync(directory);
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