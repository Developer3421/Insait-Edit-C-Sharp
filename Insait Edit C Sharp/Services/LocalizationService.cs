using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Localization service supporting English, Ukrainian, German, Russian and Turkish.
/// Reads translations from AXAML ResourceDictionary files in "Interface Localization/" folder.
/// </summary>
public static class LocalizationService
{
    public enum AppLanguage { English, Ukrainian, German, Russian, Turkish }

    private static AppLanguage _currentLanguage = AppLanguage.English;
    private static ResourceInclude? _currentDictionary;

    private const string CustomLangSettingKey = "custom_language";

    public static AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value && GitHubCopilotService.LoadedLanguageName == null) return;
            _currentLanguage = value;
            LoadLanguageDictionary(value);
            // User explicitly chose a standard language — clear the custom language choice
            SaveCustomLanguageName(null);
            // Persist the chosen language so it is restored on next launch
            SettingsDbService.SaveLanguage(value);
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Fires <see cref="LanguageChanged"/> from outside the class.
    /// Call this after injecting custom translation keys into Application.Current.Resources.
    /// </summary>
    public static void NotifyLanguageChanged()
        => LanguageChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// Initialize localization by loading the saved language from the encrypted database.
    /// Falls back to English if the database is missing, corrupted, or returns no value.
    /// Call this once from App.OnFrameworkInitializationCompleted or similar.
    /// </summary>
    public static void Initialize()
    {
        // Try to restore previously saved language; fall back to English on any error
        try
        {
            var saved = SettingsDbService.LoadLanguage();
            if (saved.HasValue)
                _currentLanguage = saved.Value;
            else
                _currentLanguage = AppLanguage.English;
        }
        catch
        {
            _currentLanguage = AppLanguage.English;
        }

        LoadLanguageDictionary(_currentLanguage);

        // Try to restore the last selected custom language (if any)
        try
        {
            var savedCustom = SettingsDbService.LoadSetting(CustomLangSettingKey);
            if (!string.IsNullOrEmpty(savedCustom))
            {
                if (GitHubCopilotService.DictionaryExists(savedCustom))
                {
                    GitHubCopilotService.LoadCustomDictionary(savedCustom);
                    // Persist the choice again so SaveCustomLanguage stays in sync
                }
                else
                {
                    // File was deleted — clear the setting and stay on the standard language
                    SettingsDbService.SaveSetting(CustomLangSettingKey, "");
                    System.Diagnostics.Debug.WriteLine(
                        $"[Localization] Custom language '{savedCustom}' not found, falling back to {_currentLanguage}");
                }
            }
        }
        catch
        {
            // Non-fatal — just stay on the standard language
        }
    }

    /// <summary>
    /// Get a localized string by key from the currently loaded AXAML resource dictionary.
    /// Falls back to the key itself if not found.
    /// </summary>
    public static string Get(string key)
    {
        var app = Application.Current;
        if (app != null && app.Resources.TryGetResource(key, null, out var val) && val is string s)
        {
            return s;
        }
        return key;
    }

    /// <summary>
    /// Persists the name of the active custom language so it can be restored on next launch.
    /// Pass <c>null</c> or empty string to clear.
    /// </summary>
    public static void SaveCustomLanguageName(string? name)
    {
        try
        {
            SettingsDbService.SaveSetting(CustomLangSettingKey, name ?? "");
        }
        catch
        {
            // Non-fatal
        }
    }

    /// <summary>
    /// Load the AXAML resource dictionary for the given language into Application.Current.Resources.
    /// </summary>
    private static void LoadLanguageDictionary(AppLanguage language)
    {
        var app = Application.Current;
        if (app == null) return;

        // Clear any custom (user-created) dictionary overrides so they don't
        // shadow the standard language being loaded.
        GitHubCopilotService.UnloadCustomDictionary();


        var fileName = language switch
        {
            AppLanguage.Ukrainian => "Ukrainian",
            AppLanguage.German    => "German",
            AppLanguage.Russian   => "Russian",
            AppLanguage.Turkish   => "Turkish",
            _                     => "English",
        };

        try
        {
            // Remove previously loaded localization dictionary
            if (_currentDictionary != null)
            {
                app.Resources.MergedDictionaries.Remove(_currentDictionary);
                _currentDictionary = null;
            }

            // Create and add the new resource dictionary
            var uri = new Uri($"avares://Insait%20Edit%20C%20Sharp/Interface Localization/{fileName}.axaml");
            var newDict = new ResourceInclude(uri) { Source = uri };
            app.Resources.MergedDictionaries.Add(newDict);
            _currentDictionary = newDict;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Localization] Failed to load {fileName}.axaml: {ex.Message}");
        }
    }
}
