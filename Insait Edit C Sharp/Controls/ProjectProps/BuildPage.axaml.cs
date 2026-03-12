using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class BuildPage : UserControl
{
    public BuildPage()
    {
        InitializeComponent();
        BrowseOutputButton.Click += BrowseOutput_Click;
    }

    private void InitializeComponent() =>
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window win) return;
        var result = await win.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            { Title = "Select output folder", AllowMultiple = false });
        if (result.Count > 0) OutputPathBox.Text = result[0].Path.LocalPath;
    }

    public void Populate(XElement? pg)
    {
        string? Prop(string n) => pg?.Element(n)?.Value?.Trim();
        ConfigurationCombo.SelectedIndex = 0;
        PlatformCombo.SelectedIndex      = 0;
        OptimizeCheck.IsChecked         = ParseBool(Prop("Optimize"), false);
        WarningsAsErrorsCheck.IsChecked = ParseBool(Prop("TreatWarningsAsErrors"), false);
        var wl = Prop("WarningLevel") ?? "4";
        var matched = false;
        for (int i = 0; i < WarningLevelCombo.Items.Count; i++)
            if (WarningLevelCombo.Items[i] is ComboBoxItem ci && ci.Tag?.ToString() == wl)
            { WarningLevelCombo.SelectedIndex = i; matched = true; break; }
        if (!matched) WarningLevelCombo.SelectedIndex = 4;
        NoWarnBox.Text                 = Prop("NoWarn") ?? "";
        OutputPathBox.Text             = Prop("OutputPath") ?? "";
        IntermediateOutputPathBox.Text = Prop("IntermediateOutputPath") ?? "";
        GenerateDocXmlCheck.IsChecked  = ParseBool(Prop("GenerateDocumentationFile"), false);
    }

    public void Apply(XElement pg)
    {
        void Set(string n, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) { pg.Element(n)?.Remove(); return; }
            var el = pg.Element(n); if (el == null) pg.Add(new XElement(n, v)); else el.Value = v;
        }
        Set("Optimize",               OptimizeCheck.IsChecked == true ? "true" : null);
        Set("TreatWarningsAsErrors",  WarningsAsErrorsCheck.IsChecked == true ? "true" : null);
        Set("NoWarn",                 NoWarnBox.Text?.Trim());
        Set("OutputPath",             OutputPathBox.Text?.Trim());
        Set("IntermediateOutputPath", IntermediateOutputPathBox.Text?.Trim());
        Set("GenerateDocumentationFile", GenerateDocXmlCheck.IsChecked == true ? "true" : null);
        if (WarningLevelCombo.SelectedItem is ComboBoxItem wci)
        {
            var tag = wci.Tag?.ToString() ?? "4";
            if (tag != "4") Set("WarningLevel", tag); else pg.Element("WarningLevel")?.Remove();
        }
    }

    private static bool ParseBool(string? v, bool def) =>
        v == null ? def : string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
}