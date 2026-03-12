using Avalonia.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class SolutionBuildCfgPage : UserControl
{
    public SolutionBuildCfgPage() { InitializeComponent(); }

    public void Populate(List<string> lines)
    {
        var inSection = false;
        var cfgs = new List<string>();
        foreach (var raw in lines)
        {
            var l = raw.Trim();
            if (l.Contains("GlobalSection(SolutionConfigurationPlatforms)")) { inSection = true; continue; }
            if (inSection && l.StartsWith("EndGlobalSection")) break;
            if (inSection && l.Contains("="))
                cfgs.Add(l.Split('=')[0].Trim());
        }
        if (this.FindControl<ItemsControl>("CfgList") is { } ic)
            ic.ItemsSource = cfgs;
    }
}