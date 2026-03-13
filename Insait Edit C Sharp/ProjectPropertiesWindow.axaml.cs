using Avalonia.Controls;
using Insait_Edit_C_Sharp.Controls.ProjectProps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp;

public partial class ProjectPropertiesWindow : Window
{
    private readonly string _projectPath;

    // Supported MSBuild project extensions
    private static readonly HashSet<string> MsBuildExts =
        new(StringComparer.OrdinalIgnoreCase)
        { ".csproj", ".fsproj", ".vbproj", ".nfproj" };

    // Project kind derived from extension
    private enum ProjectKind { CSharp, FSharp, VisualBasic, NanoFramework, Unknown }
    private ProjectKind _projectKind = ProjectKind.Unknown;

    // Pages
    private readonly GeneralPage _generalPage = new();
    private readonly BuildPage   _buildPage   = new();
    private readonly DebugPage   _debugPage   = new();
    private readonly PackagePage _packagePage = new();
    private readonly SigningPage _signingPage = new();

    private record NavEntry(string BtnName, string Title, Control Page);
    private List<NavEntry> _navMap = new();
    private NavEntry? _activePage;

    // Track the TargetFramework at load time so we can detect changes
    private string? _originalTargetFramework;

    public ProjectPropertiesWindow() { InitializeComponent(); _projectPath = ""; }

    public ProjectPropertiesWindow(string projectPath) : this()
    {
        _projectPath = projectPath;
        _projectKind = DetectKind(projectPath);
        SetupUI();
        LoadProject();
    }

    // ── project kind ─────────────────────────────────────────────────────────

    private static ProjectKind DetectKind(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csproj"  => ProjectKind.CSharp,
            ".fsproj"  => ProjectKind.FSharp,
            ".vbproj"  => ProjectKind.VisualBasic,
            ".nfproj"  => ProjectKind.NanoFramework,
            _          => ProjectKind.Unknown,
        };

    private static bool IsMsBuildProject(string path) =>
        MsBuildExts.Contains(Path.GetExtension(path));

    // ── setup ────────────────────────────────────────────────────────────────

    private void SetupUI()
    {
        SetupTitleBar();
        SetupNav();
        SetupSearch();
        SetupFooter();
    }

    private void SetupTitleBar()
    {
        var tb = this.FindControl<Border>("TitleBar")!;
        tb.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();

        var projName = Path.GetFileNameWithoutExtension(_projectPath);
        var projDir  = Path.GetDirectoryName(_projectPath) ?? _projectPath;
        if (this.FindControl<TextBlock>("TitleText")   is { } t)  t.Text  = $"Project Properties — {projName}";
        if (this.FindControl<TextBlock>("SubTitleText") is { } st) st.Text = projDir;
    }

    private void SetupNav()
    {
        _navMap = new List<NavEntry>
        {
            new("NavGeneral",  "General",        _generalPage),
            new("NavBuild",    "Build",           _buildPage),
            new("NavDebug",    "Debug",           _debugPage),
            new("NavPackage",  "Package / NuGet", _packagePage),
            new("NavSigning",  "Signing",         _signingPage),
        };

        // Hide pages that don't apply to the current project type
        ApplyNavVisibility();

        foreach (var entry in _navMap)
        {
            var btn = this.FindControl<Button>(entry.BtnName);
            if (btn != null)
                btn.Click += (_, _) => ActivatePage(entry);
        }

        // Activate the first visible page
        var first = _navMap.FirstOrDefault(e => this.FindControl<Button>(e.BtnName)?.IsVisible != false);
        if (first != null) ActivatePage(first);
    }

    private void ApplyNavVisibility()
    {
        // For non-MSBuild projects only General page makes sense;
        // F# / VB / nanoFramework hide package-only features.
        bool isMsBuild = IsMsBuildProject(_projectPath);

        void SetVisible(string btnName, bool visible)
        {
            if (this.FindControl<Button>(btnName) is { } btn) btn.IsVisible = visible;
        }

        // All MSBuild projects: all pages shown.
        // Unknown: only show General (or nothing useful — show a disabled nav).
        SetVisible("NavBuild",   isMsBuild);
        SetVisible("NavDebug",   isMsBuild);
        SetVisible("NavPackage", _projectKind is ProjectKind.CSharp or ProjectKind.FSharp or ProjectKind.VisualBasic);
        SetVisible("NavSigning", _projectKind is ProjectKind.CSharp or ProjectKind.FSharp or ProjectKind.VisualBasic);
    }

