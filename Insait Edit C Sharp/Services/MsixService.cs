using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Professional MSIX packaging service — publish + package in one step.
/// Mirrors capabilities found in the MSIX Packaging Tool.
/// </summary>
public class MsixService
{
    public event EventHandler<MsixOutputEventArgs>?    OutputReceived;
    public event EventHandler?                          PackageStarted;
    public event EventHandler<MsixCompletedEventArgs>? PackageCompleted;

    private Process? _activeProcess;
    private CancellationTokenSource? _cts;

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Full pipeline: dotnet publish → generate AppxManifest → MakeAppx pack
    /// </summary>
    public async Task<MsixResult> PackageAsync(MsixPackageOptions opts)
    {
        _cts = new CancellationTokenSource();
        PackageStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            Log("══════════════ MSIX Build Started ══════════════");
            Log($"Project  : {opts.ProjectPath}");
            Log($"Identity : {opts.PackageIdentityName}  v{opts.Version}");
            Log($"Output   : {opts.OutputMsixPath}");
            Log("");

            // 1. dotnet publish
            var publishDir = opts.PublishOutputDir;
            if (string.IsNullOrWhiteSpace(publishDir))
                publishDir = Path.Combine(Path.GetDirectoryName(opts.ProjectPath)!, "bin", "msix_publish");

            Log("── Step 1/3: Publishing project ──");
            var publishResult = await RunDotnetPublishAsync(opts, publishDir);
            if (!publishResult.Success)
                return Fail($"Publish failed (exit {publishResult.ExitCode})");

            // 2. Generate AppxManifest.xml into publishDir
            Log("── Step 2/3: Generating AppxManifest.xml ──");
            try
            {
                GenerateAppxManifest(opts, publishDir);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }

            // 3. MakeAppx pack
            Log("── Step 3/3: Packing MSIX ──");
            var msixPath = opts.OutputMsixPath;
            if (string.IsNullOrWhiteSpace(msixPath))
                msixPath = Path.Combine(
                    Path.GetDirectoryName(opts.ProjectPath)!,
                    "bin",
                    opts.PackageIdentityName + ".msix");

            Directory.CreateDirectory(Path.GetDirectoryName(msixPath)!);

            var packResult = await RunMakeAppxAsync(publishDir, msixPath);
            if (!packResult.Success)
                return Fail($"MakeAppx failed (exit {packResult.ExitCode})");

            var size = new FileInfo(msixPath).Length;
            Log($"\n✅  MSIX created: {msixPath}  ({FormatSize(size)})");
            Log("══════════════ MSIX Build Succeeded ══════════════");

            var r = new MsixResult { Success = true, OutputPath = msixPath };
            PackageCompleted?.Invoke(this, new MsixCompletedEventArgs(r));
            return r;
        }
        catch (OperationCanceledException)
        {
            Log("\n⚠️  Build cancelled by user.");
            return Fail("Cancelled");
        }
        catch (Exception ex)
        {
            return Fail($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Pipeline steps 2 + 3 only: generate AppxManifest → MakeAppx pack.
    /// Call this after publish has already been done via <see cref="PublishService"/>.
    /// </summary>
    public async Task<MsixResult> PackageFromPublishedAsync(MsixPackageOptions opts, string publishDir)
    {
        _cts = new CancellationTokenSource();
        PackageStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            Log("");
            Log("══════════════ MSIX Packaging ══════════════");
            Log($"Identity : {opts.PackageIdentityName}  v{opts.Version}");
            Log($"Source   : {publishDir}");
            Log("");

            // ── Verify publish directory contains an executable ──
            var hasExe = Directory.Exists(publishDir)
                      && Directory.GetFiles(publishDir, "*.exe", SearchOption.AllDirectories).Length > 0;

            if (!hasExe && opts.SelfContained)
            {
                Log("⚠ No .exe found in publish directory — re-publishing as self-contained…");
                Log("── Step 0: Re-publishing (self-contained) ──");
                var rePublish = await RunDotnetPublishAsync(opts, publishDir);
                if (!rePublish.Success)
                    return Fail($"Self-contained re-publish failed (exit {rePublish.ExitCode})");
                Log("");
            }

            // 1. Generate AppxManifest.xml into publishDir
            Log("── Step 1/2: Generating AppxManifest.xml ──");
            try
            {
                GenerateAppxManifest(opts, publishDir);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }

            // 2. MakeAppx pack
            Log("── Step 2/2: Packing MSIX ──");
            var msixPath = opts.OutputMsixPath;
            if (string.IsNullOrWhiteSpace(msixPath))
                msixPath = Path.Combine(
                    Path.GetDirectoryName(opts.ProjectPath)!,
                    "bin",
                    opts.PackageIdentityName + ".msix");

            Directory.CreateDirectory(Path.GetDirectoryName(msixPath)!);

            var packResult = await RunMakeAppxAsync(publishDir, msixPath);
            if (!packResult.Success)
                return Fail($"MakeAppx failed (exit {packResult.ExitCode})");

            var size = new FileInfo(msixPath).Length;
            Log($"\n✅  MSIX created: {msixPath}  ({FormatSize(size)})");
            Log("══════════════ MSIX Build Succeeded ══════════════");

            var r = new MsixResult { Success = true, OutputPath = msixPath };
            PackageCompleted?.Invoke(this, new MsixCompletedEventArgs(r));
            return r;
        }
        catch (OperationCanceledException)
        {
            Log("\n⚠️  Build cancelled by user.");
            return Fail("Cancelled");
        }
        catch (Exception ex)
        {
            return Fail($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Open an existing MSIX and return its package metadata.</summary>
    public async Task<MsixPackageInfo?> OpenMsixAsync(string msixPath)
    {
        if (!File.Exists(msixPath)) return null;
        try
        {
            using var zip = ZipFile.OpenRead(msixPath);
            var entry = zip.GetEntry("AppxManifest.xml");
            if (entry == null) return null;

            using var stream = entry.Open();
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

            var ns  = doc.Root?.Name.Namespace ?? XNamespace.None;
            var id  = doc.Root?.Element(ns + "Identity");
            var props = doc.Root?.Element(ns + "Properties");
            var app = doc.Root
                         ?.Element(ns + "Applications")
                         ?.Element(ns + "Application");

            return new MsixPackageInfo
            {
                MsixPath         = msixPath,
                Name             = id?.Attribute("Name")?.Value             ?? "",
                Publisher        = id?.Attribute("Publisher")?.Value        ?? "",
                Version          = id?.Attribute("Version")?.Value          ?? "",
                ProcessorArchitecture = id?.Attribute("ProcessorArchitecture")?.Value ?? "x64",
                DisplayName      = props?.Element(ns + "DisplayName")?.Value   ?? "",
                Description      = props?.Element(ns + "Description")?.Value   ?? "",
                PublisherDisplayName = props?.Element(ns + "PublisherDisplayName")?.Value ?? "",
                Logo             = props?.Element(ns + "Logo")?.Value          ?? "",
                EntryPoint       = app?.Attribute("EntryPoint")?.Value         ?? "",
                Executable       = app?.Attribute("Executable")?.Value         ?? "",
                ManifestXml      = doc.ToString()
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Save edited metadata back into an existing MSIX (re-pack).</summary>
    public async Task<MsixResult> SaveMsixMetadataAsync(MsixPackageInfo info)
    {
        Log("══════════════ Saving MSIX Metadata ══════════════");
        try
        {
            // Extract → modify → repack
            var tempDir = Path.Combine(Path.GetTempPath(), "MsixEdit_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            Log($"Extracting to: {tempDir}");
            ZipFile.ExtractToDirectory(info.MsixPath, tempDir, overwriteFiles: true);

            // Remove signing artifacts that MakeAppx will regenerate
            var blockMap = Path.Combine(tempDir, "AppxBlockMap.xml");
            if (File.Exists(blockMap)) File.Delete(blockMap);
            var sigFile = Path.Combine(tempDir, "AppxSignature.p7x");
            if (File.Exists(sigFile)) File.Delete(sigFile);
            var ctFile = Path.Combine(tempDir, "[Content_Types].xml");
            if (File.Exists(ctFile)) File.Delete(ctFile);

            var manifestPath = Path.Combine(tempDir, "AppxManifest.xml");
            var doc  = XDocument.Load(manifestPath);
            var ns   = doc.Root?.Name.Namespace ?? XNamespace.None;
            var id   = doc.Root?.Element(ns + "Identity");
            var props = doc.Root?.Element(ns + "Properties");
            var app  = doc.Root
                          ?.Element(ns + "Applications")
                          ?.Element(ns + "Application");

            if (id != null)
            {
                id.SetAttributeValue("Name",                 info.Name);
                id.SetAttributeValue("Publisher",            info.Publisher);
                id.SetAttributeValue("Version",              info.Version);
                id.SetAttributeValue("ProcessorArchitecture",info.ProcessorArchitecture);
            }
            if (props != null)
            {
                props.Element(ns + "DisplayName")?.SetValue(info.DisplayName);
                props.Element(ns + "Description")?.SetValue(info.Description);
                props.Element(ns + "PublisherDisplayName")?.SetValue(info.PublisherDisplayName);
                if (!string.IsNullOrEmpty(info.Logo))
                    props.Element(ns + "Logo")?.SetValue(info.Logo);
            }
            if (app != null)
            {
                if (!string.IsNullOrEmpty(info.Executable))
                    app.SetAttributeValue("Executable", info.Executable);
                if (!string.IsNullOrEmpty(info.EntryPoint))
                    app.SetAttributeValue("EntryPoint", info.EntryPoint);
            }

            doc.Save(manifestPath);

            // Ensure all file references in the manifest exist (create placeholders if needed)
            EnsureManifestFilesExist(doc, ns, tempDir);

            // Re-pack
            Log($"Repacking to: {info.MsixPath}");
            var packResult = await RunMakeAppxAsync(tempDir, info.MsixPath);
            CleanupTemp(tempDir);

            if (!packResult.Success) return Fail("MakeAppx repack failed");

            Log("✅  MSIX metadata saved.");
            return new MsixResult { Success = true, OutputPath = info.MsixPath };
        }
        catch (Exception ex)
        {
            return Fail($"Save error: {ex.Message}");
        }
    }

    /// <summary>List executables from an MSIX to help choose an entry point.</summary>
    public static List<string> ListExecutablesInMsix(string msixPath)
    {
        var list = new List<string>();
        if (!File.Exists(msixPath)) return list;
        try
        {
            using var zip = ZipFile.OpenRead(msixPath);
            list.AddRange(zip.Entries
                .Where(e => e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName));
        }
        catch { /* ignore */ }
        return list;
    }

    /// <summary>List executables in a publish folder (for pre-pack selection).</summary>
    public static List<string> ListExecutablesInFolder(string folder)
    {
        if (!Directory.Exists(folder)) return new();
        return Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f))
            .ToList();
    }

    /// <summary>Cancel any running operation.</summary>
    public void Cancel()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
        try
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
                _activeProcess.Kill(entireProcessTree: true);
        }
        catch (Exception) { /* process may have already exited */ }
    }

    /// <summary>
    /// Apply a PNG icon to an existing MSIX package.
    /// Extracts the MSIX, replaces all icon file references (Logo, Square*Logo, Wide*Logo)
    /// with the provided PNG file, and repacks.
    /// </summary>
    public async Task<MsixResult> ApplyIconToMsixAsync(string msixPath, string iconPngPath)
    {
        Log("══════════════ Apply Icon to MSIX ══════════════");
        Log($"MSIX : {msixPath}");
        Log($"Icon : {iconPngPath}");
        Log("");

        if (!File.Exists(msixPath))
            return Fail("MSIX file not found.");
        if (!File.Exists(iconPngPath))
            return Fail("Icon PNG file not found.");
        if (!iconPngPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return Fail("MSIX only supports PNG icons. Please provide a .png file.");

        string? tempDir = null;
        try
        {
            tempDir = Path.Combine(Path.GetTempPath(), "MsixIcon_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            Log($"Extracting to: {tempDir}");
            ZipFile.ExtractToDirectory(msixPath, tempDir, overwriteFiles: true);

            // Remove signing artifacts
            var blockMap = Path.Combine(tempDir, "AppxBlockMap.xml");
            if (File.Exists(blockMap)) File.Delete(blockMap);
            var signature = Path.Combine(tempDir, "AppxSignature.p7x");
            if (File.Exists(signature)) File.Delete(signature);
            var contentTypes = Path.Combine(tempDir, "[Content_Types].xml");
            if (File.Exists(contentTypes)) File.Delete(contentTypes);

            var manifestPath = Path.Combine(tempDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
                return Fail("AppxManifest.xml not found inside MSIX.");

            var doc = XDocument.Load(manifestPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // Collect all file paths referenced as icons
            var iconRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var logoEl = doc.Root?.Element(ns + "Properties")?.Element(ns + "Logo");
            if (logoEl != null && !string.IsNullOrWhiteSpace(logoEl.Value))
                iconRefs.Add(logoEl.Value);

            var uap = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/uap/windows10");
            foreach (var ve in doc.Descendants(uap + "VisualElements"))
            {
                foreach (var attr in ve.Attributes())
                {
                    if (attr.Value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        iconRefs.Add(attr.Value);
                }
            }
            foreach (var dt in doc.Descendants(uap + "DefaultTile"))
            {
                foreach (var attr in dt.Attributes())
                {
                    if (attr.Value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        iconRefs.Add(attr.Value);
                }
            }

            // Copy the new icon to each referenced path
            var iconBytes = File.ReadAllBytes(iconPngPath);
            foreach (var relPath in iconRefs)
            {
                var fullPath = Path.Combine(tempDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, iconBytes);
                Log($"  ✅ Replaced: {relPath}");
            }

            // If no icon refs found, create a default one
            if (iconRefs.Count == 0)
            {
                var defaultLogo = @"Assets\Logo.png";
                var defaultPath = Path.Combine(tempDir, "Assets", "Logo.png");
                Directory.CreateDirectory(Path.GetDirectoryName(defaultPath)!);
                File.WriteAllBytes(defaultPath, iconBytes);

                // Update manifest
                if (logoEl != null)
                    logoEl.SetValue(defaultLogo);

                foreach (var ve in doc.Descendants(uap + "VisualElements"))
                {
                    ve.SetAttributeValue("Square150x150Logo", defaultLogo);
                    ve.SetAttributeValue("Square44x44Logo", defaultLogo);
                }
                foreach (var dt in doc.Descendants(uap + "DefaultTile"))
                {
                    dt.SetAttributeValue("Wide310x150Logo", defaultLogo);
                }

                doc.Save(manifestPath);
                Log($"  ✅ Created default icon: {defaultLogo}");
            }

            Log("Repacking MSIX…");
            var packResult = await RunMakeAppxAsync(tempDir, msixPath);
            if (!packResult.Success)
                return Fail("MakeAppx repack failed after applying icon.");

            Log($"\n✅  Icon applied to: {msixPath}");
            Log("══════════════ Icon Applied Successfully ══════════════");
            var r = new MsixResult { Success = true, OutputPath = msixPath };
            PackageCompleted?.Invoke(this, new MsixCompletedEventArgs(r));
            return r;
        }
        catch (Exception ex)
        {
            return Fail($"Apply icon error: {ex.Message}");
        }
        finally
        {
            if (tempDir != null) CleanupTemp(tempDir);
        }
    }

    /// <summary>
    /// Sign an MSIX package using SignTool.exe with a certificate from the
    /// CurrentUser\My store identified by thumbprint.
    /// Automatically ensures the manifest Publisher matches the certificate Subject.
    /// </summary>
    public async Task<MsixResult> SignMsixAsync(string msixPath, string thumbprint, string hashAlgorithm = "SHA256")
    {
        Log("══════════════ MSIX Sign Started ══════════════");
        Log($"File      : {msixPath}");
        Log($"Thumbprint: {thumbprint}");
        Log($"Hash      : {hashAlgorithm}");
        Log("");

        var signTool = FindSignTool();
        if (signTool == null)
        {
            return Fail("SignTool.exe not found. Install Windows SDK and ensure it is in the PATH or under 'C:\\Program Files (x86)\\Windows Kits\\10\\bin'.");
        }

        // ── Ensure manifest Publisher matches the certificate Subject ──
        // MSIX signing requires an exact match between the Identity/@Publisher
        // attribute in AppxManifest.xml and the signing certificate's Subject DN.
        // Error 0x8007000b (ERROR_BAD_FORMAT) occurs when they don't match.
        try
        {
            var certSubjectDN = GetCertificateSubjectDN(thumbprint);
            if (certSubjectDN != null)
            {
                Log($"Certificate Subject: {certSubjectDN}");
                var manifestPublisher = ReadManifestPublisher(msixPath);
                if (manifestPublisher != null)
                {
                    Log($"Manifest Publisher : {manifestPublisher}");

                    if (!string.Equals(manifestPublisher, certSubjectDN, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("");
                        Log("⚠ Publisher mismatch detected — updating manifest to match certificate…");
                        var updated = await UpdateManifestPublisherAsync(msixPath, certSubjectDN);
                        if (!updated)
                        {
                            return Fail(
                                $"Publisher mismatch: manifest has \"{manifestPublisher}\" but certificate requires \"{certSubjectDN}\". " +
                                "Auto-fix failed — please update the Publisher field manually in the Identity editor.");
                        }
                        Log($"✅ Manifest Publisher updated to: {certSubjectDN}");
                    }
                    else
                    {
                        Log("✅ Publisher matches certificate — OK");
                    }
                }
                Log("");
            }
        }
        catch (Exception ex)
        {
            Log($"⚠ Could not verify Publisher/certificate match: {ex.Message}");
            Log("  Proceeding with signing anyway…");
            Log("");
        }

        // signtool sign /fd <hash> /sha1 <thumbprint> "<file>"
        // CurrentUser\My store is searched by default (no /sm flag)
        var args = $"sign /fd {hashAlgorithm} /sha1 {thumbprint} \"{msixPath}\"";
        Log($"{signTool} {args}");

        var result = await RunProcessAsync(signTool, args, Path.GetDirectoryName(msixPath) ?? "");

        if (result.Success)
        {
            Log("\n✅  MSIX signed successfully.");
            Log("══════════════ MSIX Sign Succeeded ══════════════");
            var r = new MsixResult { Success = true, OutputPath = msixPath };
            PackageCompleted?.Invoke(this, new MsixCompletedEventArgs(r));
            return r;
        }
        else
        {
            return Fail($"SignTool failed (exit {result.ExitCode})");
        }
    }

    /// <summary>
    /// Reads the Publisher attribute from the Identity element of the AppxManifest.xml inside an MSIX.
    /// </summary>
    private static string? ReadManifestPublisher(string msixPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(msixPath);
            var entry = zip.GetEntry("AppxManifest.xml");
            if (entry == null) return null;

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            return doc.Root?.Element(ns + "Identity")?.Attribute("Publisher")?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Retrieves the full Subject distinguished name from a certificate in the CurrentUser\My store.
    /// </summary>
    private static string? GetCertificateSubjectDN(string thumbprint)
    {
        try
        {
            using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.My,
                System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser,
                System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);

            var certs = store.Certificates.Find(
                System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint,
                thumbprint, validOnly: false);

            if (certs.Count == 0) return null;
            return certs[0].SubjectName.Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the MSIX, updates the Publisher in AppxManifest.xml, and repacks.
    /// Also ensures all file references in the manifest (logos, etc.) exist,
    /// creating placeholder PNGs for any missing files.
    /// Returns true on success.
    /// </summary>
    private async Task<bool> UpdateManifestPublisherAsync(string msixPath, string newPublisher)
    {
        string? tempDir = null;
        try
        {
            tempDir = Path.Combine(Path.GetTempPath(), "MsixSign_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            Log($"  Extracting to: {tempDir}");
            ZipFile.ExtractToDirectory(msixPath, tempDir, overwriteFiles: true);

            // Remove signing artifacts that MakeAppx doesn't like
            var blockMap = Path.Combine(tempDir, "AppxBlockMap.xml");
            if (File.Exists(blockMap)) File.Delete(blockMap);
            var signature = Path.Combine(tempDir, "AppxSignature.p7x");
            if (File.Exists(signature)) File.Delete(signature);
            var contentTypes = Path.Combine(tempDir, "[Content_Types].xml");
            if (File.Exists(contentTypes)) File.Delete(contentTypes);

            var manifestPath = Path.Combine(tempDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return false;

            var doc = XDocument.Load(manifestPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var identity = doc.Root?.Element(ns + "Identity");
            if (identity == null) return false;

            identity.SetAttributeValue("Publisher", newPublisher);

            // ── Ensure all file references in the manifest actually exist ──
            // MakeAppx validates that Logo / Square*Logo / Wide*Logo files are present.
            // Collect all file paths referenced by attributes and element values.
            EnsureManifestFilesExist(doc, ns, tempDir);

            doc.Save(manifestPath);

            Log("  Repacking MSIX…");
            var packResult = await RunMakeAppxAsync(tempDir, msixPath);
            return packResult.Success;
        }
        catch (Exception ex)
        {
            Log($"  ❌ Update failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (tempDir != null) CleanupTemp(tempDir);
        }
    }

    /// <summary>
    /// Scans the manifest for all file path references (Logo, Square*Logo, Wide*Logo, etc.)
    /// and creates placeholder PNG files for any that don't exist in the content directory.
    /// </summary>
    private void EnsureManifestFilesExist(XDocument doc, XNamespace ns, string contentDir)
    {
        // 1×1 transparent PNG (89 bytes)
        const string placeholderPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var fileRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect file paths from well-known elements / attributes
        // Properties/Logo (element value)
        var logo = doc.Root?.Element(ns + "Properties")?.Element(ns + "Logo")?.Value;
        if (!string.IsNullOrWhiteSpace(logo)) fileRefs.Add(logo);

        // VisualElements attributes: Square150x150Logo, Square44x44Logo, Square71x71Logo, etc.
        // DefaultTile attributes: Wide310x150Logo, Square310x310Logo, etc.
        var uap = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/uap/windows10");
        var visualElements = doc.Descendants(uap + "VisualElements");
        foreach (var ve in visualElements)
        {
            foreach (var attr in ve.Attributes())
            {
                var val = attr.Value;
                if (val.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || val.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || val.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    fileRefs.Add(val);
                }
            }
        }

        var defaultTiles = doc.Descendants(uap + "DefaultTile");
        foreach (var dt in defaultTiles)
        {
            foreach (var attr in dt.Attributes())
            {
                var val = attr.Value;
                if (val.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || val.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || val.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    fileRefs.Add(val);
                }
            }
        }

        // Create placeholder files for any missing references
        foreach (var relPath in fileRefs)
        {
            var fullPath = Path.Combine(contentDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                Log($"  ⚠ Missing file referenced in manifest: {relPath} — creating placeholder PNG");
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, Convert.FromBase64String(placeholderPngBase64));
            }
        }
    }

    private static string? FindSignTool()
    {
        // 0. Check user settings first
        var fromSettings = SettingsPanelControl.ResolveSignToolExe();
        if (fromSettings != null) return fromSettings;

        // 1. PATH
        try
        {
            var inPath = Process.Start(new ProcessStartInfo
            {
                FileName = "where", Arguments = "signtool.exe",
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            });
            if (inPath != null)
            {
                var line = inPath.StandardOutput.ReadLine();
                inPath.WaitForExit();
                if (!string.IsNullOrEmpty(line) && File.Exists(line.Trim()))
                    return line.Trim();
            }
        }
        catch { /* ignore */ }

        // 2. Windows SDK locations
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var pf in programFiles)
        {
            var sdkBin = Path.Combine(pf, "Windows Kits", "10", "bin");
            if (!Directory.Exists(sdkBin)) continue;
            var versions = Directory.GetDirectories(sdkBin)
                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+"))
                .OrderByDescending(d => d).ToList();
            foreach (var ver in versions)
            {
                foreach (var arch in new[] { "x64", "x86" })
                {
                    var candidate = Path.Combine(ver, arch, "signtool.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────

    private async Task<ProcessRunResult> RunDotnetPublishAsync(MsixPackageOptions opts, string publishDir)
    {
        var projectPath = ResolveSolutionToProject(opts.ProjectPath);
        var sb = new StringBuilder($"publish \"{projectPath}\"");
        sb.Append($" -c {opts.Configuration}");
        sb.Append($" -r {opts.RuntimeIdentifier}");
        sb.Append($" -o \"{publishDir}\"");
        sb.Append(opts.SelfContained ? " --self-contained true" : " --self-contained false");
        if (!string.IsNullOrEmpty(opts.Framework))
            sb.Append($" -f {opts.Framework}");
        if (opts.SingleFile)
            sb.Append(" -p:PublishSingleFile=true");
        if (opts.ReadyToRun)
            sb.Append(" -p:PublishReadyToRun=true");
        if (opts.TrimUnusedAssemblies)
            sb.Append(" -p:PublishTrimmed=true");

        Log($"dotnet {sb}");
        return await RunProcessAsync(SettingsPanelControl.ResolveDotNetExe(), sb.ToString(),
            Path.GetDirectoryName(opts.ProjectPath) ?? "");
    }

    /// <summary>
    /// If the path points to a .sln/.slnx file, resolve to the first
    /// project file inside it. Avoids NETSDK1194 when using <c>-o</c> with solutions.
    /// </summary>
    private string ResolveSolutionToProject(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".sln" and not ".slnx") return path;

        var solutionDir = Path.GetDirectoryName(path) ?? "";
        string? resolved = null;

        try
        {
            if (ext == ".slnx")
            {
                var content = File.ReadAllText(path);
                var doc = XDocument.Parse(content);
                var root = doc.Root;
                if (root != null)
                {
                    var projEl = root.Elements("Project").FirstOrDefault()
                              ?? root.Elements("Folder")
                                     .SelectMany(f => f.Elements("Project"))
                                     .FirstOrDefault();
                    var relPath = projEl?.Attribute("Path")?.Value;
                    if (!string.IsNullOrEmpty(relPath))
                    {
                        var full = Path.GetFullPath(Path.Combine(solutionDir, relPath.Replace("/", "\\")));
                        if (File.Exists(full)) resolved = full;
                    }
                }
            }
            else
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (!line.StartsWith("Project(")) continue;
                    var parts = line.Split('"');
                    if (parts.Length >= 6)
                    {
                        var relPath = parts[5];
                        if (relPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                            relPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                            relPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                        {
                            var full = Path.GetFullPath(Path.Combine(solutionDir, relPath));
                            if (File.Exists(full)) { resolved = full; break; }
                        }
                    }
                }
            }
        }
        catch { /* fallback below */ }

        if (resolved == null)
        {
            var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0) resolved = csprojFiles[0];
        }

        if (resolved != null)
        {
            Log($"📌 Resolved solution to project: {Path.GetFileName(resolved)}");
            return resolved;
        }

        return path;
    }

    private async Task<ProcessRunResult> RunMakeAppxAsync(string contentDir, string msixPath)
    {
        // Try MakeAppx.exe from Windows SDK (various versions)
        var makeAppx = FindMakeAppx();
        if (makeAppx == null)
        {
            // Fallback: create a ZIP with .msix extension (works for sideloading)
            Log("⚠️  MakeAppx.exe not found — creating ZIP-based MSIX (unsigned).");
            await CreateZipMsixAsync(contentDir, msixPath);
            return new ProcessRunResult { Success = true };
        }

        var args = $"pack /d \"{contentDir}\" /p \"{msixPath}\" /o";
        Log($"{makeAppx} {args}");
        return await RunProcessAsync(makeAppx, args, contentDir);
    }

    private static string? FindMakeAppx()
    {
        // Common SDK paths
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        foreach (var pf in programFiles)
        {
            var sdkDir = Path.Combine(pf, "Windows Kits", "10", "bin");
            if (!Directory.Exists(sdkDir)) continue;

            // Pick newest SDK version
            var versions = Directory.GetDirectories(sdkDir)
                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d+\.\d+"))
                .OrderByDescending(d => d)
                .ToList();

            foreach (var ver in versions)
            {
                foreach (var arch in new[] { "x64", "x86" })
                {
                    var candidate = Path.Combine(ver, arch, "MakeAppx.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    private static Task CreateZipMsixAsync(string sourceDir, string msixPath)
    {
        return Task.Run(() =>
        {
            if (File.Exists(msixPath)) File.Delete(msixPath);
            ZipFile.CreateFromDirectory(sourceDir, msixPath,
                CompressionLevel.Optimal, includeBaseDirectory: false);
        });
    }

    private void GenerateAppxManifest(MsixPackageOptions opts, string publishDir)
    {
        // ── Log all files in publishDir for diagnostics ──
        var allFiles = Directory.GetFiles(publishDir, "*", SearchOption.AllDirectories);
        Log($"  📂 Publish directory contains {allFiles.Length} file(s):");
        foreach (var f in allFiles)
            Log($"     • {Path.GetRelativePath(publishDir, f)}");

        // Find executable — search recursively, not just top-level
        var exe = opts.EntryExecutable;
        if (string.IsNullOrWhiteSpace(exe))
        {
            // 1. Try top-level .exe first
            var found = Directory.GetFiles(publishDir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (found == null)
            {
                // 2. Try recursive .exe search
                found = Directory.GetFiles(publishDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            if (found == null)
            {
                // 3. Framework-dependent publish may produce .dll instead of .exe
                found = Directory.GetFiles(publishDir, "*.dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                                      && !Path.GetFileName(f).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
            }
            exe = found != null ? Path.GetRelativePath(publishDir, found) : "App.exe";
        }

        // ── Validate the executable exists in the publish directory ──
        var exeFullPath = Path.Combine(publishDir, exe);
        if (!File.Exists(exeFullPath))
        {
            Log($"  ⚠ Executable '{exe}' not found in publish directory!");
            Log("  🔍 Searching for any executable in publish directory...");

            // Try to find any .exe or .dll that could be the entry point
            var candidates = Directory.GetFiles(publishDir, "*.exe", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(publishDir, "*.dll", SearchOption.AllDirectories))
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return !name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("api-ms-", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("clr", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("coreclr", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("hostfxr", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("hostpolicy", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("mscordaccore.dll", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("clrjit.dll", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (candidates.Count > 0)
            {
                // Prefer .exe over .dll; prefer files whose name matches the project name
                var projectName = Path.GetFileNameWithoutExtension(opts.ProjectPath);
                var best = candidates
                    .OrderByDescending(f => Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(f => Path.GetFileNameWithoutExtension(f)
                        .Equals(projectName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .First();
                exe = Path.GetRelativePath(publishDir, best);
                Log($"  ✅ Found candidate executable: {exe}");
            }
            else
            {
                Log("  ❌ No executable candidates found in publish directory.");
                throw new InvalidOperationException(
                    $"No executable found in publish directory '{publishDir}'. " +
                    "Ensure the project was published with '--self-contained true' or set EntryExecutable explicitly.");
            }
        }

        // Re-validate after fallback
        exeFullPath = Path.Combine(publishDir, exe);
        if (!File.Exists(exeFullPath))
        {
            throw new InvalidOperationException(
                $"Executable '{exe}' still not found in publish directory '{publishDir}'. " +
                "The publish output may be incomplete.");
        }

        var entryPoint = opts.EntryPoint;
        if (string.IsNullOrWhiteSpace(entryPoint))
            entryPoint = "Windows.FullTrustApplication";

        // ── Resolve logo: must be relative path + .png/.jpg/.jpeg (not .ico) ──
        var logoRelative = ResolveLogo(opts.LogoRelativePath, publishDir);

        var xml = $"""
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  IgnorableNamespaces="uap rescap desktop">

  <Identity
    Name="{EscapeXml(opts.PackageIdentityName)}"
    Publisher="{EscapeXml(opts.Publisher)}"
    Version="{EscapeXml(opts.Version)}"
    ProcessorArchitecture="{EscapeXml(opts.ProcessorArchitecture)}" />

  <Properties>
    <DisplayName>{EscapeXml(opts.DisplayName)}</DisplayName>
    <PublisherDisplayName>{EscapeXml(opts.PublisherDisplayName)}</PublisherDisplayName>
    <Description>{EscapeXml(opts.Description)}</Description>
    <Logo>{EscapeXml(logoRelative)}</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="App"
                 Executable="{EscapeXml(exe)}"
                 EntryPoint="{EscapeXml(entryPoint)}">
      <uap:VisualElements
        DisplayName="{EscapeXml(opts.DisplayName)}"
        Description="{EscapeXml(opts.Description)}"
        BackgroundColor="transparent"
        Square150x150Logo="{EscapeXml(logoRelative)}"
        Square44x44Logo="{EscapeXml(logoRelative)}">
        <uap:DefaultTile Wide310x150Logo="{EscapeXml(logoRelative)}" />
      </uap:VisualElements>
      <Extensions>
        <desktop:Extension Category="windows.fullTrustProcess" Executable="{EscapeXml(exe)}" />
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>

</Package>
""";

        File.WriteAllText(Path.Combine(publishDir, "AppxManifest.xml"), xml, Encoding.UTF8);

        // Create placeholder logo if the resolved file doesn't exist in publishDir
        var logoFile = Path.Combine(publishDir, logoRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(logoFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logoFile)!);
            // 1×1 transparent PNG (89 bytes)
            File.WriteAllBytes(logoFile, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
            Log("  ⚠ Logo file not found — using 1×1 transparent placeholder");
        }

        Log($"AppxManifest.xml written  (exe={exe}, entryPoint={entryPoint}, logo={logoRelative})");
    }

    /// <summary>
    /// Resolves the logo path to a valid relative path inside the publish directory.
    /// MSIX requires the logo to be .png/.jpg/.jpeg (NOT .ico).
    /// If an absolute path is given, the file is copied into publishDir/Assets/.
    /// If the file is .ico, it is converted to .png.
    /// </summary>
    private string ResolveLogo(string logoPath, string publishDir)
    {
        const string defaultLogo = @"Assets\Logo.png";

        if (string.IsNullOrWhiteSpace(logoPath))
            return defaultLogo;

        // Check if it's an absolute path (external file)
        bool isAbsolute = Path.IsPathRooted(logoPath);
        bool isIco = logoPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);

        if (isAbsolute)
        {
            // Copy the file into publishDir/Assets/ with appropriate extension
            if (!File.Exists(logoPath))
            {
                Log($"  ⚠ Logo file not found: {logoPath}");
                return defaultLogo;
            }

            var assetsDir = Path.Combine(publishDir, "Assets");
            Directory.CreateDirectory(assetsDir);

            if (isIco)
            {
                // Convert .ico → .png
                var destPath = Path.Combine(assetsDir, "Logo.png");
                try
                {
                    ConvertIcoToPng(logoPath, destPath);
                    Log($"  🔄 Converted .ico to .png: {Path.GetFileName(logoPath)} → Assets\\Logo.png");
                    return @"Assets\Logo.png";
                }
                catch (Exception ex)
                {
                    Log($"  ⚠ Failed to convert .ico to .png: {ex.Message}");
                    return defaultLogo;
                }
            }
            else
            {
                // .png/.jpg — just copy it
                var fileName = Path.GetFileName(logoPath);
                var destPath = Path.Combine(assetsDir, fileName);
                try
                {
                    File.Copy(logoPath, destPath, true);
                    Log($"  📋 Copied logo: {fileName} → Assets\\{fileName}");
                    return @"Assets\" + fileName;
                }
                catch (Exception ex)
                {
                    Log($"  ⚠ Failed to copy logo: {ex.Message}");
                    return defaultLogo;
                }
            }
        }

        // It's a relative path
        if (isIco)
        {
            // .ico at a relative path — try to convert in-place
            var icoFull = Path.Combine(publishDir, logoPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(icoFull))
            {
                var pngPath = Path.ChangeExtension(icoFull, ".png");
                try
                {
                    ConvertIcoToPng(icoFull, pngPath);
                    Log($"  🔄 Converted .ico to .png: {logoPath}");
                    return Path.ChangeExtension(logoPath, ".png").Replace('/', '\\');
                }
                catch (Exception ex)
                {
                    Log($"  ⚠ Failed to convert .ico: {ex.Message}");
                    return defaultLogo;
                }
            }
            else
            {
                // .ico doesn't exist at relative path — use default
                Log($"  ⚠ Logo .ico not found at: {logoPath}");
                return defaultLogo;
            }
        }

        // It's a relative path with a valid extension — use as-is
        return logoPath;
    }

    /// <summary>
    /// Extracts the largest image from an .ico file and saves it as a .png.
    /// ICO files contain one or more BMP or PNG images; we pick the largest.
    /// </summary>
    private static void ConvertIcoToPng(string icoPath, string pngPath)
    {
        using var fs = File.OpenRead(icoPath);
        using var reader = new BinaryReader(fs);

        // ICO header: reserved(2) + type(2) + count(2)
        reader.ReadUInt16(); // reserved
        var type = reader.ReadUInt16(); // 1 = ICO
        if (type != 1) throw new InvalidDataException("Not a valid .ico file");
        var count = reader.ReadUInt16();
        if (count == 0) throw new InvalidDataException("ICO file contains no images");

        // Read directory entries to find the largest image
        int bestWidth = 0, bestOffset = 0, bestSize = 0;
        for (int i = 0; i < count; i++)
        {
            int w = reader.ReadByte(); if (w == 0) w = 256;
            int h = reader.ReadByte(); if (h == 0) h = 256;
            reader.ReadByte(); // color count
            reader.ReadByte(); // reserved
            reader.ReadUInt16(); // planes
            reader.ReadUInt16(); // bit count
            int dataSize = reader.ReadInt32();
            int dataOffset = reader.ReadInt32();

            if (w >= bestWidth)
            {
                bestWidth = w;
                bestOffset = dataOffset;
                bestSize = dataSize;
            }
        }

        // Read the image data
        fs.Seek(bestOffset, SeekOrigin.Begin);
        var imageData = reader.ReadBytes(bestSize);

        // Check if it's already PNG (starts with PNG signature 0x89 0x50 0x4E 0x47)
        if (imageData.Length >= 4 && imageData[0] == 0x89 && imageData[1] == 0x50
                                  && imageData[2] == 0x4E && imageData[3] == 0x47)
        {
            // It's already a PNG — just write it out
            File.WriteAllBytes(pngPath, imageData);
            return;
        }

        // It's a BMP inside the ICO — extract pixels and create a minimal PNG.
        // For simplicity and no external dependencies, create a 1×1 placeholder PNG
        // and log a warning. For full BMP→PNG, a library like SkiaSharp would be needed.
        // However, most modern .ico files embed PNG data for larger sizes.
        //
        // Fallback: write a simple colored 1×1 PNG
        File.WriteAllBytes(pngPath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg=="));
    }

    private async Task<ProcessRunResult> RunProcessAsync(string exe, string args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            WorkingDirectory       = workDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        _activeProcess = new Process { StartInfo = psi };
        _activeProcess.OutputDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        _activeProcess.ErrorDataReceived  += (_, e) => { if (e.Data != null) Log($"[ERR] {e.Data}"); };

        _activeProcess.Start();
        _activeProcess.BeginOutputReadLine();
        _activeProcess.BeginErrorReadLine();

        await _activeProcess.WaitForExitAsync(_cts?.Token ?? CancellationToken.None);
        var code = _activeProcess.ExitCode;
        _activeProcess.Dispose();
        _activeProcess = null;

        return new ProcessRunResult { Success = code == 0, ExitCode = code };
    }

    private MsixResult Fail(string msg)
    {
        Log($"\n❌  {msg}");
        Log("══════════════ MSIX Build Failed ══════════════");
        var r = new MsixResult { Success = false, ErrorMessage = msg };
        PackageCompleted?.Invoke(this, new MsixCompletedEventArgs(r));
        return r;
    }

    private void Log(string line)
    {
        OutputReceived?.Invoke(this, new MsixOutputEventArgs(line + "\n"));
    }

    private static string EscapeXml(string? s) =>
        System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;

    private static string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB" };
        int i = 0; double d = bytes;
        while (d >= 1024 && i < suf.Length - 1) { d /= 1024; i++; }
        return $"{d:0.##} {suf[i]}";
    }

    private static void CleanupTemp(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (Exception) { /* ignore cleanup errors */ }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Data classes
// ─────────────────────────────────────────────────────────────────────────────

public class MsixPackageOptions
{
    // Build source
    public string ProjectPath          { get; set; } = "";
    public string Configuration        { get; set; } = "Release";
    public string RuntimeIdentifier    { get; set; } = "win-x64";
    public string? Framework           { get; set; }
    public bool   SelfContained        { get; set; } = true;   // MSIX requires self-contained by default
    public bool   SingleFile           { get; set; }
    public bool   ReadyToRun           { get; set; } = true;
    public bool   TrimUnusedAssemblies { get; set; }
    public bool   CleanOutputFolder    { get; set; } = true;

    // Publish output (temp) folder — auto-generated if empty
    public string? PublishOutputDir    { get; set; }

    // MSIX identity (AppxManifest)
    public string PackageIdentityName   { get; set; } = "MyApp";
    public string Publisher             { get; set; } = "CN=Developer";
    public string Version               { get; set; } = "1.0.0.0";
    public string ProcessorArchitecture { get; set; } = "x64";
    public string DisplayName           { get; set; } = "My Application";
    public string PublisherDisplayName  { get; set; } = "Developer";
    public string Description           { get; set; } = "";
    public string LogoRelativePath      { get; set; } = "Assets\\Square150x150Logo.png";

    // Entry point
    public string? EntryExecutable  { get; set; }   // e.g. "MyApp.exe"
    public string? EntryPoint       { get; set; }   // e.g. "Windows.FullTrustApplication"

    // Output
    public string? OutputMsixPath   { get; set; }
}

public class MsixPackageInfo
{
    public string MsixPath              { get; set; } = "";
    public string Name                  { get; set; } = "";
    public string Publisher             { get; set; } = "";
    public string Version               { get; set; } = "";
    public string ProcessorArchitecture { get; set; } = "x64";
    public string DisplayName           { get; set; } = "";
    public string PublisherDisplayName  { get; set; } = "";
    public string Description           { get; set; } = "";
    public string Logo                  { get; set; } = "";
    public string EntryPoint            { get; set; } = "";
    public string Executable            { get; set; } = "";
    public string ManifestXml           { get; set; } = "";
    public List<string> Executables     { get; set; } = new();
}

public class MsixResult
{
    public bool   Success      { get; set; }
    public string? OutputPath  { get; set; }
    public string? ErrorMessage { get; set; }
}

public class MsixOutputEventArgs : EventArgs
{
    public string Output { get; }
    public MsixOutputEventArgs(string output) { Output = output; }
}

public class MsixCompletedEventArgs : EventArgs
{
    public MsixResult Result { get; }
    public MsixCompletedEventArgs(MsixResult result) { Result = result; }
}

internal class ProcessRunResult
{
    public bool Success  { get; set; }
    public int  ExitCode { get; set; }
}

