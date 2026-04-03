using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Insait_Edit_C_Sharp.Models;

namespace Insait_Edit_C_Sharp.Services;

public sealed class NotificationService : INotifyPropertyChanged
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());

    public static NotificationService Instance => _instance.Value;

    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    public int UnreadCount => Notifications.Count(notification => notification.IsUnread);
    public bool HasNotifications => Notifications.Count > 0;

    private NotificationService()
    {
        Notifications.CollectionChanged += OnNotificationsCollectionChanged;
    }

    public NotificationItem Add(NotificationSeverity severity, string title, string message, string source)
    {
        var notification = new NotificationItem
        {
            Severity = severity,
            Title = title,
            Message = message,
            Source = source,
            IsUnread = true
        };

        Notifications.Insert(0, notification);
        notification.PropertyChanged += NotificationOnPropertyChanged;
        RaiseCountersChanged();
        return notification;
    }

    public NotificationItem AddInfo(string title, string message, string source = "IDE") =>
        Add(NotificationSeverity.Info, title, message, source);

    public NotificationItem AddSuccess(string title, string message, string source = "IDE") =>
        Add(NotificationSeverity.Success, title, message, source);

    public NotificationItem AddWarning(string title, string message, string source = "IDE") =>
        Add(NotificationSeverity.Warning, title, message, source);

    public NotificationItem AddError(string title, string message, string source = "IDE") =>
        Add(NotificationSeverity.Error, title, message, source);

    public void MarkAllAsRead()
    {
        foreach (var notification in Notifications)
            notification.IsUnread = false;

        RaiseCountersChanged();
    }

    public void Clear()
    {
        foreach (var notification in Notifications)
            notification.PropertyChanged -= NotificationOnPropertyChanged;

        Notifications.Clear();
        RaiseCountersChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotificationOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationItem.IsUnread))
            RaiseCountersChanged();
    }

    private void OnNotificationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<NotificationItem>())
                item.PropertyChanged -= NotificationOnPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<NotificationItem>())
                item.PropertyChanged += NotificationOnPropertyChanged;
        }

        RaiseCountersChanged();
    }

    private void RaiseCountersChanged()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(Notifications));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

