using Avalonia.Controls;
using Insait_Edit_C_Sharp.Services;
using System.Collections.Generic;
using System.Linq;

namespace Insait_Edit_C_Sharp.Controls.ProjectProps;

public record ProjectEntry(string Name, string RelativePath, string Guid);

public partial class SolutionProjectsPage : UserControl
{

    public SolutionProjectsPage() { InitializeComponent(); }

    public void Populate(IEnumerable<SolutionProjectEntry> projects, string? _ = null)
    {
        var items = projects
            .Select(project => new ProjectEntry(
                project.Name,
                project.RelativePath.Replace('\\', '/'),
                project.Guid?.Trim('{', '}') ?? string.Empty))
            .ToList();

        if (this.FindControl<ItemsControl>("ProjectList") is { } ic)
            ic.ItemsSource = items;
    }
}