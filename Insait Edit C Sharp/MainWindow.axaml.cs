using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.ViewModels;
using Insait_Edit_C_Sharp.Services;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow : Window
{
    private bool _isMaximized;
    private PixelPoint _restorePosition;
    private Size _restoreSize;
    private readonly MainViewModel _viewModel;
    private readonly FileService _fileService;
    private string? _projectPath;
    private MonacoEditorControl? _monacoEditor;
    private TerminalControl? _terminalControl;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? projectPath)
    {
        InitializeComponent();
        
        _viewModel = new MainViewModel();
        _fileService = new FileService();
        _projectPath = projectPath;
        
        DataContext = _viewModel;
        
        // Set initial window position for restore
        _restoreSize = new Size(Width, Height);
        
        // Initialize Monaco Editor
        InitializeMonacoEditor();
        
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
        
        // Update title
        UpdateTitle();
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
        // Mark current file as modified
        _viewModel.StatusText = "Modified";
    }

    private void OnCursorPositionChanged(object? sender, CursorPositionChangedEventArgs e)
    {
        _viewModel.StatusText = $"Ln {e.Line}, Col {e.Column}";
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
                // Open as single file
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

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion

    #region Menu Handlers

    private void MenuFile_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement file menu flyout
    }

    private void MenuEdit_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement edit menu flyout
    }

    private void MenuView_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement view menu flyout
    }

    private void MenuBuild_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement build menu flyout
    }

    private void MenuDebug_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement debug menu flyout
    }

    private void MenuTools_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement tools menu flyout
    }

    private void MenuHelp_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement help menu flyout
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

    private void CloseTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is EditorTab tab)
        {
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

    #endregion

    #region Terminal

    private void InitializeTerminal()
    {
        var container = this.FindControl<Border>("TerminalContainer");
        if (container != null)
        {
            _terminalControl = new TerminalControl();
            
            // Set working directory to project path if available
            if (!string.IsNullOrEmpty(_projectPath))
            {
                var directory = File.Exists(_projectPath) 
                    ? Path.GetDirectoryName(_projectPath) 
                    : _projectPath;
                    
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    _terminalControl.WorkingDirectory = directory;
                }
            }
            
            // Subscribe to terminal events
            _terminalControl.ProcessStarted += OnTerminalProcessStarted;
            _terminalControl.ProcessExited += OnTerminalProcessExited;
            _terminalControl.OutputReceived += OnTerminalOutputReceived;
            
            container.Child = _terminalControl;
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
        // TODO: Switch to problems panel
    }
    
    private void OutputTab_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Switch to output panel
    }
    
    private void TerminalTab_Click(object? sender, RoutedEventArgs e)
    {
        // Focus terminal
        _terminalControl?.FocusInput();
    }
    
    private void DebugConsoleTab_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Switch to debug console panel
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

    #endregion
}