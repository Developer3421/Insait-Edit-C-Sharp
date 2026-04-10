using System.Collections.Generic;
using Insait_Edit_C_Sharp.Controls;
namespace Insait_Edit_C_Sharp.Services.RoslynHintsServices;
/// <summary>
/// Hint category: Style 🎨
/// Purely visual / formatting — braces, var, modifier order, parentheses, expression bodies.
/// </summary>
public sealed class StyleHints : IHintCategory
{
    public string CategoryId          => "Style";
    public string CategoryName        => "Style";
    public string CategoryIcon        => "🎨";
    public string CategoryDescription => "Visual / formatting suggestions — braces, var, modifier order, parentheses.";
    public DiagnosticSeverityKind DefaultSeverity => DiagnosticSeverityKind.Info;
    private static readonly Dictionary<string, string> _desc =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["IDE0007"] = "Use var instead of explicit type",
            ["IDE0008"] = "Use explicit type instead of var",
            ["IDE0003"] = "Remove this qualification",
            ["IDE0009"] = "Add this qualification",
            ["IDE0011"] = "Add braces",
            ["IDE0055"] = "Fix formatting",
            ["IDE0021"] = "Use expression body for constructor",
            ["IDE0022"] = "Use expression body for method",
            ["IDE0023"] = "Use expression body for conversion operator",
            ["IDE0024"] = "Use expression body for operator",
            ["IDE0025"] = "Use expression body for property",
            ["IDE0026"] = "Use expression body for indexer",
            ["IDE0027"] = "Use expression body for accessor",
            ["IDE0061"] = "Use expression body for local function",
            ["IDE0036"] = "Order modifiers",
            ["IDE0040"] = "Add accessibility modifiers",
            ["IDE0047"] = "Remove unnecessary parentheses",
            ["IDE0048"] = "Add parentheses for clarity",
            ["IDE0017"] = "Simplify object initialization",
            ["IDE0028"] = "Use collection initializers",
            ["IDE0018"] = "Inline variable declaration",
            ["IDE0029"] = "Null check simplified (ternary)",
            ["IDE0030"] = "Null check simplified (nullable ternary)",
            ["IDE0031"] = "Use null propagation",
            ["IDE0041"] = "Use is null check",
            ["IDE0032"] = "Use auto property",
            ["IDE0044"] = "Add readonly modifier",
            ["IDE0033"] = "Use explicitly provided tuple name",
            ["IDE0034"] = "Simplify default expression",
            ["IDE0037"] = "Use inferred member name",
            ["IDE0042"] = "Deconstruct variable declaration",
            ["IDE0045"] = "Simplify if to conditional expression",
            ["IDE0046"] = "Simplify if to return",
            ["IDE0049"] = "Use language keywords",
            ["IDE0054"] = "Use compound assignment",
            ["IDE0062"] = "Make local function static",
            ["IDE0063"] = "Use simple using statement",
            ["IDE0065"] = "Misplaced using directive",
            ["IDE0066"] = "Convert switch to expression",
            ["IDE0071"] = "Simplify string interpolation",
            ["IDE0074"] = "Use coalesce compound assignment",
            ["IDE0075"] = "Simplify conditional expression",
            ["IDE0073"] = "Use file header",
            ["IDE1006"] = "Naming rule violation",
        };
    private static readonly IReadOnlySet<string> _diagnosticCodes =
        new HashSet<string>(_desc.Keys, System.StringComparer.OrdinalIgnoreCase);

    public static readonly StyleHints Instance = new();

    private StyleHints()
    {
    }

    public IReadOnlySet<string> DiagnosticCodes => _diagnosticCodes;

    public string? DescriptionFor(string code) =>
        _desc.TryGetValue(code, out var d) ? d : null;
}
