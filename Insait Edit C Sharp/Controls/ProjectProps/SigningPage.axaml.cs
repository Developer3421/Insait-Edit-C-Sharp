using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class SigningPage : UserControl
{
    public SigningPage()
    {
        InitializeComponent();
        SignAssemblyCheck.IsCheckedChanged += (_, _) =>
            SignDetailsPanel.IsVisible = SignAssemblyCheck.IsChecked == true;
        BrowseKeyButton.Click += BrowseKey_Click;
    }

    private void InitializeComponent() =>
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private async void BrowseKey_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window win) return;
        var result = await win.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select strong-name key file",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Key files") { Patterns = new[] { "*.snk", "*.pfx" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });
        if (result.Count > 0) KeyFileBox.Text = result[0].Path.LocalPath;
    }

    public void Populate(XElement? pg)
    {
        string? Prop(string n) => pg?.Element(n)?.Value?.Trim();
        var sign = string.Equals(Prop("SignAssembly"), "true", StringComparison.OrdinalIgnoreCase);
        SignAssemblyCheck.IsChecked = sign;
        KeyFileBox.Text             = Prop("AssemblyOriginatorKeyFile") ?? "";
        DelaySignCheck.IsChecked    = string.Equals(Prop("DelaySign"), "true", StringComparison.OrdinalIgnoreCase);
        SignDetailsPanel.IsVisible  = sign;
    }

    public void Apply(XElement pg)
    {
        void Set(string n, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) { pg.Element(n)?.Remove(); return; }
            var el = pg.Element(n); if (el == null) pg.Add(new XElement(n, v)); else el.Value = v;
        }
        Set("SignAssembly",              SignAssemblyCheck.IsChecked == true ? "true" : null);
        Set("AssemblyOriginatorKeyFile", KeyFileBox.Text?.Trim());
        Set("DelaySign",                 DelaySignCheck.IsChecked == true ? "true" : null);
    }
}