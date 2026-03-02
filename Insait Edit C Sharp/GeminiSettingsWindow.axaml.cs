using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class GeminiSettingsWindow : Window
{
    private TextBox? _apiKeyBox;
    private CheckBox? _showKeyCheck;

    public GeminiSettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _apiKeyBox   = this.FindControl<TextBox>("ApiKeyBox");
        _showKeyCheck = this.FindControl<CheckBox>("ShowKeyCheck");

        // Pre-fill saved key
        var saved = SettingsDbService.LoadGeminiApiKey();
        if (!string.IsNullOrEmpty(saved) && _apiKeyBox != null)
            _apiKeyBox.Text = saved;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void ShowKeyCheck_Checked(object? sender, RoutedEventArgs e)
    {
        if (_apiKeyBox != null) _apiKeyBox.PasswordChar = '\0';
    }

    private void ShowKeyCheck_Unchecked(object? sender, RoutedEventArgs e)
    {
        if (_apiKeyBox != null) _apiKeyBox.PasswordChar = '•';
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var key = _apiKeyBox?.Text?.Trim() ?? string.Empty;
        SettingsDbService.SaveGeminiApiKey(key);
        Close();
    }
}

