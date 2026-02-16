using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for detecting and analyzing Git repositories using Git CLI (GitForWindows)
/// </summary>
public class GitDetectionService : IDisposable
{
    private string? _repositoryPath;
    private bool _disposed;
    private static string? _gitExecutablePath;

    /// <summary>
    /// Event fired when a repository is detected
    /// </summary>
    public event EventHandler<GitRepositoryDetectedEventArgs>? RepositoryDetected;

    /// <summary>
    /// Event fired when repository status changes
    /// </summary>
    public event EventHandler<GitRepositoryStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Gets whether a repository is currently open
    /// </summary>
    public bool IsRepositoryOpen => !string.IsNullOrEmpty(_repositoryPath) && 
                                     Directory.Exists(Path.Combine(_repositoryPath, ".git"));

    /// <summary>
    /// Gets the current repository path
    /// </summary>
    public string? RepositoryPath => _repositoryPath;

    #region Git Executable Detection

    /// <summary>
    /// Gets or finds the Git executable path
    /// </summary>
    public static string GitExecutablePath
    {
        get
        {
            if (string.IsNullOrEmpty(_gitExecutablePath))
            {
                _gitExecutablePath = FindGitExecutable();
            }
            return _gitExecutablePath ?? "git";
        }
        set => _gitExecutablePath = value;
    }

    /// <summary>
    /// Finds the Git executable from various locations
    /// </summary>
    private static string? FindGitExecutable()
    {
        // Try common Git installation paths on Windows
        var possiblePaths = new List<string>
        {
            // GitForWindows NuGet package paths (in output directory)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "git", "bin", "git.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "git", "cmd", "git.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win-x64", "native", "git.exe"),
            
            // Standard Git for Windows installation paths
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\bin\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            
            // Portable Git
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Programs", "Git", "bin", "git.exe"),
            
            // Scoop
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                "scoop", "apps", "git", "current", "bin", "git.exe"),
            
            // Chocolatey
            @"C:\ProgramData\chocolatey\bin\git.exe",
        };

