using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Controls.ProjectProps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp;

public partial class ProjectPropertiesWindow : Window
{
    private readonly string _projectPath;
    private readonly string _projectDir;

    // Pages
    private readonly GeneralPage _generalPage = new();
    private readonly BuildPage _buildPage = new();
    private readonly PackagePage _packagePage = new();
    private readonly DebugPage _debugPage = new();
    private readonly SigningPage _signingPage = new();

    private record NavEntry(string BtnName, string Title, Control Page);
    private List<NavEntry> _navMap = new();
    private NavEntry? _activePage;

    public ProjectPropertiesWindow()
    {
        InitializeComponent();
        _projectPath = "";
        _projectDir = "";
    }
    public ProjectPropertiesWindow(string projectPath) : this()
    {
        _projectPath = projectPath;
        _projectDir = Path.GetDirectoryName(projectPath) ?? projectPath;
        SetupUI();
        LoadProject();
    }

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
        tb.PointerPressed += (s, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();

        var proj = Path.GetFileName(_projectPath);
        if (this.FindControl<TextBlock>("TitleText") is { } t) t.Text = $"Project Properties — {proj}";
        if (this.FindControl<TextBlock>("SubTitleText") is { } st) st.Text = _projectDir;
    }

    private void SetupNav()
    {
        _navMap = new List<NavEntry>
        {
            new("NavGeneral", "General",         _generalPage),
            new("NavBuild",   "Build",           _buildPage),
            new("NavPackage", "Package / NuGet", _packagePage),
            new("NavDebug",   "Debug",           _debugPage),
            new("NavSigning", "Signing",         _signingPage),
        };

        foreach (var entry in _navMap)
        {
            var btn = this.FindControl<Button>(entry.BtnName)!;
            btn.Click += (_, _) => ActivatePage(entry);
        }

        ActivatePage(_navMap[0]);
    }

    private void ActivatePage(NavEntry entry)
    {
        var host = this.FindControl<Panel>("PageHost")!;
        var titleBlock = this.FindControl<TextBlock>("PageTitle")!;

        if (_activePage != null)
        {
            _activePage.Page.IsVisible = false;
            var oldBtn = this.FindControl<Button>(_activePage.BtnName)!;
            oldBtn.Classes.Remove("active");
        }

        if (!host.Children.Contains(entry.Page))
            host.Children.Add(entry.Page);

        entry.Page.IsVisible = true;
        var btn = this.FindControl<Button>(entry.BtnName)!;
        if (!btn.Classes.Contains("active")) btn.Classes.Add("active");
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
            if (btn == null) continue;
            btn.IsVisible = string.IsNullOrEmpty(query) || entry.Title.ToLowerInvariant().Contains(query);
        }
    }

    private void SetupFooter()
    {
        this.FindControl<Button>("ApplyButton")!.Click  += (_, _) => ApplyChanges();
        this.FindControl<Button>("OkButton")!.Click     += (_, _) => { ApplyChanges(); Close(); };
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
    }

    private void LoadProject()
    {
        if (!File.Exists(_projectPath)) return;

        XElement? pg = null;
        try
        {
            var doc = XDocument.Load(_projectPath);
            pg = doc.Root?.Elements("PropertyGroup").FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (this.FindControl<TextBlock>("StatusLabel") is { } lbl)
                lbl.Text = $"Could not load project: {ex.Message}";
        }

        _generalPage.Populate(pg, _projectPath);
        _buildPage.Populate(pg);
        _packagePage.Populate(pg, _projectPath);
        _debugPage.Populate();
        _signingPage.Populate(pg);
    }

    private void ApplyChanges()
    {
        if (!File.Exists(_projectPath)) return;

        try
        {
            var doc = XDocument.Load(_projectPath);
            var pg = doc.Root?.Elements("PropertyGroup").FirstOrDefault();
            if (pg == null)
            {
                pg = new XElement("PropertyGroup");
                doc.Root?.AddFirst(pg);
            }

            _generalPage.Apply(pg);
            _buildPage.Apply(pg);
            _packagePage.Apply(pg);
            _signingPage.Apply(pg);

            doc.Save(_projectPath);

            if (this.FindControl<TextBlock>("StatusLabel") is { } lbl)
                lbl.Text = $"Saved at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            if (this.FindControl<TextBlock>("StatusLabel") is { } lbl)
                lbl.Text = $"Error: {ex.Message}";
        }
    }
}
