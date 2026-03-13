using Avalonia.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public partial class SolutionGeneralPage : UserControl
{
    public SolutionGeneralPage() { InitializeComponent(); }

    public void Populate(string solutionPath, List<string> lines)
    {
        SetBox("NameBox",         Path.GetFileNameWithoutExtension(solutionPath));
        SetBox("DirBox",          Path.GetDirectoryName(solutionPath) ?? solutionPath);
        SetBox("FileNameBox",     Path.GetFileName(solutionPath));
        SetBox("PathBox",         solutionPath);
        SetBox("FormatBox",       FirstMatch(lines, "Format Version "));
        SetBox("VsVersionBox",    FirstMatch(lines, "VisualStudioVersion = "));
        SetBox("VsMinVersionBox", FirstMatch(lines, "MinimumVisualStudioVersion = "));
    }

    private void SetBox(string name, string? val)
    {
        if (this.FindControl<TextBox>(name) is { } b) b.Text = val ?? "";
    }

    private static string FirstMatch(List<string> lines, string prefix)
        => lines.Select(l => l.Trim())
                .Where(l => l.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Substring(l.IndexOf('=') < 0 ? prefix.Length : l.IndexOf('=') + 1).Trim())
                .FirstOrDefault() ?? "";
}