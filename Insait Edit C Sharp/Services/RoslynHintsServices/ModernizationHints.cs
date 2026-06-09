using System.Collections.Generic;
using Insait_Edit_C_Sharp.Controls;
namespace Insait_Edit_C_Sharp.Services.RoslynHintsServices;
/// <summary>
/// Hint category: Modernization 🚀
/// Suggestions to adopt newer C# language features — primary constructors,
/// collection expressions, pattern matching, file-scoped namespaces, etc.
/// Codes: IDE0290, IDE0300-IDE0305, IDE0161, IDE0083, IDE0082, etc.
/// </summary>
public sealed class ModernizationHints : IHintCategory
{
    public string CategoryId          => "Modernization";
    public string CategoryName        => "Modernization";
    public string CategoryIcon        => "🚀";
    public string CategoryDescription => "Use newer C# language features — primary constructors, collection expressions, pattern matching.";
    public DiagnosticSeverityKind DefaultSeverity => DiagnosticSeverityKind.Info;
    private static readonly Dictionary<string, string> _desc =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            // ── Primary constructor / records ────────────────────────────
            ["IDE0290"] = "Use primary constructor",
            // ── Collection expressions (C# 12) ──────────────────────────
            ["IDE0300"] = "Use collection expression for array",
            ["IDE0301"] = "Use collection expression for empty",
            ["IDE0302"] = "Use collection expression for stackalloc",
            ["IDE0303"] = "Use collection expression for Create()",
            ["IDE0304"] = "Use collection expression for builder",
            ["IDE0305"] = "Use collection expression for fluent",
            // ── Pattern matching ─────────────────────────────────────────
            ["IDE0019"] = "Use pattern matching (as + null check)",
            ["IDE0020"] = "Use pattern matching (is + cast)",
            ["IDE0038"] = "Use pattern matching (is-check + cast)",
            ["IDE0078"] = "Use pattern matching",
            ["IDE0083"] = "Use pattern matching (not operator)",
            ["IDE0084"] = "Use pattern matching (IsNotType)",
            ["IDE0150"] = "Prefer null check over type check",
            ["IDE0260"] = "Use pattern matching",
            ["IDE0270"] = "Null check simplified (conditional access)",
            // ── Namespace style ──────────────────────────────────────────
            ["IDE0130"] = "Namespace does not match folder structure",
            ["IDE0160"] = "Use block-scoped namespace",
            ["IDE0161"] = "Use file-scoped namespace",
            // ── nameof / typeof modernization ────────────────────────────
            ["IDE0082"] = "Convert typeof to nameof",
            ["IDE0280"] = "Use nameof",
            // ── new() simplification ─────────────────────────────────────
            ["IDE0090"] = "Simplify new expression (new())",
            // ── Delegate / lambda ────────────────────────────────────────
            ["IDE1005"] = "Delegate invocation can be simplified",
            ["IDE0039"] = "Use local function instead of lambda",
            // ── Readonly structs ─────────────────────────────────────────
            ["IDE0250"] = "Make struct readonly",
            ["IDE0251"] = "Make member readonly",
            // ── Top-level / switch ───────────────────────────────────────
            ["IDE0210"] = "Convert to top-level statements",
            ["IDE0072"] = "Add missing cases to switch expression",
            ["IDE0010"] = "Add missing cases to switch statement",
            // ── Misc modern C# ───────────────────────────────────────────
            ["IDE0016"] = "Use throw expression",
            ["IDE0056"] = "Use index operator (^)",
            ["IDE0057"] = "Use range operator (..)",
            ["IDE0220"] = "Add explicit cast",
            ["IDE0230"] = "Use UTF-8 string literal",
            ["IDE0240"] = "Remove redundant nullable directive",
            ["IDE0064"] = "Struct contains auto-property not set in constructor",
            ["IDE0076"] = "Invalid global SuppressMessageAttribute",
            ["IDE0180"] = "Use tuple to swap values",
            ["CS1998"] = "Async method lacks await operators",
            ["CS1591"] = "Missing XML comment",
        };
    private static readonly IReadOnlySet<string> _diagnosticCodes =
        new HashSet<string>(_desc.Keys, System.StringComparer.OrdinalIgnoreCase);

    public static readonly ModernizationHints Instance = new();

    private ModernizationHints()
    {
    }

    public IReadOnlySet<string> DiagnosticCodes => _diagnosticCodes;

    public string? DescriptionFor(string code) =>
        _desc.TryGetValue(code, out var d) ? d : null;
}