        // Check environment variable
        var gitFromEnv = Environment.GetEnvironmentVariable("GIT_PATH");
        if (!string.IsNullOrEmpty(gitFromEnv))
        {
            possiblePaths.Insert(0, gitFromEnv);
        }

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Try to find git in PATH
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                    {
                        return firstLine;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    /// <summary>
    /// Checks if Git is available
    /// </summary>
    public static async Task<bool> IsGitAvailableAsync()
    {
        try
        {
            var result = await RunGitCommandStaticAsync("--version", null);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the Git version
    /// </summary>
    public static async Task<string> GetGitVersionAsync()
    {
        var result = await RunGitCommandStaticAsync("--version", null);
        return result.Success ? result.Output.Trim() : "Git not found";
    }

    #endregion

    #region Repository Detection

    /// <summary>
    /// Detects if the given path is inside a Git repository and returns info about it
    /// </summary>
    public async Task<GitDetectionResult> DetectRepositoryAsync(string path)
    {
        var result = new GitDetectionResult { SearchedPath = path };

        if (string.IsNullOrEmpty(path))
        {
            result.Error = "Path cannot be null or empty";
            return result;
        }

        try
        {
            // Normalize path
            path = Path.GetFullPath(path);

            // If it's a file, get its directory
            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path) ?? path;
            }

            if (!Directory.Exists(path))
            {
                result.Error = "Directory does not exist";
                return result;
            }

            // Try to find repository root
            var repoRootResult = await RunGitCommandAsync("rev-parse --show-toplevel", path);
            
            if (!repoRootResult.Success)
            {
                result.IsRepository = false;
                return result;
            }

            result.IsRepository = true;
            result.WorkingDirectory = repoRootResult.Output.Trim().Replace("/", "\\");
            result.RepositoryPath = Path.Combine(result.WorkingDirectory, ".git");

            // Get current branch
            var branchResult = await RunGitCommandAsync("rev-parse --abbrev-ref HEAD", result.WorkingDirectory);
            result.CurrentBranch = branchResult.Success ? branchResult.Output.Trim() : "HEAD";

            // Check if HEAD is detached
            var symbolicRef = await RunGitCommandAsync("symbolic-ref -q HEAD", result.WorkingDirectory);
            result.IsHeadDetached = !symbolicRef.Success;

            // Check if HEAD is unborn (no commits yet)
            var headCheck = await RunGitCommandAsync("rev-parse HEAD", result.WorkingDirectory);
            result.IsHeadUnborn = !headCheck.Success;

            // Check if bare repository
            var isBareResult = await RunGitCommandAsync("rev-parse --is-bare-repository", result.WorkingDirectory);
            result.IsBare = isBareResult.Success && isBareResult.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            // Get remote info
            var remoteResult = await RunGitCommandAsync("remote get-url origin", result.WorkingDirectory);
            if (remoteResult.Success)
            {
                result.RemoteUrl = remoteResult.Output.Trim();
                result.RemoteName = "origin";
            }

            // Get status counts using porcelain format
            var statusResult = await RunGitCommandAsync("status --porcelain=v1 -uall", result.WorkingDirectory);
            if (statusResult.Success)
            {
                var lines = statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Length < 2) continue;
                    
                    var indexStatus = line[0];
                    var workTreeStatus = line[1];

                    // Staged changes
                    if (indexStatus != ' ' && indexStatus != '?')
                    {
                        result.StagedCount++;
                        if (indexStatus == 'A') result.AddedCount++;
                        else if (indexStatus == 'D') result.DeletedCount++;
                        else if (indexStatus == 'M') result.ModifiedCount++;
                    }

                    // Unstaged changes
                    if (workTreeStatus != ' ')
                    {
                        if (indexStatus == '?') result.UntrackedCount++;
                        else if (workTreeStatus == 'D') result.DeletedCount++;
                        else if (workTreeStatus == 'M') result.ModifiedCount++;
                    }
                }
            }

            // Raise event
            RepositoryDetected?.Invoke(this, new GitRepositoryDetectedEventArgs(result));

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Opens a repository for operations
    /// </summary>
    public async Task<bool> OpenRepositoryAsync(string path)
    {
        try
        {
            var result = await DetectRepositoryAsync(path);
            if (result.IsRepository && !string.IsNullOrEmpty(result.WorkingDirectory))
            {
                _repositoryPath = result.WorkingDirectory;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes the current repository
    /// </summary>
    public void CloseRepository()
    {
        _repositoryPath = null;
    }

    /// <summary>
    /// Scans a directory recursively for Git repositories
    /// </summary>
    public async Task<List<GitDetectionResult>> ScanForRepositoriesAsync(string rootPath, int maxDepth = 5)
    {
        var results = new List<GitDetectionResult>();
        var visitedRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await ScanDirectoryAsync(rootPath, 0, maxDepth, results, visitedRepos);

        return results;
    }

    private async Task ScanDirectoryAsync(string path, int currentDepth, int maxDepth,
        List<GitDetectionResult> results, HashSet<string> visitedRepos)
    {
        if (currentDepth > maxDepth) return;

        try
        {
            var gitFolder = Path.Combine(path, ".git");
            if (Directory.Exists(gitFolder) || File.Exists(gitFolder))
            {
                var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
                if (!visitedRepos.Contains(normalizedPath))
                {
                    visitedRepos.Add(normalizedPath);
                    var result = await DetectRepositoryAsync(path);
                    if (result.IsRepository)
                    {
                        results.Add(result);
                    }
                }
                return; // Don't recurse into repositories
            }

            // Check subdirectories
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirName = Path.GetFileName(dir);

                // Skip common non-repository directories
                if (dirName.StartsWith(".") ||
                    dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("packages", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await ScanDirectoryAsync(dir, currentDepth + 1, maxDepth, results, visitedRepos);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (DirectoryNotFoundException)
        {
            // Skip if directory was deleted during scan
        }
    }

    #endregion

    #region Repository Status

    /// <summary>
    /// Gets the current status of the opened repository
    /// </summary>
    public async Task<GitRepositoryStatus?> GetStatusAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath)) return null;

        try
        {
            var status = new GitRepositoryStatus
            {
                RepositoryPath = _repositoryPath
            };

            // Get current branch
            var branchResult = await RunGitCommandAsync("rev-parse --abbrev-ref HEAD");
            status.CurrentBranch = branchResult.Success ? branchResult.Output.Trim() : "HEAD";

            // Check if HEAD is detached
            var symbolicRef = await RunGitCommandAsync("symbolic-ref -q HEAD");
            status.IsHeadDetached = !symbolicRef.Success;

            // Get tracking branch info
            var trackingResult = await RunGitCommandAsync($"rev-parse --abbrev-ref {status.CurrentBranch}@{{upstream}}");
            if (trackingResult.Success)
            {
                status.TrackingBranch = trackingResult.Output.Trim();
            }

            // Get ahead/behind counts
            var aheadBehindResult = await RunGitCommandAsync("rev-list --left-right --count HEAD...@{upstream}");
            if (aheadBehindResult.Success)
            {
                var parts = aheadBehindResult.Output.Trim().Split('\t');
                if (parts.Length >= 2)
                {
                    int.TryParse(parts[0], out int ahead);
                    int.TryParse(parts[1], out int behind);
                    status.AheadBy = ahead;
                    status.BehindBy = behind;
                }
            }

            // Get file status
            var statusResult = await RunGitCommandAsync("status --porcelain=v1 -uall");
            if (statusResult.Success)
            {
                var lines = statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Length < 3) continue;

                    var indexStatus = line[0];
                    var workTreeStatus = line[1];
                    var filePath = line.Substring(3).Trim();

                    // Handle renamed files
                    if (filePath.Contains(" -> "))
                    {
                        var parts = filePath.Split(" -> ");
                        filePath = parts[1];
                    }

                    var fileInfo = new GitFileStatusInfo
                    {
                        FilePath = filePath,
                        IndexStatusChar = indexStatus,
                        WorkTreeStatusChar = workTreeStatus
                    };

                    // Categorize
                    if (indexStatus != ' ' && indexStatus != '?')
                    {
                        status.StagedFiles.Add(fileInfo);
                    }

                    if (workTreeStatus == 'M' || workTreeStatus == 'D' || workTreeStatus == 'T')
                    {
                        status.ModifiedFiles.Add(fileInfo);
                    }
                    else if (indexStatus == '?' && workTreeStatus == '?')
                    {
                        status.UntrackedFiles.Add(fileInfo);
                    }

                    if (indexStatus == 'U' || workTreeStatus == 'U' ||
                        (indexStatus == 'A' && workTreeStatus == 'A') ||
                        (indexStatus == 'D' && workTreeStatus == 'D'))
                    {
                        status.ConflictedFiles.Add(fileInfo);
                    }
                }
            }

            // Get recent commits
            var logResult = await RunGitCommandAsync("log -10 --format=\"%H|%h|%an|%ae|%ai|%s\"");
            if (logResult.Success)
            {
                var lines = logResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 6)
                    {
                        status.RecentCommits.Add(new GitCommitInfo
                        {
                            Sha = parts[0],
                            ShortSha = parts[1],
                            Author = parts[2],
                            AuthorEmail = parts[3],
                            Date = DateTime.TryParse(parts[4], out var date) ? date : DateTime.MinValue,
                            Message = parts[5]
                        });
                    }
                }
            }

            // Get local branches
            var branchesResult = await RunGitCommandAsync("branch --format=\"%(refname:short)\"");
            if (branchesResult.Success)
            {
                status.LocalBranches = branchesResult.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim())
                    .ToList();
            }

            // Get remote branches
            var remoteBranchesResult = await RunGitCommandAsync("branch -r --format=\"%(refname:short)\"");
            if (remoteBranchesResult.Success)
            {
                status.RemoteBranches = remoteBranchesResult.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim())
                    .ToList();
            }

