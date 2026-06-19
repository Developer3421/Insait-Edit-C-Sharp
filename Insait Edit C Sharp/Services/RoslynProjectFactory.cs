using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Insait_Edit_C_Sharp.Services;

internal sealed class RoslynProjectBuild
{
    public ProjectInfo ProjectInfo { get; }
    public DocumentId ActiveDocumentId { get; }
    public IReadOnlyDictionary<string, DocumentId> DocumentIds { get; }
    public IReadOnlySet<string> UserSourceFiles { get; }
    public bool HasProjectMetadataReferences { get; }
    public IReadOnlyList<string> EditorConfigPaths { get; }

    public RoslynProjectBuild(
        ProjectInfo projectInfo,
        DocumentId activeDocumentId,
        IReadOnlyDictionary<string, DocumentId> documentIds,
        IReadOnlySet<string> userSourceFiles,
        bool hasProjectMetadataReferences,
        IReadOnlyList<string> editorConfigPaths)
    {
        ProjectInfo = projectInfo;
        ActiveDocumentId = activeDocumentId;
        DocumentIds = documentIds;
        UserSourceFiles = userSourceFiles;
        HasProjectMetadataReferences = hasProjectMetadataReferences;
        EditorConfigPaths = editorConfigPaths;
    }

    public bool ShouldIncludeDiagnostic(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
            return false;

        var path = diagnostic.Location.SourceTree?.FilePath ?? diagnostic.Location.GetLineSpan().Path;
        return !string.IsNullOrWhiteSpace(path) && UserSourceFiles.Contains(RoslynProjectFactory.NormalizePath(path));
    }

