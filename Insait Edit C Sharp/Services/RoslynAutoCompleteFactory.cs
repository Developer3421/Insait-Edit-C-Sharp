using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;

namespace Insait_Edit_C_Sharp.Services;

// ═══════════════════════════════════════════════════════════════════════════
// RoslynAutoCompleteFactory
// ─────────────────────────────────────────────────────────────────────────
// Pure factory-based real autocompletion service.
//
// Design principles:
//   • No custom completion logic — 100 % delegated to the official Roslyn
//     CompletionService returned by CompletionService.GetService(document).
//   • No mock/hardcoded suggestions.
//   • MefHostServices is the single factory that boots every registered
//     MEF completion provider:  MS.CA.Features, MS.CA.CSharp.Features, etc.
//   • AdhocWorkspace is the lightweight host — no MSBuild needed.
//   • Document is kept in sync via SourceText.From(sourceCode) on each call.
//   • The class is IDisposable; the workspace is released on Dispose().
// ═══════════════════════════════════════════════════════════════════════════
public sealed class RoslynAutoCompleteFactory : IDisposable
{
    // ── MEF host — created once, shared across all documents ───────────────
    private static readonly Lazy<MefHostServices> _sharedHost =
        new(BuildSharedHost, LazyThreadSafetyMode.ExecutionAndPublication);

    // ── per-factory state ──────────────────────────────────────────────────
    private readonly AdhocWorkspace          _workspace;
    private readonly List<MetadataReference> _refs;

    private ProjectId?  _projectId;
    private DocumentId? _documentId;
    private string?     _trackedFile;

    // ── construction ───────────────────────────────────────────────────────
    public RoslynAutoCompleteFactory()
    {
        _workspace = new AdhocWorkspace(_sharedHost.Value);
        _refs      = CollectDefaultReferences();
    }

    // ── public factory API ─────────────────────────────────────────────────

