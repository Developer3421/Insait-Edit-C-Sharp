using System.Collections.Generic;
using Insait_Edit_C_Sharp.Controls;
namespace Insait_Edit_C_Sharp.Services.RoslynHintsServices;
/// <summary>
/// Hint category: Unused Code 🗑
/// Dead code — unused variables, unused members, unreachable code, unnecessary using.
/// Codes: IDE0051, IDE0052, IDE0059, IDE0060, CS0168, CS0219, CS8019, etc.
/// </summary>
public sealed class UnusedCodeHints : IHintCategory
{
    public string CategoryId          => "UnusedCode";
    public string CategoryName        => "Unused Code";
    public string CategoryIcon        => "🗑";
    public string CategoryDescription => "Dead code — unused variables, members, parameters, unreachable code.";
    public DiagnosticSeverityKind DefaultSeverity => DiagnosticSeverityKind.Info;
    private static readonly Dictionary<string, string> _desc =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // ── C# compiler codes ────────────────────────────────────────
            ["CS0168"] = "Variable declared but never used",
            ["CS0219"] = "Variable assigned but value never used",
            ["CS0649"] = "Field never assigned, will always have default value",
            ["CS0067"] = "Event is never used",
            ["CS0162"] = "Unreachable code detected",
            ["CS0164"] = "Label not referenced",
            ["CS8019"] = "Unnecessary using directive",
            // ── IDE analyzer codes ───────────────────────────────────────
            ["IDE0001"] = "Simplify name",
            ["IDE0002"] = "Simplify member access",
            ["IDE0004"] = "Remove unnecessary cast",
            ["IDE0005"] = "Remove unnecessary import",
            ["IDE0051"] = "Private member is unused",
            ["IDE0052"] = "Private member can be removed",
            ["IDE0058"] = "Expression value is never used",
            ["IDE0059"] = "Unnecessary assignment of a value",
            ["IDE0060"] = "Remove unused parameter",
            ["IDE0079"] = "Remove unnecessary suppression",
            ["IDE0080"] = "Remove unnecessary suppression operator",
            ["IDE0100"] = "Remove unnecessary equality operator",
            ["IDE0110"] = "Remove unnecessary discard",
            ["IDE0120"] = "Simplify LINQ expression",
            ["IDE0200"] = "Remove unnecessary lambda expression",
        };
    private static readonly IReadOnlySet<string> _diagnosticCodes =
        new HashSet<string>(_desc.Keys, System.StringComparer.OrdinalIgnoreCase);

    public static readonly UnusedCodeHints Instance = new();

    private UnusedCodeHints()
    {
    }

    public IReadOnlySet<string> DiagnosticCodes => _diagnosticCodes;

    public string? DescriptionFor(string code) =>
        _desc.TryGetValue(code, out var d) ? d : null;
}
