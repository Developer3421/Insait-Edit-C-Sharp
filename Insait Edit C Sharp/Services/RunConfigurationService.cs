using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for managing run configurations like JetBrains Rider
/// </summary>
public class RunConfigurationService
{
    public event EventHandler<RunOutputEventArgs>? OutputReceived;
    public event EventHandler<RunCompletedEventArgs>? RunCompleted;
    public event EventHandler? RunStarted;
    public event EventHandler? RunStopped;

    private Process? _runningProcess;
    private readonly StringBuilder _outputBuffer = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<RunConfiguration> _configurations = new();
    private readonly List<CompoundRunConfiguration> _compoundConfigurations = new();
    private RunConfiguration? _activeConfiguration;
    private CompoundRunConfiguration? _activeCompoundConfiguration;

    // Track all processes when running a compound configuration
    private readonly List<Process> _runningProcesses = new();

    public IReadOnlyList<RunConfiguration> Configurations => _configurations.AsReadOnly();
    public IReadOnlyList<CompoundRunConfiguration> CompoundConfigurations => _compoundConfigurations.AsReadOnly();
    public RunConfiguration? ActiveConfiguration => _activeConfiguration;
    public CompoundRunConfiguration? ActiveCompoundConfiguration => _activeCompoundConfiguration;
    public bool IsRunning => (_runningProcess != null && !_runningProcess.HasExited) ||
                             _runningProcesses.Any(p => { try { return !p.HasExited; } catch { return false; } });

    /// <summary>
    /// Load configurations from project
    /// </summary>
    public async Task LoadConfigurationsAsync(string projectPath)
    {
        _configurations.Clear();
        
        // Find all runnable projects
        var projects = await FindRunnableProjectsAsync(projectPath);
        
        foreach (var project in projects)
        {
            var projectName = Path.GetFileNameWithoutExtension(project);
            var projectDir = Path.GetDirectoryName(project) ?? projectPath;
            
            // Read project file to determine output type
            var outputType = await GetProjectOutputTypeAsync(project);
            
            // Create default configurations for each project
            _configurations.Add(new RunConfiguration
            {
                Name = projectName,
                ProjectPath = project,
                WorkingDirectory = projectDir,
                Configuration = "Debug",
                Framework = await GetDefaultFrameworkAsync(project),
                LaunchProfile = null,
                EnvironmentVariables = new Dictionary<string, string>(),
                CommandLineArguments = "",
                OutputType = outputType,
                IsDefault = _configurations.Count == 0
            });
            
            // Also add Release configuration
            _configurations.Add(new RunConfiguration
            {
                Name = $"{projectName} (Release)",
                ProjectPath = project,
                WorkingDirectory = projectDir,
                Configuration = "Release",
                Framework = await GetDefaultFrameworkAsync(project),
                LaunchProfile = null,
                EnvironmentVariables = new Dictionary<string, string>(),
                CommandLineArguments = "",
                OutputType = outputType,
                IsDefault = false
            });
            
            // Load launch profiles if available
            var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
            if (File.Exists(launchSettingsPath))
            {
                await LoadLaunchProfilesAsync(launchSettingsPath, project, projectName);
            }
        }
        
        // Set active configuration to first default
        _activeConfiguration = _configurations.FirstOrDefault(c => c.IsDefault) ?? _configurations.FirstOrDefault();
    }

