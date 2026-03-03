using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;

namespace Insait_Edit_C_Sharp;

public partial class MsixManagerWindow : Window
{
    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────
    private readonly MsixService   _service     = new();
    private readonly PublishService _pubService = new();
    private string?  _loadedMsixPath;
    private MsixPackageInfo? _loadedPackage;
    private bool     _isBusy;
    private string?  _lastOutputMsixPath;
    private readonly StringBuilder _logBuffer = new();

    // Certificate signing
    private List<CertificateViewModel> _userCerts = new();
    private CertificateViewModel?      _selectedCert;

    // Certificate for sign-after-build
    private List<CertificateViewModel> _buildCerts = new();
    private CertificateViewModel?      _buildSelectedCert;

    // ─────────────────────────────────────────────────────────────
    //  Constructors
    // ─────────────────────────────────────────────────────────────
    public MsixManagerWindow() : this(null) { }

    public MsixManagerWindow(string? projectPath)
    {
        InitializeComponent();

        WireEvents();
        if (!string.IsNullOrEmpty(projectPath))
            PreFillFromProject(projectPath);
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("Msix.Title");

        // Title bar
        SetText("TitleBarText", L("Msix.Title"));

        // Nav section headers
        SetText("NavActionsHeader",  L("Msix.NavActions"));
        SetText("NavPackageHeader",  L("Msix.NavPackage"));
        SetText("NavOutputHeader",   L("Msix.NavOutput"));
        SetText("NavSigningHeader",  L("Msix.NavSigning"));

        // Nav items
        SetText("NavBuildText",    L("Msix.BuildMsix"));
        SetText("NavOpenText",     L("Msix.OpenMsix"));
        SetText("NavIdentityText", L("Msix.Identity"));
        SetText("NavEntryText",    L("Msix.EntryPoint"));
        SetText("NavManifestText", L("Msix.ManifestXml"));
        SetText("NavBuildLogText", L("Msix.BuildLog"));
        SetText("NavSignText",     L("Msix.SignMsix"));

        // ── Build page ──
        SetText("BuildPageTitle",      L("Msix.BuildPageTitle"));
        SetText("BuildPageSubtitle",   L("Msix.BuildPageSubtitle"));
        SetText("BuildProjectHeader",  L("Msix.BuildProjectHeader"));
        SetText("BuildProjectFileLabel", L("Msix.BuildProjectFile"));
        SetBtn("BrowseProjectBtn",     L("Msix.Browse"));
        SetText("BuildConfigLabel",    L("Msix.Configuration"));
        SetText("BuildRuntimeLabel",   L("Msix.TargetRuntime"));
        SetText("BuildFrameworkLabel", L("Msix.TargetFramework"));
        SetText("BuildIdentityHeader", L("Msix.PackageIdentity"));
        SetText("BuildIdNameLabel",    L("Msix.PackageIdName"));
        SetText("BuildVersionLabel",   L("Msix.Version"));
        SetText("BuildDisplayNameLabel", L("Msix.DisplayName"));
        SetText("BuildPublisherLabel", L("Msix.PublisherDN"));
        SetText("BuildPubDisplayLabel", L("Msix.PublisherDisplayName"));
        SetText("BuildDescLabel",      L("Msix.Description"));
        SetText("BuildLogoLabel",      L("Msix.LogoPath"));
        SetText("BuildEntryHeader",    L("Msix.EntryPointHeader"));
        SetBtn("DetectExeBtn",         L("Msix.AutoDetect"));
        SetText("BuildExeLabel",       L("Msix.Executable"));
        SetText("BuildEntryClassLabel", L("Msix.EntryPointClass"));
        SetText("BuildEntryHint",      L("Msix.EntryPointHint"));
        SetText("BuildPublishHeader",  L("Msix.PublishOptions"));
        SetCheckBox("BuildSingleFileCheck", L("Msix.SingleFile"));
        SetCheckBox("BuildReadyToRunCheck", L("Msix.ReadyToRun"));
        SetCheckBox("BuildTrimCheck",       L("Msix.TrimAssemblies"));
        SetCheckBox("BuildCleanOutputCheck", L("Msix.CleanBeforePublish"));
        SetText("BuildOutputHeader",   L("Msix.OutputHeader"));
        SetText("BuildOutputPathLabel", L("Msix.MsixOutputPath"));
        SetBtn("BrowseMsixOutputBtn",  L("Msix.Browse"));
        SetText("BuildPublishDirLabel", L("Msix.IntermediateFolder"));
        SetBtn("BrowsePublishDirBtn",   L("Msix.Browse"));

        // ── Open page ──
        SetText("OpenPageTitle",    L("Msix.OpenPageTitle"));
        SetText("OpenPageSubtitle", L("Msix.OpenPageSubtitle"));
        SetText("OpenFileHeader",   L("Msix.PackageFile"));
        SetBtn("BrowseOpenMsixBtn", L("Msix.Browse"));
        SetBtn("OpenMsixBtn",       L("Msix.OpenPackageBtn"));
        SetText("OpenLoadedHeader", L("Msix.LoadedPackage"));
        SetText("OpenNameLabel",    L("Msix.LabelName"));
        SetText("OpenVersionLabel", L("Msix.LabelVersion"));
        SetText("OpenPublisherLabel", L("Msix.LabelPublisher"));
        SetText("OpenExeLabel",     L("Msix.LabelExecutable"));
        SetBtn("EditOpenedIdentityBtn", L("Msix.EditIdentity"));
        SetBtn("EditOpenedEntryBtn",    L("Msix.EditEntryPoint"));
        SetBtn("ViewOpenedManifestBtn", L("Msix.ViewManifest"));

        // ── Identity page ──
        SetText("IdPageTitle",      L("Msix.IdPageTitle"));
        SetText("IdPageSubtitle",   L("Msix.IdPageSubtitle"));
        SetText("IdNameLabel",      L("Msix.PackageName"));
        SetText("IdVersionLabel",   L("Msix.VersionLabel"));
        SetText("IdPublisherLabel", L("Msix.PublisherDNShort"));
        SetText("IdArchLabel",      L("Msix.Architecture"));
        SetText("IdDisplayNameLabel", L("Msix.DisplayName"));
        SetText("IdPubDisplayLabel", L("Msix.PublisherDisplayName"));
        SetText("IdDescLabel",      L("Msix.Description"));
        SetText("IdLogoLabel",      L("Msix.LogoRelPath"));
        SetBtn("SaveIdentityBtn",   L("Msix.SaveChanges"));

        // ── Entry Point page ──
        SetText("EntryPageTitle",       L("Msix.EntryPageTitle"));
        SetText("EntryPageSubtitle",    L("Msix.EntryPageSubtitle"));
        SetText("EntryExeFoundLabel",   L("Msix.ExeFoundLabel"));
        SetBtn("ScanExeBtn",           L("Msix.Scan"));
        SetBtn("UseSelectedExeBtn",    L("Msix.UseSelectedExe"));
        SetText("EntrySelectedExeLabel", L("Msix.SelectedExe"));
        SetText("EntryClassLabel",      L("Msix.EntryPointClass"));
        SetBtn("SaveEntryBtn",         L("Msix.SaveEntryPoint"));
        SetText("EntryRefTitle",       L("Msix.EntryRefTitle"));
        SetText("EntryRefFullTrust",   L("Msix.EntryRefFullTrust"));
        SetText("EntryRefWinUI",       L("Msix.EntryRefWinUI"));

        // ── Manifest page ──
        SetText("ManifestPageTitle",    L("Msix.ManifestTitle"));
        SetText("ManifestPageSubtitle", L("Msix.ManifestSubtitle"));
        SetBtn("SaveManifestBtn",      L("Msix.SaveManifest"));
        SetBtn("CopyManifestBtn",      L("Msix.Copy"));

        // ── Build Log page ──
        SetText("LogPageTitle",    L("Msix.LogTitle"));
        SetText("LogPageSubtitle", L("Msix.LogSubtitle"));
        SetBtn("ClearLogBtn",     L("Msix.ClearLog"));
        SetBtn("CopyLogBtn",      L("Msix.Copy"));
        SetBtn("OpenOutputFolderBtn", L("Msix.OpenOutputFolder"));

        // ── Sign page ──
        SetText("SignPageTitle",      L("Msix.SignPageTitle"));
        SetText("SignPageSubtitle",   L("Msix.SignPageSubtitle"));
        SetText("SignFileHeader",     L("Msix.SignFileHeader"));
        SetText("SignFileLabel",      L("Msix.SignFilePath"));
        SetBtn("BrowseSignMsixBtn",  L("Msix.Browse"));
        SetText("SignCertsHeader",    L("Msix.CertsHeader"));
        SetBtn("RefreshCertsBtn",    L("Msix.Refresh"));
        SetText("SignSelectCertLabel", L("Msix.SelectCert"));
        SetText("CertSubjectLabel",  L("Msix.CertSubject"));
        SetText("CertIssuerLabel",   L("Msix.CertIssuer"));
        SetText("CertThumbLabel",    L("Msix.CertThumbprint"));
        SetText("CertValidLabel",    L("Msix.CertValidUntil"));
        SetText("NoCertsText",       L("Msix.NoCerts"));
        SetText("SignIconHeader",    L("Msix.SignIconHeader"));
        SetText("SignIconHint",      L("Msix.SignIconHint"));
        SetText("SignIconFileLabel", L("Msix.SignIconFileLabel"));
        SetBtn("BrowseSignIconBtn",  L("Msix.Browse"));
        SetBtn("ApplySignIconBtn",   L("Msix.ApplyIcon"));
        SetText("SignIconPreviewLabel", L("Msix.Preview"));
        SetText("SignOptionsHeader", L("Msix.SignOptionsHeader"));
        SetText("SignHashLabel",     L("Msix.HashAlgorithm"));
        SetText("SignHashHint",      L("Msix.HashHint"));

        // ── Build page — Sign after build ──
        SetText("BuildSignHeader",   L("Msix.SignAfterBuildHeader"));
        SetCheckBox("BuildSignAfterBuildCheck", L("Msix.SignAfterBuild"));
        SetText("BuildSignCertLabel", L("Msix.CertsHeader"));
        SetBtn("BuildRefreshCertsBtn", L("Msix.Refresh"));
        SetText("BuildSignHashLabel", L("Msix.HashAlgorithm"));

        // ── Footer ──
        SetText("StatusText",       L("Msix.Ready"));
        SetBtn("CancelBuildBtn",    L("Msix.Cancel"));
        SetBtn("CloseWindowBtn",    L("Msix.Close"));
        SetBtn("DoSignMsixBtn",     L("Msix.SignMsixBtn"));
        SetBtn("StartBuildBtn",     L("Msix.StartBuild"));
    }

