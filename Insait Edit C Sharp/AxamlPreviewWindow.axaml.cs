using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;
using System;
using System.IO;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Preview window shell — all XAML loading is delegated to <see cref="AxamlLiveHost"/>.
/// </summary>
public partial class AxamlPreviewWindow : Window
{
    // ── state ──────────────────────────────────────────────────────────
    private string _filePath = string.Empty;
    private string _xamlText = string.Empty;
    private bool   _darkBg   = true;
    private bool   _isMaximized;
    private PixelPoint _restorePos;
    private Size       _restoreSize;
    private (double W, double H)? _fixedSize;
    private string? _lastError;          // shown in the separate error window

    private static readonly IBrush DarkBg  = new SolidColorBrush(Color.Parse("#FF1F1A24"));
    private static readonly IBrush LightBg = new SolidColorBrush(Colors.White);

    // The compiled host control declared in AxamlPreviewWindow.axaml
    private AxamlLiveHost?    _liveHost;
    private PreviewErrorWindow? _errorWindow;   // lazily created, reused

    // ── constructors ───────────────────────────────────────────────────
    public AxamlPreviewWindow() { InitializeComponent(); AttachHost(); ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    public AxamlPreviewWindow(string filePath)
    {
        InitializeComponent();
        _filePath    = filePath;
        _restoreSize = new Size(Width, Height);
        AttachHost();
        LoadFromFile();
        ApplyLocalization();
    }

    public AxamlPreviewWindow(string filePath, string content)
    {
        InitializeComponent();
        _filePath    = filePath;
        _xamlText    = content;
        _restoreSize = new Size(Width, Height);
        AttachHost();
        LoadFromText(content);
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("AxamlPreview.Title");
    }

    // ── host wiring ────────────────────────────────────────────────────

    private void AttachHost()
    {
        // PreviewContent is the ContentControl defined in AxamlPreviewWindow.axaml
        var host = this.FindControl<ContentControl>("PreviewContent");
        if (host == null) return;
        _liveHost        = new AxamlLiveHost();
        host.Content     = _liveHost;
    }

    // ── public API ─────────────────────────────────────────────────────
    public void Reload()
    {
        if (!string.IsNullOrEmpty(_xamlText)) LoadFromText(_xamlText);
        else LoadFromFile();
    }

    public void UpdateContent(string xaml) { _xamlText = xaml; LoadFromText(xaml); }

    // ── loading ────────────────────────────────────────────────────────
    private void LoadFromFile()
    {
        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
        {
            SetError("File not found: " + _filePath);
            return;
        }
        UpdateTitleLabels(Path.GetFileName(_filePath));
        _liveHost?.LoadFile(_filePath);
        UpdateStatusFromHost();
    }

    private void LoadFromText(string xaml)
    {
        var name = string.IsNullOrEmpty(_filePath) ? "Untitled.axaml" : Path.GetFileName(_filePath);
        UpdateTitleLabels(name);
        _liveHost?.LoadXaml(xaml, _filePath);
        UpdateStatusFromHost();
    }

    private void UpdateTitleLabels(string name)
    {
        var fmt = LocalizationService.Get("AxamlPreview.TitleFormat");
        var titleStr = string.Format(fmt, name);
        if (TitleText     != null) TitleText.Text     = titleStr;
        if (FileNameLabel != null) FileNameLabel.Text = name;
        Title = titleStr;
    }

    private void UpdateStatusFromHost()
    {
        if (_liveHost == null) return;

        if (_liveHost.IsLiveRender)
        {
            SetStatus(LocalizationService.Get("AxamlPreview.LivePreview"), "#FFA6E3A1");
            ClearError();
        }
        else
        {
            SetError(_liveHost.LastError ?? LocalizationService.Get("AxamlPreview.UnknownError"));
        }
    }

    // ── error management ───────────────────────────────────────────────
    private void SetError(string message)
    {
        _lastError = message;

        SetStatus(LocalizationService.Get("AxamlPreview.FallbackView"), "#FFFFC09F");

        // Show the error button in the toolbar
        var btn = this.FindControl<Button>("ErrorButton");
        if (btn != null) btn.IsVisible = true;

        // If the error window is already open — update it live
        _errorWindow?.UpdateError(message);
    }

    private void ClearError()
    {
        _lastError = null;
        var btn = this.FindControl<Button>("ErrorButton");
        if (btn != null) btn.IsVisible = false;
        _errorWindow?.Close();
        _errorWindow = null;
    }

    // ── UI helpers ─────────────────────────────────────────────────────
    private void ApplyCanvasSize()
    {
        var canvas = this.FindControl<Border>("PreviewCanvas");
        if (canvas == null) return;
        canvas.Width  = _fixedSize.HasValue ? _fixedSize.Value.W : double.NaN;
        canvas.Height = _fixedSize.HasValue ? _fixedSize.Value.H : double.NaN;
    }

    private void SetStatus(string msg, string hexColor)
    {
        var lbl = this.FindControl<TextBlock>("StatusLabel");
        if (lbl == null) return;
        lbl.Text       = msg;
        lbl.Foreground = new SolidColorBrush(Color.Parse(hexColor));
    }

    // ── event handlers ─────────────────────────────────────────────────
    private void ErrorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_errorWindow == null || !_errorWindow.IsVisible)
        {
            _errorWindow = new PreviewErrorWindow(_lastError ?? string.Empty);
            _errorWindow.Closed += (object? s, System.EventArgs a) => _errorWindow = null;
            _errorWindow.Show(this);
        }
        else
        {
            _errorWindow.Activate();
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        if (_isMaximized)
        {
            WindowState = WindowState.Normal;
            Position    = _restorePos;
            Width       = _restoreSize.Width;
            Height      = _restoreSize.Height;
        }
        else
        {
            _restorePos  = Position;
            _restoreSize = new Size(Width, Height);
            WindowState  = WindowState.Maximized;
        }
        _isMaximized = !_isMaximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)   => Close();
    private void Refresh_Click(object? sender, RoutedEventArgs e) => Reload();

    private void ToggleBackground_Click(object? sender, RoutedEventArgs e)
    {
        _darkBg = !_darkBg;
        var canvas = this.FindControl<Border>("PreviewCanvas");
        if (canvas != null) canvas.Background = _darkBg ? DarkBg : LightBg;
    }

    private void SizePhone_Click(object? sender, RoutedEventArgs e)
        { _fixedSize = (360, 640);  UpdateSizeLabel("360×640");  ApplyCanvasSize(); }
    private void SizeTablet_Click(object? sender, RoutedEventArgs e)
        { _fixedSize = (768, 1024); UpdateSizeLabel("768×1024"); ApplyCanvasSize(); }
    private void SizeDesktop_Click(object? sender, RoutedEventArgs e)
        { _fixedSize = (1280, 800); UpdateSizeLabel("1280×800"); ApplyCanvasSize(); }
    private void SizeFill_Click(object? sender, RoutedEventArgs e)
        { _fixedSize = null;        UpdateSizeLabel("Free");     ApplyCanvasSize(); }

    private void UpdateSizeLabel(string text)
    {
        var lbl = this.FindControl<TextBlock>("SizeLabel");
        if (lbl != null) lbl.Text = text;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if      (e.Key == Key.F5)     { Reload(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close();  e.Handled = true; }
    }
}
