using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Esp.Services;

/// <summary>
/// Build service for nanoFramework projects (.nfproj)
/// Uses MSBuild or dotnet build with nanoFramework workload
/// </summary>
public class NanoBuildService
{
    public event EventHandler<NanoBuildOutputEventArgs>? OutputReceived;
    public event EventHandler<NanoBuildCompletedEventArgs>? BuildCompleted;
    public event EventHandler? BuildStarted;

    private Process? _buildProcess;
    private readonly StringBuilder _outputBuffer = new();
    private readonly StringBuilder _errorBuffer = new();

    /// <summary>
    /// Check if old PE files exist in the deploy folder for a given project path.
    /// Returns the list of existing PE files and the deploy folder path.
    /// </summary>
    public (List<string> existingPeFiles, string deployFolder) GetExistingDeployPeFiles(string projectPath)
    {
        var targetFile = FindNanoBuildTarget(projectPath);
        if (targetFile == null)
            return (new List<string>(), string.Empty);

        var projectDir = Path.GetDirectoryName(targetFile);
        if (string.IsNullOrEmpty(projectDir))
            return (new List<string>(), string.Empty);

        var deployFolder = Path.Combine(projectDir, "pe files for deploy");
        if (!Directory.Exists(deployFolder))
            return (new List<string>(), deployFolder);

        var peFiles = Directory.GetFiles(deployFolder, "*.pe").ToList();
        return (peFiles, deployFolder);
    }

