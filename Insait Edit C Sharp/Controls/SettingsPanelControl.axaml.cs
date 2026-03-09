using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

public partial class SettingsPanelControl : UserControl
{
    // Settings keys for SettingsDbService
    private const string KeyDotNetSdk = "path_dotnet_sdk";
    private const string KeyGitHubCli = "path_github_cli";
    private const string KeyCopilotCli = "path_copilot_cli";
    private const string KeySignTool = "path_signtool";
    private const string KeyMSBuild = "path_msbuild";

    /// <summary>
    /// Raised when the status text should be shown in the main window status bar.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    public SettingsPanelControl()
    {
        InitializeComponent();
    }

    // ──────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// Load persisted settings into the UI.
    /// </summary>
    public void LoadSettings()
    {
        SetBox("DotNetSdkPathBox", SettingsDbService.LoadSetting(KeyDotNetSdk) ?? "");
        SetBox("GitHubCliPathBox", SettingsDbService.LoadSetting(KeyGitHubCli) ?? "");
        SetBox("CopilotCliPathBox", SettingsDbService.LoadSetting(KeyCopilotCli) ?? "");
        SetBox("SignToolPathBox", SettingsDbService.LoadSetting(KeySignTool) ?? "");
        SetBox("MSBuildPathBox", SettingsDbService.LoadSetting(KeyMSBuild) ?? "");

        ValidateAllPaths();
    }

    /// <summary>
    /// Returns the saved .NET SDK path (or empty string).
    /// </summary>
    public static string GetDotNetSdkPath() => SettingsDbService.LoadSetting(KeyDotNetSdk) ?? "";

    /// <summary>
    /// Returns the saved GitHub CLI path (or empty string).
    /// </summary>
    public static string GetGitHubCliPath() => SettingsDbService.LoadSetting(KeyGitHubCli) ?? "";
    public static string GetCopilotCliPath() => SettingsDbService.LoadSetting(KeyCopilotCli) ?? "";
    /// <summary>
    /// Returns the saved SignTool path (or empty string).
    /// </summary>
    public static string GetSignToolPath() => SettingsDbService.LoadSetting(KeySignTool) ?? "";

    /// <summary>
    /// Returns the saved MSBuild path (or empty string).
    /// </summary>
    public static string GetMSBuildPath() => SettingsDbService.LoadSetting(KeyMSBuild) ?? "";

    /// <summary>
    /// Resolves the dotnet executable path from saved SDK path.
    /// If the SDK path is set (e.g. C:\Program Files\dotnet\sdk\9.0.100),
    /// walks up to find dotnet.exe. Falls back to "dotnet" (PATH lookup).
    /// </summary>
    public static string ResolveDotNetExe()
    {
        var sdk = GetDotNetSdkPath();
        if (!string.IsNullOrWhiteSpace(sdk))
        {
            // Walk up from sdk version folder to find dotnet.exe
            var dir = sdk;
            for (int i = 0; i < 4; i++)
            {
                var candidate = Path.Combine(dir, "dotnet.exe");
                if (File.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }
        }
        return "dotnet";
    }

    /// <summary>
    /// Resolves gh.exe from saved settings. Falls back to "gh" (PATH lookup).
    /// </summary>
    public static string ResolveGhExe()
    {
        var gh = GetGitHubCliPath();
        if (!string.IsNullOrWhiteSpace(gh))
        {
            // if the user pointed directly at the executable, just return it
            if (File.Exists(gh))
                return gh;

            // if they gave a directory, look for gh.exe inside
            if (Directory.Exists(gh))
            {
                var inside = Path.Combine(gh, "gh.exe");
                if (File.Exists(inside))
                    return inside;
            }
        }
        return "gh";
    }

    /// <summary>
    /// Resolves signtool.exe from saved settings. Falls back to null (auto-detect).
    /// </summary>
    public static string? ResolveSignToolExe()
    {
        var st = GetSignToolPath();
        if (!string.IsNullOrWhiteSpace(st) && File.Exists(st))
            return st;
        return null;
    }

    /// <summary>
    /// Resolves MSBuild.exe from saved settings. Falls back to null (auto-detect).
    /// </summary>
    public static string? ResolveMSBuildExe()
    {
        var mb = GetMSBuildPath();
        if (!string.IsNullOrWhiteSpace(mb) && File.Exists(mb))
            return mb;
        return null;
    }

    // ──────────────────────────────────────────────────────
    //  Event handlers
    // ──────────────────────────────────────────────────────

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var dotnet = GetBox("DotNetSdkPathBox");
        var gh = GetBox("GitHubCliPathBox");
        var copilot = GetBox("CopilotCliPathBox");
        var sign = GetBox("SignToolPathBox");
        var msbuild = GetBox("MSBuildPathBox");

        SettingsDbService.SaveSetting(KeyDotNetSdk, dotnet);
        SettingsDbService.SaveSetting(KeyGitHubCli, gh);
        SettingsDbService.SaveSetting(KeyCopilotCli, copilot);
        SettingsDbService.SaveSetting(KeySignTool, sign);
        SettingsDbService.SaveSetting(KeyMSBuild, msbuild);

        ValidateAllPaths();
        ShowStatus("✅ Settings saved successfully.", isSuccess: true);
        StatusChanged?.Invoke(this, "Settings saved.");
    }

