using System;
using System.Collections.Generic;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Manages breakpoints per file path.
/// A breakpoint is identified by its 1-based line number within a file.
/// </summary>
public static class BreakpointService
{
    // file path (normalized) → set of 1-based line numbers
    private static readonly Dictionary<string, HashSet<int>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    public static event EventHandler<BreakpointChangedEventArgs>? BreakpointsChanged;

    /// <summary>
    /// Toggle a breakpoint at <paramref name="line"/> (1-based) for the given file.
    /// Returns <see langword="true"/> if the breakpoint was added, <see langword="false"/> if removed.
    /// </summary>
    public static bool Toggle(string filePath, int line)
    {
        if (!_breakpoints.TryGetValue(filePath, out var set))
        {
            set = new HashSet<int>();
            _breakpoints[filePath] = set;
        }

        bool added;
        if (set.Contains(line))
        {
            set.Remove(line);
            added = false;
        }
        else
        {
            set.Add(line);
            added = true;
        }

        BreakpointsChanged?.Invoke(null, new BreakpointChangedEventArgs(filePath, line, added));
        return added;
    }

    /// <summary>Returns whether a breakpoint exists at <paramref name="line"/> (1-based).</summary>
    public static bool Has(string filePath, int line)
    {
        return _breakpoints.TryGetValue(filePath, out var set) && set.Contains(line);
    }

    /// <summary>Returns all breakpoint line numbers (1-based) for the given file.</summary>
    public static IReadOnlySet<int> GetLines(string filePath)
    {
        if (_breakpoints.TryGetValue(filePath, out var set))
            return set;
        return new HashSet<int>();
    }

    /// <summary>Removes all breakpoints for the given file.</summary>
    public static void ClearFile(string filePath)
    {
        if (_breakpoints.Remove(filePath))
            BreakpointsChanged?.Invoke(null, new BreakpointChangedEventArgs(filePath, -1, false));
    }

    /// <summary>Removes all breakpoints across all files.</summary>
    public static void ClearAll()
    {
        _breakpoints.Clear();
        BreakpointsChanged?.Invoke(null, new BreakpointChangedEventArgs(string.Empty, -1, false));
    }
}

public sealed class BreakpointChangedEventArgs : EventArgs
{
    public string FilePath { get; }
    public int    Line     { get; }   // -1 means "all cleared"
    public bool   Added    { get; }

    public BreakpointChangedEventArgs(string filePath, int line, bool added)
    {
        FilePath = filePath;
        Line     = line;
        Added    = added;
    }
}

