using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Step 1 of 2: user enters the target language name.
/// On Next, opens GeminiModelWindow (step 2).
/// </summary>
public partial class GeminiLanguageNameWindow : Window
{
    private TextBox? _languageNameBox;

    public GeminiLanguageNameWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _languageNameBox = this.FindControl<TextBox>("LanguageNameBox");
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)  => Close();
    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        var name = _languageNameBox?.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _languageNameBox?.Focus();
            return;
        }

        var step2 = new GeminiModelWindow(name);
        step2.ShowDialog(this);
        step2.Closed += (_, _2) => Close();
    }
}

