using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

/// <summary>
/// Full-featured C# completion engine built on top of the official Roslyn pipeline:
///   Microsoft.CodeAnalysis.Features  →  CompletionService (the real engine)
///   Microsoft.CodeAnalysis.CSharp.Features  →  C#-specific providers
///   Microsoft.CodeAnalysis.Workspaces.Common  →  AdhocWorkspace / Document management
///
/// This replaces the hand-rolled fallback that was in CSharpCompletionService and gives
/// the same quality of suggestions that Visual Studio / Rider produce.
/// </summary>
public sealed class RoslynCompletionEngine : IDisposable
{
    // ── workspace ──────────────────────────────────────────────────────────
    private readonly AdhocWorkspace       _workspace;
    private readonly MefHostServices      _host;
    private readonly List<MetadataReference> _defaultRefs;

    // current open "file" tracked inside the workspace
    private ProjectId?  _projectId;
    private DocumentId? _documentId;
    private string?     _trackedFilePath;

    // ── ctor ───────────────────────────────────────────────────────────────
    public RoslynCompletionEngine()
    {
        // MefHostServices loads *all* default Roslyn MEF assemblies which includes
        // Microsoft.CodeAnalysis.Features and Microsoft.CodeAnalysis.CSharp.Features
        // so CompletionService.GetService(document) is guaranteed to return a real service.
        _host      = MefHostServices.Create(BuildMefAssemblies());
        _workspace = new AdhocWorkspace(_host);
        _defaultRefs = CollectDefaultReferences();
    }

    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Main entry point: returns completion items for the given C# source at
    /// (line, column) (both 1-based).
    /// </summary>
    public async Task<IReadOnlyList<RoslynCompletionItem>> GetCompletionsAsync(
        string filePath,
        string sourceCode,
        int line,
        int column,
        CancellationToken ct = default)
    {
        var document = SyncDocument(filePath, sourceCode);
        var position = LineColToOffset(sourceCode, line, column);

        var svc = CompletionService.GetService(document);
        if (svc is null) return Array.Empty<RoslynCompletionItem>();

        CompletionList? list = null;
        try
        {
            list = await svc.GetCompletionsAsync(document, position, cancellationToken: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Array.Empty<RoslynCompletionItem>(); }

        if (list is null) return Array.Empty<RoslynCompletionItem>();

        var result = new List<RoslynCompletionItem>(list.ItemsList.Count);
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
            catch { }

            result.Add(new RoslynCompletionItem
            {
                Label      = item.DisplayText,
                InsertText = item.DisplayText,
                FilterText = item.FilterText,
                SortText   = item.SortText,
                Kind       = MapKind(item.Tags),
                Detail     = detail,
                RoslynItem = item,
            });
        }

        // Sort: items starting with "private" get priority (appear first)
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
    /// Resolves the actual text change for a completion item.
    /// For snippets this returns the expanded snippet body; for normal items
    /// it returns the text that should replace the completion span.
    /// Returns null if the change cannot be resolved.
    /// </summary>
    public async Task<RoslynCompletionChange?> GetCompletionChangeAsync(
        RoslynCompletionItem item,
        string filePath,
        string sourceCode,
        CancellationToken ct = default)
    {
        if (item.RoslynItem is null) return null;
        try
        {
            var document = SyncDocument(filePath, sourceCode);
            var svc = CompletionService.GetService(document);
            if (svc is null) return null;

            var change = await svc.GetChangeAsync(document, item.RoslynItem, cancellationToken: ct);
            var tc = change.TextChange;

            // Collect additional text changes (e.g. using directive insertions)
            var additionalChanges = new List<(int SpanStart, int SpanLength, string NewText)>();
            if (change.TextChanges.Length > 1)
            {
                foreach (var extra in change.TextChanges)
                {
                    // Skip the primary text change — it's already handled
                    if (extra.Span == tc.Span && extra.NewText == tc.NewText)
                        continue;
                    additionalChanges.Add((extra.Span.Start, extra.Span.Length, extra.NewText ?? ""));
                }
            }

            return new RoslynCompletionChange
            {
                SpanStart        = tc.Span.Start,
                SpanLength       = tc.Span.Length,
                NewText          = tc.NewText ?? item.InsertText,
                IsSnippet        = item.Kind == "Snippet",
                AdditionalChanges = additionalChanges,
            };
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Signature help at <paramref name="position"/> (0-based absolute offset).
    /// </summary>
    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken ct = default)
    {
        var document = SyncDocument(filePath, sourceCode);

        var semanticModel = await document.GetSemanticModelAsync(ct);
        var syntaxRoot    = await document.GetSyntaxRootAsync(ct);
        if (semanticModel is null || syntaxRoot is null) return null;

        var token = syntaxRoot.FindToken(position);
        var argList = token.Parent?
            .AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentListSyntax>()
            .FirstOrDefault();

        if (argList?.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
            return null;

        var symbolInfo  = semanticModel.GetSymbolInfo(invocation, ct);
        var method = (symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) as IMethodSymbol;
        if (method is null) return null;

        var overloads = method.ContainingType
            .GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var sigs = overloads.Select(o => new SignatureInfo
        {
            Label      = o.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Documentation = TrimXmlDoc(o.GetDocumentationCommentXml()),
            Parameters = o.Parameters.Select(p => new ParameterInfo
            {
                Label         = $"{p.Type.ToDisplayString()} {p.Name}",
                Documentation = TrimXmlDoc(p.GetDocumentationCommentXml()),
            }).ToList(),
        }).ToList();

        // active parameter from separators
        int activeParam = 0;
        var separators  = argList.Arguments.GetSeparators().ToList();
        for (int i = 0; i < separators.Count; i++)
            if (position > separators[i].SpanStart) activeParam = i + 1;

        return new SignatureHelpInfo
        {
            Signatures      = sigs,
            ActiveSignature = 0,
            ActiveParameter = activeParam,
        };
    }

    /// <summary>
    /// Quick info (hover) at <paramref name="position"/> (0-based absolute offset).
    /// Uses QuickInfoService when available, with fallback to semantic model.
    /// </summary>
    public async Task<QuickInfoResult?> GetQuickInfoAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken ct = default)
    {
        var document = SyncDocument(filePath, sourceCode);
        var qiSvc    = QuickInfoService.GetService(document);
        if (qiSvc is not null)
        {
            var qi = await qiSvc.GetQuickInfoAsync(document, position, ct);
            if (qi is not null)
            {
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
        }
        return null;
    }

    /// <summary>Format entire document via Roslyn Formatter.</summary>
    public async Task<string?> FormatDocumentAsync(
        string filePath,
        string sourceCode,
        CancellationToken ct = default)
    {
        try
        {
            var document  = SyncDocument(filePath, sourceCode);
            var formatted = await Formatter.FormatAsync(document, cancellationToken: ct);
            var text      = await formatted.GetTextAsync(ct);
            return text.ToString();
        }
        catch { return null; }
    }

    /// <summary>Adds extra DLL references (e.g. from a project's bin/ folder).</summary>
    public void AddReference(string dllPath)
    {
        if (!File.Exists(dllPath)) return;
        if (_defaultRefs.Any(r => string.Equals(r.Display, dllPath, StringComparison.OrdinalIgnoreCase)))
            return;
        try { _defaultRefs.Add(MetadataReference.CreateFromFile(dllPath)); }
        catch { }

        // Force project rebuild on next call
        _trackedFilePath = null;
    }

    // ── project context ───────────────────────────────────────────────────
    private string? _projectDir;

    /// <summary>
    /// Sets the project directory so all .cs files are loaded into the workspace
    /// for full cross-file completion. Also resolves NuGet package references.
    /// </summary>
    public void SetProjectContext(string? projectDir)
    {
        if (string.Equals(_projectDir, projectDir, StringComparison.OrdinalIgnoreCase))
            return;
        _projectDir = projectDir;
        _trackedFilePath = null; // force rebuild

        System.Diagnostics.Debug.WriteLine(
            $"[RoslynCompletion] Project context: {projectDir}");
    }

    // ── workspace sync ─────────────────────────────────────────────────────
    private Document SyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFilePath != filePath)
        {
            RebuildProject(filePath, sourceCode);
        }
        else
        {
            // Update only the text
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc is not null)
            {
                var newDoc = doc.WithText(SourceText.From(sourceCode));
                _workspace.TryApplyChanges(newDoc.Project.Solution);
            }
        }
        return _workspace.CurrentSolution.GetDocument(_documentId!)!;
    }

