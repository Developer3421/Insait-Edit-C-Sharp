using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class GeneralPage : UserControl
{
    public GeneralPage()
    {
        InitializeComponent();
        BrowseIconButton.Click += BrowseIcon_Click;
        ClearIconButton.Click += (_, _) =>
        {
            AppIconPathBox.Text = "";
            AppIconPreview.IsVisible = false;
            AppIconPlaceholder.IsVisible = true;
        };
        AppIconPathBox.TextChanged += (_, _) => UpdateIconPreview();
    }

    private void InitializeComponent() =>
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private async void BrowseIcon_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window win) return;
        var result = await win.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select icon file",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Icon files") { Patterns = new[] { "*.ico" } },
                    new Avalonia.Platform.Storage.FilePickerFileType("All files")  { Patterns = new[] { "*" } }
                }
            });
        if (result.Count > 0) { AppIconPathBox.Text = result[0].Path.LocalPath; UpdateIconPreview(); }
    }

    private void UpdateIconPreview()
    {
        var path = AppIconPathBox.Text?.Trim() ?? "";
        if (File.Exists(path))
        {
            try { AppIconPreview.Source = new Bitmap(path); AppIconPreview.IsVisible = true; AppIconPlaceholder.IsVisible = false; return; }
            catch { }
        }
        AppIconPreview.IsVisible = false; AppIconPlaceholder.IsVisible = true;
    }

    public void Populate(XElement? pg, string projectPath)
    {
        string? Prop(string n) => pg?.Element(n)?.Value?.Trim();
        var name = Path.GetFileNameWithoutExtension(projectPath);
        AssemblyNameBox.Text     = Prop("AssemblyName")  ?? name;
        DefaultNamespaceBox.Text = Prop("RootNamespace") ?? name;
        StartupObjectBox.Text    = Prop("StartupObject") ?? "";
        AppIconPathBox.Text      = Prop("ApplicationIcon") ?? "";
        SelectByContent(TargetFrameworkCombo, Prop("TargetFramework") ?? "net9.0");
        SelectByTag(OutputTypeCombo, Prop("OutputType") ?? "Exe");
        SelectByContent(LangVersionCombo, Prop("LangVersion") ?? "Default (latest major)");
        SelectByContent(NullableCombo, Prop("Nullable") ?? "enable");
        ImplicitUsingsCheck.IsChecked = ParseBool(Prop("ImplicitUsings"), true);
        AllowUnsafeCheck.IsChecked    = ParseBool(Prop("AllowUnsafeBlocks"), false);
        UpdateIconPreview();
    }

    public void Apply(XElement pg)
    {
        void Set(string n, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) { pg.Element(n)?.Remove(); return; }
            var el = pg.Element(n); if (el == null) pg.Add(new XElement(n, v)); else el.Value = v;
        }
        Set("AssemblyName", AssemblyNameBox.Text?.Trim());
        Set("RootNamespace", DefaultNamespaceBox.Text?.Trim());
        Set("StartupObject", StartupObjectBox.Text?.Trim());
        Set("ApplicationIcon", AppIconPathBox.Text?.Trim());
        Set("TargetFramework", ComboContent(TargetFrameworkCombo));
        Set("OutputType", ComboTag(OutputTypeCombo) ?? ComboContent(OutputTypeCombo));
        var lv = ComboContent(LangVersionCombo);
        if (!string.IsNullOrEmpty(lv) && !lv.StartsWith("Default")) Set("LangVersion", lv);
        else pg.Element("LangVersion")?.Remove();
        Set("Nullable", ComboContent(NullableCombo));
        Set("ImplicitUsings", ImplicitUsingsCheck.IsChecked == true ? "enable" : "disable");
        Set("AllowUnsafeBlocks", AllowUnsafeCheck.IsChecked == true ? "true" : null);
    }

    private static bool ParseBool(string? v, bool def) =>
        v == null ? def : string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(v, "enable", StringComparison.OrdinalIgnoreCase);

    internal static void SelectByContent(ComboBox c, string text)
    {
        for (int i = 0; i < c.Items.Count; i++)
            if (c.Items[i] is ComboBoxItem ci && string.Equals(ci.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            { c.SelectedIndex = i; return; }
        if (c.Items.Count > 0) c.SelectedIndex = 0;
    }

    internal static void SelectByTag(ComboBox c, string tag)
    {
        for (int i = 0; i < c.Items.Count; i++)
            if (c.Items[i] is ComboBoxItem ci && string.Equals(ci.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            { c.SelectedIndex = i; return; }
        if (c.Items.Count > 0) c.SelectedIndex = 0;
    }

    internal static string? ComboContent(ComboBox c) =>
        c.SelectedItem is ComboBoxItem ci ? ci.Content?.ToString() : c.SelectedItem?.ToString();

    internal static string? ComboTag(ComboBox c) =>
        c.SelectedItem is ComboBoxItem ci ? ci.Tag?.ToString() : null;
}