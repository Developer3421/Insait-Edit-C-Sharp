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

        // Guard against null controls (can happen if InitializeComponent
        // fails to bind named controls e.g. due to a missing embedded resource)
        if (BrowseIconButton is { } browse)
            browse.Click += BrowseIcon_Click;

        if (ClearIconButton is { } clear)
            clear.Click += (_, _) =>
            {
                if (AppIconPathBox    is { } box)  box.Text      = "";
                if (AppIconPreview    is { } prev) prev.IsVisible = false;
                if (AppIconPlaceholder is { } ph)  ph.IsVisible  = true;
            };

        if (AppIconPathBox is { } pathBox)
            pathBox.TextChanged += (_, _) => UpdateIconPreview();
    }


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
        if (result.Count > 0)
        {
            if (AppIconPathBox is { } box) box.Text = result[0].Path.LocalPath;
            UpdateIconPreview();
        }
    }

    private void UpdateIconPreview()
    {
        var path = AppIconPathBox?.Text?.Trim() ?? "";
        if (File.Exists(path))
        {
            try
            {
                if (AppIconPreview    is { } prev) { prev.Source = new Bitmap(path); prev.IsVisible = true; }
                if (AppIconPlaceholder is { } ph)  ph.IsVisible = false;
                return;
            }
            catch { }
        }
        if (AppIconPreview    is { } p2)  p2.IsVisible = false;
        if (AppIconPlaceholder is { } ph2) ph2.IsVisible = true;
    }

    public void Populate(XElement? pg, string projectPath)
    {
        // Guard — if InitializeComponent failed to bind controls, do nothing
        if (AssemblyNameBox == null) return;

        string? Prop(string n) => pg?.Element(n)?.Value?.Trim();
        var name = Path.GetFileNameWithoutExtension(projectPath);

        AssemblyNameBox.Text     = Prop("AssemblyName")  ?? name;
        DefaultNamespaceBox.Text = Prop("RootNamespace") ?? name;
        StartupObjectBox.Text    = Prop("StartupObject") ?? "";
        if (AppIconPathBox is { } ib) ib.Text = Prop("ApplicationIcon") ?? "";
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
        // Guard — nothing to apply if controls were never initialised
        if (AssemblyNameBox == null) return;

        void Set(string n, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) { pg.Element(n)?.Remove(); return; }
            var el = pg.Element(n); if (el == null) pg.Add(new XElement(n, v)); else el.Value = v;
        }
        Set("AssemblyName",    AssemblyNameBox.Text?.Trim());
        Set("RootNamespace",   DefaultNamespaceBox.Text?.Trim());
        Set("StartupObject",   StartupObjectBox.Text?.Trim());
        Set("ApplicationIcon", AppIconPathBox?.Text?.Trim());
        Set("TargetFramework", ComboContent(TargetFrameworkCombo));
        Set("OutputType",      ComboTag(OutputTypeCombo) ?? ComboContent(OutputTypeCombo));
        var lv = ComboContent(LangVersionCombo);
        if (!string.IsNullOrEmpty(lv) && !lv.StartsWith("Default")) Set("LangVersion", lv);
        else pg.Element("LangVersion")?.Remove();
        Set("Nullable",        ComboContent(NullableCombo));
        Set("ImplicitUsings",  ImplicitUsingsCheck.IsChecked == true ? "enable" : "disable");
        Set("AllowUnsafeBlocks", AllowUnsafeCheck.IsChecked == true ? "true" : null);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool ParseBool(string? v, bool def) =>
        v == null ? def : string.Equals(v, "true",   StringComparison.OrdinalIgnoreCase)
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