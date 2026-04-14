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

    // Page
    private readonly GeneralPage _generalPage = new();

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
        SetupContent();
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

    private void SetupContent()
    {
        var host = this.FindControl<Panel>("PageHost")!;
        host.Children.Add(_generalPage);

        if (this.FindControl<TextBlock>("PageTitle") is { } pt)
            pt.Text = "General";
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
