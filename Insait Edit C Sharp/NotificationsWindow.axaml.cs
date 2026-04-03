using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Services;
using System.Collections.Specialized;
using System.Linq;

namespace Insait_Edit_C_Sharp;

public partial class NotificationsWindow : Window
{
    private readonly NotificationService _notificationService = NotificationService.Instance;

    public NotificationsWindow()
    {
        InitializeComponent();
        DataContext = _notificationService;

        if (this.FindControl<Border>("TitleBar") is { } titleBar)
            titleBar.PointerPressed += TitleBar_PointerPressed;

        if (this.FindControl<Button>("CloseButton") is { } closeButton)
            closeButton.Click += (_, _) => Close();

        if (this.FindControl<Button>("CloseWindowButton") is { } closeWindowButton)
            closeWindowButton.Click += (_, _) => Close();

        if (this.FindControl<Button>("MarkAllReadButton") is { } markAllReadButton)
            markAllReadButton.Click += MarkAllReadButton_Click;

        if (this.FindControl<Button>("ClearAllButton") is { } clearAllButton)
            clearAllButton.Click += ClearAllButton_Click;

        if (this.FindControl<ListBox>("NotificationsList") is { } listBox)
            listBox.SelectionChanged += NotificationsList_SelectionChanged;

        Opened += (_, _) =>
        {
            _notificationService.MarkAllAsRead();
            UpdateSummary();
            UpdateEmptyState();
        };

        _notificationService.Notifications.CollectionChanged += Notifications_CollectionChanged;
        Closed += (_, _) => _notificationService.Notifications.CollectionChanged -= Notifications_CollectionChanged;

        UpdateSummary();
        UpdateEmptyState();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Notifications_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSummary();
        UpdateEmptyState();
    }

    private void NotificationsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var notification in e.AddedItems.OfType<NotificationItem>())
            notification.IsUnread = false;

        UpdateSummary();
    }

    private void MarkAllReadButton_Click(object? sender, RoutedEventArgs e)
    {
        _notificationService.MarkAllAsRead();
        UpdateSummary();
    }

    private void ClearAllButton_Click(object? sender, RoutedEventArgs e)
    {
        _notificationService.Clear();
        UpdateSummary();
        UpdateEmptyState();
    }

    private void UpdateSummary()
    {
        var summaryText = this.FindControl<TextBlock>("SummaryText");
        if (summaryText == null)
            return;

        var total = _notificationService.Notifications.Count;
        var unread = _notificationService.UnreadCount;
        summaryText.Text = unread > 0
            ? $"{total} notifications • {unread} unread"
            : $"{total} notifications";
    }

    private void UpdateEmptyState()
    {
        var emptyStateText = this.FindControl<TextBlock>("EmptyStateText");
        var notificationsList = this.FindControl<ListBox>("NotificationsList");
        if (emptyStateText == null || notificationsList == null)
            return;

        var isEmpty = _notificationService.Notifications.Count == 0;
        emptyStateText.IsVisible = isEmpty;
        notificationsList.IsVisible = !isEmpty;
    }
}

