using System;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Event args for editor content changes that include the new content value.
/// </summary>
public class ContentChangedEventArgs : EventArgs
{
    public string NewContent { get; }

    public ContentChangedEventArgs(string newContent)
    {
        NewContent = newContent;
    }
}

/// <summary>
/// Event args for cursor position changes in the editor.
/// </summary>
public class CursorPositionChangedEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public CursorPositionChangedEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

