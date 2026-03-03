using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Insait_Edit_C_Sharp.Esp.Services;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for managing NuGet packages - search, install, and display
/// </summary>
public class NuGetService
{
    private readonly HttpClient _httpClient;
    private readonly SourceCacheContext _cacheContext;
    private readonly ILogger _logger;
    private readonly List<PackageSource> _packageSources;
    
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Whether the current project is a nanoFramework (.csproj with NanoFrameworkProject marker or legacy .nfproj) project
    /// </summary>
    public bool IsNanoFrameworkProject { get; private set; }
    
    public NuGetService()
    {
        _httpClient = new HttpClient();
        _cacheContext = new SourceCacheContext();
        _logger = NullLogger.Instance;
        
        _packageSources = new List<PackageSource>
        {
            new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org")
        };
    }
    
    /// <summary>
    /// Configure the service for a specific project type
    /// </summary>
    public void ConfigureForProject(string projectPath)
    {
        IsNanoFrameworkProject = IsNfprojFile(projectPath) || IsNfprojInDirectory(projectPath) || IsNanoFrameworkCsproj(projectPath);
    }
    
    /// <summary>
    /// Check if a file is a .nfproj file
    /// </summary>
    private static bool IsNfprojFile(string path)
    {
        return !string.IsNullOrEmpty(path) && 
               path.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Check if a .csproj file contains NanoFrameworkProject marker
    /// </summary>
    private static bool IsNanoFrameworkCsproj(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            string? filePath = null;
            if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                filePath = path;
            }
            else if (Directory.Exists(path))
            {
                var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
                filePath = csprojFiles.FirstOrDefault();
            }
            
            if (filePath != null)
            {
                var content = File.ReadAllText(filePath);
                return content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { /* Ignore read errors */ }
        return false;
    }
    
    /// <summary>
    /// Check if a directory contains .nfproj files
    /// </summary>
    private static bool IsNfprojInDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
        return Directory.GetFiles(path, "*.nfproj", SearchOption.AllDirectories).Length > 0;
    }
    
    /// <summary>
    /// Find the project file (.csproj or .nfproj) in a path
    /// </summary>
    public static string? FindProjectFile(string path)
    {
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csproj" || ext == ".nfproj" || ext == ".fsproj" || ext == ".vbproj")
                return path;
        }
        
        if (Directory.Exists(path))
        {
            // Prefer .csproj (standard SDK-style, including nanoFramework projects)
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0) return csprojFiles[0];
            
