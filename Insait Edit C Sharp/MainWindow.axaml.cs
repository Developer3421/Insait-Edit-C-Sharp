using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.ViewModels;
using Insait_Edit_C_Sharp.Services;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.InsaitCodeEditor;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Esp.Windows;
using Insait_Edit_C_Sharp.Esp.Models;
using Insait_Edit_C_Sharp.Esp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow : Window
{
    private bool _isMaximized;
    private PixelPoint _restorePosition;
    private EditorTab? _pendingTab; // tab waiting to be shown once editor is ready
    private Size _restoreSize;
    private readonly MainViewModel _viewModel;
    private readonly FileService _fileService;
    private readonly BuildService _buildService;
    private readonly NanoBuildService _nanoBuildService;
    private readonly CodeAnalysisService _codeAnalysisService;
    private readonly RunConfigurationService _runConfigService;
    private readonly PublishService _publishService;
    private readonly CopilotCliService _copilotCliService;
    private string? _projectPath;
    private InsaitEditor? _insaitEditor;
    private TerminalControl? _terminalControl;
    private bool _isBuildInProgress;
    private bool _isAnalysisInProgress;
    private readonly StringBuilder _buildOutput = new();
    private DispatcherTimer? _autoSaveTimer;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? projectPath)
    {
        InitializeComponent();
        
        // Set up column constraints for splitters (JetBrains Rider style)
        SetupColumnConstraints();
        
        _viewModel = new MainViewModel();
        _fileService = new FileService();
        _buildService = new BuildService();
        _nanoBuildService = new NanoBuildService();
        _codeAnalysisService = new CodeAnalysisService();
        _runConfigService = new RunConfigurationService();
        _publishService = new PublishService();
        _copilotCliService = new CopilotCliService();
        _projectPath = projectPath;
        
        DataContext = _viewModel;
        
        // Wire up file watcher refresh action to run on UI thread
        _viewModel.RefreshTreeAction = () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshFileTree();
            });
        };
        
        // Set initial window position for restore
        _restoreSize = new Size(Width, Height);

        // Wire up the editor that is declared directly in AXAML
        InitializeInsaitEditor();

        // Initialize Build Service events
        InitializeBuildService();
        
        // Initialize Code Analysis Service events
        InitializeCodeAnalysisService();

        // Initialize Search panel
        InitializeSearchPanel();
        
        // Load project if specified, otherwise load current directory
        if (!string.IsNullOrEmpty(projectPath))
        {
            LoadProject(projectPath);
        }
        else
        {
            // Load current executable directory as default
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            // Go up to find the project root (where .csproj is)
            var projectDir = FindProjectRoot(currentDir);
            if (!string.IsNullOrEmpty(projectDir))
            {
                _projectPath = projectDir;
                LoadProject(projectDir);
            }
        }

        // Initialize Terminal with project directory
        InitializeTerminal();

        // Update title
        UpdateTitle();
        
        // Setup keyboard shortcuts
        SetupKeyboardShortcuts();
        
        // Apply localization and subscribe to language changes
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(ApplyLocalization);
    }
    
    private void SetupColumnConstraints()
    {
        // Col 0: Activity bar - fixed 52px, never resized
        // Col 1: Side panel - 250px default, min 150, max 550
        // Col 2: Left splitter - 5px fixed
        // Col 3: Editor - fills remaining space, min 300px
        // Col 4: Right splitter - 5px fixed
        // Col 5: AI panel - 300px default, min 220, max 600
        var mainGrid = this.FindControl<Grid>("MainContentGrid");
        if (mainGrid == null || mainGrid.ColumnDefinitions.Count < 6) return;

        var sideCol   = mainGrid.ColumnDefinitions[1];
        var editorCol = mainGrid.ColumnDefinitions[3];
        var aiCol     = mainGrid.ColumnDefinitions[5];

        sideCol.MinWidth   = 150;
        sideCol.MaxWidth   = 550;
        editorCol.MinWidth = 300;
        aiCol.MinWidth     = 220;
        aiCol.MaxWidth     = 600;
    }

    private void SetupKeyboardShortcuts()
    {
        KeyDown += OnWindowKeyDown;
    }
    
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+S - Save current file
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Ctrl+Shift+S - Save All
                await SaveAllFilesAsync();
            }
            else
            {
                // Ctrl+S - Save current
                await SaveCurrentFileAsync();
            }
            e.Handled = true;
        }
        // Ctrl+O - Open file
        else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await OpenNewFileAsync();
            e.Handled = true;
        }
        // Ctrl+N - New file / Ctrl+Shift+N - New Project
        else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Ctrl+Shift+N - New Project
                var currentSolution = FindSolutionFile();
                var projectWindow = new NewProjectWindow(currentSolution);
                var projectResult = await projectWindow.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(projectResult))
                {
                    if (projectResult.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                        projectResult.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(projectResult))
                        {
                            var solutionDir = Path.GetDirectoryName(projectResult);
                            if (!string.IsNullOrEmpty(solutionDir) && Directory.Exists(solutionDir))
                            {
                                _projectPath = solutionDir;
                                _viewModel.CurrentProjectPath = solutionDir;
                                _viewModel.FileTreeItems.Clear();
                                await _viewModel.LoadProjectFolderAsync(solutionDir);
                                UpdateTitle();
                                _viewModel.StatusText = $"Created solution: {Path.GetFileName(projectResult)}";
                            }
                        }
                    }
                    else if (projectResult.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                             || projectResult.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                             || projectResult.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                    {
                        var projDir = Path.GetDirectoryName(projectResult) ?? "";
                        var slnFile = FindSolutionFileFromPath(projDir);
                        if (!string.IsNullOrEmpty(slnFile) && File.Exists(slnFile))
                        {
                            var slnDir = Path.GetDirectoryName(slnFile) ?? projDir;
                            _projectPath = slnDir;
                            _viewModel.CurrentProjectPath = slnDir;
                            _viewModel.FileTreeItems.Clear();
                            await _viewModel.LoadProjectFolderAsync(slnDir);
                            UpdateTitle();
                        }
                        else if (!string.IsNullOrEmpty(projDir) && Directory.Exists(projDir))
                        {
                            _projectPath = projDir;
                            _viewModel.CurrentProjectPath = projDir;
                            _viewModel.FileTreeItems.Clear();
                            await _viewModel.LoadProjectFolderAsync(projDir);
                            UpdateTitle();
                        }
                    }
                }
            }
            else
            {
                // Ctrl+N - New file
                CreateNewFile();
            }
            e.Handled = true;
        }
        // Ctrl+B - Build project
        else if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Ctrl+Shift+B - Rebuild
                await BuildProjectAsync();
            }
            else
            {
                // Ctrl+B - Build
                await BuildProjectAsync();
            }
            e.Handled = true;
        }
        // Ctrl+Shift+A - Analyze code
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            await AnalyzeProjectAsync();
            e.Handled = true;
        }
        // F5 - Run project
        else if (e.Key == Key.F5)
        {
            await RunProjectAsync();
            e.Handled = true;
        }
        // Ctrl+W - Close current tab
        else if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await CloseCurrentTabAsync();
            e.Handled = true;
        }
        // Ctrl+Shift+F - Find in Files (content search)
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _searchTabIsFiles = false;
            EnsureSidePanelVisible();
            SwitchSidePanel("explorer");
            ToggleExplorerSearch(open: true);
            UpdateSearchTabUI();
            this.FindControl<TextBox>("ContentSearchInputBox")?.Focus();
            e.Handled = true;
        }
        // Ctrl+P - Find file by name
        else if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _searchTabIsFiles = true;
            EnsureSidePanelVisible();
            SwitchSidePanel("explorer");
            ToggleExplorerSearch(open: true);
            UpdateSearchTabUI();
            this.FindControl<TextBox>("SearchInputBox")?.Focus();
            e.Handled = true;
        }
        // Ctrl+Shift+Z - Zen Mode
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ToggleZenMode();
            e.Handled = true;
        }
        // Ctrl+Shift+P - AXAML Preview
        else if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            await OpenAxamlPreviewAsync();
            e.Handled = true;
        }
        // Ctrl+Shift+E - Toggle Explorer panel
        else if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ToggleSidePanel("explorer");
            e.Handled = true;
        }
        // Ctrl+Shift+I - Toggle AI / Right panel
        else if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ToggleAIPanel();
            e.Handled = true;
        }
        // Ctrl+` - Toggle Terminal / bottom panel
        else if (e.Key == Key.OemTilde && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ToggleBottomPanel();
            e.Handled = true;
        }
        // Escape - exit Zen Mode if active
        else if (e.Key == Key.Escape && _isZenMode)
        {
            ToggleZenMode();
            e.Handled = true;
        }
    }

    private async Task OpenNewFileAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("C# Files") { Patterns = new[] { "*.cs" } },
                new("XAML Files") { Patterns = new[] { "*.axaml", "*.xaml" } },
                new("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            OpenFileInEditor(file.Path.LocalPath);
        }
    }
    
    private void CreateNewFile()
    {
        var newTab = new EditorTab
        {
            FileName = "Untitled.cs",
            FilePath = string.Empty,
            Content = string.Empty,
            Language = "csharp",
            IsDirty = false
        };
        
        _viewModel.Tabs.Add(newTab);
        _viewModel.ActiveTab = newTab;
        
        if (_insaitEditor != null)
        {
            _insaitEditor.SetContent(string.Empty, "csharp");
        }
        
        _viewModel.StatusText = "New file created";
    }
    
    private async Task CloseCurrentTabAsync()
    {
        if (_viewModel.ActiveTab != null)
        {
            var tab = _viewModel.ActiveTab;
            
            // Check if the tab has unsaved changes
            if (tab.IsDirty)
            {
                var result = await ShowSaveConfirmationDialogAsync(tab.FileName);
                
                if (result == SaveConfirmationResult.Save)
                {
                    await SaveCurrentFileAsync();
                }
                else if (result == SaveConfirmationResult.Cancel)
                {
                    return;
                }
            }
            
            _viewModel.CloseTab(tab);
            
            // Update editor with active tab content
            if (_insaitEditor != null && _viewModel.ActiveTab != null)
            {
                _insaitEditor.SetContent(_viewModel.ActiveTab.Content, _viewModel.ActiveTab.Language);
            }
            else if (_insaitEditor != null)
            {
                _insaitEditor.SetContent("", "plaintext");
            }
            UpdateWelcomeScreenVisibility();
        }
    }

    private string? FindProjectRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            // Look for .slnx files first (new format)
            var slnxFiles = dir.GetFiles("*.slnx");
            if (slnxFiles.Length > 0)
            {
                return dir.FullName;
            }
            
            // Look for .sln files
            var slnFiles = dir.GetFiles("*.sln");
            if (slnFiles.Length > 0)
            {
                return dir.FullName;
            }
            
            var csprojFiles = dir.GetFiles("*.csproj");
            if (csprojFiles.Length > 0)
            {
                return dir.Parent?.FullName ?? dir.FullName;
            }
            
            var nfprojFiles = dir.GetFiles("*.nfproj");
            if (nfprojFiles.Length > 0)
            {
                return dir.Parent?.FullName ?? dir.FullName;
            }
            
            dir = dir.Parent;
        }
        return startPath;
    }

    private void InitializeInsaitEditor()
    {
        // Create the editor in code-behind and place it inside the named Border placeholder.
        // This guarantees correct layout — no AXAML template/size issues.
        var container = this.FindControl<Border>("EditorContainer");
        if (container == null)
        {
            // Retry after window is loaded
            this.Loaded += (_, _) => InitializeInsaitEditor();
            return;
        }

        _insaitEditor = new InsaitEditor();
        container.Child = _insaitEditor;

        WireEditorEvents();

        // Apply any tab that was queued before the editor was ready
        if (_pendingTab != null)
        {
            var tab = _pendingTab;
            _pendingTab = null;
            ShowTabInEditor(tab);
        }

        // Show/hide welcome screen based on whether any tab is open
        UpdateWelcomeScreenVisibility();
    }

    private void WireEditorEvents()
    {
        _insaitEditor!.EditorReady             += OnEditorReady;
        _insaitEditor!.ContentChanged          += OnEditorContentChanged;
        _insaitEditor!.ContentChangedWithValue += OnEditorContentChangedWithValue;
        _insaitEditor!.CursorPositionChanged   += OnCursorPositionChanged;
        _insaitEditor!.UndoRedoManager.StateChanged += OnUndoRedoStateChanged;
        _insaitEditor!.GoToDefinitionRequested += OnGoToDefinitionRequested;
        _insaitEditor!.RenameCompleted         += OnRenameCompleted;
    }

    /// <summary>
    /// Handles Go to Definition navigation to a different file.
    /// Opens the file in editor and navigates to the specified line/column.
    /// </summary>
    private void OnGoToDefinitionRequested(object? sender, InsaitCodeEditor.GoToDefinitionRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpenFileInEditor(e.FilePath);
            // After opening, navigate to line/col
            _insaitEditor?.GoToLine(e.Line, e.Column);
            _viewModel.StatusText = $"Definition: {Path.GetFileName(e.FilePath)} Ln {e.Line}, Col {e.Column}";
        });
    }

    /// <summary>
    /// Handles Rename Symbol completion — applies changes to other open tabs.
    /// </summary>
    private void OnRenameCompleted(object? sender, InsaitCodeEditor.RenameCompletedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var currentFile = _viewModel.ActiveTab?.FilePath;
            // Apply changes to other open tabs
            foreach (var change in e.Result.Changes)
            {
                if (string.Equals(change.FilePath, currentFile, StringComparison.OrdinalIgnoreCase))
                    continue; // already applied by InsaitEditor

                var tab = _viewModel.FindTabByPath(change.FilePath);
                if (tab != null)
                {
                    // Re-read the file from disk to reflect rename
                    try
                    {
                        tab.Content = File.ReadAllText(change.FilePath);
                        tab.IsDirty = true;
                    }
                    catch { }
                }
            }
            _viewModel.StatusText = $"Renamed: {e.Result.OldName} → {e.Result.NewName} ({e.Result.Changes.Count} changes)";
        });
    }

    private void OnEditorReady(object? sender, EventArgs e)
    {
        _viewModel.StatusText = "Editor Ready";
    }

    private void OnEditorContentChanged(object? sender, EventArgs e)
    {
        // Mark current tab as modified - INotifyPropertyChanged handles UI update
        if (_viewModel.ActiveTab != null)
        {
            _viewModel.ActiveTab.IsDirty = true;
        }
    }

    private void OnEditorContentChangedWithValue(object? sender, ContentChangedEventArgs e)
    {
        // Update the active tab's content with the new value from the editor
        if (_viewModel.ActiveTab != null)
        {
            _viewModel.ActiveTab.Content = e.NewContent;
            _viewModel.ActiveTab.IsDirty = true;
            _viewModel.ActiveTab.LastModified = DateTime.Now;
            _viewModel.StatusText = $"Modified: {_viewModel.ActiveTab.FileName}";
        }

        // Debounced auto-save: reset timer on every keystroke, save 2 s after last change
        if (_autoSaveTimer == null)
        {
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _autoSaveTimer.Tick += async (_, _) =>
            {
                _autoSaveTimer.Stop();
                await AutoSaveCurrentFileAsync();
            };
        }
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// Silently saves the active tab to disk if it has a valid file path.
    /// Called by the auto-save timer — does not show a Save-As dialog.
    /// </summary>
    private async Task AutoSaveCurrentFileAsync()
    {
        var tab = _viewModel.ActiveTab;
        if (tab == null || string.IsNullOrEmpty(tab.FilePath)) return;
        try
        {
            if (_insaitEditor != null) tab.Content = await _insaitEditor.GetContentAsync();
            await File.WriteAllTextAsync(tab.FilePath, tab.Content);
            tab.IsDirty = false;
            _insaitEditor?.MarkAsSaved();
            _viewModel.StatusText = $"Auto-saved: {tab.FileName}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auto-save failed: {ex.Message}");
        }
    }

    private void UpdateTabTitle(EditorTab tab)
    {
        // UI updates automatically through INotifyPropertyChanged
        // This method can be used for additional UI updates if needed
    }

    private void OnCursorPositionChanged(object? sender, CursorPositionChangedEventArgs e)
    {
        _viewModel.StatusText = $"Ln {e.Line}, Col {e.Column}";
        UpdateCursorPositionDisplay(e.Line, e.Column);
    }

    private void LoadProject(string projectPath)
    {
        // Fire and forget async version for backwards compatibility
        _ = LoadProjectAsync(projectPath);
    }

    private async Task LoadProjectAsync(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            var extension = Path.GetExtension(projectPath).ToLowerInvariant();
            
            if (extension == ".sln" || extension == ".slnx")
            {
                // Load solution - load its directory
                var directory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    // Always keep _projectPath as the directory for consistency
                    _projectPath = directory;
                    _viewModel.CurrentProjectPath = directory;
                    await _viewModel.LoadProjectFolderAsync(directory);
                    _viewModel.StatusText = $"Loaded solution: {Path.GetFileName(projectPath)}";
                    
                    // Load run configurations for the solution
                    _ = _runConfigService.LoadConfigurationsAsync(projectPath);
                    
                    // Update Git panel
                    UpdateGitPanel(directory);
                    
                    UpdateTitle();
                }
                else
                {
                    _viewModel.StatusText = $"Solution directory not found: {directory}";
                }
            }
            else if (extension == ".csproj" || extension == ".fsproj" || extension == ".vbproj" || extension == ".nfproj")
            {
                // Load project - load its directory
                var directory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    // Always keep _projectPath as the directory for consistency
                    _projectPath = directory;
                    _viewModel.CurrentProjectPath = directory;
                    await _viewModel.LoadProjectFolderAsync(directory);
                    _viewModel.StatusText = $"Loaded project: {Path.GetFileName(projectPath)}";
                    
                    // Load run configurations for the project
                    _ = _runConfigService.LoadConfigurationsAsync(projectPath);
                    
                    // Update Git panel
                    UpdateGitPanel(directory);
                    
                    UpdateTitle();
                }
                else
                {
                    _viewModel.StatusText = $"Project directory not found: {directory}";
                }
            }
            else
            {
                // Open as single file
                _viewModel.OpenFile(projectPath);
                OpenFileInEditor(projectPath);
            }
        }
        else if (Directory.Exists(projectPath))
        {
            // Load folder
            await _viewModel.LoadProjectFolderAsync(projectPath);
            _viewModel.StatusText = $"Loaded folder: {Path.GetFileName(projectPath)}";
            
            // Try to find solution/project and load configurations
            var solutionFile = FindSolutionFile();
            if (!string.IsNullOrEmpty(solutionFile))
            {
                _ = _runConfigService.LoadConfigurationsAsync(solutionFile);
            }
            
            // Update Git panel
            UpdateGitPanel(projectPath);
        }
        else
        {
            _viewModel.StatusText = $"Path not found: {projectPath}";
        }
    }

    private void UpdateGitPanel(string path)
    {
        // Git is now a separate window — refresh it if open
        if (_gitWindow != null && _gitWindow.IsVisible)
        {
            _ = _gitWindow.RefreshAsync();
        }
    }

    private void UpdateTitle()
    {
        if (!string.IsNullOrEmpty(_projectPath))
        {
            Title = $"{Path.GetFileName(_projectPath)} - Insait Edit";
        }
        else
        {
            Title = "Insait Edit";
        }
    }

    /// <summary>Apply current language strings to all named UI elements.</summary>
    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;

        // ── Title Bar buttons ────────────────────────────────────
        SetButtonText("MainMenuButton", "☰", L("TitleBar.Menu"));
        SetButtonTooltip("MainMenuButton", L("Tooltip.Menu"));

        SetButtonTooltip("BuildProjectButton", L("Tooltip.Build"));

        SetButtonTooltip("AnalyzeProjectButton", L("Tooltip.Analyze"));

        SetButtonTooltip("RunConfigDropdownButton", L("Tooltip.RunConfig"));
        SetButtonTooltip("EditConfigurationsButton", L("Tooltip.EditConfig"));

        SetButtonText("RunProjectButton", "▶", L("TitleBar.Run"), textFgColor: "#FFA6E3A1");
        SetButtonTooltip("RunProjectButton", L("Tooltip.Run"));

        SetButtonText("DebugProjectButton", "🐛", L("TitleBar.Debug"));
        SetButtonTooltip("DebugProjectButton", L("Tooltip.Debug"));

        SetButtonText("CancelBuildButton", "⏹", L("TitleBar.Stop"));
        SetButtonTooltip("CancelBuildButton", L("Tooltip.Stop"));

        SetButtonTooltip("PublishButton", L("Tooltip.Publish"));
        SetButtonTooltip("MsixManagerButton", L("Tooltip.MsixManager"));
        SetButtonTooltip("UndoButton", L("Tooltip.Undo"));
        SetButtonTooltip("RedoButton", L("Tooltip.Redo"));
        SetButtonTooltip("NewWindowButton", L("Tooltip.NewWindow"));
        SetButtonTooltip("RestartButton", L("Tooltip.Restart"));

        // ── Sidebar tooltips ─────────────────────────────────────
        SetButtonTooltip("ExplorerButton", L("Sidebar.Explorer"));
        SetButtonTooltip("SearchButton", L("Sidebar.Search"));
        SetButtonTooltip("GitButton", L("Sidebar.Git"));
        SetButtonTooltip("NuGetButton", L("Sidebar.NuGet"));
        SetButtonTooltip("LedPanelButton", L("Sidebar.LedPanel"));
        SetButtonTooltip("AccountButton", L("Sidebar.Account"));
        SetButtonTooltip("SettingsButton", L("Sidebar.Settings"));

        // ── Panel Headers ────────────────────────────────────────
        var sideHeader = this.FindControl<TextBlock>("SidePanelHeaderText");
        if (sideHeader != null) sideHeader.Text = L("Panel.Explorer");

        // ── Explorer actions tooltips ────────────────────────────
        SetButtonTooltip("NewFileButton", L("Explorer.NewFile"));
        SetButtonTooltip("NewFolderButton", L("Explorer.NewFolder"));
        SetButtonTooltip("RefreshTreeButton", L("Explorer.Refresh"));

        // ── AXAML Preview button ─────────────────────────────────
        SetButtonTooltip("AxamlPreviewButton", L("Tooltip.PreviewAxaml"));

        // ── Bottom panel tabs (set TextBlock inside StackPanel) ──
        SetToolTabText("ProblemsTabButton", L("Tab.Problems"), 1);
        SetToolTabText("BuildTabButton", L("Tab.Build"), 1);
        SetToolTabText("RunTabButton", L("Tab.Run"), 1);
        SetToolTabText("TerminalTabButton", L("Tab.Terminal"), 1);

        // ── Bottom tool action tooltips ──────────────────────────
        SetButtonTooltip("NewTerminalButton", L("Tooltip.NewTerminal"));
        SetButtonTooltip("SplitTerminalButton", L("Tooltip.SplitTerminal"));
        SetButtonTooltip("ClearTerminalButton", L("Tooltip.ClearAll"));
        SetButtonTooltip("MinimizePanelButton", L("Tooltip.Minimize"));
        SetButtonTooltip("MaximizePanelButton", L("Tooltip.Maximize"));
        SetButtonTooltip("HidePanelButton", L("Tooltip.HidePanel"));
        SetButtonTooltip("RefreshAnalysisButton", L("Tooltip.RefreshAnalysis"));
        SetButtonTooltip("ClearProblemsButton", L("Tooltip.ClearProblems"));

        // ── Problems panel ───────────────────────────────────────
        var noProblems = this.FindControl<TextBlock>("NoProblemsText");
        if (noProblems != null) noProblems.Text = L("Problems.NoProblems");

        var msgText = this.FindControl<TextBlock>("ProblemsMessagesText");
        if (msgText != null) msgText.Text = L("Problems.Messages");

        // ── Build/Run output ─────────────────────────────────────
        var buildOut = this.FindControl<SelectableTextBlock>("BuildOutputText");
        if (buildOut != null && buildOut.Text == "Build output will appear here...")
            buildOut.Text = L("Output.BuildPlaceholder");
        var runOut = this.FindControl<SelectableTextBlock>("RunOutputText");
        if (runOut != null && runOut.Text == "Run output will appear here...")
            runOut.Text = L("Output.RunPlaceholder");

        // ── Status bar tooltips ──────────────────────────────────
        SetButtonTooltip("StatusProblemsButton", L("Status.ViewProblems"));
        SetButtonTooltip("NotificationsButton", L("Status.Notifications"));

        // ── AI Panel ─────────────────────────────────────────────
        SetButtonTooltip("GitHubTuiButton", L("AI.Tooltip.OpenTUI"));
        SetButtonTooltip("ClearAIChatButton", L("AI.Tooltip.NewChat"));
        SetButtonTooltip("CloseAIPanelButton", L("AI.Tooltip.ClosePanel"));
        SetButtonTooltip("SendAIButton", L("AI.Tooltip.Execute"));

        var aiInput = this.FindControl<TextBox>("AIChatInput");
        if (aiInput != null) aiInput.Watermark = L("AI.InputPlaceholder");

        // ── AI Welcome message ────────────────────────────────────
        var aiWelcomeTitle = this.FindControl<TextBlock>("AiWelcomeTitle");
        if (aiWelcomeTitle != null) aiWelcomeTitle.Text = L("AI.Ready");
        var aiWelcomeManage = this.FindControl<TextBlock>("AiWelcomeManageFiles");
        if (aiWelcomeManage != null) aiWelcomeManage.Text = L("AI.ManageFiles");
        var aiWelcomeGitHub = this.FindControl<TextBlock>("AiWelcomeGitHubCommands");
        if (aiWelcomeGitHub != null) aiWelcomeGitHub.Text = L("AI.GitHubCommands");
        var aiWelcomeHelp = this.FindControl<TextBlock>("AiWelcomeTypeHelp");
        if (aiWelcomeHelp != null) aiWelcomeHelp.Text = L("AI.TypeHelp");

        // ── Search panel ─────────────────────────────────────────
        var searchInput = this.FindControl<TextBox>("SearchInputBox");
        if (searchInput != null) searchInput.Watermark = L("Search.FileNamePlaceholder");
        var contentInput = this.FindControl<TextBox>("ContentSearchInputBox");
        if (contentInput != null) contentInput.Watermark = L("Search.ContentPlaceholder");
        var replaceInput = this.FindControl<TextBox>("ReplaceInputBox");
        if (replaceInput != null) replaceInput.Watermark = L("Search.ReplacePlaceholder");

        SetButtonTooltip("SearchTabFilesBtn", L("Search.FindFiles"));
        SetButtonTooltip("SearchTabContentBtn", L("Search.FindInFiles"));
        SetButtonTooltip("SearchFileNamesButton", L("Search.FindFiles"));
        SetButtonTooltip("SearchContentButton", L("Search.FindInFiles"));
        SetButtonTooltip("ReplaceAllButton", L("Search.ReplaceAll"));

        // Context menu items
        var ctxRun = this.FindControl<MenuItem>("ContextMenuRun");
        if (ctxRun != null) ctxRun.Header = L("Context.Run");
        var ctxNew = this.FindControl<MenuItem>("ContextMenuNew");
        if (ctxNew != null) ctxNew.Header = L("Context.New");
        var ctxAdd = this.FindControl<MenuItem>("ContextMenuAdd");
        if (ctxAdd != null) ctxAdd.Header = L("Context.Add");
        var ctxBuild = this.FindControl<MenuItem>("ContextMenuBuild");
        if (ctxBuild != null) ctxBuild.Header = L("Context.Build");
        var ctxRebuild = this.FindControl<MenuItem>("ContextMenuRebuild");
        if (ctxRebuild != null) ctxRebuild.Header = L("Context.Rebuild");
        var ctxClean = this.FindControl<MenuItem>("ContextMenuClean");
        if (ctxClean != null) ctxClean.Header = L("Context.Clean");
        var ctxNuGet = this.FindControl<MenuItem>("ContextMenuNuGet");
        if (ctxNuGet != null) ctxNuGet.Header = L("Context.ManageNuGet");
        var ctxAddRef = this.FindControl<MenuItem>("ContextMenuAddReference");
        if (ctxAddRef != null) ctxAddRef.Header = L("Context.AddReference");
        var ctxCut = this.FindControl<MenuItem>("ContextMenuCut");
        if (ctxCut != null) ctxCut.Header = L("Context.Cut");
        var ctxCopy = this.FindControl<MenuItem>("ContextMenuCopy");
        if (ctxCopy != null) ctxCopy.Header = L("Context.Copy");
        var ctxPaste = this.FindControl<MenuItem>("ContextMenuPaste");
        if (ctxPaste != null) ctxPaste.Header = L("Context.Paste");
        var ctxRename = this.FindControl<MenuItem>("ContextMenuRename");
        if (ctxRename != null) ctxRename.Header = L("Context.Rename");
        var ctxDelete = this.FindControl<MenuItem>("ContextMenuDelete");
        if (ctxDelete != null) ctxDelete.Header = L("Context.Delete");
        var ctxCopyPath = this.FindControl<MenuItem>("ContextMenuCopyPath");
        if (ctxCopyPath != null) ctxCopyPath.Header = L("Context.CopyPath");
        var ctxExplorer = this.FindControl<MenuItem>("ContextMenuOpenExplorer");
        if (ctxExplorer != null) ctxExplorer.Header = L("Context.OpenExplorer");
        var ctxTerminal = this.FindControl<MenuItem>("ContextMenuOpenTerminal");
        if (ctxTerminal != null) ctxTerminal.Header = L("Context.OpenTerminal");
        var ctxRemove = this.FindControl<MenuItem>("ContextMenuRemoveFromSolution");
        if (ctxRemove != null) ctxRemove.Header = L("Context.RemoveFromSolution");
        var ctxUnload = this.FindControl<MenuItem>("ContextMenuUnloadProject");
        if (ctxUnload != null) ctxUnload.Header = L("Context.UnloadProject");
        var ctxGit = this.FindControl<MenuItem>("ContextMenuGit");
        if (ctxGit != null) ctxGit.Header = L("Context.Git");
        var ctxProps = this.FindControl<MenuItem>("ContextMenuProperties");
        if (ctxProps != null) ctxProps.Header = L("Context.Properties");

        // ── Run Config Name (Default label) ──────────────────────
        var runConfigName = this.FindControl<TextBlock>("RunConfigNameText");
        if (runConfigName != null)
        {
            // Only update if still showing a "Default"-like value (not a real config name)
            var cur = runConfigName.Text ?? "";
            if (cur == "Default" || cur == "За замовчуванням" || cur == "Stандартная" ||
                cur == "Standard" || cur == "Varsayılan" || cur == "По умолчанию")
                runConfigName.Text = L("RunConfig.Default");
        }

        // ── Welcome Screen ───────────────────────────────────────
        ApplyWelcomeScreenLocalization(L);
    }

    // ── Localization helpers ─────────────────────────────────────────
    private void SetButtonTooltip(string name, string tooltip)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) ToolTip.SetTip(btn, tooltip);
    }

    private void SetButtonText(string name, string icon, string label, string? textFgColor = null)
    {
        var btn = this.FindControl<Button>(name);
        if (btn?.Content is StackPanel sp && sp.Children.Count >= 2)
        {
            if (sp.Children[1] is TextBlock tb) tb.Text = label;
        }
    }

    private void SetToolTabText(string name, string text, int textBlockIndex)
    {
        var btn = this.FindControl<Button>(name);
        if (btn?.Content is StackPanel sp && sp.Children.Count > textBlockIndex && sp.Children[textBlockIndex] is TextBlock tb)
            tb.Text = text;
    }

    // ═══════════════════════════════════════════════════════════
    //  Welcome Screen
    // ═══════════════════════════════════════════════════════════

    private void UpdateWelcomeScreenVisibility()
    {
        var panel = this.FindControl<Border>("WelcomeScreenPanel");
        if (panel == null) return;
        panel.IsVisible = _viewModel.ActiveTab == null;
    }

    private void ApplyWelcomeScreenLocalization(Func<string, string> L)
    {
        var subtitle = this.FindControl<TextBlock>("WelcomeMainSubtitle");
        if (subtitle != null) subtitle.Text = L("WelcomeScreen.Subtitle");

        var cardNew = this.FindControl<TextBlock>("WelcomeCardNewProject");
        if (cardNew != null) cardNew.Text = L("WelcomeScreen.NewProject");

        var cardOpen = this.FindControl<TextBlock>("WelcomeCardOpen");
        if (cardOpen != null) cardOpen.Text = L("WelcomeScreen.Open");

        var cardClone = this.FindControl<TextBlock>("WelcomeCardClone");
        if (cardClone != null) cardClone.Text = L("WelcomeScreen.Clone");

        var tip = this.FindControl<TextBlock>("WelcomeTip");
        if (tip != null) tip.Text = L("WelcomeScreen.Tip");

        var shortcutNew = this.FindControl<TextBlock>("WelcomeShortcutNew");
        if (shortcutNew != null) shortcutNew.Text = L("WelcomeScreen.NewProject");

        var shortcutOpen = this.FindControl<TextBlock>("WelcomeShortcutOpen");
        if (shortcutOpen != null) shortcutOpen.Text = L("WelcomeScreen.Open");
    }

    private async void WelcomeNewProject_Click(object? sender, RoutedEventArgs e)
    {
        var currentSolution = FindSolutionFile();
        var win = new NewProjectWindow(currentSolution ?? _projectPath);
        var result = await win.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(result))
            await LoadProjectAsync(result);
    }

    private async void WelcomeOpen_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Project or Solution",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Solution / Project Files") { Patterns = new[] { "*.sln", "*.slnx", "*.csproj", "*.fsproj" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count > 0)
            await LoadProjectAsync(files[0].Path.LocalPath);
    }

    private async void WelcomeClone_Click(object? sender, RoutedEventArgs e)
    {
        var win = new CloneRepositoryWindow();
        var result = await win.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(result))
            await LoadProjectAsync(result);
    }

    #region Undo / Redo

    private void UndoButton_Click(object? sender, RoutedEventArgs e)
    {
        _insaitEditor?.Undo();
    }

    private void RedoButton_Click(object? sender, RoutedEventArgs e)
    {
        _insaitEditor?.Redo();
    }

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var undoBtn = this.FindControl<Button>("UndoButton");
            var redoBtn = this.FindControl<Button>("RedoButton");
            if (undoBtn != null && _insaitEditor != null)
            {
                undoBtn.IsEnabled = _insaitEditor.CanUndo;
                undoBtn.Opacity = _insaitEditor.CanUndo ? 1.0 : 0.5;
            }
            if (redoBtn != null && _insaitEditor != null)
            {
                redoBtn.IsEnabled = _insaitEditor.CanRedo;
                redoBtn.Opacity = _insaitEditor.CanRedo ? 1.0 : 0.5;
            }
        });
    }

    #endregion

    #region Window Controls

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void NewWindowButton_Click(object? sender, RoutedEventArgs e)
    {
        // Open a fresh instance of the IDE in a new window
        var newWindow = new MainWindow();
        newWindow.Show();
    }

    private void RestartButton_Click(object? sender, RoutedEventArgs e)
    {
        // Restart: launch a new process then close this one
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exe))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true
            });
        }
        Close();
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (_isMaximized)
        {
            WindowState = WindowState.Normal;
            Position = _restorePosition;
            Width = _restoreSize.Width;
            Height = _restoreSize.Height;
            _isMaximized = false;
        }
        else
        {
            _restorePosition = Position;
            _restoreSize = new Size(Width, Height);
            WindowState = WindowState.Maximized;
            _isMaximized = true;
        }
    }

    private async void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        await TryCloseWindowAsync();
    }
    
    private async Task TryCloseWindowAsync()
    {
        // Check for unsaved changes
        var unsavedTabs = _viewModel.Tabs.Where(t => t.IsDirty).ToList();
        
        if (unsavedTabs.Count > 0)
        {
            var result = await ShowSaveAllConfirmationDialogAsync(unsavedTabs.Count);
            
            if (result == SaveConfirmationResult.Save)
            {
                // Save all before closing
                await SaveAllFilesAsync();
            }
            else if (result == SaveConfirmationResult.Cancel)
            {
                // Don't close the window
                return;
            }
            // If result is DontSave, continue closing without saving
        }
        
        Close();
    }
    
    private async Task<SaveConfirmationResult> ShowSaveAllConfirmationDialogAsync(int unsavedCount)
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 450,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly
        };

        var result = SaveConfirmationResult.Cancel;

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(20)
        };

        var messageText = new TextBlock
        {
            Text = $"You have {unsavedCount} unsaved file(s). Do you want to save changes before closing?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetRow(messageText, 0);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };
        Grid.SetRow(buttonPanel, 1);

        var saveButton = new Button { Content = "Save All", Width = 90 };
        saveButton.Click += (s, e) => { result = SaveConfirmationResult.Save; dialog.Close(); };

        var dontSaveButton = new Button { Content = "Don't Save", Width = 100 };
        dontSaveButton.Click += (s, e) => { result = SaveConfirmationResult.DontSave; dialog.Close(); };

        var cancelButton = new Button { Content = "Cancel", Width = 80 };
        cancelButton.Click += (s, e) => { result = SaveConfirmationResult.Cancel; dialog.Close(); };

        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(dontSaveButton);
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(messageText);
        grid.Children.Add(buttonPanel);

        dialog.Content = grid;

        await dialog.ShowDialog(this);

        return result;
    }

    #endregion

    #region Menu Handlers

    private void MainMenu_Click(object? sender, RoutedEventArgs e)
    {
        var menuWindow = new MenuWindow(this);
        menuWindow.ShowDialog(this);
    }

    /// <summary>
    /// Execute menu action from MenuWindow
    /// </summary>
    public async void ExecuteMenuAction(string action)
    {
        switch (action)
        {
            // Solution & Project actions
            case "NewSolution":
                await CreateNewSolutionAsync();
                break;
            case "NewProject":
                var currentSolutionForProject = FindSolutionFile();
                System.Diagnostics.Debug.WriteLine($"NewProject: currentSolutionForProject before dialog: '{currentSolutionForProject}'");
                var projectWindow = new NewProjectWindow(currentSolutionForProject);
                var projectResult = await projectWindow.ShowDialog<string?>(this);
                System.Diagnostics.Debug.WriteLine($"NewProject: projectResult: '{projectResult}'");
                if (!string.IsNullOrEmpty(projectResult))
                {
                    // projectResult is the solution file path (.sln or .slnx) returned by NewProjectWindow
                    string? solutionFile = null;
                    if (projectResult.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                        projectResult.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                    {
                        solutionFile = File.Exists(projectResult) ? projectResult : null;
                    }
                    else if (projectResult.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        // .csproj returned — look for solution ONLY in the project's own directory
                        var projDir = Path.GetDirectoryName(projectResult) ?? "";
                        solutionFile = FindSolutionFileFromPath(projDir);
                    }

                    System.Diagnostics.Debug.WriteLine($"NewProject: solutionFile resolved: '{solutionFile}'");

                    if (!string.IsNullOrEmpty(solutionFile) && File.Exists(solutionFile))
                    {
                        // Load the solution directory so the solution file appears in explorer
                        var solutionDir = Path.GetDirectoryName(solutionFile);
                        System.Diagnostics.Debug.WriteLine($"NewProject: solutionDir: '{solutionDir}'");
                        if (!string.IsNullOrEmpty(solutionDir) && Directory.Exists(solutionDir))
                        {
                            _projectPath = solutionDir;
                            _viewModel.CurrentProjectPath = solutionDir;
                            _viewModel.FileTreeItems.Clear();
                            await _viewModel.LoadProjectFolderAsync(solutionDir);
                            UpdateTitle();
                            _viewModel.StatusText = $"Created solution: {Path.GetFileName(solutionFile)}";
                        }
                    }
                    else
                    {
                        // No solution file — load the project directory only (never load parent)
                        var projPath = projectResult.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                            ? projectResult
                            : null;
                        var projDir = projPath != null
                            ? Path.GetDirectoryName(projPath) ?? ""
                            : "";
                        if (!string.IsNullOrEmpty(projDir) && Directory.Exists(projDir))
                        {
                            _projectPath = projDir;
                            _viewModel.CurrentProjectPath = projDir;
                            _viewModel.FileTreeItems.Clear();
                            await _viewModel.LoadProjectFolderAsync(projDir);
                            UpdateTitle();
                            _viewModel.StatusText = $"Created project: {Path.GetFileNameWithoutExtension(projectResult)}";
                        }
                    }
                }
                break;
            case "AddProjectToSolution":
                await AddNewProjectToSolutionAsync();
                break;
            case "NewEspProject":
                var currentSolutionForEsp = FindSolutionFile();
                var espWindow = new Esp.Windows.NewNanoProjectWindow(currentSolutionForEsp);
                var espResult = await espWindow.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(espResult))
                {
                    // Reload the solution/project tree to show the new nanoFramework project
                    var solutionFileForEsp = FindSolutionFile();
                    if (!string.IsNullOrEmpty(solutionFileForEsp))
                    {
                        var solutionDirForEsp = Path.GetDirectoryName(solutionFileForEsp);
                        if (!string.IsNullOrEmpty(solutionDirForEsp))
                        {
                            _projectPath = solutionDirForEsp;
                            _viewModel.CurrentProjectPath = solutionDirForEsp;
                            _viewModel.FileTreeItems.Clear();
                            await _viewModel.LoadProjectFolderAsync(solutionDirForEsp);
                            UpdateTitle();
                        }
                    }
                    else
                    {
                        // No solution — load the project directory directly
                        var espProjectDir = Path.GetDirectoryName(espResult);
                        if (!string.IsNullOrEmpty(espProjectDir))
                        {
                            _projectPath = espProjectDir;
                            LoadProject(espProjectDir);
                            UpdateTitle();
                            RefreshFileTree();
                        }
                    }
                    _viewModel.StatusText = $"Created nanoFramework project: {Path.GetFileNameWithoutExtension(espResult)}";
                }
                break;
            case "OpenSolution":
                await OpenSolutionAsync();
                break;
            
            // File actions
            case "NewFile":
                CreateNewFile();
                break;
            case "OpenFile":
                await OpenNewFileAsync();
                break;
            case "OpenFolder":
                await OpenFolderAsync();
                break;
            case "Save":
                await SaveCurrentFileAsync();
                break;
            case "SaveAs":
                await SaveCurrentFileAsAsync();
                break;
            case "SaveAll":
                await SaveAllFilesAsync();
                break;
            case "Exit":
                Close();
                break;

            // Edit actions
            case "Undo":
                _insaitEditor?.Undo();
                break;
            case "Redo":
                _insaitEditor?.Redo();
                break;
            case "Find":
                _insaitEditor?.Find();
                break;
            case "Replace":
                _insaitEditor?.Replace();
                break;
            case "FindInFiles":
                _searchTabIsFiles = false;
                EnsureSidePanelVisible();
                SwitchSidePanel("explorer");
                ToggleExplorerSearch(open: true);
                UpdateSearchTabUI();
                this.FindControl<TextBox>("ContentSearchInputBox")?.Focus();
                break;
            case "FormatDocument":
                _insaitEditor?.FormatDocument();
                break;
            case "ToggleComment":
                // TODO: Implement toggle comment
                break;
            case "PreviewAxaml":
                await OpenAxamlPreviewAsync();
                break;

            // View actions
            case "ToggleAI":
            case "ToggleRightPanel":
                ToggleAIPanel();
                break;
            case "ToggleLeftPanel":
                ToggleLeftPanel();
                break;
            case "ToggleBottomPanel":
                ToggleBottomPanel();
                break;
            case "ToggleZenMode":
                ToggleZenMode();
                break;
            case "ShowExplorer":
                ToggleSidePanel("explorer");
                break;
            case "ShowSearch":
                _searchTabIsFiles = true;
                EnsureSidePanelVisible();
                SwitchSidePanel("explorer");
                ToggleExplorerSearch(open: true);
                UpdateSearchTabUI();
                this.FindControl<TextBox>("SearchInputBox")?.Focus();
                break;
            case "ShowSourceControl":
                await OpenGitWindowAsync();
                break;
            case "ShowTerminal":
                SwitchToolWindowPanel("terminal");
                EnsureBottomPanelVisible();
                break;
            case "NewTerminal":
                _terminalControl?.OpenExternalTerminal(title: "Insait Edit — Terminal");
                _viewModel.StatusText = LocalizationService.Get("Menu.NewTerminal");
                break;
            case "ShowProblems":
                SwitchToolWindowPanel("problems");
                break;
            case "ShowBuildOutput":
                SwitchToolWindowPanel("build");
                break;
            case "ShowRunOutput":
                SwitchToolWindowPanel("run");
                break;
            case "ShowDebugConsole":
                SwitchToolWindowPanel("build");
                break;
            case "Minimize":
                WindowState = WindowState.Minimized;
                break;
            case "ToggleMaximize":
                ToggleMaximize();
                break;

            // Build actions
            case "Build":
                await BuildProjectAsync();
                break;
            case "Rebuild":
                await RebuildProjectAsync();
                break;
            case "Analyze":
                await AnalyzeProjectAsync();
                break;
            case "ToggleBreakpoint":
                if (_insaitEditor != null)
                {
                    var (bpLine, _) = _insaitEditor.CursorPosition;
                    var bpFile = _viewModel.ActiveTab?.FilePath ?? string.Empty;
                    if (!string.IsNullOrEmpty(bpFile))
                        BreakpointService.Toggle(bpFile, bpLine);
                }
                break;
            case "DeleteAllBreakpoints":
                BreakpointService.ClearAll();
                break;
            case "StartDebugging":
            case "StopDebugging":
            case "StepOver":
            case "StepInto":
            case "StepOut":
            case "StartWithoutDebugging":
                _viewModel.StatusText = $"Debug: {action} (future DAP integration)";
                break;
            case "Clean":
                await CleanProjectAsync();
                break;
            case "Run":
                await RunProjectAsync();
                break;
            case "Stop":
                CancelBuild();
                StopRunningProcess();
                break;
            case "RestorePackages":
                await RestorePackagesAsync();
                break;
            case "RunConfigurations":
                await ShowRunConfigurationsAsync();
                break;
            case "Publish":
                await ShowPublishWindowAsync();
                break;

            // Tools actions
            case "OpenTerminal":
                SwitchToolWindowPanel("terminal");
                break;
            case "RefreshFileTree":
                RefreshFileTree();
                break;
            case "OpenSettings":
                // TODO: Implement settings
                break;
            case "OpenTheme":
                // TODO: Implement theme selector
                break;
            case "OpenKeyboardShortcuts":
                // TODO: Implement keyboard shortcuts
                break;
            case "ManageNuGetPackages":
                NuGet_Click(null, null!);
                break;

            // Help actions
            case "OpenDocumentation":
                // TODO: Implement documentation
                break;
            case "GettingStarted":
                // TODO: Implement getting started
                break;
            case "ShowKeyboardShortcuts":
                // TODO: Implement keyboard shortcuts
                break;
            case "ReportIssue":
                // TODO: Implement report issue
                break;
            case "FeatureRequest":
                // TODO: Implement feature request
                break;
            case "ShowAbout":
                ShowAboutDialog();
                break;
            case "CheckForUpdates":
                // TODO: Implement check for updates
                break;
        }
    }

    private async Task OpenFolderAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            _projectPath = folder.Path.LocalPath;
            LoadProject(_projectPath);
            UpdateTitle();
        }
    }

    private void RefreshFileTree()
    {
        if (!string.IsNullOrEmpty(_projectPath))
        {
            // Use the ViewModel's RefreshFileTree method which preserves expanded state
            if (!string.IsNullOrEmpty(_viewModel.CurrentProjectPath))
            {
                _viewModel.RefreshFileTree();
            }
            else
            {
                // First time loading, use LoadProjectFolderAsync (fire and forget)
                _ = _viewModel.LoadProjectFolderAsync(_projectPath);
            }
        }

        // Reload all open editor tabs whose files changed on disk
        ReloadOpenTabsFromDisk();
    }

    /// <summary>
    /// Reloads all open editor tabs from disk.
    /// Tabs that are not dirty are silently reloaded if their on-disk content differs.
    /// The active tab's content is also pushed to the Insait Code Editor when updated.
    /// </summary>
    private void ReloadOpenTabsFromDisk()
    {
        bool activeTabReloaded = false;

        foreach (var tab in _viewModel.Tabs)
        {
            if (string.IsNullOrEmpty(tab.FilePath) || !File.Exists(tab.FilePath))
                continue;

            // Skip dirty tabs — the user has unsaved changes we must not overwrite
            if (tab.IsDirty)
                continue;

            try
            {
                var newContent = File.ReadAllText(tab.FilePath);

                // Only update if content actually changed (avoid unnecessary editor flicker)
                if (tab.Content == newContent)
                    continue;

                tab.Content = newContent;
                tab.IsDirty = false;

                if (tab == _viewModel.ActiveTab)
                    activeTabReloaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReloadOpenTabsFromDisk: could not read {tab.FilePath}: {ex.Message}");
            }
        }

        // Push the reloaded content into the editor if the active tab was updated
        if (activeTabReloaded && _viewModel.ActiveTab != null && _insaitEditor != null)
        {
            _insaitEditor.SetContent(_viewModel.ActiveTab.Content, _viewModel.ActiveTab.Language);
            _viewModel.StatusText = $"Reloaded from disk: {_viewModel.ActiveTab.FileName}";
        }
    }

    private void ShowAboutDialog()
    {
        var dialog = new Window
        {
            Title = "About Insait Edit",
            Width = 400,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            Background = new SolidColorBrush(Color.Parse("#FF1E1E2E"))
        };

        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("36,*")
        };

        // Custom title bar
        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FF3C3C3C")),
            CornerRadius = new CornerRadius(8, 8, 0, 0)
        };
        Grid.SetRow(titleBar, 0);

        var titleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var titleText = new TextBlock
        {
            Text = "ℹ️ About",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(12, 0)
        };
        Grid.SetColumn(titleText, 0);

        var closeTitleBtn = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
            BorderThickness = new Thickness(0),
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        closeTitleBtn.Click += (s, e) => dialog.Close();
        Grid.SetColumn(closeTitleBtn, 1);

        titleGrid.Children.Add(titleText);
        titleGrid.Children.Add(closeTitleBtn);
        titleBar.Child = titleGrid;

        // Make title bar draggable
        titleBar.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(dialog).Properties.IsLeftButtonPressed)
            {
                dialog.BeginMoveDrag(e);
            }
        };

        // Content
        var contentBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FF1E1E2E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(0, 0, 8, 8)
        };
        Grid.SetRow(contentBorder, 1);

        var stack = new StackPanel
        {
            Margin = new Thickness(24, 16),
            Spacing = 12
        };

        stack.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Border
                {
                    Width = 48,
                    Height = 48,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.Parse("#FFFAB387")),
                    Child = new TextBlock
                    {
                        Text = "⚡",
                        FontSize = 24,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.Parse("#FF1E1E2E"))
                    }
                },
                new StackPanel
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Insait Edit",
                            FontSize = 20,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.Parse("#FFFAB387"))
                        },
                        new TextBlock
                        {
                            Text = "C# IDE",
                            FontSize = 13,
                            Foreground = new SolidColorBrush(Color.Parse("#FF9399B2"))
                        }
                    }
                }
            }
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Version 1.0.0",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4"))
        });

        stack.Children.Add(new TextBlock
        {
            Text = "A modern, lightweight C# IDE built with Avalonia UI and Insait Code Editor.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        stack.Children.Add(new TextBlock
        {
            Text = "© 2026 Insait Edit",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
            Margin = new Thickness(0, 8, 0, 0)
        });

        contentBorder.Child = stack;

        rootGrid.Children.Add(titleBar);
        rootGrid.Children.Add(contentBorder);

        var outerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = rootGrid
        };

        dialog.Content = outerBorder;
        dialog.ShowDialog(this);
    }

    #endregion

    #region Sidebar Handlers

    private AccountPanelControl? _accountPanelControl;
    private string _currentSidePanel = "explorer";
    private GitWindow? _gitWindow;

    private void Explorer_Click(object? sender, RoutedEventArgs e)
    {
        ToggleSidePanel("explorer");
    }

    /// <summary>
    /// Toggles the side panel: if the requested panel is already showing, hide the whole side panel;
    /// otherwise show it (if hidden) and switch to the requested panel.
    /// </summary>
    private void ToggleSidePanel(string panelName)
    {
        bool isSamePanel = _currentSidePanel == panelName && _leftPanelVisible;

        if (isSamePanel)
        {
            // Same panel clicked again → collapse
            SnapshotSizes();
            ApplyLeftPanel(false);
        }
        else
        {
            // Different panel, or panel was hidden → show and switch
            ApplyLeftPanel(true);
            SwitchSidePanel(panelName);
            if (panelName == "explorer")
                ToggleExplorerSearch(open: false);
        }
    }

    /// <summary>Ensures the side panel container is visible (restores if collapsed).</summary>
    private void EnsureSidePanelVisible()
    {
        if (!_leftPanelVisible) ApplyLeftPanel(true);
    }

    private void Search_Click(object? sender, RoutedEventArgs e)
    {
        // Switch to explorer and toggle the search bar
        EnsureSidePanelVisible();
        SwitchSidePanel("explorer");
        ToggleExplorerSearch(open: true);
    }

    private void ExplorerSearchToggle_Click(object? sender, RoutedEventArgs e)
    {
        var searchBar = this.FindControl<Border>("ExplorerSearchBar");
        if (searchBar == null) return;
        ToggleExplorerSearch(open: !searchBar.IsVisible);
    }

    private void ToggleExplorerSearch(bool open)
    {
        var searchBar    = this.FindControl<Border>("ExplorerSearchBar");
        var resultsScroll= this.FindControl<ScrollViewer>("SearchResultsScrollViewer");
        if (searchBar == null) return;

        searchBar.IsVisible = open;
        if (resultsScroll != null) resultsScroll.IsVisible = open;

        if (open)
        {
            UpdateSearchScopeCombos();
            UpdateSearchTabUI();
            if (_searchTabIsFiles)
                this.FindControl<TextBox>("SearchInputBox")?.Focus();
            else
                this.FindControl<TextBox>("ContentSearchInputBox")?.Focus();
        }
        else
        {
            ClearSearchResults();
        }
    }

    private void SwitchSidePanel(string panelName)
    {
        _currentSidePanel = panelName;
        
        // Get all panels
        var explorerPanel = this.FindControl<Grid>("ExplorerPanel");
        var nugetPanel = this.FindControl<Border>("NuGetSidePanel");
        var accountPanel = this.FindControl<Border>("AccountSidePanel");
        var settingsPanel = this.FindControl<Border>("SettingsSidePanel");
        
        // Get all sidebar buttons
        var explorerButton = this.FindControl<Button>("ExplorerButton");
        var gitButton = this.FindControl<Button>("GitButton");
        var nugetButton = this.FindControl<Button>("NuGetButton");
        var accountButton = this.FindControl<Button>("AccountButton");
        var settingsButton = this.FindControl<Button>("SettingsButton");
        
        // Hide all panels
        if (explorerPanel != null) explorerPanel.IsVisible = false;
        if (nugetPanel != null) nugetPanel.IsVisible = false;
        if (accountPanel != null) accountPanel.IsVisible = false;
        if (settingsPanel != null) settingsPanel.IsVisible = false;
        
        // Remove active class from all buttons
        explorerButton?.Classes.Remove("active");
        gitButton?.Classes.Remove("active");
        nugetButton?.Classes.Remove("active");
        accountButton?.Classes.Remove("active");
        settingsButton?.Classes.Remove("active");
        
        // Show selected panel and activate button
        switch (panelName)
        {
            case "explorer":
                if (explorerPanel != null) explorerPanel.IsVisible = true;
                explorerButton?.Classes.Add("active");
                break;
            case "nuget":
                if (nugetPanel != null) nugetPanel.IsVisible = true;
                nugetButton?.Classes.Add("active");
                break;
            case "account":
                if (accountPanel != null) accountPanel.IsVisible = true;
                accountButton?.Classes.Add("active");
                break;
            case "settings":
                if (settingsPanel != null) settingsPanel.IsVisible = true;
                settingsButton?.Classes.Add("active");
                break;
        }
    }

    private async void Git_Click(object? sender, RoutedEventArgs e)
    {
        await OpenGitWindowAsync();
    }

    private async Task OpenGitWindowAsync()
    {
        // Collect all project paths from the solution
        var allProjects = CollectAllProjectPaths();

        if (_gitWindow == null || !_gitWindow.IsVisible)
        {
            _gitWindow = new GitWindow();
            _gitWindow.FileOpenRequested += (_, filePath) => OpenFileInEditor(filePath);
            await _gitWindow.InitializeAsync(_projectPath, allProjects);
            _gitWindow.Show();
        }
        else
        {
            _gitWindow.Activate();
            await _gitWindow.RefreshAsync();
        }
    }

    private List<string> CollectAllProjectPaths()
    {
        var paths = new List<string>();
        foreach (var item in _viewModel.FileTreeItems)
            CollectProjects(item, paths);
        return paths;
    }

    private static void CollectProjects(Models.FileTreeItem item, List<string> paths)
    {
        if (item.ItemType is Models.FileTreeItemType.Project or Models.FileTreeItemType.EspProject)
            paths.Add(item.FullPath);
        foreach (var child in item.Children)
            CollectProjects(child, paths);
    }

    private void Debug_Click(object? sender, RoutedEventArgs e)
    {
        // Debug panel removed
    }

    private NuGetPanelControl? _nugetPanelControl;
    
    private async void NuGet_Click(object? sender, RoutedEventArgs e)
    {
        EnsureSidePanelVisible();
        SwitchSidePanel("nuget");
        
        // Initialize NuGet panel if not done yet
        if (_nugetPanelControl == null)
        {
            InitializeNuGetPanel();
        }
        
        // Set project path if available
        if (_nugetPanelControl != null && !string.IsNullOrEmpty(_projectPath))
        {
            await _nugetPanelControl.SetProjectPathAsync(_projectPath);
        }
    }

    private void InitializeNuGetPanel()
    {
        var nugetPanelContainer = this.FindControl<Border>("NuGetSidePanel");
        if (nugetPanelContainer == null) return;
        
        _nugetPanelControl = new NuGetPanelControl();
        
        // Subscribe to events
        _nugetPanelControl.StatusChanged += (s, status) =>
        {
            _viewModel.StatusText = status;
        };
        
        _nugetPanelControl.ErrorOccurred += (s, error) =>
        {
            _viewModel.StatusText = $"Error: {error}";
        };
        
        // Add to container
        nugetPanelContainer.Child = _nugetPanelControl;
        
        // Set project path
        if (!string.IsNullOrEmpty(_projectPath))
        {
            _ = _nugetPanelControl.SetProjectPathAsync(_projectPath);
        }
    }

    private async void GitStatus_Click(object? sender, RoutedEventArgs e)
    {
        await ShowGitStatusDialog();
    }

    private async Task ShowGitStatusDialog()
    {
        var gitService = new GitService();
        
        // Перевірка чи Git встановлений
        var isGitInstalled = await gitService.IsGitInstalledAsync();
        
        if (!isGitInstalled)
        {
            await ShowGitNotInstalledDialog();
            return;
        }

        // Перевірка стану репозиторію
        if (string.IsNullOrEmpty(_projectPath))
        {
            await ShowNoProjectOpenedDialog();
            return;
        }

        // Отримуємо директорію проекту (якщо _projectPath - це файл, беремо його директорію)
        var projectDirectory = GetProjectDirectory(_projectPath);
        
        if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            await ShowNoProjectOpenedDialog();
            return;
        }

        var gitStatusResult = await CheckGitRepositoryStatus(projectDirectory);
        await ShowGitStatusResultDialog(gitStatusResult);
    }

    /// <summary>
    /// Отримує директорію проекту з шляху (може бути файл або папка)
    /// </summary>
    private string? GetProjectDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Якщо це файл (має розширення типу .csproj, .sln, тощо), беремо його директорію
        if (File.Exists(path))
        {
            return Path.GetDirectoryName(path);
        }
        
        // Якщо це директорія, повертаємо як є
        if (Directory.Exists(path))
        {
            return path;
        }

        // Спробуємо отримати директорію
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            return directory;
        }

        return null;
    }

    private async Task<GitRepositoryCheckResult> CheckGitRepositoryStatus(string path)
    {
        var result = new GitRepositoryCheckResult { Path = path };
        var gitPath = Path.Combine(path, ".git");

        // Перевірка існування .git директорії
        if (!Directory.Exists(gitPath))
        {
            result.Status = GitRepoStatus.NotInitialized;
            result.Message = "Git репозиторій не ініціалізований";
            return result;
        }

        // Перевірка чи це справжня директорія Git
        var gitService = new GitService(path);
        var statusResult = await gitService.GetStatusAsync();

        // Спробувати виконати команду git status для перевірки цілісності
        try
        {
            var testResult = await RunGitCommandAsync("status", path);
            if (testResult.ExitCode != 0)
            {
                if (testResult.Error.Contains("fatal") || testResult.Error.Contains("corrupt"))
                {
                    result.Status = GitRepoStatus.Corrupted;
                    result.Message = $"Git репозиторій пошкоджений: {testResult.Error}";
                    return result;
                }
            }
            
            result.Status = GitRepoStatus.Healthy;
            result.Message = $"Git репозиторій OK. Гілка: {statusResult.CurrentBranch}";
            result.BranchName = statusResult.CurrentBranch;
            result.ChangesCount = statusResult.TotalChanges;
        }
        catch (Exception ex)
        {
            result.Status = GitRepoStatus.Corrupted;
            result.Message = $"Помилка перевірки Git: {ex.Message}";
        }

        return result;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(string arguments, string workingDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private async Task ShowGitNotInstalledDialog()
    {
        var dialog = new Window
        {
            Title = "Git Status",
            Width = 450,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        var mainGrid = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto") };
        
        var content = new StackPanel 
        { 
            Spacing = 16, 
            Margin = new Thickness(24),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        
        content.Children.Add(new TextBlock
        {
            Text = "⚠️",
            FontSize = 48,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });
        
        content.Children.Add(new TextBlock
        {
            Text = "Git не встановлений",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = Avalonia.Media.Brushes.White
        });
        
        content.Children.Add(new TextBlock
        {
            Text = "Для роботи з Git необхідно встановити Git.\nЗавантажте з https://git-scm.com/",
            FontSize = 13,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9399B2"))
        });

        Grid.SetRow(content, 0);
        mainGrid.Children.Add(content);

        var buttonPanel = new StackPanel 
        { 
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 20)
        };
        
        var closeButton = new Button 
        { 
            Content = "Закрити", 
            Width = 100,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            Foreground = Avalonia.Media.Brushes.White
        };
        closeButton.Click += (s, e) => dialog.Close();
        buttonPanel.Children.Add(closeButton);
        
        Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(buttonPanel);

        var border = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = mainGrid
        };

        dialog.Content = border;
        await dialog.ShowDialog(this);
    }

    private async Task ShowNoProjectOpenedDialog()
    {
        var dialog = new Window
        {
            Title = "Git Status",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent
        };

        var content = new StackPanel 
        { 
            Spacing = 16, 
            Margin = new Thickness(24),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        
        content.Children.Add(new TextBlock
        {
            Text = "📂",
            FontSize = 40,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });
        
        content.Children.Add(new TextBlock
        {
            Text = "Немає відкритого проекту",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = Avalonia.Media.Brushes.White
        });

        var closeButton = new Button 
        { 
            Content = "Закрити", 
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Width = 100,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            Foreground = Avalonia.Media.Brushes.White
        };
        closeButton.Click += (s, e) => dialog.Close();
        content.Children.Add(closeButton);

        dialog.Content = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = content
        };

        await dialog.ShowDialog(this);
    }

    private async Task ShowGitStatusResultDialog(GitRepositoryCheckResult result)
    {
        var dialog = new Window
        {
            Title = "Git Status",
            Width = 500,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent
        };

        var mainGrid = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto") };

        var content = new StackPanel 
        { 
            Spacing = 16, 
            Margin = new Thickness(24),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        // Іконка та статус
        var statusIcon = result.Status switch
        {
            GitRepoStatus.Healthy => "✅",
            GitRepoStatus.NotInitialized => "📁",
            GitRepoStatus.Corrupted => "⚠️",
            _ => "❓"
        };

        var statusColor = result.Status switch
        {
            GitRepoStatus.Healthy => "#A6E3A1",
            GitRepoStatus.NotInitialized => "#89B4FA",
            GitRepoStatus.Corrupted => "#F38BA8",
            _ => "#9399B2"
        };

        content.Children.Add(new TextBlock
        {
            Text = statusIcon,
            FontSize = 48,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        var statusTitle = result.Status switch
        {
            GitRepoStatus.Healthy => "Git репозиторій OK",
            GitRepoStatus.NotInitialized => "Git не ініціалізований",
            GitRepoStatus.Corrupted => "Git репозиторій пошкоджений",
            _ => "Невідомий статус"
        };

        content.Children.Add(new TextBlock
        {
            Text = statusTitle,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(statusColor))
        });

        content.Children.Add(new TextBlock
        {
            Text = result.Message,
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9399B2"))
        });

        if (result.Status == GitRepoStatus.Healthy && result.ChangesCount > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Змін: {result.ChangesCount}",
                FontSize = 13,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FAB387"))
            });
        }

        Grid.SetRow(content, 0);
        mainGrid.Children.Add(content);

        // Кнопки
        var buttonPanel = new StackPanel 
        { 
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 20)
        };

        if (result.Status == GitRepoStatus.NotInitialized)
        {
            var initButton = new Button 
            { 
                Content = "🔧 Ініціалізувати Git", 
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#89B4FA")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(16, 10)
            };
            initButton.Click += async (s, e) =>
            {
                dialog.Close();
                await InitializeGitRepository(result.Path);
            };
            buttonPanel.Children.Add(initButton);
        }
        else if (result.Status == GitRepoStatus.Corrupted)
        {
            var repairButton = new Button 
            { 
                Content = "🔧 Спробувати відновити", 
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FAB387")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(16, 10)
            };
            repairButton.Click += async (s, e) =>
            {
                dialog.Close();
                await RepairGitRepository(result.Path);
            };
            buttonPanel.Children.Add(repairButton);

            var reinitButton = new Button 
            { 
                Content = "🗑️ Видалити та створити новий", 
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F38BA8")),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(16, 10)
            };
            reinitButton.Click += async (s, e) =>
            {
                dialog.Close();
                await ReinitializeGitRepository(result.Path);
            };
            buttonPanel.Children.Add(reinitButton);
        }

        var closeButton = new Button 
        { 
            Content = "Закрити", 
            Width = 100,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            Foreground = Avalonia.Media.Brushes.White,
            Padding = new Thickness(16, 10)
        };
        closeButton.Click += (s, e) => dialog.Close();
        buttonPanel.Children.Add(closeButton);

        Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(buttonPanel);

        dialog.Content = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = mainGrid
        };

        // Оновлюємо індикатор
        UpdateGitStatusIndicator(result.Status);

        await dialog.ShowDialog(this);
    }

    private async Task InitializeGitRepository(string path)
    {
        _viewModel.StatusText = "Ініціалізація Git репозиторію...";
        
        var result = await RunGitCommandAsync("init", path);
        
        if (result.ExitCode == 0)
        {
            _viewModel.StatusText = "✅ Git репозиторій успішно ініціалізовано!";
            UpdateGitStatusIndicator(GitRepoStatus.Healthy);
            
            // Оновлюємо Git вікно якщо воно відкрите
            if (_gitWindow != null && _gitWindow.IsVisible)
            {
                await _gitWindow.InitializeAsync(path);
            }
        }
        else
        {
            _viewModel.StatusText = $"❌ Помилка ініціалізації: {result.Error}";
        }
    }

    private async Task RepairGitRepository(string path)
    {
        _viewModel.StatusText = "Спроба відновлення Git репозиторію...";

        // Спробувати git fsck
        var fsckResult = await RunGitCommandAsync("fsck --full", path);
        
        if (fsckResult.ExitCode == 0 || !fsckResult.Error.Contains("fatal"))
        {
            // Спробувати git gc
            var gcResult = await RunGitCommandAsync("gc --prune=now", path);
            
            _viewModel.StatusText = "✅ Git репозиторій відновлено!";
            UpdateGitStatusIndicator(GitRepoStatus.Healthy);
        }
        else
        {
            _viewModel.StatusText = $"⚠️ Не вдалося відновити: {fsckResult.Error}";
            
            // Показати діалог з пропозицією переініціалізувати
            await ShowRepairFailedDialog(path);
        }
    }

    private async Task ShowRepairFailedDialog(string path)
    {
        var dialog = new Window
        {
            Title = "Відновлення не вдалося",
            Width = 450,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent
        };

        var mainGrid = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto") };

        var content = new StackPanel 
        { 
            Spacing = 12, 
            Margin = new Thickness(24),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        content.Children.Add(new TextBlock
        {
            Text = "⚠️",
            FontSize = 40,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = "Не вдалося відновити репозиторій",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F38BA8"))
        });

        content.Children.Add(new TextBlock
        {
            Text = "Бажаєте видалити пошкоджений Git та створити новий?",
            FontSize = 13,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9399B2"))
        });

        Grid.SetRow(content, 0);
        mainGrid.Children.Add(content);

        var buttonPanel = new StackPanel 
        { 
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 20)
        };
        
        var reinitButton = new Button 
        { 
            Content = "Так, переініціалізувати", 
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FAB387")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(16, 10)
        };
        reinitButton.Click += async (s, e) =>
        {
            dialog.Close();
            await ReinitializeGitRepository(path);
        };
        buttonPanel.Children.Add(reinitButton);

        var cancelButton = new Button 
        { 
            Content = "Скасувати", 
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            Foreground = Avalonia.Media.Brushes.White,
            Padding = new Thickness(16, 10)
        };
        cancelButton.Click += (s, e) => dialog.Close();
        buttonPanel.Children.Add(cancelButton);

        Grid.SetRow(buttonPanel, 1);
        mainGrid.Children.Add(buttonPanel);

        dialog.Content = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E2E")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = mainGrid
        };

        await dialog.ShowDialog(this);
    }

    private async Task ReinitializeGitRepository(string path)
    {
        _viewModel.StatusText = "Видалення пошкодженого Git...";

        var gitPath = Path.Combine(path, ".git");
        
        try
        {
            if (Directory.Exists(gitPath))
            {
                // Видаляємо .git директорію
                Directory.Delete(gitPath, true);
            }

            _viewModel.StatusText = "Ініціалізація нового Git репозиторію...";
            
            // Створюємо новий репозиторій
            await InitializeGitRepository(path);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"❌ Помилка: {ex.Message}";
        }
    }

    private void UpdateGitStatusIndicator(GitRepoStatus status)
    {
        var indicator = this.FindControl<Border>("GitStatusIndicator");
        if (indicator == null) return;

        var color = status switch
        {
            GitRepoStatus.Healthy => "#A6E3A1",      // Зелений
            GitRepoStatus.NotInitialized => "#89B4FA", // Синій
            GitRepoStatus.Corrupted => "#F38BA8",    // Червоний
            _ => "#6C6C6C"                           // Сірий
        };

        indicator.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color));
    }

    private async void Account_Click(object? sender, RoutedEventArgs e)
    {
        EnsureSidePanelVisible();
        SwitchSidePanel("account");
        
        // Initialize Account panel if not done yet
        if (_accountPanelControl == null)
        {
            InitializeAccountPanel();
        }
        
        // Refresh account data when panel is shown
        if (_accountPanelControl != null)
        {
            await _accountPanelControl.InitializeAsync();
        }
    }

    private SettingsPanelControl? _settingsPanelControl;

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // Initialize Settings panel if not done yet (BEFORE switching)
        if (_settingsPanelControl == null)
        {
            InitializeSettingsPanel();
        }

        EnsureSidePanelVisible();
        SwitchSidePanel("settings");
        
        // Reload settings each time panel is shown
        _settingsPanelControl?.LoadSettings();
    }

    private void InitializeSettingsPanel()
    {
        var settingsPanelContainer = this.FindControl<Border>("SettingsSidePanel");
        if (settingsPanelContainer == null) return;
        
        _settingsPanelControl = new SettingsPanelControl();
        
        // Subscribe to events
        _settingsPanelControl.StatusChanged += (s, status) =>
        {
            _viewModel.StatusText = status;
        };
        
        // Add to container
        settingsPanelContainer.Child = _settingsPanelControl;
    }

    // ─────────────────────────────────────────────────────────────
    //  Search Panel — Find Files & Find in Files
    // ─────────────────────────────────────────────────────────────

    private bool _searchTabIsFiles = true; // true = Find Files, false = Find in Files

    private void InitializeSearchPanel()
    {
        // Tab toggle buttons
        var tabFilesBtn   = this.FindControl<Button>("SearchTabFilesBtn");
        var tabContentBtn = this.FindControl<Button>("SearchTabContentBtn");

        if (tabFilesBtn != null)
            tabFilesBtn.Click += (_, _) => { _searchTabIsFiles = true;  UpdateSearchTabUI(); };
        if (tabContentBtn != null)
            tabContentBtn.Click += (_, _) => { _searchTabIsFiles = false; UpdateSearchTabUI(); };

        // File-name search: press Enter or click button
        var searchBox = this.FindControl<TextBox>("SearchInputBox");
        if (searchBox != null)
            searchBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) ExecuteFileNameSearch(); };

        var searchFileBtn = this.FindControl<Button>("SearchFileNamesButton");
        if (searchFileBtn != null)
            searchFileBtn.Click += (_, _) => ExecuteFileNameSearch();

        // Content search: press Enter or click button
        var contentBox = this.FindControl<TextBox>("ContentSearchInputBox");
        if (contentBox != null)
            contentBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) _ = ExecuteContentSearchAsync(); };

        var searchContentBtn = this.FindControl<Button>("SearchContentButton");
        if (searchContentBtn != null)
            searchContentBtn.Click += (_, _) => _ = ExecuteContentSearchAsync();

        // Populate scope combos once
        UpdateSearchScopeCombos();

        // Show correct tab initially
        UpdateSearchTabUI();
    }

    private void UpdateSearchTabUI()
    {
        var filesBorder   = this.FindControl<StackPanel>("FindFilesBorder");
        var contentBorder = this.FindControl<StackPanel>("FindContentBorder");
        if (filesBorder   != null) filesBorder.IsVisible   = _searchTabIsFiles;
        if (contentBorder != null) contentBorder.IsVisible  = !_searchTabIsFiles;

        ClearSearchResults();
        SetSearchStatus("");
    }

    /// <summary>
    /// Populate the scope ComboBoxes with "Whole Solution" + one entry per project.
    /// Called when the search panel is opened for the first time and also on each search
    /// so the list is always fresh.
    /// </summary>
    private void UpdateSearchScopeCombos()
    {
        var combos = new[] { "SearchScopeCombo", "ContentSearchScopeCombo" };

        // Collect project directories from the file tree
        var projects = new List<(string Label, string Directory)>();
        foreach (var item in _viewModel.FileTreeItems)
            CollectProjectsFromTree(item, projects);

        foreach (var comboName in combos)
        {
            var combo = this.FindControl<ComboBox>(comboName);
            if (combo == null) continue;

            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "Whole Solution", Tag = "solution" });

            foreach (var (label, dir) in projects)
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = dir });

            combo.SelectedIndex = 0;
        }
    }

    private static void CollectProjectsFromTree(FileTreeItem item,
        List<(string Label, string Directory)> projects)
    {
        if (item.ItemType == FileTreeItemType.Project ||
            item.ItemType == FileTreeItemType.EspProject)
        {
            var dir = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath) ?? "";
            if (!string.IsNullOrEmpty(dir))
                projects.Add((item.Name, dir));
        }
        foreach (var child in item.Children)
            CollectProjectsFromTree(child, projects);
    }

    /// <summary>
    /// Returns the root directory to search in, based on the selected scope combo.
    /// </summary>
    private string? GetSearchRootFromCombo(string comboName)
    {
        var combo = this.FindControl<ComboBox>(comboName);
        if (combo?.SelectedItem is ComboBoxItem ci)
        {
            var tag = ci.Tag?.ToString() ?? "";
            if (tag == "solution")
            {
                // Whole solution: use the solution's root folder
                var slnFile = FindSolutionFile();
                if (!string.IsNullOrEmpty(slnFile))
                    return Path.GetDirectoryName(slnFile);
                // Fallback to project path
                return _projectPath ?? _viewModel.CurrentProjectPath;
            }
            // tag is a directory path
            if (Directory.Exists(tag)) return tag;
        }
        return _projectPath ?? _viewModel.CurrentProjectPath;
    }

    // ── Find Files by Name ────────────────────────────────────────

    private void ExecuteFileNameSearch()
    {
        UpdateSearchScopeCombos();

        var pattern = this.FindControl<TextBox>("SearchInputBox")?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(pattern))
        {
            SetSearchStatus("Enter a file name or pattern.");
            return;
        }

        var root = GetSearchRootFromCombo("SearchScopeCombo");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            SetSearchStatus("No project / solution loaded.");
            return;
        }

        SetSearchStatus("Searching…");
        ClearSearchResults();

        // Build glob-style patterns: if no wildcard, wrap with wildcards
        var searchPattern = pattern.Contains('*') || pattern.Contains('?')
            ? pattern
            : $"*{pattern}*";

        try
        {
            var files = Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            if (files.Count == 0)
            {
                SetSearchStatus($"No files found matching '{pattern}'.");
                return;
            }

            SetSearchStatus($"{files.Count} file(s) found.");

            foreach (var file in files)
                AddFileResult(file, null, -1);
        }
        catch (Exception ex)
        {
            SetSearchStatus($"Error: {ex.Message}");
        }
    }

    // ── Find in Files (content search) ───────────────────────────

    private async Task ExecuteContentSearchAsync()
    {
        UpdateSearchScopeCombos();

        var query = this.FindControl<TextBox>("ContentSearchInputBox")?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            SetSearchStatus("Enter search text.");
            return;
        }

        var root = GetSearchRootFromCombo("ContentSearchScopeCombo");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            SetSearchStatus("No project / solution loaded.");
            return;
        }

        bool caseSensitive = this.FindControl<CheckBox>("SearchCaseSensitiveCheck")?.IsChecked == true;
        bool useRegex      = this.FindControl<CheckBox>("SearchRegexCheck")?.IsChecked == true;
        bool wholeWord     = this.FindControl<CheckBox>("SearchWholeWordCheck")?.IsChecked == true;

        SetSearchStatus("Searching…");
        ClearSearchResults();

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        System.Text.RegularExpressions.Regex? regex = null;
        if (useRegex)
        {
            try
            {
                var opts = caseSensitive
                    ? System.Text.RegularExpressions.RegexOptions.None
                    : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                var regexPattern = wholeWord ? $@"\b{query}\b" : query;
                regex = new System.Text.RegularExpressions.Regex(regexPattern, opts);
            }
            catch
            {
                SetSearchStatus("Invalid regular expression.");
                return;
            }
        }

        int totalMatches = 0;
        int totalFiles   = 0;

        try
        {
            var files = await Task.Run(() =>
                Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsTextFile(f) && !IsExcluded(f))
                    .ToList());

            foreach (var file in files)
            {
                var lines = await Task.Run(() =>
                {
                    try { return File.ReadAllLines(file); }
                    catch { return Array.Empty<string>(); }
                });

                bool fileHadMatch = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    bool matched;
                    if (useRegex && regex != null)
                        matched = regex.IsMatch(lines[i]);
                    else if (wholeWord)
                        matched = ContainsWholeWord(lines[i], query, comparison);
                    else
                        matched = lines[i].Contains(query, comparison);

                    if (matched)
                    {
                        AddFileResult(file, lines[i].Trim(), i + 1);
                        totalMatches++;
                        fileHadMatch = true;
                    }
                }
                if (fileHadMatch) totalFiles++;
            }

            SetSearchStatus(totalMatches == 0
                ? $"No matches for '{query}'."
                : $"{totalMatches} match(es) in {totalFiles} file(s).");
        }
        catch (Exception ex)
        {
            SetSearchStatus($"Error: {ex.Message}");
        }
    }

    private static bool ContainsWholeWord(string line, string word, StringComparison comparison)
    {
        int idx = 0;
        while ((idx = line.IndexOf(word, idx, comparison)) >= 0)
        {
            bool leftOk  = idx == 0 || !char.IsLetterOrDigit(line[idx - 1]) && line[idx - 1] != '_';
            bool rightOk = idx + word.Length >= line.Length ||
                           !char.IsLetterOrDigit(line[idx + word.Length]) && line[idx + word.Length] != '_';
            if (leftOk && rightOk) return true;
            idx++;
        }
        return false;
    }

    // ── Result rendering ─────────────────────────────────────────

    /// <summary>Add one clickable result row to the results panel.</summary>
    private void AddFileResult(string filePath, string? lineText, int lineNumber)
    {
        var panel = this.FindControl<ItemsControl>("SearchResultsPanel");
        if (panel == null) return;

        var root = _projectPath ?? _viewModel.CurrentProjectPath ?? "";
        var relPath = filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? filePath[(root.Length > 0 && (root.EndsWith('/') || root.EndsWith('\\')) ? root.Length : root.Length + 1)..]
                .TrimStart('/', '\\')
            : Path.GetFileName(filePath);

        var border = new Border
        {
            Background = Avalonia.Media.Brushes.Transparent,
            Padding    = new Thickness(6, 4),
            Cursor     = new Cursor(StandardCursorType.Hand),
            Margin     = new Thickness(0, 1)
        };
        border.PointerEntered += (_, _) => border.Background =
            new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#30FFFFFF"));
        border.PointerExited  += (_, _) => border.Background = Avalonia.Media.Brushes.Transparent;
        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                OpenFileInEditor(filePath);
        };

        var innerStack = new StackPanel { Spacing = 2 };

        var fileNameLine = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var iconTb = new TextBlock
        {
            Text   = GetFileIcon(filePath),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 11
        };
        var pathTb = new TextBlock
        {
            Text      = relPath,
            FontSize  = 12,
            Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#FFCDD6F4")),
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(iconTb, 0);
        Grid.SetColumn(pathTb, 1);
        fileNameLine.Children.Add(iconTb);
        fileNameLine.Children.Add(pathTb);
        innerStack.Children.Add(fileNameLine);

        if (lineNumber > 0 && !string.IsNullOrEmpty(lineText))
        {
            var linePreview = new TextBlock
            {
                Text      = $"  line {lineNumber}: {lineText}",
                FontSize  = 11,
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#FF9399B2")),
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Margin   = new Thickness(0, 1, 0, 0)
            };
            innerStack.Children.Add(linePreview);
        }

        border.Child = innerStack;
        panel.Items.Add(border);
        // Ensure the results scroll view is visible
        var sv = this.FindControl<ScrollViewer>("SearchResultsScrollViewer");
        if (sv != null) sv.IsVisible = true;
    }

    private void ClearSearchResults()
    {
        var panel = this.FindControl<ItemsControl>("SearchResultsPanel");
        if (panel != null) panel.Items.Clear();
        var sv = this.FindControl<ScrollViewer>("SearchResultsScrollViewer");
        if (sv != null) sv.IsVisible = false;
    }

    private void SetSearchStatus(string text)
    {
        var lbl    = this.FindControl<TextBlock>("SearchStatusLabel");
        var border = this.FindControl<Border>("SearchStatusBorder");
        if (lbl    != null) lbl.Text       = text;
        if (border != null) border.IsVisible = !string.IsNullOrEmpty(text);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static bool IsExcluded(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Any(p =>
            p == "bin" || p == "obj" || p == ".git" || p == "node_modules" ||
            p == ".vs" || p == "packages" || p == "__pycache__");
    }

    private static bool IsTextFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cs" or ".axaml" or ".xaml" or ".xml" or ".json" or ".yaml" or ".yml"
                   or ".txt" or ".md" or ".csproj" or ".sln" or ".slnx" or ".props" or ".targets"
                   or ".razor" or ".html" or ".css" or ".js" or ".ts" or ".config" or ".ini"
                   or ".sh" or ".bat" or ".cmd" or ".ps1" or ".gitignore" or ".editorconfig";
    }

    private static string GetFileIcon(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs"    => "C#",
            ".axaml" => "AX",
            ".xaml"  => "XA",
            ".json"  => "{}",
            ".xml"   => "<>",
            ".md"    => "📄",
            ".csproj" or ".sln" or ".slnx" => "⚙",
            _        => "📄"
        };


    private void InitializeAccountPanel()
    {
        var accountPanelContainer = this.FindControl<Border>("AccountSidePanel");
        if (accountPanelContainer == null) return;
        
        _accountPanelControl = new AccountPanelControl();
        
        // Subscribe to events
        _accountPanelControl.StatusChanged += (s, status) =>
        {
            _viewModel.StatusText = status;
        };
        
        _accountPanelControl.RepositoryCloneRequested += async (s, repo) =>
        {
            // Show clone dialog with pre-filled URL
            var result = await ShowFolderPickerAsync("Select folder to clone repository");
            if (!string.IsNullOrEmpty(result))
            {
                var service = new GitHubAccountService();
                var clonePath = Path.Combine(result, repo.Name);
                _viewModel.StatusText = $"Cloning {repo.FullName}...";
                
                var success = await service.CloneRepositoryAsync(repo.Url, clonePath);
                if (success)
                {
                    _viewModel.StatusText = $"Successfully cloned {repo.Name}";
                    // Ask if user wants to open the cloned repository
                    LoadProject(clonePath);
                    UpdateTitle();
                }
                else
                {
                    _viewModel.StatusText = $"Failed to clone {repo.Name}";
                }
            }
        };
        
        // Add to container
        accountPanelContainer.Child = _accountPanelControl;
    }

    private async Task<string?> ShowFolderPickerAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private void StartDebug_Click(object? sender, RoutedEventArgs e)
    {
        // Start debugging
        _ = RunProjectAsync();
    }

    #endregion

    #region File Tree Context Menu Handlers

    private async void AddNewItem_Click(object? sender, RoutedEventArgs e)
    {
        var targetDir = GetTargetDirectory();
        
        // Ensure directory exists before opening the dialog
        if (!Directory.Exists(targetDir))
        {
            _viewModel.StatusText = $"Error: Target directory does not exist: {targetDir}";
            return;
        }
        
        var addItemWindow = new AddNewItemWindow(targetDir);
        var result = await addItemWindow.ShowDialog<string?>(this);
        
        if (!string.IsNullOrEmpty(result))
        {
            RefreshFileTree();
            OpenFileInEditor(result);
            _viewModel.StatusText = $"Created: {Path.GetFileName(result)}";
        }
    }

    private async void AddNewFolder_Click(object? sender, RoutedEventArgs e)
    {
        var targetDir = GetTargetDirectory();
        
        var folderName = await ShowInputDialogAsync("New Folder", "Enter folder name:", "NewFolder");
        if (!string.IsNullOrEmpty(folderName))
        {
            try
            {
                var newFolderPath = Path.Combine(targetDir, folderName);
                Directory.CreateDirectory(newFolderPath);
                RefreshFileTree();
                _viewModel.StatusText = $"Created folder: {folderName}";
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Error creating folder: {ex.Message}";
            }
        }
    }

    private async void AddNewProject_Click(object? sender, RoutedEventArgs e)
    {
        // Find solution file
        var solutionPath = FindSolutionFile();
        if (string.IsNullOrEmpty(solutionPath))
        {
            _viewModel.StatusText = "No solution file found. Create a solution first.";
            return;
        }

        var addProjectWindow = new AddProjectToSolutionWindow(solutionPath);
        var result = await addProjectWindow.ShowDialog<string?>(this);
        
        if (!string.IsNullOrEmpty(result))
        {
            RefreshFileTree();
            _viewModel.StatusText = $"Added project: {Path.GetFileNameWithoutExtension(result)}";
        }
    }

    private async void RenameItem_Click(object? sender, RoutedEventArgs e)
    {
        var selectedItem = GetSelectedTreeItem();
        if (selectedItem == null) return;

        var newName = await ShowInputDialogAsync("Rename", "Enter new name:", selectedItem.Name);
        if (!string.IsNullOrEmpty(newName) && newName != selectedItem.Name)
        {
            try
            {
                var directory = Path.GetDirectoryName(selectedItem.FullPath);
                if (directory == null) return;

                var newPath = Path.Combine(directory, newName);
                
                if (selectedItem.IsDirectory)
                {
                    Directory.Move(selectedItem.FullPath, newPath);
                }
                else
                {
                    File.Move(selectedItem.FullPath, newPath);
                    
                    // Update tab if file is open
                    var tab = _viewModel.FindTabByPath(selectedItem.FullPath);
                    if (tab != null)
                    {
                        tab.FilePath = newPath;
                        tab.FileName = newName;
                    }
                }
                
                RefreshFileTree();
                _viewModel.StatusText = $"Renamed to: {newName}";
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Error renaming: {ex.Message}";
            }
        }
    }

    private async void DeleteItem_Click(object? sender, RoutedEventArgs e)
    {
        var selectedItems = GetSelectedTreeItems()
            .Where(x => x.ItemType is not FileTreeItemType.Solution
                                   and not FileTreeItemType.SolutionFolder
                                   and not FileTreeItemType.Project
                                   and not FileTreeItemType.EspProject)
            .ToList();

        if (selectedItems.Count == 0) return;

        string confirmMsg;
        if (selectedItems.Count == 1)
        {
            var single = selectedItems[0];
            confirmMsg = "Are you sure you want to delete '" + single.Name + "'?" +
                         (single.IsDirectory ? "\n\nThis will delete all contents." : "");
        }
        else
        {
            var dirCount  = selectedItems.Count(x => x.IsDirectory);
            var fileCount = selectedItems.Count(x => !x.IsDirectory);
            var parts     = new List<string>();
            if (fileCount > 0) parts.Add(fileCount + " file" + (fileCount > 1 ? "s" : ""));
            if (dirCount  > 0) parts.Add(dirCount  + " folder" + (dirCount > 1 ? "s" : ""));
            confirmMsg = "Are you sure you want to delete " + selectedItems.Count + " items (" + string.Join(", ", parts) + ")?";
            if (dirCount > 0) confirmMsg += "\n\nAll folder contents will be deleted.";
        }

        var confirmDelete = await ShowConfirmDialogAsync("Confirm Delete", confirmMsg);
        if (!confirmDelete) return;

        int deleted = 0, errors = 0;
        foreach (var item in selectedItems)
        {
            try
            {
                if (item.IsDirectory)
                {
                    Directory.Delete(item.FullPath, true);
                }
                else
                {
                    File.Delete(item.FullPath);
                    var tab = _viewModel.FindTabByPath(item.FullPath);
                    if (tab != null) _viewModel.CloseTab(tab);
                }
                deleted++;
            }
            catch (Exception ex)
            {
                errors++;
                _viewModel.StatusText = "Error deleting '" + item.Name + "': " + ex.Message;
            }
        }

        RefreshFileTree();
        _viewModel.StatusText = errors == 0
            ? "Deleted " + deleted + " item" + (deleted > 1 ? "s" : "")
            : "Deleted " + deleted + ", failed " + errors;
    }

    private void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        var selectedItem = GetSelectedTreeItem();
        if (selectedItem == null) return;

        _ = CopyToClipboardAsync(selectedItem.FullPath);
        _viewModel.StatusText = $"Copied path: {selectedItem.FullPath}";
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OpenInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        var selectedItem = GetSelectedTreeItem();
        if (selectedItem == null) return;

        try
        {
            var path = selectedItem.IsDirectory ? selectedItem.FullPath : Path.GetDirectoryName(selectedItem.FullPath);
            if (!string.IsNullOrEmpty(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error opening explorer: {ex.Message}";
        }
    }

    private void OpenInTerminal_Click(object? sender, RoutedEventArgs e)
    {
        var selectedItem = GetSelectedTreeItem();
        if (selectedItem == null) return;

        var targetDir = selectedItem.IsDirectory ? selectedItem.FullPath : Path.GetDirectoryName(selectedItem.FullPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            _terminalControl?.ChangeDirectory(targetDir);
            SwitchToolWindowPanel("terminal");
            _viewModel.StatusText = $"Terminal directory: {targetDir}";
        }
    }

    private void RefreshTree_Click(object? sender, RoutedEventArgs e)
    {
        RefreshFileTree();
    }

    #endregion

    #region Context Menu — JetBrains Style

    private async void ContextMenu_RunProject_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        if (item == null) return;
        var projectPath = FindProjectFile(item.FullPath);
        if (string.IsNullOrEmpty(projectPath)) { _viewModel.StatusText = "Could not find project file"; return; }
        await _runConfigService.LoadConfigurationsAsync(projectPath);
        var config = _runConfigService.Configurations.FirstOrDefault(c => c.ProjectPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
        if (config != null) { _runConfigService.SetActiveConfiguration(config); await RunWithConfigurationAsync(config); }
        else { _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("run"); await _buildService.BuildAndRunAsync(projectPath); }
    }

    private void ContextMenu_DebugProject_Click(object? sender, RoutedEventArgs e) => ContextMenu_RunProject_Click(sender, e);

    private async void ContextMenu_NewClass_Click(object? sender, RoutedEventArgs e)           => await CreateNewCSharpFile("class");
    private async void ContextMenu_NewInterface_Click(object? sender, RoutedEventArgs e)       => await CreateNewCSharpFile("interface");
    private async void ContextMenu_NewRecord_Click(object? sender, RoutedEventArgs e)          => await CreateNewCSharpFile("record");
    private async void ContextMenu_NewEnum_Click(object? sender, RoutedEventArgs e)            => await CreateNewCSharpFile("enum");
    private async void ContextMenu_NewAvaloniaWindow_Click(object? sender, RoutedEventArgs e)  => await CreateNewAvaloniaFile("window");
    private async void ContextMenu_NewAvaloniaUserControl_Click(object? sender, RoutedEventArgs e) => await CreateNewAvaloniaFile("usercontrol");

    private async Task CreateNewCSharpFile(string type)
    {
        var targetDir  = GetTargetDirectory();
        var typeName   = await ShowInputDialogAsync($"New {type}", $"Enter {type} name:", $"New{char.ToUpper(type[0])}{type[1..]}");
        if (string.IsNullOrEmpty(typeName)) return;
        var ns = DetermineNamespace(targetDir);
        var template = type.ToLower() switch
        {
            "interface" => $"namespace {ns};\n\npublic interface {typeName}\n{{\n}}\n",
            "record"    => $"namespace {ns};\n\npublic record {typeName};\n",
            "enum"      => $"namespace {ns};\n\npublic enum {typeName}\n{{\n}}\n",
            _           => $"namespace {ns};\n\npublic class {typeName}\n{{\n    public {typeName}() {{ }}\n}}\n"
        };
        var filePath = Path.Combine(targetDir, $"{typeName}.cs");
        try { await File.WriteAllTextAsync(filePath, template); RefreshFileTree(); OpenFileInEditor(filePath); _viewModel.StatusText = $"Created {type}: {typeName}.cs"; }
        catch (Exception ex) { _viewModel.StatusText = $"Error creating {type}: {ex.Message}"; }
    }

    private async Task CreateNewAvaloniaFile(string type)
    {
        var targetDir = GetTargetDirectory();
        var typeName  = await ShowInputDialogAsync($"New Avalonia {type}", $"Enter {type} name:", $"My{char.ToUpper(type[0])}{type[1..]}");
        if (string.IsNullOrEmpty(typeName)) return;
        var ns       = DetermineNamespace(targetDir);
        var baseType = type.ToLower() == "window" ? "Window" : "UserControl";
        var axaml = $"<{baseType} xmlns=\"https://github.com/avaloniaui\"\n        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n        x:Class=\"{ns}.{typeName}\">\n    <Grid/>\n</{baseType}>\n";
        var cs    = $"using Avalonia.Controls;\nnamespace {ns};\npublic partial class {typeName} : {baseType}\n{{\n    public {typeName}() {{ InitializeComponent(); }}\n}}\n";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(targetDir, $"{typeName}.axaml"), axaml);
            await File.WriteAllTextAsync(Path.Combine(targetDir, $"{typeName}.axaml.cs"), cs);
            RefreshFileTree(); OpenFileInEditor(Path.Combine(targetDir, $"{typeName}.axaml"));
            _viewModel.StatusText = $"Created Avalonia {type}: {typeName}";
        }
        catch (Exception ex) { _viewModel.StatusText = $"Error: {ex.Message}"; }
    }

    private string DetermineNamespace(string directory)
    {
        var projectPath = FindProjectFileInParents(directory);
        if (string.IsNullOrEmpty(projectPath)) return Path.GetFileName(directory) ?? "MyNamespace";
        var projectDir  = Path.GetDirectoryName(projectPath);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        if (string.IsNullOrEmpty(projectDir)) return projectName ?? "MyNamespace";
        if (directory.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
        {
            var rel = directory[projectDir.Length..].TrimStart(Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(rel) ? projectName ?? "MyNamespace" : $"{projectName}.{rel.Replace(Path.DirectorySeparatorChar, '.')}";
        }
        return projectName ?? "MyNamespace";
    }

    private string? FindProjectFileInParents(string directory)
    {
        var dir = new DirectoryInfo(directory);
        while (dir != null) { var f = dir.GetFiles("*.csproj").FirstOrDefault(); if (f != null) return f.FullName; dir = dir.Parent; }
        return null;
    }

    private async void ContextMenu_AddExistingProject_Click(object? sender, RoutedEventArgs e)
    {
        var sln = FindSolutionFile();
        if (string.IsNullOrEmpty(sln)) { _viewModel.StatusText = "No solution file found"; return; }
        var dialog = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Existing Project", AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { new("C# Project") { Patterns = new[] { "*.csproj", "*.nfproj" } } }
        });
        if (dialog.Count > 0)
        {
            var projectPath = dialog[0].Path.LocalPath;
            var svc = new SolutionService();
            if (await svc.AddProjectToSolutionAsync(sln, projectPath)) { RefreshFileTree(); _viewModel.StatusText = $"Added: {Path.GetFileNameWithoutExtension(projectPath)}"; }
            else _viewModel.StatusText = "Failed to add project";
        }
    }

    private async void ContextMenu_AddExistingItem_Click(object? sender, RoutedEventArgs e)
    {
        var targetDir = GetTargetDirectory();
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add Existing Item", AllowMultiple = true });
        foreach (var file in result)
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file.Path.LocalPath));
            try { if (file.Path.LocalPath != dest) File.Copy(file.Path.LocalPath, dest, true); }
            catch (Exception ex) { _viewModel.StatusText = $"Error: {ex.Message}"; }
        }
        if (result.Count > 0) { RefreshFileTree(); _viewModel.StatusText = $"Added {result.Count} item(s)"; }
    }

    private async void ContextMenu_BuildProject_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        var projectPath = item?.ItemType == FileTreeItemType.Solution ? item.FullPath : FindProjectFile(item?.FullPath ?? "");
        if (string.IsNullOrEmpty(projectPath)) { _viewModel.StatusText = "No project to build"; return; }
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        if (IsNanoFrameworkProject(projectPath)) await _nanoBuildService.BuildAsync(projectPath);
        else await _buildService.BuildAsync(projectPath);
    }

    private async void ContextMenu_RebuildProject_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        var projectPath = item?.ItemType == FileTreeItemType.Solution ? item.FullPath : FindProjectFile(item?.FullPath ?? "");
        if (string.IsNullOrEmpty(projectPath)) { _viewModel.StatusText = "No project to rebuild"; return; }
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        if (IsNanoFrameworkProject(projectPath)) await _nanoBuildService.BuildAsync(projectPath);
        else { await _buildService.CleanAsync(projectPath); await _buildService.BuildAsync(projectPath); }
    }

    private async void ContextMenu_CleanProject_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        var projectPath = item?.ItemType == FileTreeItemType.Solution ? item.FullPath : FindProjectFile(item?.FullPath ?? "");
        if (string.IsNullOrEmpty(projectPath)) { _viewModel.StatusText = "No project to clean"; return; }
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        if (!IsNanoFrameworkProject(projectPath)) await _buildService.CleanAsync(projectPath);
        else _viewModel.StatusText = "Clean not supported for nanoFramework projects";
    }

    private void AnalyzeProject_Click(object? sender, RoutedEventArgs e)   => _ = AnalyzeProjectAsync();
    private void RefreshAnalysis_Click(object? sender, RoutedEventArgs e)  => _ = AnalyzeProjectAsync();
    private void ClearProblems_Click(object? sender, RoutedEventArgs e)    { _viewModel.Problems.Clear(); _viewModel.StatusText = "Problems cleared"; UpdateTabDiagnosticIndicators(); }

    /// <summary>Whether the Problems tab shows all errors (true) or only current file (false).</summary>
    private bool _problemsShowAll = true;

    private async void CopyProblems_Click(object? sender, RoutedEventArgs e)
    {
        var items = GetFilteredProblems();
        if (items.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        foreach (var item in items)
        {
            var sev = item.Severity switch
            {
                DiagnosticSeverity.Error => "Error",
                DiagnosticSeverity.Warning => "Warning",
                DiagnosticSeverity.Info => "Info",
                _ => "Hint",
            };
            sb.AppendLine($"{sev} {item.Code}: {item.Message} [{item.Location}]");
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(sb.ToString());
            _viewModel.StatusText = $"Copied {items.Count} diagnostics to clipboard";
        }
        catch { _viewModel.StatusText = "Failed to copy to clipboard"; }
    }

    private async void CopySingleProblem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            // Walk up to the ListBoxItem to get the DataContext
            var item = (btn.DataContext as DiagnosticItem)
                       ?? (btn.Parent as Grid)?.DataContext as DiagnosticItem;
            if (item == null) return;

            var sev = item.Severity switch
            {
                DiagnosticSeverity.Error => "Error",
                DiagnosticSeverity.Warning => "Warning",
                DiagnosticSeverity.Info => "Info",
                _ => "Hint",
            };
            var text = $"{sev} {item.Code}: {item.Message} [{item.Location}]";

            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(text);
                _viewModel.StatusText = $"Copied: {item.Code}";
            }
            catch { }
        }
    }

    private void ProblemsTabAll_Click(object? sender, RoutedEventArgs e)
    {
        _problemsShowAll = true;
        UpdateProblemsTabStyles();
        ApplyProblemsFilter();
    }

    private void ProblemsTabCurrentFile_Click(object? sender, RoutedEventArgs e)
    {
        _problemsShowAll = false;
        UpdateProblemsTabStyles();
        ApplyProblemsFilter();
    }

    private void UpdateProblemsTabStyles()
    {
        var tabAll = this.FindControl<Button>("ProblemsTabAll");
        var tabCurrent = this.FindControl<Button>("ProblemsTabCurrentFile");
        var label = this.FindControl<TextBlock>("ProblemsCurrentFileLabel");

        if (tabAll != null)
        {
            tabAll.Background = _problemsShowAll
                ? new SolidColorBrush(Color.Parse("#FF3E3050"))
                : Brushes.Transparent;
            tabAll.Foreground = _problemsShowAll
                ? new SolidColorBrush(Color.Parse("#FFF0E8F4"))
                : new SolidColorBrush(Color.Parse("#FF9E90B0"));
        }
        if (tabCurrent != null)
        {
            tabCurrent.Background = !_problemsShowAll
                ? new SolidColorBrush(Color.Parse("#FF3E3050"))
                : Brushes.Transparent;
            tabCurrent.Foreground = !_problemsShowAll
                ? new SolidColorBrush(Color.Parse("#FFF0E8F4"))
                : new SolidColorBrush(Color.Parse("#FF9E90B0"));
        }
        if (label != null)
        {
            var currentPath = _viewModel.ActiveTab?.FilePath;
            label.Text = string.IsNullOrEmpty(currentPath) ? "" : Path.GetFileName(currentPath);
        }
    }

    private List<DiagnosticItem> GetFilteredProblems()
    {
        if (_problemsShowAll)
            return _viewModel.Problems.ToList();

        var currentPath = _viewModel.ActiveTab?.FilePath;
        if (string.IsNullOrEmpty(currentPath))
            return _viewModel.Problems.ToList();

        return _viewModel.Problems
            .Where(p => string.Equals(p.FilePath, currentPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ApplyProblemsFilter()
    {
        var listBox = this.FindControl<ListBox>("ProblemsListBox");
        if (listBox == null) return;

        if (_problemsShowAll)
        {
            listBox.ItemsSource = _viewModel.Problems;
        }
        else
        {
            var currentPath = _viewModel.ActiveTab?.FilePath;
            if (string.IsNullOrEmpty(currentPath))
            {
                listBox.ItemsSource = _viewModel.Problems;
            }
            else
            {
                var filtered = _viewModel.Problems
                    .Where(p => string.Equals(p.FilePath, currentPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                listBox.ItemsSource = filtered;
            }
        }
    }

    private void ProblemsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DiagnosticItem diagnostic)
        {
            if (!string.IsNullOrEmpty(diagnostic.FilePath) && File.Exists(diagnostic.FilePath))
            {
                // Open file (creates tab if needed, switches to existing tab)
                OpenFileInEditor(diagnostic.FilePath);
                
                // Update tab visual styles (active class + error/warning classes)
                UpdateTabButtonStyles();
                
                // Navigate to error line/column
                _insaitEditor?.GoToLine(diagnostic.Line, diagnostic.Column);
                _viewModel.StatusText = $"{diagnostic.SeverityIcon} {diagnostic.Code}: {diagnostic.Message}";
            }
        }
    }

    private void ExplorerNewFile_Click(object? sender, RoutedEventArgs e)   => AddNewItem_Click(sender, e);
    private void ExplorerNewFolder_Click(object? sender, RoutedEventArgs e) => AddNewFolder_Click(sender, e);

    // ═══════════════════════════════════════════════════════════
    //  Tab diagnostic indicators & visual styles
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Updates HasErrors/HasWarnings/ErrorCount/WarningCount on all open tabs
    /// based on the current Problems collection.
    /// </summary>
    private void UpdateTabDiagnosticIndicators()
    {
        // Build a lookup: filePath → (errorCount, warningCount)
        var diagnosticsByFile = new Dictionary<string, (int errors, int warnings)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var problem in _viewModel.Problems)
        {
            if (string.IsNullOrEmpty(problem.FilePath)) continue;
            
            if (!diagnosticsByFile.TryGetValue(problem.FilePath, out var counts))
                counts = (0, 0);

            if (problem.Severity == DiagnosticSeverity.Error)
                counts.errors++;
            else if (problem.Severity == DiagnosticSeverity.Warning)
                counts.warnings++;

            diagnosticsByFile[problem.FilePath] = counts;
        }

        // Apply to each tab
        foreach (var tab in _viewModel.Tabs)
        {
            if (diagnosticsByFile.TryGetValue(tab.FilePath, out var c))
            {
                tab.ErrorCount = c.errors;
                tab.WarningCount = c.warnings;
                tab.HasErrors = c.errors > 0;
                tab.HasWarnings = c.warnings > 0;
            }
            else
            {
                tab.ErrorCount = 0;
                tab.WarningCount = 0;
                tab.HasErrors = false;
                tab.HasWarnings = false;
            }
        }

        // Update CSS classes on tab buttons
        UpdateTabButtonStyles();
    }

    /// <summary>
    /// Updates CSS classes (active, has-errors, has-warnings) on all tab Button elements.
    /// </summary>
    private void UpdateTabButtonStyles()
    {
        var editorGrid = this.FindControl<Grid>("EditorGrid");
        if (editorGrid == null) return;

        Button? activeBtn = null;
        foreach (var btn in editorGrid.GetVisualDescendants().OfType<Button>())
        {
            if (!btn.Classes.Contains("tab-button")) continue;
            if (btn.Tag is not EditorTab tab) continue;
            ApplyTabButtonClasses(btn, tab);
            if (tab == _viewModel.ActiveTab)
                activeBtn = btn;
        }

        // Scroll the active tab into view
        if (activeBtn != null)
            ScrollActiveTabIntoView(activeBtn);
    }

    /// <summary>
    /// Scrolls the tab bar horizontally so the active tab button is fully visible.
    /// </summary>
    private void ScrollActiveTabIntoView(Button activeBtn)
    {
        var scroller = this.FindControl<ScrollViewer>("TabScrollViewer");
        if (scroller == null) return;

        // Wait for layout to complete before measuring positions
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // Get the button's bounds relative to the ScrollViewer's content
                var transform = activeBtn.TransformToVisual(scroller);
                if (transform == null) return;

                var btnBoundsInScroller = transform.Value.Transform(new Point(0, 0));
                var btnLeft  = btnBoundsInScroller.X;
                var btnRight = btnLeft + activeBtn.Bounds.Width;

                var viewportLeft  = scroller.Offset.X;
                var viewportRight = scroller.Offset.X + scroller.Viewport.Width;

                // Add a small margin so the tab doesn't sit exactly at the edge
                const double margin = 20;

                if (btnLeft < viewportLeft + margin)
                {
                    // Tab is to the left of the visible area — scroll left
                    scroller.Offset = new Vector(Math.Max(0, btnLeft - margin), 0);
                }
                else if (btnRight > viewportRight - margin)
                {
                    // Tab is to the right — scroll right
                    scroller.Offset = new Vector(btnRight - scroller.Viewport.Width + margin, 0);
                }
            }
            catch { /* layout not ready yet — skip */ }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyTabButtonClasses(Button btn, EditorTab tab)
    {
        // Active state
        if (tab == _viewModel.ActiveTab)
            { if (!btn.Classes.Contains("active")) btn.Classes.Add("active"); }
        else
            btn.Classes.Remove("active");

        // Error state
        if (tab.HasErrors)
            { if (!btn.Classes.Contains("has-errors")) btn.Classes.Add("has-errors"); }
        else
            btn.Classes.Remove("has-errors");

        // Warning state
        if (tab.HasWarnings && !tab.HasErrors)
            { if (!btn.Classes.Contains("has-warnings")) btn.Classes.Add("has-warnings"); }
        else
            btn.Classes.Remove("has-warnings");
    }

    #endregion

}

/// <summary>
/// Статус Git репозиторію
/// </summary>
public enum GitRepoStatus
{
    Unknown,
    Healthy,
    NotInitialized,
    Corrupted
}

/// <summary>
/// Результат перевірки Git репозиторію
/// </summary>
public class GitRepositoryCheckResult
{
    public string Path { get; set; } = string.Empty;
    public GitRepoStatus Status { get; set; } = GitRepoStatus.Unknown;
    public string Message { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public int ChangesCount { get; set; }
}
