using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Models;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for Git operations using local git CLI
/// </summary>
public class GitService
{
    private string? _repositoryPath;
    
    public string? RepositoryPath 
    { 
        get => _repositoryPath;
        set => _repositoryPath = value;
    }
    
    public bool IsRepository => !string.IsNullOrEmpty(_repositoryPath) && 
                                 Directory.Exists(Path.Combine(_repositoryPath, ".git"));

    public GitService()
    {
    }

    public GitService(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
    }

    #region Repository Operations

    /// <summary>
    /// Initialize a new git repository
    /// </summary>
    public async Task<GitResult> InitAsync(string path)
    {
        // Ensure path is a directory
        string dirPath = path;
        if (File.Exists(path))
        {
            dirPath = Path.GetDirectoryName(path) ?? path;
        }
        
        if (!Directory.Exists(dirPath))
        {
            return new GitResult { Success = false, Error = $"Directory does not exist: {dirPath}" };
        }
        
        _repositoryPath = dirPath;
        return await RunGitCommandAsync("init", dirPath);
    }

    /// <summary>
    /// Clone a repository
    /// </summary>
    public async Task<GitResult> CloneAsync(string url, string localPath)
    {
        var result = await RunGitCommandAsync($"clone \"{url}\" \"{localPath}\"", null);
        if (result.Success)
        {
            _repositoryPath = localPath;
        }
        return result;
    }

    /// <summary>
    /// Check if path is a git repository
    /// </summary>
    public bool IsGitRepository(string path)
    {
        // Ensure path is a directory
        string dirPath = path;
        if (File.Exists(path))
        {
            dirPath = Path.GetDirectoryName(path) ?? path;
        }
        return Directory.Exists(Path.Combine(dirPath, ".git"));
    }

    /// <summary>
    /// Find git repository root from any path inside it
    /// </summary>
    public async Task<string?> FindRepositoryRootAsync(string path)
    {
        // Ensure path is a directory
        string dirPath = path;
        if (File.Exists(path))
        {
            dirPath = Path.GetDirectoryName(path) ?? path;
        }
        
        // Normalize path
        dirPath = Path.GetFullPath(dirPath);
        
        var result = await RunGitCommandAsync("rev-parse --show-toplevel", dirPath);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
        {
            var repoRoot = result.Output.Trim();
            // Normalize the repo root path (git returns forward slashes on Windows)
            repoRoot = Path.GetFullPath(repoRoot.Replace('/', Path.DirectorySeparatorChar));
            _repositoryPath = repoRoot;
            return repoRoot;
        }
        return null;
    }

    #endregion

    #region Status & Changes

