using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Terminal control for executing CMD/PowerShell commands in Avalonia
/// Supports both normal and administrator mode
/// </summary>
public class TerminalControl : UserControl
{
    private Process? _process;
    private StreamWriter? _inputWriter;
    private readonly StringBuilder _outputBuffer = new();
    private readonly ConcurrentQueue<string> _inputHistory = new();
    private int _historyIndex = -1;
    private bool _isRunning;
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    
    private TextBox? _inputTextBox;
    private TextBlock? _outputTextBlock;
    private ScrollViewer? _scrollViewer;
    private StackPanel? _mainPanel;
    private TextBlock? _promptLabel;
    
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    public event EventHandler? ProcessExited;
    public event EventHandler? ProcessStarted;
    
    public static readonly StyledProperty<bool> IsAdministratorProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(IsAdministrator), defaultValue: false);
    
    public static readonly StyledProperty<TerminalShellType> ShellTypeProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalShellType>(nameof(ShellType), defaultValue: TerminalShellType.Cmd);
    
    public static readonly StyledProperty<string> WorkingDirectoryProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(WorkingDirectory), defaultValue: "");
    
    public bool IsAdministrator
    {
        get => GetValue(IsAdministratorProperty);
        set => SetValue(IsAdministratorProperty, value);
    }
    
    public TerminalShellType ShellType
    {
        get => GetValue(ShellTypeProperty);
        set => SetValue(ShellTypeProperty, value);
    }
    
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set
        {
            if (Directory.Exists(value))
            {
                var oldDir = _workingDirectory;
                _workingDirectory = value;
                SetValue(WorkingDirectoryProperty, value);
                
                // Update prompt label
                UpdatePrompt();
                
                // If a process is running, change directory
                if (_isRunning && _inputWriter != null && oldDir != value)
                {
                    // Send cd command to change directory
                    _inputWriter.WriteLine($"cd /d \"{value}\"");
                }
            }
        }
    }
    
    /// <summary>
    /// Update the prompt to show current working directory
    /// </summary>
    private void UpdatePrompt()
    {
        if (_promptLabel != null)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _promptLabel.Text = $"{_workingDirectory}> ";
            });
        }
    }
    
    /// <summary>
    /// Change to the specified directory
    /// </summary>
    public void ChangeDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            WorkingDirectory = path;
            if (!_isRunning)
            {
                AppendOutput($"Changed directory to: {path}{Environment.NewLine}", Color.Parse("#858585"));
            }
        }
        else
        {
            AppendOutput($"Directory not found: {path}{Environment.NewLine}", Color.Parse("#F44747"));
        }
    }
    
    public bool IsRunning => _isRunning;
    
    public TerminalControl()
    {
        InitializeUI();
    }
    
    private void InitializeUI()
    {
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        
        // Output area with scroll
        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E"))
        };
        
        _mainPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical
        };
        
        _outputTextBlock = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(8),
            IsVisible = true
        };
        
        _mainPanel.Children.Add(_outputTextBlock);
        _scrollViewer.Content = _mainPanel;
        
        Grid.SetRow(_scrollViewer, 0);
        grid.Children.Add(_scrollViewer);
        
        // Input area
        var inputPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Background = new SolidColorBrush(Color.Parse("#252526")),
            Margin = new Thickness(0, 2, 0, 0)
        };
        
        _promptLabel = new TextBlock
        {
            Text = $"{_workingDirectory}> ",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#569CD6")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 0, 0)
        };
        
        Grid.SetColumn(_promptLabel, 0);
        inputPanel.Children.Add(_promptLabel);
        
        _inputTextBox = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#6B2F9C")),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = new SolidColorBrush(Color.Parse("#6B2F9C")),
            Padding = new Thickness(4, 8),
            AcceptsReturn = false
        };
        
        _inputTextBox.KeyDown += OnInputKeyDown;
        
        Grid.SetColumn(_inputTextBox, 1);
        inputPanel.Children.Add(_inputTextBox);
        
        Grid.SetRow(inputPanel, 1);
        grid.Children.Add(inputPanel);
        
        Content = grid;
        
        // Show welcome message
        AppendOutput($"Insait Terminal [Version 1.0.0]{Environment.NewLine}");
        AppendOutput($"(c) Insait Edit. Type 'help' for commands.{Environment.NewLine}{Environment.NewLine}");
    }
    
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var command = _inputTextBox?.Text ?? string.Empty;
            ExecuteCommand(command);
            if (_inputTextBox != null)
            {
                _inputTextBox.Text = string.Empty;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            NavigateHistory(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            NavigateHistory(1);
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            // Ctrl+C to stop current process
            StopCurrentProcess();
            e.Handled = true;
        }
    }
    
    private void NavigateHistory(int direction)
    {
        var historyArray = _inputHistory.ToArray();
        if (historyArray.Length == 0) return;
        
        _historyIndex += direction;
        
        if (_historyIndex < 0) _historyIndex = 0;
        if (_historyIndex >= historyArray.Length) _historyIndex = historyArray.Length - 1;
        
        if (_inputTextBox != null && _historyIndex >= 0 && _historyIndex < historyArray.Length)
        {
            _inputTextBox.Text = historyArray[historyArray.Length - 1 - _historyIndex];
            _inputTextBox.CaretIndex = _inputTextBox.Text?.Length ?? 0;
        }
    }
    
    /// <summary>
    /// Execute a command in the terminal
    /// </summary>
    public void ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        
        // Add to history
        _inputHistory.Enqueue(command);
        _historyIndex = -1;
        
        // Show command in output
        AppendOutput($"> {command}{Environment.NewLine}", Color.Parse("#569CD6"));
        
        // Handle built-in commands
        if (HandleBuiltInCommand(command)) return;
        
        // Execute external command
        if (_isRunning && _inputWriter != null)
        {
            // Send to running process
            _inputWriter.WriteLine(command);
        }
        else
        {
            // Start new command
            _ = ExecuteExternalCommandAsync(command);
        }
    }
    
    private bool HandleBuiltInCommand(string command)
    {
        var parts = command.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        
        var cmd = parts[0].ToLowerInvariant();
        
        switch (cmd)
        {
            case "clear":
            case "cls":
                ClearOutput();
                return true;
                
            case "help":
                ShowHelp();
                return true;
                
            case "admin":
                StartAdministratorShell();
                return true;
                
            case "powershell":
            case "ps":
                ShellType = TerminalShellType.PowerShell;
                AppendOutput($"Switched to PowerShell mode.{Environment.NewLine}", Color.Parse("#4EC9B0"));
                return true;
                
            case "cmd":
                ShellType = TerminalShellType.Cmd;
                AppendOutput($"Switched to CMD mode.{Environment.NewLine}", Color.Parse("#4EC9B0"));
                return true;
                
            case "exit":
                StopCurrentProcess();
                return true;
                
            case "pwd":
                AppendOutput($"{_workingDirectory}{Environment.NewLine}");
                return true;
            
            case "cd":
                // Handle cd command when no process is running
                if (!_isRunning && parts.Length > 1)
                {
                    var targetPath = parts[1].Trim().Trim('"');
                    
                    // Handle relative paths
                    string newPath;
                    if (Path.IsPathRooted(targetPath))
                    {
                        newPath = targetPath;
                    }
                    else if (targetPath == "..")
                    {
                        var parent = Directory.GetParent(_workingDirectory);
                        newPath = parent?.FullName ?? _workingDirectory;
                    }
                    else
                    {
                        newPath = Path.Combine(_workingDirectory, targetPath);
                    }
                    
                    if (Directory.Exists(newPath))
                    {
                        _workingDirectory = Path.GetFullPath(newPath);
                        UpdatePrompt();
                        AppendOutput($"{_workingDirectory}{Environment.NewLine}");
                    }
                    else
                    {
                        AppendOutput($"The system cannot find the path specified.{Environment.NewLine}", Color.Parse("#F44747"));
                    }
                    return true;
                }
                else if (!_isRunning && parts.Length == 1)
                {
                    // Just show current directory
                    AppendOutput($"{_workingDirectory}{Environment.NewLine}");
                    return true;
                }
                return false; // Let the running process handle it
                
            default:
                return false;
        }
    }
    
    private void ShowHelp()
    {
        var help = @"
╔══════════════════════════════════════════════════════════════╗
║                    INSAIT TERMINAL HELP                       ║
╠══════════════════════════════════════════════════════════════╣
║  Built-in Commands:                                           ║
║  ─────────────────                                            ║
║  cls, clear    - Clear terminal output                        ║
║  help          - Show this help                               ║
║  admin         - Start new administrator shell                ║
║  powershell,ps - Switch to PowerShell mode                    ║
║  cmd           - Switch to CMD mode                           ║
║  exit          - Stop current process                         ║
║  pwd           - Print working directory                      ║
║                                                               ║
║  Keyboard Shortcuts:                                          ║
║  ──────────────────                                           ║
║  ↑/↓           - Navigate command history                     ║
║  Ctrl+C        - Stop current process                         ║
║  Enter         - Execute command                              ║
╚══════════════════════════════════════════════════════════════╝

";
        AppendOutput(help, Color.Parse("#4EC9B0"));
    }
    
    /// <summary>
    /// Start a new administrator shell
    /// </summary>
    public void StartAdministratorShell()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                UseShellExecute = true,
                Verb = "runas", // Run as administrator
                WorkingDirectory = _workingDirectory
            };
            
            Process.Start(startInfo);
            AppendOutput($"Administrator shell started in new window.{Environment.NewLine}", Color.Parse("#4EC9B0"));
        }
        catch (Exception ex)
        {
            AppendOutput($"Error starting administrator shell: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
        }
    }
    
    /// <summary>
    /// Start a persistent interactive shell session
    /// </summary>
    public async Task StartInteractiveShellAsync()
    {
        if (_isRunning) return;
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            if (ShellType == TerminalShellType.PowerShell)
            {
                startInfo.Arguments = "-NoExit -Command -";
            }
            
            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.Exited += OnProcessExited;
            _process.EnableRaisingEvents = true;
            
            _process.Start();
            _inputWriter = _process.StandardInput;
            
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            
            _isRunning = true;
            ProcessStarted?.Invoke(this, EventArgs.Empty);
            
            AppendOutput($"Interactive {ShellType} session started.{Environment.NewLine}", Color.Parse("#4EC9B0"));
            AppendOutput($"Working directory: {_workingDirectory}{Environment.NewLine}", Color.Parse("#858585"));
        }
        catch (Exception ex)
        {
            AppendOutput($"Error starting shell: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
        }
        
        await Task.CompletedTask;
    }
    
    private async Task ExecuteExternalCommandAsync(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                Arguments = GetShellArguments(command),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.Exited += OnProcessExited;
            _process.EnableRaisingEvents = true;
            
            _isRunning = true;
            _process.Start();
            _inputWriter = _process.StandardInput;
            
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            
            ProcessStarted?.Invoke(this, EventArgs.Empty);
            
            await _process.WaitForExitAsync();
            
            _isRunning = false;
            _inputWriter = null;
        }
        catch (Exception ex)
        {
            AppendOutput($"Error: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
            _isRunning = false;
        }
    }
    
    /// <summary>
    /// Run a command with administrator privileges (UAC prompt)
    /// </summary>
    public async Task<bool> RunAsAdministratorAsync(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                Arguments = GetShellArguments(command),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = _workingDirectory
            };
            
            AppendOutput($"[ADMIN] {command}{Environment.NewLine}", Color.Parse("#CE9178"));
            
            var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                AppendOutput($"Administrator command completed with exit code: {process.ExitCode}{Environment.NewLine}", 
                    process.ExitCode == 0 ? Color.Parse("#4EC9B0") : Color.Parse("#F44747"));
                return process.ExitCode == 0;
            }
            
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            AppendOutput($"Administrator operation cancelled by user.{Environment.NewLine}", Color.Parse("#DCDCAA"));
            return false;
        }
        catch (Exception ex)
        {
            AppendOutput($"Error running as administrator: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
            return false;
        }
    }
    
    private string GetShellExecutable()
    {
        return ShellType switch
        {
            TerminalShellType.PowerShell => "powershell.exe",
            TerminalShellType.PowerShellCore => "pwsh.exe",
            TerminalShellType.Cmd => "cmd.exe",
            TerminalShellType.GitBash => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            _ => "cmd.exe"
        };
    }
    
    private string GetShellArguments(string command)
    {
        return ShellType switch
        {
            TerminalShellType.PowerShell => $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            TerminalShellType.PowerShellCore => $"-NoProfile -Command \"{command}\"",
            TerminalShellType.Cmd => $"/c {command}",
            TerminalShellType.GitBash => $"-c \"{command}\"",
            _ => $"/c {command}"
        };
    }
    
    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppendOutput(e.Data + Environment.NewLine);
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(e.Data, false));
            });
        }
    }
    
    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppendOutput(e.Data + Environment.NewLine, Color.Parse("#F44747"));
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(e.Data, true));
            });
        }
    }
    
    private void OnProcessExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isRunning = false;
            _inputWriter = null;
            ProcessExited?.Invoke(this, EventArgs.Empty);
        });
    }
    
    /// <summary>
    /// Stop the current running process
    /// </summary>
    public void StopCurrentProcess()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                AppendOutput($"{Environment.NewLine}^C Process terminated.{Environment.NewLine}", Color.Parse("#DCDCAA"));
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error stopping process: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
        }
        finally
        {
            _isRunning = false;
            _inputWriter = null;
        }
    }
    
    /// <summary>
    /// Append text to terminal output
    /// </summary>
    public void AppendOutput(string text, Color? color = null)
    {
        if (_outputTextBlock == null) return;
        
        _outputBuffer.Append(text);
        
        // For simplicity, we update the entire text block
        // In a more advanced implementation, you could use Inlines for different colors
        _outputTextBlock.Text = _outputBuffer.ToString();
        
        // Scroll to bottom
        _scrollViewer?.ScrollToEnd();
    }
    
    /// <summary>
    /// Clear terminal output
    /// </summary>
    public void ClearOutput()
    {
        _outputBuffer.Clear();
        if (_outputTextBlock != null)
        {
            _outputTextBlock.Text = string.Empty;
        }
    }
    
    /// <summary>
    /// Set focus to the input text box
    /// </summary>
    public void FocusInput()
    {
        _inputTextBox?.Focus();
    }
    
    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        StopCurrentProcess();
        _process?.Dispose();
    }
}

/// <summary>
/// Shell type enumeration
/// </summary>
public enum TerminalShellType
{
    Cmd,
    PowerShell,
    PowerShellCore,
    GitBash
}

/// <summary>
/// Event args for terminal output
/// </summary>
public class TerminalOutputEventArgs : EventArgs
{
    public string Output { get; }
    public bool IsError { get; }
    
    public TerminalOutputEventArgs(string output, bool isError)
    {
        Output = output;
        IsError = isError;
    }
}

