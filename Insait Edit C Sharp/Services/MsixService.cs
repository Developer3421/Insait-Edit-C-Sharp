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
            GenerateAppxManifest(opts, publishDir);

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

    // ─────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────

    private async Task<ProcessRunResult> RunDotnetPublishAsync(MsixPackageOptions opts, string publishDir)
    {
        var sb = new StringBuilder($"publish \"{opts.ProjectPath}\"");
        sb.Append($" -c {opts.Configuration}");
        sb.Append($" -r {opts.RuntimeIdentifier}");
        sb.Append($" -o \"{publishDir}\"");
        sb.Append(" --self-contained true");
        if (!string.IsNullOrEmpty(opts.Framework))
            sb.Append($" -f {opts.Framework}");
        if (opts.SingleFile)
            sb.Append(" -p:PublishSingleFile=true");
        if (opts.ReadyToRun)
            sb.Append(" -p:PublishReadyToRun=true");
        if (opts.TrimUnusedAssemblies)
            sb.Append(" -p:PublishTrimmed=true");

        Log($"dotnet {sb}");
        return await RunProcessAsync("dotnet", sb.ToString(),
            Path.GetDirectoryName(opts.ProjectPath) ?? "");
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
        // Find executable
        var exe = opts.EntryExecutable;
        if (string.IsNullOrWhiteSpace(exe))
        {
            var found = Directory.GetFiles(publishDir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            exe = found != null ? Path.GetFileName(found) : "App.exe";
        }

        var entryPoint = opts.EntryPoint;
        if (string.IsNullOrWhiteSpace(entryPoint))
            entryPoint = "Windows.FullTrustApplication";

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
    <Logo>{EscapeXml(opts.LogoRelativePath)}</Logo>
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
        Square150x150Logo="{EscapeXml(opts.LogoRelativePath)}"
        Square44x44Logo="{EscapeXml(opts.LogoRelativePath)}">
        <uap:DefaultTile Wide310x150Logo="{EscapeXml(opts.LogoRelativePath)}" />
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

        // Create placeholder logo if none supplied or not present
        var logoFile = Path.Combine(publishDir, opts.LogoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(logoFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logoFile)!);
            // 1×1 transparent PNG (89 bytes)
            File.WriteAllBytes(logoFile, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        }

        Log($"AppxManifest.xml written  (exe={exe}, entryPoint={entryPoint})");
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
    public bool   SingleFile           { get; set; }
    public bool   ReadyToRun           { get; set; } = true;
    public bool   TrimUnusedAssemblies { get; set; }

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

