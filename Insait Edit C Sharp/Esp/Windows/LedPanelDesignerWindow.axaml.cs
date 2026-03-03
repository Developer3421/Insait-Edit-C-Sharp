using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.Esp.Models;
using Insait_Edit_C_Sharp.Esp.Services;
using Insait_Edit_C_Sharp.Services;
using Path = System.IO.Path;

namespace Insait_Edit_C_Sharp.Esp.Windows;

public partial class LedPanelDesignerWindow : Window
{
    private readonly LedPanelConfig _config = new();
    private readonly LedPanelCodeGenerator _codeGenerator = new();
    private string _currentColor = "#FFFF0000";
    private bool _isDrawing;
    private CancellationTokenSource? _previewCts;
    private string? _projectPath;

    // LED grid rendering
    private const double LED_PADDING = 2;
    private const double MIN_LED_SIZE = 12;
    private const double MAX_LED_SIZE = 50;
    private readonly Dictionary<string, Rectangle> _ledRects = new();

    /// <summary>
    /// Event raised when code is generated and should be inserted into the editor
    /// </summary>
    public event EventHandler<string>? CodeGenerated;

    /// <summary>
    /// The generated LED panel config
    /// </summary>
    public LedPanelConfig Config => _config;

    // Predefined color palette
    private static readonly string[] PaletteColors =
    {
        "#FFFF0000", "#FFFF4500", "#FFFFA500", "#FFFFFF00", "#FF7FFF00",
        "#FF00FF00", "#FF00FA9A", "#FF00FFFF", "#FF1E90FF", "#FF0000FF",
        "#FF8A2BE2", "#FFFF00FF", "#FFFF1493", "#FFFFFFFF", "#FFC0C0C0",
        "#FF808080", "#FF404040", "#FF000000", "#FFFF6B6B", "#FFFFD93D",
        "#FF6BCB77", "#FF4D96FF", "#FFEE6C4D", "#FFF38BA8", "#FFA6E3A1",
        "#FF89B4FA", "#FFCBA6F7", "#FFFAB387"
    };

