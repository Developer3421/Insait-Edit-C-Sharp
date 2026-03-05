using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for managing GitHub account, Copilot usage, and repositories
/// </summary>
public class GitHubAccountService
{
    /// <summary>
    /// Event raised when account status changes
    /// </summary>
    public event EventHandler<GitHubAccountInfo?>? AccountChanged;
    
    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    private GitHubAccountInfo? _currentAccount;
    
    /// <summary>
    /// Current logged in account info
    /// </summary>
    public GitHubAccountInfo? CurrentAccount => _currentAccount;
    
    /// <summary>
    /// Check if GitHub CLI is installed
    /// </summary>
    public async Task<bool> IsGitHubCliInstalledAsync()
    {
        try
        {
            var result = await RunGhCommandAsync("--version");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Check if user is logged in to GitHub
    /// </summary>
    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var result = await RunGhCommandAsync("auth status");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Login to GitHub
    /// </summary>
    public async Task<bool> LoginAsync()
    {
        try
        {
            // This will open browser for authentication
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveGhExe(),
                Arguments = "auth login --web",
                UseShellExecute = true
            };
            
            var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                // Refresh account info after login
                await RefreshAccountInfoAsync();
                
                return process.ExitCode == 0;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Logout from GitHub
    /// </summary>
    public async Task<bool> LogoutAsync()
    {
        try
        {
            var result = await RunGhCommandAsync("auth logout --hostname github.com");
            if (result.Success)
            {
                _currentAccount = null;
                AccountChanged?.Invoke(this, null);
            }
            return result.Success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Logout failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Get account information
    /// </summary>
    public async Task<GitHubAccountInfo?> GetAccountInfoAsync()
    {
        try
        {
            // Get user info
            var userResult = await RunGhCommandAsync("api user");
            if (!userResult.Success)
            {
                return null;
            }
            
            var userJson = JsonDocument.Parse(userResult.Output);
            var root = userJson.RootElement;
            
            var accountInfo = new GitHubAccountInfo
            {
                Username = root.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "",
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Email = root.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
                AvatarUrl = root.TryGetProperty("avatar_url", out var avatar) ? avatar.GetString() ?? "" : "",
                Bio = root.TryGetProperty("bio", out var bio) ? bio.GetString() ?? "" : "",
                Company = root.TryGetProperty("company", out var company) ? company.GetString() ?? "" : "",
                Location = root.TryGetProperty("location", out var location) ? location.GetString() ?? "" : "",
                PublicRepos = root.TryGetProperty("public_repos", out var repos) ? repos.GetInt32() : 0,
                Followers = root.TryGetProperty("followers", out var followers) ? followers.GetInt32() : 0,
                Following = root.TryGetProperty("following", out var following) ? following.GetInt32() : 0,
                IsLoggedIn = true
            };
            
            _currentAccount = accountInfo;
            AccountChanged?.Invoke(this, accountInfo);
            
            return accountInfo;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get account info: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Refresh account information
    /// </summary>
    public async Task RefreshAccountInfoAsync()
    {
        await GetAccountInfoAsync();
    }
    
    /// <summary>
    /// Get Copilot usage/subscription status
    /// </summary>
    public async Task<CopilotUsageInfo?> GetCopilotUsageAsync()
    {
        try
        {
            // Check Copilot extension status
            var extensionResult = await RunGhCommandAsync("extension list");
            var hasCopilotExtension = extensionResult.Success && 
                extensionResult.Output.Contains("copilot", StringComparison.OrdinalIgnoreCase);
            
            // Try to get Copilot status
            var copilotResult = await RunGhCommandAsync("copilot --version");
            var hasCopilot = copilotResult.Success;
            
            // Get rate limit info as proxy for API usage
            var rateLimitResult = await RunGhCommandAsync("api rate_limit");
            
            var usageInfo = new CopilotUsageInfo
            {
                IsAvailable = hasCopilot || hasCopilotExtension,
                HasExtension = hasCopilotExtension,
                Status = hasCopilot ? "Active" : (hasCopilotExtension ? "Extension installed" : "Not configured")
            };
            
            if (rateLimitResult.Success)
            {
                try
                {
                    var rateJson = JsonDocument.Parse(rateLimitResult.Output);
                    var resources = rateJson.RootElement.GetProperty("resources");
                    var core = resources.GetProperty("core");
                    
                    usageInfo.ApiLimit = core.GetProperty("limit").GetInt32();
                    usageInfo.ApiRemaining = core.GetProperty("remaining").GetInt32();
                    usageInfo.ApiUsed = usageInfo.ApiLimit - usageInfo.ApiRemaining;
                    usageInfo.UsagePercentage = usageInfo.ApiLimit > 0 
                        ? (double)usageInfo.ApiUsed / usageInfo.ApiLimit * 100 
                        : 0;
                    
                    var resetTimestamp = core.GetProperty("reset").GetInt64();
                    usageInfo.ResetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp).LocalDateTime;
                }
                catch { }
            }
            
            return usageInfo;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get Copilot usage: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Get list of user's repositories
    /// </summary>
    public async Task<List<GitHubRepository>> GetRepositoriesAsync(int limit = 30)
    {
        var repositories = new List<GitHubRepository>();
        
        try
        {
            var result = await RunGhCommandAsync($"repo list --limit {limit} --json name,owner,description,url,isPrivate,isFork,stargazerCount,forkCount,primaryLanguage,updatedAt");
            
            if (!result.Success)
            {
                return repositories;
            }
            
            var reposJson = JsonDocument.Parse(result.Output);
            
            foreach (var repo in reposJson.RootElement.EnumerateArray())
            {
                var repository = new GitHubRepository
                {
                    Name = repo.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Description = repo.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    Url = repo.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                    IsPrivate = repo.TryGetProperty("isPrivate", out var priv) && priv.GetBoolean(),
                    IsFork = repo.TryGetProperty("isFork", out var fork) && fork.GetBoolean(),
                    Stars = repo.TryGetProperty("stargazerCount", out var stars) ? stars.GetInt32() : 0,
                    Forks = repo.TryGetProperty("forkCount", out var forks) ? forks.GetInt32() : 0
                };
                
                if (repo.TryGetProperty("owner", out var owner) && 
                    owner.TryGetProperty("login", out var ownerLogin))
                {
                    repository.Owner = ownerLogin.GetString() ?? "";
                }
                
                if (repo.TryGetProperty("primaryLanguage", out var lang) && 
                    lang.ValueKind != JsonValueKind.Null &&
                    lang.TryGetProperty("name", out var langName))
                {
                    repository.Language = langName.GetString() ?? "";
                }
                
                if (repo.TryGetProperty("updatedAt", out var updated))
                {
                    if (DateTime.TryParse(updated.GetString(), out var updatedDate))
                    {
                        repository.UpdatedAt = updatedDate;
                    }
                }
                
                repositories.Add(repository);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get repositories: {ex.Message}");
        }
        
        return repositories;
    }
    
    /// <summary>
    /// Clone a repository
    /// </summary>
    public async Task<bool> CloneRepositoryAsync(string repoUrl, string? targetPath = null)
    {
        try
        {
            var args = $"repo clone {repoUrl}";
            if (!string.IsNullOrEmpty(targetPath))
            {
                args += $" \"{targetPath}\"";
            }
            
            var result = await RunGhCommandAsync(args);
            return result.Success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Clone failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Open repository in browser
    /// </summary>
    public async Task OpenRepositoryInBrowserAsync(string repoFullName)
    {
        try
        {
            await RunGhCommandAsync($"repo view {repoFullName} --web");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to open repository: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Install GitHub Copilot CLI extension
    /// </summary>
    public async Task<bool> InstallCopilotExtensionAsync()
    {
        try
        {
            var result = await RunGhCommandAsync("extension install github/gh-copilot");
            return result.Success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to install Copilot extension: {ex.Message}");
            return false;
        }
    }
    
    private async Task<GhCommandResult> RunGhCommandAsync(string arguments, int timeoutMs = 10000)
    {
        var result = new GhCommandResult();
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = SettingsPanelControl.ResolveGhExe(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            using var process = new Process { StartInfo = startInfo };
            
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Wait with timeout to prevent hanging
            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout - kill the process
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
                
                result.Success = false;
                result.Error = "Command timed out";
                return result;
            }
            
            result.Success = process.ExitCode == 0;
            result.ExitCode = process.ExitCode;
            result.Output = outputBuilder.ToString();
            result.Error = errorBuilder.ToString();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        
        return result;
    }
    
    private class GhCommandResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
    }
}

/// <summary>
/// GitHub account information
/// </summary>
public class GitHubAccountInfo
{
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public int PublicRepos { get; set; }
    public int Followers { get; set; }
    public int Following { get; set; }
    public bool IsLoggedIn { get; set; }
}

/// <summary>
/// Copilot usage information
/// </summary>
public class CopilotUsageInfo
{
    public bool IsAvailable { get; set; }
    public bool HasExtension { get; set; }
    public string Status { get; set; } = "";
    public int ApiLimit { get; set; }
    public int ApiRemaining { get; set; }
    public int ApiUsed { get; set; }
    public double UsagePercentage { get; set; }
    public DateTime? ResetTime { get; set; }
}

/// <summary>
/// GitHub repository information
/// </summary>
public class GitHubRepository
{
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string Language { get; set; } = "";
    public bool IsPrivate { get; set; }
    public bool IsFork { get; set; }
    public int Stars { get; set; }
    public int Forks { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    public string FullName => $"{Owner}/{Name}";
}

