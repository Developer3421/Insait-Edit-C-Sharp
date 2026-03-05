using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Insait_Edit_C_Sharp.Services;

// ═══════════════════════════════════════════════════════════════════════════
//  LiveTemplateSession
//  ─────────────────────────────────────────────────────────────────────────
//  Manages an active live template expansion with multiple tab-stop
//  placeholders. The user navigates between placeholders with Tab /
//  Shift+Tab and can edit the default text in each placeholder.
//
//  Tab-stop numbering follows VS / TextMate conventions:
//    $0               — final cursor position (session ends here)
//    $1, $2, …        — numbered stops visited in order
//    ${1:defaultText}  — stop with pre-filled default text
//
//  Linked tab-stops: multiple occurrences of the same number (e.g. ${1:i}
//  appearing 3 times in a "for" loop) are linked — editing one updates all.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class LiveTemplateSession
{
    /// <summary>
    /// A single tab-stop placeholder inside the expanded template text.
    /// </summary>
    public sealed class TabStop
    {
        /// <summary>Tab-stop number (0 = final cursor).</summary>
        public int Number { get; init; }

        /// <summary>Absolute offset in the full document text where this placeholder starts.</summary>
        public int Offset { get; set; }

        /// <summary>Length of the placeholder default text.</summary>
        public int Length { get; set; }

        /// <summary>The default/current text of this placeholder.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Group ID for linked stops (same number = same group).</summary>
        public int GroupId => Number;
    }

    // ── State ────────────────────────────────────────────────────────────
    private readonly List<TabStop> _stops = new();
    private int _currentIndex = -1;

    /// <summary>Whether the session is still active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The absolute offset where the template was inserted in the document.</summary>
    public int InsertionOffset { get; private set; }

    /// <summary>Total length of the expanded template text.</summary>
    public int TotalLength { get; private set; }

    /// <summary>All tab-stops in the session (ordered by number, then offset).</summary>
    public IReadOnlyList<TabStop> Stops => _stops;

    /// <summary>The currently active tab-stop, or null if session ended.</summary>
    public TabStop? CurrentStop =>
        _currentIndex >= 0 && _currentIndex < _stops.Count ? _stops[_currentIndex] : null;

    /// <summary>
    /// Index of the currently selected tab-stop group (for rendering highlights).
    /// </summary>
    public int CurrentGroupNumber => CurrentStop?.Number ?? -1;

    // ── Factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Expands a template body at the given document offset, producing the
    /// plain text to insert and populating the tab-stop list.
    /// </summary>
    /// <param name="body">Template body with VS-style placeholders.</param>
    /// <param name="documentOffset">Absolute offset in the document where the text will be inserted.</param>
    /// <param name="indent">Whitespace indent to prepend to every newline.</param>
    /// <returns>The expanded plain text ready for insertion.</returns>
    public string Expand(string body, int documentOffset, string indent)
    {
        InsertionOffset = documentOffset;
        IsActive = true;
        _stops.Clear();
        _currentIndex = -1;

        // Replace newlines with newline + indent
        var indented = body.Replace("\n", "\n" + indent);

        // Parse and replace placeholders, collecting tab-stop positions
        var result = new System.Text.StringBuilder();
        int pos = 0;

        // Regex matches ${N:text} and $N patterns
        var regex = new Regex(@"\$\{(\d+):([^}]*)\}|\$(\d+)");
        var matches = regex.Matches(indented);

        foreach (Match m in matches)
        {
            // Append text before this match
            result.Append(indented, pos, m.Index - pos);

            int stopNumber;
            string defaultText;

            if (m.Groups[1].Success)
            {
                // ${N:text} form
                stopNumber = int.Parse(m.Groups[1].Value);
                defaultText = m.Groups[2].Value;
            }
            else
            {
                // $N form
                stopNumber = int.Parse(m.Groups[3].Value);
                defaultText = string.Empty;
            }

            int offset = documentOffset + result.Length;

            _stops.Add(new TabStop
            {
                Number = stopNumber,
                Offset = offset,
                Length = defaultText.Length,
                Text   = defaultText,
            });

            result.Append(defaultText);
            pos = m.Index + m.Length;
        }

        // Append remaining text
        if (pos < indented.Length)
            result.Append(indented, pos, indented.Length - pos);

        var expandedText = result.ToString();
        TotalLength = expandedText.Length;

        // Sort stops: numbered stops (1, 2, 3...) first, then $0 (final cursor) last
        _stops.Sort((a, b) =>
        {
            // $0 always comes last
            if (a.Number == 0 && b.Number != 0) return 1;
            if (a.Number != 0 && b.Number == 0) return -1;
            // Then by number
            int cmp = a.Number.CompareTo(b.Number);
            if (cmp != 0) return cmp;
            // Within same number, by offset
            return a.Offset.CompareTo(b.Offset);
        });

        // Move to the first stop (skip $0 which is last)
        if (_stops.Count > 0)
            _currentIndex = 0;

        return expandedText;
    }

    /// <summary>
    /// Advances to the next tab-stop. Returns true if moved, false if session ended.
    /// </summary>
    public bool MoveNext()
    {
        if (!IsActive || _stops.Count == 0) return false;

        // Find the next group (skip stops with the same number as current)
        int currentNumber = CurrentStop?.Number ?? -1;
        int nextIndex = _currentIndex + 1;

        // Skip duplicate group members (linked stops)
        while (nextIndex < _stops.Count && _stops[nextIndex].Number == currentNumber)
            nextIndex++;

        if (nextIndex >= _stops.Count)
        {
            // Session complete
            End();
            return false;
        }

        _currentIndex = nextIndex;
        return true;
    }

    /// <summary>
    /// Moves to the previous tab-stop. Returns true if moved.
    /// </summary>
    public bool MovePrevious()
    {
        if (!IsActive || _currentIndex <= 0) return false;

        int currentNumber = CurrentStop?.Number ?? -1;
        int prevIndex = _currentIndex - 1;

        // Find the first stop of the previous group
        while (prevIndex > 0 && _stops[prevIndex].Number == currentNumber)
            prevIndex--;

        // Now find the first stop in this group
        int groupNumber = _stops[prevIndex].Number;
        while (prevIndex > 0 && _stops[prevIndex - 1].Number == groupNumber)
            prevIndex--;

        _currentIndex = prevIndex;
        return true;
    }

    /// <summary>
    /// Updates the text for all linked tab-stops in the current group.
    /// Called when the user types inside a placeholder.
    /// </summary>
    /// <param name="newText">The new text for the current group.</param>
    /// <param name="fullDocumentText">The full document text (for recalculating offsets).</param>
    /// <returns>
    /// List of (offset, oldLength, newText) replacements to apply in the document,
    /// ordered from last to first (so offsets remain valid during application).
    /// </returns>
    public List<(int offset, int oldLength, string newText)> UpdateCurrentGroup(
        string newText, string fullDocumentText)
    {
        if (!IsActive || CurrentStop == null)
            return new List<(int, int, string)>();

        int groupNumber = CurrentStop.Number;
        var groupStops = _stops.Where(s => s.Number == groupNumber).ToList();

        var replacements = new List<(int offset, int oldLength, string newText)>();

        // Process from last to first to keep offsets stable
        for (int i = groupStops.Count - 1; i >= 0; i--)
        {
            var stop = groupStops[i];
            replacements.Add((stop.Offset, stop.Length, newText));
        }

        // Now update the tab-stop records
        int lengthDelta = newText.Length - (groupStops.FirstOrDefault()?.Length ?? 0);

        foreach (var stop in groupStops)
        {
            stop.Text = newText;
            stop.Length = newText.Length;
        }

        // Adjust offsets of all stops that come after each replacement
        // Since replacements are applied last-to-first, we process forward
        foreach (var stop in _stops)
        {
            if (stop.Number == groupNumber) continue;
            int shiftCount = groupStops.Count(gs => gs.Offset < stop.Offset);
            stop.Offset += lengthDelta * shiftCount;
        }

        TotalLength += lengthDelta * groupStops.Count;
        return replacements;
    }

    /// <summary>
    /// Returns all stops belonging to the currently active group.
    /// </summary>
    public IReadOnlyList<TabStop> GetCurrentGroupStops()
    {
        if (!IsActive || CurrentStop == null)
            return Array.Empty<TabStop>();
        int num = CurrentStop.Number;
        return _stops.Where(s => s.Number == num).ToList();
    }

    /// <summary>
    /// Returns all unique group numbers in order (excluding already visited ones).
    /// </summary>
    public IReadOnlyList<int> GetGroupNumbers()
    {
        return _stops.Select(s => s.Number).Distinct().ToList();
    }

    /// <summary>
    /// Ends the template session.
    /// </summary>
    public void End()
    {
        IsActive = false;
        _currentIndex = -1;
    }

    /// <summary>
    /// Returns the offset where the cursor should be placed for the current stop.
    /// </summary>
    public int GetCursorOffset()
    {
        var stop = CurrentStop;
        if (stop == null)
        {
            // If there's a $0 stop, return its offset
            var finalStop = _stops.FirstOrDefault(s => s.Number == 0);
            return finalStop?.Offset ?? (InsertionOffset + TotalLength);
        }
        return stop.Offset;
    }

    /// <summary>
    /// Returns the selection range (offset, length) for the current stop.
    /// Used to highlight/select the current placeholder text.
    /// </summary>
    public (int offset, int length) GetCurrentSelection()
    {
        var stop = CurrentStop;
        if (stop == null) return (GetCursorOffset(), 0);
        return (stop.Offset, stop.Length);
    }
}

