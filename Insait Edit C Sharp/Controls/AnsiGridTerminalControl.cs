using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Lightweight terminal renderer that understands a practical subset of ANSI/VT sequences.
/// It's not a full xterm, but it handles the most common features used by modern CLIs:
/// - SGR colors (30-37/90-97, 40-47/100-107, 0 reset, 1 bold)
/// - Clear screen/line (ESC[2J, ESC[2K)
/// - Cursor positioning (ESC[H, ESC[row;colH)
/// - Carriage return/newline/backspace
///
/// Rendering is done with a fixed-size character grid and a monospaced font.
/// </summary>
public sealed class AnsiGridTerminalControl : UserControl
{
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _text;

    private readonly AnsiGridBuffer _buffer = new(cols: 120, rows: 3000);
    private readonly AnsiParser _parser;

    public AnsiGridTerminalControl()
    {
        _text = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 14,
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(8)
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _text,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E"))
        };

        Content = _scroll;

        _parser = new AnsiParser(_buffer);
        _parser.Changed += (_, __) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _text.Text = _buffer.ToPlainText();
                _scroll.ScrollToEnd();
            });
        };
    }

    public void Write(string text) => _parser.Feed(text);

    public void Clear()
    {
        _buffer.Clear();
        _text.Text = string.Empty;
    }
}

