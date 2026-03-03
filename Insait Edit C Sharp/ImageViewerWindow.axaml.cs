using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class ImageViewerWindow : Window
{
    private readonly string _filePath;
    private Bitmap? _bitmap;
    private double _zoomLevel = 1.0;
    private const double ZoomStep = 0.25;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 10.0;

    public ImageViewerWindow()
    {
        InitializeComponent();
        _filePath = string.Empty;
        ApplyLocalization();
    }

    public ImageViewerWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        LoadImage();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("ImageViewer.Title");
        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText != null) titleText.Text = L("ImageViewer.Title");
        var zoomIn = this.FindControl<Button>("ZoomInBtn");
        if (zoomIn != null) ToolTip.SetTip(zoomIn, L("ImageViewer.ZoomIn"));
        var zoomOut = this.FindControl<Button>("ZoomOutBtn");
        if (zoomOut != null) ToolTip.SetTip(zoomOut, L("ImageViewer.ZoomOut"));
        var fit = this.FindControl<Button>("FitToWindowBtn");
        if (fit != null) ToolTip.SetTip(fit, L("ImageViewer.FitToWindow"));
        var actual = this.FindControl<Button>("ActualSizeBtn");
        if (actual != null) ToolTip.SetTip(actual, L("ImageViewer.ActualSize"));
        var ext = this.FindControl<Button>("OpenExternalBtn");
        if (ext != null) ToolTip.SetTip(ext, L("ImageViewer.OpenExternal"));
    }

    private void LoadImage()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                ShowError("File not found");
                return;
            }

            var ext = Path.GetExtension(_filePath).ToLowerInvariant();
            var fileName = Path.GetFileName(_filePath);
            
            TitleText.Text = $"Image Viewer — {fileName}";
            Title = $"Image Viewer — {fileName}";
            FileNameText.Text = fileName;

            // Get file size
            var fileInfo = new FileInfo(_filePath);
            FileSizeText.Text = FormatFileSize(fileInfo.Length);
            FileTypeText.Text = ext.TrimStart('.').ToUpperInvariant();

            if (ext == ".svg")
            {
                // SVG files can't be loaded as Bitmap - show message
                ShowError("SVG preview is not supported.\nUse 'Open in system viewer' to view this file.");
                DimensionsText.Text = "SVG";
                return;
            }

            // Load as bitmap
            using var stream = File.OpenRead(_filePath);
            _bitmap = new Bitmap(stream);
            
            ImageDisplay.Source = _bitmap;
            DimensionsText.Text = $"{_bitmap.PixelSize.Width} × {_bitmap.PixelSize.Height} px";

            // Auto fit to window
            Opened += (_, _) => FitToWindow();
        }
        catch (Exception ex)
        {
            ShowError($"Cannot load image: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ImageDisplay.IsVisible = false;
        var errorText = new TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        ImagePanel.Children.Add(errorText);
    }

    private void ApplyZoom()
    {
        if (_bitmap == null) return;

        ImageDisplay.Width = _bitmap.PixelSize.Width * _zoomLevel;
        ImageDisplay.Height = _bitmap.PixelSize.Height * _zoomLevel;
        
        ZoomLevelText.Text = $"{Math.Round(_zoomLevel * 100)}%";
    }

    private void FitToWindow()
    {
        if (_bitmap == null) return;

        var scrollViewer = ImageScrollViewer;
        var availableWidth = scrollViewer.Bounds.Width - 20;
        var availableHeight = scrollViewer.Bounds.Height - 20;

        if (availableWidth <= 0 || availableHeight <= 0) return;

        var scaleX = availableWidth / _bitmap.PixelSize.Width;
        var scaleY = availableHeight / _bitmap.PixelSize.Height;
        _zoomLevel = Math.Min(scaleX, scaleY);
        _zoomLevel = Math.Max(MinZoom, Math.Min(MaxZoom, _zoomLevel));

        ApplyZoom();
    }

    #region Event Handlers

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _bitmap?.Dispose();
        Close();
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Min(MaxZoom, _zoomLevel + ZoomStep);
        ApplyZoom();
    }

    private void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Max(MinZoom, _zoomLevel - ZoomStep);
        ApplyZoom();
    }

    private void FitToWindow_Click(object? sender, RoutedEventArgs e)
    {
        FitToWindow();
    }

    private void ActualSize_Click(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    private void OpenExternal_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open external: {ex.Message}");
        }
    }

    private void Image_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_bitmap == null) return;

        var delta = e.Delta.Y;
        if (delta > 0)
            _zoomLevel = Math.Min(MaxZoom, _zoomLevel * 1.15);
        else if (delta < 0)
            _zoomLevel = Math.Max(MinZoom, _zoomLevel / 1.15);

        ApplyZoom();
        e.Handled = true;
    }

    #endregion

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

