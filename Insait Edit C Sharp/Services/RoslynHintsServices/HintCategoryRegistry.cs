using System;
using System.Collections.Generic;
using System.Linq;
using Insait_Edit_C_Sharp.Controls;
namespace Insait_Edit_C_Sharp.Services.RoslynHintsServices;
/// <summary>
/// Central registry for all <see cref="IHintCategory"/> implementations.
///
/// Provides O(1) lookup: diagnostic code → category + severity.
/// New hint services can be added without touching DiagnosticSeverityMatrix —
/// just register them in <see cref="Default"/> here.
///
/// Usage:
///   var cat = HintCategoryRegistry.Default.GetCategory("IDE0290");
///   // cat?.CategoryIcon == "🚀"
///
///   DiagnosticSeverityKind? kind = HintCategoryRegistry.Default.ResolveSeverity("IDE0051");
///   // kind == DiagnosticSeverityKind.Info
/// </summary>
public sealed class HintCategoryRegistry
{
    // ── Singleton ─────────────────────────────────────────────────────────
    /// <summary>
    /// The default registry containing Style, UnusedCode, and Modernization.
    /// Add new <see cref="IHintCategory"/> implementations to this list.
    /// </summary>
    public static readonly HintCategoryRegistry Default = new(
        StyleHints.Instance,
        UnusedCodeHints.Instance,
        ModernizationHints.Instance
    );
    // ── State ─────────────────────────────────────────────────────────────
    /// <summary>All registered categories in declaration order.</summary>
    public IReadOnlyList<IHintCategory> Categories { get; }
    // code (OrdinalIgnoreCase) → owning category
    private readonly Dictionary<string, IHintCategory> _index;
    // ── Constructor ───────────────────────────────────────────────────────
    public HintCategoryRegistry(params IHintCategory[] categories)
    {
        Categories = categories.ToList();
        _index = new Dictionary<string, IHintCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in categories)
        {
            foreach (var code in cat.DiagnosticCodes)
            {
                // First-registered category wins on conflict
                _index.TryAdd(code, cat);
            }
        }
    }
    // ── Lookup API ────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the <see cref="IHintCategory"/> that owns <paramref name="code"/>,
    /// or <c>null</c> if no category knows this code.
    /// </summary>
    public IHintCategory? GetCategory(string code) =>
        _index.TryGetValue(code, out var cat) ? cat : null;
    /// <summary>
    /// Returns the effective <see cref="DiagnosticSeverityKind"/> for
    /// <paramref name="code"/> as declared by its owning category,
    /// or <c>null</c> if no category claims this code.
    /// </summary>
    public DiagnosticSeverityKind? ResolveSeverity(string code) =>
        _index.TryGetValue(code, out var cat) ? cat.DefaultSeverity : null;
    /// <summary>
    /// Returns a short user-visible description for <paramref name="code"/>,
    /// or <c>null</c> if unknown.
    /// </summary>
    public string? DescriptionFor(string code) =>
        _index.TryGetValue(code, out var cat) ? cat.DescriptionFor(code) : null;
    /// <summary>
    /// Returns true when <paramref name="code"/> belongs to any registered category.
    /// </summary>
    public bool Contains(string code) => _index.ContainsKey(code);
    /// <summary>
    /// Returns a display string like "🎨 Style" for the badge in the panel row.
    /// Returns empty string if no category owns the code.
    /// </summary>
    public string BadgeFor(string code) =>
        _index.TryGetValue(code, out var cat)
            ? $"{cat.CategoryIcon} {cat.CategoryName}"
            : string.Empty;
}
