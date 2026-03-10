using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Insait_Edit_C_Sharp.Controls;
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
    /// Find the project file (.csproj) in a path
    /// </summary>
    public static string? FindProjectFile(string path)
    {
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj")
                return path;
        }

        if (Directory.Exists(path))
        {
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0) return csprojFiles[0];
        }

        return null;
    }

    /// <summary>
    /// Search for NuGet packages by query.
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
    /// Get installed packages from a project file (.csproj)
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
    /// Install a NuGet package to a project (.csproj)
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

            var versionArg = string.IsNullOrEmpty(version) ? "" : $"--version {version}";
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SettingsPanelControl.ResolveDotNetExe(),
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
    /// Uninstall a NuGet package from a project (.csproj)
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

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SettingsPanelControl.ResolveDotNetExe(),
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

    private static string? ResolveProjectFile(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            if (ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj")
                return projectPath;
        }

        if (Directory.Exists(projectPath))
        {
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0) return csprojFiles[0];
        }

        return null;
    }


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
}

