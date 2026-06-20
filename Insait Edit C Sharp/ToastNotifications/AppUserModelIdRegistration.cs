using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Insait_Edit_C_Sharp.ToastNotifications;

internal static class AppUserModelIdRegistration
{
    public static void EnsureRegistered(string appId)
    {
        try
        {
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Insait Edit C Sharp.lnk");

            if (File.Exists(startMenuPath))
            {
                var existingAppId = GetAppUserModelId(startMenuPath);
                if (string.Equals(existingAppId, appId, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            CreateShortcut(startMenuPath, appId);
        }
        catch
        {
        }
    }

    private static void CreateShortcut(string shortcutPath, string appId)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        var shell = Activator.CreateInstance(shellType);
        if (shell == null) return;

        try
        {
            var shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell,
                new object[] { shortcutPath });

            if (shortcut != null)
            {
                var t = shortcut.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                t.InvokeMember("AppUserModelID", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { appId });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

                if (Marshal.IsComObject(shortcut))
                    Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            if (Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
    }

    private static string? GetAppUserModelId(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return null;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            try
            {
                var folder = shellType.InvokeMember("NameSpace",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell,
                    new object[] { Path.GetDirectoryName(shortcutPath) });

                var folderItem = folder?.GetType().InvokeMember("ParseName",
                    System.Reflection.BindingFlags.InvokeMethod, null, folder,
                    new object[] { Path.GetFileName(shortcutPath) });

                var verbs = folderItem?.GetType().InvokeMember("Verbs",
                    System.Reflection.BindingFlags.GetProperty, null, folderItem, null);

                var verb = verbs?.GetType().InvokeMember("Item",
                    System.Reflection.BindingFlags.InvokeMethod, null, verbs,
                    new object[] { 0 });

                var appId = verb?.GetType().InvokeMember("AppUserModelID",
                    System.Reflection.BindingFlags.GetProperty, null, verb, null);

                return appId?.ToString();
            }
            finally
            {
                if (Marshal.IsComObject(shell))
                    Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }
}
