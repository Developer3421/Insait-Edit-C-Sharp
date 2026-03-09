using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Professional GitHub Copilot Chat service using the official GitHub.Copilot.SDK.
/// Features: streaming, model selection, file attachments, abort, session management,
/// infinite sessions, hooks, and persisted settings.
/// </summary>
public sealed class CopilotSdkService : IAsyncDisposable, IDisposable
{
    // ── Internals ────────────────────────────────────────────────────────
    private CopilotClient? _client;
    private CopilotSession? _session;
    private bool _initialized;
    private bool _disposed;
    private CancellationTokenSource? _abortCts;

    // ── Configuration ────────────────────────────────────────────────────
    private string _model = "gpt-4o";
    private bool _streamingEnabled = true;
    private string _reasoningEffort = "medium";
    private string? _customSystemMessage;

    // ── Events ───────────────────────────────────────────────────────────

    /// <summary>Connection / status text changes.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>Fired for each streaming content chunk (delta text).</summary>
    public event EventHandler<string>? StreamingTokenReceived;

    /// <summary>Fired for each streaming reasoning chunk.</summary>
    public event EventHandler<string>? ReasoningTokenReceived;

    /// <summary>Fired when a tool execution starts (tool name).</summary>
    public event EventHandler<string>? ToolExecutionStarted;

    /// <summary>Fired when a tool execution completes (tool name).</summary>
    public event EventHandler<string>? ToolExecutionCompleted;

    /// <summary>Fired when session compaction starts/completes.</summary>
    public event EventHandler<string>? CompactionEvent;

    /// <summary>Fired on session errors.</summary>
    public event EventHandler<string>? ErrorOccurred;

    // ── Public state ─────────────────────────────────────────────────────

    public bool IsAvailable => _initialized && _session != null;
    public bool IsProcessing { get; private set; }
    public string CurrentModel => _model;
    public bool IsStreamingEnabled => _streamingEnabled;
    public string ReasoningEffort => _reasoningEffort;
    public string? CurrentSessionId => _session?.SessionId;

    /// <summary>Known models available for selection.</summary>
    public static IReadOnlyList<CopilotModelInfo> AvailableModels { get; } = new List<CopilotModelInfo>
    {
        new("gpt-4o",             "GPT-4o",              "Fast & capable"),
        new("gpt-5",              "GPT-5",               "Most capable model"),
        new("o3",                 "o3",                   "Advanced reasoning"),
        new("o4-mini",            "o4-mini",              "Fast reasoning"),
        new("claude-sonnet-4.5",  "Claude Sonnet 4.5",   "Anthropic's best"),
        new("claude-sonnet-4",    "Claude Sonnet 4",     "Balanced Anthropic"),
        new("gemini-2.5-pro",     "Gemini 2.5 Pro",      "Google's flagship"),
    };

    // ── Configuration setters ────────────────────────────────────────────

    /// <summary>Set the model to use. Takes effect on next session creation.</summary>
    public void SetModel(string model)
    {
        _model = model;
        SettingsDbService.SaveSetting("copilot_model", model);
    }

    /// <summary>Enable or disable streaming. Takes effect on next session.</summary>
    public void SetStreamingEnabled(bool enabled)
    {
        _streamingEnabled = enabled;
        SettingsDbService.SaveSetting("copilot_streaming", enabled.ToString());
    }

    /// <summary>Set reasoning effort: low, medium, high.</summary>
    public void SetReasoningEffort(string effort)
    {
        _reasoningEffort = effort;
        SettingsDbService.SaveSetting("copilot_reasoning_effort", effort);
    }

    /// <summary>Set custom system message (null to use default).</summary>
    public void SetCustomSystemMessage(string? message)
    {
        _customSystemMessage = message;
        if (message != null)
            SettingsDbService.SaveSetting("copilot_system_message", message);
    }

