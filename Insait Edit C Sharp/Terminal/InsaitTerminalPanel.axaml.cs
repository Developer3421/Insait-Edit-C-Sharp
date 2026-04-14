using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Insait_Edit_C_Sharp.Terminal;

/// <summary>
/// Professional terminal panel built on System.Diagnostics.Process with
/// redirected stdin / stdout / stderr.  Supports programmatic command
/// execution (dotnet, cmd, powershell, git, etc.) and returns clean output.
/// Replaces the former Iciclecreek.Avalonia.Terminal wrapper.
/// </summary>
public partial class InsaitTerminalPanel : UserControl
{
    // ── Process ──────────────────────────────────────────────
    private Process? _shellProcess;
    private StreamWriter? _shellInput;
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _isLaunched;
    private CancellationTokenSource? _readCts;

    // ── UI controls ──────────────────────────────────────────
    private TextBox? _outputTextBox;
    private TextBox? _inputTextBox;
    private ScrollViewer? _scrollViewer;
    private TextBlock? _promptLabel;

    // ── Output buffer (thread-safe) ──────────────────────────
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _bufferLock = new();
    private const int MaxOutputLength = 500_000; // ~500 KB visible output

    // ── Command history ──────────────────────────────────────
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    // ── Pending project selection (dotnet auto-discovery) ────
    private List<string>? _pendingProjectSelection;
    private string? _pendingDotnetSubcommand;
    private string? _pendingDotnetArgs;

    /// <summary>Commands that need project auto-discovery.</summary>
    private static readonly HashSet<string> DotnetProjectCommands =
        new(StringComparer.OrdinalIgnoreCase) { "run", "build", "publish", "test", "clean", "watch" };

    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", ".idea", "artifacts" };

    // ── Events (same signature as before) ────────────────────
    public event EventHandler? ShellExited;
    public event EventHandler? ShellStarted;

