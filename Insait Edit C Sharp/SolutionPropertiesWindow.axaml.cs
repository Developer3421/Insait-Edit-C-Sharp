using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Controls.ProjectProps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Insait_Edit_C_Sharp;

public partial class SolutionPropertiesWindow : Window
{
    private readonly string _solutionPath;
    private readonly string _solutionDir;

    // Pages
    private readonly SolutionGeneralPage _generalPage = new();
    private readonly SolutionBuildCfgPage _buildCfgPage = new();
    private readonly SolutionProjectsPage _projectsPage = new();

    private record NavEntry(string BtnName, string Title, Control Page);
    private List<NavEntry> _navMap = new();
    private NavEntry? _activePage;

    public SolutionPropertiesWindow() { InitializeComponent(); _solutionPath = ""; _solutionDir = ""; }
    public SolutionPropertiesWindow(string solutionPath) : this()
    {
        _solutionPath = solutionPath;
        _solutionDir = Path.GetDirectoryName(solutionPath) ?? solutionPath;
        SetupUI();
        LoadSolution();
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

        var sln = Path.GetFileName(_solutionPath);
        if (this.FindControl<TextBlock>("TitleText") is { } t) t.Text = $"Solution Properties — {sln}";
        if (this.FindControl<TextBlock>("SubTitleText") is { } st) st.Text = $"{_solutionDir}";
    }

    private void SetupNav()
    {
        _navMap = new List<NavEntry>
        {
            new("NavGeneral",  "General",                 _generalPage),
            new("NavBuildCfg", "Build Configurations",    _buildCfgPage),
            new("NavProjects", "Projects",                _projectsPage),
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
        this.FindControl<Button>("CloseFooterButton")!.Click += (_, _) => Close();
    }

    private void LoadSolution()
    {
        if (!File.Exists(_solutionPath)) return;
        var lines = File.ReadAllLines(_solutionPath).ToList();
        _generalPage.Populate(_solutionPath, lines);
        _buildCfgPage.Populate(lines);
        _projectsPage.Populate(lines, _solutionDir);
    }
}