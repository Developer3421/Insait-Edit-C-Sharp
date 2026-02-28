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

    private void ShowPage(string page)
    {
        string[] pageNames = { "PageBuild", "PageOpen", "PageIdentity", "PageEntry", "PageManifest", "PageOutput" };
        string[] navNames  = { "NavBuildBtn", "NavOpenBtn", "NavIdentityBtn", "NavEntryBtn", "NavManifestBtn", "NavOutputBtn" };
        var pageMap = new Dictionary<string, string>
        {
            ["build"] = "PageBuild", ["open"] = "PageOpen", ["identity"] = "PageIdentity",
            ["entry"] = "PageEntry", ["manifest"] = "PageManifest", ["output"] = "PageOutput",
        };
        var btnMap = new Dictionary<string, string>
        {
            ["build"] = "NavBuildBtn", ["open"] = "NavOpenBtn", ["identity"] = "NavIdentityBtn",
            ["entry"] = "NavEntryBtn", ["manifest"] = "NavManifestBtn", ["output"] = "NavOutputBtn",
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
        if (startBtn != null) startBtn.IsVisible = page == "build";
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
        _logBuffer.Clear();
        SetLog("");
        ShowPage("output");
        SetStatus("Building…", null);
        await _service.PackageAsync(opts);
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
            SingleFile            = singleFile,
            ReadyToRun            = r2r,
            TrimUnusedAssemblies  = trim,
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
}

