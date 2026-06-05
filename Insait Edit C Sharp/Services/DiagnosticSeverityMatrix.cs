using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Services.RoslynHintsServices;
using AppSeverity  = Insait_Edit_C_Sharp.ViewModels.DiagnosticSeverity;
using AppDiagItem  = Insait_Edit_C_Sharp.ViewModels.DiagnosticItem;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Central technical component — the Diagnostic Severity Matrix.
///
/// Instead of binary suppress/show, every Roslyn diagnostic code is mapped to
/// one of four display tiers:
///
///   ⛔ Error   – compilation failures, must fix
///   ⚠  Warning – potential bugs, deprecated APIs  
///   ℹ  Info    – style suggestions, unused code, IDE refinements
///   💡 Hint    – cosmetic / very-low-priority notes
///   (null)     – suppressed entirely (genuine noise / false positives in
///                fallback mode when NuGet refs are missing)
///
/// Usage (inline analysis):
///   DiagnosticSeverityKind? kind = DiagnosticSeverityMatrix.ResolveInline(id, nativeSev, hasNuGetRefs);
///   if (kind is null) continue; // suppress
///
/// Usage (project-level analysis):
///   AppSeverity? sev = DiagnosticSeverityMatrix.ResolveApp(id, nativeSev, hasNuGetRefs);
///   if (sev is null) continue; // suppress
/// </summary>
public static class DiagnosticSeverityMatrix
{
    // ─────────────────────────────────────────────────────────────────────
    // ① Reclassify as INFO  (ℹ blue)
    //    Only C# *compiler* codes that are style/polish, not real bugs.
    //    All IDE0xxx codes are now owned by HintCategoryRegistry categories
    //    (StyleHints, UnusedCodeHints, ModernizationHints) so they are NOT
    //    listed here — the registry is checked in ResolveInline/ResolveApp.
    // ─────────────────────────────────────────────────────────────────────
    public static readonly IReadOnlySet<string> ReclassifyAsInfo =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Compiler codes with style/polish nature (not bugs)
            "CS8019",  // Unnecessary using directive       → also in UnusedCodeHints
            "CS1591",  // Missing XML comment               → also in ModernizationHints
            "CS0168",  // Variable declared but never used  → also in UnusedCodeHints
            "CS0219",  // Variable assigned, value unused   → also in UnusedCodeHints
            "CS0162",  // Unreachable code detected         → also in UnusedCodeHints
            "CS0164",  // Label not referenced              → also in UnusedCodeHints
            "CS0649",  // Field never assigned              → also in UnusedCodeHints
            "CS1998",  // Async lacks 'await'               → also in ModernizationHints
        };

    // ─────────────────────────────────────────────────────────────────────
    // ② Reclassify as HINT  (💡 green)
    //    Very low priority / cosmetic — visible but de-emphasised.
    // ─────────────────────────────────────────────────────────────────────
    public static readonly IReadOnlySet<string> ReclassifyAsHint =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CS0067",   // The event is never used
            "CS4014",   // Because this call is not awaited, execution continues before the call is completed
        };

    // ─────────────────────────────────────────────────────────────────────
    // ③ Fallback suppression
    //    Suppress ONLY when NuGet references were NOT successfully loaded.
    //    In that mode these almost always are false positives from missing
    //    assemblies; with proper refs they become legitimate.
    // ─────────────────────────────────────────────────────────────────────
    public static readonly IReadOnlySet<string> FallbackSuppressed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CS0103",  // The name 'X' does not exist in the current context
            "CS0246",  // The type or namespace name 'X' could not be found
            "CS0234",  // The type or namespace name 'X' does not exist in namespace 'Y'
            "CS1061",  // 'X' does not contain a definition for 'Y'
            "CS0117",  // 'X' does not contain a definition for 'Y'
            "CS0121",  // The call is ambiguous between the following methods
            "CS7036",  // There is no argument given that corresponds to required parameter
            "CS0012",  // The type 'X' is defined in an assembly that is not referenced
            "CS0616",  // 'X' is not an attribute class
            "CS0433",  // The type 'X' exists in both assemblies
            "CS0518",  // Predefined type 'System.X' is not defined or imported
            "CS1729",  // 'X' does not contain a constructor that takes N arguments
            "CS0535",  // 'X' does not implement interface member 'Y'
            "CS0122",  // 'X' is inaccessible due to its protection level
            "CS0305",  // Using the generic type 'X' requires N type arguments
            "CS1503",  // Argument N: cannot convert from 'X' to 'Y'
            "CS0029",  // Cannot implicitly convert type 'X' to 'Y'
            "CS0311",  // The type 'X' cannot be used as type parameter 'T'
        };

    // ─────────────────────────────────────────────────────────────────────
    //  Resolution API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the effective display severity for inline (squiggle) use.
    /// Returns <c>null</c> if the diagnostic should be suppressed entirely.
    ///
    /// Resolution order:
    ///   1. FallbackSuppressed  → null       (false positives without NuGet refs)
    ///   2. ReclassifyAsInfo    → Info        (compiler codes with style nature)
    ///   3. ReclassifyAsHint    → Hint        (low-priority compiler codes)
    ///   4. HintCategoryRegistry → category severity  (IDE0xxx via plugins)
    ///   5. Native Roslyn severity            (everything else)
    /// </summary>
    public static DiagnosticSeverityKind? ResolveInline(
        string id,
        Microsoft.CodeAnalysis.DiagnosticSeverity nativeSeverity,
        bool hasNuGetRefs)
    {
        // ① Suppress in fallback mode (false positives from missing assemblies)
        if (!hasNuGetRefs && FallbackSuppressed.Contains(id))
            return null;

        // ② Compiler codes reclassified as Info
        if (ReclassifyAsInfo.Contains(id))
            return DiagnosticSeverityKind.Info;

        // ③ Compiler codes reclassified as Hint
        if (ReclassifyAsHint.Contains(id))
            return DiagnosticSeverityKind.Hint;

        // ④ Delegate IDE0xxx and other codes to the HintCategoryRegistry
        var registrySeverity = HintCategoryRegistry.Default.ResolveSeverity(id);
        if (registrySeverity.HasValue)
            return registrySeverity.Value;

        // ⑤ Fall back to native Roslyn severity
        return nativeSeverity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error   => DiagnosticSeverityKind.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => DiagnosticSeverityKind.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info    => DiagnosticSeverityKind.Info,
            _                                                  => DiagnosticSeverityKind.Hint,
        };
    }

    /// <summary>
    /// Resolve the effective display severity for project-level analysis.
    /// Returns <c>null</c> if the diagnostic should be suppressed entirely.
    /// </summary>
    public static AppSeverity? ResolveApp(
        string id,
        Microsoft.CodeAnalysis.DiagnosticSeverity nativeSeverity,
        bool hasNuGetRefs)
    {
        var kind = ResolveInline(id, nativeSeverity, hasNuGetRefs);
        return kind switch
        {
            DiagnosticSeverityKind.Error   => AppSeverity.Error,
            DiagnosticSeverityKind.Warning => AppSeverity.Warning,
            DiagnosticSeverityKind.Info    => AppSeverity.Info,
            DiagnosticSeverityKind.Hint    => AppSeverity.Hint,
            null                           => null,
            _                              => null,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Summarize: build the matrix summary from a flat list
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a full matrix summary (total counts + per-file rows + per-code rows)
    /// from the provided flat list of diagnostic items.
    /// </summary>
    public static DiagnosticMatrixSummary Summarize(IEnumerable<AppDiagItem> items)
    {
        var list = items.ToList();

        var byFile = list
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DiagnosticFileEntry
            {
                FilePath = g.Key,
                FileName = g.First().FileName,
                Errors   = g.Count(d => d.Severity == AppSeverity.Error),
                Warnings = g.Count(d => d.Severity == AppSeverity.Warning),
                Info     = g.Count(d => d.Severity == AppSeverity.Info),
                Hints    = g.Count(d => d.Severity == AppSeverity.Hint),
                Items    = g.OrderBy(d => d.Line).ToList(),
            })
            .OrderByDescending(f => f.Errors)
            .ThenByDescending(f => f.Warnings)
            .ThenBy(f => f.FileName)
            .ToList();

        var byCode = list
            .GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DiagnosticCodeEntry
            {
                Code     = g.Key,
                Severity = g.First().Severity,
                Sample   = g.First().Message,
                Count    = g.Count(),
                Items    = g.OrderBy(d => d.FilePath).ThenBy(d => d.Line).ToList(),
            })
            .OrderBy(c => c.Severity)
            .ThenByDescending(c => c.Count)
            .ToList();

        return new DiagnosticMatrixSummary
        {
            TotalErrors   = list.Count(d => d.Severity == AppSeverity.Error),
            TotalWarnings = list.Count(d => d.Severity == AppSeverity.Warning),
            TotalInfo     = list.Count(d => d.Severity == AppSeverity.Info),
            TotalHints    = list.Count(d => d.Severity == AppSeverity.Hint),
            ByFile        = byFile,
            ByCode        = byCode,
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Data-transfer objects for the matrix
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Complete matrix summary: total counts + per-file rows + per-code rows.
/// Produced by <see cref="DiagnosticSeverityMatrix.Summarize"/>.
/// </summary>
public sealed class DiagnosticMatrixSummary
{
    public int TotalErrors   { get; init; }
    public int TotalWarnings { get; init; }
    public int TotalInfo     { get; init; }
    public int TotalHints    { get; init; }

    /// <summary>Grand total across all severity levels.</summary>
    public int Total => TotalErrors + TotalWarnings + TotalInfo + TotalHints;

    /// <summary>Diagnostics grouped and sorted by source file.</summary>
    public IReadOnlyList<DiagnosticFileEntry> ByFile { get; init; } =
        Array.Empty<DiagnosticFileEntry>();

    /// <summary>Diagnostics grouped and sorted by diagnostic code.</summary>
    public IReadOnlyList<DiagnosticCodeEntry> ByCode { get; init; } =
        Array.Empty<DiagnosticCodeEntry>();

    /// <summary>Returns true when there are zero diagnostics at any level.</summary>
    public bool IsClean => Total == 0;
}

/// <summary>Per-file row in the diagnostic matrix.</summary>
public sealed class DiagnosticFileEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int    Errors   { get; init; }
    public int    Warnings { get; init; }
    public int    Info     { get; init; }
    public int    Hints    { get; init; }

    /// <summary>Total across all severity levels for this file.</summary>
    public int Total => Errors + Warnings + Info + Hints;

    /// <summary>Individual diagnostics in this file, sorted by line.</summary>
    public IReadOnlyList<AppDiagItem> Items { get; init; } =
        Array.Empty<AppDiagItem>();
}

/// <summary>Per-code row in the diagnostic matrix.</summary>
public sealed class DiagnosticCodeEntry
{
    public string     Code     { get; init; } = string.Empty;
    public AppSeverity Severity { get; init; }

    /// <summary>Message text of the first occurrence (representative sample).</summary>
    public string     Sample   { get; init; } = string.Empty;

    /// <summary>Total occurrences of this code across all files.</summary>
    public int        Count    { get; init; }

    /// <summary>All occurrences, sorted by file then line.</summary>
    public IReadOnlyList<AppDiagItem> Items { get; init; } =
        Array.Empty<AppDiagItem>();
}

// BuiltInAnalyzerProvider moved to Analyzer\BuiltInAnalyzerProvider.cs