    /// <summary>
    /// Load launch profiles from launchSettings.json
    /// </summary>
    private async Task LoadLaunchProfilesAsync(string launchSettingsPath, string projectPath, string projectName)
    {
        try
        {
            var json = await File.ReadAllTextAsync(launchSettingsPath);
            var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("profiles", out var profiles))
            {
                foreach (var profile in profiles.EnumerateObject())
                {
                    var profileName = profile.Name;
                    var profileConfig = profile.Value;
                    
                    var config = new RunConfiguration
                    {
                        Name = $"{projectName}: {profileName}",
                        ProjectPath = projectPath,
                        WorkingDirectory = Path.GetDirectoryName(projectPath) ?? "",
                        Configuration = "Debug",
                        LaunchProfile = profileName,
                        EnvironmentVariables = new Dictionary<string, string>(),
                        CommandLineArguments = "",
                        IsDefault = false
                    };
                    
                    // Parse profile settings
                    if (profileConfig.TryGetProperty("commandLineArgs", out var args))
                    {
                        config.CommandLineArguments = args.GetString() ?? "";
                    }
                    
                    if (profileConfig.TryGetProperty("workingDirectory", out var workDir))
                    {
                        config.WorkingDirectory = workDir.GetString() ?? config.WorkingDirectory;
                    }
                    
                    if (profileConfig.TryGetProperty("environmentVariables", out var envVars))
                    {
                        foreach (var envVar in envVars.EnumerateObject())
                        {
                            config.EnvironmentVariables[envVar.Name] = envVar.Value.GetString() ?? "";
                        }
                    }
                    
                    if (profileConfig.TryGetProperty("applicationUrl", out var url))
                    {
                        config.ApplicationUrl = url.GetString();
                    }
                    
                    _configurations.Add(config);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading launch profiles: {ex.Message}");
        }
    }

    /// <summary>
    /// Find all runnable projects (Exe output type)
    /// </summary>
    private async Task<List<string>> FindRunnableProjectsAsync(string path)
    {
        var projects = new List<string>();
        
        if (string.IsNullOrEmpty(path)) return projects;
        
        // If path is a solution, get projects from it
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".sln" || ext == ".slnx")
        {
            var solutionService = new SolutionService();
            var solutionProjects = await solutionService.GetSolutionProjectsAsync(path);
            
            foreach (var project in solutionProjects)
            {
                if (await IsRunnableProjectAsync(project))
                {
                    projects.Add(project);
                }
            }
        }
        else if (ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj")
        {
            if (await IsRunnableProjectAsync(path))
            {
                projects.Add(path);
            }
        }
        else if (Directory.Exists(path))
        {
            // Search directory for solution first
            var slnxFiles = Directory.GetFiles(path, "*.slnx", SearchOption.TopDirectoryOnly);
            var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
            
            if (slnxFiles.Length > 0)
            {
                return await FindRunnableProjectsAsync(slnxFiles[0]);
            }
            if (slnFiles.Length > 0)
            {
                return await FindRunnableProjectsAsync(slnFiles[0]);
            }
            
            // Search for project files
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            foreach (var proj in csprojFiles)
            {
                if (await IsRunnableProjectAsync(proj))
                {
                    projects.Add(proj);
                }
            }
        }
        
        return projects;
    }

    /// <summary>
    /// Check if project is runnable (Exe or WinExe output type)
    /// </summary>
    private async Task<bool> IsRunnableProjectAsync(string projectPath)
    {
        if (!File.Exists(projectPath)) return false;
        
        var outputType = await GetProjectOutputTypeAsync(projectPath);
        return outputType == "Exe" || outputType == "WinExe";
    }

    /// <summary>
    /// Get project output type from csproj
    /// </summary>
    private async Task<string> GetProjectOutputTypeAsync(string projectPath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(projectPath);
            
            // Check for OutputType
            var outputTypeMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<OutputType>(\w+)</OutputType>", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (outputTypeMatch.Success)
            {
                return outputTypeMatch.Groups[1].Value;
            }
            
            // Check for SDK-style project with no OutputType (defaults to Library)
            // But check for common executable SDKs
            if (content.Contains("Microsoft.NET.Sdk.Web") || 
                content.Contains("Microsoft.NET.Sdk.Worker") ||
                content.Contains("Microsoft.NET.Sdk.BlazorWebAssembly"))
            {
                return "Exe";
            }
            
            // Check for WPF/WinForms SDK
            if (content.Contains("Microsoft.NET.Sdk.WindowsDesktop") ||
                content.Contains("UseWPF") || content.Contains("UseWindowsForms"))
            {
                if (content.Contains("<OutputType>"))
                {
                    return outputTypeMatch.Success ? outputTypeMatch.Groups[1].Value : "Library";
                }
            }
            
            return "Library";
        }
        catch
        {
            return "Library";
        }
    }

