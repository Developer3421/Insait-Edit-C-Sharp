using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for building .NET projects using MSBuild/dotnet CLI
/// </summary>
public class BuildService
{
    public event EventHandler<BuildOutputEventArgs>? OutputReceived;
    public event EventHandler<BuildCompletedEventArgs>? BuildCompleted;
    public event EventHandler? BuildStarted;

    private Process? _buildProcess;
    private readonly StringBuilder _outputBuffer = new();
    private readonly StringBuilder _errorBuffer = new();

    /// <summary>
    /// Build a project or solution
    /// </summary>
    /// <param name="projectPath">Path to .sln, .csproj, or project directory</param>
    /// <param name="configuration">Build configuration (Debug/Release)</param>
    /// <returns>True if build was successful</returns>
    public async Task<BuildResult> BuildAsync(string projectPath, string configuration = "Debug")
    {
        if (string.IsNullOrEmpty(projectPath))
        {
            return new BuildResult
            {
                Success = false,
                ErrorMessage = "No project path specified",
                Output = ""
            };
        }

        // Find the project/solution file
        string? targetFile = FindBuildTarget(projectPath);
        if (targetFile == null)
        {
            return new BuildResult
            {
                Success = false,
                ErrorMessage = $"No .sln or .csproj file found in: {projectPath}",
                Output = ""
            };
        }

        _outputBuffer.Clear();
        _errorBuffer.Clear();

        BuildStarted?.Invoke(this, EventArgs.Empty);
        OnOutput($"========== Build Started: {Path.GetFileName(targetFile)} ==========\n");
        OnOutput($"Configuration: {configuration}\n");
        OnOutput($"Target: {targetFile}\n\n");

        try
        {
            // Use dotnet build command
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = $"build \"{targetFile}\" --configuration {configuration} --no-restore",
                WorkingDirectory = Path.GetDirectoryName(targetFile) ?? projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

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

            OnOutput($"\n========== Build {(success ? "Succeeded" : "Failed")} ==========\n");

            var result = new BuildResult
            {
                Success = success,
                ExitCode = exitCode,
                Output = _outputBuffer.ToString(),
                ErrorOutput = _errorBuffer.ToString(),
                ErrorMessage = success ? null : "Build failed with errors"
            };

            BuildCompleted?.Invoke(this, new BuildCompletedEventArgs(result));

            return result;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Build process error: {ex.Message}";
            OnOutput($"\n[ERROR] {errorMessage}\n");

            var result = new BuildResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Output = _outputBuffer.ToString(),
                ErrorOutput = _errorBuffer.ToString()
            };

            BuildCompleted?.Invoke(this, new BuildCompletedEventArgs(result));

            return result;
        }
        finally
        {
            _buildProcess?.Dispose();
            _buildProcess = null;
        }
    }

    /// <summary>
    /// Build and run a project
    /// </summary>
    public async Task<BuildResult> BuildAndRunAsync(string projectPath, string configuration = "Debug")
    {
        var buildResult = await BuildAsync(projectPath, configuration);
        
        if (!buildResult.Success)
        {
            return buildResult;
        }

        // Run the project
        await RunProjectAsync(projectPath, configuration);
        
        return buildResult;
    }

    /// <summary>
    /// Run a project without rebuilding - handles both GUI and Console applications
    /// </summary>
    public async Task RunProjectAsync(string projectPath, string configuration = "Debug")
    {
        string? targetFile = FindBuildTarget(projectPath);
        if (targetFile == null)
        {
            OnOutput("[ERROR] No project file found to run\n");
            return;
        }

        OnOutput($"\n========== Running: {Path.GetFileName(targetFile)} ==========\n\n");

        try
        {
            // Determine if this is a GUI application
            var isGuiApp = IsGuiApplication(targetFile);
            
            // Find the output executable
            var projectDir = Path.GetDirectoryName(targetFile);
            var projectName = Path.GetFileNameWithoutExtension(targetFile);
            var outputDir = Path.Combine(projectDir!, "bin", configuration);
            
            // Find the target framework folder (e.g., net9.0-windows)
            string? executablePath = null;
            if (Directory.Exists(outputDir))
            {
                var frameworkDirs = Directory.GetDirectories(outputDir);
                foreach (var frameworkDir in frameworkDirs)
                {
                    var exePath = Path.Combine(frameworkDir, $"{projectName}.exe");
                    if (File.Exists(exePath))
                    {
                        executablePath = exePath;
                        break;
                    }
                    // Also try .dll for cross-platform apps
                    var dllPath = Path.Combine(frameworkDir, $"{projectName}.dll");
                    if (File.Exists(dllPath))
                    {
                        executablePath = dllPath;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(executablePath) && executablePath.EndsWith(".exe"))
            {
                OnOutput($"Starting: {executablePath}\n");
                
                if (isGuiApp)
                {
                    // For GUI apps, use ShellExecute so the window appears properly
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = Path.GetDirectoryName(executablePath),
                        UseShellExecute = true
                    };

                    var runProcess = Process.Start(startInfo);
                    if (runProcess != null)
                    {
                        OnOutput($"Application started (PID: {runProcess.Id})\n");
                        OnOutput($"[Info] Running as GUI application (output not captured)\n");
                        OnOutput($"\n========== Application Running ==========\n");
                    }
                }
                else
                {
                    // For console apps, capture output
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = Path.GetDirectoryName(executablePath),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = false,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    };

                    using var runProcess = new Process { StartInfo = startInfo };
                    
                    runProcess.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            OnOutput(e.Data + "\n");
                        }
                    };
                    
                    runProcess.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            OnOutput($"[stderr] {e.Data}\n");
                        }
                    };
                    
                    runProcess.Start();
                    OnOutput($"Application started (PID: {runProcess.Id})\n\n");
                    
                    runProcess.BeginOutputReadLine();
                    runProcess.BeginErrorReadLine();
                    
                    await runProcess.WaitForExitAsync();
                    
                    OnOutput($"\n========== Application Exited (Code: {runProcess.ExitCode}) ==========\n");
                }
            }
            else
            {
                // Fall back to dotnet run with output capture
                OnOutput("Starting via dotnet run...\n");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = SettingsPanelControl.ResolveDotNetExe(),
                    Arguments = $"run --project \"{targetFile}\" --configuration {configuration} --no-build",
                    WorkingDirectory = projectDir ?? projectPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var runProcess = new Process { StartInfo = startInfo };
                
                runProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OnOutput(e.Data + "\n");
                    }
                };
                
                runProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OnOutput($"[stderr] {e.Data}\n");
                    }
                };
                
                runProcess.Start();
                OnOutput($"Application started (PID: {runProcess.Id})\n\n");
                
                runProcess.BeginOutputReadLine();
                runProcess.BeginErrorReadLine();
                
                await runProcess.WaitForExitAsync();
                
                OnOutput($"\n========== Application Exited (Code: {runProcess.ExitCode}) ==========\n");
            }
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Failed to run project: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Check if the project is a GUI application (Avalonia, WPF, WinForms)
    /// </summary>
    private bool IsGuiApplication(string projectFile)
    {
        try
        {
            var content = File.ReadAllText(projectFile);
            
            // Check for OutputType=WinExe
            if (content.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Check for GUI frameworks
            return content.Contains("Avalonia") ||
                   content.Contains("UseWPF") ||
                   content.Contains("UseWindowsForms") ||
                   content.Contains("<UseWPF>true</UseWPF>") ||
                   content.Contains("<UseWindowsForms>true</UseWindowsForms>");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clean the build output
    /// </summary>
    public async Task<bool> CleanAsync(string projectPath, string configuration = "Debug")
    {
        string? targetFile = FindBuildTarget(projectPath);
        if (targetFile == null)
        {
            OnOutput("[ERROR] No project file found to clean\n");
            return false;
        }

        OnOutput($"========== Clean Started: {Path.GetFileName(targetFile)} ==========\n");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = $"clean \"{targetFile}\" --configuration {configuration}",
                WorkingDirectory = Path.GetDirectoryName(targetFile) ?? projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnOutput(e.Data + "\n");
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnOutput($"[ERROR] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput($"\n========== Clean {(success ? "Succeeded" : "Failed")} ==========\n");

            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Clean failed: {ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// Restore NuGet packages
    /// </summary>
    public async Task<bool> RestoreAsync(string projectPath)
    {
        string? targetFile = FindBuildTarget(projectPath);
        if (targetFile == null)
        {
            OnOutput("[ERROR] No project file found to restore\n");
            return false;
        }

        OnOutput($"========== Restore Started: {Path.GetFileName(targetFile)} ==========\n");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveDotNetExe(),
                Arguments = $"restore \"{targetFile}\"",
                WorkingDirectory = Path.GetDirectoryName(targetFile) ?? projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnOutput(e.Data + "\n");
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    OnOutput($"[ERROR] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput($"\n========== Restore {(success ? "Succeeded" : "Failed")} ==========\n");

            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Restore failed: {ex.Message}\n");
            return false;
        }
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
    /// Find the build target (.slnx, .sln or .csproj file)
    /// </summary>
    private string? FindBuildTarget(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // If path is already a project/solution file
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".slnx" || ext == ".sln" || ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj" || ext == ".nfproj")
                return path;
            
            // If it's a different file, search in its directory
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (!Directory.Exists(path))
            return null;

        // Look for .slnx file first (new format)
        var slnxFiles = Directory.GetFiles(path, "*.slnx", SearchOption.TopDirectoryOnly);
        if (slnxFiles.Length > 0)
            return slnxFiles[0];

        // Then look for .sln file (legacy format)
        var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length > 0)
            return slnFiles[0];

        // Then look for .csproj
        var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
        if (csprojFiles.Length > 0)
            return csprojFiles[0];

        // Then look for .nfproj (nanoFramework)
        var nfprojFiles = Directory.GetFiles(path, "*.nfproj", SearchOption.TopDirectoryOnly);
        if (nfprojFiles.Length > 0)
            return nfprojFiles[0];

        // Check subdirectories for .csproj or .nfproj (one level deep)
        foreach (var subDir in Directory.GetDirectories(path))
        {
            csprojFiles = Directory.GetFiles(subDir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
                return csprojFiles[0];

            nfprojFiles = Directory.GetFiles(subDir, "*.nfproj", SearchOption.TopDirectoryOnly);
            if (nfprojFiles.Length > 0)
                return nfprojFiles[0];
        }

        return null;
    }

    private void OnOutput(string message)
    {
        OutputReceived?.Invoke(this, new BuildOutputEventArgs(message));
    }
}

/// <summary>
/// Build result information
/// </summary>
public class BuildResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string ErrorOutput { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event args for build output
/// </summary>
public class BuildOutputEventArgs : EventArgs
{
    public string Output { get; }

    public BuildOutputEventArgs(string output)
    {
        Output = output;
    }
}

/// <summary>
/// Event args for build completed
/// </summary>
public class BuildCompletedEventArgs : EventArgs
{
    public BuildResult Result { get; }

    public BuildCompletedEventArgs(BuildResult result)
    {
        Result = result;
    }
}
