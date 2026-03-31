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
    private bool _isCancelling;
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
        LocalizationService.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void ApplyLocalization()
    {
        var currentTitle = GetCurrentWindowTitle();
        Title = currentTitle;

        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText != null) titleText.Text = currentTitle;

        var projectNameText = this.FindControl<TextBlock>("ProjectNameText");
        if (projectNameText != null)
            projectNameText.Text = Path.GetFileNameWithoutExtension(_profile.ProjectPath);

        UpdateLocalizedStatus();
        UpdateLocalizedResultInfo();
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
        _isCancelling = false;
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
            SetStatus("⏳", LocalizationService.Get("PublishProgress.Publishing"), BuildPublishDetail());
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
        _isCancelling = false;

        var progressBar = this.FindControl<ProgressBar>("PublishProgressBar");
        if (progressBar != null)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = 100;
            progressBar.Value = 100;
        }

        if (_result != null && _result.Success)
        {
            if (progressBar != null)
                progressBar.Foreground = Avalonia.Media.Brushes.LimeGreen;

            // Show result info
            var resultPanel = this.FindControl<StackPanel>("ResultInfoPanel");
            if (resultPanel != null) resultPanel.IsVisible = true;

            UpdateLocalizedResultInfo();
        }
        else
        {
            if (progressBar != null)
                progressBar.Foreground = Avalonia.Media.Brushes.IndianRed;
        }

        ApplyLocalization();
    }

    // ═══════════════════════════════════════════════════════════
    //  Button handlers
    // ═══════════════════════════════════════════════════════════

    private void CancelPublish_Click(object? sender, RoutedEventArgs e)
    {
        _isCancelling = true;
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
            _isCancelling = true;
            _publishService.Cancel();
        }
        Close();
    }

    private string GetCurrentWindowTitle()
    {
        if (_result?.Success == true)
            return LocalizationService.Get("PublishProgress.SucceededTitle");

        if (_result != null)
            return LocalizationService.Get("PublishProgress.FailedTitle");

        return LocalizationService.Get("PublishProgress.Title");
    }

    private void UpdateLocalizedStatus()
    {
        if (_result?.Success == true)
        {
            SetStatus("✅", LocalizationService.Get("PublishProgress.Succeeded"), _profile.OutputPath);
            return;
        }

        if (_result != null)
        {
            SetStatus("❌", LocalizationService.Get("PublishProgress.Failed"), BuildFailureDetailMessage());
            return;
        }

        if (_isPublishing)
        {
            if (_isCancelling)
            {
                SetStatus("🚫", LocalizationService.Get("PublishProgress.Cancelling"));
                return;
            }

            SetStatus("⏳", LocalizationService.Get("PublishProgress.Publishing"), BuildPublishDetail());
            return;
        }

        SetStatus("⏳", LocalizationService.Get("PublishProgress.Preparing"));
    }

    private void UpdateLocalizedResultInfo()
    {
        var sizeText = this.FindControl<TextBlock>("OutputSizeText");
        if (sizeText != null)
        {
            if (_result?.Success == true && Directory.Exists(_profile.OutputPath))
            {
                var size = GetDirectorySize(_profile.OutputPath);
                sizeText.Text = string.Format(LocalizationService.Get("PublishProgress.OutputSize"), FormatFileSize(size));
            }
            else
            {
                sizeText.Text = string.Empty;
            }
        }

        var pathText = this.FindControl<TextBlock>("OutputPathText");
        if (pathText != null)
        {
            pathText.Text = _result?.Success == true ? _profile.OutputPath : string.Empty;
        }
    }

    private string BuildPublishDetail()
    {
        return _profile.Configuration + " | " + (_profile.RuntimeIdentifier ?? LocalizationService.Get("PublishProgress.PortableRuntime"));
    }

    private string BuildFailureDetailMessage()
    {
        var outputText = _output.ToString();
        var errorCount = outputText.Split('\n')
            .Count(l => l.Contains(" error ", StringComparison.OrdinalIgnoreCase)
                     || l.Contains(": error ", StringComparison.OrdinalIgnoreCase));

        if (errorCount > 0)
        {
            return string.Format(LocalizationService.Get("PublishProgress.CompilationErrors"), errorCount);
        }

        return _result?.ErrorMessage ?? LocalizationService.Get("PublishProgress.UnknownError");
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