    /// <summary>
    /// Get default target framework from project
    /// </summary>
    private async Task<string?> GetDefaultFrameworkAsync(string projectPath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(projectPath);
            
            // Check for TargetFramework (single)
            var tfmMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<TargetFramework>([^<]+)</TargetFramework>");
            
            if (tfmMatch.Success)
            {
                return tfmMatch.Groups[1].Value;
            }
            
            // Check for TargetFrameworks (multiple) - use first one
            var tfmsMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<TargetFrameworks>([^<]+)</TargetFrameworks>");
            
            if (tfmsMatch.Success)
            {
                var frameworks = tfmsMatch.Groups[1].Value.Split(';');
                return frameworks.FirstOrDefault();
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Set active configuration
    /// </summary>
    public void SetActiveConfiguration(RunConfiguration config)
    {
        _activeConfiguration = config;
    }

    /// <summary>
    /// Set active configuration by name
    /// </summary>
    public void SetActiveConfiguration(string name)
    {
        _activeConfiguration = _configurations.FirstOrDefault(c => c.Name == name);
    }

    /// <summary>
    /// Run the active configuration
    /// </summary>
    public async Task<RunResult> RunAsync(bool withDebugging = false)
    {
        if (_activeConfiguration == null)
        {
            return new RunResult
            {
                Success = false,
                ErrorMessage = "No run configuration selected"
            };
        }

        return await RunConfigurationAsync(_activeConfiguration, withDebugging);
    }

    /// <summary>
    /// Run a specific configuration
    /// </summary>
    public async Task<RunResult> RunConfigurationAsync(RunConfiguration config, bool withDebugging = false)
    {
        if (IsRunning)
        {
            return new RunResult
            {
                Success = false,
                ErrorMessage = "A process is already running. Stop it first."
            };
        }

        _outputBuffer.Clear();
        _cancellationTokenSource = new CancellationTokenSource();

        RunStarted?.Invoke(this, EventArgs.Empty);
        OnOutput($"========== Run Started: {config.Name} ==========\n");
        OnOutput($"Configuration: {config.Configuration}\n");
        OnOutput($"Project: {config.ProjectPath}\n");
        if (!string.IsNullOrEmpty(config.CommandLineArguments))
        {
            OnOutput($"Arguments: {config.CommandLineArguments}\n");
        }
        OnOutput("\n");

        try
        {
            // Build first if needed
            var buildService = new BuildService();
            buildService.OutputReceived += (s, e) => OnOutput(e.Output);
            
            var buildResult = await buildService.BuildAsync(config.ProjectPath, config.Configuration);
            if (!buildResult.Success)
            {
                OnOutput("\n========== Build Failed ==========\n");
                return new RunResult
                {
                    Success = false,
                    ErrorMessage = "Build failed",
                    Output = _outputBuffer.ToString()
                };
            }

            // Find executable
            var executablePath = FindExecutable(config);
            if (string.IsNullOrEmpty(executablePath))
            {
                // Fall back to dotnet run
                return await RunWithDotnetAsync(config);
            }

            OnOutput($"\nStarting: {executablePath}\n\n");

            // Determine if this is a GUI application
            var isGuiApp = config.OutputType == "WinExe" || IsGuiApplication(config);
            
            // Run the executable
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = config.CommandLineArguments,
                WorkingDirectory = config.WorkingDirectory,
                CreateNoWindow = false
            };

            // Add environment variables
            foreach (var envVar in config.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
            }

            if (isGuiApp)
            {
                // For GUI apps, use ShellExecute so the window appears properly
                startInfo.UseShellExecute = true;
                startInfo.RedirectStandardOutput = false;
                startInfo.RedirectStandardError = false;
                
                OnOutput($"[Info] Running as GUI application (output not captured)\n\n");
            }
            else
            {
                // For console apps, redirect output so we can capture it
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            _runningProcess = new Process { StartInfo = startInfo };

            if (!startInfo.UseShellExecute)
            {
                _runningProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputBuffer.AppendLine(e.Data);
                        OnOutput(e.Data + "\n");
                    }
                };

                _runningProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputBuffer.AppendLine(e.Data);
                        OnOutput($"[stderr] {e.Data}\n");
                    }
                };
            }

