using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Manages breakpoints per file path.
/// A breakpoint is identified by its 1-based line number within a file.
/// </summary>
public static class BreakpointService
{
    // file path (normalized) → set of 1-based line numbers
    private static readonly Dictionary<string, HashSet<int>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string _storageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Insait Edit",
        "debug");
    private static readonly string _storagePath = Path.Combine(_storageDirectory, "breakpoints.json");

    public static event EventHandler<BreakpointChangedEventArgs>? BreakpointsChanged;

    static BreakpointService()
    {
        Load();
    }

    /// <summary>
    /// Toggle a breakpoint at <paramref name="line"/> (1-based) for the given file.
    /// Returns <see langword="true"/> if the breakpoint was added, <see langword="false"/> if removed.
    /// </summary>
    public static bool Toggle(string filePath, int line)
    {
        filePath = NormalizePath(filePath);
        if (string.IsNullOrWhiteSpace(filePath) || line <= 0)
            return false;

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

        if (set.Count == 0)
            _breakpoints.Remove(filePath);

        Save();

        BreakpointsChanged?.Invoke(null, new BreakpointChangedEventArgs(filePath, line, added));
        return added;
    }

    /// <summary>Returns whether a breakpoint exists at <paramref name="line"/> (1-based).</summary>
    public static bool Has(string filePath, int line)
    {
        filePath = NormalizePath(filePath);
        return _breakpoints.TryGetValue(filePath, out var set) && set.Contains(line);
    }

    /// <summary>Returns all breakpoint line numbers (1-based) for the given file.</summary>
    public static IReadOnlySet<int> GetLines(string filePath)
    {
        filePath = NormalizePath(filePath);
        if (_breakpoints.TryGetValue(filePath, out var set))
            return set;
        return new HashSet<int>();
    }

    /// <summary>Returns a snapshot of all breakpoints grouped by file path.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> GetAll()
    {
        return _breakpoints.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<int>)pair.Value.OrderBy(line => line).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Removes all breakpoints for the given file.</summary>
    public static void ClearFile(string filePath)
    {
        filePath = NormalizePath(filePath);
        if (_breakpoints.Remove(filePath))
        {
            Save();
            BreakpointsChanged?.Invoke(null, new BreakpointChangedEventArgs(filePath, -1, false));
        }
    }

    /// <summary>Removes all breakpoints across all files.</summary>
    public static void ClearAll()
    {
        _breakpoints.Clear();
        Save();
        BreakpointsChanged?.Invoke(null, new BreakpointChangedEventArgs(string.Empty, -1, false));
    }

    private static string NormalizePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        try
        {
            return Path.GetFullPath(filePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return filePath.Trim();
        }
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(_storagePath))
                return;

            var json = File.ReadAllText(_storagePath);
            var data = JsonSerializer.Deserialize<List<BreakpointEntry>>(json);
            if (data == null)
                return;

            foreach (var entry in data)
            {
                var normalizedPath = NormalizePath(entry.FilePath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(normalizedPath) || entry.Lines == null)
                    continue;

                var lines = entry.Lines.Where(line => line > 0).Distinct().ToHashSet();
                if (lines.Count > 0)
                    _breakpoints[normalizedPath] = lines;
            }
        }
        catch
        {
            // Ignore persisted-state errors; breakpoints can still work in-memory.
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(_storageDirectory);

            var data = _breakpoints
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new BreakpointEntry
                {
                    FilePath = pair.Key,
                    Lines = pair.Value.OrderBy(line => line).ToList()
                })
                .ToList();

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch
        {
            // Ignore persistence errors; runtime breakpoint state is still valid.
        }
    }

    private sealed class BreakpointEntry
    {
        public string? FilePath { get; set; }
        public List<int>? Lines { get; set; }
    }
}

public sealed class BreakpointChangedEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }   // -1 means "all cleared"
    public bool Added { get; }

    public BreakpointChangedEventArgs(string filePath, int line, bool added)
    {
        FilePath = filePath;
        Line = line;
        Added = added;
    }
}

