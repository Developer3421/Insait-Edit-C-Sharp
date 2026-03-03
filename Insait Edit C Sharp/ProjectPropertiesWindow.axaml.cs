using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Project Properties dialog — General, Build, Package, Signing and Debug tabs.
/// Reads / writes the .csproj XML directly.
/// </summary>
public partial class ProjectPropertiesWindow : Window
{
    private readonly string _projectPath;
    private XDocument? _csproj;
    private XElement? _propertyGroup;

    // Track which nav panel is active
    private Button? _activeNavButton;
    private StackPanel? _activePanel;

    public ProjectPropertiesWindow() : this(string.Empty) { }

    public ProjectPropertiesWindow(string projectPath)
    {
        InitializeComponent();
        _projectPath = projectPath;
        SetupEvents();
        LoadProject();
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("ProjectProps.Title");
        SetNavBtn("NavGeneral",  L("ProjectProps.General"));
        SetNavBtn("NavBuild",    L("ProjectProps.Build"));
        SetNavBtn("NavPackage",  L("ProjectProps.Package"));
        SetNavBtn("NavSign",     L("ProjectProps.Signing"));
        SetNavBtn("NavDebug",    L("ProjectProps.Debug"));
        SetLabel("AssemblyNameLabel",      L("ProjectProps.AssemblyName"));
        SetLabel("DefaultNamespaceLabel",  L("ProjectProps.DefaultNamespace"));
        SetLabel("TargetFrameworkLabel",   L("ProjectProps.TargetFramework"));
        SetLabel("OutputTypeLabel",        L("ProjectProps.OutputType"));
        SetLabel("LangVersionLabel",       L("ProjectProps.LangVersion"));
        SetLabel("NullableLabel",          L("ProjectProps.Nullable"));
        SetLabel("ImplicitUsingsLabel",    L("ProjectProps.ImplicitUsings"));
        SetLabel("AllowUnsafeLabel",       L("ProjectProps.AllowUnsafe"));
        SetBtn("ApplyButton",  L("ProjectProps.Apply"));
        SetBtn("OkButton",     L("ProjectProps.OK"));
        SetBtn("CancelButton", L("ProjectProps.Cancel"));
    }