    private void RebuildProject(string filePath, string sourceCode)
    {
        // Remove old project
        if (_projectId is not null)
        {
            _workspace.TryApplyChanges(
                _workspace.CurrentSolution.RemoveProject(_projectId));
        }

        var build = RoslynProjectFactory.CreateBuild(_projectDir, _defaultRefs, filePath, sourceCode);
        var solution = _workspace.CurrentSolution.AddProject(build.ProjectInfo);

        _workspace.TryApplyChanges(solution);

        _projectId       = build.ProjectInfo.Id;
        _documentId      = build.ActiveDocumentId;
        _trackedFilePath = filePath;
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private static int LineColToOffset(string text, int line, int col)
    {
        int offset = 0, currentLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (currentLine == line) { offset = i + (col - 1); break; }
            if (text[i] == '\n') currentLine++;
        }
        return Math.Clamp(offset, 0, text.Length);
    }

    private static string? TrimXmlDoc(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            xml, @"<summary>\s*(.*?)\s*</summary>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : xml.Trim();
    }

    private static string MapKind(ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case WellKnownTags.Class:
                case "Record":      return "Class";
                case "Struct":      return "Struct";
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

    // ── MEF assembly list ──────────────────────────────────────────────────
    /// <summary>
    /// Builds the list of assemblies for MefHostServices so that both
    /// Microsoft.CodeAnalysis.Features and Microsoft.CodeAnalysis.CSharp.Features
    /// are loaded and contribute their completion providers.
    /// </summary>
    private static IEnumerable<Assembly> BuildMefAssemblies()
    {
        var set = new HashSet<Assembly>(MefHostServices.DefaultAssemblies);
        // Load Features assemblies so that CompletionService.GetService() returns
        // a real completion provider instead of null. Swallow any load failures
        // gracefully — the editor falls back to no completions rather than crashing.
        foreach (var name in new[]
        {
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Features",
            "Microsoft.CodeAnalysis.Workspaces.Common",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
        })
        {
            try { set.Add(Assembly.Load(name)); } catch { /* ignore missing / version mismatch */ }
        }
        return set;
    }

    // ── default metadata references ────────────────────────────────────────
    /// <summary>Exposed for QuickFixService / InlineDiagnosticService to share the same references.</summary>
    public static List<MetadataReference> CollectPublicDefaultReferences() => CollectDefaultReferences();

    private static List<MetadataReference> CollectDefaultReferences()
    {
        var refs = new List<MetadataReference>();

        if (TryAddReferencePackAssemblies(refs))
            return refs;

        AddRuntimeFallbackReferences(refs);

        return refs;
    }

    private static bool TryAddReferencePackAssemblies(List<MetadataReference> refs)
    {
        var anyAdded = false;

        foreach (var refDir in GetReferencePackDirectories())
        {
            if (!Directory.Exists(refDir))
                continue;

            try
            {
                foreach (var dll in Directory.GetFiles(refDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    TryAdd(refs, dll);
                    anyAdded = true;
                }
            }
            catch
            {
                // Ignore broken targeting pack directories and continue with fallbacks.
            }
        }

        var privateUriPath = GetSharedFrameworkAssemblyPath("System.Private.Uri.dll");
        if (!string.IsNullOrEmpty(privateUriPath))
            TryAdd(refs, privateUriPath);

        return anyAdded;
    }

    private static void AddRuntimeFallbackReferences(List<MetadataReference> refs)
    {
        // Runtime directory
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
        var coreAssemblies = new[]
        {
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
            "System.Private.Uri.dll",
            "netstandard.dll",
            "System.Private.CoreLib.dll",
            "System.ObjectModel.dll",
            "System.ComponentModel.dll",
            "System.Runtime.Extensions.dll",
            "System.Runtime.InteropServices.dll",
            "System.Memory.dll",
            "Microsoft.CSharp.dll",
        };

        foreach (var name in coreAssemblies)
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
                TryAdd(refs, path);
        }

        var sharedPrivateUri = GetSharedFrameworkAssemblyPath("System.Private.Uri.dll");
        if (!string.IsNullOrEmpty(sharedPrivateUri))
            TryAdd(refs, sharedPrivateUri);

        TryAdd(refs, typeof(object).Assembly.Location);
        TryAdd(refs, typeof(Enumerable).Assembly.Location);
        TryAdd(refs, typeof(System.Text.StringBuilder).Assembly.Location);
    }

    private static IEnumerable<string> GetReferencePackDirectories()
    {
        var dotnetRoot = GetDotNetRoot();
        if (string.IsNullOrWhiteSpace(dotnetRoot) || !Directory.Exists(dotnetRoot))
            yield break;

        foreach (var dir in GetLatestPackReferenceDirs(dotnetRoot, "Microsoft.NETCore.App.Ref", new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0" }))
            yield return dir;

        foreach (var dir in GetLatestPackReferenceDirs(dotnetRoot, "Microsoft.WindowsDesktop.App.Ref", new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0" }))
            yield return dir;
    }

    private static IEnumerable<string> GetLatestPackReferenceDirs(string dotnetRoot, string packName, IReadOnlyList<string> tfms)
    {
        var packRoot = Path.Combine(dotnetRoot, "packs", packName);
        if (!Directory.Exists(packRoot))
            yield break;

        IEnumerable<string> versionDirs;
        try
        {
            versionDirs = Directory.GetDirectories(packRoot)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            yield break;
        }

        foreach (var versionDir in versionDirs)
        {
            foreach (var tfm in tfms)
            {
                var refDir = Path.Combine(versionDir, "ref", tfm);
                if (Directory.Exists(refDir))
                {
                    yield return refDir;
                    yield break;
                }
            }
        }
    }

    private static string? GetSharedFrameworkAssemblyPath(string assemblyName)
    {
        var dotnetRoot = GetDotNetRoot();
        if (string.IsNullOrWhiteSpace(dotnetRoot) || !Directory.Exists(dotnetRoot))
            return null;

        var sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
        if (!Directory.Exists(sharedRoot))
            return null;

        try
        {
            foreach (var versionDir in Directory.GetDirectories(sharedRoot)
                         .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(versionDir, assemblyName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Ignore probe errors.
        }

        return null;
    }

    private static string? GetDotNetRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"),
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }

    private static void TryAdd(List<MetadataReference> list, string path)
    {
        if (!File.Exists(path)) return;
        if (list.Any(r => string.Equals(r.Display, path, StringComparison.OrdinalIgnoreCase))) return;
        try { list.Add(MetadataReference.CreateFromFile(path)); } catch { }
    }

    // ── IDisposable ────────────────────────────────────────────────────────
    public void Dispose() => _workspace.Dispose();
}

// ── DTOs ──────────────────────────────────────────────────────────────────

/// <summary>A single completion item produced by <see cref="RoslynCompletionEngine"/>.</summary>
public sealed class RoslynCompletionItem
{
    public string  Label      { get; init; } = string.Empty;
    public string  InsertText { get; init; } = string.Empty;
    public string? FilterText { get; init; }
    public string? SortText   { get; init; }
    public string  Kind       { get; init; } = "Text";
    public string? Detail     { get; init; }

    /// <summary>
    /// The original Roslyn CompletionItem — kept internally so that
    /// <see cref="RoslynCompletionEngine.GetCompletionChangeAsync"/> can
    /// resolve the real text change (including snippet expansions).
    /// </summary>
    internal CompletionItem? RoslynItem { get; init; }
}

/// <summary>
/// The resolved text change for a completion item, returned by
/// <see cref="RoslynCompletionEngine.GetCompletionChangeAsync"/>.
/// </summary>
public sealed class RoslynCompletionChange
{
    /// <summary>Start offset in the source text to replace.</summary>
    public int    SpanStart  { get; init; }
    /// <summary>Length of the span to replace.</summary>
    public int    SpanLength { get; init; }
    /// <summary>The text to insert (may be multi-line for snippets).</summary>
    public string NewText    { get; init; } = string.Empty;
    /// <summary>Whether this change originated from a snippet item.</summary>
    public bool   IsSnippet  { get; init; }

    /// <summary>
    /// Additional text changes that should be applied to the document,
    /// e.g. adding <c>using</c> directives at the top of the file.
    /// Each entry is (SpanStart, SpanLength, NewText).
    /// These should be applied in reverse order (bottom-to-top) so offsets stay valid.
    /// </summary>
    public List<(int SpanStart, int SpanLength, string NewText)> AdditionalChanges { get; init; } = new();
}

