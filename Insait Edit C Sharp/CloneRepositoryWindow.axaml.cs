using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Insait_Edit_C_Sharp;

public partial class CloneRepositoryWindow : Window
{
    private readonly string _defaultPath;
    public string? ClonedPath { get; private set; }

    public CloneRepositoryWindow()
    {
        InitializeComponent();
        
        _defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos");
        
        var localPathBox = this.FindControl<TextBox>("LocalPathBox");
        if (localPathBox != null)
        {
            localPathBox.Text = _defaultPath;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RepoUrl_Changed(object? sender, TextChangedEventArgs e)
    {
        var repoUrlBox = this.FindControl<TextBox>("RepoUrlBox");
        var localPathBox = this.FindControl<TextBox>("LocalPathBox");
        var cloneButton = this.FindControl<Button>("CloneButton");

        if (repoUrlBox == null || localPathBox == null || cloneButton == null) return;

        var url = repoUrlBox.Text ?? string.Empty;
        var isValidUrl = IsValidGitUrl(url);
        
        cloneButton.IsEnabled = isValidUrl;

        // Auto-fill local path based on repo name
        if (isValidUrl)
        {
            var repoName = ExtractRepoName(url);
            if (!string.IsNullOrEmpty(repoName))
            {
                localPathBox.Text = Path.Combine(_defaultPath, repoName);
            }
        }
    }

    private bool IsValidGitUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        
        // HTTPS URL
        if (url.StartsWith("https://") && url.Contains(".git"))
            return true;
        
        // SSH URL
        if (url.StartsWith("git@") && url.Contains(":"))
            return true;
        
        // GitHub/GitLab shorthand
        if (Regex.IsMatch(url, @"^[\w-]+/[\w-]+$"))
            return true;

        return false;
    }

    private string ExtractRepoName(string url)
    {
        try
        {
            // Remove .git suffix
            url = url.TrimEnd('/');
            if (url.EndsWith(".git"))
                url = url[..^4];

            // Get last segment
            var lastSlash = url.LastIndexOf('/');
            var lastColon = url.LastIndexOf(':');
            var lastSeparator = Math.Max(lastSlash, lastColon);
            
            if (lastSeparator >= 0)
                return url[(lastSeparator + 1)..];

            return url;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async void BrowsePath_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Clone Location",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var localPathBox = this.FindControl<TextBox>("LocalPathBox");
            if (localPathBox != null)
            {
                localPathBox.Text = folders[0].Path.LocalPath;
            }
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Clone_Click(object? sender, RoutedEventArgs e)
    {
        var repoUrlBox = this.FindControl<TextBox>("RepoUrlBox");
        var localPathBox = this.FindControl<TextBox>("LocalPathBox");
        var statusPanel = this.FindControl<Border>("StatusPanel");
        var statusText = this.FindControl<TextBlock>("StatusText");
        var statusIcon = this.FindControl<TextBlock>("StatusIcon");
        var cloneButton = this.FindControl<Button>("CloneButton");

        if (repoUrlBox == null || localPathBox == null) return;

        var repoUrl = repoUrlBox.Text?.Trim() ?? string.Empty;
        var localPath = localPathBox.Text?.Trim() ?? _defaultPath;

        if (string.IsNullOrEmpty(repoUrl)) return;

        // Show status
        if (statusPanel != null) statusPanel.IsVisible = true;
        if (statusText != null) statusText.Text = "Cloning repository...";
        if (statusIcon != null) statusIcon.Text = "⏳";
        if (cloneButton != null) cloneButton.IsEnabled = false;

        try
        {
            // Expand GitHub shorthand
            if (Regex.IsMatch(repoUrl, @"^[\w-]+/[\w-]+$"))
            {
                repoUrl = $"https://github.com/{repoUrl}.git";
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"clone \"{repoUrl}\" \"{localPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                if (statusText != null) statusText.Text = "Clone successful!";
                if (statusIcon != null) statusIcon.Text = "✓";
                
                ClonedPath = localPath;
                
                // Close after short delay
                await System.Threading.Tasks.Task.Delay(500);
                Close(ClonedPath);
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                if (statusText != null) statusText.Text = $"Error: {error}";
                if (statusIcon != null) statusIcon.Text = "✕";
                if (cloneButton != null) cloneButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            if (statusText != null) statusText.Text = $"Error: {ex.Message}";
            if (statusIcon != null) statusIcon.Text = "✕";
            if (cloneButton != null) cloneButton.IsEnabled = true;
        }
    }
}

