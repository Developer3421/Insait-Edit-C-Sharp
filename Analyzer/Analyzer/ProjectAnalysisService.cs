using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Analyzer;

public sealed class ProjectAnalysisBuild : IDisposable
{
    public AdhocWorkspace Workspace { get; }
    public Project Project { get; }
    public CSharpCompilation Compilation { get; }
    public bool HasProjectReferences { get; }

    internal ProjectAnalysisBuild(
        AdhocWorkspace workspace,
        Project project,
        CSharpCompilation compilation,
        bool hasProjectReferences)
    {
        Workspace = workspace;
        Project = project;
        Compilation = compilation;
        HasProjectReferences = hasProjectReferences;
    }

    public void Dispose() => Workspace?.Dispose();
}

/// <summary>
/// Result of a full project analysis (compilation + analyzer diagnostics).
/// Does NOT expose workspace/project/compilation — all analysis is complete.
/// </summary>
public sealed class ProjectAnalysisResult
{
    public ImmutableArray<Diagnostic> CompilationDiagnostics { get; init; }
    public IReadOnlyList<Diagnostic> AnalyzerDiagnostics { get; init; } = Array.Empty<Diagnostic>();
    public bool HasProjectReferences { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Project item types (parsed from .csproj ItemGroup elements)
// ─────────────────────────────────────────────────────────────

public sealed record ProjectItems
{
    public IReadOnlyList<ProjectReferenceItem> ProjectReferences { get; init; } = Array.Empty<ProjectReferenceItem>();
    public IReadOnlyList<PackageReferenceItem> PackageReferences { get; init; } = Array.Empty<PackageReferenceItem>();
    public IReadOnlyList<ReferenceItem> References { get; init; } = Array.Empty<ReferenceItem>();
    public IReadOnlyList<ComReferenceItem> ComReferences { get; init; } = Array.Empty<ComReferenceItem>();
    public IReadOnlyList<AnalyzerItem> AnalyzerItems { get; init; } = Array.Empty<AnalyzerItem>();
    public IReadOnlyList<AdditionalFileItem> AdditionalFiles { get; init; } = Array.Empty<AdditionalFileItem>();
    public IReadOnlyList<EmbeddedResourceItem> EmbeddedResources { get; init; } = Array.Empty<EmbeddedResourceItem>();
    public IReadOnlyList<ResourceItem> Resources { get; init; } = Array.Empty<ResourceItem>();
    public IReadOnlyList<NoneItem> NoneItems { get; init; } = Array.Empty<NoneItem>();
    public IReadOnlyList<ContentItem> ContentItems { get; init; } = Array.Empty<ContentItem>();
}

public sealed record ProjectReferenceItem(string Include);
public sealed record PackageReferenceItem(string Id, string Version);
public sealed record ReferenceItem(string Include, string? HintPath);
public sealed record ComReferenceItem(string Include, string? Guid, int? VersionMajor, int? VersionMinor, int? Lcid, string? WrapperTool);
public sealed record AnalyzerItem(string Include);
public sealed record AdditionalFileItem(string Include);
public sealed record EmbeddedResourceItem(string Include, string? LogicalName);
public sealed record ResourceItem(string Include);
public sealed record NoneItem(string Include);
public sealed record ContentItem(string Include);

/// <summary>
/// Builds a Roslyn project in-memory on the thread pool. All CPU-heavy work
/// (file I/O, XML parsing, reference resolution, compilation) runs off the
/// caller's thread. The returned <see cref="ProjectAnalysisBuild"/> owns a
/// fresh <see cref="AdhocWorkspace"/> that the caller must dispose.
/// </summary>
public sealed class ProjectAnalysisService
{
    public delegate void AnalysisProgressHandler(string message, int current, int total);

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Build project and compile. Returns a fully compiled in-memory project.
    /// </summary>
    /// <param name="projectDir">Project directory containing a .csproj</param>
    /// <param name="sdkRoot">.NET SDK root from settings (optional)</param>
    /// <param name="ct">Cancellation token</param>
    public Task<ProjectAnalysisBuild> BuildProjectAsync(
        string projectDir, string? sdkRoot = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
            throw new ArgumentNullException(nameof(projectDir));
        if (!Directory.Exists(projectDir))
            throw new DirectoryNotFoundException($"Project directory not found: {projectDir}");

        return Task.Run(() => BuildCore(projectDir, sdkRoot, ct), ct);
    }

    /// <summary>
    /// Full async project analysis: build workspace, compile, run analyzers.
    /// All CPU-bound work (I/O, parsing, compilation) runs on the thread pool.
    /// Returns <see cref="ProjectAnalysisResult"/> with both compilation and
    /// analyzer diagnostics. Accepts an optional progress callback.
    /// </summary>
    public async Task<ProjectAnalysisResult> AnalyzeProjectAsync(
        string projectDir, string? sdkRoot = null,
        AnalysisProgressHandler? onProgress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
            throw new ArgumentNullException(nameof(projectDir));
        if (!Directory.Exists(projectDir))
            throw new DirectoryNotFoundException($"Project directory not found: {projectDir}");

        onProgress?.Invoke("Building workspace...", 1, 4);

        using var build = await Task
            .Run(() => BuildCore(projectDir, sdkRoot, ct), ct)
            .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        onProgress?.Invoke("Getting compilation diagnostics...", 2, 4);

        var compDiags = build.Compilation.GetDiagnostics(ct);

        ct.ThrowIfCancellationRequested();

        onProgress?.Invoke("Running analyzers...", 3, 4);

        var anlDiags = await RunAnalyzersAsync(build.Project, build.Compilation, ct)
            .ConfigureAwait(false);

        onProgress?.Invoke("Finalising...", 4, 4);

        // Filter out diagnostics from generated stub files (.g.cs) — they are
        // synthetic in-memory documents and their diagnostics are false positives
        // or meaningless to the user. Real errors in user code are what matters.
        compDiags = FilterGeneratedStubDiagnostics(compDiags);
        anlDiags = FilterGeneratedStubDiagnostics(anlDiags);

        return new ProjectAnalysisResult
        {
            CompilationDiagnostics = compDiags,
            AnalyzerDiagnostics = anlDiags,
            HasProjectReferences = build.HasProjectReferences,
        };
    }

    private static async Task<List<Diagnostic>> RunAnalyzersAsync(
        Project project, CSharpCompilation compilation, CancellationToken ct)
    {
        var projectAnalyzers = project.AnalyzerReferences
            .SelectMany(r => SafeGetAnalyzers(r, project.Language));
        var analyzers = BuiltInAnalyzerProvider.Merge(projectAnalyzers);
        if (analyzers.Length == 0) return new List<Diagnostic>();

        var opts = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);

        try
        {
            var result = await compilation
                .WithAnalyzers(analyzers, opts)
                .GetAnalyzerDiagnosticsAsync(ct)
                .ConfigureAwait(false);
            return result.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analyzer] Analyzer run failed: {ex.Message}");
            return new List<Diagnostic>();
        }
    }

