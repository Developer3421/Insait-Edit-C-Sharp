using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

public partial class ConfirmDialog : Window
{
    public bool Result { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
        Title = LocalizationService.Get("Dialog.ConfirmDelete.Title");

        if (this.FindControl<Button>("ConfirmBtn") is { } confirmBtn)
            confirmBtn.Content = LocalizationService.Get("Common.Yes");

        if (this.FindControl<Button>("CancelBtn") is { } cancelBtn)
            cancelBtn.Content = LocalizationService.Get("Common.No");

        this.FindControl<Button>("ConfirmBtn")!.Click += OnConfirm;
        this.FindControl<Button>("CancelBtn")!.Click += OnCancel;
        this.FindControl<Button>("CloseTitleBtn")!.Click += OnCancel;
        KeyDown += OnDialogKeyDown;
    }

    public ConfirmDialog(
        string title,
        string message,
        string confirmText,
        string cancelText,
        string icon = "⚠️",
        bool danger = false) : this()
    {
        Title = title;

        if (this.FindControl<TextBlock>("TitleText") is { } titleText)
            titleText.Text = title;

        if (this.FindControl<TextBlock>("TitleIcon") is { } titleIcon)
            titleIcon.Text = icon;

        if (this.FindControl<TextBlock>("MessageText") is { } messageText)
            messageText.Text = message;

        if (this.FindControl<Button>("ConfirmBtn") is { } confirmBtn)
        {
            confirmBtn.Content = confirmText;
            if (danger)
            {
                confirmBtn.Classes.Remove("confirm-btn");
                confirmBtn.Classes.Add("danger-btn");
            }
        }

        if (this.FindControl<Button>("CancelBtn") is { } cancelBtn)
            cancelBtn.Content = cancelText;

        Opened += (_, _) => this.FindControl<Button>("ConfirmBtn")?.Focus();
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Confirm();
    private void OnCancel(object? sender, RoutedEventArgs e) => Cancel();

    private void Confirm()
    {
        Result = true;
        Close();
    }

    private void Cancel()
    {
        Result = false;
        Close();
    }
}