    public bool BelongsToFile(Diagnostic diagnostic, string filePath)
    {
        if (!ShouldIncludeDiagnostic(diagnostic))
            return false;

        var path = diagnostic.Location.SourceTree?.FilePath ?? diagnostic.Location.GetLineSpan().Path;
        return string.Equals(
            RoslynProjectFactory.NormalizePath(path),
            RoslynProjectFactory.NormalizePath(filePath),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal static class RoslynProjectFactory
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex PartialClassRegex = new(
        @"(?<!\w)(?<access>public|internal|protected\s+internal|private\s+protected)?\s*(?:sealed\s+|abstract\s+|static\s+)*partial\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);
    private static readonly Regex InitializeComponentDefinitionRegex = new(
        @"\b(?:public|private|protected|internal|protected\s+internal|private\s+protected)?\s*(?:static\s+)?(?:partial\s+)?void\s+InitializeComponent\s*\(",
        RegexOptions.Compiled);

    // Regex to extract `global using <namespace>;` directives from source text.
    private static readonly Regex GlobalUsingRegex = new(
        @"^\s*global\s+using\s+(?<ns>[^;]+)\s*;",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Default implicit usings for Microsoft.NET.Sdk (non-Web) projects per TFM.
    /// </summary>
    private static readonly IReadOnlySet<string> DefaultImplicitUsings = new HashSet<string>(StringComparer.Ordinal)
    {
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    };

    /// <summary>
    /// Additional implicit usings for web (Microsoft.NET.Sdk.Web) projects.
    /// </summary>
    private static readonly IReadOnlySet<string> WebImplicitUsings = new HashSet<string>(StringComparer.Ordinal)
    {
        "System.Net.Http.Json",
        "Microsoft.AspNetCore.Builder",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Logging",
    };

    public static RoslynProjectBuild CreateBuild(
        string? projectContextPath,
        IReadOnlyCollection<MetadataReference> baseReferences,
        string activeFilePath,
        string activeSourceCode)
    {
        var projectDir = ResolveProjectDirectory(projectContextPath, activeFilePath);
        var projectFilePath = FindProjectFile(projectDir);
        var generatedEditorConfig = FindGeneratedEditorConfig(projectDir);
        var evaluatedProperties = LoadEvaluatedBuildProperties(generatedEditorConfig);
        var projectProperties = LoadProjectProperties(projectFilePath, evaluatedProperties);

        var sourceFiles = EnumerateProjectSourceFiles(projectDir).ToList();
        var generatedSourceFiles = EnumerateGeneratedSourceFiles(projectDir, generatedEditorConfig).ToList();
        var additionalFiles = EnumerateAdditionalFiles(projectDir).ToList();
        var metadataReferences = MergeMetadataReferences(baseReferences, ResolveProjectMetadataReferences(projectDir));

        // Parse project item types from .csproj (AdditionalFiles, Analyzer, Reference, etc.)
        var projectItems = !string.IsNullOrWhiteSpace(projectFilePath)
            ? ParseProjectItems(projectFilePath)
            : new ParsedProjectItems(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ReferenceInfo>(), Array.Empty<ComReferenceInfo>(), Array.Empty<LinkedFileInfo>(), Array.Empty<string>());

        // Add <AdditionalFiles> items from .csproj to the additional files list
        foreach (var af in projectItems.AdditionalFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectDir!, af));
            if (File.Exists(fullPath) && !additionalFiles.Contains(fullPath, PathComparer))
                additionalFiles.Add(fullPath);
        }

        // Resolve <Reference> items with HintPath as metadata references
        foreach (var reference in projectItems.References)
        {
            var fullPath = !string.IsNullOrWhiteSpace(reference.HintPath)
                ? Path.GetFullPath(Path.Combine(projectDir!, reference.HintPath))
                : null;

            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                // Try resolving by assembly name from SDK/runtime references
                fullPath = ResolveReferenceByAssemblyName(fullPath ?? reference.Include, projectDir);
            }

            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                continue;

            try
            {
                var properties = string.IsNullOrWhiteSpace(reference.Aliases)
                    ? default(MetadataReferenceProperties)
                    : new MetadataReferenceProperties(
                        aliases: reference.Aliases
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(static a => a.Trim())
                            .Where(static a => a.Length > 0)
                            .ToImmutableArray());

                metadataReferences.Add(MetadataReference.CreateFromFile(fullPath, properties));
            }
            catch { }
        }

        // ── Shared Projects (.shproj) ──
        foreach (var shprojRel in projectItems.SharedProjectImports)
        {
            var shprojPath = Path.GetFullPath(Path.Combine(projectDir!, shprojRel));
            if (!File.Exists(shprojPath)) continue;
            try
            {
                var shprojDoc = XDocument.Load(shprojPath);
                var import = shprojDoc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Import");
                var projitemsRel = import?.Attribute("Project")?.Value;
                if (string.IsNullOrWhiteSpace(projitemsRel)) continue;

                var projitemsPath = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(shprojPath)!, projitemsRel));
                if (!File.Exists(projitemsPath)) continue;

                var itemsDoc = XDocument.Load(projitemsPath);
                foreach (var compile in itemsDoc.Descendants()
                    .Where(e => e.Name.LocalName == "Compile"))
                {
                    var include = compile.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include)) continue;
                    var fullPath = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(projitemsPath)!, include));
                    if (File.Exists(fullPath) && !sourceFiles.Contains(fullPath, PathComparer))
                        sourceFiles.Add(fullPath);
                }
            }
            catch { }
        }

        // ── Linked Files (files outside project dir via <Compile Link="...">) ──
        foreach (var linkedFile in projectItems.LinkedFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectDir!, linkedFile.Include));
            if (File.Exists(fullPath) && !sourceFiles.Contains(fullPath, PathComparer))
                sourceFiles.Add(fullPath);
        }

        // ── COM References ──
        foreach (var comRef in projectItems.ComReferences)
        {
            var interopDll = ResolveComReference(comRef, projectDir);
            if (interopDll is not null)
            {
                try { metadataReferences.Add(MetadataReference.CreateFromFile(interopDll)); }
                catch { }
            }
        }

        var analyzerReferences = ResolveAnalyzerReferences(projectFilePath, projectItems);

        var projectId = ProjectId.CreateNewId();
        var documentIds = new Dictionary<string, DocumentId>(PathComparer);
        var userSourceFiles = new HashSet<string>(PathComparer);
        var documents = new List<DocumentInfo>();
        var sourceTextsByPath = new Dictionary<string, string>(PathComparer);

        var activeDocumentId = DocumentId.CreateNewId(projectId);
        documents.Add(CreateSourceDocument(activeDocumentId, activeFilePath, activeSourceCode));
        documentIds[NormalizePath(activeFilePath)] = activeDocumentId;
        userSourceFiles.Add(NormalizePath(activeFilePath));
        sourceTextsByPath[NormalizePath(activeFilePath)] = activeSourceCode;

        foreach (var sourceFile in sourceFiles)
        {
            if (PathEquals(sourceFile, activeFilePath))
                continue;

            var text = TryReadAllText(sourceFile);
            if (text is null)
                continue;

            var documentId = DocumentId.CreateNewId(projectId);
            documents.Add(CreateSourceDocument(documentId, sourceFile, text));
            documentIds[NormalizePath(sourceFile)] = documentId;
            userSourceFiles.Add(NormalizePath(sourceFile));
            sourceTextsByPath[NormalizePath(sourceFile)] = text;
        }

        foreach (var generatedSourceFile in generatedSourceFiles)
        {
            if (PathEquals(generatedSourceFile, activeFilePath))
                continue;

            var text = TryReadAllText(generatedSourceFile);
            if (text is null)
                continue;

            var documentId = DocumentId.CreateNewId(projectId);
            documents.Add(CreateSourceDocument(documentId, generatedSourceFile, text));
            documentIds[NormalizePath(generatedSourceFile)] = documentId;
        }

        var additionalDocuments = new List<DocumentInfo>();
        foreach (var additionalFile in additionalFiles)
        {
            var text = TryReadAllText(additionalFile);
            if (text is null)
                continue;

            additionalDocuments.Add(CreateTextDocument(projectId, additionalFile, text));
        }

        documents.AddRange(CreateSyntheticAvaloniaDocuments(projectId, additionalFiles, sourceTextsByPath));

        // ── Global usings (ImplicitUsings + explicit global using directives) ──
        var globalUsingDocument = CreateGlobalUsingsDocument(projectId, sourceTextsByPath, projectProperties);
        if (globalUsingDocument is not null)
            documents.Add(globalUsingDocument);

        // ── Synthetic AssemblyInfo (SDK-generated attributes) ──
        var assemblyInfoDoc = CreateAssemblyInfoDocument(projectId, projectProperties);
        if (assemblyInfoDoc is not null)
            documents.Add(assemblyInfoDoc);

        var parseOptions = CreateParseOptions(projectProperties, generatedEditorConfig);
        var compilationOptions = CreateCompilationOptions(projectProperties);

        var projectName = !string.IsNullOrWhiteSpace(projectProperties.ProjectName)
            ? projectProperties.ProjectName
            : Path.GetFileNameWithoutExtension(projectFilePath ?? activeFilePath);

        var assemblyName = !string.IsNullOrWhiteSpace(projectProperties.AssemblyName)
            ? projectProperties.AssemblyName
            : projectName;

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: projectName,
            assemblyName: assemblyName,
            language: LanguageNames.CSharp,
            filePath: projectFilePath,
            compilationOptions: compilationOptions,
            parseOptions: parseOptions,
            documents: documents,
            metadataReferences: metadataReferences,
            analyzerReferences: analyzerReferences,
            additionalDocuments: additionalDocuments);

        var editorConfigPaths = new List<string>();
        var editorConfigPath = FindEditorConfig(projectDir);
        if (editorConfigPath is not null)
            editorConfigPaths.Add(editorConfigPath);

        return new RoslynProjectBuild(
            projectInfo,
            activeDocumentId,
            documentIds,
            userSourceFiles,
            metadataReferences.Count > baseReferences.Count,
            editorConfigPaths);
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string? ResolveProjectDirectory(string? projectContextPath, string activeFilePath)
    {
        var resolved = NuGetReferenceResolver.ResolveProjectDirectory(projectContextPath)
                       ?? NuGetReferenceResolver.ResolveProjectDirectory(activeFilePath);
        if (!string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved))
            return resolved;

        var activeDir = Path.GetDirectoryName(activeFilePath);
        return !string.IsNullOrWhiteSpace(activeDir) && Directory.Exists(activeDir)
            ? activeDir
            : null;
    }

    private static string? FindProjectFile(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return null;

        try
        {
            return Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateProjectSourceFiles(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (IsIgnoredPath(file))
                continue;

            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateGeneratedSourceFiles(string? projectDir, string? generatedEditorConfig)
    {
        var intermediateDir = !string.IsNullOrWhiteSpace(generatedEditorConfig)
            ? Path.GetDirectoryName(generatedEditorConfig)
            : FindLatestIntermediateDirectory(projectDir);

        if (string.IsNullOrWhiteSpace(intermediateDir) || !Directory.Exists(intermediateDir))
            yield break;

        string[] files;
        try
        {
            files = Directory.GetFiles(intermediateDir, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (file.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.Contains(Path.DirectorySeparatorChar + "refint" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateAdditionalFiles(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            yield break;

        string[] files;
        try
        {
            files = Directory.GetFiles(projectDir, "*.axaml", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (IsIgnoredPath(file))
                continue;

            yield return file;
        }
    }

    private static bool IsIgnoredPath(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindGeneratedEditorConfig(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return null;

        var objDir = Path.Combine(projectDir, "obj");
        if (!Directory.Exists(objDir))
            return null;

        try
        {
            return Directory.GetFiles(objDir, "*.GeneratedMSBuildEditorConfig.editorconfig", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindLatestIntermediateDirectory(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return null;

        var objDir = Path.Combine(projectDir, "obj");
        if (!Directory.Exists(objDir))
            return null;

        try
        {
            return Directory.GetDirectories(objDir, "*", SearchOption.AllDirectories)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static List<MetadataReference> ResolveProjectMetadataReferences(string? projectDir)
    {
        var resolver = new NuGetReferenceResolver();
        return resolver.Resolve(projectDir);
    }

    private static List<MetadataReference> MergeMetadataReferences(
        IReadOnlyCollection<MetadataReference> baseReferences,
        IReadOnlyCollection<MetadataReference> extraReferences)
    {
        var allReferences = new List<MetadataReference>(baseReferences);
        var addedPaths = new HashSet<string>(
            baseReferences.Select(r => r.Display).Where(static p => !string.IsNullOrWhiteSpace(p))!,
            StringComparer.OrdinalIgnoreCase);

        foreach (var extraReference in extraReferences)
        {
            if (string.IsNullOrWhiteSpace(extraReference.Display) || !addedPaths.Add(extraReference.Display))
                continue;

            allReferences.Add(extraReference);
        }

        return allReferences;
    }

    /// <summary>
    /// Attempt to resolve a reference by assembly name. Searches SDK ref pack
    /// directories and the project's own output directory.
    /// </summary>
    private static string? ResolveReferenceByAssemblyName(string assemblyName, string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return null;

        // Strip extension if provided
        var name = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assemblyName[..^4]
            : assemblyName;

        var searchDirs = new List<string>();

        // Add SDK ref pack directories
        var sdkRefDirs = GetSdkReferenceDirectories();
        searchDirs.AddRange(sdkRefDirs);

        // Add project output directory
        if (!string.IsNullOrWhiteSpace(projectDir))
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                foreach (var tfm in new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0" })
                {
                    var outDir = Path.Combine(projectDir, "bin", config, tfm);
                    if (Directory.Exists(outDir))
                        searchDirs.Add(outDir);
                }
            }
        }

        foreach (var dir in searchDirs)
        {
            var candidate = Path.Combine(dir, name + ".dll");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Try to resolve a COM reference to its interop assembly.
    /// Searches: project obj/ and bin/ directories, typical interop locations.
    /// </summary>
    private static string? ResolveComReference(ComReferenceInfo comRef, string? projectDir)
    {
        var searchPatterns = new List<string>();

        // Common interop DLL naming conventions
        var nameVariants = new List<string> { comRef.Include };
        if (!comRef.Include.EndsWith("Lib", StringComparison.OrdinalIgnoreCase))
            nameVariants.Add(comRef.Include + "Lib");
        nameVariants.Add("Interop." + comRef.Include);
        nameVariants.Add(comRef.Include + ".Interop");

        foreach (var dir in EnumeratePossibleInteropDirs(projectDir))
        {
            foreach (var variant in nameVariants)
            {
                var dll = Path.Combine(dir, variant + ".dll");
                if (File.Exists(dll)) return dll;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePossibleInteropDirs(string? projectDir)
    {
        // Project obj/ and bin/ directories
        if (!string.IsNullOrWhiteSpace(projectDir))
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                foreach (var tfm in new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0" })
                {
                    yield return Path.Combine(projectDir, "bin", config, tfm);
                    yield return Path.Combine(projectDir, "obj", config, tfm);
                    yield return Path.Combine(projectDir, "obj", config, tfm, "Interop");
                    yield return Path.Combine(projectDir, "obj", config, tfm, "TEMP");
                }
            }
        }

        // User TEMP (where TlbImp sometimes writes)
        var temp = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(temp))
            yield return temp;
    }

    private static List<string> GetSdkReferenceDirectories()
    {
        var dirs = new List<string>();
        var dotnetRoots = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"),
        };

        foreach (var root in dotnetRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            foreach (var pack in new[] { "Microsoft.NETCore.App.Ref", "Microsoft.WindowsDesktop.App.Ref" })
            {
                var packDir = Path.Combine(root, "packs", pack);
                if (!Directory.Exists(packDir)) continue;
                try
                {
                    var newest = Directory.GetDirectories(packDir)
                        .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (newest is null) continue;
                    foreach (var tfm in new[] { "net10.0", "net9.0", "net8.0", "net7.0", "net6.0" })
                    {
                        var refDir = Path.Combine(newest, "ref", tfm);
                        if (Directory.Exists(refDir)) { dirs.Add(refDir); break; }
                    }
                }
                catch { }
            }
        }

        return dirs;
    }

    private static List<AnalyzerReference> ResolveAnalyzerReferences(string? projectFilePath, ParsedProjectItems projectItems)
    {
        var references = new List<AnalyzerReference>();
        var loader = RoslynAnalyzerAssemblyLoader.Instance;
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Resolve NuGet package analyzers (from <PackageReference> analyzer/ directories)
        if (!string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath))
        {
            var globalPackages = GetNuGetGlobalCacheDirectory();

            foreach (var (packageId, version) in ParsePackageReferences(projectFilePath))
            {
                var packageDir = Path.Combine(globalPackages, packageId.ToLowerInvariant(), version);
                if (!Directory.Exists(packageDir))
                    continue;

                IEnumerable<string> analyzerDlls;
                try
                {
                    analyzerDlls = Directory.EnumerateFiles(Path.Combine(packageDir, "analyzers"), "*.dll", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (var analyzerDll in analyzerDlls)
                {
                    if (!addedPaths.Add(analyzerDll))
                        continue;

                    try
                    {
                        foreach (var dependency in Directory.GetFiles(Path.GetDirectoryName(analyzerDll)!, "*.dll", SearchOption.TopDirectoryOnly))
                            loader.AddDependencyLocation(dependency);

                        references.Add(new AnalyzerFileReference(analyzerDll, loader));
                    }
                    catch
                    {
                    }
                }
            }
        }

        // 2. Add <Analyzer> items from .csproj (direct file paths)
        var projectDir = !string.IsNullOrWhiteSpace(projectFilePath)
            ? Path.GetDirectoryName(projectFilePath)
            : null;

        foreach (var analyzerPath in projectItems.AnalyzerPaths)
        {
            var fullPath = projectDir is not null
                ? Path.GetFullPath(Path.Combine(projectDir, analyzerPath))
                : Path.GetFullPath(analyzerPath);

            if (!File.Exists(fullPath) || !addedPaths.Add(fullPath))
                continue;

            try
            {
                references.Add(new AnalyzerFileReference(fullPath, loader));
            }
            catch
            {
            }
        }

        return references;
    }

    private static IEnumerable<(string PackageId, string Version)> ParsePackageReferences(string projectFilePath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectFilePath);
        }
        catch
        {
            yield break;
        }

        foreach (var packageReference in document.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var packageId = packageReference.Attribute("Include")?.Value;
            var version = packageReference.Attribute("Version")?.Value
                          ?? packageReference.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

            if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(version))
                yield return (packageId, version);
        }
    }

    // ── Project item types (parsed from .csproj ItemGroup elements) ──

    private sealed record ReferenceInfo(string Include, string? HintPath, string? Aliases);

    private sealed record ComReferenceInfo(
        string Include, string? Guid, int? VersionMajor, int? VersionMinor, int? Lcid, string? WrapperTool);

    private sealed record LinkedFileInfo(string Include, string? Link);

    private sealed record ParsedProjectItems(
        IReadOnlyList<string> AdditionalFiles,
        IReadOnlyList<string> AnalyzerPaths,
        IReadOnlyList<ReferenceInfo> References,
        IReadOnlyList<ComReferenceInfo> ComReferences,
        IReadOnlyList<LinkedFileInfo> LinkedFiles,
        IReadOnlyList<string> SharedProjectImports);

    private static ParsedProjectItems ParseProjectItems(string csprojPath)
    {
        var additionalFiles = new List<string>();
        var analyzerPaths = new List<string>();
        var references = new List<ReferenceInfo>();
        var comReferences = new List<ComReferenceInfo>();
        var linkedFiles = new List<LinkedFileInfo>();
        var sharedProjectImports = new List<string>();

        if (!File.Exists(csprojPath))
            return new ParsedProjectItems(additionalFiles, analyzerPaths, references, comReferences, linkedFiles, sharedProjectImports);

        try
        {
            var doc = XDocument.Load(csprojPath);
            foreach (var item in doc.Descendants())
            {
                switch (item.Name.LocalName)
                {
                    case "AdditionalFiles":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            additionalFiles.Add(include);
                        break;
                    }
                    case "Analyzer":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (!string.IsNullOrWhiteSpace(include))
                            analyzerPaths.Add(include);
                        break;
                    }
                    case "Reference":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (string.IsNullOrWhiteSpace(include)) break;
                        var hintPath = item.Elements()
                            .FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value;
                        var aliases = item.Attribute("Aliases")?.Value;
                        references.Add(new ReferenceInfo(include, hintPath, aliases));
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
                        comReferences.Add(new ComReferenceInfo(include, guid, vmaj, vmin, lcid, wrapper));
                        break;
                    }
                    case "Compile":
                    {
                        var include = item.Attribute("Include")?.Value;
                        if (string.IsNullOrWhiteSpace(include)) break;
                        var link = item.Element(item.Name.Namespace + "Link")?.Value
                                   ?? item.Attribute("Link")?.Value;
                        linkedFiles.Add(new LinkedFileInfo(include, link));
                        break;
                    }
                }
            }

            // Parse <Import> elements to find shared project references (.shproj)
            foreach (var import in doc.Descendants().Where(e => e.Name.LocalName == "Import"))
            {
                var project = import.Attribute("Project")?.Value;
                if (!string.IsNullOrWhiteSpace(project) &&
                    project.EndsWith(".shproj", StringComparison.OrdinalIgnoreCase))
                {
                    sharedProjectImports.Add(project);
                }
            }
        }
        catch
        {
        }

        return new ParsedProjectItems(additionalFiles, analyzerPaths, references, comReferences, linkedFiles, sharedProjectImports);
    }

    private static int? TryParseInt(string? s)
        => int.TryParse(s, out var v) ? v : null;

    private static string GetNuGetGlobalCacheDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    private static Dictionary<string, string> LoadEvaluatedBuildProperties(string? generatedEditorConfig)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(generatedEditorConfig) || !File.Exists(generatedEditorConfig))
            return result;

        try
        {
            foreach (var rawLine in File.ReadLines(generatedEditorConfig))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                    break;

                if (!line.StartsWith("build_property.", StringComparison.OrdinalIgnoreCase))
                    continue;

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                var key = line.Substring("build_property.".Length, separatorIndex - "build_property.".Length).Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                    result[key] = value;
            }
        }
        catch
        {
            // Ignore malformed editorconfig files.
        }

        return result;
    }

    private static ProjectProperties LoadProjectProperties(string? projectFilePath, IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        var properties = new ProjectProperties();

        if (!string.IsNullOrWhiteSpace(projectFilePath))
        {
            properties.ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);
            properties.AssemblyName = properties.ProjectName;
        }

        if (!string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath))
        {
            try
            {
                var document = XDocument.Load(projectFilePath);
                var propertyElements = document.Descendants().Where(e => e.Parent?.Name.LocalName == "PropertyGroup");

                var xElements = propertyElements as XElement[] ?? propertyElements.ToArray();
                properties.AssemblyName = GetProperty(xElements, "AssemblyName") ?? properties.AssemblyName;
                properties.RootNamespace = GetProperty(xElements, "RootNamespace") ?? properties.RootNamespace;
                properties.Nullable = GetProperty(xElements, "Nullable") ?? properties.Nullable;
                properties.LangVersion = GetProperty(xElements, "LangVersion") ?? properties.LangVersion;
                properties.DefineConstants = GetProperty(xElements, "DefineConstants") ?? properties.DefineConstants;
                properties.OutputType = GetProperty(xElements, "OutputType") ?? properties.OutputType;
                properties.AllowUnsafe = GetProperty(xElements, "AllowUnsafeBlocks") ?? properties.AllowUnsafe;
                properties.TreatWarningsAsErrors = GetProperty(xElements, "TreatWarningsAsErrors") ?? properties.TreatWarningsAsErrors;
                properties.WarningLevel = GetProperty(xElements, "WarningLevel") ?? properties.WarningLevel;
                properties.NoWarn = GetProperty(xElements, "NoWarn") ?? properties.NoWarn;
                properties.WarningsAsErrors = GetProperty(xElements, "WarningsAsErrors") ?? properties.WarningsAsErrors;
                properties.WarningsNotAsErrors = GetProperty(xElements, "WarningsNotAsErrors") ?? properties.WarningsNotAsErrors;
                properties.CheckForOverflowUnderflow = GetProperty(xElements, "CheckForOverflowUnderflow") ?? properties.CheckForOverflowUnderflow;
                properties.Deterministic = GetProperty(xElements, "Deterministic") ?? properties.Deterministic;
                properties.Optimize = GetProperty(xElements, "Optimize") ?? properties.Optimize;
                properties.ImplicitUsings = GetProperty(xElements, "ImplicitUsings") ?? properties.ImplicitUsings;
                properties.TargetFrameworks = GetProperty(xElements, "TargetFrameworks") ?? properties.TargetFrameworks;
            }
            catch
            {
                // Ignore malformed project XML and keep best-effort defaults.
            }
        }

        if (evaluatedProperties.TryGetValue("RootNamespace", out var rootNamespace) && !string.IsNullOrWhiteSpace(rootNamespace))
            properties.RootNamespace = rootNamespace;

        if (evaluatedProperties.TryGetValue("TargetFramework", out var targetFramework) && !string.IsNullOrWhiteSpace(targetFramework))
            properties.TargetFramework = targetFramework;

        if (evaluatedProperties.TryGetValue("TargetFrameworks", out var targetFrameworks) && !string.IsNullOrWhiteSpace(targetFrameworks))
            properties.TargetFrameworks = targetFrameworks;

        if (evaluatedProperties.TryGetValue("DefineConstants", out var defineConstants) && !string.IsNullOrWhiteSpace(defineConstants))
            properties.DefineConstants = defineConstants;

        if (evaluatedProperties.TryGetValue("ImplicitUsings", out var implicitUsings) && !string.IsNullOrWhiteSpace(implicitUsings))
            properties.ImplicitUsings = implicitUsings;

        if (evaluatedProperties.TryGetValue("Nullable", out var nullable) && !string.IsNullOrWhiteSpace(nullable))
            properties.Nullable = nullable;

        if (evaluatedProperties.TryGetValue("LangVersion", out var langVersion) && !string.IsNullOrWhiteSpace(langVersion))
            properties.LangVersion = langVersion;

        if (string.IsNullOrWhiteSpace(properties.RootNamespace) && !string.IsNullOrWhiteSpace(properties.AssemblyName))
            properties.RootNamespace = SanitizeNamespace(properties.AssemblyName);

        return properties;
    }

    private static string? GetProperty(IEnumerable<XElement> propertyElements, string localName)
        => propertyElements.FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static CSharpParseOptions CreateParseOptions(ProjectProperties properties, string? generatedEditorConfig)
    {
        var languageVersion = LanguageVersion.Latest;
        if (!string.IsNullOrWhiteSpace(properties.LangVersion) &&
            LanguageVersionFacts.TryParse(properties.LangVersion, out var parsedLanguageVersion))
        {
            languageVersion = parsedLanguageVersion;
        }

        var preprocessorSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(properties.DefineConstants))
        {
            foreach (var symbol in properties.DefineConstants.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                preprocessorSymbols.Add(symbol.Trim());
        }

        var normalizedConfigPath = NormalizePath(generatedEditorConfig ?? string.Empty);
        if (normalizedConfigPath.Contains(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            preprocessorSymbols.Add("DEBUG");
            preprocessorSymbols.Add("TRACE");
        }

        return new CSharpParseOptions(languageVersion)
            .WithDocumentationMode(DocumentationMode.Diagnose)
            .WithPreprocessorSymbols(preprocessorSymbols);
    }

    private static CSharpCompilationOptions CreateCompilationOptions(ProjectProperties properties)
    {
        var outputKind = properties.OutputType?.Trim() switch
        {
            "Exe" => OutputKind.ConsoleApplication,
            "WinExe" => OutputKind.WindowsApplication,
            _ => OutputKind.DynamicallyLinkedLibrary,
        };

        var nullableContextOptions = properties.Nullable?.Trim().ToLowerInvariant() switch
        {
            "enable" => NullableContextOptions.Enable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            "disable" => NullableContextOptions.Disable,
            _ => NullableContextOptions.Disable,
        };

        var allowUnsafe = string.Equals(properties.AllowUnsafe, "true", StringComparison.OrdinalIgnoreCase);
        var treatWarningsAsErrors = string.Equals(properties.TreatWarningsAsErrors, "true", StringComparison.OrdinalIgnoreCase);
        var checkForOverflowUnderflow = string.Equals(properties.CheckForOverflowUnderflow, "true", StringComparison.OrdinalIgnoreCase);
        var deterministic = string.Equals(properties.Deterministic, "true", StringComparison.OrdinalIgnoreCase);
        var optimize = string.Equals(properties.Optimize, "true", StringComparison.OrdinalIgnoreCase);

        var warningLevel = 4;
        if (int.TryParse(properties.WarningLevel, out var parsedWl) && parsedWl >= 0 && parsedWl <= 4)
            warningLevel = parsedWl;

        var specificDiagOptions = new Dictionary<string, ReportDiagnostic>();
        if (!string.IsNullOrWhiteSpace(properties.NoWarn))
        {
            foreach (var id in properties.NoWarn.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = id.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    specificDiagOptions[trimmed] = ReportDiagnostic.Suppress;
            }
        }

        ReportDiagnostic generalDiagOption;
        if (treatWarningsAsErrors)
        {
            generalDiagOption = ReportDiagnostic.Error;
            if (!string.IsNullOrWhiteSpace(properties.WarningsNotAsErrors))
            {
                foreach (var id in properties.WarningsNotAsErrors.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = id.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !specificDiagOptions.ContainsKey(trimmed))
                        specificDiagOptions[trimmed] = ReportDiagnostic.Warn;
                }
            }
            if (!string.IsNullOrWhiteSpace(properties.WarningsAsErrors))
            {
                foreach (var id in properties.WarningsAsErrors.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = id.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !specificDiagOptions.ContainsKey(trimmed))
                        specificDiagOptions[trimmed] = ReportDiagnostic.Error;
                }
            }
        }
        else
        {
            generalDiagOption = ReportDiagnostic.Default;
            if (!string.IsNullOrWhiteSpace(properties.WarningsAsErrors))
            {
                foreach (var id in properties.WarningsAsErrors.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = id.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !specificDiagOptions.ContainsKey(trimmed))
                        specificDiagOptions[trimmed] = ReportDiagnostic.Error;
                }
            }
        }

        return new CSharpCompilationOptions(outputKind)
            .WithNullableContextOptions(nullableContextOptions)
            .WithAllowUnsafe(allowUnsafe)
            .WithOverflowChecks(checkForOverflowUnderflow)
            .WithOptimizationLevel(optimize ? OptimizationLevel.Release : OptimizationLevel.Debug)
            .WithDeterministic(deterministic)
            .WithWarningLevel(warningLevel)
            .WithGeneralDiagnosticOption(generalDiagOption)
            .WithSpecificDiagnosticOptions(specificDiagOptions);
    }

    private static DocumentInfo CreateSourceDocument(DocumentId documentId, string filePath, string text)
    {
        return DocumentInfo.Create(
            documentId,
            name: filePath,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
            filePath: filePath);
    }

    private static DocumentInfo CreateTextDocument(ProjectId projectId, string filePath, string text)
    {
        return DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name: filePath,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
            filePath: filePath);
    }

    private static IEnumerable<DocumentInfo> CreateSyntheticAvaloniaDocuments(
        ProjectId projectId,
        IEnumerable<string> additionalFiles,
        IReadOnlyDictionary<string, string> sourceTextsByPath)
    {
        foreach (var additionalFile in additionalFiles)
        {
            if (!additionalFile.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
                continue;

            var generatedSource = TryCreateAvaloniaInitializeComponentStub(additionalFile, sourceTextsByPath);
            if (generatedSource is null)
                continue;

            var syntheticPath = NormalizePath(additionalFile) + ".Roslyn.Avalonia.g.cs";
            yield return CreateSourceDocument(DocumentId.CreateNewId(projectId), syntheticPath, generatedSource);
        }
    }

    private static string? TryCreateAvaloniaInitializeComponentStub(
        string axamlFilePath,
        IReadOnlyDictionary<string, string> sourceTextsByPath)
    {
        var axamlText = TryReadAllText(axamlFilePath);
        if (string.IsNullOrWhiteSpace(axamlText))
            return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(axamlText, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return null;
        }

        var root = document.Root;
        if (root is null)
            return null;

        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var fullClassName = root.Attribute(xNamespace + "Class")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(fullClassName))
            return null;

        var lastDotIndex = fullClassName.LastIndexOf('.');
        var namespaceName = lastDotIndex >= 0 ? fullClassName[..lastDotIndex] : string.Empty;
        var className = lastDotIndex >= 0 ? fullClassName[(lastDotIndex + 1)..] : fullClassName;
        if (string.IsNullOrWhiteSpace(className))
            return null;

        var relatedPartialClassTexts = sourceTextsByPath.Values
            .Where(text => DefinesPartialClass(text, className))
            .ToList();

        if (relatedPartialClassTexts.Count == 0)
            return null;

        if (relatedPartialClassTexts.Any(DefinesInitializeComponent))
            return null;

        var accessibility = relatedPartialClassTexts
            .Select(text => GetPartialClassAccessibility(text, className))
            .FirstOrDefault(static access => !string.IsNullOrWhiteSpace(access));

        var classDeclaration = string.IsNullOrWhiteSpace(accessibility)
            ? $"partial class {className}"
            : $"{accessibility} partial class {className}";

        return string.IsNullOrWhiteSpace(namespaceName)
            ? $"// <auto-generated />{Environment.NewLine}{classDeclaration}{Environment.NewLine}{{{Environment.NewLine}    private void InitializeComponent(){{ }}{Environment.NewLine}}}{Environment.NewLine}"
            : $"// <auto-generated />{Environment.NewLine}namespace {namespaceName};{Environment.NewLine}{Environment.NewLine}{classDeclaration}{Environment.NewLine}{{{Environment.NewLine}    private void InitializeComponent(){{ }}{Environment.NewLine}}}{Environment.NewLine}";
    }

    private static bool DefinesPartialClass(string sourceText, string className)
        => PartialClassRegex.Matches(sourceText)
            .Cast<Match>()
            .Any(match => string.Equals(match.Groups["name"].Value, className, StringComparison.Ordinal));

    private static string? GetPartialClassAccessibility(string sourceText, string className)
        => PartialClassRegex.Matches(sourceText)
            .Cast<Match>()
            .Where(match => string.Equals(match.Groups["name"].Value, className, StringComparison.Ordinal))
            .Select(match => match.Groups["access"].Value.Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static bool DefinesInitializeComponent(string sourceText)
        => InitializeComponentDefinitionRegex.IsMatch(sourceText);

    private static string? TryReadAllText(string filePath)
    {
        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the effective target framework from <c>TargetFramework</c> or
    /// <c>TargetFrameworks</c> (multi-targeting). Returns the first entry.
    /// </summary>
    private static string? ResolveTargetFramework(ProjectProperties properties)
    {
        if (!string.IsNullOrWhiteSpace(properties.TargetFramework))
            return properties.TargetFramework;

        if (!string.IsNullOrWhiteSpace(properties.TargetFrameworks))
        {
            var tfms = properties.TargetFrameworks.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return tfms.Select(static t => t.Trim()).FirstOrDefault(static t => !string.IsNullOrWhiteSpace(t));
        }

        return null;
    }

    /// <summary>
    /// Creates a synthetic <c>AssemblyInfo.g.cs</c> document with common assembly-level
    /// attributes that the .NET SDK normally generates during build. Without this,
    /// analyzers may report false positives (e.g. CS0114 on types with assembly attributes).
    /// Returns <c>null</c> if the project name is unavailable.
    /// </summary>
    private static DocumentInfo? CreateAssemblyInfoDocument(ProjectId projectId, ProjectProperties properties)
    {
        var projectName = properties.AssemblyName ?? properties.ProjectName;
        if (string.IsNullOrWhiteSpace(projectName))
            return null;

        var guid = GuidFromString(projectName);
        var tfm = ResolveTargetFramework(properties) ?? "net10.0";
        var config = "Debug";
        var source = $@"// <auto-generated />
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyVersion(""1.0.0.0"")]
[assembly: AssemblyFileVersion(""1.0.0.0"")]
[assembly: AssemblyCompany("""")]
[assembly: AssemblyConfiguration(""{config}"")]
[assembly: AssemblyCopyright("""")]
[assembly: AssemblyDescription("""")]
[assembly: AssemblyProduct(""{projectName}"")]
[assembly: AssemblyTitle(""{projectName}"")]
[assembly: AssemblyTrademark("""")]
[assembly: ComVisible(false)]
[assembly: Guid(""{guid}"")]
";

        return DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name: "AssemblyInfo.g.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source, Encoding.UTF8), VersionStamp.Create())),
            filePath: Path.Combine(Path.GetTempPath(), "RoslynAssemblyInfo.g.cs"));
    }

    private static string GuidFromString(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.Take(16).ToArray()).ToString("D");
    }

    /// <summary>
    /// Finds the closest <c>.editorconfig</c> file starting from the project directory
    /// and walking up. Returns <c>null</c> if none found.
    /// </summary>
    private static string? FindEditorConfig(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return null;

        var dir = Path.GetFullPath(projectDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, ".editorconfig");
            if (File.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(dir, parent, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent;
        }

        return null;
    }

    private static string SanitizeNamespace(string value)
    {
        var chars = value.Select(static ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('.');
        if (string.IsNullOrWhiteSpace(sanitized))
            return "RoslynLiveProject";

        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
    }

    /// <summary>
    /// Creates a synthetic source document containing all effective global usings:
    ///   1. <c>global using</c> directives extracted from every source file in the project.
    ///   2. Standard implicit usings if <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> is set.
    ///
    /// Returns <c>null</c> when there are no global usings to add.
    /// </summary>
    private static DocumentInfo? CreateGlobalUsingsDocument(
        ProjectId projectId,
        IReadOnlyDictionary<string, string> sourceTextsByPath,
        ProjectProperties properties)
    {
        var usings = new HashSet<string>(StringComparer.Ordinal);
        var isImplicitUsingsEnabled = string.Equals(properties.ImplicitUsings, "enable", StringComparison.OrdinalIgnoreCase);
        var isWebSdk = properties.TargetFramework?.Contains("aspnet", StringComparison.OrdinalIgnoreCase) == true
                       || properties.OutputType == "Web";

        // 1. Collect explicit `global using` directives from all source files
        foreach (var text in sourceTextsByPath.Values)
        {
            foreach (Match match in GlobalUsingRegex.Matches(text))
            {
                var ns = match.Groups["ns"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(ns))
                    usings.Add(ns);
            }
        }

        // 2. Add implicit usings if enabled
        if (isImplicitUsingsEnabled)
        {
            foreach (var ns in DefaultImplicitUsings)
                usings.Add(ns);

            if (isWebSdk)
            {
                foreach (var ns in WebImplicitUsings)
                    usings.Add(ns);
            }
        }

        if (usings.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated /> — Global Usings");
        foreach (var ns in usings.OrderBy(static n => n, StringComparer.Ordinal))
            sb.Append("global using ").Append(ns).AppendLine(";");

        var sourceText = SourceText.From(sb.ToString(), Encoding.UTF8);
        return DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            name: "GlobalUsings.g.cs",
            loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Create())),
            filePath: Path.Combine(Path.GetTempPath(), "RoslynGlobalUsings.g.cs"));
    }

    private sealed class ProjectProperties
    {
        public string? ProjectName { get; set; }
        public string? AssemblyName { get; set; }
        public string? RootNamespace { get; set; }
        public string? Nullable { get; set; }
        public string? LangVersion { get; set; }
        public string? DefineConstants { get; set; }
        public string? OutputType { get; set; }
        public string? AllowUnsafe { get; set; }
        public string? TargetFramework { get; set; }
        public string? TargetFrameworks { get; set; }
        public string? TreatWarningsAsErrors { get; set; }
        public string? WarningLevel { get; set; }
        public string? NoWarn { get; set; }
        public string? WarningsAsErrors { get; set; }
        public string? WarningsNotAsErrors { get; set; }
        public string? CheckForOverflowUnderflow { get; set; }
        public string? Deterministic { get; set; }
        public string? Optimize { get; set; }
        public string? ImplicitUsings { get; set; }
    }
}

internal sealed class RoslynAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    public static RoslynAnalyzerAssemblyLoader Instance { get; } = new();

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
