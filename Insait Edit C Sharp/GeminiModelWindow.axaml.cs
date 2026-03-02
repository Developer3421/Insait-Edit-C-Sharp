using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Step 2 of 2: user selects a Gemini model and triggers translation generation.
/// </summary>
public partial class GeminiModelWindow : Window
{
    private readonly string _languageName;
    private CancellationTokenSource? _cts;
    private bool _generating;

    private TextBlock? _languageLabel;
    private TextBox?   _modelBox;
    private TextBlock? _progressLabel;
    private Button?    _generateButton;

    public GeminiModelWindow(string languageName)
    {
        AvaloniaXamlLoader.Load(this);
        _languageName = languageName;

        _languageLabel  = this.FindControl<TextBlock>("LanguageLabel");
        _modelBox       = this.FindControl<TextBox>("ModelBox");
        _progressLabel  = this.FindControl<TextBlock>("ProgressLabel");
        _generateButton = this.FindControl<Button>("GenerateButton");

        if (_languageLabel != null) _languageLabel.Text = languageName;

        // Pre-fill last used model from saved languages if available
        var existing = LanguagesDbService.LoadAll()
            .Find(e => string.Equals(e.LanguageName, languageName, StringComparison.OrdinalIgnoreCase));
        if (_modelBox != null)
            _modelBox.Text = (existing != null && !string.IsNullOrEmpty(existing.GeminiModel))
                ? existing.GeminiModel
                : "gemini-2.0-flash";
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)  => CancelAndClose();
    private void CancelButton_Click(object? sender, RoutedEventArgs e) => CancelAndClose();

    private void CancelAndClose()
    {
        _cts?.Cancel();
        Close();
    }

    private async void GenerateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_generating) { CancelAndClose(); return; }

        var model = _modelBox?.Text?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            ShowProgress("Please enter a Gemini model name.", isError: false);
            _modelBox?.Focus();
            return;
        }

        _generating = true;
        if (_generateButton != null) _generateButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        void Progress(string msg) => Dispatcher.UIThread.Post(() => ShowProgress(msg, false));

        try
        {
            var result = await CustomTranslationService.GenerateAndSaveAsync(
                _languageName, model, Progress, _cts.Token);

            if (result.StartsWith("Error:"))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ShowProgress(result, isError: true);
                    if (_generateButton != null) _generateButton.IsEnabled = true;
                    _generating = false;
                });
                return;
            }

            // Success — save to languages DB
            LanguagesDbService.Save(new CustomLanguageEntry
            {
                LanguageName   = _languageName,
                GeminiModel    = model,
                DictionaryPath = result
            });

            Dispatcher.UIThread.Post(() =>
            {
                ShowProgress(
                    $"Translation saved!\nFile: {result}\n\nYou can now select '{_languageName}' in Menu > Language.",
                    isError: false);
                if (_generateButton != null)
                {
                    _generateButton.Content = new TextBlock { Text = "Close", FontSize = 13 };
                    _generateButton.IsEnabled = true;
                }
                _generating = false;
            });
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ShowProgress("Cancelled.", isError: false);
                if (_generateButton != null) _generateButton.IsEnabled = true;
                _generating = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ShowProgress($"Error: {ex.Message}", isError: true);
                if (_generateButton != null) _generateButton.IsEnabled = true;
                _generating = false;
            });
        }
    }

    private void ShowProgress(string message, bool isError)
    {
        if (_progressLabel == null) return;
        _progressLabel.Text      = message;
        _progressLabel.Foreground = isError
            ? Avalonia.Media.Brushes.Salmon
            : Avalonia.Media.Brushes.LightBlue;
        _progressLabel.IsVisible = true;
    }
}

