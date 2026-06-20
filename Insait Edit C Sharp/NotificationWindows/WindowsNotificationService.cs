using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NotificationWindows;

public static class WindowsNotificationService
{
    private static bool _initialized;
    private const string AppId = "InsaitEdit.InsaitEditCSharp";

    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIF_INFO = 16;
    private const uint NIF_GUID = 32;
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint cmd, ref NOTIFYICONDATA data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

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

            var hWnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hWnd == nint.Zero) return;

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hWnd,
                uID = 0,
                uFlags = NIF_INFO | NIF_GUID,
                szInfo = content ?? "",
                szInfoTitle = title ?? "",
                szInfoFlags = "NIIF_INFO",
                guidItem = Guid.NewGuid(),
                uTimeoutOrVersion = 5000,
                szTip = "Insait Edit",
            };

            Shell_NotifyIcon(NIM_ADD, ref data);
        }
        catch
        {
        }
    }
}
