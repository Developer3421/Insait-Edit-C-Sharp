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
    private ConPtyHost? _conPty;
    private bool _usingConPty;
    private readonly StringBuilder _outputBuffer = new();
    private readonly ConcurrentQueue<string> _inputHistory = new();
    private int _historyIndex = -1;
    private bool _isRunning;
    private bool _isGitHubCliMode;
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    
    private TextBox? _inputTextBox;
    private AnsiGridTerminalControl? _ansiTerminal;
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
    
    public static readonly StyledProperty<bool> UsePseudoConsoleProperty =
        AvaloniaProperty.Register<TerminalControl, bool>(nameof(UsePseudoConsole), defaultValue: true);

    /// <summary>
    /// If true (default), uses Windows ConPTY (pseudo console) for interactive shells.
    /// This is required for many interactive CLIs to behave correctly inside the app.
    /// </summary>
    public bool UsePseudoConsole
    {
        get => GetValue(UsePseudoConsoleProperty);
        set => SetValue(UsePseudoConsoleProperty, value);
    }

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
                if (_isGitHubCliMode)
                {
                    _promptLabel.Text = $"🐙 gh [{_workingDirectory}]> ";
                }
                else
                {
                    _promptLabel.Text = $"{_workingDirectory}> ";
                }
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
        // Replace TextBlock-only output with an ANSI-aware renderer.
        _ansiTerminal = new AnsiGridTerminalControl();

        Grid.SetRow(_ansiTerminal, 0);
        grid.Children.Add(_ansiTerminal);

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
        var prompt = _isGitHubCliMode ? "gh> " : "> ";
        AppendOutput($"{prompt}{command}{Environment.NewLine}", Color.Parse("#569CD6"));

        // Handle built-in commands
        if (HandleBuiltInCommand(command)) return;
        
        // GitHub CLI mode - wrap commands with gh prefix
        if (_isGitHubCliMode)
        {
            _ = ExecuteGitHubCliCommandAsync(command);
            return;
        }

        // Execute external command
        if (_isRunning)
        {
            if (_usingConPty && _conPty != null)
            {
                _conPty.WriteLine(command);
                return;
            }

            if (_inputWriter != null)
            {
                _inputWriter.WriteLine(command);
                return;
            }
        }

        // Start new command
        _ = ExecuteExternalCommandAsync(command);
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
            
            case "github":
            case "ghshell":
                // Start GitHub CLI shell mode
                ShellType = TerminalShellType.GitHubCli;
                _ = StartGitHubCliShellAsync();
                return true;
            
            case "copilot":
            case "gh-copilot":
                // Open GitHub Copilot CLI in external terminal (requires real TTY)
                var copilotArgs = parts.Length > 1 ? parts[1] : null;
                OpenGitHubCopilotTerminal(copilotArgs);
                return true;
            
            case "terminal":
            case "wt":
                // Open external Windows Terminal
                var termCommand = parts.Length > 1 ? parts[1] : null;
                OpenExternalTerminal(termCommand);
                return true;
                
            case "exit":
                if (_isGitHubCliMode)
                {
                    _isGitHubCliMode = false;
                    _isRunning = false;
                    UpdatePrompt();
                    AppendOutput($"Exited GitHub CLI shell.{Environment.NewLine}", Color.Parse("#4EC9B0"));
                    ProcessExited?.Invoke(this, EventArgs.Empty);
                    return true;
                }
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
║  github, gh    - Switch to GitHub CLI mode                    ║
║  exit          - Stop current process/mode                    ║
║  pwd           - Print working directory                      ║
║                                                               ║
║  Keyboard Shortcuts:                                          ║
║  ──────────────────                                           ║
║  ↑/↓           - Navigate command history                     ║
║  Ctrl+C        - Stop current process                         ║
║  Enter         - Execute command                              ║
╚══════════════════════════════════════════════════════════════╝

