using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Solution format type
/// </summary>
public enum SolutionFormat
{
    /// <summary>
    /// Classic .sln format (Visual Studio legacy)
    /// </summary>
    Sln,
    
    /// <summary>
    /// Modern .slnx XML-based format
    /// </summary>
    Slnx
}

/// <summary>
/// Service for managing solutions and projects
/// </summary>
public class SolutionService
{
    /// <summary>
    /// Default solution format for new solutions
    /// </summary>
    public SolutionFormat DefaultFormat { get; set; } = SolutionFormat.Slnx;

    /// <summary>
    /// Create a new empty solution
    /// </summary>
    public async Task<string?> CreateSolutionAsync(string location, string solutionName, bool createDirectory = true, bool initGit = false, SolutionFormat? format = null)
    {
        var solutionFormat = format ?? DefaultFormat;
        
        try
        {
            var solutionDir = createDirectory ? Path.Combine(location, solutionName) : location;
            Directory.CreateDirectory(solutionDir);

            string solutionFilePath;
            
            if (solutionFormat == SolutionFormat.Slnx)
            {
                // Create modern .slnx format directly
                solutionFilePath = Path.Combine(solutionDir, $"{solutionName}.slnx");
                await CreateEmptySlnxFileAsync(solutionFilePath, solutionName);
            }
            else
            {
                // Create legacy .sln format manually (dotnet new sln creates .slnx in .NET 10+)
                solutionFilePath = Path.Combine(solutionDir, $"{solutionName}.sln");
                await CreateEmptySlnFileAsync(solutionFilePath, solutionName);
            }

            if (initGit)
            {
                await InitializeGitAsync(solutionDir);
            }

            return solutionFilePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating solution: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create an empty slnx file with modern XML format
    /// </summary>
    private async Task CreateEmptySlnxFileAsync(string filePath, string _ = "")
    {
        // Write clean XML with no comments so XDocument.Parse works reliably later
        var content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Solution>\r\n</Solution>\r\n";
        await File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);
        Debug.WriteLine($"Created empty .slnx file: {filePath}");
    }

    /// <summary>
    /// Create an empty .sln file with proper Visual Studio format
    /// </summary>
    private async Task CreateEmptySlnFileAsync(string filePath, string _ = "")
    {
        var slnContent = "\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n" +
                         "# Visual Studio Version 17\r\n" +
                         "VisualStudioVersion = 17.0.31903.59\r\n" +
                         "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
                         "Global\r\n" +
                         "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
                         "\t\tDebug|Any CPU = Debug|Any CPU\r\n" +
                         "\t\tRelease|Any CPU = Release|Any CPU\r\n" +
                         "\tEndGlobalSection\r\n" +
                         "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
                         "\tEndGlobalSection\r\n" +
                         "\tGlobalSection(SolutionProperties) = preSolution\r\n" +
                         "\t\tHideSolutionNode = FALSE\r\n" +
                         "\tEndGlobalSection\r\n" +
                         "EndGlobal\r\n";
        await File.WriteAllTextAsync(filePath, slnContent, System.Text.Encoding.UTF8);
        Debug.WriteLine($"Created empty .sln file: {filePath}");
    }

    /// <summary>
    /// Create a new project and optionally add to solution
    /// </summary>
    public async Task<string?> CreateProjectAsync(string location, string projectName, string template, string? solutionPath = null)
    {
        try
        {
            var projectDir = Path.Combine(location, projectName);
            Directory.CreateDirectory(projectDir);

            var templateName = GetTemplateShortName(template);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"new {templateName} -n \"{projectName}\" -o \"{projectDir}\"",
                    WorkingDirectory = location,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Failed to create project: {error}");
            }

            var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");

            // Add to solution if specified
            if (!string.IsNullOrEmpty(solutionPath) && File.Exists(solutionPath))
            {
                await AddProjectToSolutionAsync(solutionPath, projectPath);
            }

            return projectPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating project: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Add an existing project to a solution (supports both .sln and .slnx)
    /// </summary>
    public async Task<bool> AddProjectToSolutionAsync(string solutionPath, string projectPath, string? solutionFolder = null)
    {
        try
        {
            var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
            
            if (ext == ".slnx")
            {
                return await AddProjectToSlnxAsync(solutionPath, projectPath, solutionFolder);
            }
            else
            {
                return await AddProjectToSlnAsync(solutionPath, projectPath, solutionFolder);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding project to solution: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add project to slnx file (XML format) — writes directly to text to avoid XmlWriter flush issues
    /// </summary>
    private async Task<bool> AddProjectToSlnxAsync(string solutionPath, string projectPath, string? solutionFolder = null)
    {
        try
        {
            Debug.WriteLine($"=== AddProjectToSlnxAsync ===");
            Debug.WriteLine($"solutionPath: {solutionPath}");
            Debug.WriteLine($"projectPath: {projectPath}");

            var solutionDir = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDir)) return false;

            // Compute forward-slash relative path
            var relativePath = Path.GetRelativePath(solutionDir, projectPath).Replace('\\', '/');
            Debug.WriteLine($"relativePath: {relativePath}");

            // Read current content
            var content = File.Exists(solutionPath)
                ? await File.ReadAllTextAsync(solutionPath, System.Text.Encoding.UTF8)
                : "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Solution>\r\n</Solution>\r\n";

            // Check if project is already referenced
            if (content.Contains($"Path=\"{relativePath}\"", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("Project already exists in slnx");
                return true;
            }

            // Build the <Project Path="..." /> line
            var projectLine = $"  <Project Path=\"{relativePath}\" />";

            // Insert before </Solution>
            var insertAt = content.LastIndexOf("</Solution>", StringComparison.OrdinalIgnoreCase);
            if (insertAt < 0)
            {
                // Malformed file — rebuild
                content = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Solution>\r\n{projectLine}\r\n</Solution>\r\n";
            }
            else
            {
                content = content.Insert(insertAt, projectLine + "\r\n");
            }

            await File.WriteAllTextAsync(solutionPath, content, System.Text.Encoding.UTF8);
            Debug.WriteLine($"Saved slnx content:\n{content}");
            Debug.WriteLine($"Successfully added project to slnx: {relativePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding project to slnx: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Add project to sln file (legacy format) — writes manually since dotnet sln add is broken in .NET 10+
    /// </summary>
    private async Task<bool> AddProjectToSlnAsync(string solutionPath, string projectPath, string? solutionFolder = null)
    {
        try
        {
            Debug.WriteLine($"=== AddProjectToSlnAsync ===");
            Debug.WriteLine($"solutionPath: {solutionPath}");
            Debug.WriteLine($"projectPath: {projectPath}");

            var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            // Relative path with backslashes (standard .sln format)
            var relativePath = Path.GetRelativePath(solutionDir, projectPath);
            Debug.WriteLine($"relativePath: {relativePath}");

            var content = File.Exists(solutionPath)
                ? await File.ReadAllTextAsync(solutionPath, System.Text.Encoding.UTF8)
                : string.Empty;

            // Check if already referenced
            if (content.Contains(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("Project already exists in sln");
                return true;
            }

            // Generate a new GUID for the project
            var projectGuid = Guid.NewGuid().ToString("B").ToUpper();
            // C# project type GUID
            const string csharpTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

            // Project block to insert before "Global"
            var projectBlock =
                $"Project(\"{csharpTypeGuid}\") = \"{projectName}\", \"{relativePath}\", \"{projectGuid}\"\r\n" +
                $"EndProject\r\n";

            // Insert project block before "Global" section
            var globalIdx = content.IndexOf("\r\nGlobal", StringComparison.OrdinalIgnoreCase);
            if (globalIdx < 0)
                globalIdx = content.IndexOf("\nGlobal", StringComparison.OrdinalIgnoreCase);

            if (globalIdx >= 0)
            {
                content = content.Insert(globalIdx + 2, projectBlock);
            }
            else
            {
                // Append before end if no Global section found
                content += projectBlock;
            }

            // Also add project configuration mappings inside GlobalSection(ProjectConfigurationPlatforms)
            var configSection = "\t\t" + projectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n" +
                                "\t\t" + projectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU\r\n" +
                                "\t\t" + projectGuid + ".Release|Any CPU.ActiveCfg = Release|Any CPU\r\n" +
                                "\t\t" + projectGuid + ".Release|Any CPU.Build.0 = Release|Any CPU\r\n";

            var postSolutionIdx = content.IndexOf("GlobalSection(ProjectConfigurationPlatforms) = postSolution", StringComparison.OrdinalIgnoreCase);
            if (postSolutionIdx >= 0)
            {
                var endSectionIdx = content.IndexOf("EndGlobalSection", postSolutionIdx, StringComparison.OrdinalIgnoreCase);
                if (endSectionIdx >= 0)
                {
                    content = content.Insert(endSectionIdx, configSection);
                }
            }
            else
            {
                // Add ProjectConfigurationPlatforms section before EndGlobal
                var endGlobalIdx = content.IndexOf("EndGlobal", StringComparison.OrdinalIgnoreCase);
                if (endGlobalIdx >= 0)
                {
                    var section = "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
                                  configSection +
                                  "\tEndGlobalSection\r\n";
                    content = content.Insert(endGlobalIdx, section);
                }
            }

            await File.WriteAllTextAsync(solutionPath, content, System.Text.Encoding.UTF8);
            Debug.WriteLine($"Successfully added project to sln: {relativePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding project to sln: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove a project from a solution (supports both .sln and .slnx)
    /// </summary>
    public async Task<bool> RemoveProjectFromSolutionAsync(string solutionPath, string projectPath)
    {
        try
        {
            var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
            
            if (ext == ".slnx")
            {
                return await RemoveProjectFromSlnxAsync(solutionPath, projectPath);
            }
            else
            {
                return await RemoveProjectFromSlnAsync(solutionPath, projectPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing project from solution: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove project from slnx file
    /// </summary>
    private async Task<bool> RemoveProjectFromSlnxAsync(string solutionPath, string projectPath)
    {
        try
        {
            var solutionDir = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDir) || !File.Exists(solutionPath)) return false;

            var relativePath = Path.GetRelativePath(solutionDir, projectPath).Replace('\\', '/');

            var lines = (await File.ReadAllLinesAsync(solutionPath, System.Text.Encoding.UTF8)).ToList();
            var removed = lines.RemoveAll(l =>
                l.Contains($"Path=\"{relativePath}\"", StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                await File.WriteAllLinesAsync(solutionPath, lines, System.Text.Encoding.UTF8);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing project from slnx: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove project from sln file — removes Project block by project path
    /// </summary>
    private async Task<bool> RemoveProjectFromSlnAsync(string solutionPath, string projectPath)
    {
        try
        {
            if (!File.Exists(solutionPath)) return false;
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
            var relativePath = Path.GetRelativePath(solutionDir, projectPath);

            var content = await File.ReadAllTextAsync(solutionPath, System.Text.Encoding.UTF8);

            // Remove the Project...EndProject block that references this path
            var pattern = @"Project\([^)]+\)\s*=\s*""[^""]*""\s*,\s*""" 
                          + Regex.Escape(relativePath) 
                          + @"""\s*,\s*""([^""]+)""\s*\r?\nEndProject";
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);

            string? projectGuid = null;
            if (match.Success)
            {
                projectGuid = match.Groups[1].Value;
                content = content.Remove(match.Index, match.Length).TrimStart('\r', '\n');
                // Ensure there's a newline before Global
                if (!content.StartsWith("\r\n") && !content.StartsWith("\n"))
                    content = "\r\n" + content;
            }

            // Remove config entries for this project GUID
            if (projectGuid != null)
            {
                var lines = content.Split('\n').ToList();
                lines.RemoveAll(l => l.Contains(projectGuid, StringComparison.OrdinalIgnoreCase));
                content = string.Join('\n', lines);
            }

            await File.WriteAllTextAsync(solutionPath, content, System.Text.Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing project from sln: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Find solution file in directory (supports .slnx and .sln)
    /// </summary>
    public string? FindSolutionFile(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        // First look for .slnx files (new format)
        var slnxFiles = Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly);
        if (slnxFiles.Length > 0)
            return slnxFiles[0];

        // Then look for .sln files (legacy format)
        var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length > 0)
            return slnFiles[0];

        return null;
    }

    /// <summary>
    /// Check if file is a solution file (.sln or .slnx)
    /// </summary>
    public static bool IsSolutionFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".sln" || ext == ".slnx";
    }

    /// <summary>
    /// Get list of projects in a solution (supports both .sln and .slnx)
    /// </summary>
    public async Task<List<string>> GetSolutionProjectsAsync(string solutionPath)
    {
        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
        
        if (ext == ".slnx")
        {
            return await GetSlnxProjectsAsync(solutionPath);
        }
        else
        {
            return await GetSlnProjectsAsync(solutionPath);
        }
    }

    /// <summary>
    /// Get projects from slnx file
    /// </summary>
    private async Task<List<string>> GetSlnxProjectsAsync(string solutionPath)
    {
        var projects = new List<string>();

        try
        {
            var solutionDir = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDir) || !File.Exists(solutionPath))
                return projects;

            var content = await File.ReadAllTextAsync(solutionPath);
            var doc = XDocument.Parse(content);
            var root = doc.Root;
            if (root == null) return projects;

            // Get projects from root level
            foreach (var projectElement in root.Elements("Project"))
            {
                var pathAttr = projectElement.Attribute("Path")?.Value;
                if (!string.IsNullOrEmpty(pathAttr))
                {
                    var projectFullPath = Path.GetFullPath(Path.Combine(solutionDir, pathAttr.Replace("/", "\\")));
                    if (File.Exists(projectFullPath))
                    {
                        projects.Add(projectFullPath);
                    }
                }
            }

            // Get projects from folders
            foreach (var folder in root.Elements("Folder"))
            {
                foreach (var projectElement in folder.Elements("Project"))
                {
                    var pathAttr = projectElement.Attribute("Path")?.Value;
                    if (!string.IsNullOrEmpty(pathAttr))
                    {
                        var projectFullPath = Path.GetFullPath(Path.Combine(solutionDir, pathAttr.Replace("/", "\\")));
                        if (File.Exists(projectFullPath))
                        {
                            projects.Add(projectFullPath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting slnx projects: {ex.Message}");
        }

        return projects;
    }

    /// <summary>
    /// Get projects from sln file using dotnet CLI
    /// </summary>
    private async Task<List<string>> GetSlnProjectsAsync(string solutionPath)
    {
        var projects = new List<string>();

        try
        {
            var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"sln \"{solutionPath}\" list",
                    WorkingDirectory = solutionDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var foundProjects = false;

                foreach (var line in lines)
                {
                    if (line.Contains("---"))
                    {
                        foundProjects = true;
                        continue;
                    }

                    if (foundProjects && !string.IsNullOrWhiteSpace(line))
                    {
                        var projectRelativePath = line.Trim();
                        var projectFullPath = Path.GetFullPath(Path.Combine(solutionDir, projectRelativePath));
                        projects.Add(projectFullPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting solution projects: {ex.Message}");
        }

        return projects;
    }

    /// <summary>
    /// Initialize a Git repository
    /// </summary>
    public async Task<bool> InitializeGitAsync(string directory)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "init",
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // Create .gitignore
                var gitignorePath = Path.Combine(directory, ".gitignore");
                await File.WriteAllTextAsync(gitignorePath, GetDotNetGitIgnore());
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing git: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add a reference between projects
    /// </summary>
    public async Task<bool> AddProjectReferenceAsync(string projectPath, string referenceProjectPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"add \"{projectPath}\" reference \"{referenceProjectPath}\"",
                    WorkingDirectory = projectDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding project reference: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add a NuGet package to a project
    /// </summary>
    public async Task<bool> AddPackageAsync(string projectPath, string packageName, string? version = null)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;
            var args = $"add \"{projectPath}\" package {packageName}";
            if (!string.IsNullOrEmpty(version))
            {
                args += $" --version {version}";
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = args,
                    WorkingDirectory = projectDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding package: {ex.Message}");
            return false;
        }
    }

    private string GetTemplateShortName(string template)
    {
        return template.ToLowerInvariant() switch
        {
            "console" => "console",
            "classlib" => "classlib",
            "avalonia" => "avalonia.app",
            "webapi" => "webapi",
            "xunit" => "xunit",
            "nunit" => "nunit",
            "mstest" => "mstest",
            "wpf" => "wpf",
            "winforms" => "winforms",
            "blazorserver" => "blazorserver",
            "blazorwasm" => "blazorwasm",
            "worker" => "worker",
            "grpc" => "grpc",
            "razor" => "razor",
            "mvc" => "mvc",
            _ => template
        };
    }

    private static string GetDotNetGitIgnore()
    {
        return @"## .NET
bin/
obj/
*.user
*.suo
*.userosscache
*.sln.docstates

## Visual Studio
.vs/
*.rsuser
*.vspscc
*.vssscc
.builds

## JetBrains Rider
.idea/
*.sln.iml

## User-specific files
*.userprefs

## Build results
[Dd]ebug/
[Rr]elease/
x64/
x86/

## NuGet
packages/
*.nupkg
project.lock.json
project.fragment.lock.json
artifacts/

## Test results
[Tt]est[Rr]esult*/
*.trx
coverage/
";
    }
}