    public LedPanelDesignerWindow()
    {
        InitializeComponent();
        InitializeColorPalette();
        RebuildLedGrid();
        UpdateBrightnessText();
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(ApplyLocalization);

        var slider = this.FindControl<Slider>("BrightnessSlider");
        if (slider != null)
        {
            slider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Value")
                    UpdateBrightnessText();
            };
        }
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("Led.Title");
        SetText("StatusText",      L("Led.StatusReady"));
        SetText("CoordInfoText",   L("Led.HoverLeds"));
        SetText("PreviewPlayText", L("Led.PreviewPattern"));
        SetRadio("DrawSingleRadio",  L("Led.DrawSingle"));
        SetRadio("DrawRowRadio",     L("Led.DrawRow"));
        SetRadio("DrawColumnRadio",  L("Led.DrawColumn"));
        SetRadio("DrawEraseRadio",   L("Led.DrawErase"));
        SetRadio("DrawFillRadio",    L("Led.DrawFill"));
        SetCheckBox("ShowGridLinesCheck",  L("Led.GridLines"));
        SetCheckBox("ShowCoordsCheck",     L("Led.Coordinates"));
        SetCheckBox("InfiniteLoopCheck",   L("Led.InfiniteLoop"));
        SetButtonContent("ClearAll_Click_Btn",      L("Led.ClearAll"));
        SetButtonContent("InvertColors_Click_Btn",  L("Led.InvertColors"));
        SetButtonContent("RandomPattern_Click_Btn", L("Led.RandomPattern"));
        SetButtonContent("CaptureFrame_Click_Btn",  L("Led.CaptureFrame"));
        SetButtonContent("ClearFrames_Click_Btn",   L("Led.ClearFrames"));
    }

    private void SetText(string name, string text)
    {
        var tb = this.FindControl<TextBlock>(name);
        if (tb != null) tb.Text = text;
    }

    private void SetRadio(string name, string text)
    {
        var rb = this.FindControl<RadioButton>(name);
        if (rb != null) rb.Content = text;
    }

    private void SetCheckBox(string name, string text)
    {
        var cb = this.FindControl<CheckBox>(name);
        if (cb != null) cb.Content = text;
    }

    private void SetButtonContent(string name, string text)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Content = text;
    }

    public void SetProjectPath(string projectPath)
    {
        _projectPath = projectPath;
    }

    private void InitializeColorPalette()
    {
        var palette = this.FindControl<WrapPanel>("ColorPalette");
        if (palette == null) return;

        foreach (var color in PaletteColors)
        {
            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Background = new SolidColorBrush(Color.Parse(color)),
                Tag = color,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
                Margin = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            btn.Click += ColorSwatch_Click;
            palette.Children.Add(btn);
        }

        // Select first color
        SelectColor(PaletteColors[0]);
    }

    private void SelectColor(string color)
    {
        _currentColor = color;
        var preview = this.FindControl<Border>("CurrentColorPreview");
        if (preview != null)
        {
            preview.Background = new SolidColorBrush(Color.Parse(color));
        }

        // Update palette selection visual
        var palette = this.FindControl<WrapPanel>("ColorPalette");
        if (palette != null)
        {
            foreach (var child in palette.Children)
            {
                if (child is Button btn)
                {
                    btn.BorderBrush = btn.Tag?.ToString() == color
                        ? new SolidColorBrush(Color.Parse("#FFFAB387"))
                        : new SolidColorBrush(Color.Parse("#FF3D3D4D"));
                    btn.BorderThickness = btn.Tag?.ToString() == color
                        ? new Thickness(3)
                        : new Thickness(2);
                }
            }
        }
    }

    private void ColorSwatch_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string color)
        {
            SelectColor(color);
        }
    }

    private void CustomColor_Changed(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && !string.IsNullOrEmpty(tb.Text) && tb.Text.Length == 6)
        {
            try
            {
                var color = $"#FF{tb.Text}";
                Color.Parse(color);
                SelectColor(color);
            }
            catch { /* invalid color */ }
        }
    }

    private void UpdateBrightnessText()
    {
        var slider = this.FindControl<Slider>("BrightnessSlider");
        var text = this.FindControl<TextBlock>("BrightnessText");
        if (slider != null && text != null)
        {
            text.Text = ((int)slider.Value).ToString();
        }
    }

    #region LED Grid

    private void RebuildLedGrid()
    {
        var canvas = this.FindControl<Canvas>("LedCanvas");
        if (canvas == null) return;

        canvas.Children.Clear();
        _ledRects.Clear();

        var rows = _config.Rows;
        var cols = _config.Columns;

        // Calculate LED size to fit canvas
        var availableWidth = Math.Max(canvas.Width - 20, 100);
        var availableHeight = Math.Max(canvas.Height - 20, 100);

        var ledWidth = Math.Clamp((availableWidth - LED_PADDING * (cols + 1)) / cols, MIN_LED_SIZE, MAX_LED_SIZE);
        var ledHeight = Math.Clamp((availableHeight - LED_PADDING * (rows + 1)) / rows, MIN_LED_SIZE, MAX_LED_SIZE);
        var ledSize = Math.Min(ledWidth, ledHeight);

        // Resize canvas to fit
        var totalWidth = (ledSize + LED_PADDING) * cols + LED_PADDING + 20;
        var totalHeight = (ledSize + LED_PADDING) * rows + LED_PADDING + 20;
        canvas.Width = Math.Max(totalWidth, 400);
        canvas.Height = Math.Max(totalHeight, 400);

        var showGrid = this.FindControl<CheckBox>("ShowGridLinesCheck")?.IsChecked ?? true;
        var showCoords = this.FindControl<CheckBox>("ShowCoordsCheck")?.IsChecked ?? false;

        var offsetX = 10.0;
        var offsetY = 10.0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var x = offsetX + c * (ledSize + LED_PADDING);
                var y = offsetY + r * (ledSize + LED_PADDING);

                var key = _config.GetLedKey(r, c);
                var existingColor = _config.GetLedColor(r, c);

                var rect = new Rectangle
                {
                    Width = ledSize,
                    Height = ledSize,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = existingColor != null
                        ? new SolidColorBrush(Color.Parse(existingColor))
                        : new SolidColorBrush(Color.Parse("#FF2A2A3A")),
                    Stroke = showGrid == true
                        ? new SolidColorBrush(Color.Parse("#FF3D3D4D"))
                        : null,
                    StrokeThickness = showGrid == true ? 1 : 0,
                    Tag = $"{r},{c}"
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);
                _ledRects[key] = rect;

                // Show coordinates if enabled
                if (showCoords == true && ledSize >= 20)
                {
                    var coordText = new TextBlock
                    {
                        Text = $"{r},{c}",
                        FontSize = Math.Max(7, ledSize / 4),
                        Foreground = new SolidColorBrush(Color.Parse("#80FFFFFF")),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(coordText, x + 2);
                    Canvas.SetTop(coordText, y + 1);
                    canvas.Children.Add(coordText);
                }
            }
        }

        UpdateTotalLedsText();
    }

    private void UpdateTotalLedsText()
    {
        var text = this.FindControl<TextBlock>("TotalLedsText");
        if (text != null)
        {
            text.Text = _config.TotalLeds.ToString();
        }
    }

    private (int row, int col)? GetLedAtPoint(Point point)
    {
        var canvas = this.FindControl<Canvas>("LedCanvas");
        if (canvas == null) return null;

        var rows = _config.Rows;
        var cols = _config.Columns;

        var availableWidth = Math.Max(canvas.Width - 20, 100);
        var availableHeight = Math.Max(canvas.Height - 20, 100);

        var ledWidth = Math.Clamp((availableWidth - LED_PADDING * (cols + 1)) / cols, MIN_LED_SIZE, MAX_LED_SIZE);
        var ledHeight = Math.Clamp((availableHeight - LED_PADDING * (rows + 1)) / rows, MIN_LED_SIZE, MAX_LED_SIZE);
        var ledSize = Math.Min(ledWidth, ledHeight);

        var offsetX = 10.0;
        var offsetY = 10.0;

        var col = (int)((point.X - offsetX) / (ledSize + LED_PADDING));
        var row = (int)((point.Y - offsetY) / (ledSize + LED_PADDING));

        if (row >= 0 && row < rows && col >= 0 && col < cols)
        {
            return (row, col);
        }

        return null;
    }

    private void PaintLed(int row, int col)
    {
        var drawRow = this.FindControl<RadioButton>("DrawRowRadio");
        var drawCol = this.FindControl<RadioButton>("DrawColumnRadio");
        var drawErase = this.FindControl<RadioButton>("DrawEraseRadio");
        var drawFill = this.FindControl<RadioButton>("DrawFillRadio");

        if (drawErase?.IsChecked == true)
        {
            EraseLed(row, col);
        }
        else if (drawFill?.IsChecked == true)
        {
            FillAll(_currentColor);
        }
        else if (drawRow?.IsChecked == true)
        {
            _config.SetRowColor(row, _currentColor);
            RefreshLedVisuals();
        }
        else if (drawCol?.IsChecked == true)
        {
            _config.SetColumnColor(col, _currentColor);
            RefreshLedVisuals();
        }
        else
        {
            // Single LED
            _config.SetLedColor(row, col, _currentColor);
            UpdateSingleLedVisual(row, col);
        }

        UpdateStatus($"Painted LED [{row},{col}]");
    }

    private void EraseLed(int row, int col)
    {
        _config.ClearLed(row, col);
        var key = _config.GetLedKey(row, col);
        if (_ledRects.TryGetValue(key, out var rect))
        {
            rect.Fill = new SolidColorBrush(Color.Parse("#FF2A2A3A"));
        }
    }

    private void FillAll(string color)
    {
        for (int r = 0; r < _config.Rows; r++)
        for (int c = 0; c < _config.Columns; c++)
            _config.SetLedColor(r, c, color);
        RefreshLedVisuals();
    }

    private void UpdateSingleLedVisual(int row, int col)
    {
        var key = _config.GetLedKey(row, col);
        if (_ledRects.TryGetValue(key, out var rect))
        {
            var color = _config.GetLedColor(row, col);
            rect.Fill = color != null
                ? new SolidColorBrush(Color.Parse(color))
                : new SolidColorBrush(Color.Parse("#FF2A2A3A"));
        }
    }

    private void RefreshLedVisuals()
    {
        for (int r = 0; r < _config.Rows; r++)
        {
            for (int c = 0; c < _config.Columns; c++)
            {
                UpdateSingleLedVisual(r, c);
            }
        }
    }

    #endregion

    #region Event Handlers

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void PanelSize_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        var rowsInput = this.FindControl<NumericUpDown>("RowsInput");
        var colsInput = this.FindControl<NumericUpDown>("ColumnsInput");

        if (rowsInput?.Value != null) _config.Rows = (int)rowsInput.Value;
        if (colsInput?.Value != null) _config.Columns = (int)colsInput.Value;

        RebuildLedGrid();
    }

    private void ShowGridLines_Changed(object? sender, RoutedEventArgs e)
    {
        RebuildLedGrid();
    }

    private void LedCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDrawing = true;
        var pos = e.GetPosition(this.FindControl<Canvas>("LedCanvas"));
        var led = GetLedAtPoint(pos);
        if (led.HasValue)
        {
            PaintLed(led.Value.row, led.Value.col);
        }
    }

    private void LedCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this.FindControl<Canvas>("LedCanvas"));
        var led = GetLedAtPoint(pos);

        if (led.HasValue)
        {
            var coordInfo = this.FindControl<TextBlock>("CoordInfoText");
            if (coordInfo != null)
            {
                var color = _config.GetLedColor(led.Value.row, led.Value.col) ?? "OFF";
                coordInfo.Text = $"Row: {led.Value.row}  Col: {led.Value.col}  Color: {color}";
            }

            if (_isDrawing)
            {
                PaintLed(led.Value.row, led.Value.col);
            }
        }
    }

    private void LedCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDrawing = false;
    }

    private void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        _config.ClearAll();
        RefreshLedVisuals();
        UpdateStatus("Cleared all LEDs");
    }

    private void InvertColors_Click(object? sender, RoutedEventArgs e)
    {
        for (int r = 0; r < _config.Rows; r++)
        {
            for (int c = 0; c < _config.Columns; c++)
            {
                var color = _config.GetLedColor(r, c);
                if (color != null)
                {
                    _config.ClearLed(r, c);
                }
                else
                {
                    _config.SetLedColor(r, c, _currentColor);
                }
            }
        }
        RefreshLedVisuals();
        UpdateStatus("Inverted LED pattern");
    }

    private void RandomPattern_Click(object? sender, RoutedEventArgs e)
    {
        var rng = new Random();
        for (int r = 0; r < _config.Rows; r++)
        {
            for (int c = 0; c < _config.Columns; c++)
            {
                if (rng.Next(3) > 0) // 66% chance of being lit
                {
                    var color = PaletteColors[rng.Next(PaletteColors.Length)];
                    _config.SetLedColor(r, c, color);
                }
                else
                {
                    _config.ClearLed(r, c);
                }
            }
        }
        RefreshLedVisuals();
        UpdateStatus("Generated random pattern");
    }

    private void CaptureFrame_Click(object? sender, RoutedEventArgs e)
    {
        if (_config.Patterns.Count == 0)
        {
            _config.Patterns.Add(new LedPattern { Name = "Animation 1" });
        }

        var pattern = _config.Patterns[0];
        var delayInput = this.FindControl<NumericUpDown>("FrameDelayInput");
        var delay = delayInput?.Value != null ? (int)delayInput.Value : 100;

        // Sync loop settings from UI
        var infiniteCheck = this.FindControl<CheckBox>("InfiniteLoopCheck");
        pattern.InfiniteLoop = infiniteCheck?.IsChecked == true;
        pattern.Loop = true; // always looping (either infinite or fixed count)

        var loopCountInput = this.FindControl<NumericUpDown>("LoopCountInput");
        if (loopCountInput?.Value != null)
            pattern.DelayMs = (int)loopCountInput.Value; // reuse DelayMs as loop count storage

        var frame = new LedFrame { DurationMs = delay };
        frame.CopyFromConfig(_config);
        pattern.Frames.Add(frame);

        var frameText = this.FindControl<TextBlock>("FrameCountText");
        if (frameText != null)
        {
            frameText.Text = string.Format(LocalizationService.Get("Led.Frames"), pattern.Frames.Count);
        }

        UpdateStatus($"Captured frame {pattern.Frames.Count}");
    }

    private void InfiniteLoop_Changed(object? sender, RoutedEventArgs e)
    {
        var infiniteCheck = this.FindControl<CheckBox>("InfiniteLoopCheck");
        var loopCountPanel = this.FindControl<StackPanel>("LoopCountPanel");
        if (loopCountPanel != null)
            loopCountPanel.IsVisible = infiniteCheck?.IsChecked != true;

        // Sync to pattern if it exists
        if (_config.Patterns.Count > 0)
            _config.Patterns[0].InfiniteLoop = infiniteCheck?.IsChecked == true;
    }

    private void ClearFrames_Click(object? sender, RoutedEventArgs e)
    {
        _config.Patterns.Clear();
        var frameText = this.FindControl<TextBlock>("FrameCountText");
        if (frameText != null) frameText.Text = string.Format(LocalizationService.Get("Led.Frames"), 0);
        UpdateStatus("Cleared all animation frames");
    }

    private async void PreviewPlay_Click(object? sender, RoutedEventArgs e)
    {
        if (_config.Patterns.Count == 0 || _config.Patterns[0].Frames.Count == 0)
        {
            UpdateStatus("No frames to preview. Capture frames first!");
            return;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();

        var playText = this.FindControl<TextBlock>("PreviewPlayText");
        if (playText != null) playText.Text = LocalizationService.Get("Led.Playing");

        try
        {
            var pattern = _config.Patterns[0];
            var token = _previewCts.Token;
            var statusText = this.FindControl<TextBlock>("PreviewStatusText");

            for (int loop = 0; loop < 5 && !token.IsCancellationRequested; loop++)
            {
                for (int f = 0; f < pattern.Frames.Count && !token.IsCancellationRequested; f++)
                {
                    var frame = pattern.Frames[f];
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (statusText != null)
                            statusText.Text = string.Format(LocalizationService.Get("Led.FrameStatus"), f + 1, pattern.Frames.Count, loop + 1);

                        // Apply frame to grid
                        _config.LedColors.Clear();
                        foreach (var kvp in frame.LedColors)
                        {
                            _config.LedColors[kvp.Key] = kvp.Value;
                        }
                        RefreshLedVisuals();
                    });

                    await Task.Delay(frame.DurationMs, token);
                }
            }
        }
        catch (TaskCanceledException) { }
        finally
        {
            if (playText != null) playText.Text = LocalizationService.Get("Led.PreviewPattern");
            var statusText = this.FindControl<TextBlock>("PreviewStatusText");
            if (statusText != null) statusText.Text = LocalizationService.Get("Led.PreviewComplete");
        }
    }

    private void PreviewStop_Click(object? sender, RoutedEventArgs e)
    {
        _previewCts?.Cancel();
        var playText = this.FindControl<TextBlock>("PreviewPlayText");
        if (playText != null) playText.Text = LocalizationService.Get("Led.PreviewPattern");
        UpdateStatus("Preview stopped");
    }

    private async void SaveConfig_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.Get("Led.SaveDialogTitle"),
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } }
                }
            });

            if (file != null)
            {
                var path = file.Path.LocalPath;
                SyncConfigFromUI();
                await File.WriteAllTextAsync(path, _config.ToJson());
                UpdateStatus($"Saved to {Path.GetFileName(path)}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error saving: {ex.Message}");
        }
    }

    private async void LoadConfig_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationService.Get("Led.LoadDialogTitle"),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } }
                }
            });

            if (result.Count > 0)
            {
                var json = await File.ReadAllTextAsync(result[0].Path.LocalPath);
                var loaded = LedPanelConfig.FromJson(json);
                if (loaded != null)
                {
                    _config.Rows = loaded.Rows;
                    _config.Columns = loaded.Columns;
                    _config.GpioPin = loaded.GpioPin;
                    _config.Brightness = loaded.Brightness;
                    _config.StripType = loaded.StripType;
                    _config.LedColors = loaded.LedColors;
                    _config.RowColors = loaded.RowColors;
                    _config.ColumnColors = loaded.ColumnColors;
                    _config.Patterns = loaded.Patterns;

                    // Update UI inputs
                    var rowsInput = this.FindControl<NumericUpDown>("RowsInput");
                    var colsInput = this.FindControl<NumericUpDown>("ColumnsInput");
                    var gpioInput = this.FindControl<NumericUpDown>("GpioPinInput");
                    var brightnessSlider = this.FindControl<Slider>("BrightnessSlider");

                    if (rowsInput != null) rowsInput.Value = _config.Rows;
                    if (colsInput != null) colsInput.Value = _config.Columns;
                    if (gpioInput != null) gpioInput.Value = _config.GpioPin;
                    if (brightnessSlider != null) brightnessSlider.Value = _config.Brightness;

                    // Restore animation settings
                    if (_config.Patterns.Count > 0)
                    {
                        var pattern = _config.Patterns[0];
                        var infiniteCheck = this.FindControl<CheckBox>("InfiniteLoopCheck");
                        var loopCountPanel = this.FindControl<StackPanel>("LoopCountPanel");
                        var loopCountInput = this.FindControl<NumericUpDown>("LoopCountInput");
                        var frameText = this.FindControl<TextBlock>("FrameCountText");

                        if (infiniteCheck != null) infiniteCheck.IsChecked = pattern.InfiniteLoop;
                        if (loopCountPanel != null) loopCountPanel.IsVisible = !pattern.InfiniteLoop;
                        if (loopCountInput != null) loopCountInput.Value = pattern.DelayMs > 0 ? pattern.DelayMs : 10;
                        if (frameText != null) frameText.Text = string.Format(LocalizationService.Get("Led.Frames"), pattern.Frames.Count);
                    }

                    RebuildLedGrid();
                    UpdateStatus($"Loaded config: {_config.Rows}x{_config.Columns}, {_config.LedColors.Count} LEDs");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error loading: {ex.Message}");
        }
    }

    private async void GenerateCode_Click(object? sender, RoutedEventArgs e)
    {
        SyncConfigFromUI();

        var code = _codeGenerator.GenerateLedPanelCode(_config);

        // If project path is set, save directly to project
        if (!string.IsNullOrEmpty(_projectPath))
        {
            await _codeGenerator.SaveToProjectAsync(_config, _projectPath);
            UpdateStatus("Code generated and saved to project!");
        }

        CodeGenerated?.Invoke(this, code);
        UpdateStatus("ESP32 LED panel code generated!");
    }

    #endregion

    #region Helpers

    private void SyncConfigFromUI()
    {
        var gpioInput = this.FindControl<NumericUpDown>("GpioPinInput");
        var brightnessSlider = this.FindControl<Slider>("BrightnessSlider");
        var stripCombo = this.FindControl<ComboBox>("StripTypeCombo");

        if (gpioInput?.Value != null) _config.GpioPin = (int)gpioInput.Value;
        if (brightnessSlider != null) _config.Brightness = (int)brightnessSlider.Value;
        if (stripCombo?.SelectedIndex >= 0)
            _config.StripType = (LedStripType)stripCombo.SelectedIndex;

        // Sync animation loop settings
        if (_config.Patterns.Count > 0)
        {
            var pattern = _config.Patterns[0];
            var infiniteCheck = this.FindControl<CheckBox>("InfiniteLoopCheck");
            var loopCountInput = this.FindControl<NumericUpDown>("LoopCountInput");

            pattern.InfiniteLoop = infiniteCheck?.IsChecked == true;
            pattern.Loop = true;
            if (loopCountInput?.Value != null)
                pattern.DelayMs = (int)loopCountInput.Value;
        }
    }

    private void UpdateStatus(string message)
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status != null) status.Text = message;
    }

    #endregion
}