    private void SetBtn(string name, string text)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Content = text;
    }


    private void SetCheckBox(string name, string text)
    {
        var cb = this.FindControl<CheckBox>(name);
        if (cb != null) cb.Content = text;
    }

    private void InitializeComponent() =>
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    // ─────────────────────────────────────────────────────────────
    //  Event wiring
    // ─────────────────────────────────────────────────────────────
    private void WireEvents()
    {
        // Title bar drag
        var tb = this.FindControl<Border>("TitleBar");
        if (tb != null) tb.PointerPressed += TitleBar_PointerPressed;

        // Close / cancel / start
        Bind("CloseBtn",       (Button b) => b.Click += (_, _) => Close());
        Bind("CloseWindowBtn", (Button b) => b.Click += (_, _) => Close());
        Bind("StartBuildBtn",  (Button b) => b.Click += StartBuild_Click);
        Bind("CancelBuildBtn", (Button b) => b.Click += (_, _) => { _service.Cancel(); SetBusy(false); });

        // Browse buttons
        Bind("BrowseProjectBtn",   (Button b) => b.Click += BrowseProject_Click);
        Bind("BrowseMsixOutputBtn",(Button b) => b.Click += BrowseMsixOutput_Click);
        Bind("BrowsePublishDirBtn",(Button b) => b.Click += BrowsePublishDir_Click);
        Bind("BrowseLogoBtn",      (Button b) => b.Click += BrowseLogo_Click);
        Bind("DetectExeBtn",       (Button b) => b.Click += DetectExe_Click);

        // Open MSIX page
        Bind("BrowseOpenMsixBtn",  (Button b) => b.Click += BrowseOpenMsix_Click);
        Bind("OpenMsixBtn",        (Button b) => b.Click += OpenMsix_Click);
        Bind("EditOpenedIdentityBtn",(Button b) => b.Click += (_, _) => { NavIdentity_Click(null, null!); FillIdentityFromLoaded(); });
        Bind("EditOpenedEntryBtn",  (Button b) => b.Click += (_, _) => { NavEntry_Click(null, null!); ScanLoadedMsixExes(); });
        Bind("ViewOpenedManifestBtn",(Button b) => b.Click += (_, _) => { NavManifest_Click(null, null!); FillManifestFromLoaded(); });

        // Identity page
        Bind("SaveIdentityBtn",    (Button b) => b.Click += SaveIdentity_Click);

        // Entry point page
        Bind("ScanExeBtn",         (Button b) => b.Click += (_, _) => ScanLoadedMsixExes());
        Bind("UseSelectedExeBtn",  (Button b) => b.Click += UseSelectedExe_Click);
        Bind("SaveEntryBtn",       (Button b) => b.Click += SaveEntry_Click);

        // Manifest page
        Bind("SaveManifestBtn",    (Button b) => b.Click += SaveManifest_Click);
        Bind("CopyManifestBtn",    (Button b) => b.Click += async (_, _) => await CopyToClipboardAsync(GetManifestText()));

        // Log page
        Bind("ClearLogBtn",        (Button b) => b.Click += (_, _) => { _logBuffer.Clear(); SetLog(""); });
        Bind("CopyLogBtn",         (Button b) => b.Click += async (_, _) => await CopyToClipboardAsync(_logBuffer.ToString()));
        Bind("OpenOutputFolderBtn",(Button b) => b.Click += OpenOutputFolder_Click);

        // Sign page
        Bind("BrowseSignMsixBtn", (Button b) => b.Click += BrowseSignMsix_Click);
        Bind("RefreshCertsBtn",   (Button b) => b.Click += (_, _) => _ = LoadUserCertificatesAsync());
        Bind("DoSignMsixBtn",     (Button b) => b.Click += DoSignMsix_Click);
        Bind("CertListBox",       (ListBox lb) => lb.SelectionChanged += CertListBox_SelectionChanged);

        // Sign page — icon
        Bind("BrowseSignIconBtn", (Button b) => b.Click += BrowseSignIcon_Click);
        Bind("ApplySignIconBtn",  (Button b) => b.Click += ApplySignIcon_Click);

        // Build page — sign after build
        Bind("BuildSignAfterBuildCheck", (CheckBox cb) => cb.IsCheckedChanged += BuildSignAfterBuild_Changed);
        Bind("BuildRefreshCertsBtn",     (Button b) => b.Click += (_, _) => _ = LoadBuildCertificatesAsync());
        Bind("BuildCertListBox",         (ListBox lb) => lb.SelectionChanged += BuildCertListBox_SelectionChanged);

        // Service events
        _service.OutputReceived  += (_, e) => Dispatcher.UIThread.Post(() => AppendLog(e.Output));
        _service.PackageStarted  += (_, _) => Dispatcher.UIThread.Post(() => SetBusy(true));
        _service.PackageCompleted += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetBusy(false);
                _lastOutputMsixPath = e.Result.OutputPath;
                var filename = IOPath.GetFileName(e.Result.OutputPath ?? "");
                SetStatus(e.Result.Success
                    ? $"✅ Done: {filename}"
                    : $"❌ Failed: {e.Result.ErrorMessage}",
                    e.Result.Success);
                ShowPage("output");
            });
        };
    }

    private void Bind<T>(string name, Action<T> setup) where T : Control
    {
        var c = this.FindControl<T>(name);
        if (c != null) setup(c);
    }

    // ─────────────────────────────────────────────────────────────
    //  Navigation
    // ─────────────────────────────────────────────────────────────
    internal void NavBuild_Click(object? s, RoutedEventArgs e)    => ShowPage("build");
    internal void NavOpen_Click(object? s, RoutedEventArgs e)     => ShowPage("open");
    internal void NavIdentity_Click(object? s, RoutedEventArgs e) => ShowPage("identity");
    internal void NavEntry_Click(object? s, RoutedEventArgs e)    => ShowPage("entry");
    internal void NavManifest_Click(object? s, RoutedEventArgs e) => ShowPage("manifest");
    internal void NavOutput_Click(object? s, RoutedEventArgs e)   => ShowPage("output");
    internal void NavSign_Click(object? s, RoutedEventArgs e)     => ShowPage("sign");

    private void ShowPage(string page)
    {
        string[] pageNames = { "PageBuild", "PageOpen", "PageIdentity", "PageEntry", "PageManifest", "PageOutput", "PageSign" };
        string[] navNames  = { "NavBuildBtn", "NavOpenBtn", "NavIdentityBtn", "NavEntryBtn", "NavManifestBtn", "NavOutputBtn", "NavSignBtn" };
        var pageMap = new Dictionary<string, string>
        {
            ["build"]    = "PageBuild",    ["open"]     = "PageOpen",
            ["identity"] = "PageIdentity", ["entry"]    = "PageEntry",
            ["manifest"] = "PageManifest", ["output"]   = "PageOutput",
            ["sign"]     = "PageSign",
        };
        var btnMap = new Dictionary<string, string>
        {
            ["build"]    = "NavBuildBtn",    ["open"]     = "NavOpenBtn",
            ["identity"] = "NavIdentityBtn", ["entry"]    = "NavEntryBtn",
            ["manifest"] = "NavManifestBtn", ["output"]   = "NavOutputBtn",
            ["sign"]     = "NavSignBtn",
        };

        foreach (var p in pageNames)
        {
            var ctrl = this.FindControl<Control>(p);
            if (ctrl != null) ctrl.IsVisible = pageMap[page] == p;
        }
        foreach (var nb in navNames)
        {
            var btn = this.FindControl<Button>(nb);
            if (btn == null) continue;
            if (btnMap[page] == nb) btn.Classes.Add("active");
            else btn.Classes.Remove("active");
        }

        var startBtn = this.FindControl<Button>("StartBuildBtn");
        var signBtn  = this.FindControl<Button>("DoSignMsixBtn");
        if (startBtn != null) startBtn.IsVisible = page == "build";
        if (signBtn  != null) signBtn.IsVisible  = page == "sign";

        if (page == "sign")
            _ = LoadUserCertificatesAsync();
    }

    // ─────────────────────────────────────────────────────────────
    //  Pre-fill from project path
    // ─────────────────────────────────────────────────────────────
    private void PreFillFromProject(string projectPath)
    {
        var projBox = this.FindControl<TextBox>("BuildProjectPathBox");
        if (projBox != null) projBox.Text = projectPath;

        var projectName = IOPath.GetFileNameWithoutExtension(projectPath);
        var projectDir  = IOPath.GetDirectoryName(projectPath) ?? "";

        SetText("BuildIdentityNameBox",         "com.company." + projectName.ToLowerInvariant().Replace(" ", ""));
        SetText("BuildVersionBox",              "1.0.0.0");
        SetText("BuildDisplayNameBox",          projectName);
        SetText("BuildPublisherBox",            "CN=Developer");
        SetText("BuildPublisherDisplayNameBox", "Developer");
        SetText("BuildDescriptionBox",          projectName + " application");
        SetText("BuildLogoBox",                 @"Assets\Square150x150Logo.png");
        SetText("BuildOutputPathBox",           IOPath.Combine(projectDir, "bin", "msix", projectName + ".msix"));

        _ = LoadFrameworksAsync(projectPath);
        _ = AutoDetectExeAsync(projectPath);
    }

    private async Task LoadFrameworksAsync(string projectPath)
    {
        var frameworks = await _pubService.GetProjectFrameworksAsync(projectPath);
        var combo = this.FindControl<ComboBox>("BuildFrameworkCombo");
        if (combo == null) return;
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = "(auto)" });
        foreach (var f in frameworks)
            combo.Items.Add(new ComboBoxItem { Content = f });
        combo.SelectedIndex = 0;
    }

    private async Task AutoDetectExeAsync(string projectPath)
    {
        var projectName = IOPath.GetFileNameWithoutExtension(projectPath);
        var exeCombo = this.FindControl<ComboBox>("BuildExeCombo");
        if (exeCombo == null) return;

        exeCombo.Items.Clear();
        exeCombo.Items.Add(new ComboBoxItem { Content = projectName + ".exe" });

        var projectDir = IOPath.GetDirectoryName(projectPath) ?? "";
        var candidates = new[]
        {
            IOPath.Combine(projectDir, "bin", "Release", projectName + ".exe"),
            IOPath.Combine(projectDir, "bin", "Debug",   projectName + ".exe"),
        };

        foreach (var c in candidates.Where(IOFile.Exists))
        {
            var name = IOPath.GetFileName(c);
            if (exeCombo.Items.Cast<ComboBoxItem>().All(i => i.Content?.ToString() != name))
                exeCombo.Items.Add(new ComboBoxItem { Content = name });
        }
        exeCombo.SelectedIndex = 0;
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────
    //  Build MSIX
    // ─────────────────────────────────────────────────────────────
    private async void StartBuild_Click(object? s, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var opts = BuildOptions();
        if (opts == null) return;

        // Determine publish dir
        var publishDir = opts.PublishOutputDir;
        if (string.IsNullOrWhiteSpace(publishDir))
            publishDir = IOPath.Combine(IOPath.GetDirectoryName(opts.ProjectPath)!, "bin", "msix_publish");

        // ── Step 1: Publish via PublishService + PublishProgressWindow ──
        var profile = new PublishProfile
        {
            Name              = $"MSIX Publish — {opts.PackageIdentityName}",
            ProjectPath       = opts.ProjectPath,
            Configuration     = opts.Configuration,
            RuntimeIdentifier = opts.RuntimeIdentifier,
            Framework         = opts.Framework,
            OutputPath        = publishDir,
            SelfContained     = opts.SelfContained,
            SingleFile        = opts.SingleFile,
            ReadyToRun        = opts.ReadyToRun,
            TrimUnusedAssemblies = opts.TrimUnusedAssemblies,
            CleanOutputFolder = opts.CleanOutputFolder,
        };

        var progressWindow = new PublishProgressWindow(_pubService, profile);
        progressWindow.Opened += (_, _) => progressWindow.StartPublish();
        await progressWindow.ShowDialog(this);

        // Check if publish succeeded
        if (progressWindow.PublishResult == null || !progressWindow.PublishResult.Success)
        {
            SetStatus("❌ Publish failed — MSIX build aborted.", false);
            return;
        }

        // ── Steps 2 + 3: Generate manifest + MakeAppx pack ──
        _logBuffer.Clear();
        SetLog("");
        ShowPage("output");
        SetStatus("Packaging MSIX…", null);
        opts.PublishOutputDir = publishDir;
        var packageResult = await _service.PackageFromPublishedAsync(opts, publishDir);

        // ── Step 4 (optional): Sign MSIX after build ──
        if (packageResult.Success && this.FindControl<CheckBox>("BuildSignAfterBuildCheck")?.IsChecked == true)
        {
            if (_buildSelectedCert == null)
            {
                SetStatus("⚠ MSIX built but not signed — no certificate selected.", false);
                return;
            }

            var msixPath = packageResult.OutputPath;
            if (!string.IsNullOrEmpty(msixPath) && IOFile.Exists(msixPath))
            {
                var hashCombo = this.FindControl<ComboBox>("BuildSignHashCombo");
                var hashAlgo  = (hashCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SHA256";

                SetStatus("Signing MSIX…", null);
                var signResult = await _service.SignMsixAsync(msixPath, _buildSelectedCert.Thumbprint, hashAlgo);

                if (!signResult.Success)
                {
                    SetStatus($"⚠ MSIX built but signing failed: {signResult.ErrorMessage}", false);
                }
            }
        }
    }

    private MsixPackageOptions? BuildOptions()
    {
        var projectPath = GetText("BuildProjectPathBox");
        if (string.IsNullOrWhiteSpace(projectPath) || !IOFile.Exists(projectPath))
        {
            SetStatus("❌ Please select a valid project file.", false);
            ShowPage("build");
            return null;
        }

        var version = GetText("BuildVersionBox")?.Trim() ?? "1.0.0.0";
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+\.\d+$"))
            version = "1.0.0.0";

        string? framework = null;
        var fwCombo = this.FindControl<ComboBox>("BuildFrameworkCombo");
        if (fwCombo?.SelectedItem is ComboBoxItem fwItem && fwItem.Content?.ToString() is { } fw && fw != "(auto)")
            framework = fw;

        var rtCombo = this.FindControl<ComboBox>("BuildRuntimeCombo");
        var runtime = (rtCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "win-x64";

        var cfgCombo = this.FindControl<ComboBox>("BuildConfigCombo");
        var config = (cfgCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Release";

        var exeCombo = this.FindControl<ComboBox>("BuildExeCombo");
        var exe = exeCombo?.SelectionBoxItem?.ToString()
               ?? (exeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString()
               ?? exeCombo?.Text;

        var epCombo = this.FindControl<ComboBox>("BuildEntryPointCombo");
        var ep = epCombo?.SelectionBoxItem?.ToString()
              ?? (epCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString()
              ?? epCombo?.Text
              ?? "Windows.FullTrustApplication";

        var singleFile = this.FindControl<CheckBox>("BuildSingleFileCheck")?.IsChecked == true;
        var r2r        = this.FindControl<CheckBox>("BuildReadyToRunCheck")?.IsChecked  != false;
        var trim       = this.FindControl<CheckBox>("BuildTrimCheck")?.IsChecked         == true;
        var cleanOut   = this.FindControl<CheckBox>("BuildCleanOutputCheck")?.IsChecked  != false;

        var msixOutput = GetText("BuildOutputPathBox")?.Trim();
        var publishDir = GetText("BuildPublishDirBox")?.Trim();
        if (publishDir == "(auto)" || string.IsNullOrWhiteSpace(publishDir)) publishDir = null;

        var identityName = GetText("BuildIdentityNameBox")?.Trim() ?? "MyApp";
        var arch = runtime.Contains("arm") ? "arm64" : runtime.Contains("x86") ? "x86" : "x64";

        return new MsixPackageOptions
        {
            ProjectPath           = projectPath,
            Configuration         = config,
            RuntimeIdentifier     = runtime,
            Framework             = framework,
            SelfContained         = true,    // MSIX requires self-contained publish
            SingleFile            = singleFile,
            ReadyToRun            = r2r,
            TrimUnusedAssemblies  = trim,
            CleanOutputFolder     = cleanOut,
            PublishOutputDir      = publishDir,
            PackageIdentityName   = identityName,
            Publisher             = GetText("BuildPublisherBox")?.Trim() ?? "CN=Developer",
            Version               = version,
            ProcessorArchitecture = arch,
            DisplayName           = GetText("BuildDisplayNameBox")?.Trim() ?? identityName,
            PublisherDisplayName  = GetText("BuildPublisherDisplayNameBox")?.Trim() ?? "Developer",
            Description           = GetText("BuildDescriptionBox")?.Trim() ?? "",
            LogoRelativePath      = GetText("BuildLogoBox")?.Trim() ?? @"Assets\Square150x150Logo.png",
            EntryExecutable       = exe,
            EntryPoint            = ep,
            OutputMsixPath        = msixOutput,
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Open MSIX
    // ─────────────────────────────────────────────────────────────
    private async void BrowseOpenMsix_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MSIX Package", AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MSIX / APPX") { Patterns = new[] { "*.msix", "*.appx" } },
                new FilePickerFileType("All Files")   { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count > 0) SetText("OpenMsixPathBox", files[0].Path.LocalPath);
    }

    private async void OpenMsix_Click(object? s, RoutedEventArgs e)
    {
        var path = GetText("OpenMsixPathBox")?.Trim() ?? "";
        if (!IOFile.Exists(path)) { SetStatus("❌ File not found.", false); return; }

        SetStatus("Opening package…", null);
        _loadedMsixPath = path;

        var info = await _service.OpenMsixAsync(path);
        if (info == null) { SetStatus("❌ Could not parse AppxManifest.", false); return; }

        _loadedPackage   = info;
        info.MsixPath    = path;
        info.Executables = MsixService.ListExecutablesInMsix(path);

        SetText("OpenedNameText",      info.Name);
        SetText("OpenedVersionText",   info.Version);
        SetText("OpenedPublisherText", info.Publisher);
        SetText("OpenedExeText",       info.Executable);

        var panel = this.FindControl<Border>("OpenedPackagePanel");
        if (panel != null) panel.IsVisible = true;

        SetStatus("✅ Loaded: " + IOPath.GetFileName(path), true);
    }

    // ─────────────────────────────────────────────────────────────
    //  Identity editor
    // ─────────────────────────────────────────────────────────────
    private void FillIdentityFromLoaded()
    {
        if (_loadedPackage == null) return;
        SetText("IdNameBox",             _loadedPackage.Name);
        SetText("IdVersionBox",          _loadedPackage.Version);
        SetText("IdPublisherBox",        _loadedPackage.Publisher);
        SetText("IdDisplayNameBox",      _loadedPackage.DisplayName);
        SetText("IdPublisherDisplayBox", _loadedPackage.PublisherDisplayName);
        SetText("IdDescriptionBox",      _loadedPackage.Description);
        SetText("IdLogoBox",             _loadedPackage.Logo);

        var archCombo = this.FindControl<ComboBox>("IdArchCombo");
        if (archCombo != null)
        {
            var arch = _loadedPackage.ProcessorArchitecture;
            foreach (var obj in archCombo.Items)
            {
                if (obj is ComboBoxItem item &&
                    item.Content?.ToString()?.Equals(arch, StringComparison.OrdinalIgnoreCase) == true)
                { archCombo.SelectedItem = item; break; }
            }
        }
    }

    private async void SaveIdentity_Click(object? s, RoutedEventArgs e)
    {
        if (_loadedPackage == null) { SetStatus("No MSIX loaded. Open one first.", false); return; }
        if (_isBusy) return;

        _loadedPackage.Name                 = GetText("IdNameBox")             ?? _loadedPackage.Name;
        _loadedPackage.Version              = GetText("IdVersionBox")           ?? _loadedPackage.Version;
        _loadedPackage.Publisher            = GetText("IdPublisherBox")         ?? _loadedPackage.Publisher;
        _loadedPackage.DisplayName          = GetText("IdDisplayNameBox")       ?? _loadedPackage.DisplayName;
        _loadedPackage.PublisherDisplayName = GetText("IdPublisherDisplayBox")  ?? _loadedPackage.PublisherDisplayName;
        _loadedPackage.Description          = GetText("IdDescriptionBox")       ?? _loadedPackage.Description;
        _loadedPackage.Logo                 = GetText("IdLogoBox")              ?? _loadedPackage.Logo;

        var archCombo = this.FindControl<ComboBox>("IdArchCombo");
        if (archCombo?.SelectedItem is ComboBoxItem ai)
            _loadedPackage.ProcessorArchitecture = ai.Content?.ToString() ?? "x64";

        SetStatus("Saving…", null);
        SetBusy(true);
        var result = await _service.SaveMsixMetadataAsync(_loadedPackage);
        SetBusy(false);
        SetStatus(result.Success ? "✅ Identity saved." : ("❌ " + result.ErrorMessage), result.Success);
    }

    // ─────────────────────────────────────────────────────────────
    //  Entry point editor
    // ─────────────────────────────────────────────────────────────
    private void ScanLoadedMsixExes()
    {
        var exes = _loadedMsixPath != null
            ? MsixService.ListExecutablesInMsix(_loadedMsixPath)
            : new List<string>();
        var lb = this.FindControl<ListBox>("ExeListBox");
        if (lb != null) lb.ItemsSource = exes;
    }

    private void UseSelectedExe_Click(object? s, RoutedEventArgs e)
    {
        var lb = this.FindControl<ListBox>("ExeListBox");
        if (lb?.SelectedItem is string exe) SetText("EntryExeBox", exe);
    }

    private async void SaveEntry_Click(object? s, RoutedEventArgs e)
    {
        if (_loadedPackage == null) { SetStatus("No MSIX loaded.", false); return; }
        if (_isBusy) return;

        _loadedPackage.Executable = GetText("EntryExeBox") ?? _loadedPackage.Executable;

        var epCombo = this.FindControl<ComboBox>("EntryPointClassCombo");
        _loadedPackage.EntryPoint = epCombo?.SelectionBoxItem?.ToString()
                                 ?? (epCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString()
                                 ?? epCombo?.Text
                                 ?? _loadedPackage.EntryPoint;

        SetStatus("Saving entry point…", null);
        SetBusy(true);
        var result = await _service.SaveMsixMetadataAsync(_loadedPackage);
        SetBusy(false);
        SetStatus(result.Success ? "✅ Entry point saved." : ("❌ " + result.ErrorMessage), result.Success);
    }

    // ─────────────────────────────────────────────────────────────
    //  Manifest editor
    // ─────────────────────────────────────────────────────────────
    private void FillManifestFromLoaded()
    {
        if (_loadedPackage != null) SetText("ManifestXmlBox", _loadedPackage.ManifestXml);
    }

    private string GetManifestText() => this.FindControl<TextBox>("ManifestXmlBox")?.Text ?? "";

    private async void SaveManifest_Click(object? s, RoutedEventArgs e)
    {
        if (_loadedMsixPath == null || _loadedPackage == null) { SetStatus("No MSIX loaded.", false); return; }
        var xml = GetManifestText();
        if (string.IsNullOrWhiteSpace(xml)) return;

        _loadedPackage.ManifestXml = xml;
        SetStatus("Saving manifest…", null);
        SetBusy(true);
        var result = await _service.SaveMsixMetadataAsync(_loadedPackage);
        SetBusy(false);
        SetStatus(result.Success ? "✅ Manifest saved." : ("❌ " + result.ErrorMessage), result.Success);
    }

    // ─────────────────────────────────────────────────────────────
    //  Browse helpers
    // ─────────────────────────────────────────────────────────────
    private async void BrowseProject_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Project File", AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Project Files") { Patterns = new[] { "*.csproj", "*.fsproj", "*.vbproj" } },
                new FilePickerFileType("All Files")     { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        SetText("BuildProjectPathBox", path);
        PreFillFromProject(path);
    }

    private async void BrowseMsixOutput_Click(object? s, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save MSIX As", DefaultExtension = "msix",
            FileTypeChoices = new[] { new FilePickerFileType("MSIX Package") { Patterns = new[] { "*.msix" } } }
        });
        if (file != null) SetText("BuildOutputPathBox", file.Path.LocalPath);
    }

    private async void BrowsePublishDir_Click(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Select Publish Folder", AllowMultiple = false });
        if (folders.Count > 0) SetText("BuildPublishDirBox", folders[0].Path.LocalPath);
    }

    private async void BrowseLogo_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Logo (PNG only — MSIX requirement)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG Images") { Patterns = new[] { "*.png" } },
            }
        });
        if (files.Count > 0) SetText("BuildLogoBox", files[0].Path.LocalPath);
    }

    private async void DetectExe_Click(object? s, RoutedEventArgs e)
    {
        var pp = GetText("BuildProjectPathBox")?.Trim();
        if (!string.IsNullOrEmpty(pp)) await AutoDetectExeAsync(pp);
    }

    private void OpenOutputFolder_Click(object? s, RoutedEventArgs e)
    {
        var path = _lastOutputMsixPath;
        if (string.IsNullOrEmpty(path)) return;
        var dir = IOFile.Exists(path) ? IOPath.GetDirectoryName(path) : path;
        if (!string.IsNullOrEmpty(dir) && IODirectory.Exists(dir))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", dir); }
            catch (Exception) { /* ignore */ }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Busy / status helpers
    // ─────────────────────────────────────────────────────────────
    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        var prog   = this.FindControl<ProgressBar>("BuildProgress");
        var cancel = this.FindControl<Button>("CancelBuildBtn");
        var start  = this.FindControl<Button>("StartBuildBtn");
        if (prog   != null) prog.IsVisible   = busy;
        if (cancel != null) cancel.IsVisible  = busy;
        if (start  != null) start.IsEnabled   = !busy;
    }

    private void SetStatus(string text, bool? success)
    {
        var tb  = this.FindControl<TextBlock>("StatusText");
        var dot = this.FindControl<Ellipse>("StatusIndicator");
        if (tb  != null) tb.Text = text;
        if (dot != null) dot.Fill = success switch
        {
            true  => new SolidColorBrush(Color.Parse("#FFA6E3A1")),
            false => new SolidColorBrush(Color.Parse("#FFF38BA8")),
            null  => new SolidColorBrush(Color.Parse("#FFFFC09F")),
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Log helpers
    // ─────────────────────────────────────────────────────────────
    private void AppendLog(string text)
    {
        _logBuffer.Append(text);
        SetLog(_logBuffer.ToString());
        this.FindControl<ScrollViewer>("LogScrollViewer")?.ScrollToEnd();
    }

    private void SetLog(string text)
    {
        var box = this.FindControl<TextBox>("BuildLogBox");
        if (box != null) box.Text = text;
    }

    // ─────────────────────────────────────────────────────────────
    //  UI helpers
    // ─────────────────────────────────────────────────────────────
    private void SetText(string name, string? value)
    {
        if (value == null) return;
        // Try TextBox first (input fields)
        var ctrl = this.FindControl<Control>(name);
        if (ctrl is TextBox tb)        { tb.Text  = value; return; }
        if (ctrl is TextBlock tbl)     { tbl.Text = value; }
    }

    private string? GetText(string name) => this.FindControl<TextBox>(name)?.Text;

    private async Task CopyToClipboardAsync(string? text)
    {
        if (text == null) return;
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(text);
        }
        catch (Exception) { /* clipboard unavailable */ }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ─────────────────────────────────────────────────────────────
    //  MSIX Signing
    // ─────────────────────────────────────────────────────────────

    private async Task LoadUserCertificatesAsync()
    {
        _userCerts.Clear();
        var noCertsText = this.FindControl<TextBlock>("NoCertsText");
        var listBox     = this.FindControl<ListBox>("CertListBox");

        try
        {
            var certs = await Task.Run(() =>
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser, OpenFlags.ReadOnly);
                return store.Certificates
                    .Cast<X509Certificate2>()
                    .Where(c => c.HasPrivateKey)
                    .Select(c => new CertificateViewModel(c))
                    .ToList();
            });

            _userCerts = certs;
            if (listBox != null) listBox.ItemsSource = _userCerts;

            if (noCertsText != null) noCertsText.IsVisible = _userCerts.Count == 0;
        }
        catch (Exception ex)
        {
            if (noCertsText != null)
            {
                noCertsText.Text = $"⚠️  Could not read certificate store: {ex.Message}";
                noCertsText.IsVisible = true;
            }
        }

        // Clear selection info
        UpdateCertInfo(null);
    }

    private void CertListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedCert = (sender as ListBox)?.SelectedItem as CertificateViewModel;
        UpdateCertInfo(_selectedCert);
    }

    private void UpdateCertInfo(CertificateViewModel? cert)
    {
        var panel = this.FindControl<Border>("SelectedCertInfoPanel");
        if (panel != null) panel.IsVisible = cert != null;
        if (cert == null) return;

        SetText("CertSubjectText",    cert.SubjectName);
        SetText("CertIssuerText",     cert.IssuerName);
        SetText("CertThumbprintText", cert.Thumbprint);
        SetText("CertExpiryText",     cert.ExpiryDisplay);
    }

    private async void BrowseSignMsix_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select MSIX to Sign", AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MSIX Package") { Patterns = new[] { "*.msix", "*.appx" } },
                new FilePickerFileType("All Files")    { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count > 0) SetText("SignMsixPathBox", files[0].Path.LocalPath);
    }

    private async void DoSignMsix_Click(object? s, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var msixPath = GetText("SignMsixPathBox")?.Trim();
        if (string.IsNullOrEmpty(msixPath) || !IOFile.Exists(msixPath))
        {
            SetStatus("❌ Please select a valid .msix file.", false);
            return;
        }

        if (_selectedCert == null)
        {
            SetStatus("❌ Please select a certificate from the list.", false);
            return;
        }

        var hashCombo = this.FindControl<ComboBox>("SignHashAlgoCombo");
        var hashAlgo  = (hashCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SHA256";

        SetBusy(true);
        SetStatus("Signing…", null);
        _logBuffer.Clear();
        ShowPage("output");

        var result = await _service.SignMsixAsync(msixPath, _selectedCert.Thumbprint, hashAlgo);

        SetBusy(false);
        SetStatus(result.Success ? $"✅ Signed: {IOPath.GetFileName(msixPath)}"
                                 : $"❌ {result.ErrorMessage}", result.Success);
    }

    // ─────────────────────────────────────────────────────────────
    //  Sign page — Icon management
    // ─────────────────────────────────────────────────────────────

    private async void BrowseSignIcon_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Icon (PNG only — MSIX requirement)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG Images") { Patterns = new[] { "*.png" } },
            }
        });
        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            SetText("SignIconPathBox", path);

            // Show preview
            try
            {
                var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
                var preview = this.FindControl<Image>("SignIconPreview");
                if (preview != null) preview.Source = bitmap;
                var panel = this.FindControl<StackPanel>("SignIconPreviewPanel");
                if (panel != null) panel.IsVisible = true;
            }
            catch (Exception)
            {
                var panel = this.FindControl<StackPanel>("SignIconPreviewPanel");
                if (panel != null) panel.IsVisible = false;
            }
        }
    }

    private async void ApplySignIcon_Click(object? s, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var msixPath = GetText("SignMsixPathBox")?.Trim();
        if (string.IsNullOrEmpty(msixPath) || !IOFile.Exists(msixPath))
        {
            SetStatus("❌ Please select a valid .msix file first.", false);
            return;
        }

        var iconPath = GetText("SignIconPathBox")?.Trim();
        if (string.IsNullOrEmpty(iconPath) || !IOFile.Exists(iconPath))
        {
            SetStatus("❌ Please select a valid .png icon file.", false);
            return;
        }

        if (!iconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("❌ MSIX only supports PNG icons. Please select a .png file.", false);
            return;
        }

        SetBusy(true);
        SetStatus("Applying icon to MSIX…", null);
        _logBuffer.Clear();
        ShowPage("output");

        var result = await _service.ApplyIconToMsixAsync(msixPath, iconPath);

        SetBusy(false);
        SetStatus(result.Success
            ? $"✅ Icon applied to: {IOPath.GetFileName(msixPath)}"
            : $"❌ {result.ErrorMessage}", result.Success);
    }

    // ─────────────────────────────────────────────────────────────
    //  Build page — Sign after build
    // ─────────────────────────────────────────────────────────────

    private void BuildSignAfterBuild_Changed(object? s, RoutedEventArgs e)
    {
        var isChecked = this.FindControl<CheckBox>("BuildSignAfterBuildCheck")?.IsChecked == true;
        var panel = this.FindControl<StackPanel>("BuildSignOptionsPanel");
        if (panel != null) panel.IsVisible = isChecked;

        if (isChecked)
            _ = LoadBuildCertificatesAsync();
    }

    private async Task LoadBuildCertificatesAsync()
    {
        _buildCerts.Clear();
        var listBox = this.FindControl<ListBox>("BuildCertListBox");

        try
        {
            var certs = await Task.Run(() =>
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser, OpenFlags.ReadOnly);
                return store.Certificates
                    .Cast<X509Certificate2>()
                    .Where(c => c.HasPrivateKey)
                    .Select(c => new CertificateViewModel(c))
                    .ToList();
            });

            _buildCerts = certs;
            if (listBox != null) listBox.ItemsSource = _buildCerts;
        }
        catch (Exception)
        {
            // silently fail
        }
    }

    private void BuildCertListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _buildSelectedCert = (sender as ListBox)?.SelectedItem as CertificateViewModel;
    }
}

