using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

    public RoslynProjectBuild(
        ProjectInfo projectInfo,
        DocumentId activeDocumentId,
        IReadOnlyDictionary<string, DocumentId> documentIds,
        IReadOnlySet<string> userSourceFiles,
        bool hasProjectMetadataReferences)
    {
        ProjectInfo = projectInfo;
        ActiveDocumentId = activeDocumentId;
        DocumentIds = documentIds;
        UserSourceFiles = userSourceFiles;
        HasProjectMetadataReferences = hasProjectMetadataReferences;
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
            : new ParsedProjectItems(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<(string, string?)>());

        // Add <AdditionalFiles> items from .csproj to the additional files list
        foreach (var af in projectItems.AdditionalFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectDir!, af));
            if (File.Exists(fullPath) && !additionalFiles.Contains(fullPath, PathComparer))
                additionalFiles.Add(fullPath);
        }

        // Resolve <Reference> items with HintPath as metadata references
        foreach (var (include, hintPath) in projectItems.References)
        {
            if (string.IsNullOrWhiteSpace(hintPath)) continue;
            var fullPath = Path.GetFullPath(Path.Combine(projectDir!, hintPath));
            if (File.Exists(fullPath))
            {
                try { metadataReferences.Add(MetadataReference.CreateFromFile(fullPath)); }
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

        return new RoslynProjectBuild(
            projectInfo,
            activeDocumentId,
            documentIds,
            userSourceFiles,
            metadataReferences.Count > baseReferences.Count);
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

    private sealed record ParsedProjectItems(
        IReadOnlyList<string> AdditionalFiles,
        IReadOnlyList<string> AnalyzerPaths,
        IReadOnlyList<(string Include, string? HintPath)> References);

    private static ParsedProjectItems ParseProjectItems(string csprojPath)
    {
        var additionalFiles = new List<string>();
        var analyzerPaths = new List<string>();
        var references = new List<(string, string?)>();

        if (!File.Exists(csprojPath))
            return new ParsedProjectItems(additionalFiles, analyzerPaths, references);

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
                        references.Add((include, hintPath));
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return new ParsedProjectItems(additionalFiles, analyzerPaths, references);
    }

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

        if (evaluatedProperties.TryGetValue("DefineConstants", out var defineConstants) && !string.IsNullOrWhiteSpace(defineConstants))
            properties.DefineConstants = defineConstants;

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

        return new CSharpCompilationOptions(outputKind)
            .WithNullableContextOptions(nullableContextOptions)
            .WithAllowUnsafe(allowUnsafe);
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

    private static string SanitizeNamespace(string value)
    {
        var chars = value.Select(static ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('.');
        if (string.IsNullOrWhiteSpace(sanitized))
            return "RoslynLiveProject";

        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
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
