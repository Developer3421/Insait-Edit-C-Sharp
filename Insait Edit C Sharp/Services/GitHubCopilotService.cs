using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Manages custom user-created translation dictionaries stored in:
///   %AppData%\InsaitEdit\GitHubTranslations\
///
/// Each custom language is a plain AXAML file with the same x:String structure
/// as the built-in English.axaml.  Users can copy or create their own file,
/// place it in the folder, and select it from Menu → Language.
///
/// Also provides a helper to launch GitHub Copilot CLI in that folder.
/// </summary>
public static class GitHubCopilotService
{
    private static readonly string _translationsDir;

    // Currently loaded custom strings: key → value
    private static Dictionary<string, string>? _loadedEntries;
    private static string? _loadedLanguageName;

    static GitHubCopilotService()
    {
        _translationsDir = Path.Combine(SettingsDbService.AppDataDir, "GitHubTranslations");
        Directory.CreateDirectory(_translationsDir);

        // Ensure the English template is always present on first run (fire-and-forget).
        _ = EnsureEnglishDictionaryAsync();
    }

    /// <summary>AppData folder where custom AXAML dictionaries are stored.</summary>
    public static string TranslationsDirectory => _translationsDir;

    /// <summary>Full path to the English.axaml template copy in AppData.</summary>
    public static string EnglishDictionaryPath => Path.Combine(_translationsDir, "English.axaml");

    /// <summary>Currently loaded custom language name, or <c>null</c>.</summary>
    public static string? LoadedLanguageName => _loadedLanguageName;

    // ── English template ────────────────────────────────────────────────

