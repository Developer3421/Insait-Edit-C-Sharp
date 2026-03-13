using Insait_Edit_C_Sharp.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Lightweight debugger: instruments source files with pause-points before each
/// breakpoint line, compiles normally, then communicates with the running process
/// over a named pipe.  No external debug adapter (netcoredbg / DAP) required.
/// </summary>
public sealed class DebugService : IDisposable
{
    private const string HelperFileName = "__insait_debug_helper.cs";

    // ── runtime fields ────────────────────────────────────────────────────────
    private Process?                  _runProcess;
    private NamedPipeServerStream?    _pipeServer;
    private StreamReader?             _pipeReader;
    private StreamWriter?             _pipeWriter;
    private CancellationTokenSource?  _sessionCts;
    private TaskCompletionSource<bool>? _continueTcs;

    private readonly List<string> _instrumentedFiles = new();
    private string?  _helperFilePath;
    private int      _teardownStarted;
    private bool     _disposed;

    // ── events (public API unchanged) ─────────────────────────────────────────
    public event EventHandler<RunOutputEventArgs>?  OutputReceived;
    public event EventHandler<DebugStoppedEventArgs>? Stopped;
    public event EventHandler?                      Continued;
    public event EventHandler?                      SessionStarted;
    public event EventHandler?                      SessionEnded;

    public bool IsDebugging => _runProcess is { HasExited: false };
    public bool IsPaused    { get; private set; }

    public DebugService()
    {
        BreakpointService.BreakpointsChanged += OnBreakpointsChanged;
    }

