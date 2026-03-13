using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Insait_Edit_C_Sharp.Controls;

public partial class InputDialog : Window
{
    public string? Result { get; private set; }

    public InputDialog() { InitializeComponent(); }

    /// <summary>Creates a styled input dialog.</summary>
    /// <param name="title">Window header text</param>
    /// <param name="prompt">Label above the input field</param>
    /// <param name="defaultValue">Pre-filled value</param>
    /// <param name="icon">Emoji icon shown in title bar</param>
    public InputDialog(string title, string prompt, string defaultValue = "", string icon = "✏️") : this()
    {
        if (this.FindControl<TextBlock>("TitleText") is { } tb) tb.Text = title;
        if (this.FindControl<TextBlock>("TitleIcon") is { } ti) ti.Text = icon;
        if (this.FindControl<TextBlock>("PromptText") is { } pb) pb.Text = prompt;

        var inputBox = this.FindControl<TextBox>("InputBox")!;
        inputBox.Text = defaultValue;

        // Select all default text so user can immediately type
        inputBox.AttachedToVisualTree += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };

        // Key bindings
        inputBox.KeyDown += OnInputKeyDown;

        // Buttons
        this.FindControl<Button>("OkBtn")!.Click += OnOk;
        this.FindControl<Button>("CancelBtn")!.Click += OnCancel;
        this.FindControl<Button>("CloseTitleBtn")!.Click += OnCancel;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Commit();
    private void OnCancel(object? sender, RoutedEventArgs e) => Cancel();

    private void Commit()
    {
        var text = this.FindControl<TextBox>("InputBox")?.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            Result = text;
            Close();
        }
    }

    private void Cancel()
    {
        Result = null;
        Close();
    }
}

