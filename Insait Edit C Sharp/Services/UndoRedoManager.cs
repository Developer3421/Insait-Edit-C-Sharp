using System;
using System.Collections.Generic;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Lightweight undo/redo action record.
/// Stores only offset + removed/inserted text (delta), not full document snapshots.
/// </summary>
public sealed class UndoRedoAction
{
    /// <summary>Offset in the document where the change occurred.</summary>
    public int Offset { get; init; }

    /// <summary>Text that was removed (empty string for pure insertions).</summary>
    public string RemovedText { get; init; } = string.Empty;

    /// <summary>Text that was inserted (empty string for pure deletions).</summary>
    public string InsertedText { get; init; } = string.Empty;

    /// <summary>Timestamp of the action for merging nearby edits.</summary>
    public long TimestampTicks { get; init; }
}

/// <summary>
/// Memory-efficient undo/redo manager backed by a <see cref="LinkedList{T}"/>.
/// Keeps at most <see cref="MaxHistory"/> delta-actions and merges rapid
/// single-character edits (typing) into one action to save RAM.
/// </summary>
public sealed class UndoRedoManager
{
    /// <summary>Maximum number of undo steps retained.</summary>
    public const int MaxHistory = 50;

    /// <summary>If two edits happen within this window they are merged.</summary>
    private static readonly long MergeWindowTicks = TimeSpan.FromMilliseconds(800).Ticks;

    private readonly LinkedList<UndoRedoAction> _undoStack = new();
    private readonly LinkedList<UndoRedoAction> _redoStack = new();

    /// <summary>True while this manager is applying an undo/redo so external
    /// listeners should not record a new action.</summary>
    public bool IsApplying { get; private set; }

    /// <summary>Raised when CanUndo / CanRedo state changes.</summary>
    public event EventHandler? StateChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Records a document change as an undoable action.
    /// Consecutive single-char inserts / deletes within <see cref="MergeWindowTicks"/>
    /// are merged into a single action to minimise memory.
    /// </summary>
    public void RecordAction(int offset, string removedText, string insertedText)
    {
        if (IsApplying) return;

        var now = DateTime.UtcNow.Ticks;

        // Try to merge with the previous action (rapid typing)
        if (_undoStack.Last != null)
        {
            var prev = _undoStack.Last.Value;
            bool canMerge = (now - prev.TimestampTicks) < MergeWindowTicks
                            && removedText.Length <= 1
                            && insertedText.Length <= 1
                            && prev.InsertedText.Length < 120; // cap merge length

            if (canMerge)
            {
                // Continuation of typing right after previous insert
                if (insertedText.Length == 1 && prev.RemovedText.Length == 0
                    && offset == prev.Offset + prev.InsertedText.Length)
                {
                    _undoStack.Last.Value = new UndoRedoAction
                    {
                        Offset = prev.Offset,
                        RemovedText = prev.RemovedText,
                        InsertedText = prev.InsertedText + insertedText,
                        TimestampTicks = now
                    };
                    ClearRedo();
                    RaiseStateChanged();
                    return;
                }

                // Continuation of backspace right before previous deletion
                if (removedText.Length == 1 && insertedText.Length == 0
                    && offset == prev.Offset - 1
                    && prev.InsertedText.Length == 0)
                {
                    _undoStack.Last.Value = new UndoRedoAction
                    {
                        Offset = offset,
                        RemovedText = removedText + prev.RemovedText,
                        InsertedText = string.Empty,
                        TimestampTicks = now
                    };
                    ClearRedo();
                    RaiseStateChanged();
                    return;
                }
            }
        }

        // Push new action
        _undoStack.AddLast(new UndoRedoAction
        {
            Offset = offset,
            RemovedText = removedText,
            InsertedText = insertedText,
            TimestampTicks = now
        });

        // Trim to MaxHistory
        while (_undoStack.Count > MaxHistory)
            _undoStack.RemoveFirst();

        ClearRedo();
        RaiseStateChanged();
    }

    /// <summary>
    /// Returns the action to undo (caller must apply it to the document)
    /// or null if nothing to undo.
    /// </summary>
    public UndoRedoAction? Undo()
    {
        if (_undoStack.Last == null) return null;

        IsApplying = true;
        var action = _undoStack.Last.Value;
        _undoStack.RemoveLast();

        _redoStack.AddLast(action);
        while (_redoStack.Count > MaxHistory)
            _redoStack.RemoveFirst();

        IsApplying = false;
        RaiseStateChanged();
        return action;
    }

    /// <summary>
    /// Returns the action to redo (caller must apply it to the document)
    /// or null if nothing to redo.
    /// </summary>
    public UndoRedoAction? Redo()
    {
        if (_redoStack.Last == null) return null;

        IsApplying = true;
        var action = _redoStack.Last.Value;
        _redoStack.RemoveLast();

        _undoStack.AddLast(action);
        while (_undoStack.Count > MaxHistory)
            _undoStack.RemoveFirst();

        IsApplying = false;
        RaiseStateChanged();
        return action;
    }

    /// <summary>Clears all history (e.g. when a new file is loaded).</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        RaiseStateChanged();
    }

    private void ClearRedo()
    {
        if (_redoStack.Count > 0)
        {
            _redoStack.Clear();
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

