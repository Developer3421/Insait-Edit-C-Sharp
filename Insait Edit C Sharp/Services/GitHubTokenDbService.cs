using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteDB;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Persists the GitHub access token in a dedicated encrypted LiteDB database.
/// The database password is protected with DPAPI using <see cref="DataProtectionScope.CurrentUser"/>,
/// so the database can only be opened by the same Windows user account.
/// Also supports one-time migration from the legacy github_token.dat file.
/// </summary>
public static class GitHubTokenDbService
{
    private const string DbFileName = "insait_github_token.db";
    private const string KeyFileName = "insait_github_token.key";
    private const string Collection = "tokens";
    private const string AccessTokenKey = "github_access_token";
    private const string DpapiEntropy = "InsaitEditGitHubToken";
    private const string LegacyDirectoryName = "InsaitEditor";
    private const string LegacyFileName = "github_token.dat";

    private static readonly string _dbPath;
    private static readonly string _keyPath;
    private static readonly string _legacyTokenFilePath;

    static GitHubTokenDbService()
    {
        var appDataDir = SettingsDbService.AppDataDir;
        Directory.CreateDirectory(appDataDir);

        _dbPath = Path.Combine(appDataDir, DbFileName);
        _keyPath = Path.Combine(appDataDir, KeyFileName);

        var legacyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LegacyDirectoryName);
        _legacyTokenFilePath = Path.Combine(legacyDir, LegacyFileName);
    }

    /// <summary>
    /// Loads the stored GitHub access token from the encrypted database.
    /// If nothing is stored yet, attempts a one-time migration from the legacy token file.
    /// </summary>
    public static string? LoadAccessToken()
    {
        try
        {
            var password = GetOrCreatePassword();
            if (password != null)
            {
                using var db = OpenDb(password);
                var col = db.GetCollection<TokenEntry>(Collection);
                var entry = col.FindOne(x => x.Key == AccessTokenKey);
                if (!string.IsNullOrWhiteSpace(entry?.Value))
                    return entry.Value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitHubTokenDb] LoadAccessToken failed: {ex.Message}");
        }

        return TryMigrateLegacyToken();
    }

    /// <summary>
    /// Saves the GitHub access token to the encrypted database.
    /// </summary>
    public static void SaveAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ClearAccessToken();
            return;
        }

        try
        {
            var password = GetOrCreatePassword();
            if (password == null) return;

            using var db = OpenDb(password);
            var col = db.GetCollection<TokenEntry>(Collection);
            var existing = col.FindOne(x => x.Key == AccessTokenKey);
            if (existing != null)
            {
                existing.Value = token;
                col.Update(existing);
            }
            else
            {
                col.Insert(new TokenEntry { Key = AccessTokenKey, Value = token });
            }

            DeleteLegacyTokenFile();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitHubTokenDb] SaveAccessToken failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the saved GitHub access token from the database and legacy storage.
    /// </summary>
    public static void ClearAccessToken()
    {
        try
        {
            var password = GetOrCreatePassword();
            if (password != null)
            {
                using var db = OpenDb(password);
                var col = db.GetCollection<TokenEntry>(Collection);
                col.DeleteMany(x => x.Key == AccessTokenKey);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitHubTokenDb] ClearAccessToken failed: {ex.Message}");
        }

        DeleteLegacyTokenFile();
    }

    private static string? TryMigrateLegacyToken()
    {
        try
        {
            if (!File.Exists(_legacyTokenFilePath))
                return null;

            var encodedToken = File.ReadAllText(_legacyTokenFilePath).Trim();
            if (string.IsNullOrWhiteSpace(encodedToken))
                return null;

            var token = Encoding.UTF8.GetString(Convert.FromBase64String(encodedToken));
            if (string.IsNullOrWhiteSpace(token))
                return null;

            SaveAccessToken(token);
            return token;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitHubTokenDb] Legacy token migration failed: {ex.Message}");
            return null;
        }
    }

    private static void DeleteLegacyTokenFile()
    {
        try
        {
            if (File.Exists(_legacyTokenFilePath))
                File.Delete(_legacyTokenFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitHubTokenDb] DeleteLegacyTokenFile failed: {ex.Message}");
        }
    }

    private static LiteDatabase OpenDb(string password) =>
        new(new ConnectionString
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
                var encryptedKey = File.ReadAllBytes(_keyPath);
                rawKey = ProtectedData.Unprotect(
                    encryptedKey,
                    Encoding.UTF8.GetBytes(DpapiEntropy),
                    DataProtectionScope.CurrentUser);
            }
            else
            {
                rawKey = RandomNumberGenerator.GetBytes(32);
                var encryptedKey = ProtectedData.Protect(
                    rawKey,
                    Encoding.UTF8.GetBytes(DpapiEntropy),
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_keyPath, encryptedKey);
            }

            return Convert.ToHexString(rawKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitHubTokenDb] GetOrCreatePassword failed: {ex.Message}");
            return null;
        }
    }

    private sealed class TokenEntry
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}