/// <summary>View model for a certificate entry in the cert list.</summary>
public sealed class CertificateViewModel
{
    private readonly X509Certificate2 _cert;

    public CertificateViewModel(X509Certificate2 cert)
    {
        _cert = cert;
        SubjectName  = SimplifyDN(cert.SubjectName.Name);
        IssuerName   = SimplifyDN(cert.IssuerName.Name);
        Thumbprint   = cert.Thumbprint;
        NotAfter     = cert.NotAfter;

        var daysLeft = (NotAfter - DateTime.Now).TotalDays;
        ExpiryDisplay = daysLeft < 0
            ? $"Expired {-daysLeft:0}d ago"
            : $"Expires {NotAfter:yyyy-MM-dd} ({daysLeft:0}d)";
        ExpiryColor = daysLeft < 0 ? "#FFEF5350"
                    : daysLeft < 30 ? "#FFBB9A6F"
                    : "#FF9E90B0";
    }

    public string SubjectName  { get; }
    public string IssuerName   { get; }
    public string Thumbprint   { get; }
    public DateTime NotAfter   { get; }
    public string ExpiryDisplay { get; }
    public string ExpiryColor  { get; }

    /// <summary>Gets the underlying certificate (for signing).</summary>
    internal X509Certificate2 Certificate => _cert;

    private static string SimplifyDN(string dn)
    {
        // Extract CN= value for brevity
        foreach (var part in dn.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..].Trim('"');
        }
        return dn;
    }
}