    private void ActivatePage(NavEntry entry)
    {
        var host       = this.FindControl<Panel>("PageHost")!;
        var titleBlock = this.FindControl<TextBlock>("PageTitle")!;

        if (_activePage != null)
        {
            _activePage.Page.IsVisible = false;
            var oldBtn = this.FindControl<Button>(_activePage.BtnName);
            oldBtn?.Classes.Remove("active");
        }

        if (!host.Children.Contains(entry.Page))
            host.Children.Add(entry.Page);

        entry.Page.IsVisible = true;
        var btn = this.FindControl<Button>(entry.BtnName);
        if (btn != null && !btn.Classes.Contains("active")) btn.Classes.Add("active");
        titleBlock.Text = entry.Title;
        _activePage = entry;
    }

    private void SetupSearch()
    {
        var box = this.FindControl<TextBox>("NavSearchBox");
        if (box == null) return;
        box.TextChanged += (_, _) => FilterNav(box.Text ?? "");
    }

    private void FilterNav(string query)
    {
        query = query.Trim().ToLowerInvariant();
        foreach (var entry in _navMap)
        {
            var btn = this.FindControl<Button>(entry.BtnName);
            if (btn == null || !btn.IsVisible) continue;
            btn.IsVisible = string.IsNullOrEmpty(query) || entry.Title.ToLowerInvariant().Contains(query);
        }
    }

    private void SetupFooter()
    {
        this.FindControl<Button>("ApplyButton")!.Click  += (_, _) => _ = ApplyChangesAsync();
        this.FindControl<Button>("OkButton")!.Click     += (_, _) => { _ = ApplyChangesAsync(); Close(); };
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    // ── load / save ──────────────────────────────────────────────────────────

    private void LoadProject()
    {
        if (!File.Exists(_projectPath)) return;

        try
        {
            if (IsMsBuildProject(_projectPath))
            {
                var doc = XDocument.Load(_projectPath);
                var ns  = doc.Root?.Name.Namespace ?? XNamespace.None;
                var pg  = doc.Root?.Elements(ns + "PropertyGroup").FirstOrDefault();

                _originalTargetFramework = pg?.Element("TargetFramework")?.Value?.Trim()
                                        ?? pg?.Element("TargetFrameworks")?.Value?.Trim();

                _generalPage.Populate(pg, _projectPath);
                _buildPage.Populate(pg);
                _debugPage.Populate();
                _packagePage.Populate(pg, _projectPath);
                _signingPage.Populate(pg);
            }
            else
            {
                // Non-MSBuild project — show path info in status
                SetStatus($"ℹ  {Path.GetFileName(_projectPath)} — basic view (non-MSBuild project)");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load project: {ex.Message}");
        }
    }

    private async Task ApplyChangesAsync()
    {
        if (!File.Exists(_projectPath)) return;
        if (!IsMsBuildProject(_projectPath))
        {
            SetStatus("ℹ  Saving is not supported for this project type");
            return;
        }

        try
        {
            var doc = XDocument.Load(_projectPath);
            var ns  = doc.Root?.Name.Namespace ?? XNamespace.None;
            var pg  = doc.Root?.Elements(ns + "PropertyGroup").FirstOrDefault();

            if (pg == null)
            {
                pg = new XElement(ns + "PropertyGroup");
                doc.Root!.AddFirst(pg);
            }

            _generalPage.Apply(pg);
            _buildPage.Apply(pg);
            _packagePage.Apply(pg);
            _signingPage.Apply(pg);

            doc.Save(_projectPath);
            SetStatus($"✔  Saved at {DateTime.Now:HH:mm:ss}");

            // If TargetFramework changed, run dotnet restore automatically
            var newTf = pg.Element("TargetFramework")?.Value?.Trim()
                     ?? pg.Element("TargetFrameworks")?.Value?.Trim();

            if (!string.IsNullOrEmpty(newTf) &&
                !string.Equals(newTf, _originalTargetFramework, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"✔  Saved — running dotnet restore for {newTf}…");
                await RunDotnetRestoreAsync();
                _originalTargetFramework = newTf;
                SetStatus($"✔  Restore complete at {DateTime.Now:HH:mm:ss}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"❌  {ex.Message}");
        }
    }

    private async Task RunDotnetRestoreAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "dotnet",
            Arguments              = $"restore \"{_projectPath}\"",
            WorkingDirectory       = Path.GetDirectoryName(_projectPath) ?? "",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc != null) await proc.WaitForExitAsync();
        }
        catch
        {
            // Restore failure is non-fatal — the user can run it manually
        }
    }

    private void SetStatus(string msg)
    {
        if (this.FindControl<TextBlock>("StatusLabel") is { } lbl)
            lbl.Text = msg;
    }
}