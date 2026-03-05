using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for publishing .NET projects like JetBrains Rider
/// </summary>
public class PublishService
{
    public event EventHandler<PublishOutputEventArgs>? OutputReceived;
    public event EventHandler<PublishCompletedEventArgs>? PublishCompleted;
    public event EventHandler? PublishStarted;

    private Process? _publishProcess;
    private readonly StringBuilder _outputBuffer = new();

    /// <summary>
    /// Publish a project with specified profile
    /// </summary>
    public async Task<PublishResult> PublishAsync(PublishProfile profile)
    {
        if (string.IsNullOrEmpty(profile.ProjectPath))
        {
            return new PublishResult
            {
                Success = false,
                ErrorMessage = "No project specified"
            };
        }

        _outputBuffer.Clear();
        PublishStarted?.Invoke(this, EventArgs.Empty);

        OnOutput($"========== Publish Started ==========\n");
        OnOutput($"Project: {profile.ProjectPath}\n");
        OnOutput($"Configuration: {profile.Configuration}\n");
        OnOutput($"Target Runtime: {profile.RuntimeIdentifier ?? "Any"}\n");
        OnOutput($"Output: {profile.OutputPath}\n");
        OnOutput($"Self-Contained: {profile.SelfContained}\n");
        if (profile.SingleFile)
            OnOutput($"Single File: Yes\n");
        if (profile.ReadyToRun)
            OnOutput($"ReadyToRun: Yes\n");
        if (profile.TrimUnusedAssemblies)
            OnOutput($"Trimming: Yes\n");
        if (!string.IsNullOrEmpty(profile.ApplicationIcon))
            OnOutput($"Application Icon: {profile.ApplicationIcon}\n");
        if (profile.CleanOutputFolder)
            OnOutput($"Clean Output Folder: Yes\n");
        OnOutput("\n");

        try
        {
            // Clean the output folder before publishing if requested
            if (profile.CleanOutputFolder && !string.IsNullOrEmpty(profile.OutputPath))
            {
                if (Directory.Exists(profile.OutputPath))
                {
                    OnOutput($"🗑  Cleaning output folder: {profile.OutputPath}\n");
                    try
                    {
                        var files = Directory.GetFiles(profile.OutputPath, "*", SearchOption.AllDirectories);
                        var dirs = Directory.GetDirectories(profile.OutputPath, "*", SearchOption.AllDirectories)
                            .OrderByDescending(d => d.Length); // delete deepest first

                        int deletedFiles = 0;
                        foreach (var file in files)
                        {
                            try { File.Delete(file); deletedFiles++; }
                            catch (Exception ex) { OnOutput($"  ⚠ Could not delete: {Path.GetFileName(file)} — {ex.Message}\n"); }
                        }

                        int deletedDirs = 0;
                        foreach (var dir in dirs)
                        {
                            try { Directory.Delete(dir, false); deletedDirs++; }
                            catch { /* non-empty dirs will fail, that's fine */ }
                        }

                        OnOutput($"  Deleted {deletedFiles} file(s), {deletedDirs} folder(s)\n\n");
                    }
                    catch (Exception ex)
                    {
                        OnOutput($"  ⚠ Clean failed: {ex.Message}\n\n");
                    }
                }
                else
                {
                    OnOutput($"  Output folder does not exist yet — nothing to clean.\n\n");
                }
            }

            var args = BuildPublishArguments(profile);

            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(profile.ProjectPath) ?? "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _publishProcess = new Process { StartInfo = startInfo };

            _publishProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputBuffer.AppendLine(e.Data);
                    OnOutput(e.Data + "\n");
                }
            };

