using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class DebugPage : UserControl
{
    public DebugPage()
    {
        InitializeComponent();
        BrowseDebugDirButton.Click += BrowseDebugDir_Click;
        AddEnvVarButton.Click      += AddEnvVar_Click;
    }


    private async void BrowseDebugDir_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window win) return;
        var result = await win.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            { Title = "Select working directory", AllowMultiple = false });
        if (result.Count > 0) DebugWorkingDirBox.Text = result[0].Path.LocalPath;
    }

    private void AddEnvVar_Click(object? sender, RoutedEventArgs e)
    {
        var keyBox = new TextBox { Watermark = "NAME", Margin = new Thickness(4), MinWidth = 100, FontSize = 12 };
        var valBox = new TextBox { Watermark = "value", Margin = new Thickness(4), MinWidth = 140, FontSize = 12 };
        var removeBtn = new Button
        {
            Content = "x", Width = 28, Height = 28, Margin = new Thickness(4),
            Padding = new Thickness(0), Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#FF9080A8")),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), Margin = new Thickness(0, 1) };
        row.Children.Add(keyBox); row.Children.Add(valBox); row.Children.Add(removeBtn);
        Grid.SetColumn(keyBox, 0); Grid.SetColumn(valBox, 1); Grid.SetColumn(removeBtn, 2);
        removeBtn.Click += (_, _) => EnvVarsList.Items.Remove(row);
        EnvVarsList.Items.Add(row);
    }

    public void Populate()
    {
        DebugLaunchProfileCombo.SelectedIndex = 0;
        DebugArgsBox.Text = ""; DebugWorkingDirBox.Text = "";
        EnableNativeDebugCheck.IsChecked = false;
        EnableSqlDebugCheck.IsChecked    = false;
        EnvVarsList.Items.Clear();
    }

    public void Apply() { }
}