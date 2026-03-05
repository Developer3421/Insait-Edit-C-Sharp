using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Resolves NuGet package references from a .csproj file into Roslyn MetadataReferences.
///
/// Strategy (ordered by reliability):
///  1. Parse obj/project.assets.json (created by `dotnet restore`) — contains the exact
///     resolved DLL paths for every transitive dependency.
///  2. Scan the NuGet global cache (~/.nuget/packages/) for each PackageReference from
///     the .csproj. This is a fallback when the project has not been restored yet.
///  3. Scan the bin/ output folder for any DLLs (last resort).
/// </summary>
public sealed class NuGetReferenceResolver
{
    // Cache: projectDir → list of references (rebuilt when project context changes)
    private string? _cachedProjectDir;
    private List<MetadataReference>? _cachedRefs;

    /// <summary>
    /// Returns MetadataReferences for all NuGet packages referenced by the project
    /// in the given directory.  The result is cached until <paramref name="projectDir"/> changes.
    /// </summary>
    public List<MetadataReference> Resolve(string? projectDir)
    {
        if (string.IsNullOrEmpty(projectDir))
            return new List<MetadataReference>();

        // Return cache if same directory
        if (_cachedRefs != null &&
            string.Equals(_cachedProjectDir, projectDir, StringComparison.OrdinalIgnoreCase))
            return _cachedRefs;

        _cachedProjectDir = projectDir;
        _cachedRefs = ResolveCore(projectDir);
        return _cachedRefs;
    }

    /// <summary>Invalidates the cache so the next Resolve() call re-scans.</summary>
    public void InvalidateCache()
    {
        _cachedRefs = null;
        _cachedProjectDir = null;
    }

    // ── core resolution ──────────────────────────────────────────────────────

