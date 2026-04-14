using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

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
                throw new FileNotFoundException(
                    "English.axaml not found via asset loader or disk fallback.");
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
    /// </summary>
    public static void LaunchCopilotCli()
    {
        try
        {
            Directory.CreateDirectory(_translationsDir);

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
            Debug.WriteLine($"[GitHubCopilot] LaunchCopilotCli failed: {ex.Message}");
        }
    }

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
}