            _runningProcess.Start();

            if (!startInfo.UseShellExecute)
            {
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();
            }

            OnOutput($"Process started (PID: {_runningProcess.Id})\n");
            OnOutput("\n========== Application Running ==========\n");

            // Wait for exit
            await _runningProcess.WaitForExitAsync(_cancellationTokenSource.Token);

            var exitCode = _runningProcess.ExitCode;
            var success = exitCode == 0;

            OnOutput($"\n========== Run {(success ? "Completed" : "Failed")} (Exit Code: {exitCode}) ==========\n");

            var result = new RunResult
            {
                Success = success,
                ExitCode = exitCode,
                Output = _outputBuffer.ToString()
            };

            RunCompleted?.Invoke(this, new RunCompletedEventArgs(result));

            return result;
        }
        catch (OperationCanceledException)
        {
            OnOutput("\n========== Run Cancelled ==========\n");
            return new RunResult
            {
                Success = false,
                ErrorMessage = "Run was cancelled",
                Output = _outputBuffer.ToString()
            };
        }
        catch (Exception ex)
        {
            OnOutput($"\n[ERROR] {ex.Message}\n");
            return new RunResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Output = _outputBuffer.ToString()
            };
        }
        finally
        {
            _runningProcess?.Dispose();
            _runningProcess = null;
            RunStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Run using dotnet run command
    /// </summary>
    private async Task<RunResult> RunWithDotnetAsync(RunConfiguration config)
    {
        OnOutput("Running with 'dotnet run'...\n\n");

        var args = new StringBuilder();
        args.Append($"run --project \"{config.ProjectPath}\"");
        args.Append($" --configuration {config.Configuration}");
        
        if (!string.IsNullOrEmpty(config.LaunchProfile))
        {
            args.Append($" --launch-profile \"{config.LaunchProfile}\"");
        }
        
        if (!string.IsNullOrEmpty(config.Framework))
        {
            args.Append($" --framework {config.Framework}");
        }

        if (!string.IsNullOrEmpty(config.CommandLineArguments))
        {
            args.Append($" -- {config.CommandLineArguments}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args.ToString(),
            WorkingDirectory = config.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var envVar in config.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
        }

        _runningProcess = new Process { StartInfo = startInfo };

        _runningProcess.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _outputBuffer.AppendLine(e.Data);
                OnOutput(e.Data + "\n");
            }
        };

        _runningProcess.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _outputBuffer.AppendLine(e.Data);
                OnOutput($"[stderr] {e.Data}\n");
            }
        };

        _runningProcess.Start();
        _runningProcess.BeginOutputReadLine();
        _runningProcess.BeginErrorReadLine();

        OnOutput($"Process started (PID: {_runningProcess.Id})\n");

        await _runningProcess.WaitForExitAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);

        var exitCode = _runningProcess.ExitCode;
        var success = exitCode == 0;

        OnOutput($"\n========== Run {(success ? "Completed" : "Failed")} (Exit Code: {exitCode}) ==========\n");

        return new RunResult
        {
            Success = success,
            ExitCode = exitCode,
            Output = _outputBuffer.ToString()
        };
    }

    /// <summary>
    /// Find the executable for a configuration
    /// </summary>
    private string? FindExecutable(RunConfiguration config)
    {
        var projectDir = Path.GetDirectoryName(config.ProjectPath);
        var projectName = Path.GetFileNameWithoutExtension(config.ProjectPath);
        var outputDir = Path.Combine(projectDir!, "bin", config.Configuration);

        if (!Directory.Exists(outputDir)) return null;

        // Search in framework subdirectories
        foreach (var frameworkDir in Directory.GetDirectories(outputDir))
        {
            // Check for .exe
            var exePath = Path.Combine(frameworkDir, $"{projectName}.exe");
            if (File.Exists(exePath))
            {
                return exePath;
            }

            // For cross-platform, check for publish folder
            var publishDir = Path.Combine(frameworkDir, "publish");
            if (Directory.Exists(publishDir))
            {
                exePath = Path.Combine(publishDir, $"{projectName}.exe");
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Check if application is a GUI app (Avalonia, WPF, WinForms)
    /// </summary>
    private bool IsGuiApplication(RunConfiguration config)
    {
        try
        {
            var content = File.ReadAllText(config.ProjectPath);
            return content.Contains("Avalonia") ||
                   content.Contains("UseWPF") ||
                   content.Contains("UseWindowsForms") ||
                   content.Contains("WPF") ||
                   content.Contains("WindowsForms");
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Add a new configuration
    /// </summary>
    public void AddConfiguration(RunConfiguration config)
    {
        _configurations.Add(config);
    }

    /// <summary>
    /// Remove a configuration
    /// </summary>
    public void RemoveConfiguration(RunConfiguration config)
    {
        _configurations.Remove(config);
        if (_activeConfiguration == config)
        {
            _activeConfiguration = _configurations.FirstOrDefault();
        }
    }

    /// <summary>
    /// Add a compound run configuration
    /// </summary>
    public void AddCompoundConfiguration(CompoundRunConfiguration compound)
    {
        _compoundConfigurations.Add(compound);
    }

    /// <summary>
    /// Remove a compound run configuration
    /// </summary>
    public void RemoveCompoundConfiguration(CompoundRunConfiguration compound)
    {
        _compoundConfigurations.Remove(compound);
        if (_activeCompoundConfiguration == compound)
            _activeCompoundConfiguration = _compoundConfigurations.FirstOrDefault();
    }

    /// <summary>
    /// Set active compound configuration
    /// </summary>
    public void SetActiveCompoundConfiguration(CompoundRunConfiguration compound)
    {
        _activeCompoundConfiguration = compound;
        _activeConfiguration = null; // clear single
    }

    /// <summary>
    /// Run all configurations in a compound configuration simultaneously
    /// </summary>
    public async Task<CompoundRunResult> RunCompoundAsync(CompoundRunConfiguration compound)
    {
        if (IsRunning)
        {
            return new CompoundRunResult
            {
                Success = false,
                ErrorMessage = "A process is already running. Stop it first."
            };
        }

        _outputBuffer.Clear();
        _cancellationTokenSource = new CancellationTokenSource();
        _runningProcesses.Clear();

        RunStarted?.Invoke(this, EventArgs.Empty);
        OnOutput($"╔══════════════════════════════════════════════════════════╗\n");
        OnOutput($"║  🚀 Compound Run: {compound.Name,-41}║\n");
        OnOutput($"╚══════════════════════════════════════════════════════════╝\n");
        OnOutput($"Starting {compound.Configurations.Count} project(s) simultaneously...\n\n");

        var results = new List<(string Name, RunResult Result)>();
        var token = _cancellationTokenSource.Token;

        try
        {
            if (compound.StartSequentially)
            {
                // Start one by one with optional delay
                foreach (var configName in compound.Configurations)
                {
                    var config = _configurations.FirstOrDefault(c =>
                        c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase));
                    if (config == null)
                    {
                        OnOutput($"[WARN] Configuration not found: {configName}\n");
                        continue;
                    }

                    OnOutput($"▶ Starting: {config.Name}\n");
                    var r = await StartSingleProcessAsync(config, token);
                    results.Add((config.Name, r));

                    if (!r.Success && compound.StopOnFailure)
                    {
                        OnOutput($"\n[ERROR] Stopping compound run because '{config.Name}' failed.\n");
                        break;
                    }

                    if (compound.DelayBetweenStartsMs > 0)
                        await Task.Delay(compound.DelayBetweenStartsMs, token);
                }
            }
            else
            {
                // Start all in parallel
                var tasks = new List<Task<(string, RunResult)>>();

                foreach (var configName in compound.Configurations)
                {
                    var config = _configurations.FirstOrDefault(c =>
                        c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase));
                    if (config == null)
                    {
                        OnOutput($"[WARN] Configuration not found: {configName}\n");
                        continue;
                    }

                    var capturedConfig = config;
                    tasks.Add(Task.Run(async () =>
                    {
                        OnOutput($"▶ Starting: {capturedConfig.Name}\n");
                        var r = await StartSingleProcessAsync(capturedConfig, token);
                        return (capturedConfig.Name, r);
                    }, token));
                }

                var allResults = await Task.WhenAll(tasks);
                results.AddRange(allResults);
            }

            // Wait for all processes to finish (if not GUI)
            var waitTasks = _runningProcesses
                .Where(p => { try { return !p.HasExited; } catch { return false; } })
                .Select(p => p.WaitForExitAsync(token))
                .ToList();

            if (waitTasks.Count > 0)
                await Task.WhenAll(waitTasks);

            var allSuccess = results.All(r => r.Result.Success);
            OnOutput($"\n══════════════════════════════════════════════════════════\n");
            OnOutput($"  Compound Run {(allSuccess ? "✅ Completed" : "❌ Finished with errors")}\n");
            foreach (var (name, res) in results)
            {
                var icon = res.Success ? "✅" : "❌";
                OnOutput($"  {icon} {name} (exit code: {res.ExitCode})\n");
            }
            OnOutput($"══════════════════════════════════════════════════════════\n");

            var compoundResult = new CompoundRunResult
            {
                Success = allSuccess,
                Results = results.ToDictionary(r => r.Name, r => r.Result)
            };

            RunCompleted?.Invoke(this, new RunCompletedEventArgs(new RunResult
            {
                Success = allSuccess,
                Output = _outputBuffer.ToString()
            }));

            return compoundResult;
        }
        catch (OperationCanceledException)
        {
            OnOutput("\n══════ Compound Run Cancelled ══════\n");
            return new CompoundRunResult { Success = false, ErrorMessage = "Compound run was cancelled." };
        }
        catch (Exception ex)
        {
            OnOutput($"\n[ERROR] {ex.Message}\n");
            return new CompoundRunResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            foreach (var p in _runningProcesses)
            {
                try { p.Dispose(); } catch { }
            }
            _runningProcesses.Clear();
            _runningProcess = null;
            RunStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Start a single process for a run configuration (used inside compound runs)
    /// </summary>
    private async Task<RunResult> StartSingleProcessAsync(RunConfiguration config, CancellationToken token)
    {
        try
        {
            OnOutput($"\n┌─── {config.Name} ───\n");

            // Build
            var buildService = new BuildService();
            buildService.OutputReceived += (s, e) => OnOutput($"  [build] {e.Output}");
            var buildResult = await buildService.BuildAsync(config.ProjectPath, config.Configuration);
            if (!buildResult.Success)
            {
                OnOutput($"└─── ❌ Build failed: {config.Name}\n");
                return new RunResult { Success = false, ErrorMessage = "Build failed", ExitCode = -1 };
            }

            // Find executable or use dotnet run
            var exePath = FindExecutable(config);

            ProcessStartInfo startInfo;
            if (!string.IsNullOrEmpty(exePath))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = config.CommandLineArguments,
                    WorkingDirectory = config.WorkingDirectory,
                    CreateNoWindow = false,
                    UseShellExecute = true
                };

                // For console apps capture output
                if (config.OutputType != "WinExe" && !IsGuiApplication(config))
                {
                    startInfo.UseShellExecute = false;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.CreateNoWindow = true;
                    startInfo.StandardOutputEncoding = Encoding.UTF8;
                    startInfo.StandardErrorEncoding = Encoding.UTF8;
                }
            }
            else
            {
                // dotnet run
                var args = new StringBuilder();
                args.Append($"run --project \"{config.ProjectPath}\" --configuration {config.Configuration}");
                if (!string.IsNullOrEmpty(config.Framework))
                    args.Append($" --framework {config.Framework}");
                if (!string.IsNullOrEmpty(config.LaunchProfile))
                    args.Append($" --launch-profile \"{config.LaunchProfile}\"");
                if (!string.IsNullOrEmpty(config.CommandLineArguments))
                    args.Append($" -- {config.CommandLineArguments}");

                startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args.ToString(),
                    WorkingDirectory = config.WorkingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }

            foreach (var env in config.EnvironmentVariables)
                startInfo.EnvironmentVariables[env.Key] = env.Value;

            var process = new Process { StartInfo = startInfo };
            lock (_runningProcesses) { _runningProcesses.Add(process); }

            if (startInfo.RedirectStandardOutput)
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _outputBuffer.AppendLine(e.Data);
                        OnOutput($"  [{config.Name}] {e.Data}\n");
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        OnOutput($"  [{config.Name}][err] {e.Data}\n");
                };
            }

            process.Start();

            if (startInfo.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            OnOutput($"  PID {process.Id} started\n");

            // Wait until done or cancelled
            await process.WaitForExitAsync(token);

            var exit = process.ExitCode;
            OnOutput($"└─── {(exit == 0 ? "✅" : "❌")} {config.Name} exited ({exit})\n");

            return new RunResult { Success = exit == 0, ExitCode = exit };
        }
        catch (OperationCanceledException)
        {
            return new RunResult { Success = false, ErrorMessage = "Cancelled", ExitCode = -1 };
        }
        catch (Exception ex)
        {
            OnOutput($"  [ERROR] {ex.Message}\n");
            return new RunResult { Success = false, ErrorMessage = ex.Message, ExitCode = -1 };
        }
    }

    /// <summary>
    /// Stop all running processes (single and compound)
    /// </summary>
    public void Stop()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                _runningProcess.Kill(entireProcessTree: true);
                OnOutput("\n========== Process Stopped ==========\n");
            }

            lock (_runningProcesses)
            {
                foreach (var p in _runningProcesses)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch { }
                }
            }

            if (_runningProcesses.Count > 0)
                OnOutput("\n══════ All processes stopped ══════\n");
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Failed to stop processes: {ex.Message}\n");
        }
    }

    private void OnOutput(string message)
    {
        OutputReceived?.Invoke(this, new RunOutputEventArgs(message));
    }
}