    /// <summary>
    /// Get current repository status
    /// </summary>
    public async Task<GitStatus> GetStatusAsync()
    {
        var status = new GitStatus();
        
        if (!IsRepository) return status;

        // Get current branch
        var branchResult = await RunGitCommandAsync("rev-parse --abbrev-ref HEAD");
        if (branchResult.Success)
        {
            status.CurrentBranch = branchResult.Output.Trim();
        }

        // Get status with porcelain format for easy parsing
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
                
                // Handle renamed files (format: "R  old -> new")
                if (filePath.Contains(" -> "))
                {
                    var parts = filePath.Split(" -> ");
                    filePath = parts[1];
                }

                var change = new GitFileChange
                {
                    FilePath = filePath,
                    FullPath = Path.Combine(_repositoryPath!, filePath),
                    IndexStatus = ParseStatus(indexStatus),
                    WorkTreeStatus = ParseStatus(workTreeStatus)
                };

                // Categorize changes
                if (indexStatus != ' ' && indexStatus != '?')
                {
                    status.StagedChanges.Add(change);
                }
                
                if (workTreeStatus != ' ' || indexStatus == '?')
                {
                    if (indexStatus == '?')
                    {
                        change.WorkTreeStatus = GitFileStatus.Untracked;
                    }
                    status.UnstagedChanges.Add(change);
                }
            }
        }

        // Get ahead/behind count
        var aheadBehindResult = await RunGitCommandAsync("rev-list --left-right --count HEAD...@{u}");
        if (aheadBehindResult.Success)
        {
            var parts = aheadBehindResult.Output.Trim().Split('\t');
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0], out int ahead);
                int.TryParse(parts[1], out int behind);
                status.AheadCount = ahead;
                status.BehindCount = behind;
            }
        }

        return status;
    }

    /// <summary>
    /// Get diff for a specific file
    /// </summary>
    public async Task<string> GetFileDiffAsync(string filePath, bool staged = false)
    {
        var command = staged 
            ? $"diff --cached -- \"{filePath}\"" 
            : $"diff -- \"{filePath}\"";
        
        var result = await RunGitCommandAsync(command);
        return result.Success ? result.Output : string.Empty;
    }

    /// <summary>
    /// Get untracked file content
    /// </summary>
    public async Task<string> GetUntrackedFileContentAsync(string filePath)
    {
        var fullPath = Path.Combine(_repositoryPath!, filePath);
        if (File.Exists(fullPath))
        {
            return await File.ReadAllTextAsync(fullPath);
        }
        return string.Empty;
    }

    #endregion

    #region Staging

    /// <summary>
    /// Stage a file
    /// </summary>
    public async Task<GitResult> StageFileAsync(string filePath)
    {
        return await RunGitCommandAsync($"add \"{filePath}\"");
    }

    /// <summary>
    /// Stage all changes
    /// </summary>
    public async Task<GitResult> StageAllAsync()
    {
        return await RunGitCommandAsync("add -A");
    }

    /// <summary>
    /// Unstage a file
    /// </summary>
    public async Task<GitResult> UnstageFileAsync(string filePath)
    {
        return await RunGitCommandAsync($"reset HEAD \"{filePath}\"");
    }

    /// <summary>
    /// Unstage all changes
    /// </summary>
    public async Task<GitResult> UnstageAllAsync()
    {
        return await RunGitCommandAsync("reset HEAD");
    }

    /// <summary>
    /// Discard changes in a file
    /// </summary>
    public async Task<GitResult> DiscardChangesAsync(string filePath)
    {
        // Check if file is untracked
        var statusResult = await RunGitCommandAsync($"status --porcelain \"{filePath}\"");
        if (statusResult.Success && statusResult.Output.StartsWith("??"))
        {
            // Delete untracked file
            var fullPath = Path.Combine(_repositoryPath!, filePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return new GitResult { Success = true, Output = "Deleted untracked file" };
            }
        }
        
        return await RunGitCommandAsync($"checkout -- \"{filePath}\"");
    }

    /// <summary>
    /// Discard all changes
    /// </summary>
    public async Task<GitResult> DiscardAllChangesAsync()
    {
        // Clean untracked files
        await RunGitCommandAsync("clean -fd");
        // Reset tracked files
        return await RunGitCommandAsync("checkout -- .");
    }

    #endregion

    #region Commits

    /// <summary>
    /// Create a commit
    /// </summary>
    public async Task<GitResult> CommitAsync(string message, bool amendLastCommit = false)
    {
        var escapedMessage = message.Replace("\"", "\\\"");
        var command = amendLastCommit 
            ? $"commit --amend -m \"{escapedMessage}\"" 
            : $"commit -m \"{escapedMessage}\"";
        
        return await RunGitCommandAsync(command);
    }

    /// <summary>
    /// Get commit history
    /// </summary>
    public async Task<List<GitCommit>> GetCommitHistoryAsync(int count = 50, string? branch = null)
    {
        var commits = new List<GitCommit>();
        
        var branchArg = string.IsNullOrEmpty(branch) ? "" : branch;
        var format = "%H|%h|%an|%ae|%ai|%s";
        var result = await RunGitCommandAsync($"log -{count} --format=\"{format}\" {branchArg}");
        
        if (result.Success)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 6)
                {
                    commits.Add(new GitCommit
                    {
                        Hash = parts[0],
                        ShortHash = parts[1],
                        AuthorName = parts[2],
                        AuthorEmail = parts[3],
                        Date = DateTime.TryParse(parts[4], out var date) ? date : DateTime.MinValue,
                        Message = parts[5]
                    });
                }
            }
        }
        
        return commits;
    }

    /// <summary>
    /// Get details of a specific commit
    /// </summary>
    public async Task<GitCommit?> GetCommitDetailsAsync(string hash)
    {
        var format = "%H|%h|%an|%ae|%ai|%s|%b";
        var result = await RunGitCommandAsync($"log -1 --format=\"{format}\" {hash}");
        
        if (result.Success)
        {
            var parts = result.Output.Split('|');
            if (parts.Length >= 6)
            {
                var commit = new GitCommit
                {
                    Hash = parts[0],
                    ShortHash = parts[1],
                    AuthorName = parts[2],
                    AuthorEmail = parts[3],
                    Date = DateTime.TryParse(parts[4], out var date) ? date : DateTime.MinValue,
                    Message = parts[5],
                    Body = parts.Length > 6 ? parts[6] : null
                };

                // Get changed files
                var filesResult = await RunGitCommandAsync($"show --name-status --format= {hash}");
                if (filesResult.Success)
                {
                    var lines = filesResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var fileParts = line.Split('\t');
                        if (fileParts.Length >= 2)
                        {
                            commit.ChangedFiles.Add(new GitFileChange
                            {
                                FilePath = fileParts[1],
                                WorkTreeStatus = ParseStatusChar(fileParts[0][0])
                            });
                        }
                    }
                }

                return commit;
            }
        }
        
        return null;
    }

    #endregion

    #region Branches

    /// <summary>
    /// Get all branches
    /// </summary>
    public async Task<List<GitBranch>> GetBranchesAsync(bool includeRemote = true)
    {
        var branches = new List<GitBranch>();
        
        var args = includeRemote ? "-a" : "";
        var result = await RunGitCommandAsync($"branch {args} -v");
        
        if (result.Success)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var isCurrent = line.StartsWith("*");
                var trimmedLine = line.TrimStart('*', ' ');
                
                // Parse branch name and last commit
                var match = Regex.Match(trimmedLine, @"^(\S+)\s+([a-f0-9]+)\s+(.*)$");
                if (match.Success)
                {
                    var branchName = match.Groups[1].Value;
                    var isRemote = branchName.StartsWith("remotes/");
                    
                    branches.Add(new GitBranch
                    {
                        Name = branchName.Replace("remotes/", ""),
                        IsCurrent = isCurrent,
                        IsRemote = isRemote,
                        LastCommitHash = match.Groups[2].Value,
                        LastCommitMessage = match.Groups[3].Value
                    });
                }
            }
        }
        
        return branches;
    }

    /// <summary>
    /// Get current branch name
    /// </summary>
    public async Task<string> GetCurrentBranchAsync()
    {
        var result = await RunGitCommandAsync("rev-parse --abbrev-ref HEAD");
        return result.Success ? result.Output.Trim() : "HEAD";
    }

    /// <summary>
    /// Create a new branch
    /// </summary>
    public async Task<GitResult> CreateBranchAsync(string branchName, bool checkout = false)
    {
        if (checkout)
        {
            return await RunGitCommandAsync($"checkout -b \"{branchName}\"");
        }
        return await RunGitCommandAsync($"branch \"{branchName}\"");
    }

    /// <summary>
    /// Checkout a branch
    /// </summary>
    public async Task<GitResult> CheckoutBranchAsync(string branchName)
    {
        return await RunGitCommandAsync($"checkout \"{branchName}\"");
    }

    /// <summary>
    /// Delete a branch
    /// </summary>
    public async Task<GitResult> DeleteBranchAsync(string branchName, bool force = false)
    {
        var flag = force ? "-D" : "-d";
        return await RunGitCommandAsync($"branch {flag} \"{branchName}\"");
    }

    /// <summary>
    /// Rename current branch
    /// </summary>
    public async Task<GitResult> RenameBranchAsync(string newName)
    {
        return await RunGitCommandAsync($"branch -m \"{newName}\"");
    }

    /// <summary>
    /// Merge a branch into current
    /// </summary>
    public async Task<GitResult> MergeBranchAsync(string branchName, bool noFastForward = false)
    {
        var args = noFastForward ? "--no-ff" : "";
        return await RunGitCommandAsync($"merge {args} \"{branchName}\"");
    }

    #endregion

    #region Remote Operations

    /// <summary>
    /// Get remotes
    /// </summary>
    public async Task<List<GitRemote>> GetRemotesAsync()
    {
        var remotes = new List<GitRemote>();
        
        var result = await RunGitCommandAsync("remote -v");
        if (result.Success)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var processedRemotes = new HashSet<string>();
            
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    var name = parts[0];
                    if (processedRemotes.Contains(name)) continue;
                    
                    var urlPart = parts[1].Split(' ')[0];
                    remotes.Add(new GitRemote { Name = name, Url = urlPart });
                    processedRemotes.Add(name);
                }
            }
        }
        
        return remotes;
    }

    /// <summary>
    /// Add a remote
    /// </summary>
    public async Task<GitResult> AddRemoteAsync(string name, string url)
    {
        return await RunGitCommandAsync($"remote add \"{name}\" \"{url}\"");
    }

    /// <summary>
    /// Remove a remote
    /// </summary>
    public async Task<GitResult> RemoveRemoteAsync(string name)
    {
        return await RunGitCommandAsync($"remote remove \"{name}\"");
    }

    /// <summary>
    /// Fetch from remote
    /// </summary>
    public async Task<GitResult> FetchAsync(string remote = "origin", bool allBranches = true)
    {
        var args = allBranches ? "--all" : remote;
        return await RunGitCommandAsync($"fetch {args}");
    }

    /// <summary>
    /// Pull from remote
    /// </summary>
    public async Task<GitResult> PullAsync(string remote = "origin", string? branch = null)
    {
        var branchArg = string.IsNullOrEmpty(branch) ? "" : branch;
        return await RunGitCommandAsync($"pull {remote} {branchArg}");
    }

    /// <summary>
    /// Push to remote
    /// </summary>
    public async Task<GitResult> PushAsync(string remote = "origin", string? branch = null, bool setUpstream = false)
    {
        var branchArg = string.IsNullOrEmpty(branch) ? "" : branch;
        var upstreamArg = setUpstream ? "-u" : "";
        return await RunGitCommandAsync($"push {upstreamArg} {remote} {branchArg}");
    }

    /// <summary>
    /// Push with force
    /// </summary>
    public async Task<GitResult> ForcePushAsync(string remote = "origin", string? branch = null)
    {
        var branchArg = string.IsNullOrEmpty(branch) ? "" : branch;
        return await RunGitCommandAsync($"push --force {remote} {branchArg}");
    }

    #endregion

    #region Stash

    /// <summary>
    /// Stash changes
    /// </summary>
    public async Task<GitResult> StashAsync(string? message = null)
    {
        var msgArg = string.IsNullOrEmpty(message) ? "" : $"push -m \"{message}\"";
        return await RunGitCommandAsync($"stash {msgArg}");
    }

    /// <summary>
    /// Get stash list
    /// </summary>
    public async Task<List<GitStash>> GetStashListAsync()
    {
        var stashes = new List<GitStash>();
        
        var result = await RunGitCommandAsync("stash list");
        if (result.Success)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^(stash@\{\d+\}):\s*(.*?):\s*(.*)$");
                if (match.Success)
                {
                    stashes.Add(new GitStash
                    {
                        Name = match.Groups[1].Value,
                        Branch = match.Groups[2].Value,
                        Message = match.Groups[3].Value
                    });
                }
            }
        }
        
        return stashes;
    }

    /// <summary>
    /// Apply stash
    /// </summary>
    public async Task<GitResult> StashApplyAsync(string? stashName = null)
    {
        var stashArg = string.IsNullOrEmpty(stashName) ? "" : stashName;
        return await RunGitCommandAsync($"stash apply {stashArg}");
    }

    /// <summary>
    /// Pop stash
    /// </summary>
    public async Task<GitResult> StashPopAsync(string? stashName = null)
    {
        var stashArg = string.IsNullOrEmpty(stashName) ? "" : stashName;
        return await RunGitCommandAsync($"stash pop {stashArg}");
    }

    /// <summary>
    /// Drop stash
    /// </summary>
    public async Task<GitResult> StashDropAsync(string? stashName = null)
    {
        var stashArg = string.IsNullOrEmpty(stashName) ? "" : stashName;
        return await RunGitCommandAsync($"stash drop {stashArg}");
    }

    #endregion

    #region Tags

    /// <summary>
    /// Get all tags
    /// </summary>
    public async Task<List<GitTag>> GetTagsAsync()
    {
        var tags = new List<GitTag>();
        
        var result = await RunGitCommandAsync("tag -l --format=\"%(refname:short)|%(objectname:short)|%(creatordate:iso)|%(contents:subject)\"");
        if (result.Success)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 1)
                {
                    tags.Add(new GitTag
                    {
                        Name = parts[0],
                        CommitHash = parts.Length > 1 ? parts[1] : "",
                        Date = parts.Length > 2 && DateTime.TryParse(parts[2], out var date) ? date : null,
                        Message = parts.Length > 3 ? parts[3] : ""
                    });
                }
            }
        }
        
        return tags;
    }

    /// <summary>
    /// Create a tag
    /// </summary>
    public async Task<GitResult> CreateTagAsync(string tagName, string? message = null, string? commitHash = null)
    {
        var commitArg = string.IsNullOrEmpty(commitHash) ? "" : commitHash;
        if (string.IsNullOrEmpty(message))
        {
            return await RunGitCommandAsync($"tag \"{tagName}\" {commitArg}");
        }
        return await RunGitCommandAsync($"tag -a \"{tagName}\" -m \"{message}\" {commitArg}");
    }

    /// <summary>
    /// Delete a tag
    /// </summary>
    public async Task<GitResult> DeleteTagAsync(string tagName, bool deleteRemote = false)
    {
        var result = await RunGitCommandAsync($"tag -d \"{tagName}\"");
        if (deleteRemote && result.Success)
        {
            await RunGitCommandAsync($"push origin --delete \"{tagName}\"");
        }
        return result;
    }

    /// <summary>
    /// Push tags to remote
    /// </summary>
    public async Task<GitResult> PushTagsAsync(string remote = "origin")
    {
        return await RunGitCommandAsync($"push {remote} --tags");
    }

    #endregion

    #region Utility

    /// <summary>
    /// Get git configuration value
    /// </summary>
    public async Task<string?> GetConfigAsync(string key, bool global = false)
    {
        var scope = global ? "--global" : "--local";
        var result = await RunGitCommandAsync($"config {scope} {key}");
        return result.Success ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Set git configuration value
    /// </summary>
    public async Task<GitResult> SetConfigAsync(string key, string value, bool global = false)
    {
        var scope = global ? "--global" : "--local";
        return await RunGitCommandAsync($"config {scope} {key} \"{value}\"");
    }

    /// <summary>
    /// Check if git is installed
    /// </summary>
    public async Task<bool> IsGitInstalledAsync()
    {
        var result = await RunGitCommandAsync("--version", null);
        return result.Success;
    }

    /// <summary>
    /// Get git version
    /// </summary>
    public async Task<string> GetGitVersionAsync()
    {
        var result = await RunGitCommandAsync("--version", null);
        return result.Success ? result.Output.Trim() : "Git not found";
    }

    #endregion

    #region Reset

    /// <summary>
    /// Hard reset to a specific commit — discards ALL local changes and moves HEAD to that commit.
    /// USE WITH CAUTION: this rewrites history on the local branch.
    /// </summary>
    public async Task<GitResult> ResetHardAsync(string commitHash)
    {
        return await RunGitCommandAsync($"reset --hard \"{commitHash}\"");
    }

    /// <summary>
    /// Checks whether the given commit is the root commit (has no parents).
    /// </summary>
    public async Task<bool> IsRootCommitAsync(string commitHash)
    {
        var result = await RunGitCommandAsync($"rev-list --parents -n 1 {commitHash}");
        if (!result.Success) return false;
        // root commit line: "<hash>" (only one token — no parent hash)
        var tokens = result.Output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 1;
    }

    /// <summary>
    /// Creates an initial commit with all files in the working directory.
    /// Should be called right after <see cref="InitAsync"/> before any other commits.
    /// </summary>
    public async Task<GitResult> MakeInitialCommitAsync(string message = "Initial commit")
    {
        // Stage everything
        var stageResult = await RunGitCommandAsync("add -A");
        if (!stageResult.Success) return stageResult;

        // Set a default identity if git config is empty (needed on fresh systems)
        var userName  = await GetConfigAsync("user.name",  global: true);
        var userEmail = await GetConfigAsync("user.email", global: true);
        if (string.IsNullOrWhiteSpace(userName))
            await SetConfigAsync("user.name",  "Developer", global: true);
        if (string.IsNullOrWhiteSpace(userEmail))
            await SetConfigAsync("user.email", "dev@localhost", global: true);

        var escapedMsg = message.Replace("\"", "\\\"");
        return await RunGitCommandAsync($"commit -m \"{escapedMsg}\"");
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Run a git command and return success status (for simple operations)
    /// </summary>
    public async Task<bool> RunGitCommandInternalAsync(string arguments)
    {
        var result = await RunGitCommandAsync(arguments);
        return result.Success;
    }

    #endregion

    #region Private Methods

    private async Task<GitResult> RunGitCommandAsync(string arguments, string? workingDirectory = null)
    {
        workingDirectory ??= _repositoryPath;
        
        // Validate working directory
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            // Ensure it's a directory, not a file
            if (File.Exists(workingDirectory))
            {
                workingDirectory = Path.GetDirectoryName(workingDirectory);
            }
            
            // Check if directory exists
            if (!Directory.Exists(workingDirectory))
            {
                return new GitResult
                {
                    Success = false,
                    Error = $"Working directory does not exist: {workingDirectory}",
                    ExitCode = -1
                };
            }
        }
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
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
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new GitResult
            {
                Success = process.ExitCode == 0,
                Output = output,
                Error = error,
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new GitResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    private GitFileStatus ParseStatus(char status)
    {
        return status switch
        {
            'M' => GitFileStatus.Modified,
            'A' => GitFileStatus.Added,
            'D' => GitFileStatus.Deleted,
            'R' => GitFileStatus.Renamed,
            'C' => GitFileStatus.Copied,
            'U' => GitFileStatus.Unmerged,
            '?' => GitFileStatus.Untracked,
            '!' => GitFileStatus.Ignored,
            ' ' => GitFileStatus.Unmodified,
            _ => GitFileStatus.Unknown
        };
    }

    private GitFileStatus ParseStatusChar(char status)
    {
        return ParseStatus(status);
    }

    #endregion
}

/// <summary>
/// Result of a git command
/// </summary>
public class GitResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}


