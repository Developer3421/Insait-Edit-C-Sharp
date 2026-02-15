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
    private string? _projectPath;
    private MonacoEditorControl? _monacoEditor;
    private TerminalControl? _terminalControl;
    private bool _isBuildInProgress;
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
        _projectPath = projectPath;
        
        DataContext = _viewModel;
        
        // Set initial window position for restore
        _restoreSize = new Size(Width, Height);
        
        // Initialize Monaco Editor
        InitializeMonacoEditor();
        
        // Initialize Build Service events
        InitializeBuildService();
        
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
        // Ctrl+N - New file
        else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CreateNewFile();
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
            // Look for .sln or .csproj files
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
            
            if (extension == ".sln")
            {
                // Load solution - load its directory
                var directory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _viewModel.LoadProjectFolder(directory);
                }
                _viewModel.StatusText = $"Loaded solution: {Path.GetFileName(projectPath)}";
            }
            else if (extension == ".csproj" || extension == ".fsproj" || extension == ".vbproj")
            {
                // Load project - load its directory
                var directory = Path.GetDirectoryName(projectPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _viewModel.LoadProjectFolder(directory);
                }
                _viewModel.StatusText = $"Loaded project: {Path.GetFileName(projectPath)}";
            }
            else
            {
                // Open as single file d d
                _viewModel.OpenFile(projectPath);
                OpenFileInEditor(projectPath);
            }
        }
        else if (Directory.Exists(projectPath))
        {
            // Load folder
            _viewModel.LoadProjectFolder(projectPath);
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
            case "Clean":
                await CleanProjectAsync();
                break;
            case "Run":
                await RunProjectAsync();
                break;
            case "Stop":
                CancelBuild();
                break;
            case "RestorePackages":
                await RestorePackagesAsync();
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
            _viewModel.LoadProjectFolder(_projectPath);
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

    private void Explorer_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show explorer panel
    }

    private void Search_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show search panel
    }

    private void Git_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show git panel
    }

    private void Debug_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show debug panel
    }

    private void Extensions_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show extensions panel
    }

    private void Account_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show account panel
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Show settings
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
            // TODO: Send message to AI assistant
            var message = input.Text;
            input.Text = string.Empty;
            
            // Process AI message
            ProcessAIMessage(message);
        }
    }

    private async void ProcessAIMessage(string message)
    {
        // TODO: Implement AI integration
        await Task.Delay(100); // Placeholder
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
            
            if (e.Result.Success)
            {
                _viewModel.StatusText = "Build succeeded";
            }
            else
            {
                _viewModel.StatusText = $"Build failed: {e.Result.ErrorMessage ?? "Unknown error"}";
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
    /// Cancel build button click handler
    /// </summary>
    private void CancelBuild_Click(object? sender, RoutedEventArgs e)
    {
        CancelBuild();
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
        if (_isBuildInProgress)
        {
            _viewModel.StatusText = "Build already in progress";
            return;
        }

        // Get project path
        var projectPath = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(projectPath))
        {
            _viewModel.StatusText = "No project loaded to run";
            return;
        }

        // Clear previous build output
        _buildOutput.Clear();
        UpdateBuildOutput();

        // Switch to Build tab
        SwitchToolWindowPanel("build");

        // Build and run the project
        var result = await _buildService.BuildAndRunAsync(projectPath);
        
        // Update status
        if (result.Success)
        {
            _viewModel.StatusText = "Project started successfully";
        }
        else
        {
            _viewModel.StatusText = "Build failed - see Build output for details";
        }
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
}