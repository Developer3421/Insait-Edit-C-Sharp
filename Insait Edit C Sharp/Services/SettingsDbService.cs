using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteDB;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Persists application settings (e.g. language preference) in an encrypted LiteDB database.
/// The encryption key is derived from a user-level secret stored in the Windows Data Protection API
/// (DPAPI, <see cref="ProtectedData"/> with <see cref="DataProtectionScope.CurrentUser"/>),
/// so the database is bound to the OS user account that created it.
/// If the database is missing, corrupted, or the key cannot be decrypted the service falls back
/// to safe defaults (English language) without throwing.
/// </summary>
public static class SettingsDbService
{
    // ---------- constants ----------

    private const string DbFileName  = "insait_settings.db";
    private const string KeyFileName = "insait_settings.key";
    private const string Collection  = "settings";
    private const string LangKey     = "language";
    private const string GeminiApiKeyKey = "gemini_api_key";

    // ---------- private state ----------

    private static readonly string _appDataDir;
    private static readonly string _dbPath;
    private static readonly string _keyPath;

    // ---------- constructor ----------

    static SettingsDbService()
    {
        _appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InsaitEdit");

        Directory.CreateDirectory(_appDataDir);

        _dbPath  = Path.Combine(_appDataDir, DbFileName);
        _keyPath = Path.Combine(_appDataDir, KeyFileName);
    }

    // ---------- public API ----------

    /// <summary>
    /// Returns the saved language, or <see langword="null"/> when not set / error.
    /// </summary>
    public static LocalizationService.AppLanguage? LoadLanguage()
    {
        try
        {
            var password = GetOrCreatePassword();
            if (password == null) return null;

            using var db = OpenDb(password);
            var col = db.GetCollection<SettingEntry>(Collection);
            var entry = col.FindOne(x => x.Key == LangKey);
            if (entry == null) return null;

            return Enum.TryParse<LocalizationService.AppLanguage>(entry.Value, out var lang)
                ? lang
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsDb] LoadLanguage failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves the language preference to the encrypted database.
    /// </summary>
    public static void SaveLanguage(LocalizationService.AppLanguage language)
    {
        try
        {
            var password = GetOrCreatePassword();
            if (password == null) return;

            using var db = OpenDb(password);
            var col = db.GetCollection<SettingEntry>(Collection);

            var existing = col.FindOne(x => x.Key == LangKey);
            if (existing != null)
            {
                existing.Value = language.ToString();
                col.Update(existing);
            }
            else
            {
                col.Insert(new SettingEntry { Key = LangKey, Value = language.ToString() });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsDb] SaveLanguage failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the saved Gemini API key, or <see langword="null"/> when not set / error.
    /// </summary>
    public static string? LoadGeminiApiKey()
    {
        try
        {
            var password = GetOrCreatePassword();
            if (password == null) return null;

            using var db = OpenDb(password);
            var col = db.GetCollection<SettingEntry>(Collection);
            var entry = col.FindOne(x => x.Key == GeminiApiKeyKey);
            return entry?.Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsDb] LoadGeminiApiKey failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves the Gemini API key to the encrypted database.
    /// </summary>
    public static void SaveGeminiApiKey(string apiKey)
    {
        try
        {
            var password = GetOrCreatePassword();
            if (password == null) return;

            using var db = OpenDb(password);
            var col = db.GetCollection<SettingEntry>(Collection);

            var existing = col.FindOne(x => x.Key == GeminiApiKeyKey);
            if (existing != null)
            {
                existing.Value = apiKey;
                col.Update(existing);
            }
            else
            {
                col.Insert(new SettingEntry { Key = GeminiApiKeyKey, Value = apiKey });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsDb] SaveGeminiApiKey failed: {ex.Message}");
        }
    }

    /// <summary>Exposes the AppData directory path so other services can create sibling DBs.</summary>
    public static string AppDataDir => _appDataDir;

    // ---------- helpers ----------

    /// <summary>
    /// Opens (or creates) the LiteDB database protected with the given password.
    /// </summary>
    private static LiteDatabase OpenDb(string password)
    {
        var connStr = new ConnectionString
        {
            Filename = _dbPath,
            Password = password,
            Connection = ConnectionType.Direct
        };
        return new LiteDatabase(connStr);
    }

    /// <summary>
    /// Returns the AES password used to encrypt the LiteDB file.
    /// On first run a random 32-byte key is generated, encrypted with DPAPI
    /// (CurrentUser scope) and saved to disk.
    /// On subsequent runs the encrypted key is read from disk and decrypted with DPAPI.
    /// Returns <see langword="null"/> on any error so the caller can fall back gracefully.
    /// </summary>
    private static string? GetOrCreatePassword()
    {
        try
        {
            byte[] rawKey;

            if (File.Exists(_keyPath))
            {
                // Read and decrypt the stored key blob
                var encryptedKey = File.ReadAllBytes(_keyPath);
                rawKey = ProtectedData.Unprotect(
                    encryptedKey,
                    optionalEntropy: Encoding.UTF8.GetBytes("InsaitEditSettings"),
                    scope: DataProtectionScope.CurrentUser);
            }
            else
            {
                // Generate a new random 32-byte key
                rawKey = RandomNumberGenerator.GetBytes(32);

                // Encrypt with DPAPI and persist
                var encryptedKey = ProtectedData.Protect(
                    rawKey,
                    optionalEntropy: Encoding.UTF8.GetBytes("InsaitEditSettings"),
                    scope: DataProtectionScope.CurrentUser);

                File.WriteAllBytes(_keyPath, encryptedKey);
            }

            // Convert raw bytes to a hex string – LiteDB uses it as the AES password
            return Convert.ToHexString(rawKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsDb] GetOrCreatePassword failed: {ex.Message}");
            return null;
        }
    }

    // ---------- data model ----------

    private class SettingEntry
    {
        public int    Id    { get; set; }
        public string Key   { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}

