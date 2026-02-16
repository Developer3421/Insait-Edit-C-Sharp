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
using Insait_Edit_C_Sharp.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow : Window
{
    private bool _isMaximized;
    private PixelPoint _restorePosition;
    private Size _restoreSize;
    private readonly MainViewModel _viewModel;
    private readonly FileService _fileService;
    private readonly BuildService _buildService;
    private readonly CodeAnalysisService _codeAnalysisService;
    private readonly RunConfigurationService _runConfigService;
    private readonly PublishService _publishService;
    private readonly CopilotCliService _copilotCliService;
    private string? _projectPath;
    private MonacoEditorControl? _monacoEditor;
    private TerminalControl? _terminalControl;
    private bool _isBuildInProgress;
    private bool _isAnalysisInProgress;
    private readonly StringBuilder _buildOutput = new();

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? projectPath)
    {
        InitializeComponent();
        
        _viewModel = new MainViewModel();
        _fileService = new FileService();
        _buildService = new BuildService();
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
        
        // Initialize Monaco Editor
        InitializeMonacoEditor();
        
        // Initialize Build Service events
        InitializeBuildService();
        
        // Initialize Code Analysis Service events
        InitializeCodeAnalysisService();
        
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
                var projectWindow = new NewProjectWindow();
                var projectResult = await projectWindow.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(projectResult))
                {
                    var projectDir = Path.GetDirectoryName(projectResult);
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        _projectPath = projectDir;
                        LoadProject(projectDir);
                        UpdateTitle();
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
        
        if (_monacoEditor != null)
        {
            _monacoEditor.SetContent(string.Empty, "csharp");
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
            if (_monacoEditor != null && _viewModel.ActiveTab != null)
            {
                _monacoEditor.SetContent(_viewModel.ActiveTab.Content, _viewModel.ActiveTab.Language);
            }
            else if (_monacoEditor != null)
            {
                _monacoEditor.SetContent("", "plaintext");
            }
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
            
            dir = dir.Parent;
        }
        return startPath;
    }

    private void InitializeMonacoEditor()
    {
        var container = this.FindControl<Border>("MonacoEditorContainer");
        if (container != null)
        {
            _monacoEditor = new MonacoEditorControl();
            _monacoEditor.EditorReady += OnEditorReady;
            _monacoEditor.ContentChanged += OnEditorContentChanged;
            _monacoEditor.ContentChangedWithValue += OnEditorContentChangedWithValue;
            _monacoEditor.CursorPositionChanged += OnCursorPositionChanged;
            container.Child = _monacoEditor;
        }
    }

    private void OnEditorReady(object? sender, EventArgs e)
    {
        _viewModel.StatusText = "Monaco Editor Ready";
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
        // Update the active tab's content with the new value from Monaco
        if (_viewModel.ActiveTab != null)
        {
            _viewModel.ActiveTab.Content = e.NewContent;
            _viewModel.ActiveTab.IsDirty = true;
            _viewModel.ActiveTab.LastModified = DateTime.Now;
            _viewModel.StatusText = $"Modified: {_viewModel.ActiveTab.FileName}";
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
        if (File.Exists(projectPath))
        {
            var extension = Path.GetExtension(projectPath).ToLowerInvariant();
            
            if (extension == ".sln" || extension == ".slnx")
            {
                // Load solution - load its directory
                var directory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    _viewModel.LoadProjectFolder(directory);
                    _viewModel.StatusText = $"Loaded solution: {Path.GetFileName(projectPath)}";
                    
                    // Load run configurations for the solution
                    _ = _runConfigService.LoadConfigurationsAsync(projectPath);
                    
                    // Update Git panel
                    UpdateGitPanel(directory);
                }
                else
                {
                    _viewModel.StatusText = $"Solution directory not found: {directory}";
                }
            }
            else if (extension == ".csproj" || extension == ".fsproj" || extension == ".vbproj")
            {
                // Load project - load its directory
                var directory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    _viewModel.LoadProjectFolder(directory);
                    _viewModel.StatusText = $"Loaded project: {Path.GetFileName(projectPath)}";
                    
                    // Load run configurations for the project
                    _ = _runConfigService.LoadConfigurationsAsync(projectPath);
                    
                    // Update Git panel
                    UpdateGitPanel(directory);
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
            _viewModel.LoadProjectFolder(projectPath);
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
        if (_gitPanelControl != null)
        {
            // Ensure we pass a directory path, not a file path
            string dirPath = path;
            if (File.Exists(path))
            {
                dirPath = Path.GetDirectoryName(path) ?? path;
            }
            _ = _gitPanelControl.SetRepositoryPathAsync(dirPath);
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
                var projectWindow = new NewProjectWindow();
                var projectResult = await projectWindow.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(projectResult))
                {
                    var projectDir = Path.GetDirectoryName(projectResult);
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        _projectPath = projectDir;
                        LoadProject(projectDir);
                        UpdateTitle();
                    }
                }
                break;
            case "AddProjectToSolution":
                await AddNewProjectToSolutionAsync();
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
                _monacoEditor?.Undo();
                break;
            case "Redo":
                _monacoEditor?.Redo();
                break;
            case "Find":
                _monacoEditor?.Find();
                break;
            case "Replace":
                _monacoEditor?.Replace();
                break;
            case "FindInFiles":
                // TODO: Implement find in files
                break;
            case "FormatDocument":
                _monacoEditor?.FormatDocument();
                break;
            case "ToggleComment":
                // TODO: Implement toggle comment
                break;

            // View actions
            case "ToggleAI":
                ToggleAIPanel();
                break;
            case "ShowExplorer":
                // Already visible
                break;
            case "ShowSearch":
                // TODO: Implement search panel
                break;
            case "ShowSourceControl":
                // TODO: Implement source control panel
                break;
            case "ShowTerminal":
                SwitchToolWindowPanel("terminal");
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
                SwitchToolWindowPanel("debug");
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

            // Debug actions
            case "StartDebugging":
                await RunProjectAsync();
                break;
            case "StartWithoutDebugging":
                await RunProjectAsync();
                break;
            case "StopDebugging":
                CancelBuild();
                break;
            case "ToggleBreakpoint":
                // TODO: Implement breakpoint toggle
                break;
            case "DeleteAllBreakpoints":
                // TODO: Implement delete all breakpoints
                break;
            case "StepOver":
                // TODO: Implement step over
                break;
            case "StepInto":
                // TODO: Implement step into
                break;
            case "StepOut":
                // TODO: Implement step out
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
            case "ManageExtensions":
                // TODO: Implement extensions
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
                // First time loading, use LoadProjectFolder
                _viewModel.LoadProjectFolder(_projectPath);
            }
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
            Text = "A modern, lightweight C# IDE built with Avalonia UI and Monaco Editor.",
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

    private GitPanelControl? _gitPanelControl;
    private string _currentSidePanel = "explorer";

    private void Explorer_Click(object? sender, RoutedEventArgs e)
    {
        SwitchSidePanel("explorer");
    }

    private void Search_Click(object? sender, RoutedEventArgs e)
    {
        SwitchSidePanel("search");
    }

    private async void Git_Click(object? sender, RoutedEventArgs e)
    {
        SwitchSidePanel("git");
        
        // Initialize Git panel if not done yet
        if (_gitPanelControl == null)
        {
            InitializeGitPanel();
        }
        
        // Refresh Git status when panel is shown
        if (_gitPanelControl != null)
        {
            await _gitPanelControl.RefreshAsync();
        }
    }

    private void Debug_Click(object? sender, RoutedEventArgs e)
    {
        SwitchSidePanel("debug");
    }

    private void Extensions_Click(object? sender, RoutedEventArgs e)
    {
        SwitchSidePanel("extensions");
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
            
            // Оновлюємо Git панель якщо вона відкрита
            if (_gitPanelControl != null)
            {
                await _gitPanelControl.SetRepositoryPathAsync(path);
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

    private void Account_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show account panel/dialog
        _viewModel.StatusText = "Account settings coming soon...";
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show settings
        _viewModel.StatusText = "Settings coming soon...";
    }

    private void SwitchSidePanel(string panelName)
    {
        _currentSidePanel = panelName;
        
        // Get all panels
        var explorerPanel = this.FindControl<Grid>("ExplorerPanel");
        var searchPanel = this.FindControl<Grid>("SearchPanel");
        var gitPanel = this.FindControl<Border>("GitSidePanel");
        var debugPanel = this.FindControl<Grid>("DebugSidePanel");
        var extensionsPanel = this.FindControl<Grid>("ExtensionsPanel");
        
        // Get all sidebar buttons
        var explorerButton = this.FindControl<Button>("ExplorerButton");
        var searchButton = this.FindControl<Button>("SearchButton");
        var gitButton = this.FindControl<Button>("GitButton");
        var debugButton = this.FindControl<Button>("DebugButton");
        var extensionsButton = this.FindControl<Button>("ExtensionsButton");
        
        // Hide all panels
        if (explorerPanel != null) explorerPanel.IsVisible = false;
        if (searchPanel != null) searchPanel.IsVisible = false;
        if (gitPanel != null) gitPanel.IsVisible = false;
        if (debugPanel != null) debugPanel.IsVisible = false;
        if (extensionsPanel != null) extensionsPanel.IsVisible = false;
        
        // Remove active class from all buttons
        explorerButton?.Classes.Remove("active");
        searchButton?.Classes.Remove("active");
        gitButton?.Classes.Remove("active");
        debugButton?.Classes.Remove("active");
        extensionsButton?.Classes.Remove("active");
        
        // Show selected panel and activate button
        switch (panelName)
        {
            case "explorer":
                if (explorerPanel != null) explorerPanel.IsVisible = true;
                explorerButton?.Classes.Add("active");
                break;
            case "search":
                if (searchPanel != null) searchPanel.IsVisible = true;
                searchButton?.Classes.Add("active");
                // Focus search input
                var searchInput = this.FindControl<TextBox>("SearchInputBox");
                searchInput?.Focus();
                break;
            case "git":
                if (gitPanel != null) gitPanel.IsVisible = true;
                gitButton?.Classes.Add("active");
                break;
            case "debug":
                if (debugPanel != null) debugPanel.IsVisible = true;
                debugButton?.Classes.Add("active");
                break;
            case "extensions":
                if (extensionsPanel != null) extensionsPanel.IsVisible = true;
                extensionsButton?.Classes.Add("active");
                break;
        }
    }

    private void InitializeGitPanel()
    {
        var gitPanelContainer = this.FindControl<Border>("GitSidePanel");
        if (gitPanelContainer == null) return;
        
        _gitPanelControl = new GitPanelControl();
        
        // Subscribe to events
        _gitPanelControl.FileOpenRequested += (s, filePath) =>
        {
            OpenFileInEditor(filePath);
        };
        
        _gitPanelControl.FileDiffRequested += (s, filePath) =>
        {
            // TODO: Show diff view
            OpenFileInEditor(filePath);
        };
        
        _gitPanelControl.CloneRepositoryRequested += async (s, e) =>
        {
            var cloneWindow = new CloneRepositoryWindow();
            var result = await cloneWindow.ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(result))
            {
                _projectPath = result;
                LoadProject(result);
                UpdateTitle();
                // Ensure we pass directory path
                string dirPath = File.Exists(result) ? Path.GetDirectoryName(result) ?? result : result;
                await _gitPanelControl.SetRepositoryPathAsync(dirPath);
            }
        };
        
        _gitPanelControl.StatusChanged += (s, status) =>
        {
            _viewModel.StatusText = status;
        };
        
        // Add to container
        gitPanelContainer.Child = _gitPanelControl;
        
        // Set repository path
        if (!string.IsNullOrEmpty(_projectPath))
        {
            // Ensure we pass directory path
            string dirPath = File.Exists(_projectPath) ? Path.GetDirectoryName(_projectPath) ?? _projectPath : _projectPath;
            _ = _gitPanelControl.SetRepositoryPathAsync(dirPath);
        }
    }

    private void StartDebug_Click(object? sender, RoutedEventArgs e)
    {
        // Start debugging
        _ = RunProjectAsync();
    }

    #endregion

    #region File Tree

    private void FileTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is FileTreeItem item)
        {
            if (!item.IsDirectory)
            {
                // Open file in editor
                OpenFileInEditor(item.FullPath);
            }
        }
    }

    private void OpenFileInEditor(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            // Open in view model (manages tabs)
            _viewModel.OpenFile(filePath);

            // Load content in Monaco editor
            if (_monacoEditor != null && _viewModel.ActiveTab != null)
            {
                var content = _viewModel.ActiveTab.Content;
                var language = _viewModel.ActiveTab.Language;
                _monacoEditor.SetContent(content, language);
                _viewModel.StatusText = $"Opened: {Path.GetFileName(filePath)}";
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error opening file: {ex.Message}";
        }
    }

    private FileTreeItem? GetSelectedTreeItem()
    {
        var treeView = this.FindControl<TreeView>("FileTreeView");
        return treeView?.SelectedItem as FileTreeItem;
    }

    private string GetTargetDirectory()
    {
        var selectedItem = GetSelectedTreeItem();
        if (selectedItem != null)
        {
            string targetPath;
            if (selectedItem.IsDirectory)
            {
                targetPath = selectedItem.FullPath;
            }
            else
            {
                targetPath = Path.GetDirectoryName(selectedItem.FullPath) ?? _projectPath ?? Environment.CurrentDirectory;
            }
            
            // Verify the directory exists
            if (Directory.Exists(targetPath))
            {
                return targetPath;
            }
        }
        
        // Fallback to _projectPath if it exists
        if (!string.IsNullOrEmpty(_projectPath) && Directory.Exists(_projectPath))
        {
            return _projectPath;
        }
        
        // Final fallback to current directory
        return Environment.CurrentDirectory;
    }

    #endregion

    #region File Tree Context Menu Handlers

    private void ExplorerNewFile_Click(object? sender, RoutedEventArgs e)
    {
        AddNewItem_Click(sender, e);
    }

    private void ExplorerNewFolder_Click(object? sender, RoutedEventArgs e)
    {
        AddNewFolder_Click(sender, e);
    }

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
        var selectedItem = GetSelectedTreeItem();
        if (selectedItem == null) return;

        var confirmDelete = await ShowConfirmDialogAsync(
            "Confirm Delete",
            $"Are you sure you want to delete '{selectedItem.Name}'?" + 
            (selectedItem.IsDirectory ? "\n\nThis will delete all contents." : ""));

        if (confirmDelete)
        {
            try
            {
                if (selectedItem.IsDirectory)
                {
                    Directory.Delete(selectedItem.FullPath, true);
                }
                else
                {
                    File.Delete(selectedItem.FullPath);
                    
                    // Close tab if file is open
                    var tab = _viewModel.FindTabByPath(selectedItem.FullPath);
                    if (tab != null)
                    {
                        _viewModel.CloseTab(tab);
                    }
                }
                
                RefreshFileTree();
                _viewModel.StatusText = $"Deleted: {selectedItem.Name}";
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Error deleting: {ex.Message}";
            }
        }
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

    private string? FindSolutionFile()
    {
        if (string.IsNullOrEmpty(_projectPath)) return null;
        
        // Ensure the path exists
        if (!Directory.Exists(_projectPath))
        {
            // Try parent directory if _projectPath is a file
            if (File.Exists(_projectPath))
            {
                var parentDir = Path.GetDirectoryName(_projectPath);
                if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
                    return null;
            }
            else
            {
                return null;
            }
        }

        try
        {
            var dir = new DirectoryInfo(_projectPath);
            while (dir != null && dir.Exists)
            {
                // Look for .slnx files first (new format)
                var slnxFiles = dir.GetFiles("*.slnx");
                if (slnxFiles.Length > 0)
                {
                    return slnxFiles[0].FullName;
                }
                
                // Then look for .sln files (legacy format)
                var slnFiles = dir.GetFiles("*.sln");
                if (slnFiles.Length > 0)
                {
                    return slnFiles[0].FullName;
                }
                dir = dir.Parent;
            }
        }
        catch (Exception)
        {
            // Ignore any IO errors
        }
        
        return null;
    }

    private async Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            Background = new SolidColorBrush(Color.Parse("#FF1E1E2E"))
        };

        string? result = null;
        
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("36,*,Auto")
        };

        // Title bar
        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FF3C3C3C")),
            CornerRadius = new CornerRadius(8, 8, 0, 0)
        };
        Grid.SetRow(titleBar, 0);

        var titleGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleText = new TextBlock
        {
            Text = $"📝 {title}",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(12, 0)
        };
        titleGrid.Children.Add(titleText);
        titleBar.Child = titleGrid;
        titleBar.PointerPressed += (s, e) => { if (e.GetCurrentPoint(dialog).Properties.IsLeftButtonPressed) dialog.BeginMoveDrag(e); };

        // Content
        var contentStack = new StackPanel
        {
            Margin = new Thickness(20, 16),
            Spacing = 12
        };
        Grid.SetRow(contentStack, 1);

        contentStack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            FontSize = 13
        });

        var inputBox = new TextBox
        {
            Text = defaultValue,
            Background = new SolidColorBrush(Color.Parse("#FF363647")),
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10)
        };
        contentStack.Children.Add(inputBox);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(20, 0, 20, 16)
        };
        Grid.SetRow(buttonPanel, 2);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Background = new SolidColorBrush(Color.Parse("#FFFAB387")),
            Foreground = new SolidColorBrush(Color.Parse("#FF1E1E2E")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 8)
        };
        okButton.Click += (s, e) => { result = inputBox.Text; dialog.Close(); };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 8)
        };
        cancelButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        rootGrid.Children.Add(titleBar);
        rootGrid.Children.Add(contentStack);
        rootGrid.Children.Add(buttonPanel);

        var outerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = rootGrid
        };

        dialog.Content = outerBorder;
        
        // Focus input and select all
        dialog.Opened += (s, e) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            Background = new SolidColorBrush(Color.Parse("#FF1E1E2E"))
        };

        bool result = false;
        
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("36,*,Auto")
        };

        // Title bar
        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FF3C3C3C")),
            CornerRadius = new CornerRadius(8, 8, 0, 0)
        };
        Grid.SetRow(titleBar, 0);

        var titleText = new TextBlock
        {
            Text = $"⚠️ {title}",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(12, 0)
        };
        titleBar.Child = titleText;
        titleBar.PointerPressed += (s, e) => { if (e.GetCurrentPoint(dialog).Properties.IsLeftButtonPressed) dialog.BeginMoveDrag(e); };

        // Content
        var messageText = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(20, 20)
        };
        Grid.SetRow(messageText, 1);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(20, 0, 20, 16)
        };
        Grid.SetRow(buttonPanel, 2);

        var yesButton = new Button
        {
            Content = "Yes",
            Width = 80,
            Background = new SolidColorBrush(Color.Parse("#FFF38BA8")),
            Foreground = new SolidColorBrush(Color.Parse("#FF1E1E2E")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 8)
        };
        yesButton.Click += (s, e) => { result = true; dialog.Close(); };

        var noButton = new Button
        {
            Content = "No",
            Width = 80,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 8)
        };
        noButton.Click += (s, e) => dialog.Close();

        buttonPanel.Children.Add(noButton);
        buttonPanel.Children.Add(yesButton);

        rootGrid.Children.Add(titleBar);
        rootGrid.Children.Add(messageText);
        rootGrid.Children.Add(buttonPanel);

        var outerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = rootGrid
        };

        dialog.Content = outerBorder;
        await dialog.ShowDialog(this);
        return result;
    }

    #endregion

    #region Solution and Project Management

    /// <summary>
    /// Open an existing solution file
    /// </summary>
    public async Task OpenSolutionAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Solution",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Solution Files") { Patterns = new[] { "*.slnx", "*.sln" } },
                new("Project Files") { Patterns = new[] { "*.csproj", "*.fsproj", "*.vbproj" } },
                new("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            var filePath = file.Path.LocalPath;
            var directory = Path.GetDirectoryName(filePath);
            
            if (!string.IsNullOrEmpty(directory))
            {
                _projectPath = directory;
                LoadProject(filePath);
                UpdateTitle();
                _viewModel.StatusText = $"Opened: {Path.GetFileName(filePath)}";
            }
        }
    }

    /// <summary>
    /// Create a new solution
    /// </summary>
    public async Task CreateNewSolutionAsync()
    {
        var newSolutionWindow = new NewSolutionWindow();
        var result = await newSolutionWindow.ShowDialog<string?>(this);
        
        if (!string.IsNullOrEmpty(result))
        {
            var solutionDir = Path.GetDirectoryName(result);
            if (!string.IsNullOrEmpty(solutionDir))
            {
                // Wait a bit for the file system to update
                await Task.Delay(100);
                
                // Verify the solution file was created
                if (File.Exists(result))
                {
                    _projectPath = solutionDir;
                    _viewModel.LoadProjectFolder(solutionDir);
                    UpdateTitle();
                    _viewModel.StatusText = $"Created solution: {Path.GetFileName(result)}";
                }
                else
                {
                    // Try to load directory anyway
                    if (Directory.Exists(solutionDir))
                    {
                        _projectPath = solutionDir;
                        _viewModel.LoadProjectFolder(solutionDir);
                        UpdateTitle();
                        _viewModel.StatusText = $"Created solution directory: {Path.GetFileName(solutionDir)}";
                    }
                    else
                    {
                        _viewModel.StatusText = $"Error: Solution file was not created at {result}";
                    }
                }
            }
        }
    }

    /// <summary>
    /// Add a new project to the current solution
    /// </summary>
    public async Task AddNewProjectToSolutionAsync()
    {
        var solutionPath = FindSolutionFile();
        if (string.IsNullOrEmpty(solutionPath))
        {
            _viewModel.StatusText = "No solution file found. Create a solution first.";
            
            // Offer to create a solution
            var createSolution = await ShowConfirmDialogAsync(
                "No Solution Found",
                "Would you like to create a new solution?");
            
            if (createSolution)
            {
                await CreateNewSolutionAsync();
            }
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

    #endregion

    #region Tabs

    private void TabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is EditorTab tab)
        {
            _viewModel.ActiveTab = tab;
            
            // Load content in Monaco editor
            if (_monacoEditor != null)
            {
                _monacoEditor.SetContent(tab.Content, tab.Language);
                _viewModel.StatusText = $"Switched to: {tab.FileName}";
            }
        }
    }

    private async void CloseTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is EditorTab tab)
        {
            // Check if the tab has unsaved changes
            if (tab.IsDirty)
            {
                var result = await ShowSaveConfirmationDialogAsync(tab.FileName);
                
                if (result == SaveConfirmationResult.Save)
                {
                    // Save before closing
                    if (_viewModel.ActiveTab == tab)
                    {
                        await SaveCurrentFileAsync();
                    }
                    else
                    {
                        // Save this specific tab
                        if (!string.IsNullOrEmpty(tab.FilePath))
                        {
                            try
                            {
                                await File.WriteAllTextAsync(tab.FilePath, tab.Content);
                                tab.IsDirty = false;
                            }
                            catch (Exception ex)
                            {
                                _viewModel.StatusText = $"Error saving {tab.FileName}: {ex.Message}";
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
                else if (result == SaveConfirmationResult.Cancel)
                {
                    // Don't close the tab
                    e.Handled = true;
                    return;
                }
                // If result is DontSave, continue closing without saving
            }
            
            _viewModel.CloseTab(tab);
            
            // Update editor with active tab content
            if (_monacoEditor != null && _viewModel.ActiveTab != null)
            {
                _monacoEditor.SetContent(_viewModel.ActiveTab.Content, _viewModel.ActiveTab.Language);
            }
            else if (_monacoEditor != null)
            {
                _monacoEditor.SetContent("", "plaintext");
            }
        }
        
        // Stop event propagation to prevent TabButton_Click from firing
        e.Handled = true;
    }

    private enum SaveConfirmationResult
    {
        Save,
        DontSave,
        Cancel
    }

    private async Task<SaveConfirmationResult> ShowSaveConfirmationDialogAsync(string fileName)
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
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
            Text = $"Do you want to save changes to '{fileName}'?",
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

        var saveButton = new Button { Content = "Save", Width = 80 };
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

    #region Terminal

    private void InitializeTerminal()
    {
        var container = this.FindControl<Border>("TerminalContainer");
        if (container != null)
        {
            _terminalControl = new TerminalControl();
            
            // Set working directory to project path
            string? workingDir = null;
            
            // First try ViewModel's CurrentProjectPath
            if (!string.IsNullOrEmpty(_viewModel.CurrentProjectPath) && Directory.Exists(_viewModel.CurrentProjectPath))
            {
                workingDir = _viewModel.CurrentProjectPath;
            }
            // Then try _projectPath
            else if (!string.IsNullOrEmpty(_projectPath))
            {
                if (File.Exists(_projectPath))
                {
                    workingDir = Path.GetDirectoryName(_projectPath);
                }
                else if (Directory.Exists(_projectPath))
                {
                    workingDir = _projectPath;
                }
            }
            
            // Set working directory if found
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
            {
                _terminalControl.WorkingDirectory = workingDir;
            }
            
            // Subscribe to terminal events
            _terminalControl.ProcessStarted += OnTerminalProcessStarted;
            _terminalControl.ProcessExited += OnTerminalProcessExited;
            _terminalControl.OutputReceived += OnTerminalOutputReceived;
            
            container.Child = _terminalControl;
            
            // Start interactive shell automatically
            _ = _terminalControl.StartInteractiveShellAsync();
        }
    }
    
    private void OnTerminalProcessStarted(object? sender, EventArgs e)
    {
        _viewModel.StatusText = "Process running...";
    }
    
    private void OnTerminalProcessExited(object? sender, EventArgs e)
    {
        _viewModel.StatusText = "Ready";
    }
    
    private void OnTerminalOutputReceived(object? sender, TerminalOutputEventArgs e)
    {
        // Could be used to update status or log output
    }
    
    private void ProblemsTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("problems");
    }
    
    private void BuildTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("build");
    }
    
    private void RunTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("run");
    }
    
    private void OutputTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("build");
    }
    
    private void TerminalTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("terminal");
        _terminalControl?.FocusInput();
    }
    
    private void GitTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("git");
    }
    
    private void DebugConsoleTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchToolWindowPanel("debug");
    }
    
    private void SwitchToolWindowPanel(string panelName)
    {
        // Get all panels
        var terminalContainer = this.FindControl<Border>("TerminalContainer");
        var problemsPanel = this.FindControl<Border>("ProblemsPanel");
        var buildPanel = this.FindControl<Border>("BuildPanel");
        var runPanel = this.FindControl<Border>("RunPanel");
        var gitPanel = this.FindControl<Border>("GitPanel");
        var debugPanel = this.FindControl<Border>("DebugPanel");
        
        // Get all tab buttons
        var problemsTab = this.FindControl<Button>("ProblemsTabButton");
        var buildTab = this.FindControl<Button>("BuildTabButton");
        var runTab = this.FindControl<Button>("RunTabButton");
        var terminalTab = this.FindControl<Button>("TerminalTabButton");
        var gitTab = this.FindControl<Button>("GitTabButton");
        var debugTab = this.FindControl<Button>("DebugConsoleTabButton");
        
        // Hide all panels
        if (terminalContainer != null) terminalContainer.IsVisible = false;
        if (problemsPanel != null) problemsPanel.IsVisible = false;
        if (buildPanel != null) buildPanel.IsVisible = false;
        if (runPanel != null) runPanel.IsVisible = false;
        if (gitPanel != null) gitPanel.IsVisible = false;
        if (debugPanel != null) debugPanel.IsVisible = false;
        
        // Remove active class from all tabs
        RemoveActiveClass(problemsTab);
        RemoveActiveClass(buildTab);
        RemoveActiveClass(runTab);
        RemoveActiveClass(terminalTab);
        RemoveActiveClass(gitTab);
        RemoveActiveClass(debugTab);
        
        // Show selected panel and activate tab
        switch (panelName)
        {
            case "problems":
                if (problemsPanel != null) problemsPanel.IsVisible = true;
                AddActiveClass(problemsTab);
                break;
            case "build":
                if (buildPanel != null) buildPanel.IsVisible = true;
                AddActiveClass(buildTab);
                break;
            case "run":
                if (runPanel != null) runPanel.IsVisible = true;
                AddActiveClass(runTab);
                break;
            case "terminal":
                if (terminalContainer != null) terminalContainer.IsVisible = true;
                AddActiveClass(terminalTab);
                break;
            case "git":
                if (gitPanel != null) gitPanel.IsVisible = true;
                AddActiveClass(gitTab);
                break;
            case "debug":
                if (debugPanel != null) debugPanel.IsVisible = true;
                AddActiveClass(debugTab);
                break;
        }
    }
    
    private void AddActiveClass(Button? button)
    {
        if (button != null && !button.Classes.Contains("active"))
        {
            button.Classes.Add("active");
        }
    }
    
    private void RemoveActiveClass(Button? button)
    {
        if (button != null && button.Classes.Contains("active"))
        {
            button.Classes.Remove("active");
        }
    }
    
    private void NewTerminal_Click(object? sender, RoutedEventArgs e)
    {
        // Start a new interactive shell session
        _ = _terminalControl?.StartInteractiveShellAsync();
    }
    
    private void AdminTerminal_Click(object? sender, RoutedEventArgs e)
    {
        // Start administrator shell
        _terminalControl?.StartAdministratorShell();
    }
    
    private void ClearTerminal_Click(object? sender, RoutedEventArgs e)
    {
        // Clear terminal output
        _terminalControl?.ClearOutput();
    }
    
    private double _previousBottomPanelHeight = 200;
    private bool _isBottomPanelMaximized = false;
    
    private void MinimizePanel_Click(object? sender, RoutedEventArgs e)
    {
        // Minimize the bottom panel to just the tab bar
        var editorGrid = this.FindControl<Grid>("EditorGrid");
        if (editorGrid == null)
        {
            // Try to find it by traversing the visual tree
            var monacoContainer = this.FindControl<Border>("MonacoEditorContainer");
            if (monacoContainer?.Parent is Grid grid)
            {
                editorGrid = grid;
            }
        }
        
        if (editorGrid != null && editorGrid.RowDefinitions.Count > 3)
        {
            var currentHeight = editorGrid.RowDefinitions[3].Height.Value;
            if (currentHeight > 32)
            {
                _previousBottomPanelHeight = currentHeight;
                editorGrid.RowDefinitions[3].Height = new GridLength(32);
            }
            else
            {
                editorGrid.RowDefinitions[3].Height = new GridLength(_previousBottomPanelHeight);
            }
            _isBottomPanelMaximized = false;
        }
    }
    
    private void MaximizePanel_Click(object? sender, RoutedEventArgs e)
    {
        // Maximize the bottom panel
        var editorGrid = this.FindControl<Grid>("EditorGrid");
        if (editorGrid == null)
        {
            var monacoContainer = this.FindControl<Border>("MonacoEditorContainer");
            if (monacoContainer?.Parent is Grid grid)
            {
                editorGrid = grid;
            }
        }
        
        if (editorGrid != null && editorGrid.RowDefinitions.Count > 3)
        {
            if (!_isBottomPanelMaximized)
            {
                _previousBottomPanelHeight = editorGrid.RowDefinitions[3].Height.Value;
                // Make panel take most of the space (leave some for tabs and minimal editor)
                editorGrid.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
                editorGrid.RowDefinitions[1].Height = new GridLength(100);
                _isBottomPanelMaximized = true;
            }
            else
            {
                editorGrid.RowDefinitions[3].Height = new GridLength(_previousBottomPanelHeight);
                editorGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                _isBottomPanelMaximized = false;
            }
        }
    }
    
    private void HidePanel_Click(object? sender, RoutedEventArgs e)
    {
        // Hide the bottom panel completely
        var editorGrid = this.FindControl<Grid>("EditorGrid");
        if (editorGrid == null)
        {
            var monacoContainer = this.FindControl<Border>("MonacoEditorContainer");
            if (monacoContainer?.Parent is Grid grid)
            {
                editorGrid = grid;
            }
        }
        
        if (editorGrid != null && editorGrid.RowDefinitions.Count > 3)
        {
            var currentHeight = editorGrid.RowDefinitions[3].Height.Value;
            if (currentHeight > 0)
            {
                _previousBottomPanelHeight = currentHeight > 32 ? currentHeight : _previousBottomPanelHeight;
                editorGrid.RowDefinitions[3].Height = new GridLength(0);
                editorGrid.RowDefinitions[2].Height = new GridLength(0); // Hide splitter too
            }
            else
            {
                editorGrid.RowDefinitions[3].Height = new GridLength(_previousBottomPanelHeight);
                editorGrid.RowDefinitions[2].Height = new GridLength(5);
            }
            _isBottomPanelMaximized = false;
        }
    }
    
    private void StatusProblems_Click(object? sender, RoutedEventArgs e)
    {
        // Show problems panel when clicking on status bar problems
        SwitchToolWindowPanel("problems");
        
        // Make sure panel is visible
        var editorGrid = this.FindControl<Border>("MonacoEditorContainer")?.Parent as Grid;
        if (editorGrid != null && editorGrid.RowDefinitions.Count > 3)
        {
            if (editorGrid.RowDefinitions[3].Height.Value < 32)
            {
                editorGrid.RowDefinitions[3].Height = new GridLength(_previousBottomPanelHeight > 32 ? _previousBottomPanelHeight : 200);
                editorGrid.RowDefinitions[2].Height = new GridLength(5);
            }
        }
    }
    
    /// <summary>
    /// Update cursor position display in status bar
    /// </summary>
    private void UpdateCursorPositionDisplay(int line, int column)
    {
        var cursorText = this.FindControl<TextBlock>("CursorPositionText");
        if (cursorText != null)
        {
            cursorText.Text = $"Ln {line}, Col {column}";
        }
    }
    
    /// <summary>
    /// Update language mode display in status bar
    /// </summary>
    private void UpdateLanguageModeDisplay(string language)
    {
        var langText = this.FindControl<TextBlock>("LanguageModeText");
        if (langText != null)
        {
            langText.Text = language switch
            {
                "csharp" => "C#",
                "javascript" => "JavaScript",
                "typescript" => "TypeScript",
                "html" => "HTML",
                "css" => "CSS",
                "json" => "JSON",
                "xml" => "XML",
                "markdown" => "Markdown",
                _ => language
            };
        }
    }
    
    /// <summary>
    /// Execute a command in the terminal programmatically
    /// </summary>
    public void ExecuteTerminalCommand(string command)
    {
        _terminalControl?.ExecuteCommand(command);
    }
    
    /// <summary>
    /// Run a command with administrator privileges
    /// </summary>
    public async Task<bool> RunAsAdministratorAsync(string command)
    {
        if (_terminalControl != null)
        {
            return await _terminalControl.RunAsAdministratorAsync(command);
        }
        return false;
    }

    #endregion

    #region AI Assistant

    private bool _isAIPanelVisible = true;

    private void CloseAIPanel_Click(object? sender, RoutedEventArgs e)
    {
        ToggleAIPanel(false);
    }

    /// <summary>
    /// Toggle the AI Assistant panel visibility
    /// </summary>
    public void ToggleAIPanel(bool? show = null)
    {
        var panel = this.FindControl<Border>("AIPanelBorder");
        if (panel != null)
        {
            _isAIPanelVisible = show ?? !_isAIPanelVisible;
            panel.IsVisible = _isAIPanelVisible;
            
            // Update the column width
            var mainGrid = panel.Parent as Grid;
            if (mainGrid != null && mainGrid.ColumnDefinitions.Count > 3)
            {
                mainGrid.ColumnDefinitions[3].Width = _isAIPanelVisible 
                    ? new GridLength(300) 
                    : new GridLength(0);
            }
        }
    }

    private void SendAI_Click(object? sender, RoutedEventArgs e)
    {
        var input = this.FindControl<TextBox>("AIChatInput");
        if (input != null && !string.IsNullOrWhiteSpace(input.Text))
        {
            var message = input.Text;
            input.Text = string.Empty;
            
            // Process AI message
            ProcessAIMessage(message);
        }
    }

    private async void ProcessAIMessage(string message)
    {
        // Add user message to chat
        AddUserMessage(message);
        
        // Update working directory for Copilot CLI
        if (!string.IsNullOrEmpty(_projectPath))
        {
            var workingDir = Directory.Exists(_projectPath) ? _projectPath : Path.GetDirectoryName(_projectPath);
            if (!string.IsNullOrEmpty(workingDir))
            {
                _copilotCliService.WorkingDirectory = workingDir;
            }
        }
        
        // Check if it's a CLI command (starts with / or common commands)
        var trimmedMessage = message.Trim();
        var isCliCommand = trimmedMessage.StartsWith("/") || 
                          IsCliCommandKeyword(trimmedMessage.Split(' ')[0].ToLowerInvariant());
        
        if (isCliCommand)
        {
            // Remove leading / if present
            var command = trimmedMessage.StartsWith("/") ? trimmedMessage.Substring(1) : trimmedMessage;
            
            // Execute CLI command
            var result = await _copilotCliService.ExecuteAsync(command);
            
            // Add response to chat
            if (result.Success)
            {
                AddAIResponse(result.Output, isSuccess: true);
                
                // Refresh file tree if file was created/deleted/renamed
                var cmdLower = command.Split(' ')[0].ToLowerInvariant();
                if (cmdLower is "create" or "new" or "delete" or "rm" or "remove" or "mkdir" or "rmdir" 
                    or "rename" or "mv" or "copy" or "cp" or "touch" or "template")
                {
                    RefreshFileTree();
                }
            }
            else
            {
                AddAIResponse(result.Output, isSuccess: false);
            }
        }
        else
        {
            // Regular AI chat - show hint about CLI commands
            var response = GetAIResponse(message);
            AddAIResponse(response, isSuccess: true);
        }
    }
    
    private bool IsCliCommandKeyword(string word)
    {
        var cliCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "create", "new", "delete", "rm", "remove", "mkdir", "rmdir",
            "rename", "mv", "copy", "cp", "touch", "write", "append",
            "read", "cat", "ls", "dir", "tree", "pwd", "cd",
            "find", "search", "template", "help", "info", "exists",
            // GitHub CLI commands
            "gh", "gh-install", "gh-auth", "gh-repo", "gh-pr", "gh-issue", "gh-workflow", "gh-status"
        };
        return cliCommands.Contains(word);
    }
    
    private void AddUserMessage(string message)
    {
        var chatPanel = this.FindControl<StackPanel>("AIChatMessages");
        if (chatPanel == null) return;
        
        var messageBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#50569CD6")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            MaxWidth = 280
        };
        
        var messageText = new SelectableTextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Monaco, monospace"),
            FontSize = 12
        };
        
        messageBorder.Child = messageText;
        chatPanel.Children.Add(messageBorder);
        
        // Scroll to bottom
        ScrollAIChatToBottom();
    }
    
    private void AddAIResponse(string response, bool isSuccess)
    {
        var chatPanel = this.FindControl<StackPanel>("AIChatMessages");
        if (chatPanel == null) return;
        
        var responseBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse(isSuccess ? "#403D3D4D" : "#40F38BA8")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            MaxWidth = 300
        };
        
        var responsePanel = new StackPanel { Spacing = 4 };
        
        // Header with icon
        var headerPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        headerPanel.Children.Add(new TextBlock 
        { 
            Text = isSuccess ? "🤖" : "⚠️", 
            FontSize = 12 
        });
        headerPanel.Children.Add(new TextBlock 
        { 
            Text = isSuccess ? "Copilot" : "Error", 
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse(isSuccess ? "#A6E3A1" : "#F38BA8"))
        });
        responsePanel.Children.Add(headerPanel);
        
        // Response content - SelectableTextBlock for copying
        var responseText = new SelectableTextBlock
        {
            Text = response,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Monaco, monospace"),
            FontSize = 11
        };
        responsePanel.Children.Add(responseText);
        
        responseBorder.Child = responsePanel;
        chatPanel.Children.Add(responseBorder);
        
        // Scroll to bottom
        ScrollAIChatToBottom();
    }
    
    private void ScrollAIChatToBottom()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("AIChatScrollViewer");
        scrollViewer?.ScrollToEnd();
    }
    
    private string GetAIResponse(string message)
    {
        // Simple responses for common queries - this is a placeholder for real AI integration
        var lowerMessage = message.ToLowerInvariant();
        
        if (lowerMessage.Contains("help") || lowerMessage.Contains("command"))
        {
            return "💡 Copilot CLI Commands:\n\n" +
                   "📁 File Operations:\n" +
                   "  create <path> - Create file\n" +
                   "  delete <path> - Delete file\n" +
                   "  template <type> <path> - Create from template\n\n" +
                   "📂 Navigation:\n" +
                   "  ls, dir - List files\n" +
                   "  tree - Show tree view\n" +
                   "  cd <path> - Change directory\n\n" +
                   "🔍 Search:\n" +
                   "  find <pattern> - Find files\n" +
                   "  search <text> - Search in files\n\n" +
                   "Type 'help' for full command list.";
        }
        
        if (lowerMessage.Contains("create") || lowerMessage.Contains("new file"))
        {
            return "To create a file, use:\n  create <filename>\n\nExample:\n  create MyClass.cs\n  create src/Utils/Helper.cs";
        }
        
        if (lowerMessage.Contains("delete") || lowerMessage.Contains("remove"))
        {
            return "To delete a file, use:\n  delete <filename>\n\nUse --force for directories:\n  delete src/OldFolder --force";
        }
        
        return "I'm your Copilot CLI assistant! 🤖\n\n" +
               "You can use CLI commands directly:\n" +
               "  /create MyClass.cs\n" +
               "  /ls\n" +
               "  /help\n\n" +
               "Or type 'help' for all available commands.";
    }
    
    private void ClearAIChat_Click(object? sender, RoutedEventArgs e)
    {
        var chatPanel = this.FindControl<StackPanel>("AIChatMessages");
        if (chatPanel == null) return;
        
        chatPanel.Children.Clear();
        
        // Add welcome message back
        var welcomeBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#40FAB387")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        
        var welcomePanel = new StackPanel { Spacing = 8 };
        welcomePanel.Children.Add(new TextBlock 
        { 
            Text = "🚀 Copilot CLI Ready!", 
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"))
        });
        welcomePanel.Children.Add(new TextBlock 
        { 
            Text = "Type 'help' for available commands",
            Foreground = new SolidColorBrush(Color.Parse("#E0E0E0")),
            FontSize = 12
        });
        
        welcomeBorder.Child = welcomePanel;
        chatPanel.Children.Add(welcomeBorder);
        
        _viewModel.StatusText = "Chat cleared";
    }
    
    private void AIChatInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SendAI_Click(sender, e);
            e.Handled = true;
        }
    }

    #endregion

    #region File Operations

    public async Task<string?> OpenFileAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

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
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        return null;
    }

    public async Task SaveFileAsync(string content, string? suggestedFileName = null)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            SuggestedFileName = suggestedFileName ?? "Untitled.cs",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("C# Files") { Patterns = new[] { "*.cs" } },
                new("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
        }
    }

    /// <summary>
    /// Save the currently active file
    /// </summary>
    public async Task SaveCurrentFileAsync()
    {
        if (_viewModel.ActiveTab == null)
        {
            _viewModel.StatusText = "No file to save";
            return;
        }

        // Sync content from Monaco editor if available
        if (_monacoEditor != null)
        {
            var content = await _monacoEditor.GetContentAsync();
            if (!string.IsNullOrEmpty(content))
            {
                _viewModel.ActiveTab.Content = content;
            }
        }

        if (string.IsNullOrEmpty(_viewModel.ActiveTab.FilePath))
        {
            // New file - use Save As
            await SaveCurrentFileAsAsync();
            return;
        }

        try
        {
            await File.WriteAllTextAsync(_viewModel.ActiveTab.FilePath, _viewModel.ActiveTab.Content);
            _viewModel.ActiveTab.IsDirty = false;
            _viewModel.ActiveTab.LastModified = DateTime.Now;
            _monacoEditor?.MarkAsSaved();
            _viewModel.StatusText = $"Saved: {_viewModel.ActiveTab.FileName}";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error saving file: {ex.Message}";
        }
    }

    /// <summary>
    /// Save the current file with a new name
    /// </summary>
    public async Task SaveCurrentFileAsAsync()
    {
        if (_viewModel.ActiveTab == null) return;

        // Sync content from Monaco editor if available
        if (_monacoEditor != null)
        {
            var content = await _monacoEditor.GetContentAsync();
            if (!string.IsNullOrEmpty(content))
            {
                _viewModel.ActiveTab.Content = content;
            }
        }

        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File As",
            SuggestedFileName = _viewModel.ActiveTab.FileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("C# Files") { Patterns = new[] { "*.cs" } },
                new("XAML Files") { Patterns = new[] { "*.axaml", "*.xaml" } },
                new("JSON Files") { Patterns = new[] { "*.json" } },
                new("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (file != null)
        {
            try
            {
                var filePath = file.Path.LocalPath;
                await File.WriteAllTextAsync(filePath, _viewModel.ActiveTab.Content);
                
                // Update tab info
                _viewModel.ActiveTab.FilePath = filePath;
                _viewModel.ActiveTab.FileName = Path.GetFileName(filePath);
                _viewModel.ActiveTab.Language = EditorTab.GetLanguageFromExtension(filePath);
                _viewModel.ActiveTab.IsDirty = false;
                _viewModel.ActiveTab.LastModified = DateTime.Now;
                _monacoEditor?.MarkAsSaved();
                _viewModel.StatusText = $"Saved: {_viewModel.ActiveTab.FileName}";
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Error saving file: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Save all open files with changes
    /// </summary>
    public async Task SaveAllFilesAsync()
    {
        // First sync the active tab's content
        if (_monacoEditor != null && _viewModel.ActiveTab != null)
        {
            var content = await _monacoEditor.GetContentAsync();
            if (!string.IsNullOrEmpty(content))
            {
                _viewModel.ActiveTab.Content = content;
            }
        }

        int savedCount = 0;
        foreach (var tab in _viewModel.Tabs)
        {
            if (tab.IsDirty && !string.IsNullOrEmpty(tab.FilePath))
            {
                try
                {
                    await File.WriteAllTextAsync(tab.FilePath, tab.Content);
                    tab.IsDirty = false;
                    tab.LastModified = DateTime.Now;
                    savedCount++;
                }
                catch (Exception ex)
                {
                    _viewModel.StatusText = $"Error saving {tab.FileName}: {ex.Message}";
                }
            }
        }

        if (_viewModel.ActiveTab != null)
        {
            _monacoEditor?.MarkAsSaved();
        }
        
        _viewModel.StatusText = savedCount > 0 ? $"Saved {savedCount} file(s)" : "No files to save";
    }

    #endregion

    #region Build Operations

    /// <summary>
    /// Initialize build service events
    /// </summary>
    private void InitializeBuildService()
    {
        _buildService.OutputReceived += OnBuildOutputReceived;
        _buildService.BuildStarted += OnBuildStarted;
        _buildService.BuildCompleted += OnBuildCompleted;
    }

    private void OnBuildOutputReceived(object? sender, BuildOutputEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _buildOutput.Append(e.Output);
            UpdateBuildOutput();
        });
    }

    private void OnBuildStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isBuildInProgress = true;
            UpdateBuildButtons();
            _viewModel.StatusText = "Building...";
        });
    }

    private void OnBuildCompleted(object? sender, BuildCompletedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isBuildInProgress = false;
            UpdateBuildButtons();
            
            // Parse build output for errors and warnings
            var buildOutput = _buildOutput.ToString();
            ParseAndShowBuildProblems(buildOutput);
            
            if (e.Result.Success)
            {
                _viewModel.StatusText = "Build succeeded";
            }
            else
            {
                _viewModel.StatusText = $"Build failed: {e.Result.ErrorMessage ?? "Unknown error"}";
                // Switch to problems panel if there are errors
                if (_viewModel.Problems.Count > 0)
                {
                    SwitchToolWindowPanel("problems");
                }
            }
        });
    }

    private void UpdateBuildOutput()
    {
        var buildOutputText = this.FindControl<TextBlock>("BuildOutputText");
        var scrollViewer = this.FindControl<ScrollViewer>("BuildOutputScrollViewer");
        
        if (buildOutputText != null)
        {
            buildOutputText.Text = _buildOutput.ToString();
        }
        
        // Auto-scroll to bottom
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToEnd();
        }
    }

    private void UpdateBuildButtons()
    {
        var buildButton = this.FindControl<Button>("BuildProjectButton");
        var cancelButton = this.FindControl<Button>("CancelBuildButton");
        
        if (buildButton != null)
        {
            buildButton.IsEnabled = !_isBuildInProgress;
        }
        
        if (cancelButton != null)
        {
            cancelButton.IsVisible = _isBuildInProgress;
        }
    }

    /// <summary>
    /// Build button click handler
    /// </summary>
    private async void BuildProject_Click(object? sender, RoutedEventArgs e)
    {
        await BuildProjectAsync();
    }

    /// <summary>
    /// Run project button click handler
    /// </summary>
    private async void RunProject_Click(object? sender, RoutedEventArgs e)
    {
        await RunProjectAsync();
    }

    /// <summary>
    /// Debug project button click handler
    /// </summary>
    private async void DebugProject_Click(object? sender, RoutedEventArgs e)
    {
        // For now, just run without actual debugging
        await RunProjectAsync();
    }

    /// <summary>
    /// Cancel build button click handler
    /// </summary>
    private void CancelBuild_Click(object? sender, RoutedEventArgs e)
    {
        CancelBuild();
        StopRunningProcess();
    }

    /// <summary>
    /// Run configuration dropdown click handler
    /// </summary>
    private async void RunConfigDropdown_Click(object? sender, RoutedEventArgs e)
    {
        await ShowRunConfigurationMenuAsync();
    }

    /// <summary>
    /// Edit configurations button click handler
    /// </summary>
    private async void EditConfigurations_Click(object? sender, RoutedEventArgs e)
    {
        await ShowRunConfigurationsAsync();
    }

    /// <summary>
    /// Publish button click handler
    /// </summary>
    private async void Publish_Click(object? sender, RoutedEventArgs e)
    {
        await ShowPublishWindowAsync();
    }

    /// <summary>
    /// Show run configuration context menu
    /// </summary>
    private async Task ShowRunConfigurationMenuAsync()
    {
        // Ensure configurations are loaded
        if (_runConfigService.Configurations.Count == 0)
        {
            var projectPath = GetCurrentProjectPath();
            if (!string.IsNullOrEmpty(projectPath))
            {
                await _runConfigService.LoadConfigurationsAsync(projectPath);
            }
        }

        // Create context menu
        var menu = new ContextMenu();
        
        foreach (var config in _runConfigService.Configurations)
        {
            var menuItem = new MenuItem
            {
                Header = config.Name,
                Icon = new TextBlock { Text = config == _runConfigService.ActiveConfiguration ? "✓" : " ", FontSize = 12 }
            };
            
            var capturedConfig = config;
            menuItem.Click += async (s, e) =>
            {
                _runConfigService.SetActiveConfiguration(capturedConfig);
                UpdateRunConfigurationDisplay();
            };
            
            menu.Items.Add(menuItem);
        }

        // Add separator and Edit Configurations
        menu.Items.Add(new Separator());
        
        var editItem = new MenuItem { Header = "Edit Configurations..." };
        editItem.Click += async (s, e) => await ShowRunConfigurationsAsync();
        menu.Items.Add(editItem);

        // Show the menu
        var button = this.FindControl<Button>("RunConfigDropdownButton");
        if (button != null)
        {
            menu.Open(button);
        }
    }

    /// <summary>
    /// Update run configuration display in toolbar
    /// </summary>
    private void UpdateRunConfigurationDisplay()
    {
        var configNameText = this.FindControl<TextBlock>("RunConfigNameText");
        if (configNameText != null)
        {
            configNameText.Text = _runConfigService.ActiveConfiguration?.Name ?? "Default";
        }
    }

    /// <summary>
    /// Build the current project
    /// </summary>
    public async Task BuildProjectAsync()
    {
        if (_isBuildInProgress)
        {
            _viewModel.StatusText = "Build already in progress";
            return;
        }

        // Get project path
        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to build";
            return;
        }

        // Clear previous build output
        _buildOutput.Clear();
        UpdateBuildOutput();

        // Switch to Build tab
        SwitchToolWindowPanel("build");

        // Start build
        var result = await _buildService.BuildAsync(projectPath);
        
        // Update status
        if (result.Success)
        {
            _viewModel.StatusText = "Build succeeded";
        }
        else
        {
            _viewModel.StatusText = "Build failed - see Build output for details";
        }
    }

    /// <summary>
    /// Run the current project (build and run with GUI windows appearing)
    /// </summary>
    public async Task RunProjectAsync()
    {
        if (_isBuildInProgress || _runConfigService.IsRunning)
        {
            _viewModel.StatusText = "Build or run already in progress";
            return;
        }

        // Get project path
        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to run";
            return;
        }

        // Check if we have run configurations loaded
        if (_runConfigService.Configurations.Count == 0)
        {
            await _runConfigService.LoadConfigurationsAsync(projectPath);
        }

        // If we have an active configuration, use it
        if (_runConfigService.ActiveConfiguration != null)
        {
            await RunWithConfigurationAsync(_runConfigService.ActiveConfiguration);
        }
        else
        {
            // Fall back to simple build and run
            _buildOutput.Clear();
            UpdateBuildOutput();
            SwitchToolWindowPanel("run");

            var result = await _buildService.BuildAndRunAsync(projectPath);
            
            if (result.Success)
            {
                _viewModel.StatusText = "Project started successfully";
            }
            else
            {
                _viewModel.StatusText = "Build failed - see Build output for details";
            }
        }
    }

    /// <summary>
    /// Run with a specific configuration
    /// </summary>
    private async Task RunWithConfigurationAsync(RunConfiguration config)
    {
        // Clear output
        _buildOutput.Clear();
        UpdateBuildOutput();
        UpdateRunOutput("");
        
        // Switch to Run panel
        SwitchToolWindowPanel("run");

        // Subscribe to output events
        _runConfigService.OutputReceived += OnRunOutputReceived;
        _runConfigService.RunCompleted += OnRunCompleted;
        _runConfigService.RunStarted += OnRunStarted;

        try
        {
            _viewModel.StatusText = $"Running: {config.Name}";
            UpdateRunButtons(true);

            var result = await _runConfigService.RunConfigurationAsync(config, false);
            
            if (result.Success)
            {
                _viewModel.StatusText = $"Run completed: {config.Name}";
            }
            else
            {
                _viewModel.StatusText = $"Run failed: {result.ErrorMessage ?? "Unknown error"}";
            }
        }
        finally
        {
            _runConfigService.OutputReceived -= OnRunOutputReceived;
            _runConfigService.RunCompleted -= OnRunCompleted;
            _runConfigService.RunStarted -= OnRunStarted;
            UpdateRunButtons(false);
        }
    }

    private void OnRunOutputReceived(object? sender, RunOutputEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppendRunOutput(e.Output);
        });
    }

    private void OnRunStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _viewModel.StatusText = "Running...";
            UpdateRunButtons(true);
        });
    }

    private void OnRunCompleted(object? sender, RunCompletedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateRunButtons(false);
        });
    }

    private void UpdateRunOutput(string text)
    {
        var runOutputText = this.FindControl<TextBlock>("RunOutputText");
        if (runOutputText != null)
        {
            runOutputText.Text = text;
        }
    }

    private void AppendRunOutput(string text)
    {
        var runOutputText = this.FindControl<TextBlock>("RunOutputText");
        if (runOutputText != null)
        {
            if (runOutputText.Text == "Run output will appear here...")
            {
                runOutputText.Text = text;
            }
            else
            {
                runOutputText.Text += text;
            }
        }
    }

    private void UpdateRunButtons(bool isRunning)
    {
        var runButton = this.FindControl<Button>("RunProjectButton");
        var cancelButton = this.FindControl<Button>("CancelBuildButton");
        
        if (runButton != null)
        {
            runButton.IsEnabled = !isRunning;
        }
        
        if (cancelButton != null)
        {
            cancelButton.IsVisible = isRunning;
        }
    }

    /// <summary>
    /// Stop the running process
    /// </summary>
    public void StopRunningProcess()
    {
        if (_runConfigService.IsRunning)
        {
            _runConfigService.Stop();
            _viewModel.StatusText = "Process stopped";
            UpdateRunButtons(false);
        }
    }

    /// <summary>
    /// Show Run Configurations window
    /// </summary>
    public async Task ShowRunConfigurationsAsync()
    {
        var projectPath = GetCurrentProjectPath() ?? _projectPath ?? "";
        var window = new RunConfigurationsWindow(projectPath);
        var result = await window.ShowDialog<RunConfiguration?>(this);
        
        if (result != null)
        {
            _runConfigService.SetActiveConfiguration(result);
            await RunWithConfigurationAsync(result);
        }
    }

    /// <summary>
    /// Show Publish window
    /// </summary>
    public async Task ShowPublishWindowAsync()
    {
        var projectPath = GetCurrentProjectPath() ?? _projectPath ?? "";
        var window = new PublishWindow(projectPath);
        var result = await window.ShowDialog<PublishProfile?>(this);
        
        if (result != null)
        {
            await PublishProjectAsync(result);
        }
    }

    /// <summary>
    /// Publish the project with specified profile
    /// </summary>
    public async Task PublishProjectAsync(PublishProfile profile)
    {
        // Clear output and switch to build panel
        _buildOutput.Clear();
        UpdateBuildOutput();
        SwitchToolWindowPanel("build");

        // Subscribe to output events
        _publishService.OutputReceived += OnPublishOutputReceived;
        _publishService.PublishCompleted += OnPublishCompleted;
        _publishService.PublishStarted += OnPublishStarted;

        try
        {
            _viewModel.StatusText = $"Publishing: {profile.Name}";
            
            var result = await _publishService.PublishAsync(profile);
            
            if (result.Success)
            {
                _viewModel.StatusText = $"Publish succeeded: {result.OutputPath}";
                
                // Offer to open the output folder
                var openFolder = await ShowConfirmDialogAsync(
                    "Publish Succeeded",
                    $"Project published successfully to:\n{result.OutputPath}\n\nWould you like to open the output folder?");
                
                if (openFolder && !string.IsNullOrEmpty(result.OutputPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{result.OutputPath}\"",
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Ignore errors opening explorer
                    }
                }
            }
            else
            {
                _viewModel.StatusText = $"Publish failed: {result.ErrorMessage ?? "Unknown error"}";
            }
        }
        finally
        {
            _publishService.OutputReceived -= OnPublishOutputReceived;
            _publishService.PublishCompleted -= OnPublishCompleted;
            _publishService.PublishStarted -= OnPublishStarted;
        }
    }

    private void OnPublishOutputReceived(object? sender, PublishOutputEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _buildOutput.Append(e.Output);
            UpdateBuildOutput();
        });
    }

    private void OnPublishStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _viewModel.StatusText = "Publishing...";
        });
    }

    private void OnPublishCompleted(object? sender, PublishCompletedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Status will be updated in the main publish method
        });
    }

    /// <summary>
    /// Clean and rebuild the project
    /// </summary>
    public async Task RebuildProjectAsync()
    {
        if (_isBuildInProgress)
        {
            _viewModel.StatusText = "Build already in progress";
            return;
        }

        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to rebuild";
            return;
        }

        _buildOutput.Clear();
        UpdateBuildOutput();
        SwitchToolWindowPanel("build");

        // Clean first
        await _buildService.CleanAsync(projectPath);
        
        // Then build
        await _buildService.BuildAsync(projectPath);
    }

    /// <summary>
    /// Clean the project build output
    /// </summary>
    public async Task CleanProjectAsync()
    {
        if (_isBuildInProgress)
        {
            _viewModel.StatusText = "Build already in progress";
            return;
        }

        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to clean";
            return;
        }

        _buildOutput.Clear();
        UpdateBuildOutput();
        SwitchToolWindowPanel("build");

        await _buildService.CleanAsync(projectPath);
    }

    /// <summary>
    /// Restore NuGet packages
    /// </summary>
    public async Task RestorePackagesAsync()
    {
        if (_isBuildInProgress)
        {
            _viewModel.StatusText = "Build already in progress";
            return;
        }

        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to restore";
            return;
        }

        _buildOutput.Clear();
        UpdateBuildOutput();
        SwitchToolWindowPanel("build");

        await _buildService.RestoreAsync(projectPath);
    }

    /// <summary>
    /// Cancel the current build
    /// </summary>
    public void CancelBuild()
    {
        if (_isBuildInProgress)
        {
            _buildService.CancelBuild();
            _isBuildInProgress = false;
            UpdateBuildButtons();
            _viewModel.StatusText = "Build cancelled";
        }
    }

    /// <summary>
    /// Get the current project path
    /// </summary>
    private string? GetCurrentProjectPath()
    {
        // Try ViewModel's CurrentProjectPath first
        if (!string.IsNullOrEmpty(_viewModel.CurrentProjectPath))
        {
            return _viewModel.CurrentProjectPath;
        }
        
        // Fall back to _projectPath
        return _projectPath;
    }

    #endregion

    #region Code Analysis

    /// <summary>
    /// Initialize code analysis service events
    /// </summary>
    private void InitializeCodeAnalysisService()
    {
        _codeAnalysisService.AnalysisCompleted += OnAnalysisCompleted;
        _codeAnalysisService.AnalysisProgress += OnAnalysisProgress;
    }

    private void OnAnalysisCompleted(object? sender, AnalysisCompletedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isAnalysisInProgress = false;
            UpdateAnalyzeButton();

            if (e.Success)
            {
                // Clear old problems and add new ones
                _viewModel.Problems.Clear();
                foreach (var diagnostic in e.Diagnostics)
                {
                    _viewModel.Problems.Add(diagnostic);
                }

                var errorCount = e.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                var warningCount = e.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

                if (e.Diagnostics.Count == 0)
                {
                    _viewModel.StatusText = "Analysis complete - No problems found!";
                }
                else
                {
                    _viewModel.StatusText = $"Analysis complete - {errorCount} error(s), {warningCount} warning(s)";
                }

                // Switch to problems panel if there are issues
                if (e.Diagnostics.Count > 0)
                {
                    SwitchToolWindowPanel("problems");
                }
            }
            else
            {
                _viewModel.StatusText = $"Analysis failed: {e.ErrorMessage ?? "Unknown error"}";
            }
        });
    }

    private void OnAnalysisProgress(object? sender, AnalysisProgressEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _viewModel.StatusText = e.Message;
        });
    }

    /// <summary>
    /// Analyze button click handler
    /// </summary>
    private async void AnalyzeProject_Click(object? sender, RoutedEventArgs e)
    {
        await AnalyzeProjectAsync();
    }

    /// <summary>
    /// Refresh analysis button click handler
    /// </summary>
    private async void RefreshAnalysis_Click(object? sender, RoutedEventArgs e)
    {
        await AnalyzeProjectAsync();
    }

    /// <summary>
    /// Clear problems button click handler
    /// </summary>
    private void ClearProblems_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.Problems.Clear();
        _viewModel.StatusText = "Problems cleared";
    }

    /// <summary>
    /// Problems list selection changed - navigate to file location
    /// </summary>
    private void ProblemsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is DiagnosticItem diagnostic)
        {
            // Open file and navigate to location
            if (!string.IsNullOrEmpty(diagnostic.FilePath) && File.Exists(diagnostic.FilePath))
            {
                OpenFileInEditor(diagnostic.FilePath);
                
                // Navigate to the specific line in Monaco
                _monacoEditor?.GoToLine(diagnostic.Line, diagnostic.Column);
                
                _viewModel.StatusText = $"{diagnostic.SeverityIcon} {diagnostic.Code}: {diagnostic.Message}";
            }
        }
    }

    /// <summary>
    /// Analyze the current project with Roslyn
    /// </summary>
    public async Task AnalyzeProjectAsync()
    {
        if (_isAnalysisInProgress)
        {
            _viewModel.StatusText = "Analysis already in progress";
            return;
        }

        // Get project path
        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to analyze";
            return;
        }

        _isAnalysisInProgress = true;
        UpdateAnalyzeButton();

        // Switch to Problems panel
        SwitchToolWindowPanel("problems");

        _viewModel.StatusText = "Starting code analysis...";

        try
        {
            // Run analysis
            await _codeAnalysisService.AnalyzeProjectWithCallbackAsync(projectPath);
        }
        catch (Exception ex)
        {
            _isAnalysisInProgress = false;
            UpdateAnalyzeButton();
            _viewModel.StatusText = $"Analysis error: {ex.Message}";
        }
    }

    /// <summary>
    /// Analyze current file only
    /// </summary>
    public async Task AnalyzeCurrentFileAsync()
    {
        if (_viewModel.ActiveTab == null || string.IsNullOrEmpty(_viewModel.ActiveTab.FilePath))
        {
            _viewModel.StatusText = "No file to analyze";
            return;
        }

        var filePath = _viewModel.ActiveTab.FilePath;
        if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.StatusText = "Code analysis is only available for C# files";
            return;
        }

        _isAnalysisInProgress = true;
        UpdateAnalyzeButton();
        _viewModel.StatusText = "Analyzing current file...";

        try
        {
            // Get content from Monaco editor if available
            string? content = null;
            if (_monacoEditor != null)
            {
                content = await _monacoEditor.GetContentAsync();
            }

            var diagnostics = await _codeAnalysisService.AnalyzeFileAsync(filePath, content);

            // Update problems
            _viewModel.Problems.Clear();
            foreach (var diagnostic in diagnostics)
            {
                _viewModel.Problems.Add(diagnostic);
            }

            var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warningCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            if (diagnostics.Count == 0)
            {
                _viewModel.StatusText = $"No problems in {_viewModel.ActiveTab.FileName}";
            }
            else
            {
                _viewModel.StatusText = $"{_viewModel.ActiveTab.FileName}: {errorCount} error(s), {warningCount} warning(s)";
                SwitchToolWindowPanel("problems");
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Analysis error: {ex.Message}";
        }
        finally
        {
            _isAnalysisInProgress = false;
            UpdateAnalyzeButton();
        }
    }

    /// <summary>
    /// Parse build output and show problems from build
    /// </summary>
    private void ParseAndShowBuildProblems(string buildOutput)
    {
        var diagnostics = _codeAnalysisService.ParseBuildOutput(buildOutput);
        
        if (diagnostics.Count > 0)
        {
            _viewModel.Problems.Clear();
            foreach (var diagnostic in diagnostics)
            {
                _viewModel.Problems.Add(diagnostic);
            }
        }
    }

    /// <summary>
    /// Update analyze button state
    /// </summary>
    private void UpdateAnalyzeButton()
    {
        var analyzeButton = this.FindControl<Button>("AnalyzeProjectButton");
        if (analyzeButton != null)
        {
            analyzeButton.IsEnabled = !_isAnalysisInProgress;
        }
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