            // Get tags
            var tagsResult = await RunGitCommandAsync("tag -l");
            if (tagsResult.Success)
            {
                status.Tags = tagsResult.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .ToList();
            }

            // Notify subscribers
            StatusChanged?.Invoke(this, new GitRepositoryStatusEventArgs(status));

            return status;
        }
        catch (Exception ex)
        {
            return new GitRepositoryStatus
            {
                RepositoryPath = _repositoryPath ?? string.Empty,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets the last commit info
    /// </summary>
    public async Task<GitCommitInfo?> GetLastCommitAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath)) return null;

        var result = await RunGitCommandAsync("log -1 --format=\"%H|%h|%an|%ae|%ai|%s|%b\"");
        if (!result.Success) return null;

        var parts = result.Output.Split('|');
        if (parts.Length < 6) return null;

        return new GitCommitInfo
        {
            Sha = parts[0],
            ShortSha = parts[1],
            Author = parts[2],
            AuthorEmail = parts[3],
            Date = DateTime.TryParse(parts[4], out var date) ? date : DateTime.MinValue,
            Message = parts[5],
            FullMessage = parts.Length > 6 ? parts[6] : null
        };
    }

    #endregion

    #region Branch Operations

    /// <summary>
    /// Gets all branches
    /// </summary>
    public async Task<List<GitBranchInfo>> GetBranchesAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath)) return new List<GitBranchInfo>();

        var branches = new List<GitBranchInfo>();
        var result = await RunGitCommandAsync("branch -a -v --format=\"%(HEAD)|%(refname:short)|%(objectname:short)|%(upstream:short)|%(subject)\"");
        
        if (!result.Success) return branches;

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length >= 3)
            {
                var branchName = parts[1].Trim();
                branches.Add(new GitBranchInfo
                {
                    Name = branchName,
                    IsCurrent = parts[0].Trim() == "*",
                    IsRemote = branchName.StartsWith("remotes/") || branchName.Contains("/"),
                    LastCommitSha = parts[2].Trim(),
                    TrackingBranch = parts.Length > 3 ? parts[3].Trim() : null,
                    IsTracking = parts.Length > 3 && !string.IsNullOrEmpty(parts[3].Trim()),
                    LastCommitMessage = parts.Length > 4 ? parts[4].Trim() : null
                });
            }
        }

        return branches;
    }

    /// <summary>
    /// Checks out a branch
    /// </summary>
    public async Task<GitCommandResult> CheckoutBranchAsync(string branchName)
    {
        return await RunGitCommandAsync($"checkout \"{branchName}\"");
    }

    /// <summary>
    /// Creates a new branch
    /// </summary>
    public async Task<GitCommandResult> CreateBranchAsync(string branchName, bool checkout = false)
    {
        if (checkout)
        {
            return await RunGitCommandAsync($"checkout -b \"{branchName}\"");
        }
        return await RunGitCommandAsync($"branch \"{branchName}\"");
    }

    /// <summary>
    /// Deletes a branch
    /// </summary>
    public async Task<GitCommandResult> DeleteBranchAsync(string branchName, bool force = false)
    {
        var flag = force ? "-D" : "-d";
        return await RunGitCommandAsync($"branch {flag} \"{branchName}\"");
    }

    #endregion

    #region Staging Operations

    /// <summary>
    /// Stages a file
    /// </summary>
    public async Task<GitCommandResult> StageFileAsync(string filePath)
    {
        return await RunGitCommandAsync($"add \"{filePath}\"");
    }

    /// <summary>
    /// Stages all files
    /// </summary>
    public async Task<GitCommandResult> StageAllAsync()
    {
        return await RunGitCommandAsync("add -A");
    }

    /// <summary>
    /// Unstages a file
    /// </summary>
    public async Task<GitCommandResult> UnstageFileAsync(string filePath)
    {
        return await RunGitCommandAsync($"reset HEAD \"{filePath}\"");
    }

    /// <summary>
    /// Unstages all files
    /// </summary>
    public async Task<GitCommandResult> UnstageAllAsync()
    {
        return await RunGitCommandAsync("reset HEAD");
    }

    /// <summary>
    /// Discards changes in a file
    /// </summary>
    public async Task<GitCommandResult> DiscardChangesAsync(string filePath)
    {
        // Check if file is untracked
        var statusResult = await RunGitCommandAsync($"status --porcelain \"{filePath}\"");
        if (statusResult.Success && statusResult.Output.StartsWith("??"))
        {
            var fullPath = Path.Combine(_repositoryPath!, filePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return new GitCommandResult { Success = true, Output = "Deleted untracked file" };
            }
        }

        return await RunGitCommandAsync($"checkout -- \"{filePath}\"");
    }

    #endregion

    #region Commit Operations

    /// <summary>
    /// Creates a commit
    /// </summary>
    public async Task<GitCommandResult> CommitAsync(string message)
    {
        var escapedMessage = message.Replace("\"", "\\\"");
        return await RunGitCommandAsync($"commit -m \"{escapedMessage}\"");
    }

    /// <summary>
    /// Amends the last commit
    /// </summary>
    public async Task<GitCommandResult> AmendCommitAsync(string? newMessage = null)
    {
        if (string.IsNullOrEmpty(newMessage))
        {
            return await RunGitCommandAsync("commit --amend --no-edit");
        }
        var escapedMessage = newMessage.Replace("\"", "\\\"");
        return await RunGitCommandAsync($"commit --amend -m \"{escapedMessage}\"");
    }

    #endregion

    #region Remote Operations

    /// <summary>
    /// Fetches from remote
    /// </summary>
    public async Task<GitCommandResult> FetchAsync(string remote = "origin", bool allBranches = true)
    {
        var args = allBranches ? "--all" : remote;
        return await RunGitCommandAsync($"fetch {args}");
    }

    /// <summary>
    /// Pulls from remote
    /// </summary>
    public async Task<GitCommandResult> PullAsync(string remote = "origin", string? branch = null)
    {
        var branchArg = string.IsNullOrEmpty(branch) ? "" : branch;
        return await RunGitCommandAsync($"pull {remote} {branchArg}");
    }

    /// <summary>
    /// Pushes to remote
    /// </summary>
    public async Task<GitCommandResult> PushAsync(string remote = "origin", string? branch = null, bool setUpstream = false)
    {
        var branchArg = string.IsNullOrEmpty(branch) ? "" : branch;
        var upstreamArg = setUpstream ? "-u" : "";
        return await RunGitCommandAsync($"push {upstreamArg} {remote} {branchArg}");
    }

    #endregion

    #region Diff Operations

    /// <summary>
    /// Gets the diff for a file
    /// </summary>
    public async Task<string?> GetFileDiffAsync(string filePath, bool staged = false)
    {
        var stageArg = staged ? "--cached" : "";
        var result = await RunGitCommandAsync($"diff {stageArg} -- \"{filePath}\"");
        return result.Success ? result.Output : null;
    }

    /// <summary>
    /// Gets all diff
    /// </summary>
    public async Task<string?> GetAllDiffAsync(bool staged = false)
    {
        var stageArg = staged ? "--cached" : "";
        var result = await RunGitCommandAsync($"diff {stageArg}");
        return result.Success ? result.Output : null;
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Gets config value
    /// </summary>
    public async Task<string?> GetConfigAsync(string key, bool global = false)
    {
        var scope = global ? "--global" : "--local";
        var result = await RunGitCommandAsync($"config {scope} {key}");
        return result.Success ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Sets config value
    /// </summary>
    public async Task<GitCommandResult> SetConfigAsync(string key, string value, bool global = false)
    {
        var scope = global ? "--global" : "--local";
        return await RunGitCommandAsync($"config {scope} {key} \"{value}\"");
    }

    /// <summary>
    /// Gets user name from config
    /// </summary>
    public async Task<string?> GetConfigUserNameAsync()
    {
        return await GetConfigAsync("user.name");
    }

    /// <summary>
    /// Gets user email from config
    /// </summary>
    public async Task<string?> GetConfigUserEmailAsync()
    {
        return await GetConfigAsync("user.email");
    }

    #endregion

    #region Static Operations

    /// <summary>
    /// Quickly checks if a path is inside a Git repository
    /// </summary>
    public static bool IsGitRepository(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path) ?? path;
            }

            // Simple check for .git folder
            var current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, ".git")) || 
                    File.Exists(Path.Combine(current, ".git")))
                {
                    return true;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets repository root path
    /// </summary>
    public static async Task<string?> GetRepositoryRootAsync(string path)
    {
        var result = await RunGitCommandStaticAsync("rev-parse --show-toplevel", path);
        return result.Success ? result.Output.Trim().Replace("/", "\\") : null;
    }

    /// <summary>
    /// Initializes a new Git repository
    /// </summary>
    public static async Task<GitCommandResult> InitRepositoryAsync(string path)
    {
        return await RunGitCommandStaticAsync("init", path);
    }

    /// <summary>
    /// Clones a repository
    /// </summary>
    public static async Task<GitCommandResult> CloneRepositoryAsync(string sourceUrl, string destinationPath,
        Action<string>? progressCallback = null)
    {
        var result = await RunGitCommandStaticAsync($"clone --progress \"{sourceUrl}\" \"{destinationPath}\"", null,
            progressCallback);
        return result;
    }

    #endregion

    #region Private Methods

    private async Task<GitCommandResult> RunGitCommandAsync(string arguments, string? workingDirectory = null)
    {
        return await RunGitCommandStaticAsync(arguments, workingDirectory ?? _repositoryPath);
    }

    private static async Task<GitCommandResult> RunGitCommandStaticAsync(string arguments, string? workingDirectory,
        Action<string>? progressCallback = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GitExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = startInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    progressCallback?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    progressCallback?.Invoke(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return new GitCommandResult
            {
                Success = process.ExitCode == 0,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new GitCommandResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _repositoryPath = null;
        }

        _disposed = true;
    }

    ~GitDetectionService()
    {
        Dispose(false);
    }

    #endregion
}

#region Event Args

/// <summary>
/// Event args for repository detection
/// </summary>
public class GitRepositoryDetectedEventArgs : EventArgs
{
    public GitDetectionResult Result { get; }

    public GitRepositoryDetectedEventArgs(GitDetectionResult result)
    {
        Result = result;
    }
}

/// <summary>
/// Event args for status changes
/// </summary>
public class GitRepositoryStatusEventArgs : EventArgs
{
    public GitRepositoryStatus Status { get; }

    public GitRepositoryStatusEventArgs(GitRepositoryStatus status)
    {
        Status = status;
    }
}

#endregion

#region Data Classes

/// <summary>
/// Result of a Git command
/// </summary>
public class GitCommandResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}

