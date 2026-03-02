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

    public static AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            LoadLanguageDictionary(value);
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
    /// Load the AXAML resource dictionary for the given language into Application.Current.Resources.
    /// </summary>
    private static void LoadLanguageDictionary(AppLanguage language)
    {
        var app = Application.Current;
        if (app == null) return;

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