    private void Reset_Click(object? sender, RoutedEventArgs e)
    {
        SetBox("DotNetSdkPathBox", "");
        SetBox("GitHubCliPathBox", "");
        SetBox("CopilotCliPathBox", "");
        SetBox("SignToolPathBox", "");
        SetBox("MSBuildPathBox", "");

        ClearAllStatuses();
        ShowStatus("Settings cleared. Click Save to persist.", isSuccess: false);
    }

    private async void AutoDetect_Click(object? sender, RoutedEventArgs e)
    {
        ShowStatus("🔍 Detecting tools…", isSuccess: false);
        StatusChanged?.Invoke(this, "Auto-detecting tool paths…");

        await Task.Run(() =>
        {
            // .NET SDK
            var dotnet = AutoDetectDotNetSdk();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrEmpty(dotnet)) SetBox("DotNetSdkPathBox", dotnet);
            });

            // GitHub CLI
            var gh = AutoDetectGitHubCli();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrEmpty(gh)) SetBox("GitHubCliPathBox", gh);
            });

            var cop = AutoDetectCopilotCli();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrEmpty(cop)) SetBox("CopilotCliPathBox", cop);
            });

            // SignTool
            var sign = AutoDetectSignTool();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrEmpty(sign)) SetBox("SignToolPathBox", sign);
            });

            // MSBuild
            var msbuild = AutoDetectMSBuild();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrEmpty(msbuild)) SetBox("MSBuildPathBox", msbuild);
            });
        });

        ValidateAllPaths();
        ShowStatus("✅ Auto-detection complete. Review paths and click Save.", isSuccess: true);
        StatusChanged?.Invoke(this, "Auto-detection complete.");
    }

    private async void BrowseDotNetSdk_Click(object? sender, RoutedEventArgs e)
    {
        var path = await BrowseForFolderAsync("Select .NET SDK folder");
        if (!string.IsNullOrEmpty(path))
        {
            SetBox("DotNetSdkPathBox", path);
            ValidatePath("DotNetSdkPathBox", "DotNetSdkStatus", isDirectory: true);
        }
    }

    private async void BrowseGitHubCli_Click(object? sender, RoutedEventArgs e)
    {
        var path = await BrowseForFileAsync("Select gh.exe", "gh.exe", "*.exe");
        if (!string.IsNullOrEmpty(path))
        {
            SetBox("GitHubCliPathBox", path);
            ValidatePath("GitHubCliPathBox", "GitHubCliStatus", isDirectory: false, expectedName: "gh.exe");
        }
    }

    private async void BrowseCopilotCli_Click(object? sender, RoutedEventArgs e)
    {
        var path = await BrowseForFileAsync("Select copilot.exe", "copilot.exe", "*.exe");
        if (!string.IsNullOrEmpty(path))
        {
            SetBox("CopilotCliPathBox", path);
            ValidatePath("CopilotCliPathBox", "CopilotCliStatus", isDirectory: false, expectedName: "copilot.exe");
        }
    }

    private async void BrowseSignTool_Click(object? sender, RoutedEventArgs e)
    {
        var path = await BrowseForFileAsync("Select signtool.exe", "signtool.exe", "*.exe");
        if (!string.IsNullOrEmpty(path))
        {
            SetBox("SignToolPathBox", path);
            ValidatePath("SignToolPathBox", "SignToolStatus", isDirectory: false, expectedName: "signtool.exe");
        }
    }

    private async void BrowseMSBuild_Click(object? sender, RoutedEventArgs e)
    {
        var path = await BrowseForFileAsync("Select MSBuild.exe", "MSBuild.exe", "*.exe");
        if (!string.IsNullOrEmpty(path))
        {
            SetBox("MSBuildPathBox", path);
            ValidatePath("MSBuildPathBox", "MSBuildStatus", isDirectory: false, expectedName: "MSBuild.exe");
        }
    }

    // ──────────────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────────────

    private void ValidateAllPaths()
    {
        ValidatePath("DotNetSdkPathBox", "DotNetSdkStatus", isDirectory: true);
        ValidatePath("GitHubCliPathBox", "GitHubCliStatus", isDirectory: false, expectedName: "gh.exe");
        ValidatePath("CopilotCliPathBox", "CopilotCliStatus", isDirectory: false, expectedName: "copilot.exe");
        ValidatePath("SignToolPathBox", "SignToolStatus", isDirectory: false, expectedName: "signtool.exe");
        ValidatePath("MSBuildPathBox", "MSBuildStatus", isDirectory: false, expectedName: "MSBuild.exe");
    }

    private void ValidatePath(string boxName, string statusName, bool isDirectory, string? expectedName = null)
    {
        var statusBlock = this.FindControl<TextBlock>(statusName);
        if (statusBlock == null) return;

        var value = GetBox(boxName);
        if (string.IsNullOrWhiteSpace(value))
        {
            statusBlock.Text = "⚠ Not configured";
            statusBlock.Foreground = FindBrush("SettingsTextMutedBrush");
            return;
        }

        bool exists = isDirectory ? Directory.Exists(value) : File.Exists(value);

        if (!exists)
        {
            statusBlock.Text = "❌ Path not found";
            statusBlock.Foreground = FindBrush("SettingsErrorBrush");
            return;
        }

        if (!isDirectory && expectedName != null)
        {
            var fileName = Path.GetFileName(value);
            if (!fileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                statusBlock.Text = $"⚠ Expected {expectedName}";
                statusBlock.Foreground = FindBrush("SettingsYellowBrush");
                return;
            }
        }

        statusBlock.Text = "✅ Valid";
        statusBlock.Foreground = FindBrush("SettingsSuccessBrush");
    }

    private void ClearAllStatuses()
    {
        foreach (var name in new[] { "DotNetSdkStatus", "GitHubCliStatus", "CopilotCliStatus", "SignToolStatus", "MSBuildStatus" })
        {
            var tb = this.FindControl<TextBlock>(name);
            if (tb != null) tb.Text = "";
        }
    }

    // ──────────────────────────────────────────────────────
    //  Auto-detection helpers
    // ──────────────────────────────────────────────────────

    private static string? AutoDetectDotNetSdk()
    {
        // Standard installation paths
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk"),
            @"C:\Program Files\dotnet\sdk",
            @"C:\Program Files (x86)\dotnet\sdk"
        };

        foreach (var dir in candidates.Distinct())
        {
            if (Directory.Exists(dir))
            {
                // Return the latest SDK version sub-folder
                var sdkVersions = Directory.GetDirectories(dir)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                return sdkVersions ?? dir;
            }
        }

        // Try PATH
        var dotnetExe = FindInPath("dotnet.exe");
        if (dotnetExe != null)
        {
            var sdkDir = Path.Combine(Path.GetDirectoryName(dotnetExe)!, "sdk");
            if (Directory.Exists(sdkDir))
            {
                var latest = Directory.GetDirectories(sdkDir)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                return latest ?? sdkDir;
            }
        }

        return null;
    }

    private static string? AutoDetectGitHubCli()
    {
        var candidates = new[]
        {
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GitHub CLI", "gh.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return FindInPath("gh.exe");
    }

    private static string? AutoDetectSignTool()
    {
        // Windows SDK locations
        var basePaths = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\bin",
            @"C:\Program Files\Windows Kits\10\bin"
        };

        foreach (var basePath in basePaths)
        {
            if (!Directory.Exists(basePath)) continue;

            // Find the latest SDK version that contains signtool
            var versionDirs = Directory.GetDirectories(basePath)
                .Where(d => Path.GetFileName(d).StartsWith("10."))
                .OrderByDescending(d => d)
                .ToList();

            foreach (var versionDir in versionDirs)
            {
                var x64 = Path.Combine(versionDir, "x64", "signtool.exe");
                if (File.Exists(x64)) return x64;

                var x86 = Path.Combine(versionDir, "x86", "signtool.exe");
                if (File.Exists(x86)) return x86;
            }
        }

        return FindInPath("signtool.exe");
    }

    private static string? AutoDetectMSBuild()
    {
        // VS 2022+ locations
        var vsBasePaths = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio\2022",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2022",
            @"C:\Program Files\Microsoft Visual Studio\2019",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019"
        };

        var editions = new[] { "Enterprise", "Professional", "Community", "BuildTools" };

        foreach (var vsBase in vsBasePaths)
        {
            if (!Directory.Exists(vsBase)) continue;

            foreach (var edition in editions)
            {
                var msbuild = Path.Combine(vsBase, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                if (File.Exists(msbuild)) return msbuild;

                var msbuildAmd64 = Path.Combine(vsBase, edition, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe");
                if (File.Exists(msbuildAmd64)) return msbuildAmd64;
            }
        }

        return FindInPath("MSBuild.exe");
    }

    private static string? FindInPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, executable);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }

    // ──────────────────────────────────────────────────────
    //  UI helpers
    // ──────────────────────────────────────────────────────

    private string GetBox(string name)
    {
        var tb = this.FindControl<TextBox>(name);
        return tb?.Text?.Trim() ?? "";
    }

    private void SetBox(string name, string value)
    {
        var tb = this.FindControl<TextBox>(name);
        if (tb != null) tb.Text = value;
    }

    private Avalonia.Media.IBrush FindBrush(string key)
    {
        if (this.TryFindResource(key, out var resource) && resource is Avalonia.Media.IBrush brush)
            return brush;
        return Avalonia.Media.Brushes.White;
    }

    private void ShowStatus(string message, bool isSuccess)
    {
        var border = this.FindControl<Border>("StatusBorder");
        var text = this.FindControl<TextBlock>("StatusMessageText");

        if (border != null)
        {
            border.IsVisible = true;
            border.Background = isSuccess
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2050C878"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#20548AF7"));
        }

        if (text != null)
        {
            text.Text = message;
        }
    }

    private async Task<string?> BrowseForFolderAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async Task<string?> BrowseForFileAsync(string title, string filterName, string pattern)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(filterName) { Patterns = new[] { pattern } }
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}