/// <summary>
/// Result of repository detection
/// </summary>
public class GitDetectionResult
{
    public string SearchedPath { get; set; } = string.Empty;
    public bool IsRepository { get; set; }
    public string? RepositoryPath { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? CurrentBranch { get; set; }
    public string? RemoteUrl { get; set; }
    public string? RemoteName { get; set; }
    public bool IsHeadDetached { get; set; }
    public bool IsHeadUnborn { get; set; }
    public bool IsBare { get; set; }
    public int ModifiedCount { get; set; }
    public int AddedCount { get; set; }
    public int DeletedCount { get; set; }
    public int UntrackedCount { get; set; }
    public int StagedCount { get; set; }
    public string? Error { get; set; }

    public bool HasChanges => ModifiedCount > 0 || AddedCount > 0 || DeletedCount > 0 || UntrackedCount > 0;
    public int TotalChanges => ModifiedCount + AddedCount + DeletedCount + UntrackedCount;
}

/// <summary>
/// Repository status information
/// </summary>
public class GitRepositoryStatus
{
    public string RepositoryPath { get; set; } = string.Empty;
    public string? CurrentBranch { get; set; }
    public bool IsHeadDetached { get; set; }
    public string? TrackingBranch { get; set; }
    public int AheadBy { get; set; }
    public int BehindBy { get; set; }
    public List<GitFileStatusInfo> StagedFiles { get; set; } = new();
    public List<GitFileStatusInfo> ModifiedFiles { get; set; } = new();
    public List<GitFileStatusInfo> UntrackedFiles { get; set; } = new();
    public List<GitFileStatusInfo> ConflictedFiles { get; set; } = new();
    public List<GitCommitInfo> RecentCommits { get; set; } = new();
    public List<string> LocalBranches { get; set; } = new();
    public List<string> RemoteBranches { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? Error { get; set; }

    public bool HasStagedChanges => StagedFiles.Count > 0;
    public bool HasUnstagedChanges => ModifiedFiles.Count > 0 || UntrackedFiles.Count > 0;
    public bool HasConflicts => ConflictedFiles.Count > 0;
    public int TotalChanges => StagedFiles.Count + ModifiedFiles.Count + UntrackedFiles.Count;
}

/// <summary>
/// File status information
/// </summary>
public class GitFileStatusInfo
{
    public string FilePath { get; set; } = string.Empty;
    public char IndexStatusChar { get; set; } = ' ';
    public char WorkTreeStatusChar { get; set; } = ' ';