/// <summary>
/// Compound run configuration — runs multiple RunConfigurations simultaneously (like JetBrains Rider Compound)
/// </summary>
public class CompoundRunConfiguration
{
    public string Name { get; set; } = "Compound";

    /// <summary>Names of RunConfiguration entries to include</summary>
    public List<string> Configurations { get; set; } = new();

    /// <summary>Start processes one after another instead of simultaneously</summary>
    public bool StartSequentially { get; set; } = false;

    /// <summary>Stop the whole compound run if one process fails (only relevant when StartSequentially = true)</summary>
    public bool StopOnFailure { get; set; } = false;

    /// <summary>Delay in milliseconds between sequential starts (ignored when parallel)</summary>
    public int DelayBetweenStartsMs { get; set; } = 0;

    public override string ToString() => $"⚡ {Name}";
}

/// <summary>
/// Result of a compound run
/// </summary>
public class CompoundRunResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, RunResult> Results { get; set; } = new();
}

/// <summary>
/// Run configuration model
/// </summary>
public class RunConfiguration
{
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Configuration { get; set; } = "Debug";
    public string? Framework { get; set; }
    public string? LaunchProfile { get; set; }
    public string? ApplicationUrl { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public string CommandLineArguments { get; set; } = "";
    public string OutputType { get; set; } = "Exe";
    public bool IsDefault { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// Run result information
/// </summary>
public class RunResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event args for run output
/// </summary>
public class RunOutputEventArgs : EventArgs
{
    public string Output { get; }

    public RunOutputEventArgs(string output)
    {
        Output = output;
    }
}

/// <summary>
/// Event args for run completed
/// </summary>
public class RunCompletedEventArgs : EventArgs
{
    public RunResult Result { get; }

    public RunCompletedEventArgs(RunResult result)
    {
        Result = result;
    }
}

