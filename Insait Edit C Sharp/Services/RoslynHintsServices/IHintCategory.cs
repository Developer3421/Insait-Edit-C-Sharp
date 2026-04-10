using System.Collections.Generic;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services.RoslynHintsServices;

/// <summary>
/// Contract for a pluggable hint category that enriches the
/// HintCategoryRegistry without touching the base DiagnosticSeverityMatrix.
///
/// Each implementation owns a specific set of diagnostic codes and
/// declares how they should be displayed (icon, name, severity tier).
///
/// Conventions:
///   DefaultSeverity is usually Info (ℹ) — Info is shown in
///   the bottom panel alongside Warnings and is never noisy.
///   Return DiagnosticSeverityKind.Hint only for purely
///   cosmetic / experimental codes that users rarely act on.
/// </summary>
public interface IHintCategory
{
    /// <summary>Stable machine-readable identifier, e.g. "Style".</summary>
    string CategoryId { get; }

    /// <summary>Human-readable label shown in the UI, e.g. "Style".</summary>
    string CategoryName { get; }

    /// <summary>Single emoji or short icon shown in the diagnostics row.</summary>
    string CategoryIcon { get; }

    /// <summary>One-sentence description shown in tooltips.</summary>
    string CategoryDescription { get; }

    /// <summary>
    /// The display severity tier to assign to every code in this category.
    /// Usually <see cref="DiagnosticSeverityKind.Info"/>.
    /// </summary>
    DiagnosticSeverityKind DefaultSeverity { get; }

    /// <summary>All Roslyn diagnostic codes owned by this category.</summary>
    IReadOnlySet<string> DiagnosticCodes { get; }

    /// <summary>
    /// Short human-readable description for a specific code.
    /// Returns <c>null</c> if the code is not owned by this category.
    /// </summary>
    string? DescriptionFor(string code);
}

