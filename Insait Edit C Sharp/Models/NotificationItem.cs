using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Insait_Edit_C_Sharp.Models;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class NotificationItem : INotifyPropertyChanged
{
    private bool _isUnread = true;

    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Info;
    public string Source { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool IsUnread
    {
        get => _isUnread;
        set => SetProperty(ref _isUnread, value);
    }

    public string SeverityIcon => Severity switch
    {
        NotificationSeverity.Success => "✅",
        NotificationSeverity.Warning => "⚠️",
        NotificationSeverity.Error => "⛔",
        _ => "ℹ️"
    };

    public string TimestampText => Timestamp.ToString("HH:mm:ss");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }
}


