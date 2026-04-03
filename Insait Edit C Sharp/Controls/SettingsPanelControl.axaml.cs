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

    /// <summary>
    /// Raised after settings are persisted. Subscribers can use this to
    /// re-initialize services that depend on the saved paths (e.g. Copilot SDK).
    /// </summary>
    public event EventHandler? SettingsSaved;

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
    /// If the SDK path is set (e.g. &lt;drive&gt;\Program Files\dotnet\sdk\9.0.100),
    /// walks up to find dotnet.exe. Falls back to "dotnet" (PATH lookup).
    /// </summary>
    public static string ResolveDotNetExe()
    {
        var sdk = GetDotNetSdkPath();
        var configuredDotNet = FindExecutableFromConfiguredPath(sdk, "dotnet.exe", searchParents: true);
        if (!string.IsNullOrWhiteSpace(configuredDotNet))
            return configuredDotNet;

        var dotnetInPath = FindInPath("dotnet.exe");
        if (!string.IsNullOrWhiteSpace(dotnetInPath))
            return dotnetInPath;

        var autoDetectedSdk = AutoDetectDotNetSdk();
        var autoDetectedDotNet = FindExecutableFromConfiguredPath(autoDetectedSdk, "dotnet.exe", searchParents: true);
        if (!string.IsNullOrWhiteSpace(autoDetectedDotNet))
            return autoDetectedDotNet;

        return "dotnet";
    }

    /// <summary>
    /// Resolves gh.exe from saved settings. Falls back to "gh" (PATH lookup).
    /// </summary>
    public static string ResolveGhExe()
    {
        var gh = FindExecutableFromConfiguredPath(GetGitHubCliPath(), "gh.exe");
        if (!string.IsNullOrWhiteSpace(gh))
            return gh;

        var autoDetected = AutoDetectGitHubCli();
        if (!string.IsNullOrWhiteSpace(autoDetected))
            return autoDetected;

        return "gh";
    }

    /// <summary>
    /// Resolves signtool.exe from saved settings. Falls back to null (auto-detect).
    /// </summary>
    public static string? ResolveSignToolExe()
    {
        return FindExecutableFromConfiguredPath(GetSignToolPath(), "signtool.exe")
               ?? AutoDetectSignTool();
    }

    /// <summary>
    /// Resolves MSBuild.exe from saved settings. Falls back to null (auto-detect).
    /// </summary>
    public static string? ResolveMSBuildExe()
    {
        return FindExecutableFromConfiguredPath(GetMSBuildPath(), "MSBuild.exe")
               ?? AutoDetectMSBuild();
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
        SettingsSaved?.Invoke(this, EventArgs.Empty);
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

    private void ValidatePath(string boxName, string statusBaseName, bool isDirectory, string? expectedName = null)
    {
        var value = GetBox(boxName);
        if (string.IsNullOrWhiteSpace(value))
        {
            SetStatus(statusBaseName, "⚠️", "Not configured", "SettingsTextMutedBrush");
            return;
        }

        bool exists = isDirectory ? Directory.Exists(value) : File.Exists(value);

        if (!exists)
        {
            SetStatus(statusBaseName, "❌", "Path not found", "SettingsErrorBrush");
            return;
        }

        if (!isDirectory && expectedName != null)
        {
            var fileName = Path.GetFileName(value);
            if (!fileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(statusBaseName, "⚠️", $"Expected {expectedName}", "SettingsYellowBrush");
                return;
            }
        }

        SetStatus(statusBaseName, "✅", "Valid", "SettingsSuccessBrush");
    }

    private void ClearAllStatuses()
    {
        foreach (var name in new[] { "DotNetSdkStatus", "GitHubCliStatus", "CopilotCliStatus", "SignToolStatus", "MSBuildStatus" })
        {
            SetStatus(name, string.Empty, string.Empty, "SettingsTextMutedBrush");
        }
    }

    private void SetStatus(string statusBaseName, string icon, string message, string textBrushKey)
    {
        var iconBlock = this.FindControl<TextBlock>($"{statusBaseName}Icon");
        var textBlock = this.FindControl<TextBlock>($"{statusBaseName}Text");

        if (iconBlock != null)
        {
            iconBlock.Text = icon;
        }

        if (textBlock != null)
        {
            textBlock.Text = message;
            textBlock.Foreground = FindBrush(textBrushKey);
        }
    }

    // ──────────────────────────────────────────────────────
    //  Auto-detection helpers
    // ──────────────────────────────────────────────────────

    private static string? AutoDetectDotNetSdk()
    {
        var candidates = GetEnvironmentPaths("DOTNET_ROOT", "DOTNET_ROOT(x86)")
            .Select(root => Path.Combine(root, "sdk"))
            .Concat(GetProgramFilesRoots()
            .Select(root => Path.Combine(root, "dotnet", "sdk"))
            )
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                var sdkVersions = GetDirectoriesSafe(dir)
                    .OrderByDescending(GetVersionSortKey)
                    .ThenByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return sdkVersions ?? dir;
            }
        }

        var dotnetExe = FindInPath("dotnet.exe");
        if (dotnetExe != null)
        {
            var sdkDir = Path.Combine(Path.GetDirectoryName(dotnetExe)!, "sdk");
            if (Directory.Exists(sdkDir))
            {
                var latest = GetDirectoriesSafe(sdkDir)
                    .OrderByDescending(GetVersionSortKey)
                    .ThenByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return latest ?? sdkDir;
            }
        }

        return null;
    }

    private static string? AutoDetectGitHubCli()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = GetProgramFilesRoots()
            .Select(root => Path.Combine(root, "GitHub CLI", "gh.exe"))
            .Concat(new[]
            {
                Path.Combine(localAppData, "GitHub CLI", "gh.exe"),
                Path.Combine(localAppData, "Programs", "GitHub CLI", "gh.exe"),
                Path.Combine(localAppData, "Programs", "gh", "gh.exe")
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return FindInPath("gh.exe");
    }

    private static string? AutoDetectCopilotCli()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = GetProgramFilesRoots()
            .Select(root => Path.Combine(root, "Copilot", "copilot.exe"))
            .Concat(new[]
            {
                Path.Combine(localAppData, "Programs", "copilot", "copilot.exe"),
                Path.Combine(localAppData, "Programs", "Copilot", "copilot.exe")
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return FindInPath("copilot.exe");
    }

    private static string? AutoDetectSignTool()
    {
        var basePaths = GetWindowsSdkBinRoots()
            .Concat(GetProgramFilesRoots()
                .Select(root => Path.Combine(root, "Windows Kits", "10", "bin")))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var basePath in basePaths)
        {
            if (!Directory.Exists(basePath)) continue;

            var versionDirs = GetDirectoriesSafe(basePath)
                .Where(d => Path.GetFileName(d).StartsWith("10."))
                .OrderByDescending(GetVersionSortKey)
                .ThenByDescending(d => d, StringComparer.OrdinalIgnoreCase)
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
        var msbuildFromEnv = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(msbuildFromEnv) && File.Exists(msbuildFromEnv))
            return msbuildFromEnv;

        var vsInstallDir = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(vsInstallDir))
        {
            var envCandidates = new[]
            {
                Path.Combine(vsInstallDir, "MSBuild", "Current", "Bin", "MSBuild.exe"),
                Path.Combine(vsInstallDir, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe")
            };

            foreach (var candidate in envCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        var vsVersions = new[] { "2022", "2019" };
        var vsBasePaths = GetProgramFilesRoots()
            .SelectMany(root => vsVersions.Select(version => Path.Combine(root, "Microsoft Visual Studio", version)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

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
            var trimmedDir = dir.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedDir))
                continue;

            var fullPath = Path.Combine(trimmedDir, executable);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }

    private static string? FindExecutableFromConfiguredPath(string? configuredPath, string executableName, bool searchParents = false)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        configuredPath = configuredPath.Trim().Trim('"');

        if (File.Exists(configuredPath))
            return configuredPath;

        if (Directory.Exists(configuredPath))
        {
            var candidate = Path.Combine(configuredPath, executableName);
            if (File.Exists(candidate))
                return candidate;

            if (searchParents)
            {
                var dir = configuredPath;
                for (int i = 0; i < 6; i++)
                {
                    candidate = Path.Combine(dir, executableName);
                    if (File.Exists(candidate))
                        return candidate;

                    var parent = Path.GetDirectoryName(dir);
                    if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                        break;

                    dir = parent;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        return new[]
        {
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!.Trim().Trim('"'))
        .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetEnvironmentPaths(params string[] variableNames)
    {
        return variableNames
            .Select(Environment.GetEnvironmentVariable)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetWindowsSdkBinRoots()
    {
        var candidates = new List<string>();

        var versionBinPath = Environment.GetEnvironmentVariable("WindowsSdkVerBinPath");
        if (!string.IsNullOrWhiteSpace(versionBinPath))
            candidates.Add(versionBinPath);

        var sdkDir = Environment.GetEnvironmentVariable("WindowsSdkDir");
        if (!string.IsNullOrWhiteSpace(sdkDir))
        {
            candidates.Add(Path.Combine(sdkDir, "bin"));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetDirectoriesSafe(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Version GetVersionSortKey(string path)
    {
        return Version.TryParse(Path.GetFileName(path), out var version)
            ? version
            : new Version(0, 0);
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
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22FFC09F"));
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