    /// <summary>Load persisted settings from SettingsDbService.</summary>
    public void LoadSettings()
    {
        _model = SettingsDbService.LoadSetting("copilot_model") ?? "gpt-4o";
        _streamingEnabled = SettingsDbService.LoadSetting("copilot_streaming") != "False";
        _reasoningEffort = SettingsDbService.LoadSetting("copilot_reasoning_effort") ?? "medium";
        _customSystemMessage = SettingsDbService.LoadSetting("copilot_system_message");
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    /// <summary>
    /// Initialize the Copilot client and create a session.
    /// Safe to call multiple times.
    /// Resolves gh.exe path from Settings panel paths and injects into PATH.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized && _session != null) return true;

        // Allow re-initialization after a previous failure
        _initialized = false;

        try
        {
            StatusChanged?.Invoke(this, "🔄 Connecting to GitHub Copilot...");

            // Resolve gh.exe from Settings and inject into PATH
            var ghResolved = await EnsureGhInPathAsync();
            if (ghResolved != null)
                StatusChanged?.Invoke(this, $"🔍 gh: {ghResolved}");
            else
                StatusChanged?.Invoke(this, "⚠️ gh.exe not found — set path in Settings → GitHub CLI Path");

            _client = new CopilotClient();
            await CreateNewSessionAsync(ct);

            _initialized = true;
            StatusChanged?.Invoke(this, $"✅ GitHub Copilot connected — model: {_model}");
            return true;
        }
        catch (Exception ex)
        {
            _initialized = true; // mark attempted
            _session = null;

            // Build detailed error message
            var savedGh = SettingsPanelControl.GetGitHubCliPath();
            var savedCop = SettingsPanelControl.GetCopilotCliPath();
            var details = string.Empty;
            if (string.IsNullOrWhiteSpace(savedGh))
            {
                details += "GitHub CLI path is NOT set in Settings.";
            }
            else
            {
                details += $"Saved gh path: {savedGh} (exists: {File.Exists(savedGh)})";
            }
            if (!string.IsNullOrWhiteSpace(savedCop))
            {
                details += $"\nSaved copilot path: {savedCop} (exists: {File.Exists(savedCop)})";
            }

            var msg = $"⚠️ Copilot: {ex.Message}\n{details}";
            StatusChanged?.Invoke(this, msg);
            ErrorOccurred?.Invoke(this, msg);
            return false;
        }
    }

