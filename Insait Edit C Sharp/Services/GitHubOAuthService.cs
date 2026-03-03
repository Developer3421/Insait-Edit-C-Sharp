using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Octokit;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for GitHub OAuth authentication and API access using Octokit
/// Uses OAuth Web Flow with local HTTP callback server
/// </summary>
public class GitHubOAuthService
{
    // GitHub OAuth App credentials
    // Register your app at: https://github.com/settings/applications/new
    // Set callback URL to: http://localhost:8891/callback
    private const string ClientId = "Ov23liUYOYBvLHi79cSa"; // Replace with your OAuth App Client ID
    private const string ClientSecret = ""; // Leave empty for public clients
    private const string AppName = "InsaitEditor";
    private const string RedirectUri = "http://localhost:8891/callback";
    private const int CallbackPort = 8891;
    
    private readonly GitHubClient _client;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private readonly string _tokenFilePath;
    private CancellationTokenSource? _loginCts;
    private HttpListener? _httpListener;
    
    public event EventHandler<GitHubAccountInfo?>? AccountChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReady;
    public event EventHandler<string>? LoginStatusChanged;
    
    private GitHubAccountInfo? _currentAccount;
    
    public GitHubAccountInfo? CurrentAccount => _currentAccount;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public GitHubOAuthService()
    {
        _client = new GitHubClient(new ProductHeaderValue(AppName));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "InsaitEditor");
        Directory.CreateDirectory(appFolder);
        _tokenFilePath = Path.Combine(appFolder, "github_token.dat");
        
