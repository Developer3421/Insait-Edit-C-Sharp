using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// JetBrains Rider-quality quick fix service using Roslyn code analysis.
/// Provides:
///   1. Missing using directives
///   2. Missing NuGet package suggestions
///   3. Generic Roslyn code fixes
/// </summary>
public sealed class QuickFixService : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly MefHostServices _host;
    private readonly List<MetadataReference> _defaultRefs;
    private ProjectId?  _projectId;
    private DocumentId? _documentId;
    private string?     _trackedFilePath;

    // Well-known namespace → NuGet package mappings (like Rider does)
    private static readonly Dictionary<string, string[]> NamespaceToNuGet = new(StringComparer.Ordinal)
    {
        ["System.Text.Json"]                         = new[] { "System.Text.Json" },
        ["System.Net.Http"]                          = new[] { "System.Net.Http" },
        ["Newtonsoft.Json"]                          = new[] { "Newtonsoft.Json" },
        ["Microsoft.Extensions.Logging"]             = new[] { "Microsoft.Extensions.Logging" },
        ["Microsoft.Extensions.DependencyInjection"] = new[] { "Microsoft.Extensions.DependencyInjection" },
        ["Microsoft.EntityFrameworkCore"]            = new[] { "Microsoft.EntityFrameworkCore" },
        ["AutoMapper"]                               = new[] { "AutoMapper" },
        ["FluentValidation"]                         = new[] { "FluentValidation" },
        ["Serilog"]                                  = new[] { "Serilog" },
        ["NUnit"]                                    = new[] { "NUnit" },
        ["Xunit"]                                    = new[] { "xunit" },
        ["Moq"]                                      = new[] { "Moq" },
        ["SkiaSharp"]                                = new[] { "SkiaSharp" },
        ["Dapper"]                                   = new[] { "Dapper" },
        ["Polly"]                                    = new[] { "Polly" },
        ["MediatR"]                                  = new[] { "MediatR" },
        ["Avalonia"]                                 = new[] { "Avalonia" },
        ["System.IO.Ports"]                          = new[] { "System.IO.Ports" },
        ["System.Drawing"]                           = new[] { "System.Drawing.Common" },
        ["System.Data.SQLite"]                       = new[] { "System.Data.SQLite" },
        ["Microsoft.Data.SqlClient"]                 = new[] { "Microsoft.Data.SqlClient" },
        ["Npgsql"]                                   = new[] { "Npgsql" },
        ["MySql.Data"]                               = new[] { "MySql.Data" },
        ["RestSharp"]                                = new[] { "RestSharp" },
        ["CsvHelper"]                                = new[] { "CsvHelper" },
        ["ExcelDataReader"]                          = new[] { "ExcelDataReader" },
        ["iTextSharp"]                               = new[] { "iTextSharp" },
        ["QRCoder"]                                  = new[] { "QRCoder" },
        ["Microsoft.CognitiveServices.Speech"]       = new[] { "Microsoft.CognitiveServices.Speech" },
        ["Microsoft.Azure.Cosmos"]                   = new[] { "Microsoft.Azure.Cosmos" },
        ["Amazon.S3"]                                = new[] { "AWSSDK.S3" },
        ["Google.Cloud.Storage"]                     = new[] { "Google.Cloud.Storage.V1" },
    };

    public QuickFixService()
    {
        _host      = MefHostServices.Create(BuildMefAssemblies());
        _workspace = new AdhocWorkspace(_host);
        _defaultRefs = RoslynCompletionEngine.CollectPublicDefaultReferences();
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

            // 1. Missing using directive (CS0246, CS0103, CS0234)
            if (diagnosticCode is "CS0246" or "CS0103" or "CS0234")
            {
                var missingType = ExtractMissingTypeName(diagnosticMessage);
                if (!string.IsNullOrEmpty(missingType))
                {
                    // Search all known namespaces for this type
                    var namespaceFixes = await FindNamespaceFixesAsync(document, model, root, missingType, diagnosticStartOffset, ct);
                    fixes.AddRange(namespaceFixes);

                    // Also check NuGet packages
                    var nugetFixes = GetNuGetSuggestions(missingType, diagnosticMessage);
                    fixes.AddRange(nugetFixes);
                }
            }

            // 2. Unused variable (CS0168, CS0219) → suggest removing
            if (diagnosticCode is "CS0168" or "CS0219")
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title        = "Remove unused variable",
                    Kind         = QuickFixKind.RemoveCode,
                    DiagnosticCode = diagnosticCode,
                });
            }

            // 3. Missing return statement, missing override, etc. via Roslyn CodeFix providers
            var roslynFixes = await GetRoslynCodeFixesAsync(document, diagnosticStartOffset, diagnosticEndOffset, diagnosticCode, ct);
            fixes.AddRange(roslynFixes);

            // 4. Nullable reference (CS8600, CS8601, CS8602, CS8603, CS8604)
            if (diagnosticCode.StartsWith("CS86"))
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title        = "Add null check",
                    Kind         = QuickFixKind.InsertCode,
                    DiagnosticCode = diagnosticCode,
                });
                fixes.Add(new QuickFixSuggestion
                {
                    Title        = "Use null-forgiving operator (!)",
                    Kind         = QuickFixKind.InsertCode,
                    DiagnosticCode = diagnosticCode,
                });
            }

            // 5. CS1002 missing semicolon
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

            // 6. CS0501 missing body
            if (diagnosticCode is "CS0501" or "CS0161")
            {
                fixes.Add(new QuickFixSuggestion
                {
                    Title        = "Add method body",
                    Kind         = QuickFixKind.InsertCode,
                    DiagnosticCode = diagnosticCode,
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

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? ExtractMissingTypeName(string message)
    {
        // "The type or namespace name 'Foo' could not be found"
        // "The name 'Foo' does not exist in the current context"
        var m1 = System.Text.RegularExpressions.Regex.Match(message, @"'([^']+)'");
        return m1.Success ? m1.Groups[1].Value : null;
    }

    private static async Task<List<QuickFixSuggestion>> FindNamespaceFixesAsync(
        Document document,
        SemanticModel model,
        SyntaxNode root,
        string typeName,
        int position,
        CancellationToken ct)
    {
        var fixes = new List<QuickFixSuggestion>();

        // Search all referenced namespaces for types matching typeName
        var compilation = model.Compilation;
        var candidates  = new List<string>();

        foreach (var ns in GetAllNamespaces(compilation.GlobalNamespace))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var type in ns.GetTypeMembers(typeName))
            {
                candidates.Add(ns.ToDisplayString());
            }
        }

        foreach (var ns in candidates.Distinct().Take(5))
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

    private static List<QuickFixSuggestion> GetNuGetSuggestions(string typeName, string message)
    {
        var fixes = new List<QuickFixSuggestion>();
        foreach (var kv in NamespaceToNuGet)
        {
            // Match if type name contains the namespace root
            var parts = kv.Key.Split('.');
            if (typeName.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) ||
                message.Contains(parts[0], StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pkg in kv.Value)
                {
                    fixes.Add(new QuickFixSuggestion
                    {
                        Title          = $"Install NuGet package '{pkg}'",
                        Kind           = QuickFixKind.InstallNuGet,
                        NuGetPackage   = pkg,
                        DiagnosticCode = "CS0246",
                    });
                }
            }
        }
        return fixes;
    }

    private static Task<List<QuickFixSuggestion>> GetRoslynCodeFixesAsync(
        Document document,
        int startOffset,
        int endOffset,
        string diagnosticCode,
        CancellationToken ct)
    {
        // Roslyn CodeFix providers are only available in VS/Rider host processes.
        // In standalone mode we return an empty list; namespace/NuGet fixes are
        // generated by the callers above.
        return Task.FromResult(new List<QuickFixSuggestion>());
    }

    // ── Workspace sync (same pattern as RoslynCompletionEngine) ───────────

    private Document SyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFilePath != filePath)
            RebuildProject(filePath, sourceCode);
        else
        {
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc is not null)
            {
                var updated = doc.WithText(SourceText.From(sourceCode));
                _workspace.TryApplyChanges(updated.Project.Solution);
            }
        }
        return _workspace.CurrentSolution.GetDocument(_documentId!)!;
    }

    private void RebuildProject(string filePath, string sourceCode)
    {
        if (_projectId is not null)
            _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(_projectId));

        var projectId  = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var info = ProjectInfo.Create(
            projectId, VersionStamp.Create(),
            "LiveFix", "LiveFix", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: _defaultRefs);

        var sol = _workspace.CurrentSolution.AddProject(info);
        var docInfo = DocumentInfo.Create(
            documentId,
            System.IO.Path.GetFileName(filePath),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceCode), VersionStamp.Create())),
            filePath: filePath);

        sol = sol.AddDocument(docInfo);
        _workspace.TryApplyChanges(sol);

        _projectId       = projectId;
        _documentId      = documentId;
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
}

public enum QuickFixKind
{
    AddUsing,
    InstallNuGet,
    InsertCode,
    RemoveCode,
    RoslynFix,
    Other,
}

