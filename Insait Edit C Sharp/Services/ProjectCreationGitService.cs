using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

public sealed class ProjectCreationGitService
{
    private readonly GitService _gitService;

    public ProjectCreationGitService() : this(new GitService())
    {
    }

    public ProjectCreationGitService(GitService gitService)
    {
        _gitService = gitService;
    }

    public async Task<ProjectCreationGitSetupResult> EnsureRepositoryWithInitialCommitAsync(
        string targetDirectory,
        string initialCommitMessage = "Initial commit")
    {
        var repoDirectory = NormalizeDirectory(targetDirectory);
        Directory.CreateDirectory(repoDirectory);

        try
        {
            var existingRepoRoot = await _gitService.FindRepositoryRootAsync(repoDirectory);
            if (!string.IsNullOrWhiteSpace(existingRepoRoot))
            {
                var hasCommits = await _gitService.HasCommitsAsync();
                if (PathsEqual(existingRepoRoot, repoDirectory) && !hasCommits)
                {
                    await EnsureDotNetGitIgnoreAsync(repoDirectory);

                    var commitResult = await _gitService.MakeInitialCommitAsync(initialCommitMessage);
                    return new ProjectCreationGitSetupResult
                    {
                        Success = commitResult.Success || IsNothingToCommit(commitResult),
                        Error = commitResult.Success || IsNothingToCommit(commitResult) ? string.Empty : commitResult.Error
                    };
                }

                return new ProjectCreationGitSetupResult
                {
                    Success = true
                };
            }

            var initResult = await _gitService.InitAsync(repoDirectory);
            if (!initResult.Success)
            {
                return new ProjectCreationGitSetupResult
                {
                    Success = false,
                    Error = initResult.Error
                };
            }

            await EnsureDotNetGitIgnoreAsync(repoDirectory);

            var initialCommitResult = await _gitService.MakeInitialCommitAsync(initialCommitMessage);
            return new ProjectCreationGitSetupResult
            {
                Success = initialCommitResult.Success || IsNothingToCommit(initialCommitResult),
                Error = initialCommitResult.Success || IsNothingToCommit(initialCommitResult) ? string.Empty : initialCommitResult.Error
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProjectCreationGitService] Git setup failed: {ex.Message}");
            return new ProjectCreationGitSetupResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task EnsureDotNetGitIgnoreAsync(string targetDirectory)
    {
        var repoDirectory = NormalizeDirectory(targetDirectory);
        var gitIgnorePath = Path.Combine(repoDirectory, ".gitignore");

        if (File.Exists(gitIgnorePath))
        {
            var existingContent = await File.ReadAllTextAsync(gitIgnorePath);
            if (!string.IsNullOrWhiteSpace(existingContent))
                return;
        }

        await File.WriteAllTextAsync(gitIgnorePath, DotNetGitIgnore);
    }

    private static bool IsNothingToCommit(GitResult result)
    {
        return !result.Success &&
               (result.Error.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
                result.Output.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path)
    {
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;

        return Path.GetFullPath(path);
    }

    private const string DotNetGitIgnore = @"## .NET
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

public sealed class ProjectCreationGitSetupResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
}

