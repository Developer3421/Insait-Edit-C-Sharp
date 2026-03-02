using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;
using System;

namespace Insait_Edit_C_Sharp;

/// <summary>Non-modal window showing full AXAML preview error details.</summary>
public partial class PreviewErrorWindow : Window
{
    public PreviewErrorWindow() { InitializeComponent(); ApplyLocalization(); }

    public PreviewErrorWindow(string errorMessage)
    {
        InitializeComponent();
        SetText(errorMessage);
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("PreviewError.Title");
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