    private static List<MetadataReference> ResolveCore(string projectDir)
    {
        var refs = new List<MetadataReference>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find .csproj in the directory
        var csprojPath = FindCsproj(projectDir);

        // Strategy 1: project.assets.json (best — full transitive closure)
        var assetsRefs = ResolveFromAssetsJson(projectDir);
        foreach (var r in assetsRefs)
            if (addedPaths.Add(r.path))
                refs.Add(r.reference);

        // Strategy 2: NuGet global cache + PackageReference from .csproj
        if (csprojPath != null)
        {
            var packages = ParsePackageReferences(csprojPath);
            var globalCacheDir = GetNuGetGlobalCacheDir();

            foreach (var (id, version) in packages)
            {
                var dlls = FindPackageDlls(globalCacheDir, id, version);
                foreach (var dll in dlls)
                {
                    if (addedPaths.Add(dll))
                        TryAdd(refs, dll);
                }
            }
        }

        // Strategy 3: bin/ folder scan (picks up anything that was built)
        var binRefs = ScanBinFolder(projectDir);
        foreach (var dll in binRefs)
        {
            if (addedPaths.Add(dll))
                TryAdd(refs, dll);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[NuGetRefResolver] Resolved {refs.Count} package references for {projectDir}");

        return refs;
    }

    // ── Strategy 1: project.assets.json ──────────────────────────────────────

    private static List<(string path, MetadataReference reference)> ResolveFromAssetsJson(string projectDir)
    {
        var result = new List<(string, MetadataReference)>();

        // Look in obj/ folder for project.assets.json
        var objDir = Path.Combine(projectDir, "obj");
        var assetsPath = Path.Combine(objDir, "project.assets.json");

        if (!File.Exists(assetsPath))
            return result;

        try
        {
            var json = File.ReadAllText(assetsPath);
            using var doc = JsonDocument.Parse(json);

            // Get the packageFolders to know where NuGet cache is
            var packageFolders = new List<string>();
            if (doc.RootElement.TryGetProperty("packageFolders", out var foldersElem))
            {
                foreach (var folder in foldersElem.EnumerateObject())
                    packageFolders.Add(folder.Name);
            }

            // Parse "targets" → first target framework → each package
            if (!doc.RootElement.TryGetProperty("targets", out var targets))
                return result;

            foreach (var tfm in targets.EnumerateObject())
            {
                foreach (var package in tfm.Value.EnumerateObject())
                {
                    // package.Name is like "Avalonia/11.0.0"
                    if (!package.Value.TryGetProperty("runtime", out var runtime))
                        continue;

                    // Each runtime entry has a relative DLL path
                    foreach (var runtimeEntry in runtime.EnumerateObject())
                    {
                        var relativeDllPath = runtimeEntry.Name;
                        if (!relativeDllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Construct full path:  packageFolder / packageId / version / relativePath
                        var packageParts = package.Name.Split('/');
                        if (packageParts.Length < 2) continue;

                        var packageId = packageParts[0].ToLowerInvariant();
                        var packageVersion = packageParts[1];

                        foreach (var cacheDir in packageFolders)
                        {
                            var fullPath = Path.Combine(cacheDir, packageId, packageVersion, relativeDllPath);
                            fullPath = Path.GetFullPath(fullPath); // normalize
                            if (File.Exists(fullPath))
                            {
                                try
                                {
                                    var metaRef = MetadataReference.CreateFromFile(fullPath);
                                    result.Add((fullPath, metaRef));
                                }
                                catch { /* corrupted DLL, skip */ }
                                break; // found in this cache dir
                            }
                        }
                    }

                    // Also check "compile" section (compile-time-only assemblies, e.g. ref/ folder)
                    if (package.Value.TryGetProperty("compile", out var compile))
                    {
                        foreach (var compileEntry in compile.EnumerateObject())
                        {
                            var relativeDllPath = compileEntry.Name;
                            if (!relativeDllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                continue;
                            // Skip "_._" placeholder files
                            if (relativeDllPath.EndsWith("_._"))
                                continue;

                            var packageParts = package.Name.Split('/');
                            if (packageParts.Length < 2) continue;

                            var packageId = packageParts[0].ToLowerInvariant();
                            var packageVersion = packageParts[1];

                            foreach (var cacheDir in packageFolders)
                            {
                                var fullPath = Path.Combine(cacheDir, packageId, packageVersion, relativeDllPath);
                                fullPath = Path.GetFullPath(fullPath);
                                if (File.Exists(fullPath))
                                {
                                    try
                                    {
                                        var metaRef = MetadataReference.CreateFromFile(fullPath);
                                        result.Add((fullPath, metaRef));
                                    }
                                    catch { }
                                    break;
                                }
                            }
                        }
                    }
                }

                // Only process the first target framework
                break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NuGetRefResolver] Error reading project.assets.json: {ex.Message}");
        }

        return result;
    }

    // ── Strategy 2: PackageReference + global NuGet cache ─────────────────────

    private static List<(string Id, string Version)> ParsePackageReferences(string csprojPath)
    {
        var packages = new List<(string, string)>();

        try
        {
            var doc = XDocument.Load(csprojPath);
            var packageRefs = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference");

            foreach (var pr in packageRefs)
            {
                var id = pr.Attribute("Include")?.Value;
                var version = pr.Attribute("Version")?.Value
                              ?? pr.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    packages.Add((id, version));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NuGetRefResolver] Error parsing .csproj: {ex.Message}");
        }

        return packages;
    }

    private static string GetNuGetGlobalCacheDir()
    {
        // Standard NuGet global-packages folder
        var nugetPkgs = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(nugetPkgs) && Directory.Exists(nugetPkgs))
            return nugetPkgs;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".nuget", "packages");
    }

    private static List<string> FindPackageDlls(string globalCacheDir, string packageId, string version)
    {
        var dlls = new List<string>();

        // NuGet stores packages as: globalCacheDir/packageid.lowercase/version/
        var packageDir = Path.Combine(globalCacheDir, packageId.ToLowerInvariant(), version);
        if (!Directory.Exists(packageDir))
            return dlls;

        // Look for DLLs in lib/ folder, preferring the highest matching TFM
        var libDir = Path.Combine(packageDir, "lib");
        if (Directory.Exists(libDir))
        {
            // Prefer net8.0 → net7.0 → net6.0 → netstandard2.1 → netstandard2.0
            var preferredTfms = new[]
            {
                "net9.0", "net8.0", "net7.0", "net6.0",
                "netstandard2.1", "netstandard2.0",
                "netcoreapp3.1", "netcoreapp3.0",
            };

            string? bestTfmDir = null;
            foreach (var tfm in preferredTfms)
            {
                var tfmDir = Path.Combine(libDir, tfm);
                if (Directory.Exists(tfmDir))
                {
                    bestTfmDir = tfmDir;
                    break;
                }
            }

            // If no preferred TFM found, try the first available
            if (bestTfmDir == null)
            {
                try
                {
                    bestTfmDir = Directory.GetDirectories(libDir).FirstOrDefault();
                }
                catch { }
            }

            if (bestTfmDir != null)
            {
                try
                {
                    foreach (var dll in Directory.GetFiles(bestTfmDir, "*.dll"))
                        dlls.Add(dll);
                }
                catch { }
            }
        }

        // Also look in ref/ folder (reference assemblies used for compilation)
        var refDir = Path.Combine(packageDir, "ref");
        if (Directory.Exists(refDir) && dlls.Count == 0)
        {
            var preferredTfms = new[]
            {
                "net9.0", "net8.0", "net7.0", "net6.0",
                "netstandard2.1", "netstandard2.0",
            };

            foreach (var tfm in preferredTfms)
            {
                var tfmDir = Path.Combine(refDir, tfm);
                if (Directory.Exists(tfmDir))
                {
                    try
                    {
                        foreach (var dll in Directory.GetFiles(tfmDir, "*.dll"))
                            dlls.Add(dll);
                    }
                    catch { }
                    break;
                }
            }
        }

        return dlls;
    }

    // ── Strategy 3: bin/ folder scan ─────────────────────────────────────────

    private static List<string> ScanBinFolder(string projectDir)
    {
        var dlls = new List<string>();
        var binDir = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(binDir))
            return dlls;

        try
        {
            // Find the most recent build output directory
            var outputDirs = Directory.GetDirectories(binDir, "*", SearchOption.AllDirectories)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    // Look for TFM directories like net8.0, net6.0
                    return name.StartsWith("net", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(d =>
                {
                    try { return Directory.GetLastWriteTimeUtc(d); }
                    catch { return DateTime.MinValue; }
                })
                .ToList();

            var outputDir = outputDirs.FirstOrDefault();
            if (outputDir == null)
            {
                // Fall back to just scanning bin/Debug or bin/Release
                var debugDir = Path.Combine(binDir, "Debug");
                var releaseDir = Path.Combine(binDir, "Release");
                outputDir = Directory.Exists(debugDir) ? debugDir :
                            Directory.Exists(releaseDir) ? releaseDir : null;
            }

            if (outputDir != null)
            {
                foreach (var dll in Directory.GetFiles(outputDir, "*.dll"))
                {
                    // Skip the project's own assembly
                    var fileName = Path.GetFileNameWithoutExtension(dll);
                    if (fileName.Equals(Path.GetFileName(projectDir), StringComparison.OrdinalIgnoreCase))
                        continue;
                    dlls.Add(dll);
                }
            }
        }
        catch { }

        return dlls;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string? FindCsproj(string projectDir)
    {
        if (!Directory.Exists(projectDir))
            return null;

        try
        {
            return Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static void TryAdd(List<MetadataReference> list, string path)
    {
        if (!File.Exists(path)) return;
        try { list.Add(MetadataReference.CreateFromFile(path)); }
        catch { /* corrupted or locked DLL */ }
    }
}