    public string FileName => Path.GetFileName(FilePath);
    public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public string StatusIcon
    {
        get
        {
            if (IndexStatusChar == '?' && WorkTreeStatusChar == '?') return "?";
            if (IndexStatusChar == 'A' || WorkTreeStatusChar == 'A') return "A";
            if (IndexStatusChar == 'M' || WorkTreeStatusChar == 'M') return "M";
            if (IndexStatusChar == 'D' || WorkTreeStatusChar == 'D') return "D";
            if (IndexStatusChar == 'R' || WorkTreeStatusChar == 'R') return "R";
            if (IndexStatusChar == 'C' || WorkTreeStatusChar == 'C') return "C";
            if (IndexStatusChar == 'U' || WorkTreeStatusChar == 'U') return "U";
            return " ";
        }
    }
}

/// <summary>
/// Commit information
/// </summary>
public class GitCommitInfo
{
    public string Sha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? FullMessage { get; set; }
    public string Author { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

/// <summary>
/// Branch information
/// </summary>
public class GitBranchInfo
{
    public string Name { get; set; } = string.Empty;
    public bool IsRemote { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsTracking { get; set; }
    public string? TrackingBranch { get; set; }
    public string? LastCommitSha { get; set; }
    public string? LastCommitMessage { get; set; }
}

#endregion