    private void SetNavBtn(string name, string text)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Content = text;
    }

    private void SetLabel(string name, string text)
    {
        var tb = this.FindControl<TextBlock>(name);
        if (tb != null) tb.Text = text;
    }

    private void SetBtn(string name, string text)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Content = text;
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    // ─────────────────────────────────────────────────────────────
    //  Setup
    // ─────────────────────────────────────────────────────────────

    private void SetupEvents()
    {
        // Title bar drag
        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
            titleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(e);
            };

        // Close
        var closeBtn = this.FindControl<Button>("CloseButton");
        if (closeBtn != null) closeBtn.Click += (_, _) => Close(false);

        // Footer
        var applyBtn = this.FindControl<Button>("ApplyButton");
        if (applyBtn != null) applyBtn.Click += (_, _) => SaveProject();

        var okBtn = this.FindControl<Button>("OkButton");
        if (okBtn != null) okBtn.Click += (_, _) => { if (SaveProject()) Close(true); };

        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (cancelBtn != null) cancelBtn.Click += (_, _) => Close(false);

        // Navigation buttons
        WireNavButton("NavGeneral",  "PanelGeneral");
        WireNavButton("NavBuild",    "PanelBuild");
        WireNavButton("NavPackage",  "PanelPackage");
        WireNavButton("NavSign",     "PanelSign");
        WireNavButton("NavDebug",    "PanelDebug");

        // Browse buttons (Build tab)
        var browseOutput = this.FindControl<Button>("BrowseOutputButton");
        if (browseOutput != null) browseOutput.Click += BrowseOutput_Click;

        // Browse key (Sign tab)
        var browseKey = this.FindControl<Button>("BrowseKeyButton");
        if (browseKey != null) browseKey.Click += BrowseKey_Click;

        // Sign toggle
        var signCheck = this.FindControl<CheckBox>("SignAssemblyCheck");
        if (signCheck != null) signCheck.IsCheckedChanged += (_, _) =>
        {
            var panel = this.FindControl<StackPanel>("SignDetailsPanel");
            if (panel != null) panel.IsVisible = signCheck.IsChecked == true;
        };

        // Browse debug working dir (Debug tab)
        var browseDebugDir = this.FindControl<Button>("BrowseDebugDirButton");
        if (browseDebugDir != null) browseDebugDir.Click += BrowseDebugDir_Click;

        // Add env-var button (Debug tab)
        var addEnvVar = this.FindControl<Button>("AddEnvVarDebugButton");
        if (addEnvVar != null) addEnvVar.Click += AddEnvVar_Click;
    }

    private void WireNavButton(string buttonName, string panelName)
    {
        var btn = this.FindControl<Button>(buttonName);
        if (btn == null) return;

        btn.Click += (_, _) =>
        {
            // Deactivate previous
            if (_activeNavButton != null)
                _activeNavButton.Classes.Remove("active");
            if (_activePanel != null)
                _activePanel.IsVisible = false;

            // Activate new
            btn.Classes.Add("active");
            _activeNavButton = btn;

            var panel = this.FindControl<StackPanel>(panelName);
            if (panel != null)
            {
                panel.IsVisible = true;
                _activePanel = panel;
            }
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Load .csproj
    // ─────────────────────────────────────────────────────────────

    private void LoadProject()
    {
        if (!string.IsNullOrEmpty(_projectPath))
        {
            var titleTb = this.FindControl<TextBlock>("TitleText");
            if (titleTb != null)
                titleTb.Text = $"{Path.GetFileNameWithoutExtension(_projectPath)} — Properties";
        }

        if (!File.Exists(_projectPath)) return;

        try
        {
            _csproj = XDocument.Load(_projectPath);
            _propertyGroup = _csproj.Root?.Elements("PropertyGroup")
                .FirstOrDefault(pg => pg.Attribute("Condition") == null)
                ?? _csproj.Root?.Elements("PropertyGroup").FirstOrDefault();

            PopulateGeneral();
            PopulateBuild();
            PopulatePackage();
            PopulateSigning();
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading project: {ex.Message}");
        }
    }

    private string? Prop(string name) => _propertyGroup?.Element(name)?.Value?.Trim();

    // ─────────────────────────────────────────────────────────────
    //  Populate helpers
    // ─────────────────────────────────────────────────────────────

    private void PopulateGeneral()
    {
        SetText("AssemblyNameBox",     Prop("AssemblyName") ?? Path.GetFileNameWithoutExtension(_projectPath));
        SetText("DefaultNamespaceBox", Prop("RootNamespace") ?? Path.GetFileNameWithoutExtension(_projectPath));
        SetText("StartupObjectBox",    Prop("StartupObject") ?? "");

        SelectComboByContent("TargetFrameworkCombo", Prop("TargetFramework") ?? "net9.0");
        SelectComboByTag("OutputTypeCombo",          Prop("OutputType") ?? "Exe");
        SelectComboByContent("LangVersionCombo",     Prop("LangVersion") ?? "Default (latest major)");
        SelectComboByContent("NullableCombo",        Prop("Nullable") ?? "enable");

        SetCheck("ImplicitUsingsCheck", Prop("ImplicitUsings"), defaultTrue: true);
        SetCheck("AllowUnsafeCheck",    Prop("AllowUnsafeBlocks"), defaultTrue: false);
    }

    private void PopulateBuild()
    {
        SelectComboByContent("ConfigurationCombo", "Debug");
        SelectComboByContent("PlatformCombo",      "Any CPU");

        SetCheck("OptimizeCheck",         Prop("Optimize"),                 false);
        SetCheck("WarningsAsErrorsCheck", Prop("TreatWarningsAsErrors"),    false);
        SetText("NoWarnBox",              Prop("NoWarn") ?? "");
        SetText("OutputPathBox",          Prop("OutputPath") ?? "");
        SetText("IntermediateOutputPathBox", Prop("IntermediateOutputPath") ?? "");
        SetCheck("GenerateDocXmlCheck",   Prop("GenerateDocumentationFile"), false);

        var wl = Prop("WarningLevel") ?? "4";
        var wlCombo = this.FindControl<ComboBox>("WarningLevelCombo");
        if (wlCombo != null)
        {
            var idx = -1;
            for (int i = 0; i < wlCombo.Items.Count; i++)
            {
                if (wlCombo.Items[i] is ComboBoxItem ci && ci.Content?.ToString()?.StartsWith(wl) == true)
                {
                    idx = i;
                    break;
                }
            }
            wlCombo.SelectedIndex = idx >= 0 ? idx : 4;
        }
    }

    private void PopulatePackage()
    {
        SetCheck("GeneratePackageCheck", Prop("GeneratePackageOnBuild"), false);
        SetText("PackageIdBox",          Prop("PackageId") ?? Path.GetFileNameWithoutExtension(_projectPath));
        SetText("PackageVersionBox",     Prop("Version") ?? "1.0.0");
        SetText("AuthorsBox",            Prop("Authors") ?? "");
        SetText("CompanyBox",            Prop("Company") ?? "");
        SetText("ProductBox",            Prop("Product") ?? "");
        SetText("PackageDescriptionBox", Prop("Description") ?? "");
        SetText("RepositoryUrlBox",      Prop("RepositoryUrl") ?? "");
        SetText("LicenseExpressionBox",  Prop("PackageLicenseExpression") ?? "");
        SetText("PackageTagsBox",        Prop("PackageTags") ?? "");
    }

    private void PopulateSigning()
    {
        var sign = string.Equals(Prop("SignAssembly"), "true", StringComparison.OrdinalIgnoreCase);
        SetCheckDirect("SignAssemblyCheck", sign);
        SetText("KeyFileBox", Prop("AssemblyOriginatorKeyFile") ?? "");
        SetCheck("DelaySignCheck", Prop("DelaySign"), false);

        var signPanel = this.FindControl<StackPanel>("SignDetailsPanel");
        if (signPanel != null) signPanel.IsVisible = sign;
    }

    // ─────────────────────────────────────────────────────────────
    //  UI helpers
    // ─────────────────────────────────────────────────────────────

    private void SetText(string controlName, string? text)
    {
        var tb = this.FindControl<TextBox>(controlName);
        if (tb != null) tb.Text = text ?? "";
    }

    private void SetCheck(string controlName, string? value, bool defaultTrue)
    {
        var cb = this.FindControl<CheckBox>(controlName);
        if (cb == null) return;
        if (value == null) { cb.IsChecked = defaultTrue; return; }
        cb.IsChecked = string.Equals(value, "true",   StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase);
    }

    private void SetCheckDirect(string controlName, bool value)
    {
        var cb = this.FindControl<CheckBox>(controlName);
        if (cb != null) cb.IsChecked = value;
    }

    private void SelectComboByContent(string controlName, string content)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            var text = combo.Items[i] is ComboBoxItem ci ? ci.Content?.ToString() ?? "" : combo.Items[i]?.ToString() ?? "";
            if (string.Equals(text, content, StringComparison.OrdinalIgnoreCase))
            { combo.SelectedIndex = i; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void SelectComboByTag(string controlName, string tag)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem ci &&
                string.Equals(ci.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            { combo.SelectedIndex = i; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private string GetText(string controlName) =>
        this.FindControl<TextBox>(controlName)?.Text?.Trim() ?? "";

    private bool GetCheck(string controlName) =>
        this.FindControl<CheckBox>(controlName)?.IsChecked == true;

    private string? GetComboContent(string controlName)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo?.SelectedItem is ComboBoxItem ci) return ci.Content?.ToString();
        return combo?.SelectedItem?.ToString();
    }

    private string? GetComboTag(string controlName)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo?.SelectedItem is ComboBoxItem ci) return ci.Tag?.ToString();
        return null;
    }

    private void SetStatus(string text)
    {
        var lbl = this.FindControl<TextBlock>("StatusLabel");
        if (lbl != null) lbl.Text = text;
    }

    // ─────────────────────────────────────────────────────────────
    //  Save to .csproj
    // ─────────────────────────────────────────────────────────────

    private bool SaveProject()
    {
        if (!File.Exists(_projectPath)) return false;

        try
        {
            if (_csproj == null)
                _csproj = XDocument.Load(_projectPath);

            if (_propertyGroup == null)
            {
                _propertyGroup = new XElement("PropertyGroup");
                _csproj.Root!.AddFirst(_propertyGroup);
            }

            void SetProp(string name, string? value, bool removeIfEmpty = false)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _propertyGroup!.Element(name)?.Remove();
                    return;
                }
                var el = _propertyGroup!.Element(name);
                if (el == null) _propertyGroup.Add(new XElement(name, value));
                else el.Value = value;
            }

            // General
            SetProp("AssemblyName",    GetText("AssemblyNameBox"));
            SetProp("RootNamespace",   GetText("DefaultNamespaceBox"));
            SetProp("TargetFramework", GetComboContent("TargetFrameworkCombo"));
            SetProp("OutputType",      GetComboTag("OutputTypeCombo") ?? GetComboContent("OutputTypeCombo"));
            var langVer = GetComboContent("LangVersionCombo");
            if (!string.IsNullOrEmpty(langVer) && !langVer.StartsWith("Default"))
                SetProp("LangVersion", langVer);
            else _propertyGroup.Element("LangVersion")?.Remove();
            SetProp("Nullable",         GetComboContent("NullableCombo"));
            SetProp("ImplicitUsings",   GetCheck("ImplicitUsingsCheck") ? "enable" : "disable");
            SetProp("AllowUnsafeBlocks",GetCheck("AllowUnsafeCheck") ? "true" : null, removeIfEmpty: true);
            SetProp("StartupObject",    GetText("StartupObjectBox"), removeIfEmpty: true);

            // Build
            SetProp("Optimize",               GetCheck("OptimizeCheck")           ? "true" : null, removeIfEmpty: true);
            SetProp("TreatWarningsAsErrors",  GetCheck("WarningsAsErrorsCheck")   ? "true" : null, removeIfEmpty: true);
            SetProp("NoWarn",                 GetText("NoWarnBox"),               removeIfEmpty: true);
            SetProp("OutputPath",             GetText("OutputPathBox"),            removeIfEmpty: true);
            SetProp("IntermediateOutputPath", GetText("IntermediateOutputPathBox"),removeIfEmpty: true);
            SetProp("GenerateDocumentationFile", GetCheck("GenerateDocXmlCheck")  ? "true" : null, removeIfEmpty: true);

            // Package
            SetProp("GeneratePackageOnBuild",   GetCheck("GeneratePackageCheck") ? "true" : null, removeIfEmpty: true);
            SetProp("PackageId",                GetText("PackageIdBox"),          removeIfEmpty: true);
            SetProp("Version",                  GetText("PackageVersionBox"),     removeIfEmpty: true);
            SetProp("Authors",                  GetText("AuthorsBox"),            removeIfEmpty: true);
            SetProp("Company",                  GetText("CompanyBox"),            removeIfEmpty: true);
            SetProp("Product",                  GetText("ProductBox"),            removeIfEmpty: true);
            SetProp("Description",              GetText("PackageDescriptionBox"), removeIfEmpty: true);
            SetProp("RepositoryUrl",            GetText("RepositoryUrlBox"),      removeIfEmpty: true);
            SetProp("PackageLicenseExpression", GetText("LicenseExpressionBox"),  removeIfEmpty: true);
            SetProp("PackageTags",              GetText("PackageTagsBox"),        removeIfEmpty: true);

            // Signing
            SetProp("SignAssembly",               GetCheck("SignAssemblyCheck") ? "true" : null, removeIfEmpty: true);
            SetProp("AssemblyOriginatorKeyFile",  GetText("KeyFileBox"),         removeIfEmpty: true);
            SetProp("DelaySign",                  GetCheck("DelaySignCheck") ? "true" : null,    removeIfEmpty: true);

            _csproj.Save(_projectPath);
            SetStatus("Saved successfully.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving: {ex.Message}");
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Browse handlers
    // ─────────────────────────────────────────────────────────────

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Select output folder", AllowMultiple = false });
        if (result.Count > 0) SetText("OutputPathBox", result[0].Path.LocalPath);
    }

    private async void BrowseKey_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(
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
        if (result.Count > 0) SetText("KeyFileBox", result[0].Path.LocalPath);
    }

    private async void BrowseDebugDir_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Select working directory", AllowMultiple = false });
        if (result.Count > 0) SetText("DebugWorkingDirBox", result[0].Path.LocalPath);
    }

    private void AddEnvVar_Click(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ItemsControl>("EnvVarsList");
        if (list == null) return;

        var keyBox   = new TextBox { Watermark = "NAME",  Margin = new Thickness(4), MinWidth = 120 };
        var valueBox = new TextBox { Watermark = "value", Margin = new Thickness(4), MinWidth = 160 };
        var removeBtn = new Button
        {
            Content = "✕", Width = 28, Margin = new Thickness(4), Padding = new Thickness(0),
            Background = Avalonia.Media.Brushes.Transparent
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), Margin = new Thickness(0, 2) };
        row.Children.Add(keyBox);
        row.Children.Add(valueBox);
        row.Children.Add(removeBtn);
        Grid.SetColumn(keyBox, 0);
        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(removeBtn, 2);

        removeBtn.Click += (_, _) => list.Items.Remove(row);
        list.Items.Add(row);
    }
}

