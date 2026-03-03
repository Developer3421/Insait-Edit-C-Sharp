// ============================================================
//  PublishProgressWindow.axaml.cs
//  Real-time publish progress window with console output
//  and "Open Folder" action on completion.
// ============================================================
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Insait_Edit_C_Sharp;

public partial class PublishProgressWindow : Window
{
    private readonly PublishService _publishService;
    private readonly PublishProfile _profile;
    private readonly StringBuilder _output = new();
    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _elapsedTimer;
    private bool _isPublishing;
    private PublishResult? _result;

    /// <summary>The result of the publish operation. Null if not yet completed.</summary>
    public PublishResult? PublishResult => _result;

    public PublishProgressWindow() : this(new PublishService(), new PublishProfile()) { }

    public PublishProgressWindow(PublishService publishService, PublishProfile profile)
    {
        InitializeComponent();
        _publishService = publishService;
        _profile = profile;

        SetupEventHandlers();
        ApplyLocalization();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;

        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText != null) titleText.Text = L("PublishProgress.Title");

        var projectNameText = this.FindControl<TextBlock>("ProjectNameText");
        if (projectNameText != null)
            projectNameText.Text = Path.GetFileNameWithoutExtension(_profile.ProjectPath);
    }

    private void SetupEventHandlers()
    {
        // Title bar drag
        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
            titleBar.PointerPressed += TitleBar_PointerPressed;

        // Close / Cancel
        var closeBtn = this.FindControl<Button>("CloseButton");
        if (closeBtn != null) closeBtn.Click += (_, _) => TryClose();

        var closeWindowBtn = this.FindControl<Button>("CloseWindowButton");
        if (closeWindowBtn != null) closeWindowBtn.Click += (_, _) => TryClose();

        var cancelBtn = this.FindControl<Button>("CancelPublishButton");
        if (cancelBtn != null) cancelBtn.Click += CancelPublish_Click;

        // Open Folder
        var openFolderBtn = this.FindControl<Button>("OpenFolderButton");
        if (openFolderBtn != null) openFolderBtn.Click += OpenFolder_Click;

        // Output path click
        var outputPathText = this.FindControl<TextBlock>("OutputPathText");
        if (outputPathText != null)
            outputPathText.PointerPressed += (_, _) => OpenOutputFolder();

        // Clear output
        var clearBtn = this.FindControl<Button>("ClearOutputButton");
        if (clearBtn != null) clearBtn.Click += (_, _) =>
        {
            _output.Clear();
            var t = this.FindControl<SelectableTextBlock>("OutputText");
            if (t != null) t.Text = string.Empty;
        };
    }

    /// <summary>
    /// Start the publish process. Call this after ShowDialog or Show.
    /// </summary>
    public async void StartPublish()
    {
        _isPublishing = true;
        UpdateUIState();

        // Wire events
        _publishService.OutputReceived += OnOutputReceived;
        _publishService.PublishStarted += OnPublishStarted;
        _publishService.PublishCompleted += OnPublishCompleted;

        // Start elapsed timer
        _stopwatch.Start();
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();
        _elapsedTimer.Start();

        try
        {
            _result = await _publishService.PublishAsync(_profile);
        }
        catch (Exception ex)
        {
            AppendOutput($"\n[EXCEPTION] {ex.Message}\n");
            _result = new PublishResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Output = _output.ToString()
            };
        }
        finally
        {
            _publishService.OutputReceived -= OnOutputReceived;
            _publishService.PublishStarted -= OnPublishStarted;
            _publishService.PublishCompleted -= OnPublishCompleted;

            _stopwatch.Stop();
            _elapsedTimer?.Stop();
            _isPublishing = false;
            UpdateElapsedTime();
            OnFinished();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Service event handlers
    // ═══════════════════════════════════════════════════════════

    private void OnOutputReceived(object? sender, PublishOutputEventArgs e)
    {
        Dispatcher.UIThread.Post(() => AppendOutput(e.Output));
    }

    private void OnPublishStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SetStatus("⏳", LocalizationService.Get("PublishProgress.Publishing"),
                      _profile.Configuration + " | " + (_profile.RuntimeIdentifier ?? "Portable"));
        });
    }

    private void OnPublishCompleted(object? sender, PublishCompletedEventArgs e)
    {
        // Final state is handled in StartPublish finally block
    }

    // ═══════════════════════════════════════════════════════════
    //  UI helpers
    // ═══════════════════════════════════════════════════════════

    private void AppendOutput(string text)
    {
        _output.Append(text);
        var t = this.FindControl<SelectableTextBlock>("OutputText");
        if (t != null) t.Text = _output.ToString();
        this.FindControl<ScrollViewer>("OutputScrollViewer")?.ScrollToEnd();
    }

    private void SetStatus(string icon, string status, string? detail = null)
    {
        var iconBlock = this.FindControl<TextBlock>("StatusIcon");
        var statusBlock = this.FindControl<TextBlock>("StatusText");
        var detailBlock = this.FindControl<TextBlock>("StatusDetail");

        if (iconBlock != null) iconBlock.Text = icon;
        if (statusBlock != null) statusBlock.Text = status;
        if (detailBlock != null) detailBlock.Text = detail ?? string.Empty;
    }

    private void UpdateElapsedTime()
    {
        var tb = this.FindControl<TextBlock>("ElapsedTimeText");
        if (tb != null)
        {
            var ts = _stopwatch.Elapsed;
            tb.Text = ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}"
                : $"0:{ts.Seconds:D2}";
        }
    }

    private void UpdateUIState()
    {
        var cancelBtn = this.FindControl<Button>("CancelPublishButton");
        var openFolderBtn = this.FindControl<Button>("OpenFolderButton");
        var progressBar = this.FindControl<ProgressBar>("PublishProgressBar");

        if (cancelBtn != null) cancelBtn.IsVisible = _isPublishing;
        if (openFolderBtn != null) openFolderBtn.IsVisible = !_isPublishing && _result != null;
        if (progressBar != null) progressBar.IsIndeterminate = _isPublishing;
    }

    private void OnFinished()
    {
        UpdateUIState();

        var progressBar = this.FindControl<ProgressBar>("PublishProgressBar");
        if (progressBar != null)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = 100;
            progressBar.Value = 100;
        }

        if (_result != null && _result.Success)
        {
            SetStatus("✅", LocalizationService.Get("PublishProgress.Succeeded"),
                      _profile.OutputPath);

            if (progressBar != null)
                progressBar.Foreground = Avalonia.Media.Brushes.LimeGreen;

            // Show result info
            var resultPanel = this.FindControl<StackPanel>("ResultInfoPanel");
            if (resultPanel != null) resultPanel.IsVisible = true;

            var sizeText = this.FindControl<TextBlock>("OutputSizeText");
            if (sizeText != null && Directory.Exists(_profile.OutputPath))
            {
                var size = GetDirectorySize(_profile.OutputPath);
                sizeText.Text = $"📊 {FormatFileSize(size)}";
            }

            var pathText = this.FindControl<TextBlock>("OutputPathText");
            if (pathText != null) pathText.Text = _profile.OutputPath;

            var titleText = this.FindControl<TextBlock>("TitleText");
            if (titleText != null) titleText.Text = LocalizationService.Get("PublishProgress.SucceededTitle");
        }
        else
        {
            // Extract error count from output for a more specific message
            var outputText = _output.ToString();
            var errorCount = outputText.Split('\n')
                .Count(l => l.Contains(" error ", StringComparison.OrdinalIgnoreCase)
                         || l.Contains(": error ", StringComparison.OrdinalIgnoreCase));

            var detailMsg = errorCount > 0
                ? $"{errorCount} compilation error(s) — see console output below"
                : (_result?.ErrorMessage ?? "Unknown error");

            SetStatus("❌", LocalizationService.Get("PublishProgress.Failed"), detailMsg);

            if (progressBar != null)
                progressBar.Foreground = Avalonia.Media.Brushes.IndianRed;

            var titleText = this.FindControl<TextBlock>("TitleText");
            if (titleText != null) titleText.Text = LocalizationService.Get("PublishProgress.FailedTitle");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Button handlers
    // ═══════════════════════════════════════════════════════════

    private void CancelPublish_Click(object? sender, RoutedEventArgs e)
    {
        _publishService.Cancel();
        SetStatus("🚫", LocalizationService.Get("PublishProgress.Cancelling"));
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        OpenOutputFolder();
    }

    private void OpenOutputFolder()
    {
        var path = _profile.OutputPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback: try explorer.exe explicitly
            try
            {
                Process.Start("explorer.exe", path);
            }
            catch { /* ignore */ }
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void TryClose()
    {
        if (_isPublishing)
        {
            _publishService.Cancel();
        }
        Close();
    }

    // ═══════════════════════════════════════════════════════════
    //  Utility
    // ═══════════════════════════════════════════════════════════

    private static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int idx = 0;
        double size = bytes;
        while (size >= 1024 && idx < suffixes.Length - 1) { size /= 1024; idx++; }
        return $"{size:0.##} {suffixes[idx]}";
    }
}


