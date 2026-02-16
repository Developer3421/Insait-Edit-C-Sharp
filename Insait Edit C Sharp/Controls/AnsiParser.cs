using System;
using System.Collections.Generic;
using System.Globalization;

namespace Insait_Edit_C_Sharp.Controls;

internal sealed class AnsiParser
{
    private readonly AnsiGridBuffer _buffer;

    private enum State { Text, Esc, Csi }
    private State _state;

    private readonly List<int> _csiParams = new();
    private int _currentParam;
    private bool _hasParam;

    public event EventHandler? Changed;

    public AnsiParser(AnsiGridBuffer buffer)
    {
        _buffer = buffer;
    }

    public void Feed(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var anyChange = false;

        foreach (var ch in text)
        {
            switch (_state)
            {
                case State.Text:
                    if (ch == '\u001b')
                    {
                        _state = State.Esc;
                    }
                    else
                    {
                        anyChange |= HandlePlainChar(ch);
                    }
                    break;

                case State.Esc:
                    if (ch == '[')
                    {
                        _state = State.Csi;
                        _csiParams.Clear();
                        _currentParam = 0;
                        _hasParam = false;
                    }
                    else
                    {
                        // Not a CSI sequence we handle.
                        _state = State.Text;
                    }
                    break;

                case State.Csi:
                    if (char.IsDigit(ch))
                    {
                        _currentParam = (_currentParam * 10) + (ch - '0');
                        _hasParam = true;
                    }
                    else if (ch == ';')
                    {
                        _csiParams.Add(_hasParam ? _currentParam : 0);
                        _currentParam = 0;
                        _hasParam = false;
                    }
                    else
                    {
                        // Final byte
                        if (_hasParam || _csiParams.Count > 0)
                            _csiParams.Add(_hasParam ? _currentParam : 0);

                        anyChange |= ExecuteCsi(ch, _csiParams);
                        _state = State.Text;
                    }
                    break;
            }
        }

        if (anyChange)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool HandlePlainChar(char ch)
    {
        switch (ch)
        {
            case '\r':
                _buffer.CarriageReturn();
                return true;
            case '\n':
                _buffer.NewLine();
                return true;
            case '\b':
                _buffer.Backspace();
                return true;
            case '\t':
                _buffer.Tab();
                return true;
            default:
                _buffer.PutChar(ch);
                return true;
        }
    }

    private bool ExecuteCsi(char finalByte, List<int> ps)
    {
        // Supported subset
        switch (finalByte)
        {
            case 'm':
                // SGR - ignore styling in plain renderer for now.
                // Still treat as change because it affects the caret position? (no)
                return false;

            case 'J':
                // 2J clear screen
                if (ps.Count == 0 || ps[0] == 2)
                {
                    _buffer.Clear();
                    return true;
                }
                return false;

            case 'K':
                // 2K clear line
                if (ps.Count == 0 || ps[0] == 2)
                {
                    _buffer.ClearLine();
                    return true;
                }
                return false;

            case 'H':
            case 'f':
                // CUP
                var row = ps.Count >= 1 ? ps[0] : 1;
                var col = ps.Count >= 2 ? ps[1] : 1;
                _buffer.SetCursor(row - 1, col - 1);
                return true;

            case 'A':
                _buffer.MoveCursor(-Math.Max(1, ps.Count == 0 ? 1 : ps[0]), 0);
                return true;
            case 'B':
                _buffer.MoveCursor(Math.Max(1, ps.Count == 0 ? 1 : ps[0]), 0);
                return true;
            case 'C':
                _buffer.MoveCursor(0, Math.Max(1, ps.Count == 0 ? 1 : ps[0]));
                return true;
            case 'D':
                _buffer.MoveCursor(0, -Math.Max(1, ps.Count == 0 ? 1 : ps[0]));
                return true;

            default:
                return false;
        }
    }
}

