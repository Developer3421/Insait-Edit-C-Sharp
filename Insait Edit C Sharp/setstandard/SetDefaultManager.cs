using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Insait_Edit_C_Sharp.SetStandard;

/// <summary>
/// Facade that wraps <see cref="FileAssociationService"/> with
/// safety checks (OS guard, error handling) and provides
/// simple one-call methods for the rest of the application.
/// </summary>
public static class SetDefaultManager
{
    /// <summary>
    /// Returns <c>true</c> when running on Windows and the
    /// file association API is available.
    /// </summary>
    public static bool IsSupported
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Register Insait Edit as default handler for ALL supported file types.
    /// No-op on non-Windows platforms.
    /// </summary>
    public static SetDefaultResult RegisterAll()
    {
        if (!IsSupported)
            return new SetDefaultResult(false, "File associations are only supported on Windows.");

        try
        {
            var svc = CreateService();
            svc.RegisterAll();
            return new SetDefaultResult(true, $"Registered {SupportedFileTypes.All.Count} file types.");
        }
        catch (Exception ex)
        {
            return new SetDefaultResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Register Insait Edit for specific extensions only.
    /// </summary>
    public static SetDefaultResult Register(params string[] extensions)
    {
        if (!IsSupported)
            return new SetDefaultResult(false, "File associations are only supported on Windows.");

        try
        {
            var svc = CreateService();
            svc.Register(extensions);
            return new SetDefaultResult(true, $"Registered {extensions.Length} file type(s).");
        }
        catch (Exception ex)
        {
            return new SetDefaultResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Register all extensions belonging to a specific category.
    /// </summary>
    public static SetDefaultResult RegisterCategory(FileCategory category)
    {
        if (!IsSupported)
            return new SetDefaultResult(false, "File associations are only supported on Windows.");

        try
        {
            var extensions = SupportedFileTypes
                .ByCategory(category)
                .Select(f => f.Extension)
                .ToArray();

            if (extensions.Length == 0)
                return new SetDefaultResult(false, $"No extensions found for category {category}.");

            var svc = CreateService();
            svc.Register(extensions);
            return new SetDefaultResult(true, $"Registered {extensions.Length} file type(s) for {category}.");
        }
        catch (Exception ex)
        {
            return new SetDefaultResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Unregister Insait Edit from ALL supported file types.
    /// </summary>
    public static SetDefaultResult UnregisterAll()
    {
        if (!IsSupported)
            return new SetDefaultResult(false, "File associations are only supported on Windows.");

        try
        {
            var svc = CreateService();
            svc.UnregisterAll();
            return new SetDefaultResult(true, "All file associations removed.");
        }
        catch (Exception ex)
        {
            return new SetDefaultResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Get the registration status of all supported file types.
    /// </summary>
    public static IReadOnlyList<FileTypeRegistrationStatus> GetAllStatuses()
    {
        if (!IsSupported)
            return Array.Empty<FileTypeRegistrationStatus>();

        try
        {
            var svc = CreateService();
            return svc.GetAllStatuses();
        }
        catch
        {
            return Array.Empty<FileTypeRegistrationStatus>();
        }
    }

    /// <summary>
    /// Open the Windows "Default Apps" settings page.
    /// </summary>
    public static void OpenSystemSettings()
    {
        if (IsSupported)
            FileAssociationService.OpenSystemDefaultApps();
    }

    [SupportedOSPlatform("windows")]
    private static FileAssociationService CreateService() => new();
}

/// <summary>
/// Result of a set-default operation.
/// </summary>
public sealed record SetDefaultResult(bool Success, string Message);

