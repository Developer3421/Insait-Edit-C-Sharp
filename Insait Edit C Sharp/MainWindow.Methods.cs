// ============================================================
//  MainWindow.Methods.cs — partial class
//  Core helper methods: editor, tree, build, analysis, etc.
// ============================================================
using Avalonia.Controls;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow
{
    private bool _isFileTreeServiceActive = true;

    // ═══════════════════════════════════════════════════════════
    //  Code Analysis Service — initialization & wiring
    // ═══════════════════════════════════════════════════════════

    private void InitializeCodeAnalysisService()
    {
        _codeAnalysisService.AnalysisCompleted += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isAnalysisInProgress = false;
                _viewModel.Problems.Clear();
                foreach (var d in e.Diagnostics)
                    _viewModel.Problems.Add(d);

                if (e.Success)
                    _viewModel.StatusText = $"Analysis complete: {e.Diagnostics.Count} issue(s)";
                else
                    _viewModel.StatusText = $"Analysis failed: {e.ErrorMessage}";

                UpdateTabDiagnosticIndicators();
            });
        };

        _codeAnalysisService.AnalysisProgress += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
                _viewModel.StatusText = $"Analysing… {e.Message} ({e.Current}/{e.Total})");
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Analyse project (called from menu / keyboard shortcut)
    // ═══════════════════════════════════════════════════════════

    private async Task AnalyzeProjectAsync()
    {
        if (_isAnalysisInProgress)
        {
            _viewModel.StatusText = "Analysis already in progress";
            return;
        }

        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded";
            return;
        }

        _isAnalysisInProgress = true;
        _viewModel.StatusText = "Analysing project…";
        SwitchToolWindowPanel("problems");

        try
        {
            await _codeAnalysisService.AnalyzeProjectWithCallbackAsync(projectPath);
        }
        catch (Exception ex)
        {
            _isAnalysisInProgress = false;
            _viewModel.StatusText = $"Analysis error: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Open file in editor
    // ═══════════════════════════════════════════════════════════

    private void OpenFileInEditor(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        // Skip known binary files — they cannot be shown in a text editor
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".dll" or ".exe" or ".pdb" or ".obj" or ".o" or ".lib"
            or ".so" or ".dylib" or ".a"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".bmp"
            or ".svg" or ".webp" or ".tiff" or ".tif"
            or ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2"
            or ".nupkg" or ".snupkg"
            or ".ttf" or ".otf" or ".woff" or ".woff2"
            or ".mp3" or ".mp4" or ".avi" or ".mkv" or ".wav" or ".flac"
            or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx")
        {
            _viewModel.StatusText = $"Cannot open binary file: {Path.GetFileName(filePath)}";
            return;
        }

        // Check if already open — just switch to it
        var existingTab = _viewModel.FindTabByPath(filePath);
        if (existingTab != null)
        {
            try
            {
                var existingData = ReadFileWithMetadata(filePath);
                existingTab.EncodingKind = existingData.EncodingKind;
                existingTab.LineEnding = existingData.LineEnding;
                existingTab.UsesTabs = existingData.UsesTabs;
                existingTab.IndentSize = existingData.IndentSize;
            }
            catch
            {
                // Ignore metadata refresh failures and keep the tab open.
            }

            _viewModel.ActiveTab = existingTab;
            ShowTabInEditor(existingTab);
            UpdateAxamlPreviewButton();
            UpdateEditorStatusBar();
            return;
        }

        try
        {
            (string Content, string EncodingKind, string LineEnding, bool UsesTabs, int IndentSize) fileData;
            try
            {
                fileData = ReadFileWithMetadata(filePath);
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(100);
                fileData = ReadFileWithMetadata(filePath);
            }

            var content = fileData.Content;
            var language = GetLanguageForFile(filePath);

            var tab = new EditorTab
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                Content = content,
                Language = language,
                EncodingKind = fileData.EncodingKind,
                LineEnding = fileData.LineEnding,
                UsesTabs = fileData.UsesTabs,
                IndentSize = fileData.IndentSize,
                IsDirty = false
            };

            _viewModel.Tabs.Add(tab);
            _viewModel.ActiveTab = tab;
            ShowTabInEditor(tab);
            UpdateAxamlPreviewButton();
            UpdateEditorStatusBar();

            _viewModel.StatusText = $"Opened: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error opening file: {ex.Message}";
        }
    }

    /// <summary>
    /// Push a tab's content into the editor.
    /// If the editor isn't ready yet, stores the tab as pending — it will be
    /// applied automatically once InitializeInsaitEditor() completes.
    /// Waits for TextMate to be installed before setting content so that
    /// syntax highlighting is applied correctly on first open.
    /// </summary>
    private void ShowTabInEditor(EditorTab tab)
    {
        if (_insaitEditor == null)
        {
            _pendingTab = tab;
            return;
        }

        // Update welcome/empty screen visibility (depends on whether this is a start tab)
        UpdateWelcomeScreenVisibility();

        // Welcome tab — just show the overlay page, don't push anything to the editor
        if (tab.IsWelcomeTab)
        {
            UpdateTabButtonStyles();
            UpdateEditorStatusBar();
            return;
        }

        // Capture local reference to avoid closure issues
        var editor = _insaitEditor;

        // Set file path first so the editor knows the language for highlighting
        if (!string.IsNullOrEmpty(tab.FilePath))
            editor.SetFilePath(tab.FilePath);

        // Set project context so all project .cs files are available for Roslyn
        editor.SetProjectContext(GetRoslynProjectContextPath(tab.FilePath));

        // Push content with language hint
        editor.SetContent(tab.Content, tab.Language);

        // Update tab visual styles (active + error/warning indicators)
        UpdateTabButtonStyles();
        UpdateEditorStatusBar();
    }

    // ═══════════════════════════════════════════════════════════
    //  Cancel build
    // ═══════════════════════════════════════════════════════════

    private void CancelBuild()
    {
        _buildService.CancelBuild();
        _isBuildInProgress = false;
        UpdateBuildButtons();
        _viewModel.StatusText = "Build cancelled";
    }

    // ═══════════════════════════════════════════════════════════
    //  Restore NuGet packages
    // ═══════════════════════════════════════════════════════════

    private async Task RestorePackagesAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path))
        {
            _viewModel.StatusText = "No project loaded";
            return;
        }

        _viewModel.StatusText = "Restoring packages…";
        SwitchToolWindowPanel("build");
        _buildOutput.Clear();
        UpdateBuildOutput();

        try
        {
            bool success;
            success = await _buildService.RestoreAsync(path);
            _viewModel.StatusText = success ? "Packages restored successfully" : "Package restore failed";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Restore error: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Get current project path (resolves to .csproj/etc.)
    // ═══════════════════════════════════════════════════════════

    private string? GetCurrentProjectPath()
    {
        var path = _projectPath ?? _viewModel.CurrentProjectPath;
        if (string.IsNullOrEmpty(path)) return null;

        // If it's already a project/solution file return as-is
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".csproj" or ".fsproj" or ".vbproj" or ".sln" or ".slnx")
                return path;
        }

        // Search in directory for project/solution files
        if (Directory.Exists(path))
        {
            var slnx = Directory.GetFiles(path, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (slnx != null) return slnx;

            var sln = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (sln != null) return sln;

            var csproj = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj != null) return csproj;

            var fsproj = Directory.GetFiles(path, "*.fsproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (fsproj != null) return fsproj;
        }

        return path;
    }

    private string? GetRoslynProjectContextPath(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            var dir = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                try
                {
                    var csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (!string.IsNullOrEmpty(csproj))
                        return Path.GetDirectoryName(csproj);
                }
                catch
                {
                    // Ignore directory probe errors and keep walking upward.
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        var currentPath = GetCurrentProjectPath();
        return NuGetReferenceResolver.ResolveProjectDirectory(currentPath)
               ?? (File.Exists(currentPath) ? Path.GetDirectoryName(currentPath) : currentPath);
    }

    // ═══════════════════════════════════════════════════════════
    //  Get target directory for new items (based on selection)
    // ═══════════════════════════════════════════════════════════

    private string GetTargetDirectory()
    {
        var item = GetSelectedTreeItem();

        if (item != null)
        {
            if (item.IsDirectory)
                return item.FullPath;

            var dir = Path.GetDirectoryName(item.FullPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }

        // Fallback: project directory
        var projectDir = GetWorkspaceRootDirectory();
        if (!string.IsNullOrEmpty(projectDir))
        {
            if (Directory.Exists(projectDir))
                return projectDir;
            var pd = Path.GetDirectoryName(projectDir);
            if (!string.IsNullOrEmpty(pd) && Directory.Exists(pd))
                return pd;
        }

        return string.Empty;
    }

    private bool HasActiveWorkspace()
    {
        var workspaceRoot = GetWorkspaceRootDirectory();
        return !string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot);
    }

    private bool TryGetWritableTargetDirectory(out string targetDir)
    {
        targetDir = GetTargetDirectory();

        if (!HasActiveWorkspace() || string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            _viewModel.StatusText = "No active project or solution. Create or open one first.";
            return false;
        }

        return true;
    }

    private void ActivateFileTreeService()
    {
        _isFileTreeServiceActive = true;
        _viewModel.RefreshTreeAction = () =>
        {
            if (!_isFileTreeServiceActive)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (_isFileTreeServiceActive)
                    RefreshFileTree();
            });
        };
    }

    private void DeactivateFileTreeService()
    {
        _isFileTreeServiceActive = false;
        _viewModel.RefreshTreeAction = null;
        _viewModel.StopFileWatcher();
    }

    private async Task LoadWorkspaceDirectoryAsync(string directory)
    {
        ActivateFileTreeService();
        _projectPath = directory;
        _viewModel.CurrentProjectPath = directory;
        _viewModel.FileTreeItems.Clear();
        await _viewModel.LoadProjectFolderAsync(directory);
        UpdateTitle();
    }

    private async Task PrepareWorkspaceRootDeletionAsync(string? workspaceRoot)
    {
        _buildService.CancelBuild();
        _publishService.Cancel();
        _runConfigService.Stop();


        if (_terminalControl != null)
        {
            _terminalControl.StopCurrentProcess();
            _terminalControl.WorkingDirectory = Environment.CurrentDirectory;
        }

        _copilotCliService.WorkingDirectory = Environment.CurrentDirectory;

        if (_nugetPanelControl != null)
            await _nugetPanelControl.SetProjectPathAsync(string.Empty);

        try
        {
            var processDirectory = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(workspaceRoot) &&
                (PathsEqual(processDirectory, workspaceRoot) || IsPathInsideDirectory(processDirectory, workspaceRoot)))
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
            }
        }
        catch
        {
            // Ignore cwd reset failures; deletion retries below still run.
        }

        await Task.Delay(150);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        DeactivateFileTreeService();
    }

    private async Task<bool> TryDeleteDirectoryWithRetriesAsync(string path, int attempts = 6, int delayMs = 200)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return true;

        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;

                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (attempt < attempts)
                    await Task.Delay(delayMs);
            }
        }

        if (lastError != null)
            _viewModel.StatusText = "Error deleting workspace folder: " + lastError.Message;

        return false;
    }

    /// <summary>
    /// Returns true when the workspace directory contains no user files —
    /// only standard build / IDE artefact directories (bin, obj, .vs, .git, etc.).
    /// Used to decide whether to auto-delete the workspace root after the user
    /// deletes all visible project items.
    /// </summary>
    internal static bool IsWorkspaceEffectivelyEmpty(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot)) return true;
        return !HasUserFilesInDirectory(workspaceRoot);
    }

    private static readonly HashSet<string> _buildArtifactDirNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".vs", ".git", ".github", ".idea", ".vscode", "node_modules" };

    private static readonly HashSet<string> _workspaceHiddenFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        { ".gitignore", ".gitattributes", ".editorconfig" };

    private static bool HasUserFilesInDirectory(string directory)
    {
        try
        {
            // Any file at this level counts as a user file
            if (Directory.EnumerateFiles(directory).Any())
                return true;

            // Recurse into non-ignored subdirectories
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);
                if (_buildArtifactDirNames.Contains(dirName)) continue;
                if (HasUserFilesInDirectory(subDir)) return true;
            }
        }
        catch { /* ignore access / io errors during the check */ }

        return false;
    }

    internal static bool HasVisibleWorkspaceFiles(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot)) return false;
        return HasVisibleWorkspaceFilesRecursive(workspaceRoot);
    }

    private static bool HasVisibleWorkspaceFilesRecursive(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var fileName = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                if (fileName.StartsWith(".", StringComparison.Ordinal) ||
                    _workspaceHiddenFileNames.Contains(fileName) ||
                    fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".suo", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".DotSettings.user", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);
                if (string.IsNullOrWhiteSpace(dirName))
                    continue;

                if (dirName.StartsWith(".", StringComparison.Ordinal) || _buildArtifactDirNames.Contains(dirName))
                    continue;

                if (HasVisibleWorkspaceFilesRecursive(subDir))
                    return true;
            }
        }
        catch
        {
            // Ignore IO / access errors when evaluating visual emptiness.
        }

        return false;
    }


    private string? GetWorkspaceRootDirectory()
    {
        var path = _projectPath ?? _viewModel.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Directory.Exists(path))
            return NormalizePath(path);

        if (File.Exists(path))
        {
            var directory = Path.GetDirectoryName(path);
            return string.IsNullOrWhiteSpace(directory) ? null : NormalizePath(directory);
        }

        var fallbackDirectory = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(fallbackDirectory) ? null : NormalizePath(fallbackDirectory);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedDirectory = NormalizePath(directory);

        return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void CloseTabsInsidePath(string path)
    {
        foreach (var tab in _viewModel.Tabs.ToList())
        {
            if (string.IsNullOrWhiteSpace(tab.FilePath))
                continue;

            if (PathsEqual(tab.FilePath, path) || IsPathInsideDirectory(tab.FilePath, path))
                _viewModel.CloseTab(tab);
        }
    }

    private void ClearWorkspaceState(string statusText)
    {
        foreach (var tab in _viewModel.Tabs.ToList())
            _viewModel.CloseTab(tab);

        _viewModel.FileTreeItems.Clear();
        _viewModel.CurrentProjectPath = null;
        _projectPath = null;
        FileTreeItem.SetAllowedRootPaths(null);

        UpdateTitle();
        UpdateWelcomeScreenVisibility();

        if (_terminalControl != null)
            _terminalControl.WorkingDirectory = Environment.CurrentDirectory;

        _viewModel.StatusText = statusText;

        DeactivateFileTreeService();
    }

    // ═══════════════════════════════════════════════════════════
    //  Get the currently selected file-tree item (single)
    // ═══════════════════════════════════════════════════════════

    private FileTreeItem? GetSelectedTreeItem()
    {
        return GetSelectedTreeItems().FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════
    //  Get ALL currently selected file-tree items (multi)
    // ═══════════════════════════════════════════════════════════

    private List<FileTreeItem> GetSelectedTreeItems()
    {
        var result = new List<FileTreeItem>();

        // Try the TreeView SelectedItems first (works for multi-select mode)
        var tree = this.FindControl<TreeView>("FileTreeView");
        if (tree?.SelectedItems != null)
        {
            foreach (var obj in tree.SelectedItems)
            {
                if (obj is FileTreeItem fi && fi.IsSelectableInTree)
                    result.Add(fi);
            }

            if (_contextMenuTargetItem != null)
            {
                if (IsSelectableTreeItem(_contextMenuTargetItem) && result.Any(item =>
                        ReferenceEquals(item, _contextMenuTargetItem) ||
                        PathsEqual(item.FullPath, _contextMenuTargetItem.FullPath)))
                {
                    return result.Distinct().ToList();
                }

                return new List<FileTreeItem> { _contextMenuTargetItem };
            }

            if (result.Count > 0)
                return result.Distinct().ToList();
        }

        // Fallback — walk view-model tree for IsSelected == true
        CollectSelectedInTree(_viewModel.FileTreeItems, result);

        if (_contextMenuTargetItem != null)
        {
            if (IsSelectableTreeItem(_contextMenuTargetItem) && result.Any(item =>
                    ReferenceEquals(item, _contextMenuTargetItem) ||
                    PathsEqual(item.FullPath, _contextMenuTargetItem.FullPath)))
            {
                return result.Distinct().ToList();
            }

            return new List<FileTreeItem> { _contextMenuTargetItem };
        }

        return result;
    }

    private static void CollectSelectedInTree(ObservableCollection<FileTreeItem> items, List<FileTreeItem> result)
    {
        foreach (var item in items)
        {
            if (item.IsSelected && item.IsSelectableInTree) result.Add(item);
            CollectSelectedInTree(item.Children, result);
        }
    }

    private static FileTreeItem? FindSelectedInTree(FileTreeItem item)
    {
        if (item.IsSelected && item.IsSelectableInTree) return item;
        foreach (var child in item.Children)
        {
            var found = FindSelectedInTree(child);
            if (found != null) return found;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  Language detection helper
    // ═══════════════════════════════════════════════════════════

    private static string GetLanguageForFile(string filePath)
    {
        var name = Path.GetFileName(filePath).ToLowerInvariant();

        // Handle special filenames without extensions
        if (name is "dockerfile" or ".gitignore" or ".dockerignore" or ".env"
            or ".editorconfig" or "makefile" or "cmakelists.txt")
            return "plaintext";

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".axaml" => "xml",
            ".xaml" => "xml",
            ".xml" => "xml",
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".html" => "html",
            ".css" => "css",
            ".md" => "markdown",
            ".sh" => "shell",
            ".bat" or ".cmd" => "bat",
            ".ps1" => "powershell",
            ".py" => "python",
            ".fs" => "fsharp",
            ".vb" => "vb",
            ".sql" => "sql",
            ".csproj" or ".fsproj" or ".vbproj" => "xml",
            ".sln" or ".slnx" => "plaintext",
            ".txt" or ".log" or ".csv" or ".cfg" or ".ini" or ".conf" => "plaintext",
            ".gitattributes" or ".gitmodules" => "plaintext",
            ".props" or ".targets" => "xml",
            ".razor" or ".cshtml" => "html",
            ".scss" or ".sass" or ".less" => "css",
            ".jsx" or ".tsx" => "javascript",
            ".rs" => "rust",
            ".go" => "go",
            ".java" => "java",
            ".cpp" or ".c" or ".h" or ".hpp" => "cpp",
            ".rb" => "ruby",
            ".php" => "php",
            ".swift" => "swift",
            ".kt" or ".kts" => "kotlin",
            ".toml" => "toml",
            _ => "plaintext"
        };
    }
}