            _publishProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputBuffer.AppendLine(e.Data);
                    OnOutput($"[ERROR] {e.Data}\n");
                }
            };

            _publishProcess.Start();
            _publishProcess.BeginOutputReadLine();
            _publishProcess.BeginErrorReadLine();

            await _publishProcess.WaitForExitAsync();

            var exitCode = _publishProcess.ExitCode;
            var success = exitCode == 0;

            if (success)
            {
                OnOutput($"\n========== Publish Succeeded ==========\n");
                OnOutput($"Output: {profile.OutputPath}\n");
                
                // Get output folder size
                if (Directory.Exists(profile.OutputPath))
                {
                    var size = GetDirectorySize(profile.OutputPath);
                    OnOutput($"Size: {FormatFileSize(size)}\n");
                    
                    // List main files
                    var files = Directory.GetFiles(profile.OutputPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".exe") || f.EndsWith(".dll") || f.EndsWith(".json"))
                        .Take(10)
                        .ToList();
                    
                    if (files.Count > 0)
                    {
                        OnOutput("\nMain files:\n");
                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            OnOutput($"  {fileInfo.Name} ({FormatFileSize(fileInfo.Length)})\n");
                        }
                    }
                }
            }
            else
            {
                OnOutput($"\n========== Publish Failed (exit code {exitCode}) ==========\n");

                // Extract and summarize compilation errors from the output
                var output = _outputBuffer.ToString();
                var errorLines = output.Split('\n')
                    .Where(l => l.Contains(" error ", StringComparison.OrdinalIgnoreCase)
                             || l.Contains(": error ", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Distinct()
                    .ToList();

                if (errorLines.Count > 0)
                {
                    OnOutput($"\n🔴 {errorLines.Count} error(s) found:\n");
                    foreach (var err in errorLines.Take(20))
                        OnOutput($"  • {err}\n");
                    if (errorLines.Count > 20)
                        OnOutput($"  … and {errorLines.Count - 20} more\n");
                    OnOutput("\n💡 Fix the errors above and try again.\n");
                }
            }

            var result = new PublishResult
            {
                Success = success,
                ExitCode = exitCode,
                Output = _outputBuffer.ToString(),
                OutputPath = profile.OutputPath,
                ErrorMessage = success ? null : $"Publish failed with exit code {exitCode}"
            };

            PublishCompleted?.Invoke(this, new PublishCompletedEventArgs(result));

            return result;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Publish error: {ex.Message}";
            OnOutput($"\n[ERROR] {errorMessage}\n");

            var result = new PublishResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Output = _outputBuffer.ToString()
            };

            PublishCompleted?.Invoke(this, new PublishCompletedEventArgs(result));

            return result;
        }
        finally
        {
            _publishProcess?.Dispose();
            _publishProcess = null;
        }
    }

    /// <summary>
    /// Build dotnet publish arguments
    /// </summary>
    private string BuildPublishArguments(PublishProfile profile)
    {
        var args = new StringBuilder();
        var projectPath = ResolveProjectPath(profile.ProjectPath);
        args.Append($"publish \"{projectPath}\"");
        args.Append($" -c {profile.Configuration}");
        args.Append($" -o \"{profile.OutputPath}\"");

        if (!string.IsNullOrEmpty(profile.RuntimeIdentifier))
        {
            args.Append($" -r {profile.RuntimeIdentifier}");
        }

        if (!string.IsNullOrEmpty(profile.Framework))
        {
            args.Append($" -f {profile.Framework}");
        }

        if (profile.SelfContained)
        {
            args.Append(" --self-contained true");
        }
        else
        {
            args.Append(" --self-contained false");
        }

        if (profile.SingleFile)
        {
            args.Append(" -p:PublishSingleFile=true");
        }

        if (profile.ReadyToRun)
        {
            args.Append(" -p:PublishReadyToRun=true");
        }

        if (profile.TrimUnusedAssemblies)
        {
            args.Append(" -p:PublishTrimmed=true");
        }

        if (profile.EnableCompressionInSingleFile)
        {
            args.Append(" -p:EnableCompressionInSingleFile=true");
        }

        if (profile.IncludeNativeLibrariesForSelfExtract)
        {
            args.Append(" -p:IncludeNativeLibrariesForSelfExtract=true");
        }

        if (!string.IsNullOrEmpty(profile.PublishProfileName))
        {
            args.Append($" -p:PublishProfile=\"{profile.PublishProfileName}\"");
        }

        if (!string.IsNullOrEmpty(profile.ApplicationIcon))
        {
            args.Append($" -p:ApplicationIcon=\"{profile.ApplicationIcon}\"");
        }

        // Additional properties
        foreach (var prop in profile.AdditionalProperties)
        {
            args.Append($" -p:{prop.Key}={prop.Value}");
        }

        return args.ToString();
    }

    /// <summary>
    /// Get available runtime identifiers
    /// </summary>
    public static List<RuntimeIdentifierInfo> GetAvailableRuntimeIdentifiers()
    {
        return new List<RuntimeIdentifierInfo>
        {
            // Windows
            new("win-x64", "Windows x64", "Windows", true),
            new("win-x86", "Windows x86", "Windows", false),
            new("win-arm64", "Windows ARM64", "Windows", false),
            
            // Linux
            new("linux-x64", "Linux x64", "Linux", true),
            new("linux-arm64", "Linux ARM64", "Linux", false),
            new("linux-arm", "Linux ARM", "Linux", false),
            new("linux-musl-x64", "Linux musl x64 (Alpine)", "Linux", false),
            new("linux-musl-arm64", "Linux musl ARM64 (Alpine)", "Linux", false),
            
            // macOS
            new("osx-x64", "macOS x64", "macOS", true),
            new("osx-arm64", "macOS ARM64 (Apple Silicon)", "macOS", true),
            
            // Portable (framework-dependent)
            new("", "Portable (Any OS)", "Portable", true)
        };
    }

    /// <summary>
    /// Get available publish profiles from project
    /// </summary>
    public async Task<List<string>> GetPublishProfilesAsync(string projectPath)
    {
        var profiles = new List<string>();
        var projectDir = Path.GetDirectoryName(projectPath);
        
        if (string.IsNullOrEmpty(projectDir)) return profiles;

        // Check Properties/PublishProfiles folder
        var profilesDir = Path.Combine(projectDir, "Properties", "PublishProfiles");
        if (Directory.Exists(profilesDir))
        {
            var pubxmlFiles = Directory.GetFiles(profilesDir, "*.pubxml");
            foreach (var file in pubxmlFiles)
            {
                profiles.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return profiles;
    }

    /// <summary>
    /// Get available frameworks from project
    /// </summary>
    public async Task<List<string>> GetProjectFrameworksAsync(string projectPath)
    {
        var frameworks = new List<string>();
        
        try
        {
            var content = await File.ReadAllTextAsync(projectPath);
            
            // Single framework
            var tfmMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<TargetFramework>([^<]+)</TargetFramework>");
            
            if (tfmMatch.Success)
            {
                frameworks.Add(tfmMatch.Groups[1].Value);
            }
            
            // Multiple frameworks
            var tfmsMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<TargetFrameworks>([^<]+)</TargetFrameworks>");
            
            if (tfmsMatch.Success)
            {
                var tfms = tfmsMatch.Groups[1].Value.Split(';');
                frameworks.AddRange(tfms);
            }
        }
        catch
        {
            // Return empty list on error
        }
        
        return frameworks.Distinct().ToList();
    }

    /// <summary>
    /// Create a default publish profile for a project
    /// </summary>
    public PublishProfile CreateDefaultProfile(string projectPath, string configuration = "Release")
    {
        var projectDir = Path.GetDirectoryName(projectPath) ?? "";
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        return new PublishProfile
        {
            Name = $"{projectName} - Default",
            ProjectPath = projectPath,
            Configuration = configuration,
            OutputPath = Path.Combine(projectDir, "bin", "publish"),
            SelfContained = false,
            RuntimeIdentifier = null, // Framework-dependent
            SingleFile = false,
            ReadyToRun = false,
            TrimUnusedAssemblies = false
        };
    }

    /// <summary>
    /// Create a self-contained publish profile
    /// </summary>
    public PublishProfile CreateSelfContainedProfile(string projectPath, string runtimeIdentifier, string configuration = "Release")
    {
        var projectDir = Path.GetDirectoryName(projectPath) ?? "";
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        return new PublishProfile
        {
            Name = $"{projectName} - {runtimeIdentifier}",
            ProjectPath = projectPath,
            Configuration = configuration,
            OutputPath = Path.Combine(projectDir, "bin", "publish", runtimeIdentifier),
            SelfContained = true,
            RuntimeIdentifier = runtimeIdentifier,
            SingleFile = true,
            ReadyToRun = true,
            TrimUnusedAssemblies = false
        };
    }

    /// <summary>
    /// Create a single-file publish profile
    /// </summary>
    public PublishProfile CreateSingleFileProfile(string projectPath, string runtimeIdentifier, string configuration = "Release")
    {
        var projectDir = Path.GetDirectoryName(projectPath) ?? "";
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        return new PublishProfile
        {
            Name = $"{projectName} - Single File ({runtimeIdentifier})",
            ProjectPath = projectPath,
            Configuration = configuration,
            OutputPath = Path.Combine(projectDir, "bin", "publish", $"{runtimeIdentifier}-single"),
            SelfContained = true,
            RuntimeIdentifier = runtimeIdentifier,
            SingleFile = true,
            ReadyToRun = true,
            TrimUnusedAssemblies = true,
            EnableCompressionInSingleFile = true,
            IncludeNativeLibrariesForSelfExtract = true
        };
    }

    /// <summary>
    /// Cancel publishing
    /// </summary>
    public void Cancel()
    {
        try
        {
            if (_publishProcess != null && !_publishProcess.HasExited)
            {
                _publishProcess.Kill(entireProcessTree: true);
                OnOutput("\n========== Publish Cancelled ==========\n");
            }
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Failed to cancel publish: {ex.Message}\n");
        }
    }

    private void OnOutput(string message)
    {
        OutputReceived?.Invoke(this, new PublishOutputEventArgs(message));
    }

    /// <summary>
    /// If the path points to a .sln/.slnx file, resolve it to the first
    /// project file found inside. This avoids NETSDK1194 when using
    /// <c>dotnet publish --output</c> against a solution.
    /// </summary>
    private string ResolveProjectPath(string path)
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
                // Parse XML-based .slnx
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
            else // .sln
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (!line.StartsWith("Project(")) continue;
                    // Format: Project("{GUID}") = "Name", "Path.csproj", "{GUID}"
                    var parts = line.Split('"');
                    // parts[5] is typically the relative project path
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
        catch
        {
            // If parsing fails, fall back to searching for .csproj files
        }

        // Fallback: scan for .csproj files in the solution directory
        if (resolved == null)
        {
            var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
                resolved = csprojFiles[0];
        }

        if (resolved != null)
        {
            OnOutput($"📌 Resolved solution to project: {Path.GetFileName(resolved)}\n");
            return resolved;
        }

        return path; // last resort — return the original
    }

    private long GetDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.##} {suffixes[suffixIndex]}";
    }
}

