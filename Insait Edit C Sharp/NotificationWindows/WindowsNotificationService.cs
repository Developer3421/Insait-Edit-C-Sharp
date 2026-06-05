using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NotificationWindows;

public static class WindowsNotificationService
{
    private static bool _initialized;
    private const string AppId = "InsaitEdit.InsaitEditCSharp";

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            EnsureShortcut();
        }
        catch
        {
            // Notifications unavailable — non-critical
        }
    }

    private static void EnsureShortcut()
    {
        var startMenuPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "Insait Edit C Sharp.lnk");

        if (File.Exists(startMenuPath)) return;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        object? shell = Activator.CreateInstance(shellType);
        if (shell == null) return;

        try
        {
            object? shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell,
                new object[] { startMenuPath });

            if (shortcut != null)
            {
                var t = shortcut.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object?[] { exePath });
                t.InvokeMember("AppUserModelID", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object?[] { AppId });
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

    public static void Show(string title, string content)
    {
        try
        {
            if (!_initialized) Initialize();

            var builder = new StringBuilder();
            builder.Append("<toast><visual><binding template='ToastGeneric'>");
            builder.Append("<text>" + Escape(title) + "</text>");
            builder.Append("<text>" + Escape(content) + "</text>");
            builder.Append("</binding></visual></toast>");

            var doc = new XmlDocument();
            doc.LoadXml(builder.ToString());

            var toast = new ToastNotification(doc);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }
        catch
        {
            // Silently fail — notifications are non-critical
        }
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