    /// <summary>
    /// Ensures that the English.axaml template file exists in the translations folder.
    /// </summary>
    public static async Task EnsureEnglishDictionaryAsync(
        bool overwrite = false,
        CancellationToken ct = default)
    {
        var destPath = EnglishDictionaryPath;
        if (!overwrite && File.Exists(destPath))
            return;

        string content;

        // 1) Try avares:// asset loader
        try
        {
            var uri = new Uri("avares://Insait%20Edit%20C%20Sharp/Interface%20Localization/English.axaml");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            content = await reader.ReadToEndAsync(ct);
        }
        catch
        {
            // 2) Fallback: read from disk
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Interface Localization", "English.axaml"),
                Path.Combine(baseDir, "..", "Interface Localization", "English.axaml"),
                Path.Combine(baseDir, "..", "..", "..", "..", "Insait Edit C Sharp",
                    "Interface Localization", "English.axaml"),
            };

            content = string.Empty;
            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    content = await File.ReadAllTextAsync(full, System.Text.Encoding.UTF8, ct);
                    break;
                }
            }

            if (string.IsNullOrEmpty(content))
            {
                // 3) Last resort: use the hardcoded built-in content so the file is ALWAYS created
                Debug.WriteLine("[GitHubCopilot] EnsureEnglishDictionaryAsync: using built-in hardcoded fallback.");
                content = GetBuiltinEnglishContent();
            }
        }

        await File.WriteAllTextAsync(destPath, content, System.Text.Encoding.UTF8, ct);
    }

    // ── Custom language list ────────────────────────────────────────────

    /// <summary>
    /// Returns a list of custom language names found in the translations folder.
    /// Each <c>.axaml</c> file (except English.axaml) is treated as a custom language.
    /// The language name is the file name without extension.
    /// </summary>
    public static List<string> GetAvailableCustomLanguages()
    {
        var result = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(_translationsDir, "*.axaml"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                // Skip the English template
                if (string.Equals(name, "English", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(name);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitHubCopilot] GetAvailableCustomLanguages failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Returns the path to the AXAML file for the given custom language.
    /// </summary>
    public static string GetDictionaryPath(string languageName)
        => Path.Combine(_translationsDir, $"{SanitizeName(languageName)}.axaml");

    /// <summary>
    /// Returns whether a local AXAML dictionary exists for this language.
    /// </summary>
    public static bool DictionaryExists(string languageName)
        => File.Exists(GetDictionaryPath(languageName));

    // ── Load / Unload custom dictionary ─────────────────────────────────

    /// <summary>
    /// Loads the custom AXAML dictionary for the given language.
    /// Parses the AXAML file and injects all x:String entries directly into
    /// <see cref="Application.Current.Resources"/> so that
    /// <see cref="LocalizationService.Get"/> can find them.
    /// </summary>
    public static void LoadCustomDictionary(string languageName)
    {
        var path = GetDictionaryPath(languageName);
        if (!File.Exists(path)) return;

        var app = Application.Current;
        if (app == null) return;

        // Unload any previously loaded custom entries
        UnloadCustomDictionary();

        try
        {
            var xmlContent = File.ReadAllText(path);
            var entries = ParseAxamlStrings(xmlContent);
            _loadedEntries = entries;
            _loadedLanguageName = languageName;

            // Inject entries into Application.Current.Resources
            foreach (var kv in entries)
                app.Resources[kv.Key] = kv.Value;

            Debug.WriteLine(
                $"[GitHubCopilot] Loaded {entries.Count} entries for '{languageName}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[GitHubCopilot] Failed to load {path}: {ex.Message}");
        }
    }

    /// <summary>Unloads the currently active custom dictionary if any.</summary>
    public static void UnloadCustomDictionary()
    {
        var app = Application.Current;
        if (app == null || _loadedEntries == null) return;

        foreach (var key in _loadedEntries.Keys)
        {
            if (app.Resources.TryGetResource(key, null, out var existing) &&
                existing is string s && s == _loadedEntries[key])
            {
                app.Resources.Remove(key);
            }
        }

        _loadedEntries = null;
        _loadedLanguageName = null;
    }

    // ── Import from file ────────────────────────────────────────────────

    /// <summary>
    /// Copies an external AXAML file into the translations folder.
    /// The language name is derived from the file name (without extension).
    /// Returns the language name on success, or <c>null</c> on failure.
    /// </summary>
    public static string? ImportDictionaryFile(string sourceFilePath)
    {
        try
        {
            var langName = Path.GetFileNameWithoutExtension(sourceFilePath);
            if (string.IsNullOrWhiteSpace(langName)) return null;

            var destPath = GetDictionaryPath(langName);
            File.Copy(sourceFilePath, destPath, overwrite: true);

            Debug.WriteLine($"[GitHubCopilot] Imported '{langName}' from {sourceFilePath}");
            return langName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitHubCopilot] ImportDictionaryFile failed: {ex.Message}");
            return null;
        }
    }

    // ── Folder / CLI helpers ────────────────────────────────────────────

    /// <summary>
    /// Opens the translations folder in the system file explorer.
    /// </summary>
    public static void OpenTranslationsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _translationsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitHubCopilot] OpenTranslationsFolder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Launches the GitHub Copilot CLI in a new terminal window
    /// with the working directory set to the translations folder.
    /// The translations folder and the English.axaml template are
    /// guaranteed to exist before the terminal opens.
    /// </summary>
    public static async Task LaunchCopilotCliAsync(CancellationToken ct = default)
    {
        try
        {
            // Ensure the folder exists (idempotent).
            Directory.CreateDirectory(_translationsDir);

            // Ensure English.axaml template is present so the user always has a
            // reference file to work from when the CLI opens in that folder.
            await EnsureEnglishDictionaryAsync(overwrite: true, ct);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k cd /d \"{_translationsDir}\" && gh copilot",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = _translationsDir
            };

            if (!OperatingSystem.IsWindows())
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"-c \"cd '{_translationsDir}' && gh copilot; exec bash\"";
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitHubCopilot] LaunchCopilotCliAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Launches Kilo EXE in the translations folder where localization files are stored.
    /// This allows users to edit localization files directly with Kilo.
    /// </summary>
    public static async Task LaunchKiloInTranslationsFolderAsync(CancellationToken ct = default)
    {
        try
        {
            // Ensure the folder exists and English.axaml template is present
            Directory.CreateDirectory(_translationsDir);
            await EnsureEnglishDictionaryAsync(true, ct);

            // Resolve kilo.exe path (from settings or auto-detect)
            var kiloPath = Controls.SettingsPanelControl.ResolveKiloExe();
            
            var psi = new ProcessStartInfo
            {
                FileName = kiloPath,
                WorkingDirectory = _translationsDir,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GitHubCopilot] LaunchKiloInTranslationsFolder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Synchronous wrapper for backwards compatibility.
    /// </summary>
    public static void LaunchKiloInTranslationsFolder()
        => _ = LaunchKiloInTranslationsFolderAsync();

    /// <summary>
    /// Synchronous wrapper around <see cref="LaunchCopilotCliAsync"/> kept for
    /// backwards compatibility with call-sites that cannot be made async.
    /// Internally fires the async work without blocking.
    /// </summary>
    public static void LaunchCopilotCli()
        => _ = LaunchCopilotCliAsync();

    // ── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Parses x:String entries from an AXAML ResourceDictionary using regex.
    /// </summary>
    private static Dictionary<string, string> ParseAxamlStrings(string xml)
    {
        var result = new Dictionary<string, string>();
        var rx = new Regex(
            @"<x:String\s+x:Key=""([^""]+)""[^>]*>([^<]*)</x:String>",
            RegexOptions.Compiled);
        foreach (Match m in rx.Matches(xml))
        {
            var key = m.Groups[1].Value;
            var val = WebUtility.HtmlDecode(m.Groups[2].Value);
            result[key] = val;
        }

        return result;
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    /// Returns the complete English.axaml content hardcoded as a fallback,
    /// so the translations folder always gets a valid template even without
    /// the embedded asset or source files on disk.
    /// </summary>
    private static string GetBuiltinEnglishContent() => """
        <ResourceDictionary xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:sys="using:System">
                            
           <!-- ══════════════════════════════════════════════════════════════
                 ENGLISH LOCALIZATION — All 21 Windows & Popups
                 ══════════════════════════════════════════════════════════════ -->
        
            <!-- ── 1. MainWindow ─────────────────────────────────────────── -->
            <x:String x:Key="App.Title">Insait Edit - C# IDE</x:String>
            <x:String x:Key="TitleBar.Menu">Menu</x:String>
            <x:String x:Key="TitleBar.Build">Build</x:String>
            <x:String x:Key="TitleBar.Analyze">Analyze</x:String>
            <x:String x:Key="TitleBar.Run">Run</x:String>
            <x:String x:Key="TitleBar.Debug">Debug</x:String>
            <x:String x:Key="TitleBar.Stop">Stop</x:String>
            <x:String x:Key="TitleBar.Preview">Preview</x:String>
            <x:String x:Key="TitleBar.MSIX">MSIX</x:String>
            <x:String x:Key="Tooltip.Menu">Menu</x:String>
            <x:String x:Key="Tooltip.Build">Build Project (Ctrl+Shift+B)</x:String>
            <x:String x:Key="Tooltip.Analyze">Analyze Code (Ctrl+Shift+A)</x:String>
            <x:String x:Key="Tooltip.RunConfig">Select Run Configuration (Shift+Alt+F10)</x:String>
            <x:String x:Key="Tooltip.EditConfig">Edit Configurations...</x:String>
            <x:String x:Key="Tooltip.Run">Run Project (F5)</x:String>
            <x:String x:Key="Tooltip.Debug">Run in Debug Mode (F5 with debugger)</x:String>
            <x:String x:Key="Tooltip.Stop">Stop (Shift+F5)</x:String>
            <x:String x:Key="Tooltip.Publish">Publish Project...</x:String>
            <x:String x:Key="Tooltip.MsixManager">MSIX Package Manager...</x:String>
            <x:String x:Key="Tooltip.Undo">Undo (Ctrl+Z)</x:String>
            <x:String x:Key="Tooltip.Redo">Redo (Ctrl+Y)</x:String>
            <x:String x:Key="Tooltip.NewWindow">New Window</x:String>
            <x:String x:Key="Tooltip.Restart">Restart Application</x:String>
            <x:String x:Key="Tooltip.UserAgreement">User Agreement</x:String>
            <x:String x:Key="Tooltip.PreviewAxaml">Preview AXAML Design (Ctrl+Shift+P)</x:String>
            <x:String x:Key="Sidebar.Explorer">Explorer (Ctrl+Shift+E)</x:String>
            <x:String x:Key="Sidebar.Search">Search (Ctrl+Shift+F)</x:String>
            <x:String x:Key="Sidebar.Git">Source Control (Ctrl+Shift+G)</x:String>
            <x:String x:Key="Sidebar.NuGet">NuGet Packages (Ctrl+Shift+N)</x:String>
            <x:String x:Key="Sidebar.Account">Account</x:String>
            <x:String x:Key="Sidebar.Settings">Settings (Ctrl+,)</x:String>
            <x:String x:Key="Panel.Explorer">EXPLORER</x:String>
            <x:String x:Key="Panel.Search">SEARCH</x:String>
            <x:String x:Key="Panel.CopilotCli">COPILOT CLI</x:String>
            <x:String x:Key="Panel.GitHubCopilot">CONTROL PANEL</x:String>
            <x:String x:Key="Explorer.NewFile">New File</x:String>
            <x:String x:Key="Explorer.NewFolder">New Folder</x:String>
            <x:String x:Key="Explorer.Refresh">Refresh</x:String>
            <x:String x:Key="Tab.Problems">Problems</x:String>
            <x:String x:Key="Tab.Build">Build</x:String>
            <x:String x:Key="Tab.Run">Run</x:String>
            <x:String x:Key="Tab.Terminal">Terminal</x:String>
            <x:String x:Key="Tooltip.NewTerminal">New Terminal (Ctrl+Shift+`)</x:String>
            <x:String x:Key="Tooltip.SplitTerminal">Split Terminal</x:String>
            <x:String x:Key="Tooltip.ClearAll">Clear All</x:String>
            <x:String x:Key="Tooltip.ScrollToEnd">Scroll to End</x:String>
            <x:String x:Key="Tooltip.Minimize">Minimize</x:String>
            <x:String x:Key="Tooltip.Maximize">Maximize</x:String>
            <x:String x:Key="Tooltip.HidePanel">Hide (Esc)</x:String>
            <x:String x:Key="Tooltip.RefreshAnalysis">Refresh Analysis</x:String>
            <x:String x:Key="Tooltip.ClearProblems">Clear Problems</x:String>
            <x:String x:Key="Tooltip.CopyProblems">Copy all errors to clipboard</x:String>
            <x:String x:Key="Problems.NoProblems">No problems detected. Press Analyze to check for issues.</x:String>
            <x:String x:Key="Problems.Errors">{0} Errors</x:String>
            <x:String x:Key="Problems.Warnings">{0} Warnings</x:String>
            <x:String x:Key="Problems.Messages">0 Messages</x:String>
            <x:String x:Key="Problems.TabAll">All</x:String>
            <x:String x:Key="Problems.TabCurrentFile">Current File</x:String>
            <x:String x:Key="Problems.LabelErrors">Errors</x:String>
            <x:String x:Key="Problems.LabelWarnings">Warnings</x:String>
            <x:String x:Key="Problems.LabelMessages">Messages</x:String>
            <x:String x:Key="Output.BuildPlaceholder">Build output will appear here...</x:String>
            <x:String x:Key="Output.RunPlaceholder">Run output will appear here...</x:String>
            <x:String x:Key="Status.GitBranch">Git Branch</x:String>
            <x:String x:Key="Status.Sync">Synchronize Changes</x:String>
            <x:String x:Key="Status.ViewProblems">View Problems</x:String>
            <x:String x:Key="Status.Encoding">Select Encoding</x:String>
            <x:String x:Key="Status.LineEnding">Select End of Line Sequence</x:String>
            <x:String x:Key="Status.Language">Select Language Mode</x:String>
            <x:String x:Key="Status.GoToLine">Go to Line</x:String>
            <x:String x:Key="Status.Indent">Select Indentation</x:String>
            <x:String x:Key="Status.Notifications">Notifications</x:String>
            <x:String x:Key="Context.Run">▶ Run</x:String>
            <x:String x:Key="Context.New">New</x:String>
            <x:String x:Key="Context.Add">Add</x:String>
            <x:String x:Key="Context.NewClass">📄 Class...</x:String>
            <x:String x:Key="Context.NewInterface">📄 Interface...</x:String>
            <x:String x:Key="Context.NewRecord">📄 Record...</x:String>
            <x:String x:Key="Context.NewEnum">📄 Enum...</x:String>
            <x:String x:Key="Context.NewAvaloniaWindow">🪟 Avalonia Window...</x:String>
            <x:String x:Key="Context.NewAvaloniaControl">🎛️ Avalonia UserControl...</x:String>
            <x:String x:Key="Context.NewFile">📄 File...</x:String>
            <x:String x:Key="Context.NewDirectory">📁 Directory</x:String>
            <x:String x:Key="Context.AddNewProject">📦 New Project...</x:String>
            <x:String x:Key="Context.AddExistingProject">📦 Existing Project...</x:String>
            <x:String x:Key="Context.AddNewItem">📄 New Item...</x:String>
            <x:String x:Key="Context.AddExistingItem">📄 Existing Item...</x:String>
            <x:String x:Key="Context.Build">🔨 Build</x:String>
            <x:String x:Key="Context.Rebuild">🔄 Rebuild</x:String>
            <x:String x:Key="Context.Clean">🧹 Clean</x:String>
            <x:String x:Key="Context.ManageNuGet">📦 Manage NuGet Packages...</x:String>
            <x:String x:Key="Context.AddReference">🔗 Add Reference...</x:String>
            <x:String x:Key="Context.Cut">✂️ Cut</x:String>
            <x:String x:Key="Context.Copy">📋 Copy</x:String>
            <x:String x:Key="Context.Paste">📄 Paste</x:String>
            <x:String x:Key="Context.Rename">✏️ Rename</x:String>
            <x:String x:Key="Context.Delete">🗑️ Safe Delete...</x:String>
            <x:String x:Key="Context.CopyPath">Copy Path</x:String>
            <x:String x:Key="Context.AbsolutePath">📋 Absolute Path</x:String>
            <x:String x:Key="Context.RelativePath">📋 Relative Path</x:String>
            <x:String x:Key="Context.FileName">📋 File Name</x:String>
            <x:String x:Key="Context.OpenExplorer">📂 Open in Explorer</x:String>
            <x:String x:Key="Context.OpenTerminal">💻 Open in Terminal</x:String>
            <x:String x:Key="Context.RemoveFromSolution">🗑️ Remove from Solution</x:String>
            <x:String x:Key="Context.UnloadProject">⬇️ Unload Project</x:String>
            <x:String x:Key="Context.Git">Git</x:String>
            <x:String x:Key="Context.GitCommit">📝 Commit...</x:String>
            <x:String x:Key="Context.GitHistory">📜 Show History</x:String>
            <x:String x:Key="Context.GitRevert">↩️ Revert...</x:String>
            <x:String x:Key="Context.ReloadFromDisk">🔄 Reload from Disk</x:String>
            <x:String x:Key="Context.Properties">⚙️ Properties</x:String>
            <x:String x:Key="ExplorerNodeMenu.PageTitle.Solution">Solution · {0}</x:String>
            <x:String x:Key="ExplorerNodeMenu.PageTitle.Project">Project · {0}</x:String>
            <x:String x:Key="ExplorerNodeMenu.PageTitle.File">File · {0}</x:String>
            <x:String x:Key="ExplorerNodeMenu.PageTitle.Folder">Folder · {0}</x:String>
            <x:String x:Key="ExplorerNodeMenu.PageTitle.MultiSelection">{0} Items Selected{1}</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Open">Open</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Add">Add</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Create">Create</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Build">Build</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Run">Run</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Edit">Edit</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Navigate">Navigate</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.SourceControl">Source Control</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Dependencies">Dependencies</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Solution">Solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Project">Project</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.File">File</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Folder">Folder</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Start">Start</x:String>
            <x:String x:Key="ExplorerNodeMenu.Section.Recent">Recent</x:String>
            <x:String x:Key="ExplorerNodeMenu.Count.File.One">{0} file</x:String>
            <x:String x:Key="ExplorerNodeMenu.Count.File.Many">{0} files</x:String>
            <x:String x:Key="ExplorerNodeMenu.Count.Folder.One">{0} folder</x:String>
            <x:String x:Key="ExplorerNodeMenu.Count.Folder.Many">{0} folders</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.OpenSolutionFile">📄 Open Solution File</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.BuildSolution">🔨 Build Solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.RebuildSolution">🔄 Rebuild Solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.CleanSolution">🧹 Clean Solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.RunProject">▶ Run Project</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.OpenInEditor">📄 Open in Editor</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.NewFile">📄 New File...</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.NewDirectory">📁 New Directory</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.NewSubDirectory">📁 New Sub-Directory</x:String>
            <x:String x:Key="ExplorerNodeMenu.Action.DeleteSelectedItems">🗑️ Delete {0} Items...</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.OpenSolutionFile">Open the .sln / .slnx file in the editor</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.AddNewProject">Create a project inside the current solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.AddExistingProject">Attach an existing project to the solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.AddNewItem">Create a new file in the solution folder</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.AddExistingItem">Copy an existing file into the solution folder</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.BuildSolution">Build the entire solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.RebuildSolution">Clean and rebuild the solution</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.CleanSolution">Remove all build artefacts</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.CutSolutionPath">Cut the solution path</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.CopySolutionPath">Copy the solution path</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.PasteIntoSolutionFolder">Paste into the solution folder</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.OpenGitWindow">Open the Git window</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.ShowGitHistory">Show commit history in the Git window</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.OpenGitRevert">Open revert options in the Git window</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.ReloadFromDisk">Refresh the explorer tree</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.OpenSolutionProperties">Open solution properties</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.DeleteSolutionWorkspace">Delete the solution / workspace</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.RunProject">Run the selected project</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.OpenInEditor">Open this file in the code editor</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.DeleteFileSafely">Delete this file safely</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.DeleteFolderSafely">Delete this folder safely</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.CutSelectedItems">Cut {0} selected items</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.CopySelectedItems">Copy paths of {0} selected items</x:String>
            <x:String x:Key="ExplorerNodeMenu.Subtitle.DeleteSelectedItems">Delete all {0} selected items</x:String>
            <x:String x:Key="InputDialog.WindowTitle">Input</x:String>
            <x:String x:Key="InputDialog.Title.NewFolder">New Folder</x:String>
            <x:String x:Key="InputDialog.Prompt.NewFolder">Enter folder name:</x:String>
            <x:String x:Key="InputDialog.Title.Rename">Rename</x:String>
            <x:String x:Key="InputDialog.Prompt.Rename">Enter new name:</x:String>
            <x:String x:Key="InputDialog.Title.NewClass">New Class</x:String>
            <x:String x:Key="InputDialog.Prompt.NewClass">Enter class name:</x:String>
            <x:String x:Key="InputDialog.Title.NewInterface">New Interface</x:String>
            <x:String x:Key="InputDialog.Prompt.NewInterface">Enter interface name:</x:String>
            <x:String x:Key="InputDialog.Title.NewRecord">New Record</x:String>
            <x:String x:Key="InputDialog.Prompt.NewRecord">Enter record name:</x:String>
            <x:String x:Key="InputDialog.Title.NewEnum">New Enum</x:String>
            <x:String x:Key="InputDialog.Prompt.NewEnum">Enter enum name:</x:String>
            <x:String x:Key="InputDialog.Title.NewAvaloniaWindow">New Avalonia Window</x:String>
            <x:String x:Key="InputDialog.Prompt.NewAvaloniaWindow">Enter window name:</x:String>
            <x:String x:Key="InputDialog.Title.NewAvaloniaUserControl">New Avalonia UserControl</x:String>
            <x:String x:Key="InputDialog.Prompt.NewAvaloniaUserControl">Enter user control name:</x:String>
            <x:String x:Key="Dialog.ConfirmDelete.Title">Confirm Delete</x:String>
            <x:String x:Key="Dialog.Delete.Single">Are you sure you want to delete '{0}'?</x:String>
            <x:String x:Key="Dialog.Delete.SingleDirectoryWarning">This will delete all contents.</x:String>
            <x:String x:Key="Dialog.Delete.Multiple">Are you sure you want to delete {0} items ({1})?</x:String>
            <x:String x:Key="Dialog.Delete.MultipleDirectoryWarning">All folder contents will be deleted.</x:String>
            <x:String x:Key="AI.Ready">🚀 Copilot CLI Ready!</x:String>
            <x:String x:Key="AI.ManageFiles">Manage your project files with commands:</x:String>
            <x:String x:Key="AI.GitHubCommands">GitHub CLI Commands:</x:String>
            <x:String x:Key="AI.TypeHelp">Type 'help' for full command list</x:String>
            <x:String x:Key="AI.InputPlaceholder">Enter command (create, ls, help...)</x:String>
            <x:String x:Key="AI.Tooltip.OpenTUI">Open GitHub Copilot TUI in Terminal</x:String>
            <x:String x:Key="AI.Tooltip.Execute">Execute command</x:String>
            <x:String x:Key="AI.Tooltip.NewChat">New Chat</x:String>
            <x:String x:Key="AI.Tooltip.ClosePanel">Close Panel</x:String>
        
            <!-- ── CLI Terminal Messages ─────────────────────────────────── -->
            <x:String x:Key="Cli.NoCommand">No command provided</x:String>
            <x:String x:Key="Cli.InvalidCommand">Invalid command</x:String>
            <x:String x:Key="Cli.UnknownCommand">Unknown command: {0}. Use 'help' to see available commands.</x:String>
            <x:String x:Key="Cli.ErrorExecuting">Error executing '{0}': {1}</x:String>
            <x:String x:Key="Cli.FileAlreadyExists">File already exists: {0}</x:String>
            <x:String x:Key="Cli.CreatedFile">Created file: {0}</x:String>
            <x:String x:Key="Cli.UpdatedTimestamp">Updated timestamp: {0}</x:String>
            <x:String x:Key="Cli.CreatedDirectory">Created directory: {0}</x:String>
            <x:String x:Key="Cli.DirectoryAlreadyExists">Directory already exists: {0}</x:String>
            <x:String x:Key="Cli.DirectoryNotEmpty">Directory is not empty: {0}. Use --force to delete anyway.</x:String>
            <x:String x:Key="Cli.DeletedDirectory">Deleted directory: {0}</x:String>
            <x:String x:Key="Cli.DeletedFile">Deleted file: {0}</x:String>
            <x:String x:Key="Cli.PathNotFound">Path not found: {0}</x:String>
            <x:String x:Key="Cli.DirectoryNotFound">Directory not found: {0}</x:String>
            <x:String x:Key="Cli.FileNotFound">File not found: {0}</x:String>
            <x:String x:Key="Cli.SourceNotFound">Source not found: {0}</x:String>
            <x:String x:Key="Cli.DestinationExists">Destination already exists: {0}</x:String>
            <x:String x:Key="Cli.Renamed">Renamed: {0} → {1}</x:String>
            <x:String x:Key="Cli.Moved">Moved: {0} → {1}</x:String>
            <x:String x:Key="Cli.Copied">Copied: {0} → {1}</x:String>
            <x:String x:Key="Cli.WrittenTo">Written to: {0}</x:String>
            <x:String x:Key="Cli.AppendedTo">Appended to: {0}</x:String>
            <x:String x:Key="Cli.CreatedFromTemplate">Created from template '{0}': {1}</x:String>
            <x:String x:Key="Cli.UnknownTemplate">Unknown template: {0}</x:String>
            <x:String x:Key="Cli.Directory">Directory: {0}</x:String>
            <x:String x:Key="Cli.DirsAndFiles">{0} directories, {1} files</x:String>
            <x:String x:Key="Cli.NoFilesFound">No files found matching '{0}'</x:String>
            <x:String x:Key="Cli.FoundFiles">Found {0} file(s):</x:String>
            <x:String x:Key="Cli.NoMatchesFound">No matches found for '{0}'</x:String>
            <x:String x:Key="Cli.FoundMatches">Found {0} match(es):</x:String>
            <x:String x:Key="Cli.ChangedDirectory">Changed directory to: {0}</x:String>
            <x:String x:Key="Cli.ExistsYes">Yes ({0}): {1}</x:String>
            <x:String x:Key="Cli.ExistsNo">No: {0}</x:String>
            <x:String x:Key="Cli.GhNotFound">GitHub CLI (gh) not found. Run 'gh-install' to install it.</x:String>
            <x:String x:Key="Cli.GhAlreadyInstalled">✅ GitHub CLI is already installed</x:String>
            <x:String x:Key="Cli.GhInstallSuccess">✅ GitHub CLI installed successfully!</x:String>
            <x:String x:Key="Cli.GhInstallFailed">❌ Installation failed</x:String>
            <x:String x:Key="Cli.GhInstallManual">Try installing manually: winget install GitHub.cli</x:String>
            <x:String x:Key="Cli.GhInstallRestart">⚠️ You may need to restart the application. Run 'gh-auth login' to authenticate.</x:String>
            <x:String x:Key="Cli.GhAuthAlreadyLoggedIn">✅ Already logged in as: {0}</x:String>
            <x:String x:Key="Cli.GhAuthStarted">🔐 GitHub authentication started!</x:String>
            <x:String x:Key="Cli.GhAuthStartedHint">A terminal window has opened. Follow the instructions there to complete authentication.</x:String>
            <x:String x:Key="Cli.GhAuthVerifyHint">After completing, run 'gh-auth status' to verify.</x:String>
            <x:String x:Key="Cli.GhAuthFailed">❌ Could not start authentication: {0}</x:String>
            <x:String x:Key="Cli.GhAuthFailedHint">Try running manually in terminal: gh auth login</x:String>
            <x:String x:Key="Cli.GhLoggedOut">✅ Logged out from GitHub</x:String>
            <x:String x:Key="Cli.GhLogoutFailed">❌ Logout failed: {0}</x:String>
            <x:String x:Key="Cli.GhRefreshed">✅ Authentication refreshed</x:String>
            <x:String x:Key="Cli.GhRefreshFailed">❌ Refresh failed: {0}</x:String>
            <x:String x:Key="Cli.GhSetupGit">✅ Git configured to use GitHub CLI as credential helper</x:String>
            <x:String x:Key="Cli.GhSetupGitFailed">❌ Setup failed: {0}</x:String>
            <x:String x:Key="Cli.GhToken">🔑 GitHub Token:</x:String>
            <x:String x:Key="Cli.GhTokenFailed">❌ Failed to get token: {0}</x:String>
            <x:String x:Key="Cli.GhLoggedInAs">✅ Logged in to GitHub as: {0}</x:String>
            <x:String x:Key="Cli.GhNotLoggedIn">❌ Not logged in to GitHub. Run 'gh-auth login' to authenticate.</x:String>
            <x:String x:Key="Cli.GhStatusTitle">📊 GitHub CLI Status</x:String>
            <x:String x:Key="Cli.GhStatusInstalled">✅ Installed: {0}</x:String>
            <x:String x:Key="Cli.GhStatusAuthAs">✅ Authenticated as: {0}</x:String>
            <x:String x:Key="Cli.GhStatusNotAuth">❌ Not authenticated. Run 'gh-auth login' to sign in</x:String>
            <x:String x:Key="Cli.GhStatusWorkDir">📁 Working directory:</x:String>
            <x:String x:Key="Cli.GhStatusRepo">📦 Repository: {0}</x:String>
            <x:String x:Key="Cli.GhNotInstalled">❌ GitHub CLI is not installed. Run: gh-install</x:String>
            <x:String x:Key="Cli.GhCopilotSuggestion">🤖 Copilot suggestion:</x:String>
            <x:String x:Key="Cli.GhCopilotFailed">❌ Copilot task failed: {0}</x:String>
            <x:String x:Key="Cli.GhModelUpdated">✅ Copilot model updated.</x:String>
            <x:String x:Key="Cli.GhModelFailed">❌ Failed to set model: {0}</x:String>
            <x:String x:Key="Cli.GhExtensionNotInstalled">❌ GitHub Copilot extension is not installed.</x:String>
            <x:String x:Key="Cli.GhExtensionInstallHint">Run: gh-extension install github/gh-copilot</x:String>
            <x:String x:Key="Cli.GhCopilotLaunching">🚀 Launching GitHub Copilot CLI in external terminal...</x:String>
            <x:String x:Key="Cli.GhCopilotOpened">✅ Opened GitHub Copilot CLI in {0}.</x:String>
            <x:String x:Key="Cli.GhCopilotTip">💡 Tip: After using Copilot, copy the suggested command and paste it back here.</x:String>
            <x:String x:Key="Cli.GhCopilotError">❌ Error starting GitHub Copilot: {0}</x:String>
            <x:String x:Key="Cli.GhConfigList">⚙️ Configuration:</x:String>
            <x:String x:Key="Cli.GhConfigDone">✅ Config operation completed:</x:String>
            <x:String x:Key="Cli.GhConfigFailed">❌ Failed: {0}</x:String>
            <x:String x:Key="Cli.GhExtensionList">🔌 Extensions:</x:String>
            <x:String x:Key="Cli.GhExtensionDone">✅ Extension operation completed:</x:String>
            <x:String x:Key="Cli.GhExtensionFailed">❌ Failed: {0}</x:String>
            <x:String x:Key="Cli.TerminalOpened">✅ Opened {0}</x:String>
            <x:String x:Key="Cli.TerminalWorkDir">📁 Working directory: {0}</x:String>
            <x:String x:Key="Cli.TerminalCommand">🔧 Command: {0}</x:String>
            <x:String x:Key="Cli.TerminalError">❌ Error opening terminal: {0}</x:String>
            <x:String x:Key="Cli.PowerShellOpened">✅ Opened PowerShell</x:String>
            <x:String x:Key="Cli.PowerShellError">❌ Error opening PowerShell: {0}</x:String>
        
            <!-- ── CLI Usage strings (command argument errors) ───────────── -->
            <x:String x:Key="Cli.Usage.Create">Usage: create &lt;path&gt; [--content &lt;content&gt;] [--dir]</x:String>
            <x:String x:Key="Cli.Usage.Touch">Usage: touch &lt;path&gt;</x:String>
            <x:String x:Key="Cli.Usage.Mkdir">Usage: mkdir &lt;path&gt;</x:String>
            <x:String x:Key="Cli.Usage.Template">Usage: template &lt;template-name&gt; &lt;path&gt;&#10;&#10;Available templates:&#10;  class - C# class file&#10;  interface - C# interface file&#10;  record - C# record file&#10;  enum - C# enum file&#10;  service - C# service class&#10;  viewmodel - MVVM ViewModel&#10;  html - HTML file&#10;  json - JSON file&#10;  xml - XML file</x:String>
            <x:String x:Key="Cli.Usage.Delete">Usage: delete &lt;path&gt; [--force]</x:String>
            <x:String x:Key="Cli.Usage.Rmdir">Usage: rmdir &lt;path&gt; [--force]</x:String>
            <x:String x:Key="Cli.Usage.Rename">Usage: rename &lt;old-path&gt; &lt;new-name&gt;</x:String>
            <x:String x:Key="Cli.Usage.Mv">Usage: mv &lt;source&gt; &lt;destination&gt;</x:String>
            <x:String x:Key="Cli.Usage.Copy">Usage: copy &lt;source&gt; &lt;destination&gt;</x:String>
            <x:String x:Key="Cli.Usage.Write">Usage: write &lt;path&gt; &lt;content&gt;</x:String>
            <x:String x:Key="Cli.Usage.Append">Usage: append &lt;path&gt; &lt;content&gt;</x:String>
            <x:String x:Key="Cli.Usage.Read">Usage: read &lt;path&gt;</x:String>
            <x:String x:Key="Cli.Usage.Cd">Usage: cd &lt;path&gt;</x:String>
            <x:String x:Key="Cli.Usage.Find">Usage: find &lt;pattern&gt; [--path &lt;dir&gt;]</x:String>
            <x:String x:Key="Cli.Usage.Search">Usage: search &lt;text&gt; [--path &lt;dir&gt;] [--ext &lt;extension&gt;]</x:String>
            <x:String x:Key="Cli.Usage.Info">Usage: info &lt;path&gt;</x:String>
            <x:String x:Key="Cli.Usage.Exists">Usage: exists &lt;path&gt;</x:String>
            <x:String x:Key="Cli.Usage.GhTask">Usage: gh-t "&lt;task description&gt;"</x:String>
            <x:String x:Key="Cli.Usage.GhModel">Usage: gh-m &lt;model name&gt;&#10;&#10;Example:&#10;  gh-m gpt-4o</x:String>
        
            <!-- ── CLI Info command output labels ────────────────────────── -->
            <x:String x:Key="Cli.Info.Directory">Directory: {0}</x:String>
            <x:String x:Key="Cli.Info.File">File: {0}</x:String>
            <x:String x:Key="Cli.Info.Size">Size: {0}</x:String>
            <x:String x:Key="Cli.Info.Created">Created: {0}</x:String>
            <x:String x:Key="Cli.Info.Modified">Modified: {0}</x:String>
            <x:String x:Key="Cli.Info.Contains">Contains: {0} files, {1} subdirectories</x:String>
            <x:String x:Key="Cli.Info.Extension">Extension: {0}</x:String>
        
            <!-- ── CLI Exists command type labels ────────────────────────── -->
            <x:String x:Key="Cli.Exists.TypeDir">directory</x:String>
            <x:String x:Key="Cli.Exists.TypeFile">file</x:String>
            <x:String x:Key="Cli.Exists.TypeNone">nothing</x:String>
        
            <!-- ── CLI Help per-command strings ──────────────────────────── -->
            <x:String x:Key="Cli.Help.Command">Command: {0}</x:String>
            <x:String x:Key="Cli.Help.Description">Description: {0}</x:String>
            <x:String x:Key="Cli.Help.Usage">Usage: {0} {1}</x:String>
            <x:String x:Key="Cli.Help.Title">Copilot CLI Commands:</x:String>
            <x:String x:Key="Cli.Help.FileOps">File Operations:</x:String>
            <x:String x:Key="Cli.Help.Create">  create, new     - Create a new file or directory</x:String>
            <x:String x:Key="Cli.Help.Delete">  delete, rm      - Delete a file or directory</x:String>
            <x:String x:Key="Cli.Help.Touch">  touch           - Create an empty file or update timestamp</x:String>
            <x:String x:Key="Cli.Help.Template">  template        - Create file from template</x:String>
            <x:String x:Key="Cli.Help.DirOps">Directory Operations:</x:String>
            <x:String x:Key="Cli.Help.Mkdir">  mkdir           - Create a new directory</x:String>
            <x:String x:Key="Cli.Help.Rmdir">  rmdir           - Remove a directory</x:String>
            <x:String x:Key="Cli.Help.FileManip">File Manipulation:</x:String>
            <x:String x:Key="Cli.Help.Rename">  rename          - Rename a file or directory</x:String>
            <x:String x:Key="Cli.Help.Mv">  mv              - Move a file or directory</x:String>
            <x:String x:Key="Cli.Help.Copy">  copy, cp        - Copy a file or directory</x:String>
            <x:String x:Key="Cli.Help.FileContent">File Content:</x:String>
            <x:String x:Key="Cli.Help.Write">  write           - Write content to a file</x:String>
            <x:String x:Key="Cli.Help.Append">  append          - Append content to a file</x:String>
            <x:String x:Key="Cli.Help.Read">  read, cat       - Display file content</x:String>
            <x:String x:Key="Cli.Help.NavList">Navigation &amp; Listing:</x:String>
            <x:String x:Key="Cli.Help.Ls">  ls, dir         - List directory contents</x:String>
            <x:String x:Key="Cli.Help.Tree">  tree            - Display directory tree</x:String>
            <x:String x:Key="Cli.Help.Pwd">  pwd             - Print working directory</x:String>
            <x:String x:Key="Cli.Help.Cd">  cd              - Change working directory</x:String>
            <x:String x:Key="Cli.Help.SearchSec">Search:</x:String>
            <x:String x:Key="Cli.Help.Find">  find            - Find files by name pattern</x:String>
            <x:String x:Key="Cli.Help.Search">  search          - Search for text in files</x:String>
            <x:String x:Key="Cli.Help.InfoSec">Information:</x:String>
            <x:String x:Key="Cli.Help.Info">  info            - Show file/directory information</x:String>
            <x:String x:Key="Cli.Help.Exists">  exists          - Check if path exists</x:String>
            <x:String x:Key="Cli.Help.Help">  help            - Show this help or command details</x:String>
            <x:String x:Key="Cli.Help.GhSec">GitHub CLI — Setup &amp; Status:</x:String>
            <x:String x:Key="Cli.Help.GhInstall">  gh-install      - Install GitHub CLI via winget</x:String>
            <x:String x:Key="Cli.Help.GhAuth">  gh-auth         - Authenticate with GitHub (login/logout/status/token)</x:String>
            <x:String x:Key="Cli.Help.GhStatus">  gh-status       - Show GitHub CLI installation and auth status</x:String>
            <x:String x:Key="Cli.Help.GhConfig">  gh-config       - Manage gh configuration</x:String>
            <x:String x:Key="Cli.Help.GhExtension">  gh-extension    - Manage gh extensions (e.g. install Copilot)</x:String>
            <x:String x:Key="Cli.Help.TermSec">🖥️ External Terminal Commands:</x:String>
            <x:String x:Key="Cli.Help.Terminal">  terminal [cmd]       - Open Windows Terminal or CMD</x:String>
            <x:String x:Key="Cli.Help.CmdExt">  cmd-ext [cmd]        - Open external Command Prompt window</x:String>
            <x:String x:Key="Cli.Help.Wt">  wt [cmd]             - Open Windows Terminal</x:String>
            <x:String x:Key="Cli.Help.PsExt">  powershell-ext [cmd] - Open external PowerShell window</x:String>
            <x:String x:Key="Cli.Help.Footer">Use 'help &lt;command&gt;' for more details on a specific command.</x:String>
            <x:String x:Key="Search.FileNamePlaceholder">File name (e.g. *.cs, Program)</x:String>
            <x:String x:Key="Search.ContentPlaceholder">Search text...</x:String>
            <x:String x:Key="Search.ReplacePlaceholder">Replace...</x:String>
            <x:String x:Key="Search.Scope">Scope:</x:String>
            <x:String x:Key="Search.WholeSolution">Whole Solution</x:String>
            <x:String x:Key="Search.FindFiles">Find Files by Name</x:String>
            <x:String x:Key="Search.FindInFiles">Find in Files (by content)</x:String>
            <x:String x:Key="Search.ReplaceAll">Replace All</x:String>
            <x:String x:Key="Search.Case">Case</x:String>
            <x:String x:Key="Search.Regex">Regex</x:String>
            <x:String x:Key="Search.Word">Word</x:String>
        
            <!-- ── 2. WelcomeWindow ──────────────────────────────────────── -->
            <x:String x:Key="Welcome.WindowLabel">Welcome Window</x:String>
            <x:String x:Key="Welcome.Title">Welcome to Insait Edit</x:String>
            <x:String x:Key="Welcome.Subtitle">C# IDE powered by Insait Code Editor</x:String>
            <x:String x:Key="Welcome.Version">Version 1.0.2 Preview</x:String>
            <x:String x:Key="Welcome.NewSolution">New Solution</x:String>
            <x:String x:Key="Welcome.NewSolutionDesc">Create a new .sln solution</x:String>
            <x:String x:Key="Welcome.NewProject">New Project</x:String>
            <x:String x:Key="Welcome.NewProjectDesc">Create a new C# project</x:String>
            <x:String x:Key="Welcome.Open">Open</x:String>
            <x:String x:Key="Welcome.OpenDesc">Open project or solution</x:String>
            <x:String x:Key="Welcome.CloneRepository">Clone Repository</x:String>
            <x:String x:Key="Welcome.CloneRepositoryDesc">Get code from Git repository</x:String>
            <x:String x:Key="Welcome.Documentation">Documentation</x:String>
            <x:String x:Key="Welcome.GitHub">GitHub</x:String>
            <x:String x:Key="Welcome.Settings">Settings</x:String>
            <x:String x:Key="Welcome.SearchRecent">🔍  Search recent projects...</x:String>
            <x:String x:Key="Welcome.ClearAll">Clear All</x:String>
            <x:String x:Key="Welcome.RecentProjects">Recent Projects</x:String>
            <!-- Added aliases / keys required by XAML usages -->
            <x:String x:Key="RecentProjects">Recent Projects</x:String>
            <x:String x:Key="RecentProjectsWindow.Count">{0} projects</x:String>
            <x:String x:Key="RecentProjectsWindow.RemoveTooltip">Remove from list</x:String>
            <x:String x:Key="RecentProjectsWindow.EmptyTitle">No recent projects</x:String>
            <x:String x:Key="RecentProjectsWindow.EmptySubtitle">Open or create a project to see it here</x:String>
            <x:String x:Key="RecentProjectsWindow.OpenProject">Open Project…</x:String>
            <x:String x:Key="RecentProjectsWindow.OpenPickerTitle">Open Project or Solution</x:String>
            <x:String x:Key="RecentProjectsWindow.FileType.Solution">C# Solution</x:String>
            <x:String x:Key="RecentProjectsWindow.FileType.Project">C# Project</x:String>
            <x:String x:Key="RecentProjectsWindow.FileType.All">All Files</x:String>
            <x:String x:Key="DefaultTitle">Quick Fix</x:String>
        
            <!-- ── WelcomeScreen (in-IDE start screen) ──────────────────── -->
            <x:String x:Key="WelcomeScreen.Subtitle">C# IDE · Powered by Insait Code Engine</x:String>
            <x:String x:Key="WelcomeScreen.NewProject">New Project</x:String>
            <x:String x:Key="WelcomeScreen.Open">Open</x:String>
            <x:String x:Key="WelcomeScreen.Clone">Clone</x:String>
            <x:String x:Key="WelcomeScreen.Tip">Open a project or create a new one to get started</x:String>
        
            <!-- ── 3. MenuWindow ─────────────────────────────────────────── -->
            <x:String x:Key="Menu.Title">Menu</x:String>
            <x:String x:Key="Menu.File">File</x:String>
            <x:String x:Key="Menu.Edit">Edit</x:String>
            <x:String x:Key="Menu.View">View</x:String>
            <x:String x:Key="Menu.Build">Build</x:String>
            <x:String x:Key="Menu.Debug">Debug</x:String>
            <x:String x:Key="Menu.Tools">Tools</x:String>
            <x:String x:Key="Menu.Help">Help</x:String>
            <x:String x:Key="Menu.Language">🌐 Language</x:String>
            <!-- File menu -->
            <x:String x:Key="Menu.SolutionProject">📦 Solution &amp; Project</x:String>
            <x:String x:Key="Menu.NewSolution">📦 New Solution...</x:String>
            <x:String x:Key="Menu.NewProject">📁 New Project...</x:String>
            <x:String x:Key="Menu.AddProjectToSolution">➕ Add Project to Solution...</x:String>
            <x:String x:Key="Menu.FileOperations">📁 File Operations</x:String>
            <x:String x:Key="Menu.NewFile">📄 New File</x:String>
            <x:String x:Key="Menu.OpenFile">📂 Open File...</x:String>
            <x:String x:Key="Menu.OpenFolder">📁 Open Folder...</x:String>
            <x:String x:Key="Menu.OpenSolution">📦 Open Solution...</x:String>
            <x:String x:Key="Menu.Save">💾 Save</x:String>
            <x:String x:Key="Menu.SaveAs">💾 Save As...</x:String>
            <x:String x:Key="Menu.SaveAll">💾 Save All</x:String>
            <x:String x:Key="Menu.FileAssociations">⚙️ File Associations</x:String>
            <x:String x:Key="Menu.SetDefault">🔗 Set as Default for Supported Files</x:String>
            <x:String x:Key="Menu.OpenDefaultApps">⚙️ Open Default Apps Settings</x:String>
            <x:String x:Key="Menu.Exit">🚪 Exit</x:String>
            <!-- Edit menu -->
            <x:String x:Key="Menu.UndoRedo">↩️ Undo/Redo</x:String>
            <x:String x:Key="Menu.Undo">↩️ Undo</x:String>
            <x:String x:Key="Menu.Redo">↪️ Redo</x:String>
            <x:String x:Key="Menu.FindReplace">🔍 Find &amp; Replace</x:String>
            <x:String x:Key="Menu.Find">🔍 Find</x:String>
            <x:String x:Key="Menu.Replace">🔄 Replace</x:String>
            <x:String x:Key="Menu.FindInFiles">🔍 Find in Files</x:String>
            <x:String x:Key="Menu.Code">📝 Code</x:String>
            <x:String x:Key="Menu.FormatDocument">📋 Format Document</x:String>
            <x:String x:Key="Menu.ToggleComment">💬 Toggle Comment</x:String>
            <!-- View menu -->
            <x:String x:Key="Menu.Panels">📊 Panels</x:String>
            <x:String x:Key="Menu.AIAssistant">🤖 AI Assistant</x:String>
            <x:String x:Key="Menu.Explorer">📁 Explorer</x:String>
            <x:String x:Key="Menu.Search">🔍 Search</x:String>
            <x:String x:Key="Menu.SourceControl">🔀 Source Control</x:String>
            <x:String x:Key="Menu.FocusLayout">🎯 Focus Layout</x:String>
            <x:String x:Key="Menu.ToggleLeftPanel">📁 Left Panel (Explorer)</x:String>
            <x:String x:Key="Menu.ToggleBottomPanel">💻 Bottom Panel (Terminal)</x:String>
            <x:String x:Key="Menu.ToggleAIPanel">🤖 AI Panel (Copilot)</x:String>
            <x:String x:Key="Menu.ZenMode">🧘 Zen Mode (Editor Only)</x:String>
            <x:String x:Key="Menu.Preview">🎨 Preview</x:String>
            <x:String x:Key="Menu.PreviewAxaml">🎨 Preview AXAML Design</x:String>
            <x:String x:Key="Menu.BottomPanel">⬇️ Bottom Panel</x:String>
            <x:String x:Key="Menu.Terminal">💻 Terminal</x:String>
            <x:String x:Key="Menu.NewTerminal">➕ New Terminal</x:String>
            <x:String x:Key="Menu.Problems">⚠️ Problems</x:String>
            <x:String x:Key="Menu.BuildOutput">📋 Build Output</x:String>
            <x:String x:Key="Menu.RunOutput">▶️ Run Output</x:String>
            <x:String x:Key="Menu.DebugConsole">🐛 Debug Console</x:String>
            <x:String x:Key="Menu.Window">🖥️ Window</x:String>
            <x:String x:Key="Menu.Minimize">➖ Minimize</x:String>
            <x:String x:Key="Menu.MaximizeRestore">🔲 Maximize/Restore</x:String>
            <!-- Build menu -->
            <x:String x:Key="Menu.BuildHeader">🔨 Build</x:String>
            <x:String x:Key="Menu.BuildProject">🔨 Build Project</x:String>
            <x:String x:Key="Menu.RebuildProject">🔄 Rebuild Project</x:String>
            <x:String x:Key="Menu.CleanProject">🧹 Clean Project</x:String>
            <x:String x:Key="Menu.Analysis">🔍 Analysis</x:String>
            <x:String x:Key="Menu.AnalyzeCode">🔍 Analyze Code</x:String>
            <x:String x:Key="Menu.RunHeader">▶️ Run</x:String>
            <x:String x:Key="Menu.RunProject">▶️ Run Project</x:String>
            <x:String x:Key="Menu.StopProject">⏹️ Stop</x:String>
            <x:String x:Key="Menu.RunConfigurations">⚙️ Run Configurations...</x:String>
            <x:String x:Key="Menu.PackagesDeploy">📦 Packages &amp; Deploy</x:String>
            <x:String x:Key="Menu.RestoreNuGet">📦 Restore NuGet Packages</x:String>
            <x:String x:Key="Menu.Publish">📤 Publish...</x:String>
            <!-- Debug menu -->
            <x:String x:Key="Menu.DebugHeader">🐛 Debug</x:String>
            <x:String x:Key="Menu.StartDebugging">▶️ Start Debugging</x:String>
            <x:String x:Key="Menu.StartWithout">⏸️ Start Without Debugging</x:String>
            <x:String x:Key="Menu.StopDebugging">⏹️ Stop Debugging</x:String>
            <x:String x:Key="Menu.Breakpoints">🔴 Breakpoints</x:String>
            <x:String x:Key="Menu.ToggleBreakpoint">🔴 Toggle Breakpoint</x:String>
            <x:String x:Key="Menu.DeleteAllBreakpoints">❌ Delete All Breakpoints</x:String>
            <x:String x:Key="Menu.Step">👣 Step</x:String>
            <x:String x:Key="Menu.StepOver">➡️ Step Over</x:String>
            <x:String x:Key="Menu.StepInto">⬇️ Step Into</x:String>
            <x:String x:Key="Menu.StepOut">⬆️ Step Out</x:String>
            <!-- Tools menu -->
            <x:String x:Key="Menu.ToolsHeader">🔧 Tools</x:String>
            <x:String x:Key="Menu.OpenTerminal">💻 Open Terminal</x:String>
            <x:String x:Key="Menu.RefreshFileTree">🔄 Refresh File Tree</x:String>
            <x:String x:Key="Menu.SettingsHeader">⚙️ Settings</x:String>
            <x:String x:Key="Menu.Settings">⚙️ Settings</x:String>
            <x:String x:Key="Menu.Theme">🎨 Theme</x:String>
            <x:String x:Key="Menu.KeyboardShortcuts">⌨️ Keyboard Shortcuts</x:String>
            <x:String x:Key="Menu.NuGetHeader">📦 NuGet</x:String>
            <x:String x:Key="Menu.ManageNuGet">📦 Manage NuGet Packages</x:String>
            <!-- Help menu -->
            <x:String x:Key="Menu.HelpHeader">❓ Help</x:String>
            <x:String x:Key="Menu.Documentation">📖 Documentation</x:String>
            <x:String x:Key="Menu.GettingStarted">🎓 Getting Started</x:String>
            <x:String x:Key="Menu.KeyboardShortcutsHelp">⌨️ Keyboard Shortcuts</x:String>
            <x:String x:Key="Menu.Feedback">📣 Feedback</x:String>
            <x:String x:Key="Menu.ReportIssue">🐛 Report Issue</x:String>
            <x:String x:Key="Menu.FeatureRequest">💡 Feature Request</x:String>
            <x:String x:Key="Menu.About">ℹ️ About</x:String>
            <x:String x:Key="Menu.AboutInsait">ℹ️ About Insait Edit</x:String>
            <x:String x:Key="Menu.CheckUpdates">📋 Check for Updates</x:String>
        
            <!-- ── 4. NewProjectWindow ───────────────────────────────────── -->
            <x:String x:Key="NewProject.Title">New Project</x:String>
            <x:String x:Key="NewProject.SelectTemplate">Select a template</x:String>
            <x:String x:Key="NewProject.Configure">Configure your project</x:String>
            <x:String x:Key="NewProject.ProjectName">Project name</x:String>
            <x:String x:Key="NewProject.Location">Location</x:String>
            <x:String x:Key="NewProject.SolutionName">Solution name</x:String>
            <x:String x:Key="NewProject.SolutionFormat">Solution file format</x:String>
            <x:String x:Key="NewProject.PlaceSameDir">Place solution and project in the same directory</x:String>
            <x:String x:Key="NewProject.CreateGitRepo">Create Git repository</x:String>
            <x:String x:Key="NewProject.Cancel">Cancel</x:String>
            <x:String x:Key="NewProject.Create">Create</x:String>
        
            <!-- ── 5. NewSolutionWindow ──────────────────────────────────── -->
            <x:String x:Key="NewSolution.Title">New Solution</x:String>
            <x:String x:Key="NewSolution.SolutionName">Solution name</x:String>
            <x:String x:Key="NewSolution.Location">Location</x:String>
            <x:String x:Key="NewSolution.CreateSolutionDir">Create solution directory</x:String>
            <x:String x:Key="NewSolution.InitGitRepo">Initialize Git repository</x:String>
            <x:String x:Key="NewSolution.SolutionFormat">Solution format</x:String>
            <x:String x:Key="NewSolution.CreatedAt">Solution will be created at:</x:String>
            <x:String x:Key="NewSolution.Cancel">Cancel</x:String>
            <x:String x:Key="NewSolution.Create">Create</x:String>
        
            <!-- ── 6. AddNewItemWindow ───────────────────────────────────── -->
            <x:String x:Key="AddItem.Title">Add New Item</x:String>
            <x:String x:Key="AddItem.CSharpTypes">C# Types</x:String>
            <x:String x:Key="AddItem.Class">Class</x:String>
            <x:String x:Key="AddItem.Interface">Interface</x:String>
            <x:String x:Key="AddItem.Record">Record</x:String>
            <x:String x:Key="AddItem.Struct">Struct</x:String>
            <x:String x:Key="AddItem.Enum">Enum</x:String>
            <x:String x:Key="AddItem.Delegate">Delegate</x:String>
            <x:String x:Key="AddItem.Exception">Exception</x:String>
            <x:String x:Key="AddItem.GlobalUsings">Global Usings</x:String>
            <x:String x:Key="AddItem.FSharpTypes">F# Types</x:String>
            <x:String x:Key="AddItem.AvaloniaUI">Avalonia UI</x:String>
            <x:String x:Key="AddItem.ConfigData">Config / Data</x:String>
            <x:String x:Key="AddItem.DotNetConfig">.NET Config</x:String>
            <x:String x:Key="AddItem.Git">Git</x:String>
            <x:String x:Key="AddItem.Name">Name</x:String>
            <x:String x:Key="AddItem.Preview">Preview</x:String>
            <x:String x:Key="AddItem.Location">Location:</x:String>
            <x:String x:Key="AddItem.AddToProject">Add to project file (.csproj / .fsproj)</x:String>
            <x:String x:Key="AddItem.Cancel">Cancel</x:String>
            <x:String x:Key="AddItem.Add">Add</x:String>
        
            <!-- ── 7. AddProjectToSolutionWindow ─────────────────────────── -->
            <x:String x:Key="AddProject.Title">Add New Project to Solution</x:String>
            <x:String x:Key="AddProject.Solution">Solution:</x:String>
            <x:String x:Key="AddProject.Template">Project template</x:String>
            <x:String x:Key="AddProject.ProjectName">Project name</x:String>
            <x:String x:Key="AddProject.CreateGitRepo">Create Git repository</x:String>
            <x:String x:Key="AddProject.CreatedAt">Project will be created at:</x:String>
            <x:String x:Key="AddProject.Cancel">Cancel</x:String>
            <x:String x:Key="AddProject.Add">Add Project</x:String>
        
            <!-- ── 8. CloneRepositoryWindow ──────────────────────────────── -->
            <x:String x:Key="Clone.Title">Clone Repository</x:String>
            <x:String x:Key="Clone.RepoUrl">Repository URL</x:String>
            <x:String x:Key="Clone.LocalPath">Local Path</x:String>
            <x:String x:Key="Clone.Browse">Browse...</x:String>
            <x:String x:Key="Clone.Cloning">Cloning...</x:String>
            <x:String x:Key="Clone.Cancel">Cancel</x:String>
            <x:String x:Key="Clone.Clone">Clone</x:String>
        
            <!-- ── 9. GitWindow ──────────────────────────────────────────── -->
            <x:String x:Key="Git.Title">Git — Insait Edit</x:String>
            <x:String x:Key="Git.Scope">Scope:</x:String>
            <x:String x:Key="Git.SolutionAll">📁 Solution (all projects)</x:String>
            <x:String x:Key="Git.Refresh">Refresh</x:String>
            <x:String x:Key="Git.Pull">Pull / Update</x:String>
            <x:String x:Key="Git.Push">Push</x:String>
            <x:String x:Key="Git.Fetch">Fetch</x:String>
            <x:String x:Key="Git.Stash">Stash</x:String>
            <x:String x:Key="Git.PopStash">Pop stash</x:String>
            <x:String x:Key="Git.Rollback">Rollback all</x:String>
            <x:String x:Key="Git.CreateRepo">Create GitHub repository</x:String>
            <x:String x:Key="Git.Console">Git console</x:String>
            <x:String x:Key="Git.FocusCommitMessage">Focus commit message</x:String>
            <x:String x:Key="Git.SelectAll">Select / deselect all</x:String>
            <x:String x:Key="Git.FilesToCommit">Files to commit</x:String>
            <x:String x:Key="Git.StageSelected">Stage selected</x:String>
            <x:String x:Key="Git.NoRepoTitle">No Git repository</x:String>
            <x:String x:Key="Git.NoRepoDescription">Open a project with a Git repository or initialize a new one.</x:String>
            <x:String x:Key="Git.InitializeRepository">Initialize Repository</x:String>
            <x:String x:Key="Git.CloneRepository">Clone Repository…</x:String>
            <x:String x:Key="Git.NothingToCommit">Nothing to commit</x:String>
            <x:String x:Key="Git.WorkingTreeClean">Working tree is clean</x:String>
            <x:String x:Key="Git.MenuShowDiff">Show Diff</x:String>
            <x:String x:Key="Git.MenuDiscard">Discard</x:String>
            <x:String x:Key="Git.MenuOpenFile">Open File</x:String>
            <x:String x:Key="Git.Discard">Discard</x:String>
            <x:String x:Key="Git.CommitMessageWatermark">Commit message (Ctrl+Enter to commit)</x:String>
            <x:String x:Key="Git.Commit">Commit</x:String>
            <x:String x:Key="Git.CommitAndPush">Commit and Push</x:String>
            <x:String x:Key="Git.AmendLastCommit">Amend last commit</x:String>
            <x:String x:Key="Git.TabLog">Log</x:String>
            <x:String x:Key="Git.TabChanges">Changes</x:String>
            <x:String x:Key="Git.AllBranches">All branches</x:String>
            <x:String x:Key="Git.FilterCommits">Filter commits…</x:String>
            <x:String x:Key="Git.Local">Local</x:String>
            <x:String x:Key="Git.Remote">Remote</x:String>
            <x:String x:Key="Git.MenuCheckoutRevision">Checkout this revision</x:String>
            <x:String x:Key="Git.MenuNewBranchFromHere">New Branch from here…</x:String>
            <x:String x:Key="Git.MenuCherryPick">Cherry-Pick</x:String>
            <x:String x:Key="Git.MenuRevertCommit">Revert Commit</x:String>
            <x:String x:Key="Git.MenuCopyHash">Copy Hash</x:String>
            <x:String x:Key="Git.CommitDetailPlaceholder">Select a commit to see details</x:String>
            <x:String x:Key="Git.SelectFileToViewDiff">Select a file to view diff</x:String>
            <x:String x:Key="Git.Clear">Clear</x:String>
            <x:String x:Key="Git.ConsoleReady">Git console ready…</x:String>
            <x:String x:Key="Git.Loading">Loading…</x:String>
            <x:String x:Key="Git.Refreshing">Refreshing…</x:String>
            <x:String x:Key="Git.Staging">Staging…</x:String>
            <x:String x:Key="Git.NoDiffAvailable">(No diff available)</x:String>
            <x:String x:Key="Git.CheckingOut">Checking out…</x:String>
            <x:String x:Key="Git.CreateBranchFromCommitHint">Create branch from commit — use Branch menu</x:String>
            <x:String x:Key="Git.CherryPicking">Cherry-picking…</x:String>
            <x:String x:Key="Git.RevertResetTitle">Revert / Reset commit</x:String>
            <x:String x:Key="Git.RevertResetCommit">Commit: {0} — {1}</x:String>
            <x:String x:Key="Git.RevertOptionButton">↩ Revert (create undo-commit)</x:String>
            <x:String x:Key="Git.RevertOptionDescription">↩ Revert — applies the inverse of this commit as a new commit (safe, keeps history).</x:String>
            <x:String x:Key="Git.ResetRootButton">⚠ Reset to initial state (hard reset)</x:String>
            <x:String x:Key="Git.ResetButton">⚠ Reset TO this commit (hard reset — loses newer commits)</x:String>
            <x:String x:Key="Git.ResetToCommitButton">Reset project to this commit</x:String>
            <x:String x:Key="Git.ResetRootDescription">Reset — restores the project exactly to the initial commit state. All subsequent commits and local changes will be lost.</x:String>
            <x:String x:Key="Git.ResetDescription">Reset — moves HEAD (and the branch) back to this commit. All newer commits and local changes WILL BE LOST permanently.</x:String>
            <x:String x:Key="Git.Reverting">Reverting…</x:String>
            <x:String x:Key="Git.ResettingInitialCommit">Resetting to initial commit…</x:String>
            <x:String x:Key="Git.Resetting">Resetting…</x:String>
            <x:String x:Key="Git.ResetToInitialSuccess">✅ Reset to initial commit {0}</x:String>
            <x:String x:Key="Git.ResetSuccess">✅ Reset to {0}</x:String>
            <x:String x:Key="Git.ResetFailed">❌ Reset failed: {0}</x:String>
            <x:String x:Key="Git.Copied">Copied: {0}</x:String>
            <x:String x:Key="Git.Pulling">Pulling…</x:String>
            <x:String x:Key="Git.PullCompleted">Pull completed</x:String>
            <x:String x:Key="Git.PullError">Pull error: {0}</x:String>
            <x:String x:Key="Git.Pushing">Pushing…</x:String>
            <x:String x:Key="Git.PushCompleted">Push completed</x:String>
            <x:String x:Key="Git.PushError">Push error: {0}</x:String>
            <x:String x:Key="Git.Fetching">Fetching…</x:String>
            <x:String x:Key="Git.FetchCompleted">Fetch completed</x:String>
            <x:String x:Key="Git.FetchError">Fetch error: {0}</x:String>
            <x:String x:Key="Git.Stashing">Stashing…</x:String>
            <x:String x:Key="Git.StashCreated">Stash created</x:String>
            <x:String x:Key="Git.StashError">Stash error: {0}</x:String>
            <x:String x:Key="Git.PoppingStash">Popping stash…</x:String>
            <x:String x:Key="Git.StashPopped">Stash popped</x:String>
            <x:String x:Key="Git.PopError">Pop error: {0}</x:String>
            <x:String x:Key="Git.RollingBack">Rolling back…</x:String>
            <x:String x:Key="Git.RollbackCompleted">Rollback completed</x:String>
            <x:String x:Key="Git.Error">Error: {0}</x:String>
            <x:String x:Key="Git.CreateRepoDialogTitle">Create GitHub Repository</x:String>
            <x:String x:Key="Git.CreateRepoDialogHeader">🐙 Create GitHub Repository</x:String>
            <x:String x:Key="Git.GhCliNotFound">⚠ GitHub CLI ('gh') not found. Install from https://cli.github.com</x:String>
            <x:String x:Key="Git.NotLoggedIn">⚠ Not signed in to GitHub. Use the built-in GitHub sign-in flow.</x:String>
            <x:String x:Key="Git.LoginWithGitHub">Login with GitHub</x:String>
            <x:String x:Key="Git.GitHubLoginOpened">GitHub authentication completed successfully</x:String>
            <x:String x:Key="Git.LoggedInAs">Logged in as @{0}</x:String>
            <x:String x:Key="Git.RepositoryName">Repository name:</x:String>
            <x:String x:Key="Git.DescriptionOptional">Description (optional):</x:String>
            <x:String x:Key="Git.ShortDescription">Short description</x:String>
            <x:String x:Key="Git.PrivateRepository">Private repository</x:String>
            <x:String x:Key="Git.Create">Create</x:String>
            <x:String x:Key="Git.CreatingRepository">Creating repository…</x:String>
            <x:String x:Key="Git.CreateRepoSuccess">✅ Repository '{0}' created and pushed to GitHub!</x:String>
            <x:String x:Key="Git.CreateRepoFailed">❌ Failed. Make sure 'gh' CLI is installed and authenticated (gh auth login).</x:String>
            <x:String x:Key="Git.GhError">gh error: {0}</x:String>
            <x:String x:Key="Git.NewBranchMenu">➕ New Branch…</x:String>
            <x:String x:Key="Git.RemoteBranches">☁ Remote Branches</x:String>
            <x:String x:Key="Git.CheckingOutBranch">Checking out {0}…</x:String>
            <x:String x:Key="Git.SwitchedTo">Switched to {0}</x:String>
            <x:String x:Key="Git.NewBranch">New Branch</x:String>
            <x:String x:Key="Git.BranchName">Branch name:</x:String>
            <x:String x:Key="Git.CreatingBranch">Creating {0}…</x:String>
            <x:String x:Key="Git.CreatedBranch">Created {0}</x:String>
            <x:String x:Key="Git.TabBranches">Branches</x:String>
            <x:String x:Key="Git.LocalBranches">Local Branches</x:String>
            <x:String x:Key="Git.DeleteBranch">Delete Branch</x:String>
            <x:String x:Key="Git.DeletingBranch">Deleting branch {0}…</x:String>
            <x:String x:Key="Git.DeletedBranch">Branch '{0}' deleted</x:String>
            <x:String x:Key="Git.DeleteBranchConfirm">Are you sure you want to delete branch '{0}'?</x:String>
            <x:String x:Key="Git.DeleteBranchForce">Force delete (even if not merged)</x:String>
            <x:String x:Key="Git.RenameBranch">Rename Branch</x:String>
            <x:String x:Key="Git.RenamingBranch">Renaming branch to {0}…</x:String>
            <x:String x:Key="Git.RenamedBranch">Branch renamed to '{0}'</x:String>
            <x:String x:Key="Git.NewBranchName">New branch name:</x:String>
            <x:String x:Key="Git.MergeBranch">Merge into Current</x:String>
            <x:String x:Key="Git.MergingBranch">Merging {0}…</x:String>
            <x:String x:Key="Git.MergedBranch">Branch '{0}' merged successfully</x:String>
            <x:String x:Key="Git.MergeError">Merge error: {0}</x:String>
            <x:String x:Key="Git.BranchCurrent">current</x:String>
            <x:String x:Key="Git.BranchTracking">tracking: {0}</x:String>
            <x:String x:Key="Git.CreateBranchFromCommit">Create branch from {0}</x:String>
            <x:String x:Key="Git.NewBranchFromCommitName">Branch name (from {0}):</x:String>
            <x:String x:Key="Git.CreatingBranchFromCommit">Creating branch {0} from {1}…</x:String>
            <x:String x:Key="Git.NoBranches">No branches found</x:String>
            <x:String x:Key="Git.NoFilesSelected">No files selected</x:String>
            <x:String x:Key="Git.Committing">Committing…</x:String>
            <x:String x:Key="Git.CommitSuccessful">Commit successful</x:String>
            <x:String x:Key="Git.CommitError">Commit error: {0}</x:String>
            <x:String x:Key="Git.Initializing">Initializing…</x:String>
            <x:String x:Key="Git.RepositoryInitialized">Repository initialized</x:String>
            <x:String x:Key="Git.InitError">Init error: {0}</x:String>
        
            <!-- ── 10. ImageViewerWindow ─────────────────────────────────── -->
            <x:String x:Key="ImageViewer.Title">Image Viewer</x:String>
            <x:String x:Key="ImageViewer.ZoomIn">Zoom In (Ctrl++)</x:String>
            <x:String x:Key="ImageViewer.ZoomOut">Zoom Out (Ctrl+-)</x:String>
            <x:String x:Key="ImageViewer.FitToWindow">Fit to Window</x:String>
            <x:String x:Key="ImageViewer.ActualSize">Actual Size (1:1)</x:String>
            <x:String x:Key="ImageViewer.OpenExternal">Open in system viewer</x:String>
        
            <!-- ── 11. AxamlPreviewWindow ────────────────────────────────── -->
            <x:String x:Key="AxamlPreview.Title">AXAML Preview</x:String>
            <x:String x:Key="AxamlPreview.Refresh">Refresh</x:String>
            <x:String x:Key="AxamlPreview.Size">Size:</x:String>
            <x:String x:Key="AxamlPreview.Free">Free</x:String>
            <x:String x:Key="AxamlPreview.ToggleBg">Toggle background</x:String>
            <x:String x:Key="AxamlPreview.Ready">Ready</x:String>
            <x:String x:Key="AxamlPreview.Error">Error</x:String>
            <x:String x:Key="AxamlPreview.ShowError">Show error details</x:String>
        
            <!-- ── 12. PreviewErrorWindow ────────────────────────────────── -->
            <x:String x:Key="PreviewError.Title">Preview Error Details</x:String>
            <x:String x:Key="PreviewError.Hint">Text is selectable — Ctrl+A to select all</x:String>
            <x:String x:Key="PreviewError.Copy">Copy</x:String>
        
            <!-- ── UserAgreementWindow ──────────────────────────────────── -->
            <x:String x:Key="UserAgreement.Text"><![CDATA[INSAIT EDIT — END USER LICENSE AGREEMENT (EULA)
        ════════════════════════════════════════════════
        
        Last updated: March 2026
        
        Please read this End User License Agreement ("Agreement") carefully before using Insait Edit ("the Software"). By installing or using the Software you agree to be bound by the terms below.
        
        1. LICENSE GRANT
        ─────────────────
        Subject to the terms of this Agreement, you are granted a limited, non-exclusive, non-transferable license to install and use the Software on devices you own or control, solely for your personal or internal business development purposes.
        
        2. RESTRICTIONS
        ─────────────────
        Insait Edit is non-commercial, free software. You may use it to create any projects free of charge, including commercial ones.
        
        You may not:
          • Create applications that copy or closely replicate the graphical style, visual design, or user interface appearance of Insait Edit.
          • Reverse-engineer, decompile, or disassemble the Software except to the extent permitted by applicable law.
          • Remove or alter any proprietary notices or labels on the Software.
        
        3. INTELLECTUAL PROPERTY
        ─────────────────────────
        All title, ownership rights, and intellectual property rights in and to the Software remain with the Insait Edit authors. The Software is protected by copyright laws and international treaty provisions.
        
        4. THIRD-PARTY COMPONENTS
        ──────────────────────────
        The Software may include open-source components governed by their own licenses (for example, AvaloniaUI under the MIT License). Those components are provided under the terms of their respective licenses.
        
        5. DISCLAIMER OF WARRANTIES
        ────────────────────────────
        THE SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT.
        
        6. LIMITATION OF LIABILITY
        ───────────────────────────
        IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY INDIRECT, INCIDENTAL, SPECIAL, OR CONSEQUENTIAL DAMAGES ARISING OUT OF OR RELATING TO THIS AGREEMENT OR THE USE OF THE SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.
        
        7. TERMINATION
        ────────────────
        This license is effective until terminated. It will terminate automatically if you fail to comply with any term of this Agreement. Upon termination you must destroy all copies of the Software in your possession.
        
        8. GOVERNING LAW
        ─────────────────
        This Agreement shall be governed by and construed in accordance with applicable law, without regard to its conflict-of-law provisions.
        
        ════════════════════════════════════════════════
        Thank you for using Insait Edit!]]></x:String>
            <x:String x:Key="UserAgreement.Footer">By using Insait Edit you agree to these terms.</x:String>
            <x:String x:Key="UserAgreement.Close">Close</x:String>
            <x:String x:Key="UserAgreement.Accept">Accept</x:String>
        
            <!-- ── 13. CompoundRunWindow ─────────────────────────────────── -->
            <x:String x:Key="Compound.Title">Compound Run Configurations</x:String>
            <x:String x:Key="Compound.Subtitle">Run multiple projects simultaneously</x:String>
            <x:String x:Key="Compound.Configurations">Compound Configurations</x:String>
            <x:String x:Key="Compound.ConfigName">Configuration Name</x:String>
            <x:String x:Key="Compound.ProjectsToLaunch">Projects to Launch</x:String>
            <x:String x:Key="Compound.ExecutionOptions">Execution Options</x:String>
            <x:String x:Key="Compound.Sequential">Start projects sequentially (one after another)</x:String>
            <x:String x:Key="Compound.StopOnFailure">Stop on first failure</x:String>
            <x:String x:Key="Compound.Delay">Delay between starts (ms)</x:String>
            <x:String x:Key="Compound.LaunchPreview">Launch Preview</x:String>
            <x:String x:Key="Compound.RunAll">▶▶ Run All</x:String>
            <x:String x:Key="Compound.StopAll">■ Stop All</x:String>
            <x:String x:Key="Compound.Apply">Apply</x:String>
            <x:String x:Key="Compound.Cancel">Cancel</x:String>
            <x:String x:Key="Compound.OK">OK</x:String>
            <x:String x:Key="Compound.ConfigurationsDescription">Each compound config runs multiple projects at once</x:String>
            <x:String x:Key="Compound.ProjectCountSuffix">project(s)</x:String>
            <x:String x:Key="Compound.NewTooltip">New Compound Configuration</x:String>
            <x:String x:Key="Compound.RemoveTooltip">Remove Compound Configuration</x:String>
            <x:String x:Key="Compound.DuplicateTooltip">Duplicate</x:String>
            <x:String x:Key="Compound.EmptyTitle">No compound configuration selected</x:String>
            <x:String x:Key="Compound.EmptyHint">Click '+' to create a new compound configuration</x:String>
            <x:String x:Key="Compound.NameWatermark">e.g. Full Stack, Backend + Frontend</x:String>
            <x:String x:Key="Compound.ProjectsDescription">All selected projects will be launched simultaneously</x:String>
            <x:String x:Key="Compound.DelayWatermark">0</x:String>
            <x:String x:Key="Compound.RunAllTooltip">Build and run all projects in this compound configuration</x:String>
            <x:String x:Key="Compound.NoConfigsAvailable">No run configurations available. Open a solution with runnable projects first.</x:String>
            <x:String x:Key="Compound.NoProjectsSelected">No projects selected — check at least one configuration above.</x:String>
            <x:String x:Key="Compound.ModeSequential">Sequential</x:String>
            <x:String x:Key="Compound.ModeParallel">Parallel</x:String>
            <x:String x:Key="Compound.ModeSummary">Mode: {0}  •  {1} project(s)</x:String>
            <x:String x:Key="Compound.DelaySummary">⏱ {0} ms delay between starts</x:String>
            <x:String x:Key="Compound.StopOnFailureSummary">⚠️ Stops on first failure</x:String>
            <x:String x:Key="Compound.DefaultName">Compound #{0}</x:String>
            <x:String x:Key="Compound.CopyName">{0} (Copy)</x:String>
        
            <!-- ── 14. RunConfigurationsWindow ────────────────────────────── -->
            <x:String x:Key="RunConfig.Title">Run/Debug Configurations</x:String>
            <x:String x:Key="RunConfig.SearchPlaceholder">🔍 Search configurations...</x:String>
            <x:String x:Key="RunConfig.Add">Add Configuration</x:String>
            <x:String x:Key="RunConfig.Remove">Remove Configuration</x:String>
            <x:String x:Key="RunConfig.Duplicate">Duplicate Configuration</x:String>
            <x:String x:Key="RunConfig.Run">▶ Run</x:String>
            <x:String x:Key="RunConfig.Debug">🐛 Debug</x:String>
            <x:String x:Key="RunConfig.Apply">Apply</x:String>
            <x:String x:Key="RunConfig.Cancel">Cancel</x:String>
            <x:String x:Key="RunConfig.OK">OK</x:String>
            <x:String x:Key="RunConfig.ManageCompound">⚡ Manage</x:String>
            <x:String x:Key="RunConfig.Single">SINGLE</x:String>
            <x:String x:Key="RunConfig.Compound">COMPOUND</x:String>
            <x:String x:Key="RunConfig.Name">Name</x:String>
            <x:String x:Key="RunConfig.NamePlaceholder">Configuration name</x:String>
            <x:String x:Key="RunConfig.Project">Project</x:String>
            <x:String x:Key="RunConfig.BuildConfig">Build Configuration</x:String>
            <x:String x:Key="RunConfig.TargetFramework">Target Framework</x:String>
            <x:String x:Key="RunConfig.LaunchProfile">Launch Profile (Optional)</x:String>
            <x:String x:Key="RunConfig.WorkingDir">Working Directory</x:String>
            <x:String x:Key="RunConfig.WorkingDirPlaceholder">Working directory path</x:String>
            <x:String x:Key="RunConfig.Arguments">Program Arguments</x:String>
            <x:String x:Key="RunConfig.ArgumentsPlaceholder">--arg1 value1 --arg2 value2</x:String>
            <x:String x:Key="RunConfig.EnvVars">Environment Variables</x:String>
            <x:String x:Key="RunConfig.AddEnvVar">+ Add</x:String>
            <x:String x:Key="RunConfig.NoEnvVars">No environment variables defined</x:String>
            <x:String x:Key="RunConfig.RunOptions">Run Options</x:String>
            <x:String x:Key="RunConfig.AllowParallel">Allow parallel run</x:String>
            <x:String x:Key="RunConfig.BuildBeforeRun">Build before run</x:String>
            <x:String x:Key="RunConfig.ActivateToolWindow">Activate tool window on output</x:String>
            <x:String x:Key="RunConfig.Projects">projects</x:String>
            <x:String x:Key="RunConfig.Default">Default</x:String>
            <x:String x:Key="RunConfig.RunCompound">▶▶ Run Compound</x:String>
            <x:String x:Key="RunConfig.EnvVarKeyPlaceholder">VAR_NAME</x:String>
            <x:String x:Key="RunConfig.EnvVarValuePlaceholder">value</x:String>
            <x:String x:Key="RunConfig.BrowseWorkingDirTitle">Select Working Directory</x:String>
            <x:String x:Key="RunConfig.NewConfig">New Configuration</x:String>
            <x:String x:Key="RunConfig.CopySuffix"> (Copy)</x:String>
        
            <!-- ── 15. PublishWindow ─────────────────────────────────────── -->
            <x:String x:Key="Publish.Title">Publish Project</x:String>
            <x:String x:Key="Publish.TitleBar">Publish Project</x:String>
            <x:String x:Key="Publish.Project">Project</x:String>
            <x:String x:Key="Publish.DeploymentMode">Deployment Mode</x:String>
            <x:String x:Key="Publish.FrameworkDependent">Framework-dependent (requires .NET runtime on target)</x:String>
            <x:String x:Key="Publish.SelfContained">Self-contained (includes .NET runtime)</x:String>
            <x:String x:Key="Publish.Configuration">Configuration</x:String>
            <x:String x:Key="Publish.ConfigurationRelease">Release</x:String>
            <x:String x:Key="Publish.ConfigurationDebug">Debug</x:String>
            <x:String x:Key="Publish.TargetRuntime">Target Runtime</x:String>
            <x:String x:Key="Publish.TargetFramework">Target Framework</x:String>
            <x:String x:Key="Publish.OutputFolder">Output Folder</x:String>
            <x:String x:Key="Publish.OutputPlaceholder">bin\publish</x:String>
            <x:String x:Key="Publish.PortableRuntime">(Portable - Any OS)</x:String>
            <x:String x:Key="Publish.ProfileNone">(None - Use settings below)</x:String>
            <x:String x:Key="Publish.BrowseOutputDialogTitle">Select Output Folder</x:String>
            <x:String x:Key="Publish.BrowseIconDialogTitle">Select Application Icon (.ico)</x:String>
            <x:String x:Key="Publish.IconFileType">Icon files</x:String>
            <x:String x:Key="Publish.AllFiles">All files</x:String>
            <x:String x:Key="Publish.Options">Publish Options</x:String>
            <x:String x:Key="Publish.SingleFile">Produce single file</x:String>
            <x:String x:Key="Publish.SingleFileTooltip">Bundles the application into a single executable</x:String>
            <x:String x:Key="Publish.ReadyToRun">Enable ReadyToRun compilation</x:String>
            <x:String x:Key="Publish.ReadyToRunTooltip">Pre-compiles assemblies to native code for faster startup</x:String>
            <x:String x:Key="Publish.Trim">Trim unused assemblies</x:String>
            <x:String x:Key="Publish.TrimTooltip">Removes unused code to reduce size (may affect reflection)</x:String>
            <x:String x:Key="Publish.Compression">Enable compression in single file</x:String>
            <x:String x:Key="Publish.CompressionTooltip">Compresses the single file to reduce size</x:String>
            <x:String x:Key="Publish.NativeLibs">Include native libraries for self-extract</x:String>
            <x:String x:Key="Publish.NativeLibsTooltip">Bundles native libraries into the single file</x:String>
            <x:String x:Key="Publish.CleanOutput">🗑 Clean output folder before publish</x:String>
            <x:String x:Key="Publish.CleanOutputTooltip">Deletes all files in the output folder before publishing to ensure a clean build</x:String>
            <x:String x:Key="Publish.NoPdb">🚫 No PDB files (DebugType=none)</x:String>
            <x:String x:Key="Publish.NoPdbTooltip">Exclude debug symbol files (.pdb) from the published output. Sets DebugType=none.</x:String>
            <x:String x:Key="Publish.PublishProfile">Publish Profile (Optional)</x:String>
            <x:String x:Key="Publish.NewProfile">+ New Profile</x:String>
            <x:String x:Key="Publish.ProfileHint">Use existing publish profiles from Properties/PublishProfiles folder</x:String>
            <x:String x:Key="Publish.QuickPresets">Quick Presets</x:String>
            <x:String x:Key="Publish.PresetWinX64">🪟 Windows x64</x:String>
            <x:String x:Key="Publish.PresetWinArm64">🪟 Windows ARM64</x:String>
            <x:String x:Key="Publish.PresetLinuxX64">🐧 Linux x64</x:String>
            <x:String x:Key="Publish.PresetMacX64">🍎 macOS x64</x:String>
            <x:String x:Key="Publish.PresetMacArm64">🍎 macOS ARM64</x:String>
            <x:String x:Key="Publish.PresetPortable">📱 Portable</x:String>
            <x:String x:Key="Publish.EstimatedSize">Estimated output size will appear after publish</x:String>
            <x:String x:Key="Publish.Cancel">Cancel</x:String>
            <x:String x:Key="Publish.Publish">📦 Publish</x:String>
            <x:String x:Key="Publish.ApplicationIcon">Application Icon (.ico)</x:String>
            <x:String x:Key="Publish.IconPlaceholder">(none — uses default icon)</x:String>
            <x:String x:Key="Publish.IconHint">Select an .ico file to embed as the application icon in the published executable</x:String>
            <x:String x:Key="Publish.BrowseIcon">Browse for .ico file</x:String>
            <x:String x:Key="Publish.ClearIcon">Clear icon</x:String>
        
            <!-- ── 15b. PublishProgressWindow ────────────────────────────── -->
            <x:String x:Key="PublishProgress.Title">Publishing...</x:String>
            <x:String x:Key="PublishProgress.Preparing">Preparing to publish...</x:String>
            <x:String x:Key="PublishProgress.Publishing">Publishing project...</x:String>
            <x:String x:Key="PublishProgress.Succeeded">Publish succeeded!</x:String>
            <x:String x:Key="PublishProgress.SucceededTitle">✅ Publish Succeeded</x:String>
            <x:String x:Key="PublishProgress.Failed">Publish failed</x:String>
            <x:String x:Key="PublishProgress.FailedTitle">❌ Publish Failed</x:String>
            <x:String x:Key="PublishProgress.Cancelling">Cancelling...</x:String>
            <x:String x:Key="PublishProgress.ConsoleOutput">📋 Console Output</x:String>
            <x:String x:Key="PublishProgress.ClearOutput">🗑 Clear</x:String>
            <x:String x:Key="PublishProgress.CompilationErrors">{0} compilation error(s) — see console output below</x:String>
            <x:String x:Key="PublishProgress.UnknownError">Unknown error</x:String>
            <x:String x:Key="PublishProgress.OutputSize">📊 {0}</x:String>
            <x:String x:Key="PublishProgress.PortableRuntime">Portable</x:String>
            <x:String x:Key="PublishProgress.OpenFolder">📂 Open Folder</x:String>
            <x:String x:Key="PublishProgress.Cancel">⏹ Cancel</x:String>
            <x:String x:Key="PublishProgress.Close">Close</x:String>
        
            <!-- ── 16. ProjectPropertiesWindow ────────────────────────────── -->
            <x:String x:Key="ProjectProps.Title">Project Properties</x:String>
            <x:String x:Key="ProjectProps.General">⚙️  General</x:String>
            <x:String x:Key="ProjectProps.Build">🔨  Build</x:String>
            <x:String x:Key="ProjectProps.Package">📦  Package / NuGet</x:String>
            <x:String x:Key="ProjectProps.Signing">🔏  Signing</x:String>
            <x:String x:Key="ProjectProps.Debug">🐛  Debug</x:String>
            <x:String x:Key="ProjectProps.AssemblyName">Assembly name</x:String>
            <x:String x:Key="ProjectProps.DefaultNamespace">Default namespace</x:String>
            <x:String x:Key="ProjectProps.TargetFramework">Target framework</x:String>
            <x:String x:Key="ProjectProps.OutputType">Output type</x:String>
        
            <x:String x:Key="ProjectProps.LangVersion">C# language version</x:String>
            <x:String x:Key="ProjectProps.Nullable">Nullable reference types</x:String>
            <x:String x:Key="ProjectProps.ImplicitUsings">Enable implicit usings</x:String>
            <x:String x:Key="ProjectProps.AllowUnsafe">Allow unsafe code</x:String>
            <x:String x:Key="ProjectProps.Apply">Apply</x:String>
            <x:String x:Key="ProjectProps.OK">OK</x:String>
            <x:String x:Key="ProjectProps.Cancel">Cancel</x:String>
            <!-- PackagePage fields -->
            <x:String x:Key="ProjectProps.Pkg.Header">PACKAGE / NUGET</x:String>
            <x:String x:Key="ProjectProps.Pkg.GenerateOnBuild">Generate NuGet package on build</x:String>
            <x:String x:Key="ProjectProps.Pkg.PackageId">Package ID</x:String>
            <x:String x:Key="ProjectProps.Pkg.Version">Version</x:String>
            <x:String x:Key="ProjectProps.Pkg.Authors">Authors</x:String>
            <x:String x:Key="ProjectProps.Pkg.Company">Company</x:String>
            <x:String x:Key="ProjectProps.Pkg.Product">Product</x:String>
            <x:String x:Key="ProjectProps.Pkg.Description">Description</x:String>
            <x:String x:Key="ProjectProps.Pkg.LinksHeader">LINKS &amp; METADATA</x:String>
            <x:String x:Key="ProjectProps.Pkg.RepositoryUrl">Repository URL</x:String>
            <x:String x:Key="ProjectProps.Pkg.LicenseExpression">License expression (SPDX)</x:String>
            <x:String x:Key="ProjectProps.Pkg.Tags">Tags (semicolon-separated)</x:String>
            <x:String x:Key="ProjectProps.Pkg.WatermarkAuthors">Author name</x:String>
            <x:String x:Key="ProjectProps.Pkg.WatermarkCompany">My Company</x:String>
            <x:String x:Key="ProjectProps.Pkg.WatermarkProduct">My Product</x:String>
            <x:String x:Key="ProjectProps.Pkg.WatermarkDescription">Short description</x:String>
        
            <!-- ── 17. MsixManagerWindow ─────────────────────────────────── -->
            <x:String x:Key="Msix.Title">MSIX Package Manager</x:String>
            <!-- Nav -->
            <x:String x:Key="Msix.NavActions">ACTIONS</x:String>
            <x:String x:Key="Msix.NavPackage">PACKAGE</x:String>
            <x:String x:Key="Msix.NavOutput">OUTPUT</x:String>
            <x:String x:Key="Msix.NavSigning">SIGNING</x:String>
            <x:String x:Key="Msix.BuildMsix">Build MSIX</x:String>
            <x:String x:Key="Msix.OpenMsix">Open MSIX</x:String>
            <x:String x:Key="Msix.Identity">Identity</x:String>
            <x:String x:Key="Msix.EntryPoint">Entry Point</x:String>
            <x:String x:Key="Msix.ManifestXml">Manifest XML</x:String>
            <x:String x:Key="Msix.BuildLog">Build Log</x:String>
            <x:String x:Key="Msix.SignMsix">Sign MSIX</x:String>
            <!-- Build page -->
            <x:String x:Key="Msix.BuildPageTitle">Build MSIX Package</x:String>
            <x:String x:Key="Msix.BuildPageSubtitle">Publish your .NET project and wrap it in a professional MSIX installer</x:String>
            <x:String x:Key="Msix.BuildProjectHeader">📁  Project</x:String>
            <x:String x:Key="Msix.BuildProjectFile">Project File (.csproj / .fsproj)</x:String>
            <x:String x:Key="Msix.Browse">Browse…</x:String>
            <x:String x:Key="Msix.Configuration">Configuration</x:String>
            <x:String x:Key="Msix.TargetRuntime">Target Runtime</x:String>
            <x:String x:Key="Msix.TargetFramework">Target Framework (auto-detected if blank)</x:String>
            <x:String x:Key="Msix.PackageIdentity">🪪  Package Identity</x:String>
            <x:String x:Key="Msix.PackageIdName">Package Identity Name</x:String>
            <x:String x:Key="Msix.Version">Version (Major.Minor.Build.Rev)</x:String>
            <x:String x:Key="Msix.DisplayName">Display Name</x:String>
            <x:String x:Key="Msix.PublisherDN">Publisher (DN format)</x:String>
            <x:String x:Key="Msix.PublisherDisplayName">Publisher Display Name</x:String>
            <x:String x:Key="Msix.Description">Description</x:String>
            <x:String x:Key="Msix.LogoPath">Logo (relative path inside package)</x:String>
            <x:String x:Key="Msix.EntryPointHeader">▶  Entry Point</x:String>
            <x:String x:Key="Msix.AutoDetect">🔍 Auto-detect</x:String>
            <x:String x:Key="Msix.Executable">Executable (.exe)</x:String>
            <x:String x:Key="Msix.EntryPointClass">Entry Point Class</x:String>
            <x:String x:Key="Msix.EntryPointHint">💡 Use 'Windows.FullTrustApplication' for standard .NET apps (WinForms, WPF, Avalonia, Console).</x:String>
            <x:String x:Key="Msix.PublishOptions">⚙  Publish Options</x:String>
            <x:String x:Key="Msix.SingleFile">Single file</x:String>
            <x:String x:Key="Msix.ReadyToRun">ReadyToRun</x:String>
            <x:String x:Key="Msix.TrimAssemblies">Trim assemblies</x:String>
            <x:String x:Key="Msix.CleanBeforePublish">🗑 Clean before publish</x:String>
            <x:String x:Key="Msix.RunFullTrust">🔓 runFullTrust</x:String>
            <x:String x:Key="Msix.NoPdb">🚫 No PDB (DebugType=none)</x:String>
            <x:String x:Key="Msix.OutputHeader">📂  Output</x:String>
            <x:String x:Key="Msix.MsixOutputPath">MSIX Output Path (.msix)</x:String>
            <x:String x:Key="Msix.IntermediateFolder">Intermediate publish folder (blank = auto-temp)</x:String>
            <!-- Open page -->
            <x:String x:Key="Msix.OpenPageTitle">Open MSIX Package</x:String>
            <x:String x:Key="Msix.OpenPageSubtitle">Load an existing .msix / .appx to inspect or edit its metadata</x:String>
            <x:String x:Key="Msix.PackageFile">📂  Package File</x:String>
            <x:String x:Key="Msix.OpenPackageBtn">📂  Open Package</x:String>
            <x:String x:Key="Msix.LoadedPackage">📦  Loaded Package</x:String>
            <x:String x:Key="Msix.LabelName">Name:</x:String>
            <x:String x:Key="Msix.LabelVersion">Version:</x:String>
            <x:String x:Key="Msix.LabelPublisher">Publisher:</x:String>
            <x:String x:Key="Msix.LabelExecutable">Executable:</x:String>
            <x:String x:Key="Msix.EditIdentity">✏️ Edit Identity</x:String>
            <x:String x:Key="Msix.EditEntryPoint">▶ Edit Entry Point</x:String>
            <x:String x:Key="Msix.ViewManifest">📄 View Manifest</x:String>
            <!-- Identity page -->
            <x:String x:Key="Msix.IdPageTitle">Package Identity</x:String>
            <x:String x:Key="Msix.IdPageSubtitle">Edit AppxManifest.xml identity fields for a loaded MSIX</x:String>
            <x:String x:Key="Msix.PackageName">Package Name</x:String>
            <x:String x:Key="Msix.VersionLabel">Version</x:String>
            <x:String x:Key="Msix.PublisherDNShort">Publisher (DN)</x:String>
            <x:String x:Key="Msix.Architecture">Architecture</x:String>
            <x:String x:Key="Msix.LogoRelPath">Logo (relative path)</x:String>
            <x:String x:Key="Msix.SaveChanges">💾 Save Changes to MSIX</x:String>
            <!-- Entry Point page -->
            <x:String x:Key="Msix.EntryPageTitle">Entry Point</x:String>
            <x:String x:Key="Msix.EntryPageSubtitle">Select the first executable and entry point class for the package</x:String>
            <x:String x:Key="Msix.ExeFoundLabel">Executables found inside package:</x:String>
            <x:String x:Key="Msix.Scan">🔍 Scan</x:String>
            <x:String x:Key="Msix.UseSelectedExe">✅ Use Selected as Entry Executable</x:String>
            <x:String x:Key="Msix.SelectedExe">Selected Executable</x:String>
            <x:String x:Key="Msix.SaveEntryPoint">💾 Save Entry Point to MSIX</x:String>
            <x:String x:Key="Msix.EntryRefTitle">ℹ️  Entry Point Reference</x:String>
            <x:String x:Key="Msix.EntryRefFullTrust">Windows.FullTrustApplication — standard .NET apps (WPF, WinForms, Avalonia, Console).</x:String>
            <x:String x:Key="Msix.EntryRefWinUI">Microsoft.UI.Xaml.Application — WinUI 3 apps packaged with Windows App SDK.</x:String>
            <!-- Manifest page -->
            <x:String x:Key="Msix.ManifestTitle">AppxManifest.xml</x:String>
            <x:String x:Key="Msix.ManifestSubtitle">View and directly edit the raw manifest XML</x:String>
            <x:String x:Key="Msix.SaveManifest">💾 Save Manifest</x:String>
            <x:String x:Key="Msix.Copy">📋 Copy</x:String>
            <!-- Build Log page -->
            <x:String x:Key="Msix.LogTitle">Build Log</x:String>
            <x:String x:Key="Msix.LogSubtitle">Real-time output from dotnet publish and MakeAppx</x:String>
            <x:String x:Key="Msix.ClearLog">🗑 Clear</x:String>
            <x:String x:Key="Msix.OpenOutputFolder">📂 Open Output Folder</x:String>
            <!-- Sign page -->
            <x:String x:Key="Msix.SignPageTitle">Sign MSIX Package</x:String>
            <x:String x:Key="Msix.SignPageSubtitle">Sign your .msix with an X.509 certificate from the current user certificate store (My)</x:String>
            <x:String x:Key="Msix.SignFileHeader">📦  MSIX File</x:String>
            <x:String x:Key="Msix.SignFilePath">Path to .msix file</x:String>
            <x:String x:Key="Msix.CertsHeader">🔐  User Certificates  (CurrentUser\My)</x:String>
            <x:String x:Key="Msix.Refresh">🔄 Refresh</x:String>
            <x:String x:Key="Msix.SelectCert">Select a code-signing certificate:</x:String>
            <x:String x:Key="Msix.CertSubject">Subject:</x:String>
            <x:String x:Key="Msix.CertIssuer">Issuer:</x:String>
            <x:String x:Key="Msix.CertThumbprint">Thumbprint:</x:String>
            <x:String x:Key="Msix.CertValidUntil">Valid until:</x:String>
            <x:String x:Key="Msix.NoCerts">⚠️  No code-signing certificates found in CurrentUser\My store.</x:String>
            <x:String x:Key="Msix.SignOptionsHeader">⚙  Sign Options</x:String>
            <x:String x:Key="Msix.HashAlgorithm">Hash Algorithm</x:String>
            <x:String x:Key="Msix.HashHint">ℹ️  SHA256 is recommended. No timestamp will be added.</x:String>
            <!-- Sign page — Icon -->
            <x:String x:Key="Msix.SignIconHeader">🖼  Package Icon</x:String>
            <x:String x:Key="Msix.SignIconHint">Set or replace the icon inside the MSIX package before signing. MSIX only supports PNG format.</x:String>
            <x:String x:Key="Msix.SignIconFileLabel">Icon file (.png)</x:String>
            <x:String x:Key="Msix.ApplyIcon">📥 Apply Icon</x:String>
            <x:String x:Key="Msix.Preview">Preview:</x:String>
            <!-- Build page — Sign after build -->
            <x:String x:Key="Msix.SignAfterBuildHeader">🔏  Sign After Build</x:String>
            <x:String x:Key="Msix.SignAfterBuild">Sign MSIX package after build</x:String>
            <!-- Footer -->
            <x:String x:Key="Msix.Ready">Ready</x:String>
            <x:String x:Key="Msix.Cancel">⏹ Cancel</x:String>
            <x:String x:Key="Msix.Close">Close</x:String>
            <x:String x:Key="Msix.SignMsixBtn">🔏 Sign MSIX</x:String>
            <x:String x:Key="Msix.StartBuild">🔨 Build MSIX</x:String>
        
            <!-- ── 18. NuGetPanelControl ─────────────────────────────────── -->
            <x:String x:Key="NuGet.Title">NUGET PACKAGES</x:String>
            <x:String x:Key="NuGet.Browse">Browse</x:String>
            <x:String x:Key="NuGet.Installed">Installed</x:String>
            <x:String x:Key="NuGet.Updates">Updates</x:String>
            <x:String x:Key="NuGet.SearchPlaceholder">Search NuGet packages...</x:String>
            <x:String x:Key="NuGet.NoProject">No project loaded</x:String>
            <x:String x:Key="NuGet.NoPackages">No packages installed</x:String>
            <x:String x:Key="NuGet.AllUpToDate">All packages are up to date</x:String>
            <x:String x:Key="NuGet.UpdateAll">Update All</x:String>
        
            <!-- ── 19. AccountPanelControl ───────────────────────────────── -->
            <x:String x:Key="Account.Title">ACCOUNT</x:String>
            <x:String x:Key="Account.SignInTitle">Sign in with GitHub</x:String>
            <x:String x:Key="Account.SignInDesc">Connect your GitHub account to access your repositories.</x:String>
            <x:String x:Key="Account.SignInBtn">Sign in with GitHub</x:String>
            <x:String x:Key="Account.PublicRepos">Public Repos</x:String>
            <x:String x:Key="Account.Followers">Followers</x:String>
            <x:String x:Key="Account.Following">Following</x:String>
            <x:String x:Key="Account.YourRepos">YOUR REPOSITORIES</x:String>
            <x:String x:Key="Account.SearchRepos">Search repositories...</x:String>
            <x:String x:Key="Account.LoadMore">Load more repositories</x:String>
            <x:String x:Key="Account.SignOut">Sign Out</x:String>
            <x:String x:Key="Account.DeviceCodeInstr">Enter this code on GitHub:</x:String>
            <x:String x:Key="Account.DeviceCodeHint">A browser window should have opened. If not, go to github.com/login/device</x:String>
            <x:String x:Key="Account.ClickToSignIn">Click 'Sign in' to open GitHub in your browser.</x:String>
            <x:String x:Key="Account.TokenAlt">Or use Personal Access Token:</x:String>
            <x:String x:Key="Account.LoginWithToken">Login with Token</x:String>
            <x:String x:Key="Account.GetToken">Get Token</x:String>
            <x:String x:Key="Account.UseToken">Use Token Instead</x:String>
            <x:String x:Key="Account.HideToken">Hide Token Input</x:String>
            <x:String x:Key="Account.UseTokenTooltip">Sign in with Personal Access Token</x:String>
            <x:String x:Key="Account.Refresh">Refresh</x:String>
            <x:String x:Key="Account.ViewOnGitHub">View on GitHub</x:String>
            <x:String x:Key="Account.Loading">Loading...</x:String>
        
            <!-- ── 20. GitPanelControl ───────────────────────────────────── -->
            <x:String x:Key="GitPanel.LocalChanges">Local Changes</x:String>
            <x:String x:Key="GitPanel.Log">Log</x:String>
            <x:String x:Key="GitPanel.Console">Console</x:String>
            <x:String x:Key="GitPanel.NoRepo">No Git Repository</x:String>
            <x:String x:Key="GitPanel.NoRepoHint">Open a folder containing a Git repository or initialize a new one.</x:String>
            <x:String x:Key="GitPanel.InitRepo">Initialize Repository</x:String>
            <x:String x:Key="GitPanel.CloneRepo">Clone Repository...</x:String>
            <x:String x:Key="GitPanel.DefaultChangelist">Default Changelist</x:String>
            <x:String x:Key="GitPanel.CommitPlaceholder">Commit message (Ctrl+Enter to commit)</x:String>
        
            <!-- ── 21. Diagnostic / Editor ───────────────────────────────── -->
            <x:String x:Key="Diag.QuickFixes">Quick Fixes:</x:String>
            <x:String x:Key="Diag.LineCol">Line {0}, Col {1}</x:String>
            <x:String x:Key="Diag.ResolvablePackage">Install NuGet Package</x:String>
            <x:String x:Key="Diag.AddUsingSuggestion">Add missing using directive</x:String>
            <x:String x:Key="Editor.RoslynReady">✦ Insait Code Editor · Roslyn ready</x:String>
        
            <!-- LED Panel Designer -->
        
            <!-- nanoFramework Project -->
        
            <!-- ── AxamlPreview extra ─────────────────────────────────────── -->
            <x:String x:Key="AxamlPreview.LivePreview">✔ Live preview</x:String>
            <x:String x:Key="AxamlPreview.FallbackView">⚠ Fallback view</x:String>
            <x:String x:Key="AxamlPreview.TitleFormat">AXAML Preview — {0}</x:String>
            <x:String x:Key="AxamlPreview.UnknownError">Unknown error</x:String>
        
        
            <!-- ── NuGet extra ────────────────────────────────────────────── -->
            <x:String x:Key="NuGet.NoPackagesFound">No packages found</x:String>
            <x:String x:Key="NuGet.Install">Install</x:String>
            <x:String x:Key="NuGet.Update">Update</x:String>
            <x:String x:Key="NuGet.Uninstall">Uninstall</x:String>
            <x:String x:Key="NuGet.AllUpToDateCheck">✅ All packages are up to date</x:String>
            <!-- ── NuGet details & status strings ──────────────────────── -->
            <x:String x:Key="NuGet.PackageDetails">Package Details</x:String>
            <x:String x:Key="NuGet.Version">VERSION</x:String>
            <x:String x:Key="NuGet.Downloads">DOWNLOADS</x:String>
            <x:String x:Key="NuGet.Published">PUBLISHED</x:String>
            <x:String x:Key="NuGet.Description">DESCRIPTION</x:String>
            <x:String x:Key="NuGet.Dependencies">DEPENDENCIES</x:String>
            <x:String x:Key="NuGet.Tags">TAGS</x:String>
            <x:String x:Key="NuGet.Links">LINKS</x:String>
            <x:String x:Key="NuGet.ProjectLink">Project</x:String>
            <x:String x:Key="NuGet.NoDescription">No description available</x:String>
            <x:String x:Key="NuGet.Reinstall">Reinstall</x:String>
            <x:String x:Key="NuGet.Verified">✓ Verified</x:String>
            <x:String x:Key="NuGet.ViewOnNuGetOrg">View on nuget.org</x:String>
            <x:String x:Key="NuGet.SearchAbove">Search for NuGet packages above</x:String>
            <x:String x:Key="NuGet.Project.Label">Project:</x:String>
            <x:String x:Key="NuGet.Tooltip.Refresh">Refresh</x:String>
            <x:String x:Key="NuGet.ByAuthor">by {0}</x:String>
            <x:String x:Key="NuGet.PackageNamePlaceholder">Package Name</x:String>
            <x:String x:Key="NuGet.ByAuthorPlaceholder">by Author</x:String>
            <x:String x:Key="NuGet.NotAvailable">N/A</x:String>
            <x:String x:Key="NuGet.AbsoluteDateFormat">MMM dd, yyyy</x:String>
            <x:String x:Key="NuGet.InstalledVersion">Installed: v{0}</x:String>
            <x:String x:Key="NuGet.InstalledCount">{0} package(s) installed</x:String>
            <x:String x:Key="NuGet.UpdatesAvailable">{0} update(s) available</x:String>
            <x:String x:Key="NuGet.UpdatesTabBadge">Updates ({0})</x:String>
            <x:String x:Key="NuGet.Loading">Loading...</x:String>
            <x:String x:Key="NuGet.Searching">Searching for '{0}'...</x:String>
            <x:String x:Key="NuGet.LoadingDetails">Loading {0} details...</x:String>
            <x:String x:Key="NuGet.LoadingInstalled">Loading installed packages...</x:String>
            <x:String x:Key="NuGet.Installing">Installing {0}...</x:String>
            <x:String x:Key="NuGet.Uninstalling">Uninstalling {0}...</x:String>
            <x:String x:Key="NuGet.Updating">Updating {0}...</x:String>
            <x:String x:Key="NuGet.UpdatingAll">Updating all packages...</x:String>
            <x:String x:Key="NuGet.UpdatingCount">Updating {0} ({1}/{2})...</x:String>
            <x:String x:Key="NuGet.SuccessInstalled">Successfully installed {0}</x:String>
            <x:String x:Key="NuGet.SuccessUninstalled">Successfully uninstalled {0}</x:String>
            <x:String x:Key="NuGet.SuccessUpdated">Successfully updated {0}</x:String>
            <x:String x:Key="NuGet.SuccessUpdatedCount">Successfully updated {0} packages</x:String>
            <x:String x:Key="NuGet.NoProject.Error">No project loaded</x:String>
            <x:String x:Key="NuGet.SearchFailed">Search failed: {0}</x:String>
            <x:String x:Key="NuGet.LoadDetailsFailed">Failed to load package details: {0}</x:String>
            <x:String x:Key="NuGet.OpenUrlFailed">Failed to open URL: {0}</x:String>
            <x:String x:Key="NuGet.LoadPackagesFailed">Failed to load packages: {0}</x:String>
            <x:String x:Key="NuGet.RelativeDate.Today">today</x:String>
            <x:String x:Key="NuGet.RelativeDate.Yesterday">yesterday</x:String>
            <x:String x:Key="NuGet.RelativeDate.DaysAgo">{0}d ago</x:String>
            <x:String x:Key="NuGet.RelativeDate.WeeksAgo">{0}w ago</x:String>
            <x:String x:Key="NuGet.RelativeDate.MonthsAgo">{0}mo ago</x:String>
            <x:String x:Key="NuGet.RelativeDate.YearsAgo">{0}y ago</x:String>
        
            <!-- ── Common ────────────────────────────────────────────────── -->
            <x:String x:Key="Common.OK">OK</x:String>
            <x:String x:Key="Common.Cancel">Cancel</x:String>
            <x:String x:Key="Common.Yes">Yes</x:String>
            <x:String x:Key="Common.No">No</x:String>
            <x:String x:Key="Common.Apply">Apply</x:String>
            <x:String x:Key="Common.Close">Close</x:String>
            <x:String x:Key="Common.Browse">Browse...</x:String>
            <x:String x:Key="Common.Save">Save</x:String>
            <x:String x:Key="Common.Refresh">Refresh</x:String>
        
            <!-- ── Language Switcher ─────────────────────────────────────── -->
            <x:String x:Key="Lang.Language">🌐 Language</x:String>
            <x:String x:Key="Lang.English">English</x:String>
            <x:String x:Key="Lang.Ukrainian">Українська</x:String>
            <x:String x:Key="Lang.German">Deutsch</x:String>
            <x:String x:Key="Lang.Russian">Русский</x:String>
            <x:String x:Key="Lang.Turkish">Türkçe</x:String>
        
            <!-- ── Auto Fix Window ───────────────────────────────────────── -->
            <x:String x:Key="AutoFix.Title">Auto Fix — Roslyn Quick Fixes</x:String>
            <x:String x:Key="AutoFix.TabDiagnostics">Diagnostics &amp; Fixes</x:String>
            <x:String x:Key="AutoFix.TabTemplates">Code Templates</x:String>
            <x:String x:Key="AutoFix.TabKeywords">Keywords</x:String>
            <x:String x:Key="AutoFix.FixAll">Fix All</x:String>
            <x:String x:Key="AutoFix.Refresh">Refresh</x:String>
            <x:String x:Key="AutoFix.Insert">Insert</x:String>
            <x:String x:Key="AutoFix.Close">Close</x:String>
            <x:String x:Key="AutoFix.Ready">Ready</x:String>
            <x:String x:Key="AutoFix.Analyzing">Analyzing…</x:String>
            <x:String x:Key="AutoFix.FoundDiagnostics">Found {0} diagnostic(s) with fixes</x:String>
            <x:String x:Key="AutoFix.NoDiagnostics">No diagnostics with available fixes</x:String>
            <x:String x:Key="AutoFix.Applying">Applying fix…</x:String>
            <x:String x:Key="AutoFix.Applied">Applied: {0}</x:String>
            <x:String x:Key="AutoFix.ApplyingAll">Applying all fixes…</x:String>
            <x:String x:Key="AutoFix.AppliedCount">Applied {0} fix(es)</x:String>
            <x:String x:Key="AutoFix.CannotApply">Cannot apply this fix automatically</x:String>
            <x:String x:Key="AutoFix.Inserted">Inserted: {0}</x:String>
            <x:String x:Key="AutoFix.OpenWindow">Auto Fix</x:String>
        
            <!-- ── AxamlLiveHost (code-behind fallback messages) ────────────── -->
            <x:String x:Key="LiveHost.CannotRender">⚠  Cannot render — showing structure</x:String>
            <x:String x:Key="LiveHost.XmlError">XML error: </x:String>
            <x:String x:Key="LiveHost.FileNotFound">File not found: </x:String>
            <x:String x:Key="LiveHost.EmptyXaml">Empty XAML content.</x:String>
        
            <!-- ── ProjectProps page inner labels ──────────────────────── -->
            <!-- GeneralPage -->
            <x:String x:Key="ProjectProps.General.AppIconHeader">APPLICATION ICON</x:String>
            <x:String x:Key="ProjectProps.General.AppIconFile">Application icon file (.ico)</x:String>
            <x:String x:Key="ProjectProps.General.AppIconHint">The .ico file is embedded as the application icon in the executable.</x:String>
            <x:String x:Key="ProjectProps.General.SelectIconTooltip">Select .ico file</x:String>
            <x:String x:Key="ProjectProps.General.AssemblyHeader">ASSEMBLY</x:String>
            <x:String x:Key="ProjectProps.General.CodeHeader">CODE</x:String>
            <x:String x:Key="ProjectProps.General.AppHeader">APPLICATION</x:String>
            <x:String x:Key="ProjectProps.General.StartupObject">Startup object</x:String>
            <!-- BuildPage -->
            <x:String x:Key="ProjectProps.Build.Header">BUILD CONFIGURATION</x:String>
            <x:String x:Key="ProjectProps.Build.Configuration">Configuration</x:String>
            <x:String x:Key="ProjectProps.Build.PlatformTarget">Platform target</x:String>
            <x:String x:Key="ProjectProps.Build.CompilerHeader">COMPILER OPTIONS</x:String>
            <x:String x:Key="ProjectProps.Build.Optimize">Optimize code</x:String>
            <x:String x:Key="ProjectProps.Build.WarningsAsErrors">Treat warnings as errors</x:String>
            <x:String x:Key="ProjectProps.Build.WarningLevel">Warning level</x:String>
            <x:String x:Key="ProjectProps.Build.SuppressWarnings">Suppress specific warnings (semicolon-separated)</x:String>
            <x:String x:Key="ProjectProps.Build.OutputHeader">OUTPUT</x:String>
            <x:String x:Key="ProjectProps.Build.OutputPath">Output path</x:String>
            <x:String x:Key="ProjectProps.Build.IntermediateOutput">Intermediate output path</x:String>
            <x:String x:Key="ProjectProps.Build.GenerateDocXml">Generate XML documentation file</x:String>
            <!-- DebugPage -->
            <x:String x:Key="ProjectProps.Debug.LaunchHeader">LAUNCH</x:String>
            <x:String x:Key="ProjectProps.Debug.LaunchProfile">Launch profile</x:String>
            <x:String x:Key="ProjectProps.Debug.AppArguments">Application arguments</x:String>
            <x:String x:Key="ProjectProps.Debug.WorkingDir">Working directory</x:String>
            <x:String x:Key="ProjectProps.Debug.WatermarkArgs">Command line arguments</x:String>
            <x:String x:Key="ProjectProps.Debug.WatermarkWorkDir">Project directory</x:String>
            <x:String x:Key="ProjectProps.Debug.EnableNativeDebug">Enable native code debugging</x:String>
            <x:String x:Key="ProjectProps.Debug.EnableSqlDebug">Enable SQL Server debugging</x:String>
            <x:String x:Key="ProjectProps.Debug.EnvVarsHeader">ENVIRONMENT VARIABLES</x:String>
            <x:String x:Key="ProjectProps.Debug.EnvVarsHint">Variables passed to the debug process:</x:String>
            <x:String x:Key="ProjectProps.Debug.EnvVarName">Name</x:String>
            <x:String x:Key="ProjectProps.Debug.EnvVarValue">Value</x:String>
            <x:String x:Key="ProjectProps.Debug.AddEnvVar">Add environment variable</x:String>
            <!-- SigningPage -->
            <x:String x:Key="ProjectProps.Sign.Header">ASSEMBLY SIGNING</x:String>
            <x:String x:Key="ProjectProps.Sign.SignAssembly">Sign the assembly</x:String>
            <x:String x:Key="ProjectProps.Sign.KeyFile">Strong name key file (.snk / .pfx)</x:String>
            <x:String x:Key="ProjectProps.Sign.SelectKey">Select key file</x:String>
            <x:String x:Key="ProjectProps.Sign.DelaySign">Delay sign only</x:String>
            <x:String x:Key="ProjectProps.Sign.AboutTitle">ℹ  About assembly signing</x:String>
            <x:String x:Key="ProjectProps.Sign.AboutText">A strong name key uniquely identifies your assembly and prevents tampering. Use delay signing to sign later during a build pipeline.</x:String>
        
            <!-- ── SolutionPropertiesWindow ──────────────────────────────── -->
            <x:String x:Key="SolProps.Title">Solution Properties</x:String>
            <x:String x:Key="SolProps.NavCategory">SOLUTION</x:String>
            <x:String x:Key="SolProps.NavGeneral">General</x:String>
            <x:String x:Key="SolProps.NavBuildCfg">Build Configurations</x:String>
            <x:String x:Key="SolProps.NavProjects">Projects</x:String>
            <x:String x:Key="SolProps.Close">Close</x:String>
            <x:String x:Key="SolProps.SearchPlaceholder">🔍  Search settings…</x:String>
            <x:String x:Key="SolProps.BuildCfg.Header">SOLUTION CONFIGURATIONS</x:String>
            <x:String x:Key="SolProps.BuildCfg.Hint">Build configurations read from the .sln file (GlobalSection SolutionConfigurationPlatforms).</x:String>
            <x:String x:Key="SolProps.General.IdentityHeader">IDENTITY</x:String>
            <x:String x:Key="SolProps.General.Name">Name</x:String>
            <x:String x:Key="SolProps.General.FullPath">Full Path</x:String>
            <x:String x:Key="SolProps.General.FormatVersion">Format Version</x:String>
            <x:String x:Key="SolProps.General.VsVersion">Visual Studio Version</x:String>
            <x:String x:Key="SolProps.General.VsMinVersion">Minimum Visual Studio Version</x:String>
            <x:String x:Key="SolProps.Projects.Header">PROJECTS IN SOLUTION</x:String>
            <x:String x:Key="SolProps.Projects.Hint">Projects listed in this solution file.</x:String>
        
            <!-- ── SettingsPanelControl ──────────────────────────────────── -->
            <x:String x:Key="Settings.Header">SETTINGS</x:String>
            <x:String x:Key="Settings.ResetTooltip">Reset to defaults</x:String>
            <x:String x:Key="Settings.DotNetSdkTitle">📦 .NET SDK</x:String>
            <x:String x:Key="Settings.DotNetSdkDesc">Path to the .NET SDK installation directory</x:String>
            <x:String x:Key="Settings.GitHubCliTitle">🐙 GitHub CLI (gh)</x:String>
            <x:String x:Key="Settings.GitHubCliDesc">Path to the GitHub CLI executable for terminal &amp; GitHub Copilot integration</x:String>
            <x:String x:Key="Settings.CopilotCliTitle">🤖 Copilot CLI</x:String>
            <x:String x:Key="Settings.CopilotCliDesc">Path to the standalone GitHub Copilot CLI executable</x:String>
            <x:String x:Key="Settings.SignToolTitle">🔏 SignTool</x:String>
            <x:String x:Key="Settings.SignToolDesc">Path to signtool.exe for signing packages (MSIX, ClickOnce, etc.)</x:String>
            <x:String x:Key="Settings.MSBuildTitle">🔨 MSBuild</x:String>
            <x:String x:Key="Settings.MSBuildDesc">Path to MSBuild.exe for building projects outside of dotnet CLI</x:String>
            <x:String x:Key="Settings.AutoDetect">🔍  Auto-detect all paths</x:String>
            <x:String x:Key="Settings.Save">💾  Save Settings</x:String>
        
            <!-- ── GitHub Copilot Control Panel ────────────────────────────── -->
            <x:String x:Key="GitHub.ControlPanel">GitHub Copilot Control Panel</x:String>
            <x:String x:Key="GitHub.LaunchCopilot">Launch GitHub Copilot CLI</x:String>
            <x:String x:Key="GitHub.OpenTranslationsFolder">Open Translations Folder</x:String>
            <x:String x:Key="GitHub.CustomLanguages">🌐 Custom Languages</x:String>
            <x:String x:Key="GitHub.AddCustomLanguage">Add Custom Language from AXAML…</x:String>
            <x:String x:Key="GitHub.SelectAxamlFile">Select AXAML Translation File</x:String>
        
            <!-- ── Roslyn / Code tools windows ──────────────────────────── -->
            <x:String x:Key="GenMember.Title">Generate Member</x:String>
            <x:String x:Key="GenMember.Subtitle">Choose member to generate:</x:String>
            <x:String x:Key="GenMember.Property">Property</x:String>
            <x:String x:Key="GenMember.Method">Method</x:String>
            <x:String x:Key="GenMember.AsyncMethod">Async Method</x:String>
            <x:String x:Key="GenMember.Field">Field</x:String>
            <x:String x:Key="GenMember.Event">Event</x:String>
            <x:String x:Key="GenMember.Footer">Member will be inserted into the target type</x:String>
            <x:String x:Key="GenType.Title">Generate Type</x:String>
            <x:String x:Key="GenType.Prompt">Choose type to create:</x:String>
            <x:String x:Key="GenType.Class">Class</x:String>
            <x:String x:Key="GenType.Struct">Struct</x:String>
            <x:String x:Key="GenType.Interface">Interface</x:String>
            <x:String x:Key="GenType.Enum">Enum</x:String>
            <x:String x:Key="GenType.Record">Record</x:String>
            <x:String x:Key="GenType.Footer">Type will be appended to the current file</x:String>
            <x:String x:Key="GotoDef.Title">📍 Go to Definition</x:String>
            <x:String x:Key="GotoDef.Close">Close</x:String>
            <x:String x:Key="Rename.Title">✏ Rename Symbol</x:String>
            <x:String x:Key="Rename.NewName">New name:</x:String>
            <x:String x:Key="Rename.PreviewChanges">Preview changes</x:String>
            <x:String x:Key="Rename.Cancel">Cancel</x:String>
            <x:String x:Key="Rename.Rename">Rename</x:String>
            <x:String x:Key="Completion.Header">Roslyn IntelliSense</x:String>
            <x:String x:Key="Completion.CloseTooltip">Close (Esc)</x:String>
            <x:String x:Key="Completion.Keys">  Tab/Enter ⏎  Esc ✕</x:String>
            <x:String x:Key="QuickFix.Header">Quick Fix</x:String>
            <x:String x:Key="QuickFix.Footer">Enter ⏎ apply   Esc ✕ dismiss</x:String>
            <x:String x:Key="RoslynTools.Title">Roslyn Tools</x:String>
            <x:String x:Key="RoslynTools.Navigate">Double-click or Enter to navigate</x:String>
        
            <!-- ── StatusBar selection info ─────────────────────────────── -->
            <x:String x:Key="StatusBar.Selection.Chars">{0} chars</x:String>
            <x:String x:Key="StatusBar.Selection.CharsLines">{0} chars, {1} lines</x:String>
        
            <!-- ── Terminal ──────────────────────────────────────────────── -->
            <x:String x:Key="Terminal.Welcome.Version">Insait Terminal [Version 1.0.0]</x:String>
            <x:String x:Key="Terminal.Welcome.Hint">(c) Insait Edit. Type 'help' for commands.</x:String>
            <x:String x:Key="Terminal.Msg.SwitchedToPS">Switched to PowerShell mode.</x:String>
            <x:String x:Key="Terminal.Msg.SwitchedToCmd">Switched to CMD mode.</x:String>
            <x:String x:Key="Terminal.Msg.AdminStarted">Administrator shell started in new window.</x:String>
            <x:String x:Key="Terminal.Msg.ProcessTerminated">^C Process terminated.</x:String>
            <x:String x:Key="Terminal.Msg.PathNotFound">The system cannot find the path specified.</x:String>
            <x:String x:Key="Terminal.Msg.Cancelled">Cancelled.</x:String>
            <x:String x:Key="Terminal.Msg.InvalidNumber">❌ Invalid input. Please enter a number.</x:String>
            <x:String x:Key="Terminal.Dotnet.Searching">🔍 Searching for .NET projects in: {0}</x:String>
            <x:String x:Key="Terminal.Dotnet.NoneFound">❌ No .csproj / .fsproj files found in: {0}</x:String>
            <x:String x:Key="Terminal.Dotnet.NoneFoundHint">   Use 'cd &lt;directory&gt;' to navigate to a project folder.</x:String>
            <x:String x:Key="Terminal.Dotnet.Project">📦 Project: {0}</x:String>
            <x:String x:Key="Terminal.Dotnet.Running">⚙️  Running: {0}</x:String>
            <x:String x:Key="Terminal.Dotnet.MultipleFound">🔍 Found {0} projects. Choose which to use:</x:String>
            <x:String x:Key="Terminal.Dotnet.EnterNumber">Enter number (1–{0}): </x:String>
            <x:String x:Key="Terminal.Dotnet.InvalidSelection">❌ Invalid selection. Enter 1–{0} or 0 to cancel.</x:String>
            <x:String x:Key="Terminal.Dotnet.Selected">📦 Selected: {0}</x:String>
            <x:String x:Key="Terminal.Help.Title">INSAIT TERMINAL v1.0 HELP</x:String>
            <x:String x:Key="Terminal.Help.BuiltinSection">Built-in Commands:</x:String>
            <x:String x:Key="Terminal.Help.DotnetSection">.NET Commands (auto project discovery):</x:String>
            <x:String x:Key="Terminal.Help.ShortcutsSection">Keyboard Shortcuts:</x:String>
            <x:String x:Key="Terminal.Help.DotnetTip">💡 dotnet run/build/publish/test auto-find *.csproj / *.fsproj</x:String>
            <x:String x:Key="Terminal.Help.DotnetTip2">   If multiple projects found — you choose which one.</x:String>
        
            <!-- ── InsaitTerminalPanel — Welcome Banner ───────────────── -->
            <x:String x:Key="InsaitTerminal.Welcome.BannerTitle">✦  Insait Edit — Integrated Terminal  ✦</x:String>
            <x:String x:Key="InsaitTerminal.Welcome.HelpHint">Type 'insait help' to see available commands &amp; shortcuts.</x:String>
            <x:String x:Key="InsaitTerminal.Welcome.Ready">Ready for dotnet, git, cmd and any CLI tools.</x:String>
        
            <!-- ── InsaitTerminalPanel — Help Banner ───────────────────── -->
            <x:String x:Key="InsaitTerminal.Help.Title">Insait Terminal — Help</x:String>
            <x:String x:Key="InsaitTerminal.Help.BuiltinCommands">BUILT-IN COMMANDS:</x:String>
            <x:String x:Key="InsaitTerminal.Help.Cmd.Help">Show this help message</x:String>
            <x:String x:Key="InsaitTerminal.Help.Cmd.Clear">Clear terminal output</x:String>
            <x:String x:Key="InsaitTerminal.Help.Cmd.Restart">Restart the shell session</x:String>
            <x:String x:Key="InsaitTerminal.Help.Cmd.Version">Show Insait Edit version</x:String>
            <x:String x:Key="InsaitTerminal.Help.Cmd.Welcome">Show welcome banner</x:String>
            <x:String x:Key="InsaitTerminal.Help.KeyboardShortcuts">KEYBOARD SHORTCUTS:</x:String>
            <x:String x:Key="InsaitTerminal.Help.Key.Enter">Execute command</x:String>
            <x:String x:Key="InsaitTerminal.Help.Key.UpDown">Navigate command history</x:String>
            <x:String x:Key="InsaitTerminal.Help.Key.CtrlC">Stop running process</x:String>
            <x:String x:Key="InsaitTerminal.Help.Key.CtrlL">Clear terminal</x:String>
            <x:String x:Key="InsaitTerminal.Help.DotnetDiscovery">DOTNET CLI — AUTO PROJECT DISCOVERY:</x:String>
            <x:String x:Key="InsaitTerminal.Help.DotnetDiscovery.AutoFind">→ auto-finds *.csproj / *.fsproj in working dir</x:String>
            <x:String x:Key="InsaitTerminal.Help.DotnetDiscovery.Single">→ 1 project: runs immediately</x:String>
            <x:String x:Key="InsaitTerminal.Help.DotnetDiscovery.Multiple">→ 2+ projects: shows numbered menu to choose</x:String>
            <x:String x:Key="InsaitTerminal.Help.DotnetCommands">DOTNET CLI COMMANDS:</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.New">Create a new project</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Build">Build the project</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.BuildRelease">Build in Release mode</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Run">Run the project</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Test">Run unit tests</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Publish">Publish the application</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Clean">Clean build output</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Restore">Restore NuGet packages</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Watch">Watch mode (hot-reload)</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Info">Show .NET SDK information</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.Ef">Entity Framework CLI</x:String>
            <x:String x:Key="InsaitTerminal.Help.NuGetManagement">NUGET PACKAGE MANAGEMENT — AUTO PROJECT SELECTION:</x:String>
            <x:String x:Key="InsaitTerminal.Help.NuGet.AutoSelect">→ auto-selects project if only one found</x:String>
            <x:String x:Key="InsaitTerminal.Help.NuGet.Menu">→ shows menu if multiple projects in workspace</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.ListPackage">List installed packages</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.RemovePackage">Remove a NuGet package</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.NewList">List available templates</x:String>
            <x:String x:Key="InsaitTerminal.Help.Dotnet.ClearCache">Clear NuGet cache</x:String>
            <x:String x:Key="InsaitTerminal.Help.GitCommands">GIT COMMANDS:</x:String>
            <x:String x:Key="InsaitTerminal.Help.Git.Status">Show working tree status</x:String>
            <x:String x:Key="InsaitTerminal.Help.Git.Add">Stage all changes</x:String>
            <x:String x:Key="InsaitTerminal.Help.Git.Commit">Commit staged changes</x:String>
            <x:String x:Key="InsaitTerminal.Help.Git.Push">Push to remote</x:String>
            <x:String x:Key="InsaitTerminal.Help.Git.Pull">Pull from remote</x:String>
            <x:String x:Key="InsaitTerminal.Help.Git.Log">Show recent commits</x:String>
        </ResourceDictionary>
        """;
}