    /// <summary>
    /// Returns Roslyn completion items for <paramref name="filePath"/> at
    /// (1-based) <paramref name="line"/>/<paramref name="column"/>.
    /// All items are produced by the official CompletionService — no custom
    /// logic is applied inside this method.
    /// </summary>
    public async Task<IReadOnlyList<AutoCompleteItem>> GetCompletionsAsync(
        string            filePath,
        string            sourceCode,
        int               line,
        int               column,
        CancellationToken ct = default)
    {
        var document = GetOrSyncDocument(filePath, sourceCode);
        var position = LineColToOffset(sourceCode, line, column);

        // ── Factory call: CompletionService.GetService is the *real* engine ─
        var svc = CompletionService.GetService(document);
        if (svc is null) return Array.Empty<AutoCompleteItem>();

        CompletionList? list;
        try
        {
            list = await svc.GetCompletionsAsync(document, position, cancellationToken: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RoslynAutoCompleteFactory] {ex.Message}");
            return Array.Empty<AutoCompleteItem>();
        }

        if (list is null || list.ItemsList.Count == 0)
            return Array.Empty<AutoCompleteItem>();

        // ── Materialise items — description is fetched from the service too ─
        var result = new List<AutoCompleteItem>(list.ItemsList.Count);
        foreach (var item in list.ItemsList)
        {
            ct.ThrowIfCancellationRequested();

            string? detail = null;
            try
            {
                var desc = await svc.GetDescriptionAsync(document, item, ct);
                if (desc is not null)
                    detail = string.Concat(desc.TaggedParts.Select(p => p.Text));
            }
            catch { /* description is optional */ }

            result.Add(new AutoCompleteItem
            {
                Label      = item.DisplayText,
                InsertText = item.DisplayText,
                FilterText = item.FilterText,
                SortText   = item.SortText,
                Kind       = TagsToKind(item.Tags),
                Detail     = detail,
            });
        }

        // Sort: items starting with "private" get priority (appear first in list)
        result.Sort((a, b) =>
        {
            bool aPrivate = a.Label.StartsWith("private", StringComparison.OrdinalIgnoreCase);
            bool bPrivate = b.Label.StartsWith("private", StringComparison.OrdinalIgnoreCase);
            if (aPrivate && !bPrivate) return -1;
            if (!aPrivate && bPrivate) return 1;
            return string.Compare(a.SortText ?? a.Label, b.SortText ?? b.Label, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    /// <summary>
    /// Returns signature help for a method invocation at the absolute
    /// <paramref name="caretOffset"/> (0-based).  All analysis is done via
    /// the Roslyn semantic model — no manual overload resolution.
    /// </summary>
    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(
        string            filePath,
        string            sourceCode,
        int               caretOffset,
        CancellationToken ct = default)
    {
        var document = GetOrSyncDocument(filePath, sourceCode);
        var model    = await document.GetSemanticModelAsync(ct);
        var root     = await document.GetSyntaxRootAsync(ct);
        if (model is null || root is null) return null;

        var token   = root.FindToken(caretOffset);
        var argList = token.Parent?
            .AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentListSyntax>()
            .FirstOrDefault();

        if (argList?.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax inv)
            return null;

        var si     = model.GetSymbolInfo(inv, ct);
        var method = (si.Symbol ?? si.CandidateSymbols.FirstOrDefault()) as IMethodSymbol;
        if (method is null) return null;

        // Collect all overloads from the containing type
        var overloads = method.ContainingType
            .GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension)
            .ToList();

        var sigs = overloads.Select(o => new SignatureInfo
        {
            Label         = o.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Documentation = StripXmlDoc(o.GetDocumentationCommentXml()),
            Parameters    = o.Parameters.Select(p => new ParameterInfo
            {
                Label         = $"{p.Type.ToDisplayString()} {p.Name}",
                Documentation = StripXmlDoc(p.GetDocumentationCommentXml()),
            }).ToList(),
        }).ToList();

        // Active parameter from argument separators
        int active = 0;
        var seps   = argList.Arguments.GetSeparators().ToList();
        for (int i = 0; i < seps.Count; i++)
            if (caretOffset > seps[i].SpanStart) active = i + 1;

        return new SignatureHelpInfo
        {
            Signatures      = sigs,
            ActiveSignature = 0,
            ActiveParameter = active,
        };
    }

    /// <summary>
    /// Quick-info (hover) at <paramref name="caretOffset"/> (0-based).
    /// Uses <see cref="QuickInfoService"/> from the MEF composition — no
    /// custom symbol-description building.
    /// </summary>
    public async Task<QuickInfoResult?> GetQuickInfoAsync(
        string            filePath,
        string            sourceCode,
        int               caretOffset,
        CancellationToken ct = default)
    {
        var document = GetOrSyncDocument(filePath, sourceCode);
        var qiSvc    = QuickInfoService.GetService(document);
        if (qiSvc is null) return null;

        try
        {
            var qi = await qiSvc.GetQuickInfoAsync(document, caretOffset, ct);
            if (qi is null) return null;

            return new QuickInfoResult
            {
                Sections = qi.Sections
                    .Select(s => new QuickInfoSection
                    {
                        Kind = s.Kind,
                        Text = string.Concat(s.TaggedParts.Select(p => p.Text)),
                    })
                    .ToList(),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Formats the entire document via <see cref="Formatter"/> (Roslyn built-in).
    /// </summary>
    public async Task<string?> FormatAsync(
        string            filePath,
        string            sourceCode,
        CancellationToken ct = default)
    {
        try
        {
            var doc       = GetOrSyncDocument(filePath, sourceCode);
            var formatted = await Formatter.FormatAsync(doc, cancellationToken: ct);
            var text      = await formatted.GetTextAsync(ct);
            return text.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// Adds an extra DLL to the reference set (e.g. from a project's bin/ folder).
    /// The next completion call will pick up the new reference automatically.
    /// </summary>
    public void AddReference(string dllPath)
    {
        if (!File.Exists(dllPath)) return;
        if (_refs.Any(r => string.Equals(r.Display, dllPath, StringComparison.OrdinalIgnoreCase)))
            return;
        try
        {
            _refs.Add(MetadataReference.CreateFromFile(dllPath));
            // Force project rebuild on next call
            _trackedFile = null;
        }
        catch { }
    }

    // ── IDisposable ────────────────────────────────────────────────────────
    public void Dispose() => _workspace.Dispose();

    // ── document lifecycle ─────────────────────────────────────────────────

    private Document GetOrSyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFile != filePath)
        {
            RebuildProject(filePath, sourceCode);
        }
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
        // Tear down previous project
        if (_projectId is not null)
            _workspace.TryApplyChanges(
                _workspace.CurrentSolution.RemoveProject(_projectId));

        var pid = ProjectId.CreateNewId();
        var did = DocumentId.CreateNewId(pid);

        var projectInfo = ProjectInfo.Create(
            pid,
            VersionStamp.Create(),
            name:           "LiveEdit",
            assemblyName:   "LiveEdit",
            language:       LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable),
            parseOptions:       new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: _refs);

        var solution = _workspace.CurrentSolution.AddProject(projectInfo);

        var docInfo = DocumentInfo.Create(
            did,
            name:     Path.GetFileName(filePath),
            loader:   TextLoader.From(TextAndVersion.Create(
                          SourceText.From(sourceCode), VersionStamp.Create())),
            filePath: filePath);

        solution = solution.AddDocument(docInfo);
        _workspace.TryApplyChanges(solution);

        _projectId   = pid;
        _documentId  = did;
        _trackedFile = filePath;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static int LineColToOffset(string text, int line, int col)
    {
        int offset = 0, cur = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (cur == line) { offset = i + Math.Max(0, col - 1); break; }
            if (text[i] == '\n') cur++;
        }
        return Math.Clamp(offset, 0, text.Length);
    }

    private static string? StripXmlDoc(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            xml, @"<summary>\s*(.*?)\s*</summary>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : xml.Trim();
    }

    // ── MEF host factory ───────────────────────────────────────────────────

    /// <summary>
    /// Builds the shared <see cref="MefHostServices"/> that contains every
    /// Roslyn MEF assembly including the high-quality completion providers
    /// from <c>Microsoft.CodeAnalysis.Features</c> and
    /// <c>Microsoft.CodeAnalysis.CSharp.Features</c>.
    /// </summary>
    private static MefHostServices BuildSharedHost()
    {
        var set = new HashSet<Assembly>(MefHostServices.DefaultAssemblies);

        // Explicitly load feature assemblies that provide completion providers
        string[] featureAssemblies =
        [
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Features",
            "Microsoft.CodeAnalysis.Workspaces.Common",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
        ];

        foreach (var name in featureAssemblies)
        {
            try { set.Add(Assembly.Load(name)); }
            catch { /* gracefully skip if not available */ }
        }

        return MefHostServices.Create(set);
    }

    // ── default .NET runtime references ───────────────────────────────────

    private static List<MetadataReference> CollectDefaultReferences()
    {
        var refs       = new List<MetadataReference>();
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;

        // Core .NET assemblies that almost every C# file needs
        string[] coreNames =
        [
            "System.Runtime.dll",
            "System.Console.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Linq.Expressions.dll",
            "System.Threading.dll",
            "System.Threading.Tasks.dll",
            "System.IO.dll",
            "System.IO.FileSystem.dll",
            "System.Text.RegularExpressions.dll",
            "System.Net.Http.dll",
            "netstandard.dll",
            "System.Private.CoreLib.dll",
            "System.ObjectModel.dll",
            "System.ComponentModel.dll",
            "System.Runtime.Extensions.dll",
            "System.Runtime.InteropServices.dll",
            "System.Memory.dll",
            "Microsoft.CSharp.dll",
        ];

        foreach (var name in coreNames)
            TryAdd(refs, Path.Combine(runtimeDir, name));

        // Ensure the 3 canonical roots are always present
        TryAdd(refs, typeof(object).Assembly.Location);
        TryAdd(refs, typeof(Enumerable).Assembly.Location);
        TryAdd(refs, typeof(System.Text.StringBuilder).Assembly.Location);

        return refs;
    }

    private static void TryAdd(List<MetadataReference> list, string path)
    {
        if (!File.Exists(path)) return;
        if (list.Any(r => string.Equals(r.Display, path, StringComparison.OrdinalIgnoreCase))) return;
        try { list.Add(MetadataReference.CreateFromFile(path)); } catch { }
    }

    // ── Roslyn tag → kind mapping ──────────────────────────────────────────
    // Delegated to WellKnownTags — the same constants used by Visual Studio.

    private static string TagsToKind(System.Collections.Immutable.ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case WellKnownTags.Class:
                case "Record":                  return "Class";
                case WellKnownTags.Structure:   return "Struct";
                case WellKnownTags.Interface:   return "Interface";
                case WellKnownTags.Enum:        return "Enum";
                case WellKnownTags.EnumMember:  return "EnumMember";
                case WellKnownTags.Method:
                case WellKnownTags.ExtensionMethod: return "Method";
                case WellKnownTags.Property:    return "Property";
                case WellKnownTags.Field:       return "Field";
                case WellKnownTags.Event:       return "Event";
                case WellKnownTags.Local:
                case WellKnownTags.Parameter:   return "Variable";
                case WellKnownTags.Constant:    return "Constant";
                case WellKnownTags.Namespace:   return "Module";
                case WellKnownTags.Keyword:     return "Keyword";
                case WellKnownTags.Snippet:     return "Snippet";
                case WellKnownTags.TypeParameter: return "TypeParameter";
                case WellKnownTags.Delegate:    return "Function";
            }
        }
        return "Text";
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DTOs — plain data carriers; no logic.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Single autocomplete suggestion produced by <see cref="RoslynAutoCompleteFactory"/>.</summary>
public sealed record AutoCompleteItem
{
    public string  Label      { get; init; } = string.Empty;
    public string  InsertText { get; init; } = string.Empty;
    public string? FilterText { get; init; }
    public string? SortText   { get; init; }
    public string  Kind       { get; init; } = "Text";
    public string? Detail     { get; init; }
}

