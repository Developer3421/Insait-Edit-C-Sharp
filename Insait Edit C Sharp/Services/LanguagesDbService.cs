using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LiteDB;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Persists the list of custom (user-defined) languages in a separate encrypted LiteDB.
/// Each entry stores a display name, a Gemini model string, and the AppData path
/// to the custom AXAML translation dictionary.
/// This database is intentionally separate from insait_settings.db so that
/// language list management is independent from general settings.
/// </summary>
public static class LanguagesDbService
{
    // ---------- constants ----------
    private const string DbFileName  = "insait_languages.db";
    private const string KeyFileName = "insait_languages.key";
    private const string Collection  = "languages";

    // ---------- private state ----------
    private static readonly string _dbPath;
    private static readonly string _keyPath;

    static LanguagesDbService()
    {
        var dir = SettingsDbService.AppDataDir;
        _dbPath  = Path.Combine(dir, DbFileName);
        _keyPath = Path.Combine(dir, KeyFileName);
    }

    // ---------- public API ----------

    /// <summary>Returns all saved custom languages.</summary>
    public static List<CustomLanguageEntry> LoadAll()
    {
        try
        {
            var pw = GetOrCreatePassword();
            if (pw == null) return new();
            using var db = OpenDb(pw);
            return db.GetCollection<CustomLanguageEntry>(Collection).FindAll().ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LanguagesDb] LoadAll failed: {ex.Message}");
            return new();
        }
    }

    /// <summary>Saves (insert or update) a custom language entry.</summary>
    public static void Save(CustomLanguageEntry entry)
    {
        try
        {
            var pw = GetOrCreatePassword();
            if (pw == null) return;
            using var db = OpenDb(pw);
            var col = db.GetCollection<CustomLanguageEntry>(Collection);
            var existing = col.FindOne(x => x.LanguageName == entry.LanguageName);
            if (existing != null)
            {
                existing.GeminiModel = entry.GeminiModel;
                existing.DictionaryPath = entry.DictionaryPath;
                col.Update(existing);
            }
            else
            {
                col.Insert(entry);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LanguagesDb] Save failed: {ex.Message}");
        }
    }

    /// <summary>Removes a custom language entry by display name.</summary>
    public static void Delete(string languageName)
    {
        try
        {
            var pw = GetOrCreatePassword();
            if (pw == null) return;
            using var db = OpenDb(pw);
            var col = db.GetCollection<CustomLanguageEntry>(Collection);
            col.DeleteMany(x => x.LanguageName == languageName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LanguagesDb] Delete failed: {ex.Message}");
        }
    }

    // ---------- helpers ----------

    private static LiteDatabase OpenDb(string password) =>
        new LiteDatabase(new ConnectionString
        {
            Filename = _dbPath,
            Password = password,
            Connection = ConnectionType.Direct
        });

    private static string? GetOrCreatePassword()
    {
        try
        {
            byte[] rawKey;
            if (File.Exists(_keyPath))
            {
                var enc = File.ReadAllBytes(_keyPath);
                rawKey = ProtectedData.Unprotect(enc,
                    Encoding.UTF8.GetBytes("InsaitEditLanguages"),
                    DataProtectionScope.CurrentUser);
            }
            else
            {
                rawKey = RandomNumberGenerator.GetBytes(32);
                var enc = ProtectedData.Protect(rawKey,
                    Encoding.UTF8.GetBytes("InsaitEditLanguages"),
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_keyPath, enc);
            }
            return Convert.ToHexString(rawKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LanguagesDb] GetOrCreatePassword failed: {ex.Message}");
            return null;
        }
    }
}

/// <summary>A single custom language entry stored in the languages database.</summary>
public class CustomLanguageEntry
{
    public int    Id             { get; set; }
    /// <summary>User-supplied display name, e.g. "French" or "日本語".</summary>
    public string LanguageName   { get; set; } = string.Empty;
    /// <summary>Gemini model ID chosen by the user, e.g. "gemini-1.5-pro" or "gemini-2.0-flash".</summary>
    public string GeminiModel    { get; set; } = string.Empty;
    /// <summary>Full path to the AXAML resource dictionary in AppData.</summary>
    public string DictionaryPath { get; set; } = string.Empty;
}

