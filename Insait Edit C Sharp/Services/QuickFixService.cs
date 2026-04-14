using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// JetBrains Rider-quality quick fix service using Roslyn code analysis.
/// Everything is discovered naturally by Roslyn — no hardcoded dictionaries.
/// Provides:
///   1. Missing using directives (via Roslyn namespace search + built-in CodeFix providers)
///   2. Missing NuGet package suggestions (via NuGet API search)
///   3. Generic Roslyn code fixes (all built-in CodeFixProviders)
/// </summary>
public sealed class QuickFixService : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly MefHostServices _host;
    private readonly List<MetadataReference> _defaultRefs;
    private ProjectId?  _projectId;
    private DocumentId? _documentId;
    private string?     _trackedFilePath;
    private string?     _projectDir;

    /// <summary>
    /// Shared NuGet service for searching packages by type name.
    /// Lazy-initialized to avoid startup cost when NuGet search is not needed.
    /// </summary>
    private static readonly Lazy<NuGetService> _nuGetService = new(() => new NuGetService());

    public QuickFixService()
    {
        _host      = MefHostServices.Create(BuildMefAssemblies());
        _workspace = new AdhocWorkspace(_host);
        _defaultRefs = RoslynCompletionEngine.CollectPublicDefaultReferences();
    }

    public void SetProjectContext(string? projectDir)
    {
        if (string.Equals(_projectDir, projectDir, StringComparison.OrdinalIgnoreCase))
            return;

        _projectDir = projectDir;
        _trackedFilePath = null;
    }

    /// <summary>
    /// Returns quick-fix suggestions for the given position/diagnostic.
    /// </summary>
    public async Task<List<QuickFixSuggestion>> GetFixesAsync(
        string filePath,
        string sourceCode,
        int diagnosticStartOffset,
        int diagnosticEndOffset,
        string diagnosticCode,
        string diagnosticMessage,
        CancellationToken ct = default)
    {
        var fixes = new List<QuickFixSuggestion>();

        try
        {
            var document = SyncDocument(filePath, sourceCode);
            var root     = await document.GetSyntaxRootAsync(ct);
            var model    = await document.GetSemanticModelAsync(ct);
            if (root == null || model == null) return fixes;

            // 1. Missing using directive / unknown type (CS0246, CS0103, CS0234)
            if (diagnosticCode is "CS0246" or "CS0103" or "CS0234")
            {
                var missingType = ExtractMissingTypeName(diagnosticMessage);
                if (!string.IsNullOrEmpty(missingType))
                {
                    // Let Roslyn search all referenced assemblies for matching types
                    var namespaceFixes = FindNamespaceFixes(model, missingType, ct);
                    fixes.AddRange(namespaceFixes);

                    // Search NuGet API for packages containing this type name
                    // (only if Roslyn didn't find anything in existing references)
                    if (namespaceFixes.Count == 0)
                    {
                        var nugetFixes = await SearchNuGetForTypeAsync(missingType, ct);
                        fixes.AddRange(nugetFixes);
                    }

                    // Generate type via dialog window
                    fixes.Add(new QuickFixSuggestion
                    {
                        Title          = $"⚡ Generate type '{missingType}'...",
                        Kind           = QuickFixKind.GenerateType,
                        DiagnosticCode = "CS0246",
                        InsertText     = missingType,
                    });
                }
            }

            // 2. Missing member after dot (CS1061, CS0117)
            if (diagnosticCode is "CS1061" or "CS0117")
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(diagnosticMessage, @"'([^']+)'");
                if (matches.Count >= 2)
                {
                    var ownerType  = matches[0].Groups[1].Value;
                    var memberName = matches[1].Groups[1].Value;
                    fixes.Add(new QuickFixSuggestion
                    {
                        Title          = $"🔧 Generate member '{memberName}' on '{ownerType}'...",
                        Kind           = QuickFixKind.GenerateMember,
                        DiagnosticCode = diagnosticCode,
                        InsertText     = ownerType,
                        MemberName     = memberName,
                    });
                }
            }

            // 3. Unused variable (CS0168, CS0219) → suggest removing
            if (diagnosticCode is "CS0168" or "CS0219")
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title        = "Remove unused variable",
                    Kind         = QuickFixKind.RemoveCode,
                    DiagnosticCode = diagnosticCode,
                });
            }

            // 4. Roslyn built-in CodeFix providers (includes Add Import, etc.)
            var roslynFixes = await GetRoslynCodeFixesAsync(document, diagnosticStartOffset, diagnosticEndOffset, diagnosticCode, ct);
            fixes.AddRange(roslynFixes);

            // 5. Nullable reference (CS8600–CS8604)
            if (diagnosticCode.StartsWith("CS86"))
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title          = "Use null-forgiving operator (!)",
                    Kind           = QuickFixKind.InsertCode,
                    DiagnosticCode = diagnosticCode,
                    InsertText     = "!",
                    InsertOffset   = diagnosticEndOffset,
                });
            }

            // 6. CS1002 missing semicolon
            if (diagnosticCode == "CS1002")
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title        = "Insert missing semicolon",
                    Kind         = QuickFixKind.InsertCode,
                    DiagnosticCode = diagnosticCode,
                    InsertText   = ";",
                    InsertOffset = diagnosticStartOffset,
                });
            }

            // 7. CS0501/CS0161 missing body
            if (diagnosticCode is "CS0501" or "CS0161")
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title          = "Add method body",
                    Kind           = QuickFixKind.InsertCode,
                    DiagnosticCode = diagnosticCode,
                    InsertText     = "\n{\n    throw new NotImplementedException();\n}",
                    InsertOffset   = diagnosticEndOffset,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QuickFix: {ex.Message}");
        }

        return fixes;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Roslyn-based natural type discovery
    // ═══════════════════════════════════════════════════════════════════════

    private static string? ExtractMissingTypeName(string message)
    {
        var m1 = System.Text.RegularExpressions.Regex.Match(message, @"'([^']+)'");
        return m1.Success ? m1.Groups[1].Value : null;
    }

    /// <summary>
    /// Let Roslyn search all referenced assemblies for types matching the
    /// given name. Returns AddUsing suggestions for each discovered namespace.
    /// This is fully natural — no hardcoded lists.
    /// </summary>
    private static List<QuickFixSuggestion> FindNamespaceFixes(
        SemanticModel model,
        string typeName,
        CancellationToken ct)
    {
        var fixes = new List<QuickFixSuggestion>();
        var compilation = model.Compilation;
        var candidates  = new HashSet<string>();

        foreach (var ns in GetAllNamespaces(compilation.GlobalNamespace))
        {
            ct.ThrowIfCancellationRequested();
            if (ns.GetTypeMembers(typeName).Length > 0)
                candidates.Add(ns.ToDisplayString());
        }

        foreach (var ns in candidates.OrderBy(n => n).Take(8))
        {
            fixes.Add(new QuickFixSuggestion
            {
                Title          = $"using {ns};",
                Kind           = QuickFixKind.AddUsing,
                NamespaceName  = ns,
                DiagnosticCode = "CS0246",
            });
        }

        return fixes;
    }

    private static IEnumerable<INamespaceSymbol> GetAllNamespaces(INamespaceSymbol ns)
    {
        yield return ns;
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var n in GetAllNamespaces(child))
                yield return n;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  NuGet API-based package discovery (natural, no hardcoded lists)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Search NuGet.org for packages that likely contain the given type.
    /// Uses the actual NuGet search API — fully dynamic, no hardcoded mapping.
    /// Results are cached per session to avoid repeated network calls.
    /// </summary>
    private static readonly Dictionary<string, List<QuickFixSuggestion>> _nuGetSearchCache = new();

    private static async Task<List<QuickFixSuggestion>> SearchNuGetForTypeAsync(
        string typeName, CancellationToken ct)
    {
        // Cache to avoid repeated searches for the same type
        if (_nuGetSearchCache.TryGetValue(typeName, out var cached))
            return cached;

        var fixes = new List<QuickFixSuggestion>();

        try
        {
            // Use the existing NuGet search service to query nuget.org
            var results = await _nuGetService.Value.SearchPackagesAsync(
                typeName, skip: 0, take: 5, includePrerelease: false, ct);

            foreach (var pkg in results)
            {
                ct.ThrowIfCancellationRequested();

                // Only suggest packages whose ID or title closely matches
                // the type name — avoid irrelevant results
                if (!IsRelevantPackageForType(pkg.Id, pkg.Title, typeName))
                    continue;

                fixes.Add(new QuickFixSuggestion
                {
                    Title          = $"Install NuGet package '{pkg.Id}' ({pkg.Version})",
                    Kind           = QuickFixKind.InstallNuGet,
                    NuGetPackage   = pkg.Id,
                    NamespaceName  = pkg.Id, // best guess: package ID ≈ root namespace
                    DiagnosticCode = "CS0246",
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickFix] NuGet search: {ex.Message}");
        }

        _nuGetSearchCache[typeName] = fixes;
        return fixes;
    }

    /// <summary>
    /// Check whether the found NuGet package is likely relevant to the
    /// missing type. Avoids suggesting unrelated packages.
    /// </summary>
    private static bool IsRelevantPackageForType(string packageId, string title, string typeName)
    {
        // Direct match: package ID contains the type name
        if (packageId.Contains(typeName, StringComparison.OrdinalIgnoreCase))
            return true;

        // The type name starts with a namespace root that matches the package
        var pkgParts = packageId.Split('.');
        if (pkgParts.Length > 0 &&
            typeName.StartsWith(pkgParts[0], StringComparison.OrdinalIgnoreCase))
            return true;

        // Title contains the type name
        if (!string.IsNullOrEmpty(title) &&
            title.Contains(typeName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Roslyn built-in CodeFix providers
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<List<QuickFixSuggestion>> GetRoslynCodeFixesAsync(
        Document document,
        int startOffset,
        int endOffset,
        string diagnosticCode,
        CancellationToken ct)
    {
        var fixes = new List<QuickFixSuggestion>();

        try
        {
            var text = await document.GetTextAsync(ct);
            var span = TextSpan.FromBounds(
                Math.Max(0, startOffset),
                Math.Min(text.Length, Math.Max(startOffset + 1, endOffset)));

            var semanticModel = await document.GetSemanticModelAsync(ct);
            if (semanticModel == null) return fixes;

            var diagnostics = semanticModel.GetDiagnostics(span, ct)
                .Where(d => d.Id == diagnosticCode && d.Location.IsInSource)
                .ToImmutableArray();

            if (diagnostics.IsEmpty) return fixes;

            foreach (var provider in _cachedCodeFixProviders.Value)
            {
                ct.ThrowIfCancellationRequested();
                if (!provider.FixableDiagnosticIds.Contains(diagnosticCode))
                    continue;

                foreach (var diag in diagnostics)
                {
                    try
                    {
                        var context = new CodeFixContext(
                            document, diag,
                            (action, _) =>
                            {
                                var title = action.Title;

                                // Skip "generate type / move type / in new file" —
                                // replaced by our template-based approach
                                if (title.Contains("Generate type", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("Generate class", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("Generate new type", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("Generate property", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("Generate method", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("Generate field", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("in new file", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("Move type", StringComparison.OrdinalIgnoreCase))
                                    return;

                                // Classify "using …" suggestions from Roslyn's
                                // CSharpAddImportCodeFixProvider as AddUsing kind
                                // so they get the box highlight naturally
                                var kind = QuickFixKind.RoslynFix;
                                if (title.StartsWith("using ", StringComparison.Ordinal) &&
                                    title.EndsWith(";", StringComparison.Ordinal))
                                {
                                    kind = QuickFixKind.AddUsing;
                                }

                                // Detect NuGet package install suggestions from Roslyn
                                // (title contains "package" and "Install")
                                if (title.Contains("Install package", StringComparison.OrdinalIgnoreCase) ||
                                    title.Contains("NuGet", StringComparison.OrdinalIgnoreCase))
                                {
                                    kind = QuickFixKind.InstallNuGet;
                                }

                                fixes.Add(new QuickFixSuggestion
                                {
                                    Title          = title,
                                    Kind           = kind,
                                    DiagnosticCode = diagnosticCode,
                                    RoslynAction   = action,
                                });
                            },
                            ct);

                        await provider.RegisterCodeFixesAsync(context);
                    }
                    catch { /* skip broken providers */ }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickFix] Roslyn CodeFix: {ex.Message}");
        }

        return fixes;
    }

    // Lazy-cached CodeFixProviders from Roslyn Features assemblies
    private static readonly Lazy<List<CodeFixProvider>> _cachedCodeFixProviders = new(DiscoverProviders);

    private static List<CodeFixProvider> DiscoverProviders()
    {
        var providers = new List<CodeFixProvider>();
        var assemblies = BuildMefAssemblies();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); } catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(CodeFixProvider).IsAssignableFrom(type) || type.IsAbstract)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                try
                {
                    if (Activator.CreateInstance(type) is CodeFixProvider p)
                        providers.Add(p);
                }
                catch { }
            }
        }
        return providers;
    }

    // ── Workspace sync ───────────────────────────────────────────────────

    private Document SyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFilePath != filePath)
        {
            RebuildProject(filePath, sourceCode);
        }
        else
        {
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc is not null)
            {
                var updated = doc.WithText(SourceText.From(sourceCode));
                if (!_workspace.TryApplyChanges(updated.Project.Solution))
                    RebuildProject(filePath, sourceCode);
            }
            else
            {
                RebuildProject(filePath, sourceCode);
            }
        }
        return _workspace.CurrentSolution.GetDocument(_documentId!)!;
    }

    private void RebuildProject(string filePath, string sourceCode)
    {
        if (_projectId is not null)
            _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(_projectId));

        var build = RoslynProjectFactory.CreateBuild(_projectDir, _defaultRefs, filePath, sourceCode);
        var sol = _workspace.CurrentSolution.AddProject(build.ProjectInfo);

        _workspace.TryApplyChanges(sol);

        _projectId       = build.ProjectInfo.Id;
        _documentId      = build.ActiveDocumentId;
        _trackedFilePath = filePath;
    }

    private static IEnumerable<Assembly> BuildMefAssemblies()
    {
        var assemblies = new HashSet<Assembly>(MefHostServices.DefaultAssemblies);
        foreach (var name in new[]
        {
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Features",
            "Microsoft.CodeAnalysis.Workspaces.Common",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
        })
        {
            try { assemblies.Add(Assembly.Load(name)); } catch { }
        }
        return assemblies;
    }

    /// <summary>
    /// Applies a Roslyn CodeAction-based fix and returns the resulting source text.
    /// Returns null if the action cannot be applied.
    /// </summary>
    public async Task<string?> ApplyRoslynFixAsync(
        QuickFixSuggestion fix,
        string filePath,
        string sourceCode,
        CancellationToken ct = default)
    {
        if (fix.RoslynAction == null) return null;
        try
        {
            var document = SyncDocument(filePath, sourceCode);
            var operations = await fix.RoslynAction.GetOperationsAsync(ct);
            foreach (var op in operations)
            {
                if (op is ApplyChangesOperation applyOp)
                {
                    var changedDoc = applyOp.ChangedSolution.GetDocument(_documentId!);
                    if (changedDoc != null)
                    {
                        var newText = await changedDoc.GetTextAsync(ct);
                        return newText.ToString();
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuickFix] ApplyRoslynFix: {ex.Message}");
        }
        return null;
    }

    public void Dispose() => _workspace.Dispose();
}

/// <summary>A single quick-fix suggestion shown in the Rider-style gutter popup.</summary>
public sealed class QuickFixSuggestion
{
    public string        Title          { get; init; } = string.Empty;
    public QuickFixKind  Kind           { get; init; }
    public string?       NamespaceName  { get; init; }
    public string?       NuGetPackage   { get; init; }
    public string?       InsertText     { get; init; }
    public int           InsertOffset   { get; init; }
    public string        DiagnosticCode { get; init; } = string.Empty;
    /// <summary>For GenerateMember — the member name after the dot.</summary>
    public string?       MemberName     { get; init; }

    /// <summary>
    /// The Roslyn CodeAction — kept so that <see cref="QuickFixKind.RoslynFix"/>
    /// can be applied by obtaining text changes from the action.
    /// </summary>
    internal CodeAction? RoslynAction   { get; init; }
}

public enum QuickFixKind
{
    AddUsing,
    InstallNuGet,
    InsertCode,
    RemoveCode,
    RoslynFix,
    GenerateType,
    GenerateMember,
    Other,
}

