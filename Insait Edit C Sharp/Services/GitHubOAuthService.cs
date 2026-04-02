using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Octokit;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for GitHub OAuth authentication and API access using Octokit.
/// Uses GitHub OAuth Device Flow, so no GitHub CLI or local callback server is required.
/// </summary>
public class GitHubOAuthService
{
    // GitHub OAuth App settings.
    // For this public desktop client only Client ID is required for Device Flow.
    private const string ClientId = "Ov23li5blYJZ2z5pqfvT";
    private const string AppProductName = "InsaitEditIDECSharp";
    private const string DefaultScope = "repo read:user user read:org";
    
    private readonly GitHubClient _client;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private CancellationTokenSource? _loginCts;
    private DeviceCodeInfo? _currentDeviceCode;
    public event EventHandler<GitHubAccountInfo?>? AccountChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReady;
    public event EventHandler<string>? LoginStatusChanged;
    
    private GitHubAccountInfo? _currentAccount;
    
    public GitHubAccountInfo? CurrentAccount => _currentAccount;
    public DeviceCodeInfo? CurrentDeviceCode => _currentDeviceCode;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public GitHubOAuthService()
    {
        _client = new GitHubClient(new Octokit.ProductHeaderValue(AppProductName));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppProductName, "1.0"));
        
        LoadSavedToken();
    }
    
    private void LoadSavedToken()
    {
        try
        {
            var savedToken = GitHubTokenDbService.LoadAccessToken();
            if (!string.IsNullOrWhiteSpace(savedToken))
            {
                _accessToken = savedToken;
                _client.Credentials = new Credentials(_accessToken);
            }
        }
        catch { }
    }
    
    private void SaveToken(string token)
    {
        try
        {
            GitHubTokenDbService.SaveAccessToken(token);
        }
        catch { }
    }
    
    private void ClearSavedToken()
    {
        try
        {
            GitHubTokenDbService.ClearAccessToken();
        }
        catch { }
    }
    
    /// <summary>
    /// Starts GitHub OAuth Device Flow: shows a user code, opens the browser,
    /// then polls GitHub until authorization is completed.
    /// </summary>
    public async Task<bool> LoginWithDeviceFlowAsync()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource();
        _currentDeviceCode = null;
        
        try
        {
            LoginStatusChanged?.Invoke(this, "Requesting GitHub device code...");

            var deviceCode = await RequestDeviceCodeAsync(_loginCts.Token);
            if (deviceCode == null)
            {
                return false;
            }

            _loginCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(deviceCode.ExpiresIn, 60) + 15));

            var verificationUrl = !string.IsNullOrWhiteSpace(deviceCode.VerificationUriComplete)
                ? deviceCode.VerificationUriComplete
                : deviceCode.VerificationUri;

            if (!string.IsNullOrWhiteSpace(verificationUrl))
            {
                deviceCode.BrowserOpenSucceeded = TryOpenBrowser(verificationUrl);
                if (!deviceCode.BrowserOpenSucceeded)
                {
                    LoginStatusChanged?.Invoke(this,
                        $"Browser did not open automatically. Open {deviceCode.VerificationUri} and enter code {deviceCode.UserCode}.");
                }
            }

            _currentDeviceCode = deviceCode;
            DeviceCodeReady?.Invoke(this, deviceCode);

            if (deviceCode.BrowserOpenSucceeded)
                LoginStatusChanged?.Invoke(this, "Confirm sign-in in your browser for Insait Edit IDE C#...");
            var accessToken = await PollForDeviceFlowAccessTokenAsync(deviceCode, _loginCts.Token);
            
            if (string.IsNullOrEmpty(accessToken))
            {
                return false;
            }
            
            _accessToken = accessToken;
            _client.Credentials = new Credentials(accessToken);
            SaveToken(accessToken);
            _currentDeviceCode = null;
            
            LoginStatusChanged?.Invoke(this, "Loading account info...");
            await RefreshAccountInfoAsync();
            
            LoginStatusChanged?.Invoke(this, "Successfully signed in!");
            return true;
        }
        catch (OperationCanceledException)
        {
            _currentDeviceCode = null;
            LoginStatusChanged?.Invoke(this, "Login cancelled or timed out");
            return false;
        }
        catch (Exception ex)
        {
            _currentDeviceCode = null;
            ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
            return false;
        }
    }

    private async Task<DeviceCodeInfo?> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"] = DefaultScope
                })
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ErrorOccurred?.Invoke(this, $"Failed to request device code: {responseBody}");
                return null;
            }

            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;

            if (!root.TryGetProperty("device_code", out var deviceCodeProp) ||
                !root.TryGetProperty("user_code", out var userCodeProp) ||
                !root.TryGetProperty("verification_uri", out var verificationUriProp))
            {
                ErrorOccurred?.Invoke(this, "GitHub did not return a complete device code response.");
                return null;
            }

            return new DeviceCodeInfo
            {
                DeviceCode = deviceCodeProp.GetString() ?? string.Empty,
                UserCode = userCodeProp.GetString() ?? string.Empty,
                VerificationUri = verificationUriProp.GetString() ?? "https://github.com/login/device",
                VerificationUriComplete = root.TryGetProperty("verification_uri_complete", out var completeUriProp)
                    ? completeUriProp.GetString() ?? string.Empty
                    : string.Empty,
                ExpiresIn = root.TryGetProperty("expires_in", out var expiresInProp) ? expiresInProp.GetInt32() : 900,
                Interval = root.TryGetProperty("interval", out var intervalProp) ? intervalProp.GetInt32() : 5
            };
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to start device flow: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> PollForDeviceFlowAccessTokenAsync(DeviceCodeInfo deviceCode, CancellationToken cancellationToken)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(deviceCode.ExpiresIn, 60));
        var intervalSeconds = Math.Max(deviceCode.Interval, 5);

        while (DateTime.UtcNow < expiresAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["device_code"] = deviceCode.DeviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    })
                };

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    ErrorOccurred?.Invoke(this, $"Token polling failed: {responseBody}");
                    return null;
                }

                using var json = JsonDocument.Parse(responseBody);
                var root = json.RootElement;

                if (root.TryGetProperty("access_token", out var tokenProp))
                    return tokenProp.GetString();

                if (!root.TryGetProperty("error", out var errorProp))
                    continue;

                var error = errorProp.GetString() ?? string.Empty;
                switch (error)
                {
                    case "authorization_pending":
                        LoginStatusChanged?.Invoke(this, $"Waiting for GitHub confirmation… Use code {deviceCode.UserCode}.");
                        continue;
                    case "slow_down":
                        intervalSeconds += 5;
                        LoginStatusChanged?.Invoke(this, "GitHub asked to slow down polling. Waiting a bit longer...");
                        continue;
                    case "access_denied":
                        LoginStatusChanged?.Invoke(this, "GitHub authorization was denied.");
                        return null;
                    case "expired_token":
                        LoginStatusChanged?.Invoke(this, "GitHub device code expired. Please try again.");
                        return null;
                    default:
                        var description = root.TryGetProperty("error_description", out var descriptionProp)
                            ? descriptionProp.GetString()
                            : null;
                        ErrorOccurred?.Invoke(this, string.IsNullOrWhiteSpace(description)
                            ? $"GitHub authorization error: {error}"
                            : $"GitHub authorization error: {error} - {description}");
                        return null;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed while waiting for GitHub authorization: {ex.Message}");
                return null;
            }
        }

        LoginStatusChanged?.Invoke(this, "GitHub device code expired. Please try again.");
        return null;
    }
    
    public void CancelLogin()
    {
        _loginCts?.Cancel();
        _currentDeviceCode = null;
    }

    public bool TryOpenBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            if (Process.Start(psi) != null)
                return true;
        }
        catch (Exception ex)
        {
            if (ex is not Win32Exception)
                ErrorOccurred?.Invoke(this, $"Failed to open browser directly: {ex.Message}");
        }

        try
        {
            var explorerStart = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{url}\"",
                UseShellExecute = true
            };
            if (Process.Start(explorerStart) != null)
                return true;
        }
        catch (Exception ex)
        {
            if (ex is not Win32Exception)
                ErrorOccurred?.Invoke(this, $"Failed to open browser via Explorer: {ex.Message}");
        }

        try
        {
            var cmdStart = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (Process.Start(cmdStart) != null)
                return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to open browser: {ex.Message}");
        }

        ErrorOccurred?.Invoke(this, $"Failed to open browser automatically. Open this URL manually: {url}");
        return false;
    }
    
    public async Task<bool> IsLoggedInAsync()
    {
        if (string.IsNullOrEmpty(_accessToken))
            return false;
            
        try
        {
            var user = await _client.User.Current();
            return user != null;
        }
        catch
        {
            _accessToken = null;
            _client.Credentials = Credentials.Anonymous;
            ClearSavedToken();
            return false;
        }
    }
    
    public async Task<bool> LoginWithTokenAsync(string personalAccessToken)
    {
        try
        {
            _client.Credentials = new Credentials(personalAccessToken);
            
            var user = await _client.User.Current();
            if (user != null)
            {
                _accessToken = personalAccessToken;
                SaveToken(personalAccessToken);
                await RefreshAccountInfoAsync();
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
            _client.Credentials = Credentials.Anonymous;
            return false;
        }
    }
    
    public Task<bool> LogoutAsync()
    {
        _accessToken = null;
        _currentAccount = null;
        _client.Credentials = Credentials.Anonymous;
        ClearSavedToken();
        AccountChanged?.Invoke(this, null);
        return Task.FromResult(true);
    }
    
    public async Task<GitHubAccountInfo?> GetAccountInfoAsync()
    {
        if (!IsAuthenticated)
            return null;
            
        try
        {
            var user = await _client.User.Current();
            
            var accountInfo = new GitHubAccountInfo
            {
                Username = user.Login ?? "",
                Name = user.Name ?? "",
                Email = user.Email ?? "",
                AvatarUrl = user.AvatarUrl ?? "",
                Bio = user.Bio ?? "",
                Company = user.Company ?? "",
                Location = user.Location ?? "",
                PublicRepos = user.PublicRepos,
                Followers = user.Followers,
                Following = user.Following,
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
    
    public async Task RefreshAccountInfoAsync()
    {
        await GetAccountInfoAsync();
    }
    
    public async Task<CopilotUsageInfo?> GetCopilotUsageAsync()
    {
        if (!IsAuthenticated)
            return null;
            
        try
        {
            var rateLimit = await _client.RateLimit.GetRateLimits();
            
            return new CopilotUsageInfo
            {
                IsAvailable = true,
                HasExtension = false,
                Status = "Active",
                ApiLimit = rateLimit.Resources.Core.Limit,
                ApiRemaining = rateLimit.Resources.Core.Remaining,
                ApiUsed = rateLimit.Resources.Core.Limit - rateLimit.Resources.Core.Remaining,
                UsagePercentage = rateLimit.Resources.Core.Limit > 0 
                    ? (double)(rateLimit.Resources.Core.Limit - rateLimit.Resources.Core.Remaining) / rateLimit.Resources.Core.Limit * 100 
                    : 0,
                ResetTime = rateLimit.Resources.Core.Reset.LocalDateTime
            };
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get Copilot usage: {ex.Message}");
            return new CopilotUsageInfo { IsAvailable = false, Status = "Unknown" };
        }
    }
    
    public async Task<List<GitHubRepository>> GetRepositoriesAsync(int limit = 30)
    {
        var repositories = new List<GitHubRepository>();
        
        if (!IsAuthenticated)
            return repositories;
            
        try
        {
            var repos = await _client.Repository.GetAllForCurrent(new RepositoryRequest
            {
                Sort = RepositorySort.Updated,
                Direction = SortDirection.Descending
            });
            
            var count = 0;
            foreach (var repo in repos)
            {
                if (count >= limit) break;
                
                repositories.Add(new GitHubRepository
                {
                    Name = repo.Name ?? "",
                    Owner = repo.Owner?.Login ?? "",
                    Description = repo.Description ?? "",
                    Url = repo.HtmlUrl ?? "",
                    CloneUrl = repo.CloneUrl ?? NormalizeGitHubRepositoryUrl(repo.HtmlUrl ?? ""),
                    Language = repo.Language ?? "",
                    IsPrivate = repo.Private,
                    IsFork = repo.Fork,
                    Stars = repo.StargazersCount,
                    Forks = repo.ForksCount,
                    UpdatedAt = repo.UpdatedAt.LocalDateTime
                });
                
                count++;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to get repositories: {ex.Message}");
        }
        
        return repositories;
    }
    
    public async Task<bool> CloneRepositoryAsync(string repoUrl, string? targetPath = null)
    {
        try
        {
            var cleanUrl = NormalizeGitHubRepositoryUrl(repoUrl);
            var authenticatedUrl = GetAuthenticatedGitUrl(cleanUrl) ?? cleanUrl;
            var args = targetPath != null
                ? $"clone \"{authenticatedUrl}\" \"{targetPath}\""
                : $"clone \"{authenticatedUrl}\"";

            var success = await RunGitProcessAsync(args);
            if (!success)
                return false;

            if (!string.IsNullOrWhiteSpace(targetPath) &&
                !string.Equals(cleanUrl, authenticatedUrl, StringComparison.Ordinal) &&
                Directory.Exists(targetPath))
            {
                await RunGitProcessAsync($"remote set-url origin \"{cleanUrl}\"", targetPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Clone failed: {ex.Message}");
            return false;
        }
    }
    
    public void OpenRepositoryInBrowser(string repoUrl)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = repoUrl,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to open repository: {ex.Message}");
        }
    }
    
    public void OpenTokenCreationPage()
    {
        try
        {
            var url = "https://github.com/settings/tokens/new?description=Insait%20Edit%20IDE%20C%23&scopes=repo,user,read:org";
            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to open browser: {ex.Message}");
        }
    }

    public async Task<GitHubRepository?> CreateRepositoryAsync(string name, string? description, bool isPrivate)
    {
        if (!await IsLoggedInAsync())
        {
            ErrorOccurred?.Invoke(this, "You must sign in to GitHub before creating a repository.");
            return null;
        }

        try
        {
            var repository = await _client.Repository.Create(new NewRepository(name)
            {
                Description = description ?? string.Empty,
                Private = isPrivate,
                AutoInit = false
            });

            return new GitHubRepository
            {
                Name = repository.Name ?? string.Empty,
                Owner = repository.Owner?.Login ?? string.Empty,
                Description = repository.Description ?? string.Empty,
                Url = repository.HtmlUrl ?? string.Empty,
                CloneUrl = repository.CloneUrl ?? NormalizeGitHubRepositoryUrl(repository.HtmlUrl ?? string.Empty),
                Language = repository.Language ?? string.Empty,
                IsPrivate = repository.Private,
                IsFork = repository.Fork,
                Stars = repository.StargazersCount,
                Forks = repository.ForksCount,
                UpdatedAt = repository.UpdatedAt.LocalDateTime
            };
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to create repository: {ex.Message}");
            return null;
        }
    }

    public string NormalizeGitHubRepositoryUrl(string repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            return repositoryUrl;

        var trimmed = repositoryUrl.Trim();

        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            var path = trimmed["git@github.com:".Length..].Trim();
            if (!path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                path += ".git";
            return $"https://github.com/{path}";
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.AbsolutePath.Trim('/');
            if (!path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                path += ".git";
            return $"https://github.com/{path}";
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            var pathOnly = trimmed.Trim('/');
            if (pathOnly.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2)
                return $"https://github.com/{pathOnly}.git";
        }

        return trimmed;
    }

    public string GetAuthenticatedGitUrl(string repositoryUrl)
    {
        var cleanUrl = NormalizeGitHubRepositoryUrl(repositoryUrl);
        if (string.IsNullOrWhiteSpace(cleanUrl) || string.IsNullOrWhiteSpace(_accessToken))
            return cleanUrl;

        if (!Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return cleanUrl;
        }

        var path = uri.AbsolutePath.TrimStart('/');
        return $"https://x-access-token:{Uri.EscapeDataString(_accessToken)}@github.com/{path}";
    }

    private async Task<bool> RunGitProcessAsync(string arguments, string? workingDirectory = null)
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

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        using var process = Process.Start(startInfo);
        if (process == null)
            return false;

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(stderr) && process.ExitCode != 0)
            ErrorOccurred?.Invoke(this, stderr.Trim());

        if (!string.IsNullOrWhiteSpace(stdout) && process.ExitCode != 0)
            ErrorOccurred?.Invoke(this, stdout.Trim());

        return process.ExitCode == 0;
    }
}

/// <summary>
/// Device code information for OAuth device flow
/// </summary>
public class DeviceCodeInfo
{
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string VerificationUriComplete { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
    public bool BrowserOpenSucceeded { get; set; }
}

