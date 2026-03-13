using Avalonia.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public record ProjectEntry(string Name, string RelativePath, string Guid);

public partial class SolutionProjectsPage : UserControl
{

    public SolutionProjectsPage() { InitializeComponent(); }

    public void Populate(List<string> lines, string solutionDir)
    {
        // Project("{TYPE-GUID}") = "Name", "RelPath", "{PROJ-GUID}"
        var rx = new Regex(
            @"^Project\s*\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""",
            RegexOptions.IgnoreCase);

        var projects = new List<ProjectEntry>();
        foreach (var line in lines)
        {
            var m = rx.Match(line.Trim());
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            var relPath = m.Groups[2].Value;
            var guid = m.Groups[3].Value;
            // skip solution folders (no extension)
            if (!relPath.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase) &&
                !relPath.EndsWith(".fsproj", System.StringComparison.OrdinalIgnoreCase) &&
                !relPath.EndsWith(".vbproj", System.StringComparison.OrdinalIgnoreCase))
                continue;
            projects.Add(new ProjectEntry(name, relPath.Replace('\\', '/'), guid));
        }

        if (this.FindControl<ItemsControl>("ProjectList") is { } ic)
            ic.ItemsSource = projects;
    }
}