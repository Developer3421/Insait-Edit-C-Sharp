using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for managing GitHub account, Copilot usage, and repositories
/// </summary>
public class GitHubAccountService
{
    private readonly GitHubOAuthService _oauthService = new();

    /// <summary>
    /// Event raised when account status changes
    /// </summary>
    public event EventHandler<GitHubAccountInfo?>? AccountChanged;
    
    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    public event EventHandler<DeviceCodeInfo>? DeviceCodeReady;
    public event EventHandler<string>? LoginStatusChanged;

    private GitHubAccountInfo? _currentAccount;
    
    /// <summary>
    /// Current logged in account info
    /// </summary>
    public GitHubAccountInfo? CurrentAccount => _currentAccount;

    public GitHubAccountService()
    {
        _oauthService.AccountChanged += (_, account) =>
        {
            _currentAccount = account;
            AccountChanged?.Invoke(this, account);
        };

        _oauthService.ErrorOccurred += (_, error) => ErrorOccurred?.Invoke(this, error);
        _oauthService.DeviceCodeReady += (_, deviceCode) => DeviceCodeReady?.Invoke(this, deviceCode);
        _oauthService.LoginStatusChanged += (_, status) => LoginStatusChanged?.Invoke(this, status);
    }
    
    /// <summary>
    /// Legacy compatibility method. Authentication no longer depends on GitHub CLI.
    /// </summary>
    public Task<bool> IsGitHubCliInstalledAsync() => Task.FromResult(true);
    
    /// <summary>
    /// Check if user is logged in to GitHub
    /// </summary>
    public async Task<bool> IsLoggedInAsync()
    {
        return await _oauthService.IsLoggedInAsync();
    }
    
    /// <summary>
    /// Login to GitHub
    /// </summary>
    public async Task<bool> LoginAsync()
    {
        return await _oauthService.LoginWithDeviceFlowAsync();
    }

    public void CancelLogin()
    {
        _oauthService.CancelLogin();
    }
    
    /// <summary>
    /// Logout from GitHub
    /// </summary>
    public async Task<bool> LogoutAsync()
    {
        var success = await _oauthService.LogoutAsync();
        if (success)
        {
            _currentAccount = null;
            AccountChanged?.Invoke(this, null);
        }
        return success;
    }
    
    /// <summary>
    /// Get account information
    /// </summary>
    public async Task<GitHubAccountInfo?> GetAccountInfoAsync()
    {
        var accountInfo = await _oauthService.GetAccountInfoAsync();
        _currentAccount = accountInfo;
        return accountInfo;
    }
    
    /// <summary>
    /// Refresh account information
    /// </summary>
    public async Task RefreshAccountInfoAsync()
    {
        await _oauthService.RefreshAccountInfoAsync();
    }
    
    /// <summary>
    /// Get Copilot usage/subscription status
    /// </summary>
    public async Task<CopilotUsageInfo?> GetCopilotUsageAsync()
    {
        return await _oauthService.GetCopilotUsageAsync();
    }
    
    /// <summary>
    /// Get list of user's repositories
    /// </summary>
    public async Task<List<GitHubRepository>> GetRepositoriesAsync(int limit = 30)
    {
        return await _oauthService.GetRepositoriesAsync(limit);
    }
    
    /// <summary>
    /// Clone a repository
    /// </summary>
    public async Task<bool> CloneRepositoryAsync(string repoUrl, string? targetPath = null)
    {
        return await _oauthService.CloneRepositoryAsync(repoUrl, targetPath);
    }
    
    /// <summary>
    /// Open repository in browser
    /// </summary>
    public async Task OpenRepositoryInBrowserAsync(string repoFullName)
    {
        try
        {
            var url = repoFullName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      repoFullName.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? repoFullName
                : $"https://github.com/{repoFullName.Trim('/')}";

            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to open repository: {ex.Message}");
        }
    }

    public async Task<GitHubRepository?> CreateRepositoryAsync(string name, string description, bool isPrivate)
    {
        return await _oauthService.CreateRepositoryAsync(name, description, isPrivate);
    }

    public string NormalizeGitHubRepositoryUrl(string repositoryUrl)
    {
        return _oauthService.NormalizeGitHubRepositoryUrl(repositoryUrl);
    }

    public string? GetAuthenticatedGitUrl(string repositoryUrl)
    {
        return _oauthService.GetAuthenticatedGitUrl(repositoryUrl);
    }

    public DeviceCodeInfo? GetCurrentDeviceCode()
    {
        return _oauthService.CurrentDeviceCode;
    }

    public bool TryOpenBrowser(string url)
    {
        return _oauthService.TryOpenBrowser(url);
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
    public string CloneUrl { get; set; } = "";
    public string Language { get; set; } = "";
    public bool IsPrivate { get; set; }
    public bool IsFork { get; set; }
    public int Stars { get; set; }
    public int Forks { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    public string FullName => $"{Owner}/{Name}";
}