    // ── Start ─────────────────────────────────────────────────────────────────
    public async Task<RunResult> StartDebuggingAsync(RunConfiguration config)
    {
        if (IsDebugging)
            return new RunResult { Success = false, ErrorMessage = "A debug session is already active." };

        _teardownStarted = 0;
        _instrumentedFiles.Clear();
        _helperFilePath = null;

        try
        {
            OnOutput($"========== Debug Started: {config.Name} ==========\n");
            OnOutput($"Configuration: {config.Configuration}\n");
            OnOutput($"Project: {config.ProjectPath}\n\n");

            var projectDir    = Path.GetDirectoryName(config.ProjectPath) ?? string.Empty;
            var allBreakpoints = BreakpointService.GetAll();
            var hasBreakpoints = allBreakpoints.Count > 0;
            string? pipeName  = null;

            if (hasBreakpoints)
            {
                pipeName = $"insait-{Guid.NewGuid():N}";
                await InstrumentProjectAsync(projectDir, allBreakpoints);
                OnOutput($"Breakpoints instrumented in {_instrumentedFiles.Count} file(s).\n\n");
            }

            BuildResult buildResult;
            try
            {
                var buildService = new BuildService();
                buildService.OutputReceived += (_, e) => OnOutput(e.Output);
                buildResult = await buildService.BuildAsync(config.ProjectPath, config.Configuration);
            }
            finally
            {
                // Always restore source files – whether build succeeded or failed.
                RestoreInstrumentedFiles();
            }

            if (!buildResult.Success)
            {
                OnOutput("\n========== Build Failed ==========\n");
                return new RunResult { Success = false, ErrorMessage = "Build failed.", Output = buildResult.Output };
            }

            var programPath = FindProgramToDebug(config);
            if (string.IsNullOrWhiteSpace(programPath))
                return new RunResult { Success = false, ErrorMessage = "No executable or DLL was found to run." };

            _sessionCts = new CancellationTokenSource();

            if (hasBreakpoints && pipeName != null)
                _ = Task.Run(() => RunPipeServerAsync(pipeName, _sessionCts.Token));

            StartRunProcess(config, programPath, pipeName);

            SessionStarted?.Invoke(this, EventArgs.Empty);
            return new RunResult { Success = true, Output = "Debug session started." };
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Debug start failed: {ex.Message}\n");
            RestoreInstrumentedFiles();
            TearDownSession();
            return new RunResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ── Stop / Continue / Step ────────────────────────────────────────────────
    public Task StopDebuggingAsync()
    {
        _continueTcs?.TrySetResult(true);   // unblock any waiting Hit()
        TearDownSession();
        return Task.CompletedTask;
    }

    public Task ContinueAsync()
    {
        if (!IsPaused) return Task.CompletedTask;
        IsPaused = false;
        _continueTcs?.TrySetResult(true);
        Continued?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    // Step operations all just resume (no real single-stepping without a real debugger).
    public Task StepOverAsync() => ContinueAsync();
    public Task StepIntoAsync() => ContinueAsync();
    public Task StepOutAsync()  => ContinueAsync();

    /// <summary>No-op — breakpoints are collected fresh at each session start.</summary>
    public Task SyncBreakpointsAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BreakpointService.BreakpointsChanged -= OnBreakpointsChanged;
        TearDownSession();
    }

    // ── Source instrumentation ────────────────────────────────────────────────
    private async Task InstrumentProjectAsync(
        string projectDir,
        IReadOnlyDictionary<string, IReadOnlyList<int>> breakpoints)
    {
        // 1. Write the helper class into the project directory.
        _helperFilePath = Path.Combine(projectDir, HelperFileName);
        await File.WriteAllTextAsync(_helperFilePath, BuildHelperSource(), Encoding.UTF8);

        // 2. Inject a Hit() call before every breakpoint line in every relevant .cs file.
        foreach (var (filePath, bpLines) in breakpoints)
        {
            if (!File.Exists(filePath))                                                    continue;
            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))             continue; // skip .axaml, .json, etc.
            if (!IsUnderDirectory(filePath, projectDir))                                   continue;

            // Backup the original file.
            var backupPath = filePath + ".insait-bak";
            File.Copy(filePath, backupPath, overwrite: true);
            _instrumentedFiles.Add(filePath);

            var lines = (await File.ReadAllLinesAsync(filePath, Encoding.UTF8)).ToList();

            // Process in descending line order so earlier insertions don't shift later indices.
            foreach (var bpLine in bpLines.OrderByDescending(l => l))
            {
                var idx = bpLine - 1;   // 1-based → 0-based
                if (idx < 0 || idx >= lines.Count) continue;

                var indent = LeadingWhitespace(lines[idx]);
                // Insert the pause call on the line immediately BEFORE the breakpoint line.
                lines.Insert(idx,
                    $"{indent}global::__InsaitDebugHelper.Hit(@\"{EscapeVerbatim(filePath)}\", {bpLine});");
            }

            await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8);
        }
    }

    private void RestoreInstrumentedFiles()
    {
        foreach (var filePath in _instrumentedFiles)
        {
            try
            {
                var backup = filePath + ".insait-bak";
                if (File.Exists(backup))
                {
                    File.Copy(backup, filePath, overwrite: true);
                    File.Delete(backup);
                }
            }
            catch { /* best-effort */ }
        }
        _instrumentedFiles.Clear();

        try
        {
            if (_helperFilePath != null && File.Exists(_helperFilePath))
                File.Delete(_helperFilePath);
        }
        catch { }

        _helperFilePath = null;
    }

    /// <summary>
    /// Generates the helper class that is compiled into the target project.
    /// It connects to the IDE's named pipe and blocks at each Hit() call until
    /// the IDE sends "CONTINUE".
    /// </summary>
    private static string BuildHelperSource() =>
        """
        // Auto-generated by Insait Edit — DO NOT EDIT OR COMMIT.
        #nullable enable
        using System;
        using System.IO;
        using System.IO.Pipes;
        using System.Text;

        internal static class __InsaitDebugHelper
        {
            private static NamedPipeClientStream? _pipe;
            private static StreamWriter?          _writer;
            private static StreamReader?          _reader;
            private static readonly object        _lock = new object();
            private static bool                   _ok;

            static __InsaitDebugHelper()
            {
                var name = System.Environment.GetEnvironmentVariable("INSAIT_DEBUG_PIPE");
                if (string.IsNullOrEmpty(name)) return;
                try
                {
                    _pipe   = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                    _pipe.Connect(5000);
                    _writer = new StreamWriter(_pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
                    _reader = new StreamReader(_pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                    _ok     = true;
                }
                catch { }
            }

            internal static void Hit(string file, int line)
            {
                if (!_ok || _pipe is not { IsConnected: true }) return;
                lock (_lock)
                {
                    try
                    {
                        _writer!.WriteLine("HIT\t" + file + "\t" + line);
                        _reader!.ReadLine();   // blocks until the IDE replies "CONTINUE"
                    }
                    catch { _ok = false; }
                }
            }
        }
        """;

    // ── Named-pipe server (IDE side) ──────────────────────────────────────────
    private async Task RunPipeServerAsync(string pipeName, CancellationToken ct)
    {
        try
        {
            var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _pipeServer = server;

            await server.WaitForConnectionAsync(ct);

            _pipeReader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
            _pipeWriter = new StreamWriter(server, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

            while (!ct.IsCancellationRequested && server.IsConnected)
            {
                var msg = await _pipeReader.ReadLineAsync(ct);
                if (msg == null) break;

                if (!msg.StartsWith("HIT\t")) continue;

                var parts = msg.Split('\t');
                if (parts.Length < 3 || !int.TryParse(parts[2], out var hitLine)) continue;

                var hitFile = parts[1];
                IsPaused    = true;
                _continueTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                OnOutput($"\n🔴 Breakpoint hit: {Path.GetFileName(hitFile)}:{hitLine}  —  натисніть F5 або F10 щоб продовжити\n");
                Stopped?.Invoke(this, new DebugStoppedEventArgs("breakpoint", hitFile, hitLine, 0, string.Empty));

                // Wait until ContinueAsync() (or StopDebuggingAsync) sets the result.
                try { await _continueTcs.Task.WaitAsync(ct); } catch (OperationCanceledException) { break; }

                IsPaused = false;
                OnOutput("▶️  Виконання продовжено\n");
                try { _pipeWriter.WriteLine("CONTINUE"); } catch { break; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { OnOutput($"[debug pipe] {ex.Message}\n"); }
        finally { TearDownSession(); }
    }

    // ── Process launch ────────────────────────────────────────────────────────
    private void StartRunProcess(RunConfiguration config, string programPath, string? pipeName)
    {
        var isDll = programPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var si    = new ProcessStartInfo
        {
            FileName  = isDll ? SettingsPanelControl.ResolveDotNetExe() : programPath,
            Arguments = isDll
                ? $"\"{programPath}\" {config.CommandLineArguments}"
                : config.CommandLineArguments ?? string.Empty,
            WorkingDirectory       = ResolveWorkingDirectory(config),
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        if (pipeName != null)
            si.Environment["INSAIT_DEBUG_PIPE"] = pipeName;

        foreach (var kv in config.EnvironmentVariables)
            si.Environment[kv.Key] = kv.Value;

        _runProcess = new Process { StartInfo = si, EnableRaisingEvents = true };
        _runProcess.OutputDataReceived += (_, e) => { if (e.Data != null) OnOutput(e.Data + "\n"); };
        _runProcess.ErrorDataReceived  += (_, e) => { if (e.Data != null) OnOutput(e.Data + "\n"); };
        _runProcess.Exited += (s, _) =>
        {
            var code = (s as Process)?.ExitCode;
            OnOutput($"\n========== Process Exited ({code}) ==========\n");
            TearDownSession();
        };

        if (!_runProcess.Start())
            throw new InvalidOperationException("Failed to start the process.");

        _runProcess.BeginOutputReadLine();
        _runProcess.BeginErrorReadLine();
    }

    // ── Tear-down ─────────────────────────────────────────────────────────────
    private void TearDownSession()
    {
        if (Interlocked.Exchange(ref _teardownStarted, 1) != 0)
            return;

        // Unblock any paused Hit() so the process can terminate cleanly.
        _continueTcs?.TrySetCanceled();
        IsPaused = false;

        var cts = Interlocked.Exchange(ref _sessionCts, null);
        cts?.Cancel();

        try { _pipeWriter?.Dispose(); } catch { }
        try { _pipeReader?.Dispose(); } catch { }
        try { _pipeServer?.Dispose(); } catch { }
        _pipeWriter = null;
        _pipeReader = null;
        _pipeServer = null;

        try { if (_runProcess is { HasExited: false }) _runProcess.Kill(entireProcessTree: true); } catch { }
        try { _runProcess?.Dispose(); } catch { }
        _runProcess = null;

        cts?.Dispose();

        // Safety net — should already be clean after the build step.
        RestoreInstrumentedFiles();

        SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string? FindProgramToDebug(RunConfiguration config)
    {
        var projectDirectory = Path.GetDirectoryName(config.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return null;

        var projectName = Path.GetFileNameWithoutExtension(config.ProjectPath);
        var outputRoot  = Path.Combine(projectDirectory, "bin", config.Configuration);
        if (!Directory.Exists(outputRoot))
            return null;

        foreach (var frameworkDirectory in Directory.GetDirectories(outputRoot))
        {
            var exePath = Path.Combine(frameworkDirectory, $"{projectName}.exe");
            if (File.Exists(exePath)) return exePath;

            var dllPath = Path.Combine(frameworkDirectory, $"{projectName}.dll");
            if (File.Exists(dllPath)) return dllPath;

            var publishDir = Path.Combine(frameworkDirectory, "publish");
            if (!Directory.Exists(publishDir)) continue;

            exePath = Path.Combine(publishDir, $"{projectName}.exe");
            if (File.Exists(exePath)) return exePath;

            dllPath = Path.Combine(publishDir, $"{projectName}.dll");
            if (File.Exists(dllPath)) return dllPath;
        }

        return null;
    }

    private static string ResolveWorkingDirectory(RunConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.WorkingDirectory) && Directory.Exists(config.WorkingDirectory))
            return config.WorkingDirectory;

        return Path.GetDirectoryName(config.ProjectPath) ?? Environment.CurrentDirectory;
    }

    private static bool IsUnderDirectory(string filePath, string dirPath)
    {
        var dir  = Path.GetFullPath(dirPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(filePath);
        return file.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    private static string LeadingWhitespace(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i];
    }

    private static string EscapeVerbatim(string s) => s.Replace("\"", "\"\"");

    private void OnBreakpointsChanged(object? sender, BreakpointChangedEventArgs e) { /* collected fresh at session start */ }

    private void OnOutput(string output)
    {
        if (!string.IsNullOrEmpty(output))
            OutputReceived?.Invoke(this, new RunOutputEventArgs(output));
    }
}

public sealed class DebugStoppedEventArgs : EventArgs
{
    public string Reason    { get; }
    public string FilePath  { get; }
    public int    Line      { get; }
    public int    Column    { get; }
    public string FrameName { get; }

    public DebugStoppedEventArgs(string reason, string filePath, int line, int column, string frameName)
    {
        Reason    = reason;
        FilePath  = filePath;
        Line      = line;
        Column    = column;
        FrameName = frameName;
    }
}