            // Fallback to .nfproj for legacy nanoFramework projects
            var nfprojFiles = Directory.GetFiles(path, "*.nfproj", SearchOption.AllDirectories);
            if (nfprojFiles.Length > 0) return nfprojFiles[0];
        }
        
        return null;
    }

    /// <summary>
    /// Search for NuGet packages by query. For ESP32/nanoFramework projects, 
    /// automatically searches nanoFramework packages.
    /// </summary>
    public async Task<List<NuGetPackageInfo>> SearchPackagesAsync(string searchTerm, int skip = 0, int take = 30, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        var packages = new List<NuGetPackageInfo>();
        
        if (string.IsNullOrWhiteSpace(searchTerm))
            return packages;

        try
        {
            StatusChanged?.Invoke(this, $"Searching for '{searchTerm}'...");

            foreach (var source in _packageSources)
            {
                var repository = Repository.Factory.GetCoreV3(source.Source);
                var searchResource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
                
                if (searchResource == null) continue;

                var searchFilter = new SearchFilter(includePrerelease)
                {
                    IncludeDelisted = false
                };

                var results = await searchResource.SearchAsync(
                    searchTerm,
                    searchFilter,
                    skip,
                    take,
                    _logger,
                    cancellationToken
                );

                foreach (var result in results)
                {
                    var package = new NuGetPackageInfo
                    {
                        Id = result.Identity.Id,
                        Version = result.Identity.Version.ToNormalizedString(),
                        Title = result.Title ?? result.Identity.Id,
                        Description = result.Description ?? "",
                        Authors = result.Authors ?? "",
                        IconUrl = result.IconUrl?.ToString() ?? "",
                        ProjectUrl = result.ProjectUrl?.ToString() ?? "",
                        LicenseUrl = result.LicenseUrl?.ToString() ?? "",
                        TotalDownloads = result.DownloadCount ?? 0,
                        Published = result.Published?.LocalDateTime ?? DateTime.MinValue,
                        Tags = result.Tags ?? "",
                        IsVerified = result.PrefixReserved
                    };

                    // Get all versions
                    try
                    {
                        var versions = await result.GetVersionsAsync();
                        package.AllVersions = versions
                            .Select(v => v.Version.ToNormalizedString())
                            .OrderByDescending(v => NuGetVersion.Parse(v))
                            .Take(20)
                            .ToList();
                    }
                    catch
                    {
                        package.AllVersions = new List<string> { package.Version };
                    }

                    packages.Add(package);
                }
            }

            StatusChanged?.Invoke(this, $"Found {packages.Count} packages");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Search failed: {ex.Message}");
        }

        return packages;
    }

    /// <summary>
    /// Get package details including all versions
    /// </summary>
    public async Task<NuGetPackageInfo?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var source in _packageSources)
            {
                var repository = Repository.Factory.GetCoreV3(source.Source);
                var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
                
                if (metadataResource == null) continue;

                var metadataList = (await metadataResource.GetMetadataAsync(
                    packageId,
                    includePrerelease: true,
                    includeUnlisted: false,
                    _cacheContext,
                    _logger,
                    cancellationToken
                )).ToList();

                var latestStable = metadataList
                    .Where(m => !m.Identity.Version.IsPrerelease)
                    .OrderByDescending(m => m.Identity.Version)
                    .FirstOrDefault();

                var latest = latestStable ?? metadataList
                    .OrderByDescending(m => m.Identity.Version)
                    .FirstOrDefault();

                if (latest != null)
                {
                    // Try to get the published date from the latest version;
                    // fall back to the most recently published date from any version in metadata
                    var publishedDate = latest.Published?.LocalDateTime;
                    if (publishedDate == null || publishedDate == DateTime.MinValue)
                    {
                        publishedDate = metadataList
                            .Where(m => m.Published.HasValue)
                            .OrderByDescending(m => m.Identity.Version)
                            .Select(m => (DateTime?)m.Published!.Value.LocalDateTime)
                            .FirstOrDefault();
                    }

                    return new NuGetPackageInfo
                    {
                        Id = latest.Identity.Id,
                        Version = latest.Identity.Version.ToNormalizedString(),
                        Title = latest.Title ?? latest.Identity.Id,
                        Description = latest.Description ?? "",
                        Authors = latest.Authors ?? "",
                        IconUrl = latest.IconUrl?.ToString() ?? "",
                        ProjectUrl = latest.ProjectUrl?.ToString() ?? "",
                        LicenseUrl = latest.LicenseUrl?.ToString() ?? "",
                        TotalDownloads = latest.DownloadCount ?? 0,
                        Published = publishedDate ?? DateTime.MinValue,
                        Tags = latest.Tags ?? "",
                        AllVersions = metadataList
                            .Select(m => m.Identity.Version.ToNormalizedString())
                            .OrderByDescending(v => NuGetVersion.Parse(v))
                            .ToList()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get package details: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get installed packages from a project file (.csproj or .nfproj)
    /// </summary>
    public async Task<List<InstalledNuGetPackage>> GetInstalledPackagesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var packages = new List<InstalledNuGetPackage>();

        try
        {
            // Resolve project file
            var resolvedPath = ResolveProjectFile(projectPath);
            if (resolvedPath == null)
            {
                return packages;
            }
            
            // Check if it's a nanoFramework project
            if (IsNfprojFile(resolvedPath))
            {
                return await GetInstalledNfPackagesAsync(resolvedPath, cancellationToken);
            }

            if (!resolvedPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return packages;
            }

            var content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
            var doc = XDocument.Parse(content);

            var packageRefs = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .ToList();

            foreach (var packageRef in packageRefs)
            {
                var id = packageRef.Attribute("Include")?.Value;
                var version = packageRef.Attribute("Version")?.Value 
                              ?? packageRef.Element(XName.Get("Version", packageRef.Name.NamespaceName))?.Value;

                if (!string.IsNullOrEmpty(id))
                {
                    packages.Add(new InstalledNuGetPackage
                    {
                        Id = id,
                        Version = version ?? "Unknown",
                        ProjectPath = resolvedPath
                    });
                }
            }

            // Try to get additional info for each package
            foreach (var package in packages)
            {
                try
                {
                    var details = await GetPackageDetailsAsync(package.Id, cancellationToken);
                    if (details != null)
                    {
                        package.Title = details.Title;
                        package.Description = details.Description;
                        package.IconUrl = details.IconUrl;
                        package.LatestVersion = details.Version;
                        package.HasUpdate = !string.IsNullOrEmpty(details.Version) && 
                                            CompareVersions(package.Version, details.Version) < 0;
                    }
                }
                catch
                {
                    // Ignore errors for individual packages
                }
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get installed packages: {ex.Message}");
        }

        return packages;
    }

    /// <summary>
    /// Install a NuGet package to a project (.csproj or .nfproj)
    /// </summary>
    public async Task<bool> InstallPackageAsync(string projectPath, string packageId, string? version = null, CancellationToken cancellationToken = default)
    {
        try
        {
            StatusChanged?.Invoke(this, $"Installing {packageId}...");

            var resolvedPath = ResolveProjectFile(projectPath);
            if (resolvedPath == null)
            {
                ErrorOccurred?.Invoke(this, "Project file not found");
                return false;
            }

            // For nanoFramework projects, use manual XML editing
            if (IsNfprojFile(resolvedPath))
            {
                return await InstallNfPackageAsync(resolvedPath, packageId, version, cancellationToken);
            }

            var versionArg = string.IsNullOrEmpty(version) ? "" : $"--version {version}";
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"add \"{resolvedPath}\" package {packageId} {versionArg}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                StatusChanged?.Invoke(this, $"Successfully installed {packageId}");
                return true;
            }

            ErrorOccurred?.Invoke(this, $"Failed to install {packageId}: {error}");
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Install failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Uninstall a NuGet package from a project (.csproj or .nfproj)
    /// </summary>
    public async Task<bool> UninstallPackageAsync(string projectPath, string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            StatusChanged?.Invoke(this, $"Uninstalling {packageId}...");

            var resolvedPath = ResolveProjectFile(projectPath);
            if (resolvedPath == null)
            {
                ErrorOccurred?.Invoke(this, "Project file not found");
                return false;
            }

            // For nanoFramework projects, use manual XML editing
            if (IsNfprojFile(resolvedPath))
            {
                return await UninstallNfPackageAsync(resolvedPath, packageId, cancellationToken);
            }

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"remove \"{resolvedPath}\" package {packageId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                StatusChanged?.Invoke(this, $"Successfully uninstalled {packageId}");
                return true;
            }

            ErrorOccurred?.Invoke(this, $"Failed to uninstall {packageId}: {error}");
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Uninstall failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Update a NuGet package to the latest version
    /// </summary>
    public async Task<bool> UpdatePackageAsync(string projectPath, string packageId, string? targetVersion = null, CancellationToken cancellationToken = default)
    {
        // For update, we just reinstall with the new version
        return await InstallPackageAsync(projectPath, packageId, targetVersion, cancellationToken);
    }

    private static int CompareVersions(string version1, string version2)
    {
        try
        {
            var v1 = NuGetVersion.Parse(version1);
            var v2 = NuGetVersion.Parse(version2);
            return v1.CompareTo(v2);
        }
        catch
        {
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
        }
    }
    
    #region nanoFramework / ESP32 Support
    
    /// <summary>
    /// Resolve a project path to the actual project file (.csproj or .nfproj)
    /// </summary>
    private static string? ResolveProjectFile(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            if (ext == ".csproj" || ext == ".nfproj" || ext == ".fsproj" || ext == ".vbproj")
                return projectPath;
        }
        
        if (Directory.Exists(projectPath))
        {
            // Prefer .csproj (standard SDK-style projects, including ESP32)
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0) return csprojFiles[0];
            
            // Fallback to .nfproj for legacy nanoFramework projects
            var nfprojFiles = Directory.GetFiles(projectPath, "*.nfproj", SearchOption.AllDirectories);
            if (nfprojFiles.Length > 0) return nfprojFiles[0];
        }
        
        return null;
    }
    
    /// <summary>
    /// Get installed packages for a nanoFramework (.nfproj) project by reading packages.config
    /// </summary>
    private async Task<List<InstalledNuGetPackage>> GetInstalledNfPackagesAsync(string nfprojPath, CancellationToken cancellationToken)
    {
        var packages = new List<InstalledNuGetPackage>();
        var projectDir = Path.GetDirectoryName(nfprojPath);
        if (string.IsNullOrEmpty(projectDir)) return packages;
        
        try
        {
            var nfContent = await File.ReadAllTextAsync(nfprojPath, cancellationToken);
            var doc = XDocument.Parse(nfContent);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            
            // 1. Read PackageReference elements (modern approach — everything in .nfproj)
            var pkgRefs = doc.Descendants(ns + "PackageReference");
            foreach (var pkgRef in pkgRefs)
            {
                var id = pkgRef.Attribute("Include")?.Value;
                var version = pkgRef.Attribute("Version")?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    packages.Add(new InstalledNuGetPackage
                    {
                        Id = id,
                        Version = version ?? "Unknown",
                        ProjectPath = nfprojPath,
                        IsNanoFrameworkPackage = true
                    });
                }
            }
            
            // 2. Fallback: if no PackageReference found, parse Reference elements with HintPath
            if (packages.Count == 0)
            {
                var references = doc.Descendants(ns + "Reference")
                    .Where(r => r.Element(ns + "HintPath") != null);
                
                foreach (var refElement in references)
                {
                    var include = refElement.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include))
                    {
                        var hintPath = refElement.Element(ns + "HintPath")?.Value ?? "";
                        var extractedVersion = ExtractVersionFromHintPath(hintPath, include);
                        
                        packages.Add(new InstalledNuGetPackage
                        {
                            Id = include,
                            Version = extractedVersion ?? "Unknown",
                            ProjectPath = nfprojPath,
                            IsNanoFrameworkPackage = true
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error parsing .nfproj: {ex.Message}");
        }
        
        // 3. Last fallback: try packages.config if it exists and nothing found yet
        if (packages.Count == 0)
        {
            var packagesConfigPath = Path.Combine(projectDir, "packages.config");
            if (File.Exists(packagesConfigPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(packagesConfigPath, cancellationToken);
                    var doc = XDocument.Parse(content);
                    foreach (var pkg in doc.Descendants("package"))
                    {
                        var id = pkg.Attribute("id")?.Value;
                        var version = pkg.Attribute("version")?.Value;
                        if (!string.IsNullOrEmpty(id))
                        {
                            packages.Add(new InstalledNuGetPackage
                            {
                                Id = id,
                                Version = version ?? "Unknown",
                                ProjectPath = nfprojPath,
                                IsNanoFrameworkPackage = true
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Error parsing packages.config: {ex.Message}");
                }
            }
        }
        
        // Try to get latest versions for update detection
        foreach (var package in packages)
        {
            try
            {
                var details = await GetPackageDetailsAsync(package.Id, cancellationToken);
                if (details != null)
                {
                    package.Title = details.Title;
                    package.Description = details.Description;
                    package.IconUrl = details.IconUrl;
                    package.LatestVersion = details.Version;
                    package.HasUpdate = !string.IsNullOrEmpty(details.Version) &&
                                        CompareVersions(package.Version, details.Version) < 0;
                }
            }
            catch { /* Ignore errors for individual packages */ }
        }
        
        return packages;
    }
    
    /// <summary>
    /// Extract version from HintPath like "packages\nanoFramework.CoreLibrary.1.17.11\lib\..."
    /// </summary>
    private static string? ExtractVersionFromHintPath(string hintPath, string packageId)
    {
        if (string.IsNullOrEmpty(hintPath)) return null;
        
        // Look for pattern: packages\PackageId.Version\...
        var parts = hintPath.Replace('/', '\\').Split('\\');
        foreach (var part in parts)
        {
            if (part.StartsWith(packageId + ".", StringComparison.OrdinalIgnoreCase))
            {
                var versionPart = part.Substring(packageId.Length + 1);
                if (!string.IsNullOrEmpty(versionPart))
                    return versionPart;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Install a NuGet package into a nanoFramework project by editing .nfproj (PackageReference + Reference)
    /// </summary>
    private async Task<bool> InstallNfPackageAsync(string nfprojPath, string packageId, string? version, CancellationToken cancellationToken)
    {
        try
        {
            // If no version specified, get the latest from NuGet
            if (string.IsNullOrEmpty(version))
            {
                var details = await GetPackageDetailsAsync(packageId, cancellationToken);
                version = details?.Version;
                if (string.IsNullOrEmpty(version))
                {
                    ErrorOccurred?.Invoke(this, $"Could not find package {packageId}");
                    return false;
                }
            }
            
            var projectDir = Path.GetDirectoryName(nfprojPath);
            if (string.IsNullOrEmpty(projectDir)) return false;
            
            // Add PackageReference + Reference to .nfproj (no separate packages.config)
            await AddNfprojReferenceAsync(nfprojPath, packageId, version, cancellationToken);
            
            StatusChanged?.Invoke(this, $"Successfully installed {packageId} {version}");
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to install {packageId}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Uninstall a NuGet package from a nanoFramework project
    /// </summary>
    private async Task<bool> UninstallNfPackageAsync(string nfprojPath, string packageId, CancellationToken cancellationToken)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(nfprojPath);
            if (string.IsNullOrEmpty(projectDir)) return false;
            
            // Remove Reference + PackageReference from .nfproj (no separate packages.config)
            await RemoveNfprojReferenceAsync(nfprojPath, packageId, cancellationToken);
            
            StatusChanged?.Invoke(this, $"Successfully uninstalled {packageId}");
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to uninstall {packageId}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Add a Reference element to .nfproj file
    /// </summary>
    private async Task AddNfprojReferenceAsync(string nfprojPath, string packageId, string version, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(nfprojPath, cancellationToken);
        var doc = XDocument.Parse(content);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        
        // Resolve correct DLL name and path for nanoFramework package
        var dllName = NanoProjectService.GetNanoFrameworkDllName(packageId);
        var assemblyName = dllName.Replace(".dll", "");
        var projectDir = Path.GetDirectoryName(nfprojPath) ?? "";
        var hintPath = NanoProjectService.ResolveNuGetHintPath(projectDir, packageId, version, dllName);
        
        // Check if reference already exists
        var existingRef = doc.Descendants(ns + "Reference")
            .FirstOrDefault(r => string.Equals(r.Attribute("Include")?.Value, assemblyName, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(r.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));
        
        if (existingRef != null)
        {
            // Update existing reference
            existingRef.SetAttributeValue("Include", assemblyName);
            var hintPathEl = existingRef.Element(ns + "HintPath");
            if (hintPathEl != null)
            {
                hintPathEl.Value = hintPath;
            }
            else
            {
                existingRef.Add(new XElement(ns + "HintPath", hintPath));
            }
        }
        else
        {
            // Find the ItemGroup with existing References, or create a new one
            var refItemGroup = doc.Descendants(ns + "ItemGroup")
                .FirstOrDefault(g => g.Elements(ns + "Reference").Any());
            
            if (refItemGroup == null)
            {
                refItemGroup = new XElement(ns + "ItemGroup");
                doc.Root?.Add(refItemGroup);
            }
            
            var referenceElement = new XElement(ns + "Reference",
                new XAttribute("Include", assemblyName),
                new XElement(ns + "HintPath", hintPath),
                new XElement(ns + "Private", "True")
            );
            
            refItemGroup.Add(referenceElement);
        }
        
        // Also add/update PackageReference
        var existingPkgRef = doc.Descendants(ns + "PackageReference")
            .FirstOrDefault(r => string.Equals(r.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));
        
        if (existingPkgRef != null)
        {
            existingPkgRef.SetAttributeValue("Version", version);
        }
        else
        {
            var pkgRefGroup = doc.Descendants(ns + "ItemGroup")
                .FirstOrDefault(g => g.Elements(ns + "PackageReference").Any());
            
            if (pkgRefGroup == null)
            {
                pkgRefGroup = new XElement(ns + "ItemGroup");
                doc.Root?.Add(pkgRefGroup);
            }
            
            pkgRefGroup.Add(new XElement(ns + "PackageReference",
                new XAttribute("Include", packageId),
                new XAttribute("Version", version)));
        }
        
        await File.WriteAllTextAsync(nfprojPath, doc.Declaration + Environment.NewLine + doc.ToString(), cancellationToken);
    }
    
    /// <summary>
    /// Remove a Reference element from .nfproj file
    /// </summary>
    private async Task RemoveNfprojReferenceAsync(string nfprojPath, string packageId, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(nfprojPath, cancellationToken);
        var doc = XDocument.Parse(content);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        
        // Resolve assembly name for matching (e.g., nanoFramework.CoreLibrary -> mscorlib)
        var dllName = NanoProjectService.GetNanoFrameworkDllName(packageId);
        var assemblyName = dllName.Replace(".dll", "");
        
        // Remove Reference elements (match by both packageId and assemblyName)
        var references = doc.Descendants(ns + "Reference")
            .Where(r => string.Equals(r.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(r.Attribute("Include")?.Value, assemblyName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var r in references) r.Remove();
        
        // Remove PackageReference elements
        var pkgRefs = doc.Descendants(ns + "PackageReference")
            .Where(r => string.Equals(r.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var r in pkgRefs) r.Remove();
        
        // Clean up empty ItemGroups
        var emptyGroups = doc.Descendants(ns + "ItemGroup")
            .Where(g => !g.HasElements)
            .ToList();
        foreach (var g in emptyGroups) g.Remove();
        
        await File.WriteAllTextAsync(nfprojPath, doc.Declaration + Environment.NewLine + doc.ToString(), cancellationToken);
    }
    
    /// <summary>
    /// Search for nanoFramework-specific packages
    /// </summary>
    public async Task<List<NuGetPackageInfo>> SearchNanoFrameworkPackagesAsync(string searchTerm, int skip = 0, int take = 30, CancellationToken cancellationToken = default)
    {
        // Prefix with "nanoframework" if user doesn't already include it
        var nfSearchTerm = searchTerm;
        if (!searchTerm.Contains("nanoframework", StringComparison.OrdinalIgnoreCase) && 
            !searchTerm.Contains("nanoFramework", StringComparison.OrdinalIgnoreCase))
        {
            nfSearchTerm = $"nanoframework {searchTerm}";
        }
        
        return await SearchPackagesAsync(nfSearchTerm, skip, take, false, cancellationToken);
    }
    
    /// <summary>
    /// Get commonly used nanoFramework packages for ESP32
    /// </summary>
    public static List<NuGetPackageInfo> GetCommonNanoFrameworkPackages()
    {
        return new List<NuGetPackageInfo>
        {
            new() { Id = "nanoFramework.CoreLibrary", Title = "nanoFramework.CoreLibrary", Version = "2.0.0-preview.35", Description = "Core library for nanoFramework. Required for all nanoFramework projects.", Tags = "nanoframework esp32 core", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.Runtime.Native", Title = "Runtime.Native", Version = "2.0.0-preview.5", Description = "nanoFramework native runtime interop (NativeEventDispatcher, Power, etc.). Mandatory for all ESP32 projects.", Tags = "nanoframework esp32 runtime native", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.Hardware.Esp32", Title = "Hardware.Esp32", Version = "2.0.0-preview.1", Description = "ESP32-specific hardware APIs: deep sleep, touch sensors, Hall sensor, and more.", Tags = "nanoframework esp32 hardware sleep touch", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.Gpio", Title = "System.Device.Gpio", Version = "2.0.0-preview.9", Description = "GPIO pin access for nanoFramework (LEDs, buttons, sensors)", Tags = "nanoframework esp32 gpio", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.Wifi", Title = "System.Device.Wifi", Version = "2.0.0-preview.7", Description = "Wi-Fi connectivity for ESP32 devices", Tags = "nanoframework esp32 wifi", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.I2c", Title = "System.Device.I2c", Version = "2.0.0-preview.5", Description = "I2C bus communication for sensors and peripherals", Tags = "nanoframework esp32 i2c", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.Spi", Title = "System.Device.Spi", Version = "2.0.0-preview.9", Description = "SPI bus communication for displays and peripherals", Tags = "nanoframework esp32 spi", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.Pwm", Title = "System.Device.Pwm", Version = "2.0.0-preview.7", Description = "PWM (Pulse Width Modulation) for servo control, LED brightness", Tags = "nanoframework esp32 pwm", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.Adc", Title = "System.Device.Adc", Version = "2.0.0-preview.5", Description = "ADC (Analog-to-Digital Converter) for analog sensor readings", Tags = "nanoframework esp32 adc", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Net", Title = "System.Net", Version = "2.0.0-preview.1", Description = "Networking library for TCP/IP, sockets, DNS", Tags = "nanoframework esp32 network tcp", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Net.Http", Title = "System.Net.Http", Version = "2.0.0-preview.8", Description = "HTTP client for making web requests from ESP32", Tags = "nanoframework esp32 http", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.Runtime.Events", Title = "Runtime.Events", Version = "2.0.1", Description = "Event handling for nanoFramework runtime", Tags = "nanoframework esp32 events", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.IO.Ports", Title = "System.IO.Ports", Version = "2.0.0-preview.1", Description = "Serial port (UART) communication for ESP32", Tags = "nanoframework esp32 serial uart", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Math", Title = "System.Math", Version = "2.0.0-preview.1", Description = "Mathematical functions for nanoFramework", Tags = "nanoframework esp32 math", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.Json", Title = "nanoFramework.Json", Version = "2.2.138", Description = "JSON serialization/deserialization for nanoFramework", Tags = "nanoframework esp32 json", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.System.Device.Dac", Title = "System.Device.Dac", Version = "2.0.0-preview.1", Description = "DAC (Digital-to-Analog Converter) for analog output", Tags = "nanoframework esp32 dac", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.Hardware.Esp32", Title = "Hardware.Esp32", Version = "2.0.0-preview.1", Description = "ESP32-specific hardware features (sleep, touch, Hall sensor)", Tags = "nanoframework esp32 hardware", Authors = "nanoFramework Contributors" },
            new() { Id = "nanoFramework.Tools.MetadataProcessor.MsBuildTask", Title = "MetadataProcessor MSBuild Task", Version = "4.0.0-preview.73", Description = "MSBuild task for nanoFramework metadata processing (PE generation)", Tags = "nanoframework esp32 build msbuild", Authors = "nanoFramework Contributors" },
        };
    }
    
    #endregion

    public void Dispose()
    {
        _cacheContext.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>
/// Information about a NuGet package from search results
/// </summary>
public class NuGetPackageInfo
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Authors { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string ProjectUrl { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public long TotalDownloads { get; set; }
    public DateTime Published { get; set; }
    public string Tags { get; set; } = "";
    public bool IsVerified { get; set; }
    public List<string> AllVersions { get; set; } = new();
    
    public string FormattedDownloads => FormatDownloads(TotalDownloads);
    
    private static string FormatDownloads(long downloads)
    {
        if (downloads >= 1_000_000_000) return $"{downloads / 1_000_000_000.0:F1}B";
        if (downloads >= 1_000_000) return $"{downloads / 1_000_000.0:F1}M";
        if (downloads >= 1_000) return $"{downloads / 1_000.0:F1}K";
        return downloads.ToString();
    }
}

/// <summary>
/// Information about an installed NuGet package
/// </summary>
public class InstalledNuGetPackage
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool HasUpdate { get; set; }
    public bool IsNanoFrameworkPackage { get; set; }
}