    // ── ANSI escape‑code stripper ────────────────────────────
    private static readonly Regex AnsiRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))",
        RegexOptions.Compiled);

    // ── Welcome banner ───────────────────────────────────────
    private static string GetWelcomeBanner() =>
        """
        ╔══════════════════════════════════════════════════════════════╗
        ║           ✦  Insait Edit — Integrated Terminal  ✦          ║
        ╠══════════════════════════════════════════════════════════════╣
        ║  Type 'insait help' to see available commands & shortcuts.  ║
        ║  Ready for dotnet, git, cmd and any CLI tools.             ║
        ╚══════════════════════════════════════════════════════════════╝

        """;

    // ── Built-in 'insait help' command output ────────────────
    private static string GetInsaitHelp() =>
        """
        
        ╔══════════════════════════════════════════════════════════════╗
        ║                   Insait Terminal — Help                    ║
        ╠══════════════════════════════════════════════════════════════╣
        ║                                                             ║
        ║  BUILT-IN COMMANDS:                                         ║
        ║    insait help          Show this help message               ║
        ║    insait clear         Clear terminal output                ║
        ║    insait restart       Restart the shell session            ║
        ║    insait version       Show Insait Edit version             ║
        ║    insait welcome       Show welcome banner                  ║
        ║                                                             ║
        ║  KEYBOARD SHORTCUTS:                                        ║
        ║    Enter                Execute command                      ║
        ║    ↑ / ↓                Navigate command history             ║
        ║    Ctrl+C               Stop running process                 ║
        ║    Ctrl+L               Clear terminal                      ║
        ║                                                             ║
        ║  DOTNET CLI — AUTO PROJECT DISCOVERY:                       ║
        ║  ┌──────────────────────────────────────────────────────┐    ║
        ║  │ dotnet run / build / publish / test / clean / watch  │    ║
        ║  │ → auto-finds *.csproj / *.fsproj in working dir     │    ║
        ║  │ → 1 project: runs immediately                       │    ║
        ║  │ → 2+ projects: shows numbered menu to choose        │    ║
        ║  └──────────────────────────────────────────────────────┘    ║
        ║                                                             ║
        ║  DOTNET CLI COMMANDS:                                       ║
        ║    dotnet new <template>       Create a new project          ║
        ║    dotnet build                Build the project             ║
        ║    dotnet build -c Release     Build in Release mode         ║
        ║    dotnet run                  Run the project               ║
        ║    dotnet test                 Run unit tests                ║
        ║    dotnet publish              Publish the application       ║
        ║    dotnet clean                Clean build output            ║
        ║    dotnet restore              Restore NuGet packages        ║
        ║    dotnet watch                Watch mode (hot-reload)       ║
        ║    dotnet --info               Show .NET SDK information     ║
        ║    dotnet ef                   Entity Framework CLI          ║
        ║                                                             ║
        ║  NUGET PACKAGE MANAGEMENT — AUTO PROJECT SELECTION:         ║
        ║  ┌──────────────────────────────────────────────────────┐    ║
        ║  │ dotnet add package <Name>                            │    ║
        ║  │ → auto-selects project if only one found             │    ║
        ║  │ → shows menu if multiple projects in workspace       │    ║
        ║  └──────────────────────────────────────────────────────┘    ║
        ║    dotnet list package         List installed packages       ║
        ║    dotnet remove package <n>   Remove a NuGet package       ║
        ║    dotnet new list             List available templates      ║
        ║    dotnet nuget locals all -c  Clear NuGet cache            ║
        ║                                                             ║
        ║  GIT COMMANDS:                                              ║
        ║    git status                  Show working tree status      ║
        ║    git add .                   Stage all changes             ║
        ║    git commit -m "msg"         Commit staged changes         ║
        ║    git push                    Push to remote                ║
        ║    git pull                    Pull from remote              ║
        ║    git log --oneline -10       Show recent commits           ║
        ║                                                             ║
        ╚══════════════════════════════════════════════════════════════╝

        """;

    // ═════════════════════════════════════════════════════════
    //  Construction
    // ═════════════════════════════════════════════════════════
    public InsaitTerminalPanel()
    {
        InitializeComponent();
        _outputTextBox = this.FindControl<TextBox>("OutputTextBox");
        _inputTextBox  = this.FindControl<TextBox>("InputTextBox");
        _scrollViewer  = this.FindControl<ScrollViewer>("OutputScrollViewer");
        _promptLabel   = this.FindControl<TextBlock>("PromptLabel");
    }

    // ═════════════════════════════════════════════════════════
    //  Public API — full backward compatibility
    // ═════════════════════════════════════════════════════════

    /// <summary>Working directory for the terminal session.</summary>
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set
        {
            if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
            {
                _workingDirectory = value;
                UpdatePrompt();
                if (_isLaunched)
                    _ = ExecuteCommandAsync($"cd /d \"{value}\"");
            }
        }
    }

    public bool IsRunning => _isLaunched;

    // ── Lifecycle ────────────────────────────────────────────

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_isLaunched) await LaunchShellAsync();
        _inputTextBox?.Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        KillShell();
    }

    // ── Launch / Kill ────────────────────────────────────────

    public Task LaunchShellAsync()
    {
        if (_isLaunched) return Task.CompletedTask;
        var (shell, args) = GetPreferredShell();
        return LaunchProcessInternalAsync(shell, string.Join(' ', args));
    }

    public Task LaunchProcessAsync(string process, params string[] args)
    {
        return LaunchProcessInternalAsync(process, string.Join(' ', args));
    }

    private async Task LaunchProcessInternalAsync(string fileName, string arguments)
    {
        try
        {
            KillShell(); // clean up previous

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = _workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // Ensure child process gets a proper console codepage
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

            _shellProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _shellProcess.Exited += OnProcessExited;
            _shellProcess.Start();

            _shellInput = _shellProcess.StandardInput;
            _shellInput.AutoFlush = true;

            _isLaunched = true;
            _readCts = new CancellationTokenSource();

            // Start async readers for stdout & stderr
            _ = ReadStreamAsync(_shellProcess.StandardOutput, _readCts.Token);
            _ = ReadStreamAsync(_shellProcess.StandardError, _readCts.Token);

            AppendOutput(GetWelcomeBanner());
            AppendOutput($"  Shell: {fileName} {arguments}\n");
            AppendOutput($"  Directory: {_workingDirectory}\n\n");
            UpdatePrompt();

            ShellStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppendOutput($"[Insait Terminal] Error launching shell: {ex.Message}\n");
            Debug.WriteLine($"[InsaitTerminalPanel] Error launching shell: {ex.Message}");
        }
    }

    public async Task RestartShellAsync()
    {
        KillShell();
        await Task.Delay(200);
        AppendOutput("\n[Insait Terminal] ── Restarting shell ──\n\n");
        await LaunchShellAsync();
    }

    public void KillShell()
    {
        try
        {
            _readCts?.Cancel();
            _readCts = null;

            if (_shellProcess != null && !_shellProcess.HasExited)
            {
                try { _shellProcess.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
            _shellProcess?.Dispose();
            _shellProcess = null;
            _shellInput = null;
            _isLaunched = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InsaitTerminalPanel] Error killing shell: {ex.Message}");
        }
    }

    // ── Command execution ────────────────────────────────────

    /// <summary>
    /// Execute a command by writing it to the shell's stdin.
    /// </summary>
    public async Task ExecuteCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (!_isLaunched)
        {
            await LaunchShellAsync();
            await Task.Delay(500);
        }
        await SendInputAsync(command + Environment.NewLine);
    }

    /// <summary>
    /// Fire-and-forget wrapper (backward compat).
    /// </summary>
    public void ExecuteCommand(string command) => _ = ExecuteCommandAsync(command);

    /// <summary>
    /// Run a command and capture its full stdout+stderr output as a string.
    /// Useful for programmatic calls like <c>dotnet build</c>.
    /// </summary>
    public async Task<string> RunCommandAndCaptureAsync(string fileName, string arguments,
        string? workingDirectory = null, int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };
        var sb = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var exited = await Task.Run(() => proc.WaitForExit(timeoutMs));
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            sb.AppendLine("[Insait Terminal] Process timed out and was killed.");
        }

        // Also mirror to the visible terminal
        var result = sb.ToString();
        AppendOutput($"\n> {fileName} {arguments}\n{result}\n");
        return result;
    }

    /// <summary>
    /// Shortcut: run a dotnet CLI command and return its output.
    /// Example: <c>await RunDotnetAsync("build -c Release")</c>
    /// </summary>
    public Task<string> RunDotnetAsync(string arguments, string? workingDirectory = null, int timeoutMs = 120_000)
    {
        var dotnet = FindExecutableInPath("dotnet.exe") ?? "dotnet";
        return RunCommandAndCaptureAsync(dotnet, arguments, workingDirectory, timeoutMs);
    }

    public void ChangeDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            _workingDirectory = path;
            UpdatePrompt();
            if (_isLaunched) _ = ExecuteCommandAsync($"cd /d \"{path}\"");
        }
    }

    /// <summary>Send raw text to the shell stdin.</summary>
    public async Task SendInputAsync(string text)
    {
        if (_shellInput == null || string.IsNullOrEmpty(text)) return;
        try
        {
            await _shellInput.WriteAsync(text);
            await _shellInput.FlushAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InsaitTerminalPanel] Error sending input: {ex.Message}");
        }
    }

    public async Task SendCtrlCAsync()
    {
        // Ctrl+C cannot be sent through redirected stdin reliably on Windows.
        // Instead we kill the child process tree.
        StopCurrentProcess();
        await Task.CompletedTask;
    }

    public void ClearTerminal()
    {
        lock (_bufferLock) _outputBuffer.Clear();
        Dispatcher.UIThread.Post(() =>
        {
            if (_outputTextBox != null) _outputTextBox.Text = string.Empty;
        });
    }

    public void StopCurrentProcess()
    {
        if (_shellProcess == null || _shellProcess.HasExited) return;
        try
        {
            // Kill the process tree (child processes like dotnet, msbuild, etc.)
            _shellProcess.Kill(entireProcessTree: true);
            AppendOutput("\n[Insait Terminal] Process stopped.\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InsaitTerminalPanel] Error stopping: {ex.Message}");
        }
    }

    // ── External terminal / GitHub Copilot ───────────────────

    public void OpenExternalTerminal(string? command = null, string? title = null)
    {
        try
        {
            ProcessStartInfo startInfo;
            var wtPath = FindWindowsTerminal();
            if (!string.IsNullOrEmpty(wtPath))
            {
                var wtArgs = $"-d \"{_workingDirectory}\"";
                if (!string.IsNullOrEmpty(title)) wtArgs += $" --title \"{title}\"";
                if (!string.IsNullOrEmpty(command)) wtArgs += $" cmd /k \"{command}\"";
                startInfo = new ProcessStartInfo
                {
                    FileName = wtPath, Arguments = wtArgs,
                    UseShellExecute = true, WorkingDirectory = _workingDirectory
                };
            }
            else
            {
                var cmdArgs = string.IsNullOrEmpty(command) ? "/k" : $"/k \"{command}\"";
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe", Arguments = cmdArgs,
                    UseShellExecute = true, WorkingDirectory = _workingDirectory
                };
            }
            Process.Start(startInfo);
        }
        catch (Exception ex) { Debug.WriteLine($"[InsaitTerminalPanel] Error: {ex.Message}"); }
    }

    public void OpenGitHubCopilotTerminal(string? copilotArgs = null)
    {
        try
        {
            var ghPath = ResolveGhExecutable();
            var ghCommand = string.IsNullOrEmpty(copilotArgs)
                ? $"\"{ghPath}\" copilot"
                : $"\"{ghPath}\" copilot {copilotArgs}";
            ProcessStartInfo startInfo;
            var wtPath = FindWindowsTerminal();
            if (!string.IsNullOrEmpty(wtPath))
                startInfo = new ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{_workingDirectory}\" --title \"GitHub Copilot CLI\" cmd /k {ghCommand}",
                    UseShellExecute = true, WorkingDirectory = _workingDirectory
                };
            else
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k {ghCommand}",
                    UseShellExecute = true, WorkingDirectory = _workingDirectory
                };
            Process.Start(startInfo);
        }
        catch (Exception ex) { Debug.WriteLine($"[InsaitTerminalPanel] Error: {ex.Message}"); }
    }

    public void FocusInput() => _inputTextBox?.Focus();

    // ═════════════════════════════════════════════════════════
    //  Internal helpers
    // ═════════════════════════════════════════════════════════

    /// <summary>Read stdout or stderr asynchronously and append to output.</summary>
    private async Task ReadStreamAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            var buffer = new char[4096];
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break; // stream closed

                var text = new string(buffer, 0, bytesRead);
                var clean = StripAnsiCodes(text);
                AppendOutput(clean);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InsaitTerminalPanel] ReadStream error: {ex.Message}");
        }
    }

    /// <summary>Append text to the output buffer and update the UI TextBox.</summary>
    private void AppendOutput(string text)
    {
        lock (_bufferLock)
        {
            _outputBuffer.Append(text);
            // Trim if too large
            if (_outputBuffer.Length > MaxOutputLength)
            {
                var excess = _outputBuffer.Length - MaxOutputLength;
                _outputBuffer.Remove(0, excess);
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_outputTextBox == null) return;
            string snapshot;
            lock (_bufferLock) { snapshot = _outputBuffer.ToString(); }
            _outputTextBox.Text = snapshot;
            _scrollViewer?.ScrollToEnd();
        });
    }

    private void UpdatePrompt()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_promptLabel != null)
            {
                var dir = _workingDirectory;
                if (dir.Length > 40)
                    dir = "…" + dir[^38..];
                _promptLabel.Text = $"{dir} ❯";
            }
        });
    }

    private static string StripAnsiCodes(string input) => AnsiRegex.Replace(input, string.Empty);

    // ── UI event handlers ────────────────────────────────────

    private async void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var command = _inputTextBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(command))
            {
                _history.Add(command);
                _historyIndex = _history.Count;

                // ── If waiting for project number selection ──
                if (_pendingProjectSelection != null)
                {
                    AppendOutput($"> {command}\n");
                    HandleProjectSelection(command);
                    if (_inputTextBox != null) _inputTextBox.Text = string.Empty;
                    e.Handled = true;
                    return;
                }

                AppendOutput($"{_workingDirectory}> {command}\n");

                // ── Handle built-in 'insait' commands locally ──
                if (TryHandleInsaitCommand(command))
                {
                    // handled internally
                }
                // ── Handle dotnet commands with auto-discovery ──
                else if (command.StartsWith("dotnet ", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    var dotnetArgs = command.Length > 7 ? command[7..].Trim() : "";
                    await HandleDotnetAutoDiscoveryAsync(dotnetArgs);
                }
                else
                {
                    await ExecuteCommandAsync(command);
                }
            }
            if (_inputTextBox != null) _inputTextBox.Text = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_history.Count > 0 && _historyIndex > 0)
            {
                _historyIndex--;
                if (_inputTextBox != null) _inputTextBox.Text = _history[_historyIndex];
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_history.Count > 0 && _historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                if (_inputTextBox != null) _inputTextBox.Text = _history[_historyIndex];
            }
            else
            {
                _historyIndex = _history.Count;
                if (_inputTextBox != null) _inputTextBox.Text = string.Empty;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            StopCurrentProcess();
            e.Handled = true;
        }
        else if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ClearTerminal();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handle built-in 'insait' commands. Returns true if command was handled.
    /// </summary>
    private bool TryHandleInsaitCommand(string command)
    {
        var lower = command.Trim().ToLowerInvariant();

        if (lower == "insait help" || lower == "insait --help" || lower == "insait -h")
        {
            AppendOutput(GetInsaitHelp());
            return true;
        }

        if (lower == "insait clear" || lower == "insait cls")
        {
            ClearTerminal();
            return true;
        }

        if (lower == "insait restart")
        {
            _ = RestartShellAsync();
            return true;
        }

        if (lower == "insait version" || lower == "insait --version" || lower == "insait -v")
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AppendOutput($"\n  Insait Edit v{version?.ToString(3) ?? "1.0.0"}\n\n");
            return true;
        }

        if (lower == "insait welcome")
        {
            AppendOutput(GetWelcomeBanner());
            return true;
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────
    //  .NET auto project discovery  (run / build / publish / test / clean / watch)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Intercepts dotnet commands that need a project file.
    /// If user didn't specify --project, auto-discovers *.csproj / *.fsproj.
    /// One project → runs immediately. Multiple → shows numbered menu.
    /// Also handles <c>dotnet add package</c> with project auto-selection.
    /// </summary>
    private async Task HandleDotnetAutoDiscoveryAsync(string dotnetArgs)
    {
        var argParts = dotnetArgs.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCmd = argParts.Length > 0 ? argParts[0].ToLowerInvariant() : "";
        var extraArgs = argParts.Length > 1 ? argParts[1] : "";

        // ── "dotnet add package <name>" — auto-select project ──
        if (subCmd == "add" && extraArgs.StartsWith("package ", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDotnetAddPackageAsync(extraArgs[8..].Trim());
            return;
        }

        // ── Non-project commands (new, list, nuget, --version, etc.) — run as-is ──
        if (!DotnetProjectCommands.Contains(subCmd) || string.IsNullOrEmpty(subCmd))
        {
            var rawCmd = string.IsNullOrWhiteSpace(dotnetArgs) ? "dotnet --help" : $"dotnet {dotnetArgs}";
            AppendOutput($"  ▸ {rawCmd}\n");
            await ExecuteCommandAsync(rawCmd);
            return;
        }

        // ── User already specified --project → run as-is ──
        if (extraArgs.Contains("--project", StringComparison.OrdinalIgnoreCase) ||
            extraArgs.Contains("-p ", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = $"dotnet {dotnetArgs}";
            AppendOutput($"  ▸ {cmd}\n");
            await ExecuteCommandAsync(cmd);
            return;
        }

        // ── Auto-discover projects ──
        AppendOutput($"  🔍 Searching for projects in {_workingDirectory}...\n");
        var projects = FindDotnetProjects(_workingDirectory);

        if (projects.Count == 0)
        {
            AppendOutput($"  ❌ No .csproj / .fsproj found in {_workingDirectory}\n");
            AppendOutput($"  💡 Hint: cd into a project folder or use --project flag\n\n");
            return;
        }

        if (projects.Count == 1)
        {
            // Single project — auto-run
            var builtCmd = BuildDotnetCommand(subCmd, extraArgs, projects[0]);
            AppendOutput($"  📦 Project: {Path.GetFileName(projects[0])}\n");
            AppendOutput($"  ▸ {builtCmd}\n");
            await ExecuteCommandAsync(builtCmd);
            return;
        }

        // Multiple projects — ask user to choose
        AppendOutput($"\n  📂 Found {projects.Count} projects:\n");
        for (int i = 0; i < projects.Count; i++)
        {
            AppendOutput($"    [{i + 1}] {Path.GetFileName(projects[i])}\n");
            var dir = Path.GetDirectoryName(projects[i]);
            if (!string.IsNullOrEmpty(dir))
                AppendOutput($"        {dir}\n");
        }
        AppendOutput($"    [0] Cancel\n");
        AppendOutput($"  ❯ Enter number (1–{projects.Count}): ");

        _pendingProjectSelection = projects;
        _pendingDotnetSubcommand = subCmd;
        _pendingDotnetArgs = extraArgs;
    }

    /// <summary>
    /// Handle <c>dotnet add package &lt;name&gt;</c> with project auto-selection.
    /// </summary>
    private async Task HandleDotnetAddPackageAsync(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            AppendOutput("  ❌ Usage: dotnet add package <PackageName>\n");
            return;
        }

        var projects = FindDotnetProjects(_workingDirectory);

        if (projects.Count == 0)
        {
            AppendOutput($"  ❌ No .csproj / .fsproj found in {_workingDirectory}\n");
            return;
        }

        string targetProject;
        if (projects.Count == 1)
        {
            targetProject = projects[0];
            AppendOutput($"  📦 Project: {Path.GetFileName(targetProject)}\n");
        }
        else
        {
            // Multiple — show menu and wait (reuse pending selection mechanism)
            AppendOutput($"\n  📂 Found {projects.Count} projects — select target for package '{packageName}':\n");
            for (int i = 0; i < projects.Count; i++)
            {
                AppendOutput($"    [{i + 1}] {Path.GetFileName(projects[i])}\n");
                var dir = Path.GetDirectoryName(projects[i]);
                if (!string.IsNullOrEmpty(dir))
                    AppendOutput($"        {dir}\n");
            }
            AppendOutput($"    [0] Cancel\n");
            AppendOutput($"  ❯ Enter number (1–{projects.Count}): ");

            _pendingProjectSelection = projects;
            _pendingDotnetSubcommand = "add";
            _pendingDotnetArgs = $"package {packageName}";
            return;
        }

        var cmd = $"dotnet add \"{targetProject}\" package {packageName}";
        AppendOutput($"  ▸ {cmd}\n");
        await ExecuteCommandAsync(cmd);
    }

    /// <summary>
    /// Called when the user types a number to select a project after auto-discovery.
    /// </summary>
    private void HandleProjectSelection(string input)
    {
        var projects = _pendingProjectSelection!;
        var subCmd = _pendingDotnetSubcommand!;
        var extraArgs = _pendingDotnetArgs ?? "";

        // Clear state
        _pendingProjectSelection = null;
        _pendingDotnetSubcommand = null;
        _pendingDotnetArgs = null;

        if (!int.TryParse(input.Trim(), out var index))
        {
            AppendOutput("  ❌ Invalid input. Expected a number.\n\n");
            return;
        }

        if (index == 0)
        {
            AppendOutput("  ⊘ Cancelled.\n\n");
            return;
        }

        if (index < 1 || index > projects.Count)
        {
            AppendOutput($"  ❌ Invalid selection. Enter 1–{projects.Count} or 0 to cancel.\n\n");
            return;
        }

        var selectedProject = projects[index - 1];
        string cmd;

        if (subCmd == "add")
        {
            // dotnet add <project> package <name>
            cmd = $"dotnet add \"{selectedProject}\" {extraArgs}";
        }
        else
        {
            cmd = BuildDotnetCommand(subCmd, extraArgs, selectedProject);
        }

        AppendOutput($"  📦 Selected: {Path.GetFileName(selectedProject)}\n");
        AppendOutput($"  ▸ {cmd}\n");
        _ = ExecuteCommandAsync(cmd);
    }

    /// <summary>
    /// Build the final dotnet CLI command string for a given subcommand and project file.
    /// </summary>
    private static string BuildDotnetCommand(string subCmd, string extraArgs, string projectPath)
    {
        // dotnet run / watch use --project flag; build/publish/test/clean take the file directly
        var projectArg = subCmd switch
        {
            "run" => $"--project \"{projectPath}\"",
            "watch" => $"--project \"{projectPath}\"",
            _ => $"\"{projectPath}\""
        };

        return string.IsNullOrWhiteSpace(extraArgs)
            ? $"dotnet {subCmd} {projectArg}"
            : $"dotnet {subCmd} {projectArg} {extraArgs}";
    }

    /// <summary>
    /// Find .csproj and .fsproj files in the given directory tree (skips bin/obj/.git etc.).
    /// </summary>
    private static List<string> FindDotnetProjects(string directory, int maxDepth = 5)
    {
        var projects = new List<string>();
        FindProjectsRecursive(directory, projects, maxDepth, 0);
        return projects;
    }

    private static void FindProjectsRecursive(string dir, List<string> projects, int maxDepth, int depth)
    {
        if (depth > maxDepth) return;
        try
        {
            var dirName = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(dirName) && SkipDirs.Contains(dirName)) return;

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

    private void StopButton_Click(object? sender, RoutedEventArgs e) => StopCurrentProcess();

    // ── Process exit handler ─────────────────────────────────

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _isLaunched = false;
            var exitCode = _shellProcess?.ExitCode;
            AppendOutput($"\n[Insait Terminal] Shell exited (code {exitCode}).\n");
            ShellExited?.Invoke(this, EventArgs.Empty);

            // Auto-restart after 1 second if still visible
            await Task.Delay(1000);
            if (!_isLaunched && IsEffectivelyVisible)
                await LaunchShellAsync();
        });
    }

    // ── Shell / executable discovery ─────────────────────────

    private static (string Shell, string[] Args) GetPreferredShell()
    {
        var pwsh = FindExecutableInPath("pwsh.exe");
        if (!string.IsNullOrEmpty(pwsh))
            return (pwsh, new[] { "-NoLogo", "-NoProfile", "-NoExit" });
        return ("cmd.exe", new[] { "/Q", "/K" });
    }

    private static string? FindWindowsTerminal()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var path in pathEnv.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var wtPath = Path.Combine(path.Trim(), "wt.exe");
            if (File.Exists(wtPath)) return wtPath;
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(candidate)) return candidate;
        return null;
    }

    private static string ResolveGhExecutable()
    {
        var fromSettings = Controls.SettingsPanelControl.ResolveGhExe();
        if (!string.IsNullOrWhiteSpace(fromSettings) &&
            !string.Equals(fromSettings, "gh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fromSettings, "gh.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(fromSettings))
            return fromSettings;
        var inPath = FindExecutableInPath("gh.exe");
        return !string.IsNullOrEmpty(inPath) ? inPath : "gh.exe";
    }

    private static string? FindExecutableInPath(string executableName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var path in pathEnv.Split(Path.PathSeparator))
        {
            var trimmedPath = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedPath)) continue;
            var candidate = Path.Combine(trimmedPath, executableName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

