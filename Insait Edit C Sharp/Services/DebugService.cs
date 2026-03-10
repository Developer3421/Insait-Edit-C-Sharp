using Insait_Edit_C_Sharp.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Hosts a real .NET debug adapter session and synchronizes IDE breakpoints with it.
/// Uses the VS Code Debug Adapter Protocol against vsdbg.
/// </summary>
public sealed class DebugService : IDisposable
{
    private static readonly JsonElement EmptyJson = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly string AdapterInstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Insait Edit",
        "debug",
        "vsdbg");

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly HashSet<string> _syncedBreakpointFiles = new(StringComparer.OrdinalIgnoreCase);

    private Process? _adapterProcess;
    private Stream? _adapterStdout;
    private StreamWriter? _adapterStdin;
    private CancellationTokenSource? _sessionCts;
    private TaskCompletionSource<bool>? _initializedTcs;
    private int _nextSequence = 1;
    private int? _activeThreadId;
    private bool _disposed;
    private int _teardownStarted;

    public event EventHandler<RunOutputEventArgs>? OutputReceived;
    public event EventHandler<DebugStoppedEventArgs>? Stopped;
    public event EventHandler? Continued;
    public event EventHandler? SessionStarted;
    public event EventHandler? SessionEnded;

    public bool IsDebugging => _adapterProcess is { HasExited: false };
    public bool IsPaused { get; private set; }

    public DebugService()
    {
        BreakpointService.BreakpointsChanged += OnBreakpointsChanged;
    }

    public async Task<RunResult> StartDebuggingAsync(RunConfiguration config)
    {
        if (IsDebugging)
        {
            return new RunResult
            {
                Success = false,
                ErrorMessage = "A debug session is already active."
            };
        }

        _teardownStarted = 0;

        try
        {
            OnOutput($"========== Debug Started: {config.Name} ==========\n");
            OnOutput($"Configuration: {config.Configuration}\n");
            OnOutput($"Project: {config.ProjectPath}\n\n");

            var buildService = new BuildService();
            buildService.OutputReceived += (_, e) => OnOutput(e.Output);

            var buildResult = await buildService.BuildAsync(config.ProjectPath, config.Configuration);
            if (!buildResult.Success)
            {
                OnOutput("\n========== Build Failed ==========\n");
                return new RunResult
                {
                    Success = false,
                    ErrorMessage = "Build failed",
                    Output = buildResult.Output
                };
            }

            var programPath = FindProgramToDebug(config);
            if (string.IsNullOrWhiteSpace(programPath))
            {
                return new RunResult
                {
                    Success = false,
                    ErrorMessage = "No executable or DLL was found to debug."
                };
            }

            var adapterPath = await EnsureAdapterAsync();
            await StartAdapterProcessAsync(adapterPath);
            await InitializeSessionAsync(config, programPath);

            SessionStarted?.Invoke(this, EventArgs.Empty);
            return new RunResult { Success = true, Output = "Debug session started" };
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Debug start failed: {ex.Message}\n");
            await StopDebuggingAsync();
            return new RunResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task StopDebuggingAsync()
    {
        if (!IsDebugging)
            return;

        try
        {
            await SendRequestAsync("disconnect", new Dictionary<string, object?>
            {
                ["restart"] = false,
                ["terminateDebuggee"] = true
            }, TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore protocol disconnect failures and fall back to process termination.
        }

        TearDownSession();
    }

    public Task ContinueAsync() => SendThreadRequestAsync("continue");
    public Task StepOverAsync() => SendThreadRequestAsync("next");
    public Task StepIntoAsync() => SendThreadRequestAsync("stepIn");
    public Task StepOutAsync() => SendThreadRequestAsync("stepOut");

    public async Task SyncBreakpointsAsync()
    {
        if (!IsDebugging)
            return;

        var allBreakpoints = BreakpointService.GetAll();
        var filesToSync = _syncedBreakpointFiles
            .Union(allBreakpoints.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var filePath in filesToSync)
        {
            var lines = allBreakpoints.TryGetValue(filePath, out var sourceLines)
                ? sourceLines
                : Array.Empty<int>();

            await SendRequestAsync("setBreakpoints", new Dictionary<string, object?>
            {
                ["source"] = new Dictionary<string, object?> { ["path"] = filePath },
                ["breakpoints"] = lines.Select(line => new Dictionary<string, object?> { ["line"] = line }).ToArray(),
                ["sourceModified"] = false
            });
        }

        _syncedBreakpointFiles.Clear();
        foreach (var filePath in allBreakpoints.Keys)
            _syncedBreakpointFiles.Add(filePath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        BreakpointService.BreakpointsChanged -= OnBreakpointsChanged;
        TearDownSession();
        _sendLock.Dispose();
    }

    private async Task InitializeSessionAsync(RunConfiguration config, string programPath)
    {
        _initializedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await SendRequestAsync("initialize", new Dictionary<string, object?>
        {
            ["adapterID"] = "coreclr",
            ["clientID"] = "insait-edit",
            ["clientName"] = "Insait Edit",
            ["pathFormat"] = "path",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["supportsVariableType"] = true,
            ["supportsVariablePaging"] = true,
            ["supportsRunInTerminalRequest"] = false
        });

        var launchRequest = SendRequestAsync("launch", new Dictionary<string, object?>
        {
            ["name"] = config.Name,
            ["type"] = "coreclr",
            ["request"] = "launch",
            ["program"] = programPath,
            ["cwd"] = ResolveWorkingDirectory(config),
            ["args"] = SplitCommandLine(config.CommandLineArguments),
            ["env"] = config.EnvironmentVariables,
            ["stopAtEntry"] = false,
            ["justMyCode"] = true,
            ["console"] = "internalConsole"
        }, TimeSpan.FromSeconds(30));

        await WaitForInitializedAsync();
        await SyncBreakpointsAsync();
        await SendRequestAsync("setExceptionBreakpoints", new Dictionary<string, object?>
        {
            ["filters"] = Array.Empty<string>()
        });
        await SendRequestAsync("configurationDone", new Dictionary<string, object?>());
        await launchRequest;
    }

    private async Task StartAdapterProcessAsync(string adapterPath)
    {
        _sessionCts = new CancellationTokenSource();
        _adapterProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adapterPath,
                Arguments = "--interpreter=vscode",
                WorkingDirectory = Path.GetDirectoryName(adapterPath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        _adapterProcess.Exited += (_, _) => TearDownSession();

        if (!_adapterProcess.Start())
            throw new InvalidOperationException("The debug adapter could not be started.");

        _adapterStdout = _adapterProcess.StandardOutput.BaseStream;
        _adapterStdin = _adapterProcess.StandardInput;
        _adapterStdin.NewLine = "\n";
        _adapterStdin.AutoFlush = true;

        _ = Task.Run(() => ReadAdapterMessagesAsync(_sessionCts.Token));
        _ = Task.Run(() => ReadAdapterStderrAsync(_sessionCts.Token));

        OnOutput($"Using debug adapter: {adapterPath}\n");
    }

    private async Task ReadAdapterMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _adapterStdout != null)
            {
                var payload = await ReadProtocolMessageAsync(_adapterStdout, cancellationToken);
                if (payload == null)
                    break;

                HandleProtocolMessage(payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            OnOutput($"[adapter] {ex.Message}\n");
        }
        finally
        {
            TearDownSession();
        }
    }

    private async Task ReadAdapterStderrAsync(CancellationToken cancellationToken)
    {
        if (_adapterProcess == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _adapterProcess.StandardError.ReadLineAsync(cancellationToken);
                if (line == null)
                    break;

                OnOutput($"[adapter] {line}\n");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            OnOutput($"[adapter] {ex.Message}\n");
        }
    }

    private void HandleProtocolMessage(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var typeProperty))
            return;

        var messageType = typeProperty.GetString();
        if (string.Equals(messageType, "response", StringComparison.OrdinalIgnoreCase))
        {
            HandleResponse(root);
            return;
        }

        if (string.Equals(messageType, "event", StringComparison.OrdinalIgnoreCase))
            HandleEvent(root);
    }

    private void HandleResponse(JsonElement root)
    {
        var requestSequence = root.TryGetProperty("request_seq", out var requestSeqElement)
            ? requestSeqElement.GetInt32()
            : -1;

        if (!_pendingRequests.TryRemove(requestSequence, out var tcs))
            return;

        var isSuccess = root.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
        if (isSuccess)
        {
            var body = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.Clone()
                : EmptyJson;
            tcs.TrySetResult(body);
            return;
        }

        var message = root.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : "Unknown debugger error";
        tcs.TrySetException(new InvalidOperationException(message));
    }

    private void HandleEvent(JsonElement root)
    {
        var eventName = root.TryGetProperty("event", out var eventElement)
            ? eventElement.GetString()
            : string.Empty;

        switch (eventName)
        {
            case "initialized":
                _initializedTcs?.TrySetResult(true);
                break;

            case "output":
                if (root.TryGetProperty("body", out var outputBody) &&
                    outputBody.TryGetProperty("output", out var outputElement))
                {
                    OnOutput(outputElement.GetString() ?? string.Empty);
                }
                break;

            case "continued":
                IsPaused = false;
                Continued?.Invoke(this, EventArgs.Empty);
                break;

            case "stopped":
                _ = Task.Run(() => HandleStoppedEventAsync(root.Clone()));
                break;

            case "exited":
                if (root.TryGetProperty("body", out var exitBody) && exitBody.TryGetProperty("exitCode", out var exitCode))
                    OnOutput($"\n========== Debuggee Exited ({exitCode.GetInt32()}) ==========\n");
                break;

            case "terminated":
                OnOutput("\n========== Debug Session Terminated ==========\n");
                TearDownSession();
                break;
        }
    }

    private async Task HandleStoppedEventAsync(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("body", out var body))
                return;

            var reason = body.TryGetProperty("reason", out var reasonElement)
                ? reasonElement.GetString() ?? "pause"
                : "pause";
            _activeThreadId = body.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetInt32()
                : null;
            IsPaused = true;

            if (_activeThreadId is not int threadId)
            {
                Stopped?.Invoke(this, new DebugStoppedEventArgs(reason, string.Empty, 0, 0, string.Empty));
                return;
            }

            var stackTrace = await SendRequestAsync("stackTrace", new Dictionary<string, object?>
            {
                ["threadId"] = threadId,
                ["startFrame"] = 0,
                ["levels"] = 1
            });

            var topFrame = stackTrace.TryGetProperty("stackFrames", out var frames) && frames.GetArrayLength() > 0
                ? frames[0]
                : EmptyJson;

            var sourcePath = topFrame.TryGetProperty("source", out var source) && source.TryGetProperty("path", out var path)
                ? path.GetString() ?? string.Empty
                : string.Empty;
            var line = topFrame.TryGetProperty("line", out var lineElement) ? lineElement.GetInt32() : 0;
            var column = topFrame.TryGetProperty("column", out var columnElement) ? columnElement.GetInt32() : 0;
            var frameName = topFrame.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;

            Stopped?.Invoke(this, new DebugStoppedEventArgs(reason, sourcePath, line, column, frameName));
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Failed to resolve stop location: {ex.Message}\n");
        }
    }

    private async Task<JsonElement> SendRequestAsync(string command, Dictionary<string, object?> arguments, TimeSpan? timeout = null)
    {
        if (_adapterStdin == null || _sessionCts == null)
            throw new InvalidOperationException("No active debug adapter session.");

        var sequence = Interlocked.Increment(ref _nextSequence);
        var request = new Dictionary<string, object?>
        {
            ["seq"] = sequence,
            ["type"] = "request",
            ["command"] = command,
            ["arguments"] = arguments
        };

        var requestCompletion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[sequence] = requestCompletion;

        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

        await _sendLock.WaitAsync(_sessionCts.Token);
        try
        {
            await _adapterStdin.BaseStream.WriteAsync(header, 0, header.Length, _sessionCts.Token);
            await _adapterStdin.BaseStream.WriteAsync(payload, 0, payload.Length, _sessionCts.Token);
            await _adapterStdin.BaseStream.FlushAsync(_sessionCts.Token);
        }
        finally
        {
            _sendLock.Release();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
        timeoutCts.CancelAfter(timeout ?? RequestTimeout);
        using var registration = timeoutCts.Token.Register(() => requestCompletion.TrySetCanceled(timeoutCts.Token));
        return await requestCompletion.Task;
    }

    private async Task SendThreadRequestAsync(string command)
    {
        if (!IsDebugging)
            return;

        if (_activeThreadId is not int threadId)
            throw new InvalidOperationException("The debugger is not paused on a thread.");

        await SendRequestAsync(command, new Dictionary<string, object?>
        {
            ["threadId"] = threadId
        });
    }

    private async Task WaitForInitializedAsync()
    {
        if (_initializedTcs == null)
            throw new InvalidOperationException("Debugger initialization state was not created.");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var registration = timeoutCts.Token.Register(() => _initializedTcs.TrySetCanceled(timeoutCts.Token));
        await _initializedTcs.Task;
    }

    private static async Task<byte[]?> ReadProtocolMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var headerBuffer = new MemoryStream();
        var rollingHeader = new Queue<byte>(4);

        while (true)
        {
            var nextByte = new byte[1];
            var bytesRead = await stream.ReadAsync(nextByte, 0, 1, cancellationToken);
            if (bytesRead == 0)
                return null;

            headerBuffer.WriteByte(nextByte[0]);
            rollingHeader.Enqueue(nextByte[0]);
            if (rollingHeader.Count > 4)
                rollingHeader.Dequeue();

            if (rollingHeader.Count == 4 && rollingHeader.SequenceEqual(new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' }))
                break;
        }

        var headers = Encoding.ASCII.GetString(headerBuffer.ToArray());
        var contentLengthHeader = headers
            .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        if (contentLengthHeader == null)
            return null;

        var contentLength = int.Parse(contentLengthHeader[(contentLengthHeader.IndexOf(':') + 1)..].Trim());
        var payload = new byte[contentLength];
        var offset = 0;

        while (offset < contentLength)
        {
            var bytesRead = await stream.ReadAsync(payload, offset, contentLength - offset, cancellationToken);
            if (bytesRead == 0)
                throw new EndOfStreamException("Unexpected end of DAP stream.");

            offset += bytesRead;
        }

        return payload;
    }

    private async Task<string> EnsureAdapterAsync()
    {
        var existing = FindAdapterExecutable();
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        Directory.CreateDirectory(AdapterInstallDirectory);
        var scriptPath = Path.Combine(AdapterInstallDirectory, "GetVsDbg.ps1");

        OnOutput("Installing vsdbg for real breakpoints and stepping...\n");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = string.Join(" ",
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-Command",
                    QuotePowerShellCommand($"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing https://aka.ms/getvsdbgps1 -OutFile '{EscapePowerShellPath(scriptPath)}'; & '{EscapePowerShellPath(scriptPath)}' -Version latest -RuntimeID win-x64 -InstallPath '{EscapePowerShellPath(AdapterInstallDirectory)}'")),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                OnOutput($"{e.Data}\n");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                OnOutput($"[installer] {e.Data}\n");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"vsdbg installation failed with exit code {process.ExitCode}.");

        var installed = FindAdapterExecutable();
        if (string.IsNullOrWhiteSpace(installed))
            throw new FileNotFoundException("vsdbg was installed, but the executable was not found.");

        return installed;
    }

    private static string? FindAdapterExecutable()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("VSDBG_PATH"),
            Path.Combine(AdapterInstallDirectory, "vsdbg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vsdbg", "vsdbg.exe")
        };

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }

    private static string? FindProgramToDebug(RunConfiguration config)
    {
        var projectDirectory = Path.GetDirectoryName(config.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return null;

        var projectName = Path.GetFileNameWithoutExtension(config.ProjectPath);
        var outputRoot = Path.Combine(projectDirectory, "bin", config.Configuration);
        if (!Directory.Exists(outputRoot))
            return null;

        foreach (var frameworkDirectory in Directory.GetDirectories(outputRoot))
        {
            var exePath = Path.Combine(frameworkDirectory, $"{projectName}.exe");
            if (File.Exists(exePath))
                return exePath;

            var dllPath = Path.Combine(frameworkDirectory, $"{projectName}.dll");
            if (File.Exists(dllPath))
                return dllPath;

            var publishDirectory = Path.Combine(frameworkDirectory, "publish");
            if (!Directory.Exists(publishDirectory))
                continue;

            exePath = Path.Combine(publishDirectory, $"{projectName}.exe");
            if (File.Exists(exePath))
                return exePath;

            dllPath = Path.Combine(publishDirectory, $"{projectName}.dll");
            if (File.Exists(dllPath))
                return dllPath;
        }

        return null;
    }

    private static string ResolveWorkingDirectory(RunConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.WorkingDirectory) && Directory.Exists(config.WorkingDirectory))
            return config.WorkingDirectory;

        return Path.GetDirectoryName(config.ProjectPath) ?? Environment.CurrentDirectory;
    }

    private static string[] SplitCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return Array.Empty<string>();

        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private void OnBreakpointsChanged(object? sender, BreakpointChangedEventArgs e)
    {
        if (!IsDebugging)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await SyncBreakpointsAsync();
            }
            catch (Exception ex)
            {
                OnOutput($"[ERROR] Breakpoint sync failed: {ex.Message}\n");
            }
        });
    }

    private void OnOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return;

        OutputReceived?.Invoke(this, new RunOutputEventArgs(output));
    }

    private void TearDownSession()
    {
        if (Interlocked.Exchange(ref _teardownStarted, 1) != 0)
            return;

        var sessionCts = Interlocked.Exchange(ref _sessionCts, null);
        if (sessionCts != null && !sessionCts.IsCancellationRequested)
            sessionCts.Cancel();

        foreach (var pending in _pendingRequests.ToArray())
        {
            if (_pendingRequests.TryRemove(pending.Key, out var tcs))
                tcs.TrySetCanceled();
        }

        _activeThreadId = null;
        IsPaused = false;
        _syncedBreakpointFiles.Clear();

        try
        {
            _adapterStdin?.Dispose();
            _adapterStdout?.Dispose();
        }
        catch
        {
        }

        _adapterStdin = null;
        _adapterStdout = null;

        try
        {
            if (_adapterProcess is { HasExited: false })
                _adapterProcess.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            _adapterProcess?.Dispose();
        }
        catch
        {
        }

        _adapterProcess = null;
        sessionCts?.Dispose();
        SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    private static string EscapePowerShellPath(string path) => path.Replace("'", "''");

    private static string QuotePowerShellCommand(string command) => $"\"{command.Replace("\"", "`\"")}\"";
}

public sealed class DebugStoppedEventArgs : EventArgs
{
    public string Reason { get; }
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string FrameName { get; }

    public DebugStoppedEventArgs(string reason, string filePath, int line, int column, string frameName)
    {
        Reason = reason;
        FilePath = filePath;
        Line = line;
        Column = column;
        FrameName = frameName;
    }
}