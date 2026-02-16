using System;
using System.Text;

namespace Insait_Edit_C_Sharp.Controls;

internal sealed class AnsiGridBuffer
{
    private readonly int _cols;
    private readonly int _maxRows;

    private readonly StringBuilder _content = new();

    private int _cursorCol;

    public AnsiGridBuffer(int cols, int rows)
    {
        _cols = Math.Max(20, cols);
        _maxRows = Math.Max(100, rows);
    }

    public void PutChar(char ch)
    {
        // Very simple: append and maintain \r handling elsewhere.
        _content.Append(ch);
        _cursorCol++;
    }

    public void NewLine()
    {
        _content.Append('\n');
        _cursorCol = 0;
        TrimIfNeeded();
    }

    public void CarriageReturn()
    {
        // Convert to \r, and let UI overwrite via subsequent text updates.
        // In plain TextBlock this won't truly overwrite; the parser tries to use \r\n.
        _content.Append('\r');
        _cursorCol = 0;
    }

    public void Backspace()
    {
        if (_content.Length == 0) return;
        _content.Length -= 1;
        _cursorCol = Math.Max(0, _cursorCol - 1);
    }

    public void Tab()
    {
        var spaces = 4 - (_cursorCol % 4);
        for (var i = 0; i < spaces; i++) PutChar(' ');
    }

    public void Clear()
    {
        _content.Clear();
        _cursorCol = 0;
    }

    public void ClearLine()
    {
        // Remove until previous newline
        for (var i = _content.Length - 1; i >= 0; i--)
        {
            var c = _content[i];
            if (c == '\n')
            {
                _content.Length = i + 1;
                _cursorCol = 0;
                return;
            }
        }
        Clear();
    }

    public void SetCursor(int row, int col)
    {
        // Simplified: not truly supported without a real grid.
        // We'll treat as newline resets when asked to home.
        if (row <= 0 && col <= 0)
        {
            // no-op for now
        }
    }

    public void MoveCursor(int dRow, int dCol)
    {
        // Simplified: ignore.
    }

    public string ToPlainText()
    {
        return _content.ToString();
    }

    private void TrimIfNeeded()
    {
        // crude trimming by lines
        var lines = 0;
        for (var i = _content.Length - 1; i >= 0; i--)
        {
            if (_content[i] == '\n') lines++;
            if (lines > _maxRows)
            {
                // cut roughly first half
                _content.Remove(0, i);
                break;
            }
        }
    }
}

