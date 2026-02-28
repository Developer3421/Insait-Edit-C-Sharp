using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Insait_Edit_C_Sharp;

/// <summary>Non-modal window showing full AXAML preview error details.</summary>
public partial class PreviewErrorWindow : Window
{
    public PreviewErrorWindow() { InitializeComponent(); }

    public PreviewErrorWindow(string errorMessage)
    {
        InitializeComponent();
        SetText(errorMessage);
    }

    public void UpdateError(string message) => SetText(message);

    private void SetText(string message)
    {
        var tb = this.FindControl<SelectableTextBlock>("ErrorText");
        if (tb != null) tb.Text = message;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var tb = this.FindControl<SelectableTextBlock>("ErrorText");
        if (tb?.Text is { Length: > 0 } text && Clipboard != null)
            await Clipboard.SetTextAsync(text);
    }
}

