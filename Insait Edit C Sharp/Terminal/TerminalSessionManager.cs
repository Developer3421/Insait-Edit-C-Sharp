using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Terminal;

/// <summary>
/// Manages terminal sessions — tracks the active terminal panel,
/// command history, and working directory state.
/// </summary>
public sealed class TerminalSessionManager
{
    private static readonly Lazy<TerminalSessionManager> _instance = new(() => new TerminalSessionManager());
    public static TerminalSessionManager Instance => _instance.Value;

    private readonly List<string> _commandHistory = new();
    private InsaitTerminalPanel? _activePanel;

    private TerminalSessionManager() { }

    /// <summary>
    /// Gets or sets the active terminal panel.
    /// </summary>
    public InsaitTerminalPanel? ActivePanel
    {
        get => _activePanel;
        set => _activePanel = value;
    }

    /// <summary>
    /// Gets the command history.
    /// </summary>
    public IReadOnlyList<string> CommandHistory => _commandHistory.AsReadOnly();

    /// <summary>
    /// Record a command to history.
    /// </summary>
    public void RecordCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        _commandHistory.Add(command);
        // Keep only last 500 commands
        if (_commandHistory.Count > 500)
            _commandHistory.RemoveRange(0, _commandHistory.Count - 500);
    }

    /// <summary>
    /// Execute a command in the active terminal.
    /// </summary>
    public async Task ExecuteAsync(string command)
    {
        if (_activePanel == null) return;
        RecordCommand(command);
        await _activePanel.ExecuteCommandAsync(command);
    }

    /// <summary>
    /// Change directory in the active terminal.
    /// </summary>
    public void ChangeDirectory(string path)
    {
        _activePanel?.ChangeDirectory(path);
    }

    /// <summary>
    /// Clear the active terminal.
    /// </summary>
    public void Clear()
    {
        _activePanel?.ClearTerminal();
    }

    /// <summary>
    /// Restart the active shell session.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_activePanel != null)
            await _activePanel.RestartShellAsync();
    }
}