";
        if (_isGitHubCliMode)
        {
            help += @"
╔══════════════════════════════════════════════════════════════╗
║                  GITHUB CLI SHELL COMMANDS                    ║
╠══════════════════════════════════════════════════════════════╣
║  Repository:    repo create|clone|view|list|fork|delete       ║
║  Pull Requests: pr create|list|view|checkout|merge|close      ║
║  Issues:        issue create|list|view|close|reopen|edit      ║
║  Workflows:     workflow list|view|run|enable|disable         ║
║  Releases:      release create|list|view|download|delete      ║
║  Gists:         gist create|list|view|edit|delete|clone       ║
║  Authentication: auth login|logout|status|refresh             ║
║  Other:         browse|codespace|search|api|copilot           ║
║                                                               ║
║  💡 Commands run with 'gh' prefix automatically              ║
║  Example: Type 'pr list' instead of 'gh pr list'             ║
╚══════════════════════════════════════════════════════════════╝

";
        }
        AppendOutput(help, Color.Parse("#4EC9B0"));
    }
    
    /// <summary>
    /// Execute a GitHub CLI command
    /// </summary>
    private async Task ExecuteGitHubCliCommandAsync(string command)
    {
        try
        {
            var ghPath = FindGhExecutable();
            var trimmedCommand = command.Trim();
            
            // Handle special shell commands
            if (trimmedCommand.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                _isGitHubCliMode = false;
                _isRunning = false;
                AppendOutput($"Exited GitHub CLI shell.{Environment.NewLine}", Color.Parse("#4EC9B0"));
                ProcessExited?.Invoke(this, EventArgs.Empty);
                return;
            }
            
            if (trimmedCommand.Equals("gh-help", StringComparison.OrdinalIgnoreCase) || 
                trimmedCommand.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp();
                return;
            }
            
            // Handle cd command
            if (trimmedCommand.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
            {
                var targetPath = trimmedCommand.Substring(3).Trim().Trim('"');
                string newPath;
                if (Path.IsPathRooted(targetPath))
                    newPath = targetPath;
                else if (targetPath == "..")
                    newPath = Directory.GetParent(_workingDirectory)?.FullName ?? _workingDirectory;
                else
                    newPath = Path.Combine(_workingDirectory, targetPath);
                
                if (Directory.Exists(newPath))
                {
                    _workingDirectory = Path.GetFullPath(newPath);
                    UpdatePrompt();
                    AppendOutput($"Changed directory to: {_workingDirectory}{Environment.NewLine}", Color.Parse("#4EC9B0"));
                }
                else
                {
                    AppendOutput($"Directory not found: {newPath}{Environment.NewLine}", Color.Parse("#F44747"));
                }
                return;
            }
            
            // Handle pwd command
            if (trimmedCommand.Equals("pwd", StringComparison.OrdinalIgnoreCase))
            {
                AppendOutput($"{_workingDirectory}{Environment.NewLine}");
                return;
            }
            
            // Determine if command already has 'gh' prefix
            string ghArgs;
            if (trimmedCommand.StartsWith("gh ", StringComparison.OrdinalIgnoreCase))
            {
                // Command already has 'gh' prefix, remove it
                ghArgs = trimmedCommand.Substring(3).Trim();
            }
            else
            {
                // Add command as gh argument
                ghArgs = trimmedCommand;
            }
            
            // Interactive GitHub Copilot commands need external terminal (real TTY)
            if (ghArgs.StartsWith("copilot ", StringComparison.OrdinalIgnoreCase) ||
                ghArgs.Equals("copilot", StringComparison.OrdinalIgnoreCase))
            {
                var copilotSubArgs = ghArgs.Length > 8 ? ghArgs.Substring(8).Trim() : "";
                
                // suggest and explain require interactive TUI - open in external terminal
                if (string.IsNullOrEmpty(copilotSubArgs) ||
                    copilotSubArgs.StartsWith("suggest", StringComparison.OrdinalIgnoreCase) ||
                    copilotSubArgs.StartsWith("explain", StringComparison.OrdinalIgnoreCase))
                {
                    AppendOutput($"ℹ️ Interactive Copilot commands require a real terminal.{Environment.NewLine}", Color.Parse("#DCDCAA"));
                    OpenGitHubCopilotTerminal(copilotSubArgs);
                    return;
                }
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = ghPath,
                Arguments = ghArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;
            
            if (!string.IsNullOrEmpty(output))
            {
                AppendOutput(output + Environment.NewLine);
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                // Some gh commands output to stderr for progress, not always errors
                if (process.ExitCode != 0)
                    AppendOutput(error + Environment.NewLine, Color.Parse("#F44747"));
                else
                    AppendOutput(error + Environment.NewLine, Color.Parse("#DCDCAA"));
            }
            
            if (process.ExitCode != 0 && string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error))
            {
                AppendOutput($"Command exited with code: {process.ExitCode}{Environment.NewLine}", Color.Parse("#F44747"));
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error executing command: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
        }
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
    /// Opens an external Windows terminal (Windows Terminal or cmd.exe) with the specified command.
    /// Used for interactive CLI commands like gh copilot suggest/explain that require a real TTY.
    /// </summary>
    public void OpenExternalTerminal(string? command = null, string? title = null)
    {
        try
        {
            ProcessStartInfo startInfo;
            
            // Try to use Windows Terminal first (wt.exe) - provides best experience
            var wtPath = FindWindowsTerminal();
            
            if (!string.IsNullOrEmpty(wtPath))
            {
                // Windows Terminal available
                var wtArgs = $"-d \"{_workingDirectory}\"";
                if (!string.IsNullOrEmpty(title))
                    wtArgs += $" --title \"{title}\"";
                if (!string.IsNullOrEmpty(command))
                    wtArgs += $" cmd /k \"{command}\"";
                
                startInfo = new ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = wtArgs,
                    UseShellExecute = true,
                    WorkingDirectory = _workingDirectory
                };
            }
            else
            {
                // Fallback to cmd.exe
                var cmdArgs = string.IsNullOrEmpty(command) 
                    ? "/k" 
                    : $"/k \"{command}\"";
                
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    WorkingDirectory = _workingDirectory
                };
            }
            
            Process.Start(startInfo);
            
            var terminalName = !string.IsNullOrEmpty(wtPath) ? "Windows Terminal" : "Command Prompt";
            AppendOutput($"✅ Opened {terminalName} in new window.{Environment.NewLine}", Color.Parse("#4EC9B0"));
            if (!string.IsNullOrEmpty(command))
                AppendOutput($"   Running: {command}{Environment.NewLine}", Color.Parse("#858585"));
        }
        catch (Exception ex)
        {
            AppendOutput($"❌ Error opening terminal: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
        }
    }
    
    /// <summary>
    /// Opens GitHub Copilot CLI in an external terminal for interactive TUI commands.
    /// </summary>
    public void OpenGitHubCopilotTerminal(string? copilotArgs = null)
    {
        try
        {
            // Find the full path to gh.exe
            var ghPath = FindGhExecutable();
            
            // If gh.exe is just the name (in PATH), try to resolve full path
            if (ghPath == "gh.exe" || ghPath == "gh")
            {
                ghPath = ResolveFullGhPath() ?? "gh";
            }
            
            // Build the gh copilot command with full path
            var ghCommand = string.IsNullOrEmpty(copilotArgs) 
                ? $"\"{ghPath}\" copilot" 
                : $"\"{ghPath}\" copilot {copilotArgs}";
            
            ProcessStartInfo startInfo;
            
            // Try Windows Terminal first for best TUI experience
            var wtPath = FindWindowsTerminal();
            
            if (!string.IsNullOrEmpty(wtPath))
            {
                // Windows Terminal - provides best TTY support for interactive CLIs
                startInfo = new ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{_workingDirectory}\" --title \"GitHub Copilot CLI\" cmd /k {ghCommand}",
                    UseShellExecute = true,
                    WorkingDirectory = _workingDirectory
                };
            }
            else
            {
                // Fallback to cmd.exe 
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k {ghCommand}",
                    UseShellExecute = true,
                    WorkingDirectory = _workingDirectory
                };
            }
            
            Process.Start(startInfo);
            
            var terminalName = !string.IsNullOrEmpty(wtPath) ? "Windows Terminal" : "Command Prompt";
            AppendOutput($"🤖 Opened GitHub Copilot CLI in {terminalName}{Environment.NewLine}", Color.Parse("#4EC9B0"));
            AppendOutput($"   Command: {ghCommand}{Environment.NewLine}", Color.Parse("#858585"));
            AppendOutput($"   Working directory: {_workingDirectory}{Environment.NewLine}", Color.Parse("#858585"));
        }
        catch (Exception ex)
        {
            AppendOutput($"❌ Error opening GitHub Copilot: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
        }
    }
    
    /// <summary>
    /// Resolves the full path to gh.exe by searching PATH and common locations
    /// </summary>
    private static string? ResolveFullGhPath()
    {
        // Search in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var path in pathEnv.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var ghPath = Path.Combine(path.Trim(), "gh.exe");
            if (File.Exists(ghPath))
                return ghPath;
        }
        
        // Check common installation locations
        var possiblePaths = new[]
        {
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\gh\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GitHub CLI\gh.exe")
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        return null;
    }
    
    /// <summary>
    /// Finds Windows Terminal (wt.exe) if installed
    /// </summary>
    private static string? FindWindowsTerminal()
    {
        // Check if wt.exe is in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var path in pathEnv.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var wtPath = Path.Combine(path.Trim(), "wt.exe");
            if (File.Exists(wtPath))
                return wtPath;
        }
        
        // Check common installation locations
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var possiblePaths = new[]
        {
            Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe"),
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        return null;
    }
    
    /// <summary>
    /// Start a GitHub CLI interactive shell session
    /// This creates a special shell mode focused on GitHub CLI commands
    /// </summary>
    private async Task StartGitHubCliShellAsync()
    {
        try
        {
            var ghPath = FindGhExecutable();
            
            // Check if gh is installed
            try
            {
                var testProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ghPath,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                testProcess.Start();
                var version = await testProcess.StandardOutput.ReadToEndAsync();
                await testProcess.WaitForExitAsync();
                
                if (testProcess.ExitCode != 0)
                {
                    AppendOutput("❌ GitHub CLI (gh) is not installed.{Environment.NewLine}", Color.Parse("#F44747"));
                    AppendOutput("Install with: winget install GitHub.cli{Environment.NewLine}", Color.Parse("#DCDCAA"));
                    return;
                }
                
                AppendOutput($"🐙 GitHub CLI Shell{Environment.NewLine}", Color.Parse("#4EC9B0"));
                AppendOutput($"━━━━━━━━━━━━━━━━━━━━━━━━{Environment.NewLine}", Color.Parse("#858585"));
                AppendOutput($"{version}", Color.Parse("#858585"));
            }
            catch (Exception ex)
            {
                AppendOutput($"❌ GitHub CLI (gh) not found: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
                AppendOutput($"Install with: winget install GitHub.cli{Environment.NewLine}", Color.Parse("#DCDCAA"));
                return;
            }
            
            // Check authentication status
            var authProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ghPath,
                    Arguments = "auth status",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _workingDirectory
                }
            };
            authProcess.Start();
            var authOutput = await authProcess.StandardOutput.ReadToEndAsync();
            var authError = await authProcess.StandardError.ReadToEndAsync();
            await authProcess.WaitForExitAsync();
            
            if (authProcess.ExitCode == 0)
            {
                // Get logged in user
                var userProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ghPath,
                        Arguments = "api user --jq .login",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                userProcess.Start();
                var userName = (await userProcess.StandardOutput.ReadToEndAsync()).Trim();
                await userProcess.WaitForExitAsync();
                
                AppendOutput($"✅ Logged in as: {userName}{Environment.NewLine}", Color.Parse("#4EC9B0"));
            }
            else
            {
                AppendOutput($"⚠️ Not authenticated. Run 'gh auth login' to sign in.{Environment.NewLine}", Color.Parse("#DCDCAA"));
            }
            
            AppendOutput($"Working directory: {_workingDirectory}{Environment.NewLine}", Color.Parse("#858585"));
            AppendOutput($"{Environment.NewLine}", Color.Parse("#858585"));
            AppendOutput($"💡 Available commands: help or type 'gh <command>'{Environment.NewLine}", Color.Parse("#858585"));
            AppendOutput($"   Quick: repo, pr, issue, workflow, release, gist, browse{Environment.NewLine}", Color.Parse("#858585"));
            AppendOutput($"{Environment.NewLine}", Color.Parse("#858585"));
            
            _isRunning = true;
            _isGitHubCliMode = true;
            UpdatePrompt();
            ProcessStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppendOutput($"Error starting GitHub CLI shell: {ex.Message}{Environment.NewLine}", Color.Parse("#F44747"));
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
            // For GitHubCli, we start an interactive PowerShell session with gh prompt customization
            if (ShellType == TerminalShellType.GitHubCli)
            {
                await StartGitHubCliShellAsync();
                return;
            }
            
            // Prefer ConPTY (pseudo console) for interactive sessions.
            if (UsePseudoConsole)
            {
                var exe = GetShellExecutable();
                var args = ShellType switch
                {
                    TerminalShellType.PowerShell => "-NoLogo -NoExit",
                    TerminalShellType.PowerShellCore => "-NoLogo -NoExit",
                    _ => ""
                };

                _conPty = new ConPtyHost(exe, args, _workingDirectory);
                _usingConPty = true;

                _conPty.Output += (_, text) =>
                {
                    // ConPTY gives raw chunks (may include escape sequences).
                    Dispatcher.UIThread.Post(() =>
                    {
                        AppendOutput(text);
                        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text, false));
                    });
                };

                _isRunning = true;
                ProcessStarted?.Invoke(this, EventArgs.Empty);

                AppendOutput($"Interactive {ShellType} session started (ConPTY).{Environment.NewLine}", Color.Parse("#4EC9B0"));
                AppendOutput($"Working directory: {_workingDirectory}{Environment.NewLine}", Color.Parse("#858585"));

                await Task.CompletedTask;
                return;
            }

            // Fallback: old redirected pipes approach
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
            _usingConPty = false;
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
            // If ConPTY is enabled, run the command inside a ConPTY host too (so TTY apps work).
            if (UsePseudoConsole)
            {
                StopCurrentProcess();

                var exe = GetShellExecutable();
                var args = GetShellArguments(command);

                _conPty = new ConPtyHost(exe, args, _workingDirectory);
                _usingConPty = true;

                _conPty.Output += (_, text) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        AppendOutput(text);
                        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text, false));
                    });
                };

                _isRunning = true;
                ProcessStarted?.Invoke(this, EventArgs.Empty);

                await _conPty.WaitForExitAsync();

                _isRunning = false;
                _usingConPty = false;
                _conPty.Dispose();
                _conPty = null;

                ProcessExited?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Old approach
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
            TerminalShellType.GitHubCli => FindGhExecutable(),
            _ => "cmd.exe"
        };
    }
    
    private string FindGhExecutable()
    {
        // Try common locations for GitHub CLI
        var possiblePaths = new[]
        {
            "gh.exe", // In PATH
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\gh\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GitHub CLI\gh.exe")
        };
        
        foreach (var path in possiblePaths)
        {
            if (path == "gh.exe")
            {
                // Check if gh is in PATH
                try
                {
                    var testProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "gh",
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    testProcess.Start();
                    testProcess.WaitForExit(1000);
                    if (testProcess.ExitCode == 0)
                        return "gh.exe";
                }
                catch
                {
                    // Continue to next path
                }
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }
        
        // Fallback to gh.exe (might be in PATH)
        return "gh.exe";
    }
    
    private string GetShellArguments(string command)
    {
        return ShellType switch
        {
            TerminalShellType.PowerShell => $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            TerminalShellType.PowerShellCore => $"-NoProfile -Command \"{command}\"",
            TerminalShellType.Cmd => $"/c {command}",
            TerminalShellType.GitBash => $"-c \"{command}\"",
            TerminalShellType.GitHubCli => command, // Commands are passed directly to gh
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
            if (_usingConPty && _conPty != null)
            {
                _conPty.Kill();
                _conPty.Dispose();
                _conPty = null;
                _usingConPty = false;
                AppendOutput($"{Environment.NewLine}^C Process terminated.{Environment.NewLine}", Color.Parse("#DCDCAA"));
                return;
            }

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
        // Prefer ANSI renderer
        if (_ansiTerminal != null)
        {
            _ansiTerminal.Write(text);
            return;
        }

        // If renderer isn't initialized yet, buffer output.
        _outputBuffer.Append(text);
    }
    
    /// <summary>
    /// Clear terminal output
    /// </summary>
    public void ClearOutput()
    {
        _outputBuffer.Clear();
        _ansiTerminal?.Clear();
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
        _conPty?.Dispose();
        _conPty = null;
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
    GitBash,
    GitHubCli
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

