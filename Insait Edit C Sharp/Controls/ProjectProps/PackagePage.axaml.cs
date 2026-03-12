using Avalonia.Controls;
using System;
using System.IO;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class PackagePage : UserControl
{
    public PackagePage() { InitializeComponent(); }

    private void InitializeComponent() =>
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    public void Populate(XElement? pg, string projectPath)
    {
        string? Prop(string n) => pg?.Element(n)?.Value?.Trim();
        var name = Path.GetFileNameWithoutExtension(projectPath);
        GeneratePackageCheck.IsChecked = ParseBool(Prop("GeneratePackageOnBuild"), false);
        PackageIdBox.Text              = Prop("PackageId") ?? name;
        PackageVersionBox.Text         = Prop("Version") ?? "1.0.0";
        AuthorsBox.Text                = Prop("Authors") ?? "";
        CompanyBox.Text                = Prop("Company") ?? "";
        ProductBox.Text                = Prop("Product") ?? "";
        PackageDescriptionBox.Text     = Prop("Description") ?? "";
        RepositoryUrlBox.Text          = Prop("RepositoryUrl") ?? "";
        LicenseExpressionBox.Text      = Prop("PackageLicenseExpression") ?? "";
        PackageTagsBox.Text            = Prop("PackageTags") ?? "";
    }

    public void Apply(XElement pg)
    {
        void Set(string n, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) { pg.Element(n)?.Remove(); return; }
            var el = pg.Element(n); if (el == null) pg.Add(new XElement(n, v)); else el.Value = v;
        }
        Set("GeneratePackageOnBuild",   GeneratePackageCheck.IsChecked == true ? "true" : null);
        Set("PackageId",                PackageIdBox.Text?.Trim());
        Set("Version",                  PackageVersionBox.Text?.Trim());
        Set("Authors",                  AuthorsBox.Text?.Trim());
        Set("Company",                  CompanyBox.Text?.Trim());
        Set("Product",                  ProductBox.Text?.Trim());
        Set("Description",              PackageDescriptionBox.Text?.Trim());
        Set("RepositoryUrl",            RepositoryUrlBox.Text?.Trim());
        Set("PackageLicenseExpression", LicenseExpressionBox.Text?.Trim());
        Set("PackageTags",              PackageTagsBox.Text?.Trim());
    }

    private static bool ParseBool(string? v, bool def) =>
        v == null ? def : string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
}