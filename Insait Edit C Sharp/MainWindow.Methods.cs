// ============================================================
//  MainWindow.Methods.cs — partial class
//  Core helper methods: editor, tree, build, analysis, etc.
// ============================================================
using Avalonia.Controls;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow
{
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
            _viewModel.ActiveTab = existingTab;
            ShowTabInEditor(existingTab);
            UpdateAxamlPreviewButton();
            return;
        }

        try
        {
            string content;
            // Use FileShare.ReadWrite so we don't crash if GitHub Copilot CLI
            // or another external tool currently has the file open for writing.
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);
                content = sr.ReadToEnd();
            }
            catch (IOException)
            {
                // Fallback: give the other process a moment then try again
                System.Threading.Thread.Sleep(100);
                content = File.ReadAllText(filePath);
            }
            var language = GetLanguageForFile(filePath);

            var tab = new EditorTab
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                Content = content,
                Language = language,
                IsDirty = false
            };

            _viewModel.Tabs.Add(tab);
            _viewModel.ActiveTab = tab;
            ShowTabInEditor(tab);
            UpdateAxamlPreviewButton();

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

        // Hide the welcome screen when a file is opened
        UpdateWelcomeScreenVisibility();

        // Capture local reference to avoid closure issues
        var editor = _insaitEditor;

        // Set file path first so the editor knows the language for highlighting
        if (!string.IsNullOrEmpty(tab.FilePath))
            editor.SetFilePath(tab.FilePath);

        // Set project context so all project .cs files are available for Roslyn
        editor.SetProjectContext(_projectPath ?? _viewModel.CurrentProjectPath);

        // Push content with language hint
        editor.SetContent(tab.Content, tab.Language);

        // Update tab visual styles (active + error/warning indicators)
        UpdateTabButtonStyles();
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
        var projectDir = _projectPath ?? _viewModel.CurrentProjectPath;
        if (!string.IsNullOrEmpty(projectDir))
        {
            if (Directory.Exists(projectDir))
                return projectDir;
            var pd = Path.GetDirectoryName(projectDir);
            if (!string.IsNullOrEmpty(pd) && Directory.Exists(pd))
                return pd;
        }

        return Environment.CurrentDirectory;
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
                if (obj is FileTreeItem fi)
                    result.Add(fi);
            }
            if (result.Count > 0)
                return result;
        }

        // Fallback — walk view-model tree for IsSelected == true
        CollectSelectedInTree(_viewModel.FileTreeItems, result);
        return result;
    }

    private static void CollectSelectedInTree(ObservableCollection<FileTreeItem> items, List<FileTreeItem> result)
    {
        foreach (var item in items)
        {
            if (item.IsSelected) result.Add(item);
            CollectSelectedInTree(item.Children, result);
        }
    }

    private static FileTreeItem? FindSelectedInTree(FileTreeItem item)
    {
        if (item.IsSelected) return item;
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