    /// <summary>
    /// Remove diagnostics from generated stub files (.g.cs) — they are synthetic
    /// in-memory documents created for AXAML code-behind stubs. Diagnostics from
    /// these files are false positives or meaningless to the end user.
    /// </summary>
    private static ImmutableArray<Diagnostic> FilterGeneratedStubDiagnostics(
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsEmpty) return diagnostics;

        return diagnostics.Where(d =>
        {
            if (!d.Location.IsInSource) return true;
            var path = d.Location.SourceTree?.FilePath;
            if (string.IsNullOrEmpty(path)) return true;
            return !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
        }).ToImmutableArray();
    }

    private static List<Diagnostic> FilterGeneratedStubDiagnostics(
        List<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return diagnostics;

        return diagnostics.Where(d =>
        {
            if (!d.Location.IsInSource) return true;
            var path = d.Location.SourceTree?.FilePath;
            if (string.IsNullOrEmpty(path)) return true;
            return !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static IEnumerable<DiagnosticAnalyzer> SafeGetAnalyzers(
        AnalyzerReference r, string lang)
    {
        try
        {
            return r.GetAnalyzers(lang);
        }
        catch
        {
            return Enumerable.Empty<DiagnosticAnalyzer>();
        }
    }

    private ProjectAnalysisBuild BuildCore(
        string projectDir, string? sdkRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var csprojFile = FindCsproj(projectDir)
                         ?? throw new InvalidOperationException("No .csproj found in " + projectDir);

        var effectiveDir = Path.GetDirectoryName(csprojFile)!;
        var sourceFiles = EnumerateSourceFiles(effectiveDir).ToArray();
        if (sourceFiles.Length == 0)
            throw new InvalidOperationException("No .cs source files in " + effectiveDir);

        ct.ThrowIfCancellationRequested();

        var (parseOpts, compOpts) = LoadProjectOptions(csprojFile);
        var projectName = Path.GetFileNameWithoutExtension(csprojFile);
        var projectId = ProjectId.CreateNewId();

        // Load source documents
        var documents = new DocumentInfo[sourceFiles.Length];
        for (var i = 0; i < sourceFiles.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var text = TryReadAllText(sourceFiles[i]);
            if (text is null) continue;

            var docId = DocumentId.CreateNewId(projectId);
            documents[i] = DocumentInfo.Create(
                docId,
                name: sourceFiles[i],
                loader: TextLoader.From(
                    TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                filePath: sourceFiles[i]);
        }

        ct.ThrowIfCancellationRequested();

        // Build a lookup of all source file contents so XAML stub generation
        // can check code-behind files for already-defined members (fixes CS0111).
        var sourceTextsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sf in sourceFiles)
        {
            var text = TryReadAllText(sf);
            if (text is not null)
                sourceTextsByPath[sf] = text;
        }

        // Generate C# stubs from .axaml/.xaml files so XAML-generated members
        // (InitializeComponent, named controls, etc.) are visible to Roslyn.
        // Checks code-behind .cs files to avoid generating duplicate members.
        var xamlStubs = GenerateXamlStubs(effectiveDir, projectId, sourceTextsByPath).ToArray();

        // Parse all 10 item types from .csproj
        var projectItems = ParseProjectItems(csprojFile);

        // Add .axaml/.xaml files as additional documents (available to analyzers)
        var additionalDocList = EnumerateXamlFiles(effectiveDir)
            .Select(xf =>
            {
                ct.ThrowIfCancellationRequested();
                var text = TryReadAllText(xf);
                if (text is null) return null;
                var docId = DocumentId.CreateNewId(projectId);
                return DocumentInfo.Create(
                    docId,
                    name: xf,
                    loader: TextLoader.From(
                        TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                    filePath: xf);
            })
            .Where(d => d is not null).Cast<DocumentInfo>().ToList();

        // Add items from <AdditionalFiles> in .csproj as additional documents
        foreach (var af in projectItems.AdditionalFiles)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(Path.Combine(effectiveDir, af.Include));
            if (!File.Exists(fullPath)) continue;
            var text = TryReadAllText(fullPath);
            if (text is null) continue;
            var docId = DocumentId.CreateNewId(projectId);
            additionalDocList.Add(DocumentInfo.Create(
                docId,
                name: fullPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                filePath: fullPath));
        }

        // Also add <None> items that are XAML files (common for Avalonia/WPF)
        // and <Content> items that are XAML files
        foreach (var item in projectItems.NoneItems.Concat<object>(projectItems.ContentItems))
        {
            ct.ThrowIfCancellationRequested();
            var include = item is NoneItem n ? n.Include : ((ContentItem)item).Include;
            if (!include.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) &&
                !include.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                continue;
            var fullPath = Path.GetFullPath(Path.Combine(effectiveDir, include));
            if (!File.Exists(fullPath)) continue;
            if (additionalDocList.Any(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
                continue;
            var text = TryReadAllText(fullPath);
            if (text is null) continue;
            var docId = DocumentId.CreateNewId(projectId);
            additionalDocList.Add(DocumentInfo.Create(
                docId,
                name: fullPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                filePath: fullPath));
        }

        // Add <EmbeddedResource> items as additional documents too
        foreach (var er in projectItems.EmbeddedResources)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(Path.Combine(effectiveDir, er.Include));
            if (!File.Exists(fullPath)) continue;
            var text = TryReadAllText(fullPath);
            if (text is null) continue;
            var docId = DocumentId.CreateNewId(projectId);
            additionalDocList.Add(DocumentInfo.Create(
                docId,
                name: fullPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                filePath: fullPath));
        }

        var additionalDocs = additionalDocList.ToImmutableArray();

        // Combine source documents with generated stubs
        var allDocs = documents.Concat(xamlStubs)
            .Where(d => d is not null).Cast<DocumentInfo>().ToArray();

        ct.ThrowIfCancellationRequested();

        // Resolve references (most expensive part after compilation)
        var metadataRefs = ResolveAllReferences(effectiveDir, sdkRoot, csprojFile);

        // Try to resolve <Reference> items (with HintPath) as metadata references
        var refAddedPaths = new HashSet<string>(metadataRefs
            .Select(r => r.Display).Where(p => p is not null)!, StringComparer.OrdinalIgnoreCase);
        foreach (var ri in projectItems.References)
        {
            ct.ThrowIfCancellationRequested();
            // Try HintPath first
            if (!string.IsNullOrWhiteSpace(ri.HintPath))
            {
                var hintFull = Path.GetFullPath(Path.Combine(effectiveDir, ri.HintPath));
                if (File.Exists(hintFull) && refAddedPaths.Add(hintFull))
                {
                    try { metadataRefs.Add(MetadataReference.CreateFromFile(hintFull)); }
                    catch { }
                }
            }
        }

        // Build analyzer references from <Analyzer> items
        var analyzerRefs = new List<AnalyzerReference>();
        var loader = new RoslynAnalyzerAssemblyLoader();
        foreach (var ai in projectItems.AnalyzerItems)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(Path.Combine(effectiveDir, ai.Include));
            if (!File.Exists(fullPath)) continue;
            try
            {
                analyzerRefs.Add(new AnalyzerFileReference(fullPath, loader));
            }
            catch
            {
            }
        }

        var projectInfo = ProjectInfo.Create(
            projectId, VersionStamp.Create(),
            name: projectName, assemblyName: projectName,
            language: LanguageNames.CSharp,
            filePath: csprojFile,
            compilationOptions: compOpts,
            parseOptions: parseOpts,
            documents: allDocs,
            metadataReferences: metadataRefs,
            analyzerReferences: analyzerRefs,
            additionalDocuments: additionalDocs);

        ct.ThrowIfCancellationRequested();

        var workspace = new AdhocWorkspace();
        if (!workspace.TryApplyChanges(workspace.CurrentSolution.AddProject(projectInfo)))
        {
            workspace.Dispose();
            throw new InvalidOperationException("Failed to apply workspace changes");
        }

        var project = workspace.CurrentSolution.GetProject(projectId)!;
        var compilation = (CSharpCompilation)project.GetCompilationAsync(ct)
            .GetAwaiter().GetResult()!;

        return new ProjectAnalysisBuild(
            workspace, project, compilation,
            metadataRefs.Count > 0);
    }

    // ── Reference resolution ────────────────────────────────────────────────

    /// <summary>
    /// XAML framework detected from the root element's default namespace.
    /// </summary>
    private enum XamlFramework { Unknown, Avalonia, Wpf }

    /// <summary>A named element in a XAML file: its identifier and element type.</summary>
    private sealed record XamlNamedElement(string Name, string ElementType);

    /// <summary>Parsed metadata for a single XAML file.</summary>
    private sealed record XamlFileInfo(
        string Namespace, string ClassName,
        List<XamlNamedElement> NamedElements, XamlFramework Framework);

    private List<MetadataReference> ResolveAllReferences(string projectDir, string? sdkRoot,
        string? csprojFile = null)
    {
        var refs = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(PathComparer);
        var seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRuntimeReferences(refs, seenPaths, seenIdentities, sdkRoot);
        AddNuGetReferences(refs, seenPaths, seenIdentities, projectDir);
        AddPrebuiltReferencedProjectOutputs(refs, seenPaths, seenIdentities, csprojFile);
        return refs;
    }

    /// <summary>
    /// Add ALL reference assemblies from .NET ref packs (NETCore + WindowsDesktop).
    /// Ref packs are the best source for Roslyn compilation — they contain only
    /// metadata (no IL), exactly what the compiler needs. Falls back to the
    /// shared runtime directory if ref packs aren't installed.
    /// </summary>
    private static void AddRuntimeReferences(
        List<MetadataReference> refs, HashSet<string> seenPaths, HashSet<string> seenIdentities, string? sdkRoot)
    {
        var dirs = FindReferenceDirectories(sdkRoot);
        if (dirs.Count == 0)
        {
            // Last resort — current runtime directory
            var fallback = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (fallback is not null)
                AddAllDlls(refs, seenPaths, seenIdentities, fallback);
            return;
        }

        foreach (var dir in dirs)
        {
            if (Directory.Exists(dir))
                AddAllDlls(refs, seenPaths, seenIdentities, dir);
        }
    }

    private static void AddAllDlls(
        List<MetadataReference> refs, HashSet<string> seenPaths, HashSet<string> seenIdentities, string dir)
    {
        try
        {
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
                TryAdd(refs, dll, seenPaths, seenIdentities);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Add compiled output DLLs of referenced projects (from &lt;ProjectReference&gt; elements).
    /// Does NOT add the current project's own compiled output — that would cause
    /// type conflicts (CS0436) and false positives.
    /// XAML-generated types are made visible via in-memory stub generation instead.
    /// </summary>
    private static void AddPrebuiltReferencedProjectOutputs(
        List<MetadataReference> refs, HashSet<string> seenPaths, HashSet<string> seenIdentities, string? csprojFile)
    {
        if (csprojFile is null || !File.Exists(csprojFile)) return;

        try
        {
            var doc = XDocument.Load(csprojFile);
            var projectRefs = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (projectRefs.Count == 0) return;

            var projectDir = Path.GetDirectoryName(csprojFile)!;

            foreach (var relPath in projectRefs)
            {
                var refProjectPath = Path.GetFullPath(Path.Combine(projectDir, relPath!));
                if (!File.Exists(refProjectPath)) continue;

                var refProjectDir = Path.GetDirectoryName(refProjectPath)!;
                var tfm = ParseTargetFramework(refProjectPath);
                if (tfm is null) continue;

                var outputDirs = new[]
                {
                    Path.Combine(refProjectDir, "bin", "Debug", tfm),
                    Path.Combine(refProjectDir, "bin", "Release", tfm),
                };

                foreach (var dir in outputDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    try
                    {
                        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
                            TryAdd(refs, dll, seenPaths, seenIdentities);
                    }
                    catch { }
                    break;
                }
            }
        }
        catch
        {
        }
    }

    private static string? ParseTargetFramework(string csprojFile)
    {
        try
        {
            var doc = XDocument.Load(csprojFile);
            var tfm = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "TargetFramework")?.Value;
            return tfm;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Return ordered list of reference assembly directories to use.
    /// Priority: ref packs > shared runtime.
    /// </summary>
    private static List<string> FindReferenceDirectories(string? sdkRoot)
    {
        var result = new List<string>();
        var dotnetRoot = FindDotNetRoot(sdkRoot);
        if (dotnetRoot is null) return result;

        // 1) Reference packs (best for Roslyn — metadata-only)
        var netcoreDir = FindRefPack(dotnetRoot, "Microsoft.NETCore.App.Ref");
        if (netcoreDir is not null) result.Add(netcoreDir);

        var desktopDir = FindRefPack(dotnetRoot, "Microsoft.WindowsDesktop.App.Ref");
        if (desktopDir is not null) result.Add(desktopDir);

        if (result.Count > 0) return result;

        // 2) Fallback to shared runtime
        var shared = FindSharedFxDir(dotnetRoot, "Microsoft.NETCore.App");
        if (shared is not null) result.Add(shared);

        return result;
    }

    private static string? FindDotNetRoot(string? sdkRoot)
    {
        // Try configured SDK path first
        if (!string.IsNullOrWhiteSpace(sdkRoot))
        {
            // sdkRoot is typically "...\dotnet\sdk\10.0.100"
            var parent = Path.GetDirectoryName(sdkRoot); // "...\dotnet\sdk"
            if (parent is not null)
            {
                var grandParent = Path.GetDirectoryName(parent); // "...\dotnet"
                if (grandParent is not null && Directory.Exists(grandParent))
                    return grandParent;
            }
        }

        // Environment / well-known install locations
        foreach (var c in new[]
                 {
                     Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                     Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"),
                 })
        {
            if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;
        }

        return null;
    }

    private static string? FindRefPack(string dotnetRoot, string packName)
    {
        var packsDir = Path.Combine(dotnetRoot, "packs", packName);
        if (!Directory.Exists(packsDir)) return null;

        try
        {
            var newest = Directory.GetDirectories(packsDir)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (newest is null) return null;

            foreach (var tfm in new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0" })
            {
                var d = Path.Combine(newest, "ref", tfm);
                if (Directory.Exists(d)) return d;
            }

            // Fallback: use any ref directory
            var refDir = Path.Combine(newest, "ref");
            if (Directory.Exists(refDir))
            {
                return Directory.GetDirectories(refDir)
                    .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? FindSharedFxDir(string dotnetRoot, string name)
    {
        var dir = Path.Combine(dotnetRoot, "shared", name);
        if (!Directory.Exists(dir)) return null;
        try
        {
            return Directory.GetDirectories(dir)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    // ── NuGet resolution ────────────────────────────────────────────────────

    private static void AddNuGetReferences(
        List<MetadataReference> refs, HashSet<string> seenPaths, HashSet<string> seenIdentities, string projectDir)
    {
        var assets = Path.Combine(projectDir, "obj", "project.assets.json");
        if (File.Exists(assets))
        {
            foreach (var dll in ResolveFromAssets(assets))
                TryAdd(refs, dll, seenPaths, seenIdentities);
            return;
        }

        var csproj = FindCsproj(projectDir);
        if (csproj is null) return;

        var cache = GetNuGetCache();
        foreach (var (id, ver) in ParsePackageRefs(csproj))
        foreach (var dll in FindPackageDlls(cache, id, ver))
            TryAdd(refs, dll, seenPaths, seenIdentities);
    }

    private static List<string> ResolveFromAssets(string path)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            var folders = new List<string>();
            if (root.TryGetProperty("packageFolders", out var f))
                foreach (var o in f.EnumerateObject())
                    folders.Add(o.Name);

            if (!root.TryGetProperty("targets", out var targets)) return result;

            foreach (var tfm in targets.EnumerateObject())
            {
                foreach (var pkg in tfm.Value.EnumerateObject())
                {
                    var parts = pkg.Name.Split('/');
                    if (parts.Length < 2) continue;

                    // runtime
                    if (pkg.Value.TryGetProperty("runtime", out var rt))
                        foreach (var e in rt.EnumerateObject())
                        {
                            if (!e.Name.EndsWith(".dll")) continue;
                            AddFromFolders(result, folders, parts[0], parts[1], e.Name);
                        }

                    // compile
                    if (pkg.Value.TryGetProperty("compile", out var comp))
                        foreach (var e in comp.EnumerateObject())
                        {
                            if (!e.Name.EndsWith(".dll") || e.Name.EndsWith("_._")) continue;
                            AddFromFolders(result, folders, parts[0], parts[1], e.Name);
                        }
                }

                break; // first TFM only
            }
        }
        catch
        {
        }

        return result;
    }

    private static void AddFromFolders(
        List<string> result, List<string> folders,
        string id, string ver, string relative)
    {
        foreach (var f in folders)
        {
            var fp = Path.GetFullPath(Path.Combine(f, id.ToLowerInvariant(), ver, relative));
            if (File.Exists(fp))
            {
                result.Add(fp);
                break;
            }
        }
    }

    private static List<(string Id, string Version)> ParsePackageRefs(string csprojPath)
    {
        var list = new List<(string, string)>();
        try
        {
            var doc = XDocument.Load(csprojPath);
            foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var id = pr.Attribute("Include")?.Value;
                var ver = pr.Attribute("Version")?.Value
                          ?? pr.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(ver))
                    list.Add((id, ver));
            }
        }
        catch
        {
        }

        return list;
    }

    private static ProjectItems ParseProjectItems(string csprojPath)
    {
        var projectRefs   = new List<ProjectReferenceItem>();
        var packageRefs   = new List<PackageReferenceItem>();
        var refs          = new List<ReferenceItem>();
        var comRefs       = new List<ComReferenceItem>();
        var analyzers     = new List<AnalyzerItem>();
        var addFiles      = new List<AdditionalFileItem>();
        var embeddedRes   = new List<EmbeddedResourceItem>();
        var resources     = new List<ResourceItem>();
        var noneItems     = new List<NoneItem>();
        var contentItems  = new List<ContentItem>();

        if (!File.Exists(csprojPath))
            return EmptyProjectItems(projectRefs, packageRefs, refs, comRefs, analyzers, addFiles, embeddedRes, resources, noneItems, contentItems);

        try
        {
            var doc = XDocument.Load(csprojPath);
            foreach (var item in doc.Descendants())
            {
                var localName = item.Name.LocalName;
                switch (localName)
                {
                    case "ProjectReference":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            projectRefs.Add(new ProjectReferenceItem(include));
                        break;
                    }
                    case "PackageReference":
                    {
                        var id = item.Attribute("Include")?.Value;
                        var ver = item.Attribute("Version")?.Value
                                  ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                            packageRefs.Add(new PackageReferenceItem(id, ver));
                        break;
                    }
                    case "Reference":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (string.IsNullOrWhiteSpace(include)) break;
                        var hintPath = item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value;
                        refs.Add(new ReferenceItem(include, hintPath));
                        break;
                    }
                    case "COMReference":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (string.IsNullOrWhiteSpace(include)) break;
                        var guid = item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "Guid")?.Value;
                        var vmaj = TryParseInt(item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "VersionMajor")?.Value);
                        var vmin = TryParseInt(item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "VersionMinor")?.Value);
                        var lcid = TryParseInt(item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "Lcid")?.Value);
                        var wrapper = item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "WrapperTool")?.Value;
                        comRefs.Add(new ComReferenceItem(include, guid, vmaj, vmin, lcid, wrapper));
                        break;
                    }
                    case "Analyzer":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            analyzers.Add(new AnalyzerItem(include));
                        break;
                    }
                    case "AdditionalFiles":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            addFiles.Add(new AdditionalFileItem(include));
                        break;
                    }
                    case "EmbeddedResource":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (string.IsNullOrWhiteSpace(include)) break;
                        var logicalName = item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "LogicalName")?.Value;
                        embeddedRes.Add(new EmbeddedResourceItem(include, logicalName));
                        break;
                    }
                    case "Resource":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            resources.Add(new ResourceItem(include));
                        break;
                    }
                    case "None":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            noneItems.Add(new NoneItem(include));
                        break;
                    }
                    case "Content":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            contentItems.Add(new ContentItem(include));
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return new ProjectItems
        {
            ProjectReferences  = projectRefs,
            PackageReferences  = packageRefs,
            References         = refs,
            ComReferences      = comRefs,
            AnalyzerItems      = analyzers,
            AdditionalFiles    = addFiles,
            EmbeddedResources  = embeddedRes,
            Resources          = resources,
            NoneItems          = noneItems,
            ContentItems       = contentItems,
        };
    }

    private static ProjectItems EmptyProjectItems(
        List<ProjectReferenceItem> projectRefs, List<PackageReferenceItem> packageRefs,
        List<ReferenceItem> refs, List<ComReferenceItem> comRefs,
        List<AnalyzerItem> analyzers, List<AdditionalFileItem> addFiles,
        List<EmbeddedResourceItem> embeddedRes, List<ResourceItem> resources,
        List<NoneItem> noneItems, List<ContentItem> contentItems)
    {
        return new ProjectItems
        {
            ProjectReferences  = projectRefs,
            PackageReferences  = packageRefs,
            References         = refs,
            ComReferences      = comRefs,
            AnalyzerItems      = analyzers,
            AdditionalFiles    = addFiles,
            EmbeddedResources  = embeddedRes,
            Resources          = resources,
            NoneItems          = noneItems,
            ContentItems       = contentItems,
        };
    }

    private static int? TryParseInt(string? s)
        => int.TryParse(s, out var v) ? v : null;

    private static string GetNuGetCache()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
    }

    private static List<string> FindPackageDlls(string cache, string id, string version)
    {
        var dlls = new List<string>();
        var dir = Path.Combine(cache, id.ToLowerInvariant(), version);
        if (!Directory.Exists(dir)) return dlls;

        var libDir = Path.Combine(dir, "lib");
        if (Directory.Exists(libDir))
        {
            var tfms = new[]
            {
                "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
                "netstandard2.1", "netstandard2.0", "netcoreapp3.1"
            };
            string? best = null;
            foreach (var t in tfms)
            {
                var d = Path.Combine(libDir, t);
                if (Directory.Exists(d))
                {
                    best = d;
                    break;
                }
            }

            best ??= Directory.GetDirectories(libDir).FirstOrDefault();
            if (best is not null)
                try
                {
                    dlls.AddRange(Directory.GetFiles(best, "*.dll"));
                }
                catch
                {
                }
        }

        if (dlls.Count == 0)
        {
            var refDir = Path.Combine(dir, "ref");
            if (Directory.Exists(refDir))
            {
                foreach (var t in new[]
                         {
                             "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
                             "netstandard2.1", "netstandard2.0"
                         })
                {
                    var d = Path.Combine(refDir, t);
                    if (Directory.Exists(d))
                    {
                        try
                        {
                            dlls.AddRange(Directory.GetFiles(d, "*.dll"));
                        }
                        catch
                        {
                        }

                        break;
                    }
                }
            }
        }

        return dlls;
    }

    // ── Source files & project helpers ──────────────────────────────────────

    private static IEnumerable<string> EnumerateSourceFiles(string projectDir)
    {
        if (!Directory.Exists(projectDir)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var f in files)
        {
            var n = Path.GetFullPath(f);
            if (n.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                n.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                n.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            yield return f;
        }
    }

    private static (CSharpParseOptions parse, CSharpCompilationOptions comp) LoadProjectOptions(string csprojFile)
    {
        var langVer = LanguageVersion.Latest;
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DEBUG", "TRACE" };
        var outputKind = OutputKind.DynamicallyLinkedLibrary;
        var nullable = NullableContextOptions.Disable;
        var allowUnsafe = false;
        var treatWarningsAsErrors = false;
        var warningLevel = 4;
        var noWarn = new List<string>();
        var warningsAsErrors = new List<string>();
        var warningsNotAsErrors = new List<string>();
        var checkForOverflowUnderflow = false;
        var deterministic = false;
        var optimize = false;

        if (File.Exists(csprojFile))
        {
            try
            {
                var doc = XDocument.Load(csprojFile);
                var props = doc.Descendants()
                    .Where(e => e.Parent?.Name.LocalName == "PropertyGroup")
                    .ToList();

                var lv = props.FirstOrDefault(e => e.Name.LocalName == "LangVersion")?.Value;
                if (lv is not null && LanguageVersionFacts.TryParse(lv, out var p)) langVer = p;

                var cs = props.FirstOrDefault(e => e.Name.LocalName == "DefineConstants")?.Value;
                if (cs is not null)
                    foreach (var s in cs.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        symbols.Add(s.Trim());

                var ot = props.FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value;
                outputKind = ot?.Trim() switch
                {
                    "Exe" => OutputKind.ConsoleApplication,
                    "WinExe" => OutputKind.WindowsApplication,
                    _ => OutputKind.DynamicallyLinkedLibrary,
                };

                var ns = props.FirstOrDefault(e => e.Name.LocalName == "Nullable")?.Value;
                nullable = ns?.Trim().ToLowerInvariant() switch
                {
                    "enable" => NullableContextOptions.Enable,
                    "warnings" => NullableContextOptions.Warnings,
                    "annotations" => NullableContextOptions.Annotations,
                    _ => NullableContextOptions.Disable,
                };

                var us = props.FirstOrDefault(e => e.Name.LocalName == "AllowUnsafeBlocks")?.Value;
                allowUnsafe = string.Equals(us, "true", StringComparison.OrdinalIgnoreCase);

                var twe = props.FirstOrDefault(e => e.Name.LocalName == "TreatWarningsAsErrors")?.Value;
                treatWarningsAsErrors = string.Equals(twe, "true", StringComparison.OrdinalIgnoreCase);

                var wl = props.FirstOrDefault(e => e.Name.LocalName == "WarningLevel")?.Value;
                if (int.TryParse(wl, out var parsedWl) && parsedWl >= 0 && parsedWl <= 4)
                    warningLevel = parsedWl;

                var nw = props.FirstOrDefault(e => e.Name.LocalName == "NoWarn")?.Value;
                if (nw is not null)
                    noWarn.AddRange(nw.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));

                var wae = props.FirstOrDefault(e => e.Name.LocalName == "WarningsAsErrors")?.Value;
                if (wae is not null)
                    warningsAsErrors.AddRange(wae.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));

                var wnae = props.FirstOrDefault(e => e.Name.LocalName == "WarningsNotAsErrors")?.Value;
                if (wnae is not null)
                    warningsNotAsErrors.AddRange(wnae.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));

                var cou = props.FirstOrDefault(e => e.Name.LocalName == "CheckForOverflowUnderflow")?.Value;
                checkForOverflowUnderflow = string.Equals(cou, "true", StringComparison.OrdinalIgnoreCase);

                var det = props.FirstOrDefault(e => e.Name.LocalName == "Deterministic")?.Value;
                deterministic = string.Equals(det, "true", StringComparison.OrdinalIgnoreCase);

                var opt = props.FirstOrDefault(e => e.Name.LocalName == "Optimize")?.Value;
                optimize = string.Equals(opt, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        var parseOpts = new CSharpParseOptions(langVer)
            .WithDocumentationMode(DocumentationMode.Diagnose)
            .WithPreprocessorSymbols(symbols);

        var specificDiagOptions = new Dictionary<string, ReportDiagnostic>();
        foreach (var id in noWarn)
        {
            var trimmed = id.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                specificDiagOptions[trimmed] = ReportDiagnostic.Suppress;
        }

        ReportDiagnostic generalDiagOption;
        if (treatWarningsAsErrors)
        {
            generalDiagOption = ReportDiagnostic.Error;
            foreach (var id in warningsNotAsErrors)
            {
                var trimmed = id.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !specificDiagOptions.ContainsKey(trimmed))
                    specificDiagOptions[trimmed] = ReportDiagnostic.Warn;
            }
            foreach (var id in warningsAsErrors)
            {
                var trimmed = id.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !specificDiagOptions.ContainsKey(trimmed))
                    specificDiagOptions[trimmed] = ReportDiagnostic.Error;
            }
        }
        else
        {
            generalDiagOption = ReportDiagnostic.Default;
            foreach (var id in warningsAsErrors)
            {
                var trimmed = id.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !specificDiagOptions.ContainsKey(trimmed))
                    specificDiagOptions[trimmed] = ReportDiagnostic.Error;
            }
        }

        var compOpts = new CSharpCompilationOptions(outputKind)
            .WithNullableContextOptions(nullable)
            .WithAllowUnsafe(allowUnsafe)
            .WithOverflowChecks(checkForOverflowUnderflow)
            .WithOptimizationLevel(optimize ? OptimizationLevel.Release : OptimizationLevel.Debug)
            .WithDeterministic(deterministic)
            .WithWarningLevel(warningLevel)
            .WithGeneralDiagnosticOption(generalDiagOption)
            .WithSpecificDiagnosticOptions(specificDiagOptions);

        return (parseOpts, compOpts);
    }

    private static string? FindCsproj(string projectDir)
    {
        if (!Directory.Exists(projectDir)) return null;
        try
        {
            var top = Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (top is not null) return top;
            return Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault(p => !IsIgnoredDir(Path.GetDirectoryName(p) ?? ""));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsIgnoredDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => s is "bin" or "obj" or ".vs");
    }

    private static string? TryReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static void TryAdd(List<MetadataReference> refs, string path,
        HashSet<string>? seenPaths = null, HashSet<string>? seenIdentities = null)
    {
        if (!File.Exists(path)) return;
        if (seenPaths is not null && !seenPaths.Add(path)) return;

        // Check assembly identity to avoid loading the same logical assembly
        // from multiple locations with different versions (fixes CS0433).
        if (seenIdentities is not null)
        {
            try
            {
                var asmName = System.Reflection.AssemblyName.GetAssemblyName(path);
                if (!seenIdentities.Add(asmName.Name!)) return;
            }
            catch
            {
                // Not a managed assembly — allow through path dedup only
            }
        }

        try
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }
        catch
        {
        }
    }

    // ── XAML stub generation ────────────────────────────────────────────────

    /// <summary>
    /// Enumerate .axaml/.xaml files in the project directory, excluding bin/obj.
    /// </summary>
    private static IEnumerable<string> EnumerateXamlFiles(string projectDir)
    {
        if (!Directory.Exists(projectDir)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDir, "*.axaml", SearchOption.AllDirectories);
            files = files.Concat(Directory.EnumerateFiles(projectDir, "*.xaml", SearchOption.AllDirectories));
        }
        catch
        {
            yield break;
        }

        foreach (var f in files)
        {
            var n = Path.GetFullPath(f);
            if (n.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                n.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                n.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            yield return f;
        }
    }

    /// <summary>
    /// Generate C# partial class stubs — one per .axaml/.xaml file.
    /// Each stub provides InitializeComponent() and named control declarations
    /// so Roslyn doesn't report false-positive errors for XAML-generated members.
    /// Detects the UI framework (Avalonia/WPF) from the XAML root namespace.
    /// Checks code-behind .cs files to avoid generating duplicate members (CS0111).
    /// Each stub is named {axamlFileName}.g.cs.
    /// </summary>
    private static IEnumerable<DocumentInfo> GenerateXamlStubs(
        string projectDir, ProjectId projectId,
        IReadOnlyDictionary<string, string> sourceTextsByPath)
    {
        var xamlFiles = EnumerateXamlFiles(projectDir).OrderBy(f => f).ToList();
        if (xamlFiles.Count == 0) yield break;

        foreach (var xf in xamlFiles)
        {
            var info = ParseXamlFile(xf);
            if (info is null) continue;

            // Build a set of existing members from code-behind files
            // to avoid generating stubs that would conflict (CS0111).
            var existingMembers = GetExistingMembers(info, sourceTextsByPath);
            var skipInitialize = existingMembers.Contains("InitializeComponent");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable CS0108, CS0114, CS0169, CS0649, CS8600, CS8603");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
                WritePartialClass(sb, "    ", info, existingMembers);
                sb.AppendLine("}");
            }
            else
            {
                WritePartialClass(sb, "", info, existingMembers);
            }

            var stubPath = Path.ChangeExtension(xf, null) + ".g.cs";
            var sourceText = SourceText.From(sb.ToString());
            var docId = DocumentId.CreateNewId(projectId);
            yield return DocumentInfo.Create(
                docId,
                name: stubPath,
                loader: TextLoader.From(
                    TextAndVersion.Create(sourceText, VersionStamp.Create())),
                filePath: stubPath);
        }
    }

    /// <summary>
    /// Scan all source files for existing members in the same partial class
    /// to avoid generating duplicate members (fixes CS0111).
    /// Only checks files that define the same partial class name — namespace-level
    /// checks are too broad and cause false positive matches across unrelated files.
    /// </summary>
    private static HashSet<string> GetExistingMembers(
        XamlFileInfo info,
        IReadOnlyDictionary<string, string> sourceTextsByPath)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var partialClassPattern = $@"partial\s+class\s+{info.ClassName}\b";

        foreach (var kvp in sourceTextsByPath)
        {
            var text = kvp.Value;

            // Only check files that define the SAME partial class name.
            // Checking by namespace is wrong — other classes in the same
            // namespace (e.g. AxamlLiveHost) may have InitializeComponent()
            // which would falsely suppress the stub for this class.
            if (!ContainsPattern(text, partialClassPattern))
                continue;

            // Check for InitializeComponent() method definition
            if (ContainsPattern(text, @"\bvoid\s+InitializeComponent\s*\("))
                existing.Add("InitializeComponent");

            // Check for named element field declarations
            foreach (var element in info.NamedElements)
            {
                if (ContainsPattern(text, $@"\b_{element.Name}\s*;"))
                    existing.Add($"field_{element.Name}");
                if (ContainsPattern(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(element.Name)}\s*\{{\s*get"))
                    existing.Add($"prop_{element.Name}");
            }
        }

        return existing;
    }

    /// <summary>
    /// Fast regex pattern check — returns true if pattern is found in text.
    /// </summary>
    private static bool ContainsPattern(string text, string pattern)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Write a partial class stub with InitializeComponent() and named controls.
    /// Skips members that already exist in code-behind files to avoid CS0111.
    /// The control type namespace is chosen based on the detected XAML framework.
    /// [DebuggerNonUserCode] is NOT emitted on field declarations (avoids CS0592).
    /// </summary>
    private static void WritePartialClass(
        System.Text.StringBuilder sb, string indent, XamlFileInfo info,
        HashSet<string> existingMembers)
    {
        var controlNs = info.Framework switch
        {
            XamlFramework.Wpf => "global::System.Windows.Controls",
            _ => "global::Avalonia.Controls",
        };

        var compilerAttr = "[System.CodeDom.Compiler.GeneratedCode(\"XamlStubGenerator\", \"1.0\")]";

        sb.AppendLine($"{indent}[System.Diagnostics.DebuggerNonUserCode]");
        sb.AppendLine($"{indent}{compilerAttr}");
        sb.AppendLine($"{indent}partial class {info.ClassName}");
        sb.AppendLine($"{indent}{{");

        // Only generate InitializeComponent() if it doesn't already exist in code-behind
        if (!existingMembers.Contains("InitializeComponent"))
        {
            sb.AppendLine($"{indent}    [System.Diagnostics.DebuggerNonUserCode]");
            sb.AppendLine($"{indent}    {compilerAttr}");
            sb.AppendLine($"{indent}    private void InitializeComponent() {{ }}");
        }

        foreach (var element in info.NamedElements)
        {
            // Skip elements whose field/property already exists in code-behind
            if (existingMembers.Contains($"field_{element.Name}") &&
                existingMembers.Contains($"prop_{element.Name}"))
                continue;

            var typeName = string.IsNullOrEmpty(element.ElementType)
                ? $"{controlNs}.Control"
                : $"{controlNs}.{element.ElementType}";

            // Only generate backing field if it doesn't exist
            if (!existingMembers.Contains($"field_{element.Name}"))
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}    {compilerAttr}");
                sb.AppendLine($"{indent}    private {typeName} _{element.Name};");
            }

            // Only generate property if it doesn't exist
            if (!existingMembers.Contains($"prop_{element.Name}"))
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}    [System.Diagnostics.DebuggerNonUserCode]");
                sb.AppendLine($"{indent}    {compilerAttr}");
                sb.AppendLine($"{indent}    internal {typeName} {element.Name}");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        get => _{element.Name};");
                sb.AppendLine($"{indent}        set => _{element.Name} = value;");
                sb.AppendLine($"{indent}    }}");
            }
        }
        sb.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// Parse x:Class, x:Name attributes and detect UI framework from a XAML file.
    /// </summary>
    private static XamlFileInfo? ParseXamlFile(string filePath)
    {
        try
        {
            using var reader = System.Xml.XmlReader.Create(filePath, new System.Xml.XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
            });

            // Move to first element
            while (reader.Read() && reader.NodeType != System.Xml.XmlNodeType.Element) { }
            if (reader.NodeType != System.Xml.XmlNodeType.Element) return null;

            // Detect UI framework from root element's default namespace
            var rootNamespaceUri = reader.NamespaceURI;
            var framework = rootNamespaceUri switch
            {
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation" => XamlFramework.Wpf,
                "https://github.com/avaloniaui" => XamlFramework.Avalonia,
                _ => XamlFramework.Avalonia, // default to Avalonia for unknown namespaces
            };

            // Parse x:Class attribute
            string? ns = null;
            string? className = null;
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                if (string.Equals(reader.LocalName, "Class", StringComparison.Ordinal) &&
                    (string.Equals(reader.NamespaceURI, "http://schemas.microsoft.com/winfx/2006/xaml",
                         StringComparison.Ordinal) ||
                     string.Equals(reader.NamespaceURI, "http://schemas.microsoft.com/winfx/2009/xaml",
                         StringComparison.Ordinal)))
                {
                    var fullName = reader.Value;
                    var lastDot = fullName.LastIndexOf('.');
                    if (lastDot > 0)
                    {
                        ns = fullName[..lastDot];
                        className = fullName[(lastDot + 1)..];
                    }
                    else
                    {
                        ns = "";
                        className = fullName;
                    }
                }
            }

            if (string.IsNullOrEmpty(className)) return null;

            // Parse named elements with their element type names
            var namedElements = ParseXamlNamedElements(filePath);

            return new XamlFileInfo(ns ?? "", className, namedElements, framework);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse all x:Name / Name attributes from a XAML file and collect element type names.
    /// Supports both prefixed (x:Name) and bare (Name) forms — Avalonia and WPF
    /// treat bare Name as equivalent to x:Name.
    /// Returns a list of (name, elementType) tuples.
    /// </summary>
    private static List<XamlNamedElement> ParseXamlNamedElements(string filePath)
    {
        var elements = new List<XamlNamedElement>();
        try
        {
            using var reader = System.Xml.XmlReader.Create(filePath, new System.Xml.XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
            });

            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;

                var elementType = reader.LocalName;
                string? name = null;

                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    if (!string.Equals(reader.LocalName, "Name", StringComparison.Ordinal))
                        continue;

                    // Accept Name in XAML namespace (x:Name) OR empty namespace (bare Name).
                    // Avalonia and WPF both treat bare Name="..." the same as x:Name="...".
                    var ns = reader.NamespaceURI;
                    if (string.IsNullOrEmpty(ns) ||
                        string.Equals(ns, "http://schemas.microsoft.com/winfx/2006/xaml", StringComparison.Ordinal) ||
                        string.Equals(ns, "http://schemas.microsoft.com/winfx/2009/xaml", StringComparison.Ordinal))
                    {
                        name = reader.Value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(name) && elements.All(e => e.Name != name))
                {
                    elements.Add(new XamlNamedElement(name, elementType));
                }
            }
        }
        catch
        {
        }

        return elements;
    }
}

internal sealed class RoslynAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dependencyLocations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public void AddDependencyLocation(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return;

        lock (_sync)
            _dependencyLocations.Add(Path.GetFullPath(fullPath));
    }

    public Assembly LoadFromPath(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);

        lock (_sync)
        {
            if (_loadedAssemblies.TryGetValue(normalizedPath, out var assembly))
                return assembly;

            var loadedAssembly = Assembly.LoadFrom(normalizedPath);
            _loadedAssemblies[normalizedPath] = loadedAssembly;
            return loadedAssembly;
        }
    }
}
