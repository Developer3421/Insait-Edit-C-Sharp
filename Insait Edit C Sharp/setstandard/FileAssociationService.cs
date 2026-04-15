using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Insait_Edit_C_Sharp.SetStandard;

/// <summary>
/// Service that registers / unregisters Insait Edit as the default
/// Windows application for the file types listed in
/// <see cref="SupportedFileTypes"/>.
///
/// All keys are written under <c>HKEY_CURRENT_USER</c> so no
/// administrator privileges are required.
///
/// Typical usage:
/// <code>
///   var svc = new FileAssociationService();
///   // Register all supported extensions
///   svc.RegisterAll();
///   // …or register a specific set
///   svc.Register(".cs", ".csproj", ".sln");
///   // Remove everything
///   svc.UnregisterAll();
/// </code>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileAssociationService
{
    // ── Registry base paths (HKCU) ─────────────────────────────
    private const string ClassesRoot        = @"Software\Classes";
    private const string RegisteredAppsKey  = @"Software\RegisteredApplications";
    private const string AppCapabilitiesKey = @"Software\" + CapabilitiesRel;
    private const string CapabilitiesRel    = @"InsaitEdit\Capabilities";

    private readonly string _exePath;
    private readonly string _iconPath;

    public FileAssociationService()
    {
        _exePath  = DefaultProgramHelper.GetExePath();
        _iconPath = $"\"{_exePath}\",0";
    }

    // ════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════

    /// <summary>Register ALL supported file types.</summary>
    public void RegisterAll()
        => Register(SupportedFileTypes.AllExtensions());

    /// <summary>Unregister ALL supported file types.</summary>
    public void UnregisterAll()
        => Unregister(SupportedFileTypes.AllExtensions());

    /// <summary>Register specific extensions (e.g. ".cs", ".json").</summary>
    public void Register(params string[] extensions)
    {
        EnsureApplicationCapabilities();

        foreach (var ext in extensions)
        {
            var info = SupportedFileTypes.All.FirstOrDefault(f =>
                f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase));
            if (info == null) continue;

            RegisterProgId(info);
            RegisterExtension(info);
            RegisterCapabilityExtension(info);
        }

        DefaultProgramHelper.NotifyShellAssociationsChanged();
    }

    /// <summary>Unregister specific extensions.</summary>
    public void Unregister(params string[] extensions)
    {
        foreach (var ext in extensions)
        {
            var progId = SupportedFileTypes.GetProgId(ext);
            UnregisterProgId(progId);
            UnregisterExtension(ext, progId);
            UnregisterCapabilityExtension(ext);
        }

        CleanupIfEmpty();
        DefaultProgramHelper.NotifyShellAssociationsChanged();
    }

    /// <summary>
    /// Returns the subset of <paramref name="extensions"/> that are
    /// currently registered to Insait Edit.
    /// </summary>
    public IReadOnlyList<string> GetRegistered(params string[] extensions)
    {
        var result = new List<string>();
        foreach (var ext in extensions)
        {
            var progId = SupportedFileTypes.GetProgId(ext);
            using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}");
            if (key != null)
            {
                var val = key.GetValue(null) as string;
                if (string.Equals(val, progId, StringComparison.OrdinalIgnoreCase))
                    result.Add(ext);
            }
        }
        return result;
    }

    /// <summary>Check whether a single extension is registered to Insait Edit.</summary>
    public bool IsRegistered(string extension)
        => GetRegistered(extension).Count > 0;

    /// <summary>
    /// Returns all supported extensions grouped by <see cref="FileCategory"/>
    /// together with a boolean showing whether each is currently registered.
    /// Useful for building a checkbox UI.
    /// </summary>
    public IReadOnlyList<FileTypeRegistrationStatus> GetAllStatuses()
    {
        return SupportedFileTypes.All
            .Select(f => new FileTypeRegistrationStatus(
                f.Extension,
                f.Description,
                f.Category,
                IsRegistered(f.Extension)))
            .ToList();
    }

    // ════════════════════════════════════════════════════════════
    //  ProgId registration   (HKCU\Software\Classes\InsaitEdit.xxx)
    // ════════════════════════════════════════════════════════════

    private void RegisterProgId(FileTypeInfo info)
    {
        var progId = SupportedFileTypes.GetProgId(info.Extension);
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}");
        if (key == null) return;

        key.SetValue(null, info.Description);
        key.SetValue("FriendlyTypeName", $"{SupportedFileTypes.AppName} — {info.Description}");

        // Default icon (use exe icon index 0)
        using var iconKey = key.CreateSubKey("DefaultIcon");
        iconKey?.SetValue(null, _iconPath);

        // Shell\Open\Command
        using var shellKey  = key.CreateSubKey(@"shell\open\command");
        shellKey?.SetValue(null, $"\"{_exePath}\" \"%1\"");

        // Content type
        key.SetValue("Content Type", info.ContentType);

        Debug.WriteLine($"[FileAssoc] Registered ProgId: {progId}");
    }

    private void UnregisterProgId(string progId)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{progId}", throwOnMissingSubKey: false);
            Debug.WriteLine($"[FileAssoc] Removed ProgId: {progId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileAssoc] Failed to remove ProgId {progId}: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Extension registration  (HKCU\Software\Classes\.ext)
    // ════════════════════════════════════════════════════════════

    private void RegisterExtension(FileTypeInfo info)
    {
        var progId = SupportedFileTypes.GetProgId(info.Extension);
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{info.Extension}");
        if (key == null) return;

        // Save the previous default so we can restore it later
        var previous = key.GetValue(null) as string;
        if (!string.IsNullOrEmpty(previous) &&
            !previous.Equals(progId, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue("InsaitEdit_Backup", previous);
        }

        key.SetValue(null, progId);
        key.SetValue("Content Type", info.ContentType);

        // OpenWithProgIds — adds Insait Edit as an option in "Open With"
        using var owKey = key.CreateSubKey("OpenWithProgids");
        owKey?.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);

        Debug.WriteLine($"[FileAssoc] Registered extension: {info.Extension} → {progId}");
    }

    private void UnregisterExtension(string ext, string progId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}", writable: true);
            if (key == null) return;

            // Restore previous default if we backed it up
            var backup = key.GetValue("InsaitEdit_Backup") as string;
            var current = key.GetValue(null) as string;

            if (string.Equals(current, progId, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(backup))
                {
                    key.SetValue(null, backup);
                    key.DeleteValue("InsaitEdit_Backup", throwOnMissingValue: false);
                }
                else
                {
                    key.SetValue(null, string.Empty);
                }
            }

            // Remove from OpenWithProgids
            using var owKey = key.OpenSubKey("OpenWithProgids", writable: true);
            owKey?.DeleteValue(progId, throwOnMissingValue: false);

            Debug.WriteLine($"[FileAssoc] Unregistered extension: {ext}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileAssoc] Failed to unregister {ext}: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Application capabilities (RegisteredApplications)
    // ════════════════════════════════════════════════════════════

    private void EnsureApplicationCapabilities()
    {
        // HKCU\Software\InsaitEdit\Capabilities
        using var capKey = Registry.CurrentUser.CreateSubKey(AppCapabilitiesKey);
        if (capKey == null) return;

        capKey.SetValue("ApplicationName",        SupportedFileTypes.AppName);
        capKey.SetValue("ApplicationDescription", SupportedFileTypes.AppDescription);
        capKey.SetValue("ApplicationIcon",        _iconPath);

        // HKCU\Software\RegisteredApplications
        using var regApps = Registry.CurrentUser.CreateSubKey(RegisteredAppsKey);
        regApps?.SetValue(SupportedFileTypes.AppName, $@"Software\{CapabilitiesRel}");
    }

    private void RegisterCapabilityExtension(FileTypeInfo info)
    {
        var progId = SupportedFileTypes.GetProgId(info.Extension);

        // FileAssociations sub-key
        using var faKey = Registry.CurrentUser.CreateSubKey($@"{AppCapabilitiesKey}\FileAssociations");
        faKey?.SetValue(info.Extension, progId);

        // MIMEAssociations sub-key
        using var mimeKey = Registry.CurrentUser.CreateSubKey($@"{AppCapabilitiesKey}\MIMEAssociations");
        mimeKey?.SetValue(info.ContentType, progId);
    }

    private void UnregisterCapabilityExtension(string ext)
    {
        try
        {
            using var faKey = Registry.CurrentUser.OpenSubKey($@"{AppCapabilitiesKey}\FileAssociations", writable: true);
            faKey?.DeleteValue(ext, throwOnMissingValue: false);

            // Find the content type to remove from MIME associations
            var info = SupportedFileTypes.All.FirstOrDefault(f =>
                f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase));
            if (info != null)
            {
                using var mimeKey = Registry.CurrentUser.OpenSubKey($@"{AppCapabilitiesKey}\MIMEAssociations", writable: true);
                mimeKey?.DeleteValue(info.ContentType, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileAssoc] Failed to remove capability for {ext}: {ex.Message}");
        }
    }

    private void CleanupIfEmpty()
    {
        try
        {
            using var faKey = Registry.CurrentUser.OpenSubKey($@"{AppCapabilitiesKey}\FileAssociations");
            if (faKey != null && faKey.GetValueNames().Length == 0)
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\InsaitEdit", throwOnMissingSubKey: false);

                using var regApps = Registry.CurrentUser.OpenSubKey(RegisteredAppsKey, writable: true);
                regApps?.DeleteValue(SupportedFileTypes.AppName, throwOnMissingValue: false);

                Debug.WriteLine("[FileAssoc] Cleaned up empty capabilities.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileAssoc] Cleanup error: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Utility: open Windows "Default Apps" so user can confirm
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// After registering, open the Windows Settings "Default Apps" page
    /// so the user can confirm Insait Edit as default.
    /// Windows 10 1803+ requires user consent through Settings.
    /// </summary>
    public static void OpenSystemDefaultApps()
        => DefaultProgramHelper.OpenDefaultAppsSettings();
}

/// <summary>
/// Status of a single extension's registration (for UI binding).
/// </summary>
public sealed record FileTypeRegistrationStatus(
    string Extension,
    string Description,
    FileCategory Category,
    bool IsRegistered);