/// <summary>
/// Publish profile configuration
/// </summary>
public class PublishProfile
{
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Configuration { get; set; } = "Release";
    public string OutputPath { get; set; } = "";
    public string? RuntimeIdentifier { get; set; }
    public string? Framework { get; set; }
    public string? PublishProfileName { get; set; }
    public bool SelfContained { get; set; }
    public bool SingleFile { get; set; }
    public bool ReadyToRun { get; set; }
    public bool TrimUnusedAssemblies { get; set; }
    public bool EnableCompressionInSingleFile { get; set; }
    public bool IncludeNativeLibrariesForSelfExtract { get; set; }
    public bool CleanOutputFolder { get; set; }
    public string? ApplicationIcon { get; set; }
    public Dictionary<string, string> AdditionalProperties { get; set; } = new();
}

/// <summary>
/// Runtime identifier information
/// </summary>
public class RuntimeIdentifierInfo
{
    public string Rid { get; }
    public string DisplayName { get; }
    public string Platform { get; }
    public bool IsCommon { get; }

    public RuntimeIdentifierInfo(string rid, string displayName, string platform, bool isCommon)
    {
        Rid = rid;
        DisplayName = displayName;
        Platform = platform;
        IsCommon = isCommon;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Publish result
/// </summary>
public class PublishResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event args for publish output
/// </summary>
public class PublishOutputEventArgs : EventArgs
{
    public string Output { get; }

    public PublishOutputEventArgs(string output)
    {
        Output = output;
    }
}

/// <summary>
/// Event args for publish completed
/// </summary>
public class PublishCompletedEventArgs : EventArgs
{
    public PublishResult Result { get; }

    public PublishCompletedEventArgs(PublishResult result)
    {
        Result = result;
    }
}

