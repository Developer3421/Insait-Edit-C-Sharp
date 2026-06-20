using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Insait_Edit_C_Sharp.ToastNotifications;

internal static class ToastActivationHandler
{
    public static void HandleActivation(string argument)
    {
        var argsDict = ParseArguments(argument);

        if (!argsDict.TryGetValue("action", out var action))
            return;

        switch (action)
        {
            case "view_errors":
                ViewErrors();
                break;
            case "build_logs":
                OpenBuildLogs();
                break;
            case "open_folder":
                if (argsDict.TryGetValue("path", out var path))
                    OpenFolder(path);
                break;
        }
    }

    public static void ViewErrors()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = GetMainWindow();
            if (window?.DataContext is ViewModels.MainViewModel vm)
            {
                vm.IsBuildInProgress = false;
            }
        });
    }

    public static void OpenBuildLogs()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InsaitEdit",
                "Logs");

            if (Directory.Exists(logDir))
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                var folderPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (folderPath != null)
                    Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
            }
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> ParseArguments(string argument)
    {
        var result = new Dictionary<string, string>();
        var pairs = argument.Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
                result[parts[0]] = Uri.UnescapeDataString(parts[1]);
        }
        return result;
    }

    private static MainWindow? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows ?? [])
            {
                if (window is MainWindow mainWindow)
                    return mainWindow;
            }
        }
        return null;
    }
}
