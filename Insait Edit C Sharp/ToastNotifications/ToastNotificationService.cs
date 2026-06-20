using System;
using System.Collections.Generic;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Insait_Edit_C_Sharp.ToastNotifications;

public sealed class ToastNotificationService
{
    public const string AppId = "InsaitEdit.InsaitEditCSharp";

    private static readonly Lazy<ToastNotificationService> _instance =
        new(() => new ToastNotificationService());

    public static ToastNotificationService Instance => _instance.Value;

    private bool _initialized;

    private ToastNotificationService()
    {
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        AppUserModelIdRegistration.EnsureRegistered(AppId);
    }

    public void Show(string title, string message)
    {
        new ToastContentBuilder()
            .AddArgument("action", "toast")
            .AddText(title)
            .AddText(message)
            .Show();
    }

    public void ShowWithButtons(string title, string message,
        IReadOnlyList<(string label, string arguments, bool isBackground)>? buttons = null)
    {
        var builder = new ToastContentBuilder()
            .AddArgument("action", "toast")
            .AddText(title)
            .AddText(message);

        if (buttons != null)
        {
            foreach (var (label, arguments, isBackground) in buttons)
            {
                var button = new ToastButton(label, arguments);
                if (isBackground)
                    button.SetBackgroundActivation();
                builder.AddButton(button);
            }
        }

        builder.Show();
    }

    public void ShowWithLogo(string title, string message, Uri logoUri,
        ToastGenericAppLogoCrop crop = ToastGenericAppLogoCrop.Circle)
    {
        new ToastContentBuilder()
            .AddArgument("action", "toast")
            .AddText(title)
            .AddText(message)
            .AddAppLogoOverride(logoUri, crop)
            .Show();
    }

    public void ShowBuildNotification(bool success, string projectName, int errorCount, int warningCount)
    {
        var builder = new ToastContentBuilder()
            .AddArgument("action", "build")
            .AddArgument("project", projectName);

        if (success)
        {
            builder
                .AddText("Build Succeeded")
                .AddText($"{projectName} built successfully.");
        }
        else
        {
            builder
                .AddText("Build Failed")
                .AddText($"{projectName} — {errorCount} error(s), {warningCount} warning(s).")
                .AddButton(new ToastButton("View Errors", "action=view_errors")
                    .SetBackgroundActivation())
                .AddButton(new ToastButton("View Logs", "action=build_logs")
                    .SetBackgroundActivation());
        }

        builder.AddButton(new ToastButton("Dismiss", "action=dismiss"));
        builder.Show();
    }

    public void ShowMsixNotification(bool success, string packagePath)
    {
        var builder = new ToastContentBuilder()
            .AddArgument("action", "msix_publish")
            .AddArgument("path", packagePath);

        if (success)
        {
            builder
                .AddText("MSIX Package Created")
                .AddText($"Package saved to:\n{packagePath}")
                .AddButton(new ToastButton("Open Folder", $"action=open_folder&path={packagePath}")
                    .SetBackgroundActivation());
        }
        else
        {
            builder
                .AddText("MSIX Packaging Failed")
                .AddText("Check the build log for details.")
                .AddButton(new ToastButton("View Logs", "action=build_logs")
                    .SetBackgroundActivation());
        }

        builder.AddButton(new ToastButton("Dismiss", "action=dismiss"));
        builder.Show();
    }
}
