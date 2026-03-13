using Avalonia.Controls;
using Insait_Edit_C_Sharp.Controls.ProjectProps;
using System.IO;
using System.Linq;

namespace Insait_Edit_C_Sharp;

public partial class SolutionPropertiesWindow : Window
{
    private readonly string _solutionPath;
    private readonly string _solutionDir;
    private readonly SolutionProjectsPage _projectsPage = new();

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
        // Title bar drag + close
        var tb = this.FindControl<Border>("TitleBar")!;
        tb.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        this.FindControl<Button>("CloseFooterButton")!.Click += (_, _) => Close();

        // Title bar text
        var sln = Path.GetFileName(_solutionPath);
        if (this.FindControl<TextBlock>("TitleText") is { } t)   t.Text = $"Solution Properties — {sln}";
        if (this.FindControl<TextBlock>("SubTitleText") is { } st) st.Text = _solutionDir;

        // Place Projects page directly into the host panel
        var host = this.FindControl<Panel>("PageHost")!;
        host.Children.Add(_projectsPage);
    }

    private void LoadSolution()
    {
        if (!File.Exists(_solutionPath)) return;
        var lines = File.ReadAllLines(_solutionPath).ToList();
        _projectsPage.Populate(lines, _solutionDir);
    }
}