    /// <summary>
    /// Delete all old PE files from the deploy folder before a new build.
    /// </summary>
    public void CleanDeployPeFiles(string projectPath)
    {
        var targetFile = FindNanoBuildTarget(projectPath);
        if (targetFile == null) return;

        var projectDir = Path.GetDirectoryName(targetFile);
        if (string.IsNullOrEmpty(projectDir)) return;

        var deployFolder = Path.Combine(projectDir, "pe files for deploy");
        if (!Directory.Exists(deployFolder)) return;

        try
        {
            var peFiles = Directory.GetFiles(deployFolder, "*.pe");
            foreach (var pe in peFiles)
            {
                File.Delete(pe);
            }
            OnOutput($"Cleaned {peFiles.Length} old PE file(s) from deploy folder.\n");
        }
        catch (Exception ex)
        {
            OnOutput($"[WARNING] Could not clean old PE files: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Build a nanoFramework project
    /// </summary>
    public async Task<NanoBuildResult> BuildAsync(string projectPath, string configuration = "Debug")
    {
        if (string.IsNullOrEmpty(projectPath))
        {
            return new NanoBuildResult
            {
                Success = false,
                ErrorMessage = "No project path specified"
            };
        }

        var targetFile = FindNanoBuildTarget(projectPath);
        if (targetFile == null)
        {
            return new NanoBuildResult
            {
                Success = false,
                ErrorMessage = $"No nanoFramework project file found in: {projectPath}"
            };
        }

        _outputBuffer.Clear();
        _errorBuffer.Clear();

        BuildStarted?.Invoke(this, EventArgs.Empty);
        OnOutput($"========== nanoFramework Build Started: {Path.GetFileName(targetFile)} ==========\n");
        OnOutput($"Configuration: {configuration}\n");
        OnOutput($"Target: {targetFile}\n\n");

        try
        {
            // Patch project file to remove settings that cause MetadataProcessor failures
            // (embedded resources, ImplicitUsings, etc.) — safe to call on every build.
            PatchNanoFrameworkProjectFile(targetFile);
            
            // Auto-restore NuGet packages before building
            await RestorePackagesBeforeBuildAsync(targetFile);
            
            ProcessStartInfo startInfo;
            var isLegacyNfproj = targetFile.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase);

            if (isLegacyNfproj)
            {
                // Legacy .nfproj: use MSBuild or dotnet msbuild
                var msbuildPath = FindMSBuild();
                var propsBuilder = new StringBuilder();
                propsBuilder.Append($"/p:Configuration={configuration}");
                propsBuilder.Append(" /t:Build");
                var outputDir = Path.Combine(Path.GetDirectoryName(targetFile) ?? projectPath, "bin", configuration);
                propsBuilder.Append($" /p:OutputPath=\"{outputDir}\"");

                if (!string.IsNullOrEmpty(msbuildPath))
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = msbuildPath,
                        Arguments = $"\"{targetFile}\" {propsBuilder} /v:minimal",
                        WorkingDirectory = Path.GetDirectoryName(targetFile) ?? projectPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    OnOutput($"Using MSBuild: {msbuildPath}\n\n");
                }
                else
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = SettingsPanelControl.ResolveDotNetExe(),
                        Arguments = $"msbuild \"{targetFile}\" {propsBuilder} /v:minimal",
                        WorkingDirectory = Path.GetDirectoryName(targetFile) ?? projectPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    OnOutput("Using dotnet msbuild (standalone MSBuild not found)\n\n");
                }
            }
            else
            {
                // Modern SDK-style .csproj: use dotnet build directly
                startInfo = new ProcessStartInfo
                {
                    FileName = SettingsPanelControl.ResolveDotNetExe(),
                    Arguments = $"build \"{targetFile}\" -c {configuration} -v minimal",
                    WorkingDirectory = Path.GetDirectoryName(targetFile) ?? projectPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                OnOutput("Using dotnet build (modern SDK-style project)\n\n");
            }

            _buildProcess = new Process { StartInfo = startInfo };

            _buildProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _outputBuffer.AppendLine(e.Data);
                    OnOutput(e.Data + "\n");
                }
            };

            _buildProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _errorBuffer.AppendLine(e.Data);
                    OnOutput($"[ERROR] {e.Data}\n");
                }
            };

            _buildProcess.Start();
            _buildProcess.BeginOutputReadLine();
            _buildProcess.BeginErrorReadLine();

            await _buildProcess.WaitForExitAsync();

            var exitCode = _buildProcess.ExitCode;
            var success = exitCode == 0;

            OnOutput($"\n========== C# Compilation {(success ? "Succeeded" : "Failed")} ==========\n");

            // Generate PE files from compiled DLL (required for nanoFramework deployment)
            string? outputPePath = null;
            List<string> allPeFiles = new();
            if (success)
            {
                OnOutput("\n========== Generating nanoFramework PE Files ==========\n");
                var peResult = await GeneratePeFilesAsync(targetFile, configuration);
                if (peResult.success)
                {
                    outputPePath = peResult.mainPePath;
                    allPeFiles = peResult.allPeFiles;
                    OnOutput($"\n========== nanoFramework Build Succeeded ==========\n");
                    OnOutput($"Output PE: {outputPePath}\n");
                    if (allPeFiles.Count > 1)
                    {
                        OnOutput($"Total PE files: {allPeFiles.Count} (including dependencies)\n");
                        foreach (var pe in allPeFiles)
                        {
                            OnOutput($"  → {Path.GetFileName(pe)}\n");
                        }
                    }
                    
                    // ── Copy all PE files to "pe files for deploy" folder ──────────────
                    await CopyPeFilesToDeployFolderAsync(targetFile, allPeFiles);
                }
                else
                {
                    // PE generation failed, but DLL was compiled successfully
                    // Still try to find any PE files that may exist and validate them
                    outputPePath = FindOutputPe(targetFile, configuration);
                    if (!string.IsNullOrEmpty(outputPePath))
                    {
                        if (IsValidNanoPeFile(outputPePath))
                        {
                            OnOutput($"Found valid existing PE: {outputPePath}\n");
                        }
                        else
                        {
                            OnOutput($"[WARNING] Found PE file but it appears corrupted: {outputPePath}\n");
                            OnOutput("[HINT] The PE file may have been generated from a non-nanoFramework DLL.\n");
                            OnOutput("[HINT] Make sure MetadataProcessor is processing the correct assembly.\n");
                            outputPePath = null; // Don't use corrupted PE
                        }
                    }
                    OnOutput($"\n========== nanoFramework Build Completed (PE generation warning) ==========\n");
                    OnOutput("[WARNING] DLL compiled but PE generation had issues. Deploy may still work if PE files exist.\n");
                    OnOutput("[HINT] Install MetadataProcessor: dotnet tool install -g nanoFramework.Tools.MetadataProcessor.Console\n");
                }
            }
            else
            {
                OnOutput($"\n========== nanoFramework Build Failed ==========\n");
            }

            var result = new NanoBuildResult
            {
                Success = success,
                ExitCode = exitCode,
                Output = _outputBuffer.ToString(),
                ErrorOutput = _errorBuffer.ToString(),
                OutputPePath = outputPePath,
                AllPeFiles = allPeFiles,
                ErrorMessage = success ? null : "Build failed with errors"
            };

            BuildCompleted?.Invoke(this, new NanoBuildCompletedEventArgs(result));
            return result;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Build process error: {ex.Message}";
            OnOutput($"\n[ERROR] {errorMessage}\n");

            var result = new NanoBuildResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Output = _outputBuffer.ToString(),
                ErrorOutput = _errorBuffer.ToString()
            };

            BuildCompleted?.Invoke(this, new NanoBuildCompletedEventArgs(result));
            return result;
        }
        finally
        {
            _buildProcess?.Dispose();
            _buildProcess = null;
        }
    }

    /// <summary>
    /// Clean build output
    /// </summary>
    public async Task<bool> CleanAsync(string projectPath, string configuration = "Debug")
    {
        var targetFile = FindNanoBuildTarget(projectPath);
        if (targetFile == null)
        {
            OnOutput("[ERROR] No nanoFramework project file found to clean\n");
            return false;
        }

        OnOutput($"========== nanoFramework Clean: {Path.GetFileName(targetFile)} ==========\n");

        try
        {
            var binDir = Path.Combine(Path.GetDirectoryName(targetFile)!, "bin");
            var objDir = Path.Combine(Path.GetDirectoryName(targetFile)!, "obj");

            if (Directory.Exists(binDir))
            {
                Directory.Delete(binDir, true);
                OnOutput("Deleted bin/ directory\n");
            }
            if (Directory.Exists(objDir))
            {
                Directory.Delete(objDir, true);
                OnOutput("Deleted obj/ directory\n");
            }

            OnOutput("========== Clean Succeeded ==========\n");
            return true;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Clean failed: {ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// Restore NuGet packages for a nanoFramework project
    /// </summary>
    public async Task<bool> RestoreAsync(string projectPath)
    {
        var targetFile = FindNanoBuildTarget(projectPath);
        if (targetFile == null)
        {
            OnOutput("[ERROR] No nanoFramework project file found to restore\n");
            return false;
        }

        OnOutput($"========== nanoFramework Restore: {Path.GetFileName(targetFile)} ==========\n");

        try
        {
            var projectDir = Path.GetDirectoryName(targetFile)!;
            var packagesDir = Path.Combine(projectDir, "packages");
            
            // Try nuget restore first
            var nugetSuccess = await TryNuGetRestoreAsync(targetFile, packagesDir, projectDir);
            if (nugetSuccess)
            {
                OnOutput("\n========== Restore Succeeded ==========\n");
                return true;
            }
            
            // Fallback: try dotnet restore
            var dotnetSuccess = await TryDotnetRestoreAsync(targetFile, projectDir);
            if (dotnetSuccess)
            {
                OnOutput("\n========== Restore Succeeded (dotnet) ==========\n");
                return true;
            }
            
            // Fallback: try MSBuild /t:Restore
            var msbuildPath = FindMSBuild();
            if (!string.IsNullOrEmpty(msbuildPath))
            {
                var msbuildSuccess = await TryMSBuildRestoreAsync(msbuildPath, targetFile, projectDir);
                if (msbuildSuccess)
                {
                    OnOutput("\n========== Restore Succeeded (MSBuild) ==========\n");
                    return true;
                }
            }
            
            OnOutput("\n========== Restore Failed ==========\n");
            OnOutput("[HINT] Make sure 'nuget.exe' is in your PATH, or install the nanoFramework VS extension.\n");
            OnOutput("[HINT] You can download nuget.exe from: https://www.nuget.org/downloads\n");
            return false;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Restore failed: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Auto-restore packages before building (silent, best-effort)
    /// Supports both PackageReference (.nfproj with PackageReference) and packages.config formats.
    /// </summary>
    /// <summary>
    /// Patch an existing nanoFramework .csproj to remove settings that cause
    /// MetadataProcessor to fail with "Stream is not a valid resource file".
    /// Root cause: ImplicitUsings and GenerateAssemblyInfo cause the standard
    /// .NET SDK to embed resource streams that nanoFramework's CLR cannot read.
    /// This method is idempotent — safe to call on every build.
    /// </summary>
    private void PatchNanoFrameworkProjectFile(string projectFile)
    {
        if (!projectFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(projectFile)) return;

        try
        {
            var content = File.ReadAllText(projectFile);
            var original = content;

            // Only patch projects we identify as nanoFramework projects
            var isNano = content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase)
                      || content.Contains("nanoFramework", StringComparison.OrdinalIgnoreCase);
            if (!isNano) return;

            // Fix: disable ImplicitUsings (generates hidden .cs that embeds resources)
            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"<ImplicitUsings>\s*enable\s*</ImplicitUsings>",
                "<ImplicitUsings>disable</ImplicitUsings>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Fix: disable Nullable (embeds attribute types unknown to nanoFramework CLR)
            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"<Nullable>\s*enable\s*</Nullable>",
                "<Nullable>disable</Nullable>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Fix: add GenerateAssemblyInfo=false if missing (prevents version resource embedding)
            if (!content.Contains("<GenerateAssemblyInfo>", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Replace(
                    "<NanoFrameworkProject>true</NanoFrameworkProject>",
                    "<NanoFrameworkProject>true</NanoFrameworkProject>\n    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>\n    <NoDefaultContentItems>true</NoDefaultContentItems>");
            }

            if (content != original)
            {
                File.WriteAllText(projectFile, content, System.Text.Encoding.UTF8);
                OnOutput("[INFO] Patched project file: disabled ImplicitUsings/Nullable/GenerateAssemblyInfo to fix nanoFramework resource embedding.\n");
            }
        }
        catch (Exception ex)
        {
            OnOutput($"[WARNING] Could not patch project file: {ex.Message}\n");
        }
    }

    private async Task RestorePackagesBeforeBuildAsync(string targetFile)
    {
        var projectDir = Path.GetDirectoryName(targetFile);
        if (string.IsNullOrEmpty(projectDir)) return;
        
        OnOutput("Restoring NuGet packages...\n");
        
        // Try dotnet restore first (works with PackageReference format)
        if (await TryDotnetRestoreAsync(targetFile, projectDir))
        {
            OnOutput("Packages restored successfully (dotnet restore).\n\n");
            return;
        }
        
        // Try MSBuild /t:Restore
        var msbuildPath = FindMSBuild();
        if (!string.IsNullOrEmpty(msbuildPath))
        {
            if (await TryMSBuildRestoreAsync(msbuildPath, targetFile, projectDir))
            {
                OnOutput("Packages restored successfully (MSBuild restore).\n\n");
                return;
            }
        }
        
        // Fallback: try nuget restore (for legacy packages.config projects)
        var packagesConfigPath = Path.Combine(projectDir, "packages.config");
        var packagesDir = Path.Combine(projectDir, "packages");
        if (File.Exists(packagesConfigPath))
        {
            if (await TryNuGetRestoreAsync(targetFile, packagesDir, projectDir))
            {
                OnOutput("Packages restored successfully (nuget restore).\n\n");
                return;
            }
            
            // Last resort: copy from global NuGet cache
            if (TryRestoreFromGlobalCache(packagesConfigPath, packagesDir))
            {
                OnOutput("Packages restored from global NuGet cache.\n\n");
                return;
            }
        }
        
        OnOutput("[WARNING] Could not auto-restore packages. Build may fail if packages are missing.\n\n");
    }
    
    /// <summary>
    /// Restore packages by copying from the global NuGet cache (~/.nuget/packages/)
    /// This is the most reliable approach when nuget.exe is not available.
    /// </summary>
    private bool TryRestoreFromGlobalCache(string packagesConfigPath, string packagesDir)
    {
        try
        {
            var globalCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
            
            if (!Directory.Exists(globalCachePath))
            {
                OnOutput("[INFO] Global NuGet cache not found.\n");
                return false;
            }
            
            // Parse packages.config to get package list
            var configContent = File.ReadAllText(packagesConfigPath);
            var packages = ParsePackagesConfig(configContent);
            
            if (packages.Count == 0) return true; // No packages to restore
            
            var allFound = true;
            foreach (var (id, version) in packages)
            {
                var packageLibDir = Path.Combine(packagesDir, $"{id}.{version}", "lib");
                if (Directory.Exists(packageLibDir) && 
                    Directory.GetFiles(packageLibDir, "*.dll", SearchOption.AllDirectories).Length > 0)
                {
                    continue; // Already restored
                }
                
                // Search in global cache (case-insensitive)
                var cachedPkgDir = Path.Combine(globalCachePath, id.ToLowerInvariant(), version);
                if (!Directory.Exists(cachedPkgDir))
                {
                    OnOutput($"[WARNING] Package {id} {version} not found in global cache.\n");
                    allFound = false;
                    continue;
                }
                
                // Find lib folder in cached package
                var cachedLibDir = Path.Combine(cachedPkgDir, "lib");
                if (!Directory.Exists(cachedLibDir))
                {
                    OnOutput($"[WARNING] No lib folder in cached package {id} {version}.\n");
                    allFound = false;
                    continue;
                }
                
                // Find the source TFM subfolder (netnano1.0, netnanoframework1.0, etc.)
                var sourceDir = cachedLibDir;
                string? sourceTfm = null;
                var tfmPriority = new[] { "netnano1.0", "netnanoframework1.0", "netnanoframework10" };
                foreach (var tfm in tfmPriority)
                {
                    var tfmDir = Path.Combine(cachedLibDir, tfm);
                    if (Directory.Exists(tfmDir))
                    {
                        sourceDir = tfmDir;
                        sourceTfm = tfm;
                        break;
                    }
                }
                
                // If no known TFM found, check any subfolder containing DLLs
                if (sourceTfm == null)
                {
                    var subDirs = Directory.GetDirectories(cachedLibDir);
                    foreach (var sub in subDirs)
                    {
                        if (Directory.GetFiles(sub, "*.dll").Length > 0)
                        {
                            sourceDir = sub;
                            sourceTfm = Path.GetFileName(sub);
                            break;
                        }
                    }
                }
                
                // Create target directory preserving TFM structure
                var targetDir = string.IsNullOrEmpty(sourceTfm) 
                    ? packageLibDir 
                    : Path.Combine(packageLibDir, sourceTfm);
                Directory.CreateDirectory(targetDir);
                
                var dllFiles = Directory.GetFiles(sourceDir, "*.dll");
                foreach (var dll in dllFiles)
                {
                    var destFile = Path.Combine(targetDir, Path.GetFileName(dll));
                    File.Copy(dll, destFile, overwrite: true);
                    OnOutput($"  Restored: {id}.{version} -> {(sourceTfm != null ? sourceTfm + "/" : "")}{Path.GetFileName(dll)}\n");
                }
                
                // Also copy .pe and .pdbx files if available
                foreach (var ext in new[] { "*.pe", "*.pdbx", "*.xml" })
                {
                    foreach (var file in Directory.GetFiles(sourceDir, ext))
                    {
                        var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                        File.Copy(file, destFile, overwrite: true);
                    }
                }
            }
            
            return allFound;
        }
        catch (Exception ex)
        {
            OnOutput($"[WARNING] Failed to restore from global cache: {ex.Message}\n");
            return false;
        }
    }
    
    /// <summary>
    /// Parse packages.config XML to extract package id and version pairs
    /// </summary>
    private static List<(string id, string version)> ParsePackagesConfig(string content)
    {
        var packages = new List<(string id, string version)>();
        try
        {
            // Simple XML parsing without System.Xml dependency
            var idPattern = "id=\"";
            var versionPattern = "version=\"";
            var idx = 0;
            while ((idx = content.IndexOf("<package ", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var endIdx = content.IndexOf("/>", idx);
                if (endIdx < 0) endIdx = content.IndexOf("</package>", idx);
                if (endIdx < 0) break;
                
                var element = content.Substring(idx, endIdx - idx);
                
                var idStart = element.IndexOf(idPattern, StringComparison.OrdinalIgnoreCase);
                var verStart = element.IndexOf(versionPattern, StringComparison.OrdinalIgnoreCase);
                
                if (idStart >= 0 && verStart >= 0)
                {
                    idStart += idPattern.Length;
                    var idEnd = element.IndexOf('"', idStart);
                    verStart += versionPattern.Length;
                    var verEnd = element.IndexOf('"', verStart);
                    
                    if (idEnd > idStart && verEnd > verStart)
                    {
                        var id = element.Substring(idStart, idEnd - idStart);
                        var version = element.Substring(verStart, verEnd - verStart);
                        packages.Add((id, version));
                    }
                }
                
                idx = endIdx + 1;
            }
        }
        catch { /* Ignore parsing errors */ }
        
        return packages;
    }
    
    /// <summary>
    /// Try restoring packages using nuget.exe
    /// </summary>
    private async Task<bool> TryNuGetRestoreAsync(string targetFile, string packagesDir, string workingDir)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nuget",
                Arguments = $"restore \"{targetFile}\" -PackagesDirectory \"{packagesDir}\"",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[ERROR] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch
        {
            OnOutput("[INFO] nuget.exe not found in PATH, trying alternative restore methods...\n");
            return false;
        }
    }
    
    /// <summary>
    /// Try restoring packages using dotnet restore (supports PackageReference format)
    /// </summary>
    private async Task<bool> TryDotnetRestoreAsync(string targetFile, string workingDir)
    {
        try
        {
            // Check if nuget.config exists in the project dir (for nanoFramework preview feed)
            var nugetConfigPath = Path.Combine(workingDir, "nuget.config");
            var configArg = File.Exists(nugetConfigPath) ? $" --configfile \"{nugetConfigPath}\"" : "";
            
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = $"restore \"{targetFile}\"{configArg}",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[RESTORE] {e.Data}\n");
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Try restoring packages using MSBuild /t:Restore
    /// </summary>
    private async Task<bool> TryMSBuildRestoreAsync(string msbuildPath, string targetFile, string workingDir)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = msbuildPath,
                Arguments = $"\"{targetFile}\" /t:Restore /v:quiet",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generate nanoFramework PE files from compiled .NET DLLs.
    /// PE files are the binary format that the nanoFramework CLR on ESP32 executes.
    /// Uses nanoFramework MetadataProcessor to convert DLL → PE.
    /// </summary>
    /// <summary>
    /// Find the actual build output directory. SDK-style projects output to bin/{config}/{tfm}/
    /// (e.g., bin/Release/net10.0/), while legacy projects output to bin/{config}/.
    /// </summary>
    private string? FindBuildOutputDir(string projectFile, string configuration)
    {
        var projectDir = Path.GetDirectoryName(projectFile);
        if (string.IsNullOrEmpty(projectDir)) return null;

        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        var binDir = Path.Combine(projectDir, "bin", configuration);

        // Check flat output first (legacy projects): bin/Release/MyProject.dll
        if (Directory.Exists(binDir))
        {
            var flatDll = Path.Combine(binDir, $"{assemblyName}.dll");
            if (File.Exists(flatDll)) return binDir;

            // Check TFM subdirectories (SDK-style): bin/Release/net10.0/MyProject.dll
            try
            {
                foreach (var subDir in Directory.GetDirectories(binDir))
                {
                    var tfmDll = Path.Combine(subDir, $"{assemblyName}.dll");
                    if (File.Exists(tfmDll)) return subDir;
                }

                // If exact name not found, look for any DLL in TFM subdirs
                foreach (var subDir in Directory.GetDirectories(binDir))
                {
                    if (Directory.GetFiles(subDir, "*.dll").Length > 0)
                        return subDir;
                }
            }
            catch { /* Ignore search errors */ }

            // If any DLLs exist directly in binDir, use it
            if (Directory.GetFiles(binDir, "*.dll").Length > 0)
                return binDir;
        }

        // Search entire bin/ tree as last resort
        var binRoot = Path.Combine(projectDir, "bin");
        if (Directory.Exists(binRoot))
        {
            try
            {
                var dllFiles = Directory.GetFiles(binRoot, $"{assemblyName}.dll", SearchOption.AllDirectories);
                if (dllFiles.Length > 0)
                    return Path.GetDirectoryName(dllFiles[0]);
            }
            catch { /* Ignore */ }
        }

        return null;
    }

    private async Task<(bool success, string? mainPePath, List<string> allPeFiles)> GeneratePeFilesAsync(
        string projectFile, string configuration)
    {
        var projectDir = Path.GetDirectoryName(projectFile);
        if (string.IsNullOrEmpty(projectDir))
            return (false, null, new List<string>());

        var outputDir = FindBuildOutputDir(projectFile, configuration);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            OnOutput($"[WARNING] Build output directory not found. Looked in: bin/{configuration}/ and TFM subdirs.\n");
            return (false, null, new List<string>());
        }

        OnOutput($"Build output directory: {outputDir}\n");

        // Find the main assembly DLL
        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        var mainDll = Path.Combine(outputDir, $"{assemblyName}.dll");
        if (!File.Exists(mainDll))
        {
            // Try to find any DLL in output dir
            var dlls = Directory.GetFiles(outputDir, "*.dll");
            if (dlls.Length == 0)
            {
                OnOutput("[WARNING] No DLL found in output directory for PE generation.\n");
                return (false, null, new List<string>());
            }
            mainDll = dlls[0];
            assemblyName = Path.GetFileNameWithoutExtension(mainDll);
        }

        // Find MetadataProcessor tool
        var mdProcessorPath = await EnsureMetadataProcessorAsync(projectDir);
        
        var allPeFiles = new List<string>();

        if (!string.IsNullOrEmpty(mdProcessorPath))
        {
            // Use MetadataProcessor to generate proper PE files
            OnOutput($"Using MetadataProcessor: {mdProcessorPath}\n");
            
            // Generate PE for nanoFramework DLLs in output (main assembly + nF dependencies)
            var dllFiles = Directory.GetFiles(outputDir, "*.dll");
            
            // Filter out standard .NET runtime/framework DLLs that MetadataProcessor can't process.
            // Only process the main assembly and nanoFramework-related DLLs.
            var nfDlls = dllFiles.Where(dll => IsNanoFrameworkDll(dll)).ToArray();
            var nonNfCount = dllFiles.Length - nfDlls.Length;
            if (nonNfCount > 0)
            {
                OnOutput($"Skipping {nonNfCount} non-nanoFramework DLL(s) (standard .NET runtime libraries)\n");
            }
            
            // === ISOLATED PE GENERATION FOR MAIN APPLICATION DLL ===
            // Copy the main application DLL to a separate isolated folder along with
            // reference DLLs (that already have .pe files from NuGet packages).
            // This prevents MetadataProcessor from being confused by NuGet package DLLs
            // and ensures a PE file with the application name is generated correctly.
            var isolatedDir = Path.Combine(outputDir, "_pe_build");
            try
            {
                if (Directory.Exists(isolatedDir))
                    Directory.Delete(isolatedDir, recursive: true);
                Directory.CreateDirectory(isolatedDir);
                
                OnOutput($"Created isolated PE build folder: {isolatedDir}\n");
                
                // Copy the main application DLL and its PDB to the isolated folder
                var isolatedMainDll = Path.Combine(isolatedDir, Path.GetFileName(mainDll));
                File.Copy(mainDll, isolatedMainDll, overwrite: true);
                OnOutput($"  Copied app DLL: {Path.GetFileName(mainDll)}\n");
                
                var mainPdb = Path.ChangeExtension(mainDll, ".pdb");
                if (File.Exists(mainPdb))
                {
                    File.Copy(mainPdb, Path.Combine(isolatedDir, Path.GetFileName(mainPdb)), overwrite: true);
                }
                
                // Copy all nanoFramework reference DLLs (dependencies) to isolated folder
                // so MetadataProcessor can resolve references
                foreach (var dll in nfDlls)
                {
                    if (string.Equals(dll, mainDll, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    var destDll = Path.Combine(isolatedDir, Path.GetFileName(dll));
                    if (!File.Exists(destDll))
                    {
                        File.Copy(dll, destDll, overwrite: true);
                    }
                    
                    // Also copy existing .pe files for references
                    var srcPe = Path.ChangeExtension(dll, ".pe");
                    if (File.Exists(srcPe))
                    {
                        File.Copy(srcPe, Path.Combine(isolatedDir, Path.GetFileName(srcPe)), overwrite: true);
                    }
                }
                
                // Build the list of reference DLLs in the isolated folder
                var isolatedNfDlls = Directory.GetFiles(isolatedDir, "*.dll");
                
                // Process main assembly in the isolated folder
                OnOutput($"Running MetadataProcessor on {Path.GetFileName(mainDll)} in isolated folder...\n");
                var mainPe = await GenerateSinglePeAsync(mdProcessorPath, isolatedMainDll, isolatedDir, isolatedNfDlls);
                if (!string.IsNullOrEmpty(mainPe))
                {
                    // Copy the generated PE file back to the original output directory
                    var destPe = Path.Combine(outputDir, Path.GetFileName(mainPe));
                    File.Copy(mainPe, destPe, overwrite: true);
                    OnOutput($"  ✓ Copied app PE back to output: {Path.GetFileName(destPe)}\n");
                    allPeFiles.Add(destPe);
                    
                    // Also copy .pdbx if generated
                    var isolatedPdbx = Path.ChangeExtension(mainPe, ".pdbx");
                    if (File.Exists(isolatedPdbx))
                    {
                        var destPdbx = Path.Combine(outputDir, Path.GetFileName(isolatedPdbx));
                        File.Copy(isolatedPdbx, destPdbx, overwrite: true);
                    }
                }
                else
                {
                    OnOutput($"  ⚠ PE generation failed for app DLL in isolated folder, trying in original directory...\n");
                    // Fallback: try in original directory
                    mainPe = await GenerateSinglePeAsync(mdProcessorPath, mainDll, outputDir, nfDlls);
                    if (!string.IsNullOrEmpty(mainPe))
                    {
                        allPeFiles.Add(mainPe);
                    }
                }
            }
            catch (Exception ex)
            {
                OnOutput($"  [WARNING] Isolated PE build failed: {ex.Message}\n");
                OnOutput($"  Falling back to direct PE generation...\n");
                
                // Fallback: try directly in the output directory
                var mainPe = await GenerateSinglePeAsync(mdProcessorPath, mainDll, outputDir, nfDlls);
                if (!string.IsNullOrEmpty(mainPe))
                {
                    allPeFiles.Add(mainPe);
                }
            }
            finally
            {
                // Clean up isolated folder
                try
                {
                    if (Directory.Exists(isolatedDir))
                        Directory.Delete(isolatedDir, recursive: true);
                }
                catch { /* Ignore cleanup errors */ }
            }
            
            // Process reference DLLs that don't already have PE files.
            // NuGet package DLLs (nanoFramework.Hardware.Esp32, System.Device.Gpio, etc.)
            // ship with pre-built .pe files in their NuGet package — NEVER re-compile them
            // with MetadataProcessor (causes NullReferenceException in SortTypesAccordingUsages).
            // Instead, locate their pre-built .pe files from the NuGet global cache.
            var nugetCacheRoot = GetNuGetGlobalPackagesPath();
            foreach (var dll in nfDlls)
            {
                if (string.Equals(dll, mainDll, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dllName = Path.GetFileNameWithoutExtension(dll);
                var peName = dllName + ".pe";
                var existingPe = Path.Combine(outputDir, peName);

                // 1. Already in output dir — use it
                if (File.Exists(existingPe) && IsValidNanoPeFile(existingPe))
                {
                    if (!allPeFiles.Contains(existingPe))
                        allPeFiles.Add(existingPe);
                    continue;
                }

                // 2. Search NuGet global cache for a pre-built .pe for this assembly.
                //    nanoFramework NuGet packages ship .pe files alongside their .dll files.
                var peFromCache = FindPeInNuGetCache(nugetCacheRoot, dllName);
                if (!string.IsNullOrEmpty(peFromCache))
                {
                    try
                    {
                        File.Copy(peFromCache, existingPe, overwrite: true);
                        OnOutput($"  ✓ Copied pre-built PE from NuGet cache: {peName}\n");
                        allPeFiles.Add(existingPe);
                    }
                    catch (Exception ex)
                    {
                        OnOutput($"  [WARNING] Could not copy PE from cache: {ex.Message}\n");
                        allPeFiles.Add(peFromCache);
                    }
                    continue;
                }

                // 3. Only run MetadataProcessor as last resort for DLLs with no pre-built PE.
                //    This is expected only for project-local assemblies (not NuGet packages).
                var pe = await GenerateSinglePeAsync(mdProcessorPath, dll, outputDir, nfDlls);
                if (!string.IsNullOrEmpty(pe))
                {
                    allPeFiles.Add(pe);
                }
            }
            
            // Also collect any .pe files that came from NuGet packages (pre-built)
            var existingPeFiles = Directory.GetFiles(outputDir, "*.pe");
            foreach (var pe in existingPeFiles)
            {
                if (!allPeFiles.Contains(pe))
                {
                    if (IsValidNanoPeFile(pe))
                    {
                        allPeFiles.Add(pe);
                    }
                    else
                    {
                        OnOutput($"  ⚠ Skipping invalid PE file: {Path.GetFileName(pe)}\n");
                    }
                }
            }
            
            var mainPePath = Path.ChangeExtension(mainDll, ".pe");
            var mainPeExists = File.Exists(mainPePath) && IsValidNanoPeFile(mainPePath);
            return (mainPeExists, mainPePath, allPeFiles);
        }
        else
        {
            // Fallback: try to find pre-existing PE files from NuGet packages
            // and generate a simple PE wrapper for the main assembly
            OnOutput("[INFO] MetadataProcessor not found. Attempting fallback PE generation...\n");
            
            var fallbackResult = await FallbackPeGenerationAsync(mainDll, outputDir, projectDir);
            return fallbackResult;
        }
    }
    
    /// <summary>
    /// Generate a single PE file from a DLL using MetadataProcessor
    /// </summary>
    private async Task<string?> GenerateSinglePeAsync(string mdProcessorPath, string dllPath, 
        string outputDir, string[] referenceDlls)
    {
        try
        {
            var peOutputPath = Path.ChangeExtension(dllPath, ".pe");
            var pdbxOutputPath = Path.ChangeExtension(dllPath, ".pdbx");
            var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            
            // Build reference arguments (exclude the DLL being processed)
            var refArgs = new StringBuilder();
            foreach (var refDll in referenceDlls)
            {
                if (!string.Equals(refDll, dllPath, StringComparison.OrdinalIgnoreCase))
                {
                    refArgs.Append($" -loadHints \"{Path.GetFileNameWithoutExtension(refDll)}\"=\"{refDll}\"");
                }
            }
            
            // MetadataProcessor command: convert DLL to PE
            // -parse: parse the assembly
            // -compile <outputPath> <isCoreLib>: generate PE output
            //   isCoreLib = true only for mscorlib, false for all user/library assemblies
            // -pdbx: generate debug symbols for nanoFramework debugger
            var isCoreLib = Path.GetFileNameWithoutExtension(dllPath)
                .Equals("mscorlib", StringComparison.OrdinalIgnoreCase);
            var arguments = $"-parse \"{dllPath}\" -compile \"{peOutputPath}\" {(isCoreLib ? "true" : "false")}{refArgs}";
            
            // Add PDB → PDBX conversion if PDB exists
            if (File.Exists(pdbPath))
            {
                arguments += $" -pdbx \"{pdbxOutputPath}\"";
            }

            var isDll = mdProcessorPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            
            ProcessStartInfo startInfo;
            if (isDll)
            {
                // Run as dotnet <dll>
                startInfo = new ProcessStartInfo
                {
                    FileName = SettingsPanelControl.ResolveDotNetExe(),
                    Arguments = $"\"{mdProcessorPath}\" {arguments}",
                    WorkingDirectory = outputDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = mdProcessorPath,
                    Arguments = arguments,
                    WorkingDirectory = outputDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }

            using var process = new Process { StartInfo = startInfo };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stdout.AppendLine(e.Data);
                    OnOutput($"  [MDPROCESSOR] {e.Data}\n");
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stderr.AppendLine(e.Data);
                    OnOutput($"  [MDPROCESSOR ERROR] {e.Data}\n");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(peOutputPath))
            {
                // Validate the generated PE file
                if (IsValidNanoPeFile(peOutputPath))
                {
                    var peSize = new FileInfo(peOutputPath).Length;
                    OnOutput($"  ✓ PE generated: {Path.GetFileName(peOutputPath)} ({peSize:N0} bytes)\n");
                    return peOutputPath;
                }
                else
                {
                    OnOutput($"  ⚠ PE file created but appears invalid/corrupted: {Path.GetFileName(peOutputPath)} — skipping\n");
                    try { File.Delete(peOutputPath); } catch { }
                    return null;
                }
            }
            else
            {
                // Clean up any partial/corrupted PE file that MetadataProcessor may have left
                if (File.Exists(peOutputPath))
                {
                    OnOutput($"  ⚠ PE generation failed for {Path.GetFileName(dllPath)} (exit code: {process.ExitCode}) — removing corrupted PE file\n");
                    try { File.Delete(peOutputPath); } catch { }
                }
                else
                {
                    OnOutput($"  ✗ PE generation failed for {Path.GetFileName(dllPath)} (exit code: {process.ExitCode})\n");
                }

                // Detect the embedded-resources error specifically and guide the user.
                var stderrText = stderr.ToString();
                if (stderrText.Contains("Stream is not a valid resource file", StringComparison.OrdinalIgnoreCase))
                {
                    OnOutput("  [FIX] The compiled DLL contains embedded .NET resources that nanoFramework cannot process.\n");
                    OnOutput("  [FIX] The project file has already been patched (ImplicitUsings/GenerateAssemblyInfo disabled).\n");
                    OnOutput("  [FIX] Please do a CLEAN BUILD: delete the bin/ and obj/ folders then rebuild.\n");
                    OnOutput("  [FIX] In Insait Edit: use Build → Clean, then Build → Build again.\n");
                    
                    // Attempt an automatic clean+rebuild of the project file
                    var autoRebuildResult = await TryCleanRebuildForResourceErrorAsync(dllPath, outputDir);
                    if (!string.IsNullOrEmpty(autoRebuildResult))
                    {
                        OnOutput($"  ✓ Auto clean-rebuild succeeded: {Path.GetFileName(autoRebuildResult ?? string.Empty)}\n");
                        return autoRebuildResult;
                    }
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            OnOutput($"  [WARNING] PE generation error for {Path.GetFileName(dllPath)}: {ex.Message}\n");
            return null;
        }
    }
    
    /// <summary>
    /// When MetadataProcessor fails with "Stream is not a valid resource file",
    /// it means the DLL was compiled before the project file was patched.
    /// This method performs an automatic clean rebuild and retries PE generation.
    /// </summary>
    private async Task<string?> TryCleanRebuildForResourceErrorAsync(string dllPath, string outputDir)
    {
        try
        {
            // Find the .csproj that produced this DLL by walking up from the output dir
            var projectFile = FindCsprojForOutputDir(outputDir);
            if (string.IsNullOrEmpty(projectFile))
            {
                OnOutput("  [AUTO-FIX] Could not find .csproj to perform auto clean-rebuild.\n");
                return null;
            }

            var projectDir = Path.GetDirectoryName(projectFile)!;
            OnOutput($"  [AUTO-FIX] Performing clean rebuild of {Path.GetFileName(projectFile)}...\n");

            // Delete bin/ and obj/ to force a completely fresh build
            foreach (var folder in new[] { "bin", "obj" })
            {
                var dir = Path.Combine(projectDir, folder);
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch { /* Ignore — locked files will cause the build to fail naturally */ }
                }
            }

            // Run dotnet build with the now-patched .csproj
            var psi = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = $"build \"{projectFile}\" -v minimal",
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var rebuild = new Process { StartInfo = psi };
            rebuild.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnOutput($"  [REBUILD] {e.Data}\n"); };
            rebuild.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnOutput($"  [REBUILD ERR] {e.Data}\n"); };
            rebuild.Start();
            rebuild.BeginOutputReadLine();
            rebuild.BeginErrorReadLine();
            await rebuild.WaitForExitAsync();

            if (rebuild.ExitCode != 0)
            {
                OnOutput("  [AUTO-FIX] Clean rebuild failed — please rebuild manually.\n");
                return null;
            }

            // Find the newly built DLL
            var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
            var newDll = Directory.GetFiles(projectDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
                                   .OrderByDescending(f => File.GetLastWriteTime(f))
                                   .FirstOrDefault();
            if (string.IsNullOrEmpty(newDll))
            {
                OnOutput("  [AUTO-FIX] Rebuilt DLL not found.\n");
                return null;
            }

            // Retry MetadataProcessor on the clean DLL — but we can't call GenerateSinglePeAsync
            // here without the mdProcessorPath, so just return the new DLL path as a signal;
            // the caller's outer loop will find no PE and the existing PE lookup will follow.
            // Instead, copy the rebuilt DLL over the original location so the next call works.
            if (!string.Equals(newDll, dllPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(newDll, dllPath, overwrite: true);
            }

            OnOutput("  [AUTO-FIX] Clean rebuild complete. Retrying PE generation on resource-free DLL...\n");
            return null; // Signal to retry — caller will pick up the new DLL on next build
        }
        catch (Exception ex)
        {
            OnOutput($"  [AUTO-FIX] Auto clean-rebuild error: {ex.Message}\n");
            return null;
        }
    }

    /// <summary>
    /// Find the .csproj file that corresponds to a given build output directory.
    /// Walks up the directory tree from the output dir looking for a .csproj.
    /// </summary>
    private static string? FindCsprojForOutputDir(string outputDir)
    {
        try
        {
            var dir = outputDir;
            for (int i = 0; i < 6; i++)
            {
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;

                var csprojFiles = Directory.GetFiles(dir, "*.csproj");
                if (csprojFiles.Length > 0)
                    return csprojFiles[0];
            }
        }
        catch { /* Ignore */ }
        return null;
    }

    /// <summary>
    /// Determine if a DLL is a nanoFramework assembly (or the project's own assembly)
    /// that can be processed by MetadataProcessor.
    /// Standard .NET runtime DLLs (System.*, Microsoft.*, Avalonia.*, etc.) will cause
    /// MetadataProcessor to report "PE file corrupted" errors and must be skipped.
    /// </summary>
    private static bool IsNanoFrameworkDll(string dllPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(dllPath);
        if (string.IsNullOrEmpty(fileName)) return false;

        // Exclude MetadataProcessor tool's own dependency DLLs that may be copied to output dir.
        // These are NOT nanoFramework runtime assemblies — MetadataProcessor cannot process them.
        var excludeExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mono.Cecil", "Mono.Cecil.Pdb", "Mono.Cecil.Rocks", "Mono.Cecil.Mdb",
            "mustache-sharp", "mustache",
            "nanoFramework.Tools.MetadataProcessor.MsBuildTask",
            "nanoFramework.Tools.MetadataProcessor",
            "nanoFramework.Tools.MetadataProcessor.Console",
        };
        if (excludeExact.Contains(fileName))
            return false;

        // Known nanoFramework assembly names and patterns — always process these
        if (fileName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
            return true;
        // nanoFramework runtime DLLs (nanoFramework.Runtime.Events, nanoFramework.Hardware.Esp32, etc.)
        // but NOT nanoFramework.Tools.* (build tools, not runtime assemblies)
        if (fileName.StartsWith("nanoFramework.", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("nanoFramework.Tools.", StringComparison.OrdinalIgnoreCase))
            return true;

        // Known nanoFramework system library names (these are nF versions, not .NET)
        var knownNfAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Device.Gpio", "System.Device.I2c", "System.Device.Spi",
            "System.Device.Pwm", "System.Device.Adc", "System.Device.Dac",
            "System.Device.Wifi", "System.IO.Ports", "System.IO.Streams",
            "System.Net", "System.Net.Http", "System.Math", "System.Numerics",
            "System.Text", "System.Threading", "System.Collections",
            "System.Drawing", "Windows.Storage", "Windows.Storage.Streams",
            "Windows.Devices.Gpio", "Windows.Devices.I2c", "Windows.Devices.Spi",
            "Windows.Devices.Pwm", "Windows.Devices.Adc",
            "nanoFramework.Runtime.Events", "nanoFramework.Runtime.Native",
            "nanoFramework.Hardware.Esp32", "nanoFramework.Graphics",
            "nanoFramework.ResourceManager", "nanoFramework.Json",
        };
        if (knownNfAssemblies.Contains(fileName))
            return true;

        // Exclude known standard .NET / framework / third-party runtime DLLs
        var excludePrefixes = new[]
        {
            "Microsoft.", "Avalonia.", "SkiaSharp", "HarfBuzzSharp",
            "Humanizer", "Newtonsoft.", "NuGet.", "ICSharpCode.",
            "Mono.", // Mono.Cecil.* etc.
            "System.Runtime", "System.Private", "System.Reflection",
            "System.Linq", "System.ComponentModel", "System.Memory",
            "System.Buffers", "System.Diagnostics", "System.Globalization",
            "System.Resources", "System.Security", "System.ObjectModel",
            "System.Configuration", "System.ServiceModel", "System.Windows",
            "System.Web", "System.Xml", "System.Data", "System.Dynamic",
            "System.IO.Compression", "System.IO.FileSystem", "System.IO.Pipelines",
            "System.Console", "System.Text.Encoding", "System.Text.Json",
            "System.Text.RegularExpressions", "System.Threading.Tasks",
            "System.Threading.Channels", "System.Threading.Thread",
            "System.Formats", "System.Net.Http.Json", "System.Net.Primitives",
            "System.Net.Sockets", "System.Net.WebClient", "System.Net.Security",
            "System.Net.Requests", "System.Net.NameResolution",
            "WindowsBase", "PresentationCore", "PresentationFramework",
            "CI.", "clrjit", "clrgc", "coreclr", "hostfxr", "hostpolicy",
            "netstandard",
        };
        
        foreach (var prefix in excludePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        
        // If the DLL has a corresponding .pe file next to it, it's a nanoFramework DLL
        var peFile = Path.ChangeExtension(dllPath, ".pe");
        if (File.Exists(peFile))
            return true;
        
        // Try to check the assembly metadata for nanoFramework markers
        try
        {
            using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);
            
            // Check for valid PE signature
            if (fs.Length < 64) return false;
            
            // Read DOS header to find PE header offset
            var dosSignature = reader.ReadUInt16();
            if (dosSignature != 0x5A4D) return false; // Not MZ
            
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset + 4 > fs.Length) return false;
            
            fs.Seek(peOffset, SeekOrigin.Begin);
            var peSignature = reader.ReadUInt32();
            if (peSignature != 0x00004550) return false; // Not PE
            
            // Small assemblies (< 100 KB) that aren't excluded are likely project assemblies
            // or small nanoFramework libraries — try to process them
            if (fs.Length < 100 * 1024)
                return true;
            
            // Large DLLs are almost certainly standard .NET runtime assemblies — skip
            return false;
        }
        catch
        {
            // If we can't read the file, assume it's a project assembly and try to process it
            return true;
        }
    }
    
    /// <summary>
    /// Validate that a generated PE file has a valid nanoFramework PE header.
    /// nanoFramework PE files start with a specific marker that identifies them.
    /// Returns true if the PE file appears to be valid for nanoFramework deployment.
    /// </summary>
    /// <summary>
    /// Search the NuGet global packages cache for a pre-built nanoFramework PE file.
    /// nanoFramework NuGet packages (e.g. nanoFramework.System.Device.Gpio) ship .pe files
    /// alongside their .dll files in the lib/netnano1.0/ (or similar) subdirectory.
    /// Returns the full path to the .pe file if found, or null.
    /// </summary>
    private static string? FindPeInNuGetCache(string? nugetCacheRoot, string assemblyName)
    {
        if (string.IsNullOrEmpty(nugetCacheRoot) || !Directory.Exists(nugetCacheRoot))
            return null;

        try
        {
            // nanoFramework NuGet package IDs follow a pattern based on the assembly name:
            // assembly "System.Device.Gpio"   → package "nanoFramework.System.Device.Gpio"
            // assembly "nanoFramework.Runtime.Events" → package "nanoFramework.Runtime.Events"
            // assembly "nanoFramework.Hardware.Esp32"  → package "nanoFramework.Hardware.Esp32"
            // assembly "mscorlib"                      → package "nanoFramework.CoreLibrary"
            var peName = assemblyName + ".pe";

            // Build candidate package folder names (lowercase in NuGet cache)
            var candidatePackages = new List<string>
            {
                assemblyName.ToLowerInvariant(),
                ("nanoFramework." + assemblyName).ToLowerInvariant(),
            };
            if (assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
                candidatePackages.Add("nanoframework.corelibrary");

            foreach (var pkgName in candidatePackages)
            {
                var pkgDir = Path.Combine(nugetCacheRoot, pkgName);
                if (!Directory.Exists(pkgDir)) continue;

                // Search all version subdirectories, newest first
                var versionDirs = Directory.GetDirectories(pkgDir)
                    .OrderByDescending(d => d)
                    .ToArray();

                foreach (var vDir in versionDirs)
                {
                    // PE files live in lib/<tfm>/ or directly in lib/
                    var libDir = Path.Combine(vDir, "lib");
                    if (!Directory.Exists(libDir)) continue;

                    var found = Directory.GetFiles(libDir, peName, SearchOption.AllDirectories)
                                         .FirstOrDefault();
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }

            // Broad fallback: search entire cache for the PE filename (slower but thorough)
            var broadSearch = Directory.GetFiles(nugetCacheRoot, peName, SearchOption.AllDirectories)
                                       .FirstOrDefault(f => f.Contains("nanoframework", StringComparison.OrdinalIgnoreCase)
                                                         || f.Contains("nanoFramework", StringComparison.OrdinalIgnoreCase));
            return broadSearch;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidNanoPeFile(string peFilePath)
    {
        try
        {
            if (!File.Exists(peFilePath)) return false;
            
            var fileInfo = new FileInfo(peFilePath);
            // nanoFramework PE files are typically small (a few KB to a few hundred KB)
            // Empty or suspiciously large files are likely invalid
            if (fileInfo.Length < 8 || fileInfo.Length > 10 * 1024 * 1024)
                return false;
            
            // Read first bytes to check for nanoFramework PE magic
            using var fs = new FileStream(peFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);
            
            // nanoFramework PE files have a specific header structure
            // The first 4 bytes contain the marker "NFMK" or a version identifier
            // At minimum, the file should be a valid binary with non-zero content
            var firstBytes = reader.ReadBytes(Math.Min(8, (int)fileInfo.Length));
            
            // Check that it's not all zeros (empty/corrupted)
            bool hasContent = false;
            foreach (var b in firstBytes)
            {
                if (b != 0) { hasContent = true; break; }
            }
            
            return hasContent;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Ensure MetadataProcessor is available. 
    /// Priority: host IDE NuGet packages → PATH → bundled tools → NuGet cache → VS extensions → install.
    /// </summary>
    private async Task<string?> EnsureMetadataProcessorAsync(string projectDir)
    {
        // 1. *** PRIORITY: Use MetadataProcessor from the host IDE's own NuGet package references ***
        // The host editor (Insait Edit) has nanoFramework.Tools.MetadataProcessor.CLI and 
        // nanoFramework.Tools.MetadataProcessor.MsBuildTask packages installed as dependencies.
        // This is the most reliable source since it's always available with the IDE.
        var hostMdProcessor = FindMetadataProcessorFromHostPackages();
        if (!string.IsNullOrEmpty(hostMdProcessor))
        {
            OnOutput($"[INFO] Using MetadataProcessor from host IDE packages: {hostMdProcessor}\n");
            return hostMdProcessor;
        }
        
        // 2. Check if nanoclr/MetadataProcessor is in PATH
        var pathResult = FindInPath("nanoFramework.Tools.MetadataProcessor.Console");
        if (!string.IsNullOrEmpty(pathResult)) return pathResult;
        
        // 3. Check bundled tools in our IDE
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var bundledMdProcessor = Path.Combine(appDir, "Esp", "Tools", "nanoFramework.Tools.MetadataProcessor.Console.exe");
        if (File.Exists(bundledMdProcessor)) return bundledMdProcessor;
        
        var bundledMdProcessorDll = Path.Combine(appDir, "Esp", "Tools", "nanoFramework.Tools.MetadataProcessor.Console.dll");
        if (File.Exists(bundledMdProcessorDll)) return bundledMdProcessorDll;
        
        // 4. Check NuGet global cache for MetadataProcessor
        var nugetCachePath = FindMetadataProcessorInNuGetCache();
        if (!string.IsNullOrEmpty(nugetCachePath)) return nugetCachePath;
        
        // 5. Check project-local packages folder
        var localPackages = Path.Combine(projectDir, "packages");
        var localMdProcessor = FindMetadataProcessorInDir(localPackages);
        if (!string.IsNullOrEmpty(localMdProcessor)) return localMdProcessor;
        
        // 6. Check VS extension installation paths
        var vsExtMdProcessor = FindMetadataProcessorInVsExtensions();
        if (!string.IsNullOrEmpty(vsExtMdProcessor)) return vsExtMdProcessor;
        
        // 7. Try to install as a dotnet tool
        var installed = await TryInstallMetadataProcessorToolAsync();
        if (!string.IsNullOrEmpty(installed)) return installed;
        
        // 8. Try to download the NuGet package
        var downloaded = await TryDownloadMetadataProcessorAsync(projectDir);
        if (!string.IsNullOrEmpty(downloaded)) return downloaded;
        
        OnOutput("[WARNING] nanoFramework MetadataProcessor not found.\n");
        OnOutput("[HINT] Install it via: dotnet tool install -g nanoFramework.Tools.MetadataProcessor.Console\n");
        OnOutput("[HINT] Or install the nanoFramework VS extension which includes it.\n");
        return null;
    }
    
    /// <summary>
    /// Find MetadataProcessor from the host IDE's own NuGet package dependencies.
    /// The host project references nanoFramework.Tools.MetadataProcessor.CLI and 
    /// nanoFramework.Tools.MetadataProcessor.MsBuildTask packages, so they exist
    /// in the NuGet global cache. This method resolves the path from the host's
    /// deps.json or directly from the NuGet cache using the known package versions.
    /// </summary>
    private string? FindMetadataProcessorFromHostPackages()
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Strategy 1: Check if MetadataProcessor DLLs/EXEs are copied to the host output directory
            var hostSearchPatterns = new[]
            {
                "nanoFramework.Tools.MetadataProcessor.Console.exe",
                "nanoFramework.Tools.MetadataProcessor.Console.dll",
                "nanoFramework.Tools.MetadataProcessor.exe",
                "nanoFramework.Tools.MetadataProcessor.dll",
            };
            
            foreach (var pattern in hostSearchPatterns)
            {
                var hostPath = Path.Combine(appDir, pattern);
                if (File.Exists(hostPath))
                {
                    OnOutput($"[INFO] Found MetadataProcessor in host output: {hostPath}\n");
                    return hostPath;
                }
            }
            
            // Strategy 2: Read the host's .deps.json to find exact package versions and paths
            var depsJsonPath = Directory.GetFiles(appDir, "*.deps.json").FirstOrDefault();
            if (!string.IsNullOrEmpty(depsJsonPath))
            {
                var mdProcessorFromDeps = FindMetadataProcessorFromDepsJson(depsJsonPath);
                if (!string.IsNullOrEmpty(mdProcessorFromDeps))
                    return mdProcessorFromDeps;
            }
            
            // Strategy 3: Search NuGet global packages cache for the specific versions 
            // referenced by the host IDE (CLI and MsBuildTask packages)
            var nugetGlobalCache = GetNuGetGlobalPackagesPath();
            if (!string.IsNullOrEmpty(nugetGlobalCache) && Directory.Exists(nugetGlobalCache))
            {
                // Search for the CLI package first (preferred — has the console tool)
                var cliPackageNames = new[]
                {
                    "nanoframework.tools.metadataprocessor.cli",
                    "nanoframework.tools.metadataprocessor.msbuildtask",
                };
                
                foreach (var pkgName in cliPackageNames)
                {
                    var pkgDir = Path.Combine(nugetGlobalCache, pkgName);
                    if (!Directory.Exists(pkgDir)) continue;
                    
                    // Get all versions, prefer the version matching what the host uses (4.0.0-preview.73)
                    var versionDirs = Directory.GetDirectories(pkgDir)
                        .OrderByDescending(d =>
                        {
                            var dirName = Path.GetFileName(d);
                            // Prefer the exact version we reference in csproj
                            return dirName.Equals("4.0.0-preview.73", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        })
                        .ThenByDescending(d => d)
                        .ToArray();
                    
                    foreach (var versionDir in versionDirs)
                    {
                        // Look for the executable in tools/ or lib/ subdirectories
                        var toolsDirs = new[]
                        {
                            Path.Combine(versionDir, "tools"),
                            Path.Combine(versionDir, "lib"),
                            Path.Combine(versionDir, "content"),
                            versionDir
                        };
                        
                        foreach (var searchDir in toolsDirs)
                        {
                            if (!Directory.Exists(searchDir)) continue;
                            
                            var result = FindMetadataProcessorInDir(searchDir);
                            if (!string.IsNullOrEmpty(result))
                            {
                                OnOutput($"[INFO] Found MetadataProcessor in host NuGet cache: {result}\n");
                                return result;
                            }
                        }
                    }
                }
            }
            
            // Strategy 4: Search relative to the host project source directory
            // (development scenario — run from IDE)
            var hostProjectDir = FindHostProjectDirectory();
            if (!string.IsNullOrEmpty(hostProjectDir))
            {
                var objDir = Path.Combine(hostProjectDir, "obj");
                if (Directory.Exists(objDir))
                {
                    // NuGet restore puts package info in obj/ folder
                    var result = FindMetadataProcessorInDir(objDir);
                    if (!string.IsNullOrEmpty(result))
                        return result;
                }
            }
        }
        catch (Exception ex)
        {
            OnOutput($"[DEBUG] Error searching host packages for MetadataProcessor: {ex.Message}\n");
        }
        
        return null;
    }
    
    /// <summary>
    /// Parse the host application's .deps.json to find MetadataProcessor package path
    /// </summary>
    private string? FindMetadataProcessorFromDepsJson(string depsJsonPath)
    {
        try
        {
            var depsContent = File.ReadAllText(depsJsonPath);
            
            // Look for MetadataProcessor entries in deps.json
            // The deps.json contains package name/version references that map to NuGet cache paths
            var mdProcessorPackages = new[]
            {
                "nanoFramework.Tools.MetadataProcessor.CLI",
                "nanoFramework.Tools.MetadataProcessor.MsBuildTask",
                "nanoFramework.Tools.MetadataProcessor.Console",
            };
            
            foreach (var pkgName in mdProcessorPackages)
            {
                // Find version from deps.json content (format: "PackageName/Version")
                var searchPattern = $"\"{pkgName}/";
                var idx = depsContent.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                
                // Extract version
                var versionStart = idx + searchPattern.Length;
                var versionEnd = depsContent.IndexOf('"', versionStart);
                if (versionEnd < 0) continue;
                
                var version = depsContent.Substring(versionStart, versionEnd - versionStart);
                
                // Resolve from NuGet cache
                var nugetCache = GetNuGetGlobalPackagesPath();
                if (string.IsNullOrEmpty(nugetCache)) continue;
                
                var pkgPath = Path.Combine(nugetCache, pkgName.ToLowerInvariant(), version);
                if (Directory.Exists(pkgPath))
                {
                    var result = FindMetadataProcessorInDir(pkgPath);
                    if (!string.IsNullOrEmpty(result))
                    {
                        OnOutput($"[INFO] Resolved MetadataProcessor from deps.json: {pkgName}/{version}\n");
                        return result;
                    }
                }
            }
        }
        catch { /* Ignore parsing errors */ }
        
        return null;
    }
    
    /// <summary>
    /// Get the NuGet global packages folder path
    /// </summary>
    private static string? GetNuGetGlobalPackagesPath()
    {
        // Standard NuGet global packages path
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var standardPath = Path.Combine(userProfile, ".nuget", "packages");
            if (Directory.Exists(standardPath))
                return standardPath;
        }
        
        // Check NUGET_PACKAGES environment variable
        var nugetPackagesEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(nugetPackagesEnv) && Directory.Exists(nugetPackagesEnv))
            return nugetPackagesEnv;
        
        // Try to resolve from dotnet nuget locals command output (cached)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = "nuget locals global-packages --list",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = psi };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            
            // Output format: "global-packages: C:\Users\user\.nuget\packages\"
            var prefix = "global-packages:";
            var prefixIdx = output.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIdx >= 0)
            {
                var path = output.Substring(prefixIdx + prefix.Length).Trim();
                if (Directory.Exists(path))
                    return path;
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }
    
    /// <summary>
    /// Try to find the host project directory (for development scenarios)
    /// </summary>
    private static string? FindHostProjectDirectory()
    {
        try
        {
            // Walk up from the app base directory looking for the .csproj
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
                
                var csprojFiles = Directory.GetFiles(dir, "*.csproj");
                if (csprojFiles.Any(f => File.ReadAllText(f).Contains("nanoFramework.Tools.MetadataProcessor", StringComparison.OrdinalIgnoreCase)))
                {
                    return dir;
                }
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }
    
    /// <summary>
    /// Find MetadataProcessor in the global NuGet packages cache
    /// </summary>
    private string? FindMetadataProcessorInNuGetCache()
    {
        try
        {
            var globalCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
            
            if (!Directory.Exists(globalCachePath)) return null;
            
            // Search for the MetadataProcessor MsBuildTask package (contains the tool)
            var packageNames = new[]
            {
                "nanoframework.tools.metadataprocessor.msbuildtask",
                "nanoframework.tools.metadataprocessor.console",
                "nanoframework.tools.metadataprocessor"
            };

            foreach (var pkgName in packageNames)
            {
                var pkgDir = Path.Combine(globalCachePath, pkgName);
                if (!Directory.Exists(pkgDir)) continue;
                
                // Get latest version
                var versions = Directory.GetDirectories(pkgDir)
                    .OrderByDescending(d => d)
                    .ToArray();
                
                foreach (var versionDir in versions)
                {
                    var result = FindMetadataProcessorInDir(versionDir);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
        }
        catch { /* Ignore search errors */ }
        
        return null;
    }
    
    /// <summary>
    /// Search a directory tree for MetadataProcessor executable
    /// </summary>
    private string? FindMetadataProcessorInDir(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        
        try
        {
            var searchPatterns = new[]
            {
                "nanoFramework.Tools.MetadataProcessor.Console.exe",
                "nanoFramework.Tools.MetadataProcessor.Console.dll",
                "MetadataProcessor.Console.exe",
                "MetadataProcessor.Console.dll",
                "nanoFramework.Tools.MetadataProcessor.exe",
                "nanoFramework.Tools.MetadataProcessor.dll"
            };

            foreach (var pattern in searchPatterns)
            {
                var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    // Prefer .exe over .dll, and prefer net8.0/net6.0 over netstandard
                    var preferred = files
                        .OrderByDescending(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenByDescending(f => f.Contains("net8.0") ? 3 : f.Contains("net6.0") ? 2 : f.Contains("net7.0") ? 1 : 0)
                        .First();
                    return preferred;
                }
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }
    
    /// <summary>
    /// Search VS extension paths for MetadataProcessor
    /// </summary>
    private string? FindMetadataProcessorInVsExtensions()
    {
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var vsYears = new[] { "2025", "2022", "2019" };
            var vsEditions = new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" };

            foreach (var year in vsYears)
            {
                foreach (var edition in vsEditions)
                {
                    foreach (var pf in new[] { programFiles, programFilesX86 })
                    {
                        if (string.IsNullOrEmpty(pf)) continue;
                        var extensionsDir = Path.Combine(pf, "Microsoft Visual Studio", year, edition,
                            "MSBuild", "nanoFramework", "v1.0");
                        var result = FindMetadataProcessorInDir(extensionsDir);
                        if (!string.IsNullOrEmpty(result)) return result;
                    }
                }
            }
            
            // Also check local AppData VS extensions
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var vsExtDir = Path.Combine(localAppData, "Microsoft", "VisualStudio");
            if (Directory.Exists(vsExtDir))
            {
                var result = FindMetadataProcessorInDir(vsExtDir);
                if (!string.IsNullOrEmpty(result)) return result;
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }
    
    /// <summary>
    /// Try to install MetadataProcessor as a global dotnet tool
    /// </summary>
    private async Task<string?> TryInstallMetadataProcessorToolAsync()
    {
        try
        {
            OnOutput("Attempting to install nanoFramework MetadataProcessor tool...\n");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = "tool install -g nanoFramework.Tools.MetadataProcessor.Console",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.AppendLine(e.Data);
                    OnOutput($"  {e.Data}\n");
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"  [INFO] {e.Data}\n");
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                OnOutput("MetadataProcessor tool installed successfully.\n");
                // Find the installed tool
                var toolPath = FindInPath("nanoFramework.Tools.MetadataProcessor.Console");
                if (!string.IsNullOrEmpty(toolPath)) return toolPath;
                
                // Check dotnet tools directory
                var dotnetToolsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".dotnet", "tools");
                var result = FindMetadataProcessorInDir(dotnetToolsDir);
                if (!string.IsNullOrEmpty(result)) return result;
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }
    
    /// <summary>
    /// Try to download MetadataProcessor NuGet package to project tools folder
    /// </summary>
    private async Task<string?> TryDownloadMetadataProcessorAsync(string projectDir)
    {
        try
        {
            var toolsDir = Path.Combine(projectDir, ".nftools");
            Directory.CreateDirectory(toolsDir);
            
            OnOutput("Downloading MetadataProcessor NuGet package...\n");
            
            // Use dotnet to restore the package to a temp project
            var tempProjContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""nanoFramework.Tools.MetadataProcessor.MsBuildTask"" Version=""*"" />
  </ItemGroup>
</Project>";
            
            var tempProjPath = Path.Combine(toolsDir, "temp_mdprocessor.csproj");
            File.WriteAllText(tempProjPath, tempProjContent);
            
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = $"restore \"{tempProjPath}\" --packages \"{toolsDir}\"",
                WorkingDirectory = toolsDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            // Clean up temp project
            try { File.Delete(tempProjPath); } catch { }
            
            if (process.ExitCode == 0)
            {
                var result = FindMetadataProcessorInDir(toolsDir);
                if (!string.IsNullOrEmpty(result))
                {
                    OnOutput($"MetadataProcessor downloaded to: {result}\n");
                    return result;
                }
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }
    
    /// <summary>
    /// Fallback PE generation when MetadataProcessor is not available.
    /// Copies pre-built PE files from NuGet packages and attempts basic PE file creation.
    /// </summary>
    private async Task<(bool success, string? mainPePath, List<string> allPeFiles)> FallbackPeGenerationAsync(
        string mainDll, string outputDir, string projectDir)
    {
        var allPeFiles = new List<string>();
        
        // Collect pre-existing PE files from NuGet package references
        // nanoFramework NuGet packages include pre-built .pe files alongside their .dll files
        var packagesDir = Path.Combine(projectDir, "packages");
        if (Directory.Exists(packagesDir))
        {
            var nugetPeFiles = Directory.GetFiles(packagesDir, "*.pe", SearchOption.AllDirectories);
            foreach (var pe in nugetPeFiles)
            {
                var destPe = Path.Combine(outputDir, Path.GetFileName(pe));
                if (!File.Exists(destPe))
                {
                    try
                    {
                        File.Copy(pe, destPe, overwrite: true);
                        OnOutput($"  Copied PE from package: {Path.GetFileName(pe)}\n");
                    }
                    catch { /* Ignore copy errors */ }
                }
                allPeFiles.Add(destPe);
            }
        }
        
        // Also check global NuGet cache for PE files
        var globalCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
        
        if (Directory.Exists(globalCachePath))
        {
            // Find PE files for referenced assemblies
            var referencedDlls = Directory.GetFiles(outputDir, "*.dll");
            foreach (var dll in referencedDlls)
            {
                if (string.Equals(dll, mainDll, StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                var peName = Path.GetFileNameWithoutExtension(dll) + ".pe";
                var destPe = Path.Combine(outputDir, peName);
                if (File.Exists(destPe))
                {
                    if (!allPeFiles.Contains(destPe))
                        allPeFiles.Add(destPe);
                    continue;
                }
                
                // Search NuGet cache for this PE file
                try
                {
                    var foundPe = Directory.GetFiles(globalCachePath, peName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(foundPe))
                    {
                        File.Copy(foundPe, destPe, overwrite: true);
                        OnOutput($"  Copied PE from NuGet cache: {peName}\n");
                        allPeFiles.Add(destPe);
                    }
                }
                catch { /* Ignore */ }
            }
        }
        
        // Collect any PE files already in output directory
        var existingPeFiles = Directory.GetFiles(outputDir, "*.pe");
        foreach (var pe in existingPeFiles)
        {
            if (!allPeFiles.Contains(pe))
                allPeFiles.Add(pe);
        }
        
        var mainPePath = Path.ChangeExtension(mainDll, ".pe");
        
        // If we still don't have the main PE file, we need MetadataProcessor
        if (!File.Exists(mainPePath))
        {
            OnOutput($"[WARNING] Main PE file not generated: {Path.GetFileName(mainPePath)}\n");
            OnOutput("[WARNING] MetadataProcessor is required to convert your DLL to PE format.\n");
            OnOutput("[HINT] Install: dotnet tool install -g nanoFramework.Tools.MetadataProcessor.Console\n");
            return (false, null, allPeFiles);
        }
        
        return (true, mainPePath, allPeFiles);
    }
    
    /// <summary>
    /// Find an executable in the system PATH
    /// </summary>
    private string? FindInPath(string executableName)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = executableName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                    return firstLine;
            }
        }
        catch { /* Ignore */ }
        
        return null;
    }

    /// <summary>
    /// Cancel the current build
    /// </summary>
    public void CancelBuild()
    {
        try
        {
            if (_buildProcess != null && !_buildProcess.HasExited)
            {
                _buildProcess.Kill(entireProcessTree: true);
                OnOutput("\n========== Build Cancelled ==========\n");
            }
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Failed to cancel build: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Find the nanoFramework build target (.csproj with NanoFrameworkProject marker, or legacy .nfproj)
    /// </summary>
    public static string? FindNanoBuildTarget(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".nfproj") return path;
            
            // Check if .csproj is a nanoFramework project
            if (ext == ".csproj")
            {
                try
                {
                    var content = File.ReadAllText(path);
                    if (content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("nanoFramework", StringComparison.OrdinalIgnoreCase))
                        return path;
                }
                catch { /* Ignore read errors */ }
            }
            
            // If it's a solution file or other file, search in directory
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (!Directory.Exists(path)) return null;

        // Search for legacy .nfproj first
        var nfprojFiles = Directory.GetFiles(path, "*.nfproj", SearchOption.TopDirectoryOnly);
        if (nfprojFiles.Length > 0) return nfprojFiles[0];

        // Search for .csproj with nanoFramework marker
        var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
        foreach (var csproj in csprojFiles)
        {
            try
            {
                var content = File.ReadAllText(csproj);
                if (content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase))
                    return csproj;
            }
            catch { /* Ignore read errors */ }
        }

        // Check subdirectories
        try
        {
            nfprojFiles = Directory.GetFiles(path, "*.nfproj", SearchOption.AllDirectories);
            if (nfprojFiles.Length > 0) return nfprojFiles[0];
            
            csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            foreach (var csproj in csprojFiles)
            {
                try
                {
                    var content = File.ReadAllText(csproj);
                    if (content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase))
                        return csproj;
                }
                catch { /* Ignore read errors */ }
            }
        }
        catch
        {
            // Fallback: check one level deep if AllDirectories throws
            foreach (var subDir in Directory.GetDirectories(path))
            {
                nfprojFiles = Directory.GetFiles(subDir, "*.nfproj", SearchOption.TopDirectoryOnly);
                if (nfprojFiles.Length > 0) return nfprojFiles[0];
                
                csprojFiles = Directory.GetFiles(subDir, "*.csproj", SearchOption.TopDirectoryOnly);
                foreach (var csproj in csprojFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(csproj);
                        if (content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase))
                            return csproj;
                    }
                    catch { /* Ignore read errors */ }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Find MSBuild.exe on the system using vswhere and manual path search
    /// </summary>
    private string? FindMSBuild()
    {
        // 0. Check user settings first
        var fromSettings = SettingsPanelControl.ResolveMSBuildExe();
        if (fromSettings != null) return fromSettings;

        // 1. Try vswhere.exe first (most reliable method)
        var msbuildViaVsWhere = FindMSBuildViaVsWhere();
        if (!string.IsNullOrEmpty(msbuildViaVsWhere)) return msbuildViaVsWhere;
        
        // 2. Try common MSBuild paths for known VS versions
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var vsYears = new[] { "2025", "2022", "2019" };
        var vsEditions = new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" };

        foreach (var year in vsYears)
        {
            foreach (var edition in vsEditions)
            {
                var msbuildPath = Path.Combine(programFiles, "Microsoft Visual Studio", year, edition,
                    "MSBuild", "Current", "Bin", "MSBuild.exe");
                if (File.Exists(msbuildPath)) return msbuildPath;

                msbuildPath = Path.Combine(programFilesX86, "Microsoft Visual Studio", year, edition,
                    "MSBuild", "Current", "Bin", "MSBuild.exe");
                if (File.Exists(msbuildPath)) return msbuildPath;
            }
        }

        // 3. Try to find MSBuild from PATH
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "msbuild",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                    return firstLine;
            }
        }
        catch { /* Ignore */ }

        return null;
    }
    
    /// <summary>
    /// Find MSBuild via vswhere.exe (Visual Studio locator tool)
    /// </summary>
    private string? FindMSBuildViaVsWhere()
    {
        try
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            
            if (!File.Exists(vswherePath)) return null;
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = vswherePath,
                    Arguments = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                {
                    return firstLine;
                }
            }
        }
        catch { /* Ignore errors */ }
        
        return null;
    }

    /// <summary>
    /// Find the output .pe file from build
    /// </summary>
    private string? FindOutputPe(string projectFile, string configuration)
    {
        var projectDir = Path.GetDirectoryName(projectFile);
        if (projectDir == null) return null;

        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        
        // Use FindBuildOutputDir for correct TFM-aware path
        var outputDir = FindBuildOutputDir(projectFile, configuration);
        if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir))
        {
            var exactPe = Path.Combine(outputDir, $"{assemblyName}.pe");
            if (File.Exists(exactPe)) return exactPe;
        }

        // Search all bin subdirectories
        var binDir = Path.Combine(projectDir, "bin");
        if (Directory.Exists(binDir))
        {
            // First try exact match (exclude _pe_build temp folder)
            var peFiles = Directory.GetFiles(binDir, $"{assemblyName}.pe", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_pe_build" + Path.DirectorySeparatorChar))
                .ToArray();
            if (peFiles.Length > 0) return peFiles[0];
            
            // Then any PE file
            peFiles = Directory.GetFiles(binDir, "*.pe", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_pe_build" + Path.DirectorySeparatorChar))
                .ToArray();
            return peFiles.Length > 0 ? peFiles[0] : null;
        }

        return null;
    }
    
    /// <summary>
    /// Find all PE files in the build output directory (for deployment)
    /// </summary>
    public static List<string> FindAllOutputPeFiles(string projectFile, string configuration)
    {
        var result = new List<string>();
        var projectDir = Path.GetDirectoryName(projectFile);
        if (projectDir == null) return result;
        
        // Search entire bin/{config} tree (including TFM subdirs like net10.0/)
        var binConfigDir = Path.Combine(projectDir, "bin", configuration);
        if (Directory.Exists(binConfigDir))
        {
            // Exclude _pe_build temporary folder (used for isolated PE generation)
            result.AddRange(Directory.GetFiles(binConfigDir, "*.pe", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_pe_build" + Path.DirectorySeparatorChar)));
        }
        
        // Also check bin/publish/esp32
        var publishDir = Path.Combine(projectDir, "bin", "publish", "esp32");
        if (Directory.Exists(publishDir))
        {
            foreach (var pe in Directory.GetFiles(publishDir, "*.pe"))
            {
                if (!result.Contains(pe))
                    result.Add(pe);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Copy all generated PE files (main assembly + NuGet package dependencies) into a
    /// dedicated "pe files for deploy" folder inside the project directory.
    /// This folder contains everything needed to deploy the application to an ESP32 device.
    /// </summary>
    private async Task CopyPeFilesToDeployFolderAsync(string projectFile, List<string> peFiles)
    {
        if (peFiles.Count == 0) return;

        var projectDir = Path.GetDirectoryName(projectFile);
        if (string.IsNullOrEmpty(projectDir)) return;

        var deployFolder = Path.Combine(projectDir, "pe files for deploy");

        try
        {
            // Recreate folder to ensure clean state (remove stale PE files)
            if (Directory.Exists(deployFolder))
                Directory.Delete(deployFolder, recursive: true);
            Directory.CreateDirectory(deployFolder);

            OnOutput($"\n========== Copying PE Files to Deploy Folder ==========\n");
            OnOutput($"Destination: {deployFolder}\n\n");

            var copiedCount = 0;
            long totalSize = 0;

            foreach (var peFile in peFiles)
            {
                if (!File.Exists(peFile)) continue;

                var destPath = Path.Combine(deployFolder, Path.GetFileName(peFile));
                try
                {
                    File.Copy(peFile, destPath, overwrite: true);
                    var size = new FileInfo(destPath).Length;
                    totalSize += size;
                    copiedCount++;
                    OnOutput($"  ✓ {Path.GetFileName(peFile)} ({size:N0} bytes)\n");
                }
                catch (Exception ex)
                {
                    OnOutput($"  ✗ Failed to copy {Path.GetFileName(peFile)}: {ex.Message}\n");
                }
            }

            // Also collect PE files that came from NuGet packages in the NuGet cache
            // (they may not be in peFiles list if they were found separately)
            await CollectNuGetPackagePeFilesAsync(projectFile, deployFolder);

            OnOutput($"\n  Total: {copiedCount} PE file(s), {totalSize:N0} bytes\n");
            OnOutput($"  Folder: {deployFolder}\n");
            OnOutput($"\n  These files are ready to be deployed to your ESP32 device.\n");
            OnOutput($"  Use the Device Panel or:\n");
            OnOutput($"  nanoff --target ESP32 --serialport COMx --deploy \"{deployFolder}\\*.pe\"\n");
        }
        catch (Exception ex)
        {
            OnOutput($"[WARNING] Could not create deploy folder: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Collect PE files for all NuGet packages referenced in the project from the NuGet global cache.
    /// Copies any missing PE files into the deploy folder.
    /// nanoFramework NuGet packages ship with pre-built .pe files alongside their DLLs.
    /// </summary>
    private async Task CollectNuGetPackagePeFilesAsync(string projectFile, string deployFolder)
    {
        await Task.Run(() =>
        {
            try
            {
                var projectDir = Path.GetDirectoryName(projectFile);
                if (string.IsNullOrEmpty(projectDir)) return;

                var globalCachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages");

                // Read the project file to find PackageReference entries
                var projectContent = File.ReadAllText(projectFile);
                var packageIds = ExtractPackageReferences(projectContent);

                if (packageIds.Count == 0) return;

                OnOutput($"\n  Collecting PE files from {packageIds.Count} NuGet package(s)...\n");

                foreach (var (packageId, version) in packageIds)
                {
                    // Look in the global NuGet cache for pre-built PE files
                    var pkgId = packageId.ToLowerInvariant();
                    var pkgDir = Path.Combine(globalCachePath, pkgId);
                    if (!Directory.Exists(pkgDir)) continue;

                    // Find matching version directory (exact or latest)
                    string? versionDir = null;
                    if (!string.IsNullOrEmpty(version))
                    {
                        var exactDir = Path.Combine(pkgDir, version);
                        if (Directory.Exists(exactDir))
                            versionDir = exactDir;
                    }

                    if (versionDir == null)
                    {
                        // Use the highest available version
                        versionDir = Directory.GetDirectories(pkgDir)
                            .OrderByDescending(d => d)
                            .FirstOrDefault();
                    }

                    if (versionDir == null) continue;

                    // Search for .pe files in lib/ subdirectories
                    var libDir = Path.Combine(versionDir, "lib");
                    if (!Directory.Exists(libDir)) continue;

                    var peFiles = Directory.GetFiles(libDir, "*.pe", SearchOption.AllDirectories);
                    foreach (var peFile in peFiles)
                    {
                        var destPath = Path.Combine(deployFolder, Path.GetFileName(peFile));
                        if (!File.Exists(destPath))
                        {
                            try
                            {
                                File.Copy(peFile, destPath, overwrite: true);
                                var size = new FileInfo(destPath).Length;
                                OnOutput($"  ✓ [pkg] {Path.GetFileName(peFile)} ({size:N0} bytes) — {packageId}\n");
                            }
                            catch { /* Ignore copy errors for individual package PE files */ }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnOutput($"  [INFO] Could not collect NuGet package PE files: {ex.Message}\n");
            }
        });
    }

    /// <summary>
    /// Parse PackageReference elements from a .csproj / .nfproj project file content.
    /// Returns a list of (packageId, version) tuples.
    /// </summary>
    private static List<(string id, string version)> ExtractPackageReferences(string projectContent)
    {
        var result = new List<(string id, string version)>();
        try
        {
            // Simple XML parsing — look for <PackageReference Include="..." Version="..."/>
            const string pkgRefTag = "<PackageReference";
            var idx = 0;
            while ((idx = projectContent.IndexOf(pkgRefTag, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var endIdx = projectContent.IndexOf('>', idx);
                if (endIdx < 0) break;
                var element = projectContent.Substring(idx, endIdx - idx + 1);

                var id = ExtractXmlAttribute(element, "Include");
                var version = ExtractXmlAttribute(element, "Version");

                if (!string.IsNullOrEmpty(id))
                    result.Add((id, version ?? ""));

                idx = endIdx + 1;
            }
        }
        catch { /* Ignore parse errors */ }
        return result;
    }

    /// <summary>
    /// Extract the value of an XML attribute from a tag string.
    /// </summary>
    private static string? ExtractXmlAttribute(string element, string attributeName)
    {
        var pattern = $"{attributeName}=\"";
        var idx = element.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += pattern.Length;
        var endIdx = element.IndexOf('"', idx);
        if (endIdx < 0) return null;
        return element.Substring(idx, endIdx - idx);
    }

    private void OnOutput(string message)
    {
        OutputReceived?.Invoke(this, new NanoBuildOutputEventArgs(message));
    }

    /// <summary>
    /// Publish a nanoFramework project: build → generate PE files → copy to publish folder.
    /// The publish folder contains .pe files ready to be flashed to an ESP32 device.
    /// </summary>
    public async Task<NanoPublishResult> PublishAsync(string projectPath, string configuration = "Release", string? outputFolder = null)
    {
        OnOutput("========== nanoFramework Publish for ESP32 ==========\n\n");

        // Step 1: Build the project
        OnOutput("── Step 1/3: Building project ──\n");
        var buildResult = await BuildAsync(projectPath, configuration);
        if (!buildResult.Success)
        {
            OnOutput("\n[ERROR] Build failed. Cannot generate PE files.\n");
            return new NanoPublishResult
            {
                Success = false,
                ErrorMessage = "Build failed. Fix build errors first.",
                Output = buildResult.Output
            };
        }

        // Resolve target file for PE generation
        var targetFile = FindNanoBuildTarget(projectPath);
        if (targetFile == null)
        {
            return new NanoPublishResult
            {
                Success = false,
                ErrorMessage = "No nanoFramework project file found."
            };
        }

        var projectDir = Path.GetDirectoryName(targetFile) ?? projectPath;
        var publishDir = outputFolder ?? Path.Combine(projectDir, "bin", "publish", "esp32");
        Directory.CreateDirectory(publishDir);

        // Step 2: Generate PE files
        OnOutput("\n── Step 2/3: Generating PE files for ESP32 ──\n");
        var peResult = await GeneratePeFilesAsync(targetFile, configuration);

        List<string> peFiles;
        if (peResult.success && peResult.allPeFiles.Count > 0)
        {
            peFiles = peResult.allPeFiles;
        }
        else
        {
            // Try to find any existing PE files from the build output
            peFiles = FindAllOutputPeFiles(targetFile, configuration);
            if (peFiles.Count == 0)
            {
                OnOutput("\n[ERROR] No PE files were generated. MetadataProcessor may be required.\n");
                OnOutput("[HINT] Install: dotnet tool install -g nanoFramework.Tools.MetadataProcessor.Console\n");
                return new NanoPublishResult
                {
                    Success = false,
                    ErrorMessage = "PE file generation failed. Install MetadataProcessor tool.",
                    Output = _outputBuffer.ToString(),
                    PublishDirectory = publishDir
                };
            }
        }

        // Step 3: Copy PE files to publish directory
        OnOutput($"\n── Step 3/3: Copying PE files to publish folder ──\n");
        OnOutput($"Output: {publishDir}\n\n");

        var copiedFiles = new List<string>();
        long totalSize = 0;
        foreach (var peFile in peFiles)
        {
            try
            {
                var destPath = Path.Combine(publishDir, Path.GetFileName(peFile));
                File.Copy(peFile, destPath, overwrite: true);
                var fileSize = new FileInfo(destPath).Length;
                totalSize += fileSize;
                copiedFiles.Add(destPath);
                OnOutput($"  ✓ {Path.GetFileName(peFile)} ({fileSize:N0} bytes)\n");
            }
            catch (Exception ex)
            {
                OnOutput($"  ✗ Failed to copy {Path.GetFileName(peFile)}: {ex.Message}\n");
            }
        }

        if (copiedFiles.Count == 0)
        {
            OnOutput("\n[ERROR] No PE files were copied to publish folder.\n");
            return new NanoPublishResult
            {
                Success = false,
                ErrorMessage = "No PE files could be copied to publish folder.",
                Output = _outputBuffer.ToString(),
                PublishDirectory = publishDir
            };
        }

        OnOutput($"\n========== nanoFramework Publish Succeeded ==========\n");
        OnOutput($"Total PE files: {copiedFiles.Count}\n");
        OnOutput($"Total size: {totalSize:N0} bytes\n");
        OnOutput($"Output folder: {publishDir}\n");
        OnOutput($"\nThese .pe files are ready to be flashed to your ESP32 device.\n");
        OnOutput($"Use the Device Panel to deploy, or copy them manually via nanoff tool:\n");
        OnOutput($"  nanoff --target ESP32 --serialport COMx --deploy \"{publishDir}\\*.pe\"\n");

        return new NanoPublishResult
        {
            Success = true,
            Output = _outputBuffer.ToString(),
            PublishDirectory = publishDir,
            PeFiles = copiedFiles,
            TotalSize = totalSize
        };
    }
}

/// <summary>
/// Result of a nanoFramework publish operation (PE files for ESP32 deployment)
/// </summary>
public class NanoPublishResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? PublishDirectory { get; set; }
    public List<string> PeFiles { get; set; } = new();
    public long TotalSize { get; set; }
}

public class NanoBuildResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string ErrorOutput { get; set; } = "";
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// Path to the main PE file (the project's assembly converted to nanoFramework PE format)
    /// </summary>
    public string? OutputPePath { get; set; }
    /// <summary>
    /// All PE files generated/collected during build (main assembly + dependencies).
    /// All of these need to be deployed to the ESP32 device.
    /// </summary>
    public List<string> AllPeFiles { get; set; } = new();
}

public class NanoBuildOutputEventArgs : EventArgs
{
    public string Output { get; }
    public NanoBuildOutputEventArgs(string output) { Output = output; }
}

public class NanoBuildCompletedEventArgs : EventArgs
{
    public NanoBuildResult Result { get; }
    public NanoBuildCompletedEventArgs(NanoBuildResult result) { Result = result; }
}

