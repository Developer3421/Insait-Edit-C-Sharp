using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.ToastNotifications;

public static class ToastNotificationExtensions
{
    public static NotificationItem AddWithToast(this NotificationService service,
        NotificationSeverity severity, string title, string message, string source = "IDE")
    {
        var notification = service.Add(severity, title, message, source);

        try
        {
            var toast = ToastNotificationService.Instance;

            switch (severity)
            {
                case NotificationSeverity.Error:
                    toast.ShowWithButtons(title, message, new[]
                    {
                        ("View Details", "action=view_errors", true),
                        ("Dismiss", "action=dismiss", false)
                    });
                    break;

                case NotificationSeverity.Warning:
                    toast.ShowWithButtons(title, message, new[]
                    {
                        ("View Details", "action=view_errors", true),
                        ("Dismiss", "action=dismiss", false)
                    });
                    break;

                default:
                    toast.Show(title, message);
                    break;
            }
        }
        catch
        {
        }

        return notification;
    }

    public static NotificationItem AddInfoWithToast(this NotificationService service,
        string title, string message, string source = "IDE")
        => AddWithToast(service, NotificationSeverity.Info, title, message, source);

    public static NotificationItem AddSuccessWithToast(this NotificationService service,
        string title, string message, string source = "IDE")
        => AddWithToast(service, NotificationSeverity.Success, title, message, source);

    public static NotificationItem AddWarningWithToast(this NotificationService service,
        string title, string message, string source = "IDE")
        => AddWithToast(service, NotificationSeverity.Warning, title, message, source);

    public static NotificationItem AddErrorWithToast(this NotificationService service,
        string title, string message, string source = "IDE")
        => AddWithToast(service, NotificationSeverity.Error, title, message, source);
}