        LoadSavedToken();
    }
    
    private void LoadSavedToken()
    {
        try
        {
            if (File.Exists(_tokenFilePath))
            {
                var encryptedToken = File.ReadAllText(_tokenFilePath);
                _accessToken = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedToken));
                _client.Credentials = new Credentials(_accessToken);
            }
        }
        catch { }
    }
    
    private void SaveToken(string token)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
            File.WriteAllText(_tokenFilePath, encoded);
        }
        catch { }
    }
    
    private void ClearSavedToken()
    {
        try
        {
            if (File.Exists(_tokenFilePath))
                File.Delete(_tokenFilePath);
        }
        catch { }
    }
    
    /// <summary>
    /// Start OAuth login - opens browser and waits for callback
    /// </summary>
    public async Task<bool> LoginWithDeviceFlowAsync()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        
        try
        {
            LoginStatusChanged?.Invoke(this, "Starting authentication...");
            
            var state = Guid.NewGuid().ToString("N");
            
            // Start local HTTP server and wait for callback
            var code = await StartCallbackServerAndWaitForCodeAsync(state, _loginCts.Token);
            
            if (string.IsNullOrEmpty(code))
            {
                LoginStatusChanged?.Invoke(this, "Authorization cancelled or failed");
                return false;
            }
            
            LoginStatusChanged?.Invoke(this, "Exchanging code for token...");
            var accessToken = await ExchangeCodeForTokenAsync(code);
            
            if (string.IsNullOrEmpty(accessToken))
            {
                LoginStatusChanged?.Invoke(this, "Failed to get access token");
                return false;
            }
            
            _accessToken = accessToken;
            _client.Credentials = new Credentials(accessToken);
            SaveToken(accessToken);
            
            LoginStatusChanged?.Invoke(this, "Loading account info...");
            await RefreshAccountInfoAsync();
            
            LoginStatusChanged?.Invoke(this, "Successfully signed in!");
            return true;
        }
        catch (OperationCanceledException)
        {
            LoginStatusChanged?.Invoke(this, "Login cancelled or timed out");
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Login failed: {ex.Message}");
            return false;
        }
        finally
        {
            StopCallbackServer();
        }
    }
    
    private async Task<string?> StartCallbackServerAndWaitForCodeAsync(string expectedState, CancellationToken cancellationToken)
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{CallbackPort}/");
            _httpListener.Start();
            
            var authUrl = $"https://github.com/login/oauth/authorize?" +
                          $"client_id={ClientId}" +
                          $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                          $"&scope=repo%20user%20read:org" +
                          $"&state={expectedState}";
            
            OpenBrowser(authUrl);
            LoginStatusChanged?.Invoke(this, "Waiting for authorization in browser...");
            
            var contextTask = _httpListener.GetContextAsync();
            var completedTask = await Task.WhenAny(
                contextTask,
                Task.Delay(Timeout.Infinite, cancellationToken)
            );
            
            if (completedTask != contextTask)
                return null;
            
            var context = await contextTask;
            var request = context.Request;
            var response = context.Response;
            
            var query = request.Url?.Query ?? "";
            var queryParams = System.Web.HttpUtility.ParseQueryString(query);
            var code = queryParams["code"];
            var state = queryParams["state"];
            var error = queryParams["error"];
            
            string responseHtml;
            if (!string.IsNullOrEmpty(error))
            {
                responseHtml = GetErrorHtml(error);
                SendResponse(response, responseHtml);
                ErrorOccurred?.Invoke(this, $"Authorization error: {error}");
                return null;
            }
            
            if (state != expectedState)
            {
                responseHtml = GetErrorHtml("Invalid state - possible CSRF attack");
                SendResponse(response, responseHtml);
                return null;
            }
            
            if (string.IsNullOrEmpty(code))
            {
                responseHtml = GetErrorHtml("No authorization code received");
                SendResponse(response, responseHtml);
                return null;
            }
            
            responseHtml = GetSuccessHtml();
            SendResponse(response, responseHtml);
            
            return code;
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 995)
        {
            return null;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Callback server error: {ex.Message}");
            return null;
        }
    }
    
    private void StopCallbackServer()
    {
        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
            _httpListener = null;
        }
        catch { }
    }
    
    private void SendResponse(HttpListenerResponse response, string html)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch { }
    }
    
    private string GetSuccessHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
    <title>Authorization Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; 
               background: #1e1f22; color: #bcbec4; display: flex; 
               justify-content: center; align-items: center; height: 100vh; margin: 0; }
        .container { text-align: center; padding: 40px; }
        .icon { font-size: 64px; margin-bottom: 20px; }
        h1 { color: #6aab73; margin-bottom: 10px; }
        p { color: #6f737a; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='icon'>✅</div>
        <h1>Authorization Successful!</h1>
        <p>You can close this window and return to Insait Editor.</p>
    </div>
</body>
</html>";
    }
    
    private string GetErrorHtml(string error)
    {
        return @"<!DOCTYPE html>
<html>
<head>
    <title>Authorization Failed</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; 
               background: #1e1f22; color: #bcbec4; display: flex; 
               justify-content: center; align-items: center; height: 100vh; margin: 0; }
        .container { text-align: center; padding: 40px; }
        .icon { font-size: 64px; margin-bottom: 20px; }
        h1 { color: #ef5350; margin-bottom: 10px; }
        p { color: #6f737a; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='icon'>❌</div>
        <h1>Authorization Failed</h1>
        <p>" + System.Web.HttpUtility.HtmlEncode(error) + @"</p>
        <p>Please close this window and try again.</p>
    </div>
</body>
</html>";
    }
    
    private async Task<string?> ExchangeCodeForTokenAsync(string code)
    {
        try
        {
            var tokenRequest = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri
            };
            
            if (!string.IsNullOrEmpty(ClientSecret))
                tokenRequest["client_secret"] = ClientSecret;
            
            var content = new FormUrlEncodedContent(tokenRequest);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = content
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                ErrorOccurred?.Invoke(this, $"Token exchange failed: {responseBody}");
                return null;
            }
            
            var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;
            
            if (root.TryGetProperty("error", out var errorProp))
            {
                var error = errorProp.GetString();
                var description = root.TryGetProperty("error_description", out var descProp) 
                    ? descProp.GetString() : "";
                ErrorOccurred?.Invoke(this, $"Token error: {error} - {description}");
                return null;
            }
            
            if (root.TryGetProperty("access_token", out var tokenProp))
                return tokenProp.GetString();
            
            return null;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to exchange code: {ex.Message}");
            return null;
        }
    }
    
    public void CancelLogin()
    {
        _loginCts?.Cancel();
        StopCallbackServer();
    }
    
    private void OpenBrowser(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to open browser: {ex.Message}");
        }
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
            var args = targetPath != null 
                ? $"clone {repoUrl} \"{targetPath}\"" 
                : $"clone {repoUrl}";
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                UseShellExecute = true
            };
            
            var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            
            return false;
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
            var url = "https://github.com/settings/tokens/new?description=InsaitEditor&scopes=repo,user,read:org";
            var startInfo = new System.Diagnostics.ProcessStartInfo
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
}

/// <summary>
/// Device code information for OAuth device flow
/// </summary>
public class DeviceCodeInfo
{
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
}

