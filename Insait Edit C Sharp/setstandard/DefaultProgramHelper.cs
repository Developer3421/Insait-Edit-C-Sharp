using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Insait_Edit_C_Sharp.SetStandard;

/// <summary>
/// P/Invoke helpers for notifying the Windows shell about
/// file-association changes and for opening the
/// "Default Apps" system settings page.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DefaultProgramHelper
{
    // ── SHChangeNotify ─────────────────────────────────────────
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST       = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(
        uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>
    /// Notifies the Windows shell that file associations have changed.
    /// Should be called after registry writes so Explorer picks up new icons/verbs.
    /// </summary>
    public static void NotifyShellAssociationsChanged()
        => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    // ── Open system "Default Apps" settings ────────────────────
    /// <summary>
    /// Opens the Windows 10/11 "Default Apps" settings page so the user
    /// can confirm or change the default program for file types.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DefaultProgramHelper] Cannot open settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the "Choose default apps by file type" settings page.
    /// </summary>
    public static void OpenFileTypeSettings()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                Arguments = "?registeredAppUser=InsaitEdit",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DefaultProgramHelper] Cannot open file-type settings: {ex.Message}");
        }
    }

    // ── IApplicationActivationManager (Windows 8+) ─────────────
    // Used internally by the OS for "Set Default Programs" — not needed
    // for per-user registration via HKCU which is sufficient.

    /// <summary>
    /// Returns the full path to the running executable.
    /// </summary>
    public static string GetExePath()
        => Environment.ProcessPath
           ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
           ?? "InsaitEdit.exe";
}

