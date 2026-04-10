using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Placeholder control — the real diagnostic matrix logic lives in
/// <see cref="Insait_Edit_C_Sharp.Services.DiagnosticSeverityMatrix"/>.
/// </summary>
public partial class DiagnosticMatrixPanel : UserControl
{
    public DiagnosticMatrixPanel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

