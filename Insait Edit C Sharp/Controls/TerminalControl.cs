using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Services;

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
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // .NET project auto-discovery: pending project selection state
    private List<string>? _pendingProjectSelection;
    private string? _pendingDotnetSubcommand;
    private string? _pendingDotnetArgs;
    
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
        AppendOutput($"{L("Terminal.Welcome.Version")}{Environment.NewLine}");
        AppendOutput($"{L("Terminal.Welcome.Hint")}{Environment.NewLine}{Environment.NewLine}");
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

        // Handle pending .NET project selection
        if (_pendingProjectSelection != null)
        {
            HandleProjectSelection(command.Trim());
            return;
        }

        // Add to history
        _inputHistory.Enqueue(command);
        _historyIndex = -1;

        // Show command in output
        AppendOutput($"> {command}{Environment.NewLine}", Color.Parse("#569CD6"));

        // Handle built-in commands
        if (HandleBuiltInCommand(command)) return;

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
                AppendOutput($"{L("Terminal.Msg.SwitchedToPS")}{Environment.NewLine}", Color.Parse("#4EC9B0"));
                return true;
                
            case "cmd":
                ShellType = TerminalShellType.Cmd;
                AppendOutput($"{L("Terminal.Msg.SwitchedToCmd")}{Environment.NewLine}", Color.Parse("#4EC9B0"));
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
                        AppendOutput($"{L("Terminal.Msg.PathNotFound")}{Environment.NewLine}", Color.Parse("#F44747"));
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
            
            case "dotnet":
            {
                var dotnetArgs = parts.Length > 1 ? parts[1] : "";
                _ = HandleDotnetBuiltInAsync(dotnetArgs);
                return true;
            }
                
            default:
                return false;
        }
    }
    
    private void ShowHelp()
    {
        var title    = L("Terminal.Help.Title");
        var builtin  = L("Terminal.Help.BuiltinSection");
        var dotnet   = L("Terminal.Help.DotnetSection");
        var shortcuts= L("Terminal.Help.ShortcutsSection");
        var tip1     = L("Terminal.Help.DotnetTip");
        var tip2     = L("Terminal.Help.DotnetTip2");

        var help = $@"
╔══════════════════════════════════════════════════════════════════╗
║  {title,-64}║
╠══════════════════════════════════════════════════════════════════╣
║  {builtin,-64}║
║  ─────────────────                                                ║
║  cls, clear    - Clear terminal output                            ║
║  help          - Show this help                                   ║
║  admin         - Start new administrator shell                    ║
║  powershell,ps - Switch to PowerShell mode                        ║
║  cmd           - Switch to CMD mode                               ║
║  exit          - Stop current process                             ║
║  pwd           - Print working directory                          ║
║  cd <path>     - Change directory                                 ║
║  terminal, wt  - Open external Windows Terminal                   ║
║  copilot       - Open GitHub Copilot CLI in external terminal     ║
║                                                                   ║
║  {dotnet,-64}║
║  ────────────────────────────────────────                         ║
║  dotnet run              - Build & run project                    ║
║  dotnet run --config R   - Run with Release config                ║
║  dotnet build            - Build project                          ║
║  dotnet publish          - Publish project                        ║
║  dotnet test             - Run tests                              ║
║  dotnet clean            - Clean build output                     ║
║  dotnet restore          - Restore NuGet packages                 ║
║  dotnet watch run        - Run with hot reload                    ║
║  dotnet new <template>   - Create new project                     ║
║  dotnet --version        - Show .NET version                      ║
║  dotnet --info           - Show .NET info                         ║
║                                                                   ║
║  {tip1,-64}║
║  {tip2,-64}║
║                                                                   ║
║  {shortcuts,-64}║
║  ──────────────────                                               ║
║  ↑/↓           - Navigate command history                         ║
║  Ctrl+C        - Stop current process                             ║
║  Enter         - Execute command                                  ║
╚══════════════════════════════════════════════════════════════════╝

";
        AppendOutput(help, Color.Parse("#4EC9B0"));
    }
    
    // ─────────────────────────────────────────────────────────
    //  .NET Command handling with auto project discovery
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Commands that need project auto-discovery (when no --project flag given).
    /// </summary>
    private static readonly HashSet<string> _dotnetProjectCommands =
        new(StringComparer.OrdinalIgnoreCase) { "run", "build", "publish", "test", "clean", "watch" };

    private async Task HandleDotnetBuiltInAsync(string dotnetArgs)
    {
        var argParts = dotnetArgs.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCmd = argParts.Length > 0 ? argParts[0].ToLowerInvariant() : "";
        var extraArgs = argParts.Length > 1 ? argParts[1] : "";

        // For non-project commands (new, list, nuget, --version, --info, etc.) — run as-is
        if (!_dotnetProjectCommands.Contains(subCmd) || string.IsNullOrEmpty(subCmd))
        {
            var rawCmd = string.IsNullOrWhiteSpace(dotnetArgs)
                ? "dotnet --help"
                : $"dotnet {dotnetArgs}";
            AppendOutput(string.Format(L("Terminal.Dotnet.Running"), rawCmd) + Environment.NewLine, Color.Parse("#858585"));
            await ExecuteExternalCommandAsync(rawCmd);
            return;
        }

        // If the user already specified --project, just run
        if (extraArgs.Contains("--project", StringComparison.OrdinalIgnoreCase) ||
            extraArgs.Contains("-p ", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = $"dotnet {dotnetArgs}";
            AppendOutput(string.Format(L("Terminal.Dotnet.Running"), cmd) + Environment.NewLine, Color.Parse("#858585"));
            await ExecuteExternalCommandAsync(cmd);
            return;
        }

        // Auto-discover projects
        AppendOutput(string.Format(L("Terminal.Dotnet.Searching"), _workingDirectory) + Environment.NewLine, Color.Parse("#858585"));
        var projects = FindDotnetProjects(_workingDirectory);

        if (projects.Count == 0)
        {
            AppendOutput(string.Format(L("Terminal.Dotnet.NoneFound"), _workingDirectory) + Environment.NewLine, Color.Parse("#F44747"));
            AppendOutput($"{L("Terminal.Dotnet.NoneFoundHint")}{Environment.NewLine}", Color.Parse("#858585"));
            return;
        }

        if (projects.Count == 1)
        {
            var builtCmd = BuildDotnetCommand(subCmd, extraArgs, projects[0]);
            AppendOutput(string.Format(L("Terminal.Dotnet.Project"), Path.GetFileName(projects[0])) + Environment.NewLine, Color.Parse("#4EC9B0"));
            AppendOutput(string.Format(L("Terminal.Dotnet.Running"), builtCmd) + Environment.NewLine, Color.Parse("#858585"));
            await ExecuteExternalCommandAsync(builtCmd);
            return;
        }

        // Multiple projects — ask user
        AppendOutput($"{Environment.NewLine}{string.Format(L("Terminal.Dotnet.MultipleFound"), projects.Count)}{Environment.NewLine}", Color.Parse("#DCDCAA"));
        for (int i = 0; i < projects.Count; i++)
        {
            AppendOutput($"  [{i + 1}] {Path.GetFileName(projects[i])}{Environment.NewLine}", Color.Parse("#9CDCFE"));
            var dir = Path.GetDirectoryName(projects[i]);
            if (!string.IsNullOrEmpty(dir))
                AppendOutput($"      {dir}{Environment.NewLine}", Color.Parse("#858585"));
        }
        AppendOutput($"  [0] {L("Terminal.Msg.Cancelled").TrimEnd('.')}{Environment.NewLine}", Color.Parse("#858585"));
        AppendOutput(string.Format(L("Terminal.Dotnet.EnterNumber"), projects.Count), Color.Parse("#569CD6"));

        _pendingProjectSelection = projects;
        _pendingDotnetSubcommand = subCmd;
        _pendingDotnetArgs = extraArgs;
    }

    /// <summary>
    /// Called when user types a number to select a project after auto-discovery.
    /// </summary>
    private void HandleProjectSelection(string input)
    {
        var projects = _pendingProjectSelection!;
        var subCmd = _pendingDotnetSubcommand!;
        var extraArgs = _pendingDotnetArgs ?? "";

        // Clear state first
        _pendingProjectSelection = null;
        _pendingDotnetSubcommand = null;
        _pendingDotnetArgs = null;

        // Echo the user input
        AppendOutput($"{input}{Environment.NewLine}", Color.Parse("#569CD6"));

        if (!int.TryParse(input.Trim(), out var index))
        {
            AppendOutput($"{L("Terminal.Msg.InvalidNumber")}{Environment.NewLine}", Color.Parse("#F44747"));
            return;
        }

        if (index == 0)
        {
            AppendOutput($"{L("Terminal.Msg.Cancelled")}{Environment.NewLine}", Color.Parse("#858585"));
            return;
        }

        if (index < 1 || index > projects.Count)
        {
            AppendOutput(string.Format(L("Terminal.Dotnet.InvalidSelection"), projects.Count) + Environment.NewLine, Color.Parse("#F44747"));
            return;
        }

        var selectedProject = projects[index - 1];
        var cmd = BuildDotnetCommand(subCmd, extraArgs, selectedProject);
        AppendOutput(string.Format(L("Terminal.Dotnet.Selected"), Path.GetFileName(selectedProject)) + Environment.NewLine, Color.Parse("#4EC9B0"));
        AppendOutput(string.Format(L("Terminal.Dotnet.Running"), cmd) + Environment.NewLine, Color.Parse("#858585"));
        _ = ExecuteExternalCommandAsync(cmd);
    }

    /// <summary>
    /// Find .csproj and .fsproj files in the given directory tree (skips build artefact dirs).
    /// </summary>
    private static List<string> FindDotnetProjects(string directory, int maxDepth = 5)
    {
        var projects = new List<string>();
        FindProjectsRecursive(directory, projects, maxDepth, 0);
        return projects;
    }

    private static readonly HashSet<string> _skipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", ".idea" };

    private static void FindProjectsRecursive(string dir, List<string> projects, int maxDepth, int depth)
    {
        if (depth > maxDepth) return;
        try
        {
            var dirName = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(dirName) && _skipDirs.Contains(dirName)) return;

            foreach (var ext in new[] { "*.csproj", "*.fsproj" })
                projects.AddRange(Directory.GetFiles(dir, ext, SearchOption.TopDirectoryOnly));

            foreach (var sub in Directory.GetDirectories(dir))
                FindProjectsRecursive(sub, projects, maxDepth, depth + 1);
        }
        catch
        {
            // Ignore access / IO errors
        }
    }

    /// <summary>
    /// Build the final dotnet CLI command string for a given subcommand and project file.
    /// </summary>
    private static string BuildDotnetCommand(string subCmd, string extraArgs, string projectPath)
    {
        // `dotnet run` and `dotnet watch` use --project flag; others take the file/dir directly.
        var projectArg = subCmd switch
        {
            "run"   => $"--project \"{projectPath}\"",
            "watch" => $"--project \"{projectPath}\"",
            _       => $"\"{projectPath}\""
        };

        return string.IsNullOrWhiteSpace(extraArgs)
            ? $"dotnet {subCmd} {projectArg}"
            : $"dotnet {subCmd} {projectArg} {extraArgs}";
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
            AppendOutput($"{L("Terminal.Msg.AdminStarted")}{Environment.NewLine}", Color.Parse("#4EC9B0"));
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
        var fromSettings = SettingsPanelControl.ResolveGhExe();
        if (!string.IsNullOrWhiteSpace(fromSettings) &&
            !string.Equals(fromSettings, "gh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fromSettings, "gh.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(fromSettings))
        {
            return fromSettings;
        }

        return FindExecutableInPath("gh.exe");
    }

    private static string? FindExecutableInPath(string executableName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var path in pathEnv.Split(Path.PathSeparator))
        {
            var trimmedPath = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedPath))
                continue;

            var candidate = Path.Combine(trimmedPath, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        return new[]
        {
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!.Trim().Trim('"'))
        .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveGitBashExecutable()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = GetProgramFilesRoots()
            .SelectMany(root => new[]
            {
                Path.Combine(root, "Git", "bin", "bash.exe"),
                Path.Combine(root, "Git", "usr", "bin", "bash.exe")
            })
            .Concat(new[]
            {
                Path.Combine(localAppData, "Programs", "Git", "bin", "bash.exe"),
                Path.Combine(baseDirectory, "tools", "git", "bin", "bash.exe"),
                Path.Combine(baseDirectory, "tools", "git", "usr", "bin", "bash.exe")
            });

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var bashInPath = FindExecutableInPath("bash.exe");
        return !string.IsNullOrWhiteSpace(bashInPath) ? bashInPath : "bash.exe";
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
    /// Start a persistent interactive shell session
    /// </summary>
    public async Task StartInteractiveShellAsync()
    {
        if (_isRunning) return;
        
        try
        {
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
            TerminalShellType.GitBash => ResolveGitBashExecutable(),
            _ => "cmd.exe"
        };
    }
    
    private string FindGhExecutable()
    {
        var fullPath = ResolveFullGhPath();
        if (!string.IsNullOrWhiteSpace(fullPath))
            return fullPath;

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
                AppendOutput($"{Environment.NewLine}{L("Terminal.Msg.ProcessTerminated")}{Environment.NewLine}", Color.Parse("#DCDCAA"));
                return;
            }

            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                AppendOutput($"{Environment.NewLine}{L("Terminal.Msg.ProcessTerminated")}{Environment.NewLine}", Color.Parse("#DCDCAA"));
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

    /// <summary>
    /// Shortcut for LocalizationService.Get — falls back to the key itself if not found.
    /// </summary>
    private static string L(string key) => LocalizationService.Get(key);
    
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