    /// <summary>
    /// Resolve gh.exe using the Settings panel path and well-known locations,
    /// then inject its directory (and the gh-copilot extension directory) into
    /// the current process PATH so the Copilot SDK can find everything it needs.
    /// </summary>
    private static async Task<string?> EnsureGhInPathAsync()
    {
        var ghPath = FindGhFullPath();
        if (string.IsNullOrEmpty(ghPath))
        {
            // Fallback: try PATH lookup via `where`
            ghPath = await FindGhViaWhereAsync();
        }

        if (string.IsNullOrEmpty(ghPath))
        {
            Debug.WriteLine("[CopilotSdk] gh.exe not found anywhere");
            return null;
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";

        // 1) Add gh.exe directory to PATH
        var ghDir = Path.GetDirectoryName(ghPath);
        if (!string.IsNullOrEmpty(ghDir))
        {
            AddToPathIfMissing(ref currentPath, ghDir);
        }

        // 2) Add gh extensions directory to PATH (where gh-copilot agent lives)
        //    Standard location: %USERPROFILE%\.local\share\gh\extensions\gh-copilot
        //    Also check:        %APPDATA%\GitHub CLI\extensions\gh-copilot
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var extensionDirs = new[]
        {
            Path.Combine(userProfile, @".local\share\gh\extensions\gh-copilot"),
            Path.Combine(appData, @"GitHub CLI\extensions\gh-copilot"),
            Path.Combine(userProfile, @".local\share\gh\extensions"),
            Path.Combine(appData, @"GitHub CLI\extensions"),
        };

        foreach (var extDir in extensionDirs)
        {
            if (Directory.Exists(extDir))
            {
                AddToPathIfMissing(ref currentPath, extDir);
                Debug.WriteLine($"[CopilotSdk] Added extension dir to PATH: {extDir}");
            }
        }

        // 3) Apply the updated PATH
        Environment.SetEnvironmentVariable("PATH", currentPath);

        // 4) Also set GH_PATH env var — some SDK builds look at this
        Environment.SetEnvironmentVariable("GH_PATH", ghPath);

        // 5) In the new Copilot CLI world we may also need the standalone
        //    "copilot.exe" path so that other components or tools can use it.
        //    Resolve and inject it into PATH if found.
        var copilotPath = FindCopilotCliPath();
        if (!string.IsNullOrEmpty(copilotPath))
        {
            var copilotDir = Path.GetDirectoryName(copilotPath);
            if (!string.IsNullOrEmpty(copilotDir))
            {
                AddToPathIfMissing(ref currentPath, copilotDir);
            }

            Environment.SetEnvironmentVariable("COPILOT_CLI_PATH", copilotPath);
            Debug.WriteLine($"[CopilotSdk] copilot CLI resolved to: {copilotPath}");
        }

        Debug.WriteLine($"[CopilotSdk] gh resolved to: {ghPath}");
        return ghPath;
    }

    /// <summary>Add a directory to the PATH string if it's not already there.</summary>
    private static void AddToPathIfMissing(ref string currentPath, string dir)
    {
        if (!currentPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
        {
            currentPath = dir + ";" + currentPath;
        }
    }

    /// <summary>
    /// Find gh.exe synchronously using:
    /// 1. Settings panel saved path (GetGitHubCliPath — the exact saved path)
    /// 2. ResolveGhExe (which also checks File.Exists)
    /// 3. Well-known installation directories
    /// </summary>
    private static string? FindGhFullPath()
    {
        // 1) Exact saved path from Settings → GitHub CLI Path
        var savedPath = SettingsPanelControl.GetGitHubCliPath();
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            // If saved path points directly to gh.exe
            if (File.Exists(savedPath))
                return savedPath;

            // If saved path is a directory, look for gh.exe inside
            if (Directory.Exists(savedPath))
            {
                var ghInDir = Path.Combine(savedPath, "gh.exe");
                if (File.Exists(ghInDir)) return ghInDir;
            }

            // If saved path is the CLI folder (e.g. "C:\Program Files\GitHub CLI")
            // but user saved it without \gh.exe
            var withExe = savedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? savedPath
                : Path.Combine(savedPath, "gh.exe");
            if (File.Exists(withExe))
                return withExe;
        }

        // 2) ResolveGhExe (checks saved path + falls back to "gh")
        var resolved = SettingsPanelControl.ResolveGhExe();
        if (resolved != "gh" && File.Exists(resolved))
            return resolved;

        // 3) Well-known paths
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GitHub CLI\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"GitHub CLI\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\gh\gh.exe"),
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"scoop\shims\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"chocolatey\bin\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links\gh.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                Debug.WriteLine($"[CopilotSdk] Found gh at known path: {candidate}");
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Find gh.exe via `where` command (async PATH lookup).</summary>
    private static async Task<string?> FindGhViaWhereAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "gh.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0)
                {
                    var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                          .FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                        return firstLine;
                }
            }
        }
        catch { /* where.exe failed */ }

        return null;
    }

    /// <summary>
    /// Try to locate the standalone Copilot CLI binary (copilot.exe).
    /// This is separate from the gh-copilot extension and may be installed
    /// in its own directories or provided via an environment variable.
    /// </summary>
    private static string? FindCopilotCliPath()
    {
        // 1) respect environment variable if provided
        var env = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (File.Exists(env))
                return env;
            if (Directory.Exists(env))
            {
                var inside = Path.Combine(env, "copilot.exe");
                if (File.Exists(inside))
                    return inside;
            }
        }

        // 2) common installation locations
        var candidates = new[]
        {
            @"C:\Program Files\Copilot\copilot.exe",
            @"C:\Program Files (x86)\Copilot\copilot.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\copilot\\copilot.exe")
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        // 3) fallback to PATH lookup using `where`
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "copilot.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(first) && File.Exists(first))
                        return first;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>Create a new session with current settings.</summary>
    private async Task CreateNewSessionAsync(CancellationToken ct = default)
    {
        if (_client == null) return;

        // Dispose old session
        if (_session != null)
        {
            try { await _session.DisposeAsync(); } catch { /* ignore */ }
            _session = null;
        }

        var config = new SessionConfig
        {
            Model = _model,
            Streaming = _streamingEnabled,
            InfiniteSessions = new InfiniteSessionConfig
            {
                Enabled = true,
                BackgroundCompactionThreshold = 0.80,
                BufferExhaustionThreshold = 0.95
            },
            Hooks = new SessionHooks
            {
                OnPreToolUse = async (input, invocation) =>
                {
                    ToolExecutionStarted?.Invoke(this, input.ToolName ?? "unknown");
                    return new PreToolUseHookOutput
                    {
                        PermissionDecision = "allow"
                    };
                },
                OnPostToolUse = async (input, invocation) =>
                {
                    ToolExecutionCompleted?.Invoke(this, input.ToolName ?? "unknown");
                    return new PostToolUseHookOutput();
                },
                OnErrorOccurred = async (input, invocation) =>
                {
                    ErrorOccurred?.Invoke(this, $"Error in {input.ErrorContext}: {input.Error}");
                    return new ErrorOccurredHookOutput
                    {
                        ErrorHandling = "retry"
                    };
                }
            }
        };

        // System message
        if (!string.IsNullOrWhiteSpace(_customSystemMessage))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = _customSystemMessage
            };
        }

        _session = await _client.CreateSessionAsync(config, ct);

        // Subscribe to all session events
        SubscribeToSessionEvents();
    }

    /// <summary>Wire up all session event handlers.</summary>
    private void SubscribeToSessionEvents()
    {
        if (_session == null) return;

        _session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    if (!string.IsNullOrEmpty(delta.Data.DeltaContent))
                        StreamingTokenReceived?.Invoke(this, delta.Data.DeltaContent);
                    break;

                case AssistantReasoningDeltaEvent reasoningDelta:
                    if (!string.IsNullOrEmpty(reasoningDelta.Data.DeltaContent))
                        ReasoningTokenReceived?.Invoke(this, reasoningDelta.Data.DeltaContent);
                    break;

                case AssistantReasoningEvent reasoning:
                    if (!string.IsNullOrEmpty(reasoning.Data.Content))
                        ReasoningTokenReceived?.Invoke(this, reasoning.Data.Content);
                    break;

                case ToolExecutionStartEvent toolStart:
                    StatusChanged?.Invoke(this, $"🔧 Running: {toolStart.Data.ToolName}");
                    ToolExecutionStarted?.Invoke(this, toolStart.Data.ToolName ?? "tool");
                    break;

                case ToolExecutionCompleteEvent:
                    StatusChanged?.Invoke(this, "✅ Tool completed");
                    ToolExecutionCompleted?.Invoke(this, "tool");
                    break;

                case SessionCompactionStartEvent:
                    CompactionEvent?.Invoke(this, "🔄 Compacting context...");
                    StatusChanged?.Invoke(this, "🔄 Compacting session context...");
                    break;

                case SessionCompactionCompleteEvent:
                    CompactionEvent?.Invoke(this, "✅ Compaction complete");
                    StatusChanged?.Invoke(this, "✅ Context compacted");
                    break;

                case SessionErrorEvent err:
                    ErrorOccurred?.Invoke(this, err.Data.Message ?? "Unknown error");
                    StatusChanged?.Invoke(this, $"❌ Error: {err.Data.Message}");
                    break;
            }
        });
    }

    // ── Chat ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a message to Copilot and return the full reply text.
    /// When streaming is enabled, fires StreamingTokenReceived for each chunk.
    /// </summary>
    public async Task<string> ChatAsync(
        string userMessage,
        string? systemPrompt = null,
        CancellationToken ct = default)
    {
        if (_session == null)
            return "❌ GitHub Copilot is not connected. " +
                   "Make sure the Copilot CLI is installed and you are signed in.";

        IsProcessing = true;
        _abortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        StatusChanged?.Invoke(this, "💬 Processing...");

        try
        {
            var prompt = BuildPrompt(userMessage, systemPrompt);
            var msgOptions = new MessageOptions { Prompt = prompt };

            if (_streamingEnabled)
            {
                return await SendStreamingAsync(msgOptions);
            }
            else
            {
                var reply = await _session.SendAndWaitAsync(msgOptions);
                var text = reply?.Data.Content ?? string.Empty;
                return string.IsNullOrWhiteSpace(text) ? "(no response)" : text;
            }
        }
        catch (OperationCanceledException)
        {
            return "⏹️ Request cancelled.";
        }
        catch (Exception ex)
        {
            var msg = $"❌ Error: {ex.Message}";
            ErrorOccurred?.Invoke(this, msg);
            return msg;
        }
        finally
        {
            IsProcessing = false;
            _abortCts?.Dispose();
            _abortCts = null;
            StatusChanged?.Invoke(this, $"✅ Ready — {_model}");
        }
    }

    /// <summary>
    /// Send a message with file context.
    /// Includes file content in the prompt as context.
    /// </summary>
    public async Task<string> ChatWithAttachmentsAsync(
        string userMessage,
        IEnumerable<string> filePaths,
        string? systemPrompt = null,
        CancellationToken ct = default)
    {
        if (_session == null)
            return "❌ GitHub Copilot is not connected.";

        IsProcessing = true;
        _abortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        StatusChanged?.Invoke(this, "💬 Processing with attachments...");

        try
        {
            // Build file context from attached files
            var fileContext = new System.Text.StringBuilder();
            foreach (var filePath in filePaths)
            {
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    var content = await System.IO.File.ReadAllTextAsync(filePath, ct);
                    fileContext.AppendLine($"\n--- File: {fileName} ---");
                    fileContext.AppendLine(content);
                    fileContext.AppendLine("--- End of file ---\n");
                }
            }

            var fullPrompt = BuildPrompt(userMessage, systemPrompt);
            if (fileContext.Length > 0)
            {
                fullPrompt = $"Context files:\n{fileContext}\n\nUser request: {fullPrompt}";
            }

            var msgOptions = new MessageOptions { Prompt = fullPrompt };

            return await SendStreamingAsync(msgOptions);
        }
        catch (OperationCanceledException)
        {
            return "⏹️ Request cancelled.";
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _abortCts?.Dispose();
            _abortCts = null;
            StatusChanged?.Invoke(this, $"✅ Ready — {_model}");
        }
    }

    /// <summary>Send a message and wait for streaming completion.</summary>
    private async Task<string> SendStreamingAsync(MessageOptions msgOptions)
    {
        var done = new TaskCompletionSource<string>();
        string fullResponse = "";

        IDisposable? sub = _session!.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    fullResponse = msg.Data.Content ?? "";
                    break;
                case SessionIdleEvent:
                    done.TrySetResult(fullResponse);
                    break;
                case SessionErrorEvent err:
                    done.TrySetException(new Exception(err.Data.Message ?? "Session error"));
                    break;
            }
        });

        await _session.SendAsync(msgOptions);

        // Wait with cancellation support
        if (_abortCts != null)
        {
            using var reg = _abortCts.Token.Register(() => done.TrySetCanceled());
            var result = await done.Task;
            sub?.Dispose();
            return string.IsNullOrWhiteSpace(result) ? "(no response)" : result;
        }
        else
        {
            var result = await done.Task;
            sub?.Dispose();
            return string.IsNullOrWhiteSpace(result) ? "(no response)" : result;
        }
    }

    // ── Abort ─────────────────────────────────────────────────────────────

    /// <summary>Abort the currently processing request.</summary>
    public async Task AbortCurrentRequestAsync()
    {
        _abortCts?.Cancel();
        if (_session != null)
        {
            try { await _session.AbortAsync(); } catch { /* ignore */ }
        }
        IsProcessing = false;
        StatusChanged?.Invoke(this, "⏹️ Request aborted");
    }

    // ── Model switching ──────────────────────────────────────────────────

    /// <summary>Switch to a different model. Recreates the session.</summary>
    public async Task SwitchModelAsync(string model, CancellationToken ct = default)
    {
        _model = model;
        SettingsDbService.SaveSetting("copilot_model", model);

        if (_client != null && _initialized)
        {
            StatusChanged?.Invoke(this, $"🔄 Switching to {model}...");
            try
            {
                await CreateNewSessionAsync(ct);
                StatusChanged?.Invoke(this, $"✅ Now using {model}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"⚠️ Failed to switch model: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }
    }

    // ── Session management ───────────────────────────────────────────────

    /// <summary>List all sessions.</summary>
    public async Task<List<SessionMetadata>?> ListSessionsAsync()
    {
        if (_client == null) return null;
        try
        {
            return await _client.ListSessionsAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to list sessions: {ex.Message}");
            return null;
        }
    }

    /// <summary>Create a new clean session (clears conversation).</summary>
    public async Task NewSessionAsync(CancellationToken ct = default)
    {
        if (_client == null) return;
        try
        {
            StatusChanged?.Invoke(this, "🔄 Creating new session...");
            await CreateNewSessionAsync(ct);
            StatusChanged?.Invoke(this, $"✅ New session started — {_model}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"⚠️ Failed to create session: {ex.Message}");
        }
    }

    /// <summary>Resume a previous session by ID.</summary>
    public async Task ResumeSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_client == null) return;
        try
        {
            if (_session != null)
            {
                try { await _session.DisposeAsync(); } catch { /* ignore */ }
            }

            StatusChanged?.Invoke(this, "🔄 Resuming session...");
            _session = await _client.ResumeSessionAsync(sessionId, null);
            SubscribeToSessionEvents();
            StatusChanged?.Invoke(this, "✅ Session resumed");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"⚠️ Failed to resume: {ex.Message}");
        }
    }

    /// <summary>Delete a session by ID.</summary>
    public async Task DeleteSessionAsync(string sessionId)
    {
        if (_client == null) return;
        try
        {
            await _client.DeleteSessionAsync(sessionId);
            StatusChanged?.Invoke(this, "🗑️ Session deleted");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to delete session: {ex.Message}");
        }
    }

    /// <summary>Clear current conversation by creating a fresh session.</summary>
    public async Task ClearHistoryAsync(CancellationToken ct = default)
    {
        await NewSessionAsync(ct);
    }

    /// <summary>Synchronous clear — starts a background reset.</summary>
    public void ClearHistory() => _ = ClearHistoryAsync();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string BuildPrompt(string userMessage, string? systemPrompt)
    {
        return string.IsNullOrWhiteSpace(systemPrompt)
            ? userMessage
            : $"{systemPrompt}\n\n{userMessage}";
    }

    // ── Disposal ─────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _abortCts?.Cancel();
        _abortCts?.Dispose();
        if (_session != null) try { await _session.DisposeAsync(); } catch { }
        if (_client != null) try { await _client.DisposeAsync(); } catch { }
        _session = null;
        _client = null;
    }

    public void Dispose() => _ = DisposeAsync().AsTask();
}

// ── Supporting types ─────────────────────────────────────────────────────

/// <summary>Information about an available Copilot model.</summary>
public record CopilotModelInfo(string Id, string DisplayName, string Description);

