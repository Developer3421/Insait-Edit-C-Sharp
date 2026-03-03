using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Insait_Edit_C_Sharp.Services;
using System.Net.Http;
using System.IO;

namespace Insait_Edit_C_Sharp.Controls;

public partial class AccountPanelControl : UserControl
{
    private readonly GitHubAccountService _accountService;
    private readonly GitHubOAuthService _oauthService;
    private List<GitHubRepository> _allRepositories = new();
    private List<GitHubRepository> _filteredRepositories = new();
    private int _loadedRepoCount = 30;
    private bool _useOAuthService = false;
    
    /// <summary>
    /// Event raised when a repository is selected for cloning
    /// </summary>
    public event EventHandler<GitHubRepository>? RepositoryCloneRequested;
    
    /// <summary>
    /// Event raised when a repository is selected to open
    /// </summary>
    public event EventHandler<GitHubRepository>? RepositoryOpenRequested;
    
    /// <summary>
    /// Event raised when status changes
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    public AccountPanelControl()
    {
        InitializeComponent();
        _accountService = new GitHubAccountService();
        _oauthService = new GitHubOAuthService();
        
        _accountService.AccountChanged += OnAccountChanged;
        _accountService.ErrorOccurred += OnErrorOccurred;
        
        _oauthService.AccountChanged += OnAccountChanged;
        _oauthService.ErrorOccurred += OnErrorOccurred;
        _oauthService.LoginStatusChanged += OnLoginStatusChanged;
        _oauthService.DeviceCodeReady += OnDeviceCodeReady;
        
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }
    
    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        var header = this.FindControl<TextBlock>("HeaderTitleText");
        if (header != null) header.Text = L("Account.Title");
    }
    
    private void OnLoginStatusChanged(object? sender, string status)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var loadingText = this.FindControl<TextBlock>("LoadingText");
            if (loadingText != null)
            {
                loadingText.Text = status;
            }
            StatusChanged?.Invoke(this, status);
        });
    }
    
    private void OnDeviceCodeReady(object? sender, DeviceCodeInfo deviceCode)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Show the device code to user
            var codeText = this.FindControl<TextBlock>("DeviceCodeText");
            var codePanel = this.FindControl<StackPanel>("DeviceCodePanel");
            
            if (codeText != null)
            {
                codeText.Text = deviceCode.UserCode;
            }
            
            if (codePanel != null)
            {
                codePanel.IsVisible = true;
            }
            
            StatusChanged?.Invoke(this, $"Enter code {deviceCode.UserCode} on GitHub");
        });
    }

    /// <summary>
    /// Initialize the panel and check login status
    /// </summary>
    public async Task InitializeAsync()
    {
        ShowLoading("Checking authentication...");
        
        try
        {
            // First try OAuth service (saved token)
            var oauthLoggedIn = await _oauthService.IsLoggedInAsync();
            if (oauthLoggedIn)
            {
                _useOAuthService = true;
                await RefreshAccountDataAsync();
                return;
            }
            
            // Try GitHub CLI as fallback
            ShowLoading("Checking GitHub CLI...");
            var isInstalled = await _accountService.IsGitHubCliInstalledAsync();
            if (isInstalled)
            {
                var isLoggedIn = await _accountService.IsLoggedInAsync();
                if (isLoggedIn)
                {
                    _useOAuthService = false;
                    await RefreshAccountDataAsync();
                    return;
                }
            }
            
            // Not logged in - show login panel
            ShowNotLoggedIn();
            StatusChanged?.Invoke(this, "Not logged in. Sign in with GitHub or use a Personal Access Token.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
            ShowNotLoggedIn();
        }
        finally
        {
            HideLoading();
        }
    }

    private async Task RefreshAccountDataAsync()
    {
        ShowLoading("Loading account data...");
        
        try
        {
            GitHubAccountInfo? accountInfo;
            
            if (_useOAuthService)
            {
                accountInfo = await _oauthService.GetAccountInfoAsync();
                if (accountInfo == null || !accountInfo.IsLoggedIn)
                {
                    ShowNotLoggedIn();
                    return;
                }
                
                UpdateAccountUI(accountInfo);
                
                ShowLoading("Loading repositories...");
                _allRepositories = await _oauthService.GetRepositoriesAsync(_loadedRepoCount);
            }
            else
            {
                // Use CLI service
                accountInfo = await _accountService.GetAccountInfoAsync();
                if (accountInfo == null || !accountInfo.IsLoggedIn)
                {
                    ShowNotLoggedIn();
                    return;
                }
                
                UpdateAccountUI(accountInfo);
                
                ShowLoading("Loading repositories...");
                _allRepositories = await _accountService.GetRepositoriesAsync(_loadedRepoCount);
            }
            
            _filteredRepositories = new List<GitHubRepository>(_allRepositories);
            UpdateRepositoriesUI();
            
            ShowLoggedIn();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private void UpdateAccountUI(GitHubAccountInfo account)
    {
        var nameText = this.FindControl<TextBlock>("UserNameText");
        var loginText = this.FindControl<TextBlock>("UserLoginText");
        var reposCount = this.FindControl<TextBlock>("ReposCountText");
        var followersCount = this.FindControl<TextBlock>("FollowersCountText");
        var followingCount = this.FindControl<TextBlock>("FollowingCountText");
        
        if (nameText != null)
        {
            nameText.Text = !string.IsNullOrEmpty(account.Name) ? account.Name : account.Username;
        }
        
        if (loginText != null)
        {
            loginText.Text = $"@{account.Username}";
        }
        
        if (reposCount != null)
        {
            reposCount.Text = account.PublicRepos.ToString();
        }
        
        if (followersCount != null)
        {
            followersCount.Text = account.Followers.ToString();
        }
        
        if (followingCount != null)
        {
            followingCount.Text = account.Following.ToString();
        }
        
        // Load avatar image
        if (!string.IsNullOrEmpty(account.AvatarUrl))
        {
            LoadAvatarAsync(account.AvatarUrl);
        }
    }

    private async void LoadAvatarAsync(string avatarUrl)
    {
        try
        {
            var avatarImage = this.FindControl<Image>("AvatarImage");
            var defaultIcon = this.FindControl<TextBlock>("DefaultAvatarIcon");
            
            if (avatarImage == null) return;
            
            using var httpClient = new HttpClient();
            var imageBytes = await httpClient.GetByteArrayAsync(avatarUrl);
            
            using var stream = new MemoryStream(imageBytes);
            var bitmap = new Bitmap(stream);
            
            avatarImage.Source = bitmap;
            
            if (defaultIcon != null)
            {
                defaultIcon.IsVisible = false;
            }
        }
        catch
        {
            // Keep default icon on failure
        }
    }


    private void UpdateRepositoriesUI()
    {
        var listBox = this.FindControl<ItemsControl>("RepositoriesListBox");
        if (listBox != null)
        {
            listBox.ItemsSource = _filteredRepositories;
        }
    }

    #region UI State Methods
    
    private void ShowNotLoggedIn()
    {
        var notLoggedIn = this.FindControl<StackPanel>("NotLoggedInPanel");
        var loggedIn = this.FindControl<StackPanel>("LoggedInPanel");
        
        if (notLoggedIn != null) notLoggedIn.IsVisible = true;
        if (loggedIn != null) loggedIn.IsVisible = false;
    }
    
    private void ShowLoggedIn()
    {
        var notLoggedIn = this.FindControl<StackPanel>("NotLoggedInPanel");
        var loggedIn = this.FindControl<StackPanel>("LoggedInPanel");
        
        if (notLoggedIn != null) notLoggedIn.IsVisible = false;
        if (loggedIn != null) loggedIn.IsVisible = true;
    }
    
    private void ShowLoading(string message = "Loading...")
    {
        var overlay = this.FindControl<Border>("LoadingOverlay");
        var loadingText = this.FindControl<TextBlock>("LoadingText");
        
        if (overlay != null) overlay.IsVisible = true;
        if (loadingText != null) loadingText.Text = message;
    }
    
    private void HideLoading()
    {
        var overlay = this.FindControl<Border>("LoadingOverlay");
        if (overlay != null) overlay.IsVisible = false;
    }
    
    #endregion

    #region Event Handlers
    
    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        // Check if token input panel is visible (user wants to use token)
        var tokenInput = this.FindControl<TextBox>("TokenInputBox");
        var tokenPanel = this.FindControl<StackPanel>("TokenInputPanel");
        
        if (tokenPanel != null && tokenPanel.IsVisible)
        {
            // Token panel is visible - try to authenticate with token
            var token = tokenInput?.Text?.Trim();
            if (!string.IsNullOrEmpty(token))
            {
                ShowLoading("Authenticating with token...");
                StatusChanged?.Invoke(this, "Authenticating with GitHub...");
                
                try
                {
                    var success = await _oauthService.LoginWithTokenAsync(token);
                    if (success)
                    {
                        _useOAuthService = true;
                        await RefreshAccountDataAsync();
                        StatusChanged?.Invoke(this, "Successfully signed in to GitHub");
                        
                        // Clear token input and hide panel
                        if (tokenInput != null) tokenInput.Text = "";
                        if (tokenPanel != null) tokenPanel.IsVisible = false;
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "Invalid token. Please check and try again.");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Authentication error: {ex.Message}");
                }
                finally
                {
                    HideLoading();
                }
            }
            else
            {
                StatusChanged?.Invoke(this, "Please enter a Personal Access Token.");
            }
        }
        else
        {
            // Use Device Flow OAuth - opens browser automatically
            ShowLoading("Starting GitHub authentication...");
            
            try
            {
                var success = await _oauthService.LoginWithDeviceFlowAsync();
                if (success)
                {
                    _useOAuthService = true;
                    
                    // Hide device code panel if shown
                    var codePanel = this.FindControl<StackPanel>("DeviceCodePanel");
                    if (codePanel != null) codePanel.IsVisible = false;
                    
                    await RefreshAccountDataAsync();
                    StatusChanged?.Invoke(this, "Successfully signed in to GitHub");
                }
                else
                {
                    StatusChanged?.Invoke(this, "GitHub sign-in was cancelled or failed. Try using a token instead.");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Sign-in error: {ex.Message}");
            }
            finally
            {
                HideLoading();
                
                // Hide device code panel
                var codePanel = this.FindControl<StackPanel>("DeviceCodePanel");
                if (codePanel != null) codePanel.IsVisible = false;
            }
        }
    }
    
    private async void SignOut_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Signing out...");
        
        try
        {
            bool success;
            if (_useOAuthService)
            {
                success = await _oauthService.LogoutAsync();
            }
            else
            {
                success = await _accountService.LogoutAsync();
            }
            
            if (success)
            {
                _useOAuthService = false;
                ShowNotLoggedIn();
                StatusChanged?.Invoke(this, "Signed out from GitHub");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Sign-out error: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }
    
    private void InstallGh_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Open terminal with winget install command
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k winget install --id GitHub.cli",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
            
            StatusChanged?.Invoke(this, "Installing GitHub CLI via winget...");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to start installation: {ex.Message}");
        }
    }
    
    private void GetToken_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _oauthService.OpenTokenCreationPage();
            StatusChanged?.Invoke(this, "Opening GitHub token creation page...");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to open browser: {ex.Message}");
        }
    }
    
    private void ShowToken_Click(object? sender, RoutedEventArgs e)
    {
        var tokenPanel = this.FindControl<StackPanel>("TokenInputPanel");
        var showTokenButton = this.FindControl<Button>("ShowTokenButton");
        
        if (tokenPanel != null)
        {
            tokenPanel.IsVisible = !tokenPanel.IsVisible;
        }
        
        if (showTokenButton != null)
        {
            showTokenButton.Content = tokenPanel?.IsVisible == true ? "Hide Token Input" : "Use Token Instead";
        }
    }
    
    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        await InitializeAsync();
    }
    
    private void ViewOnGitHub_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to open browser: {ex.Message}");
        }
    }
    
    private void RepoSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var searchBox = this.FindControl<TextBox>("RepoSearchBox");
        var searchText = searchBox?.Text ?? "";
        
        if (string.IsNullOrWhiteSpace(searchText))
        {
            _filteredRepositories = new List<GitHubRepository>(_allRepositories);
        }
        else
        {
            _filteredRepositories = _allRepositories
                .Where(r => r.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                           r.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }
        
        UpdateRepositoriesUI();
    }
    
    private void RepoItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is GitHubRepository repo)
        {
            var props = e.GetCurrentPoint(border).Properties;
            
            if (props.IsLeftButtonPressed)
            {
                // Left click - open in browser
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = repo.Url,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    StatusChanged?.Invoke(this, $"Opening {repo.FullName} in browser...");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Failed to open repository: {ex.Message}");
                }
            }
            else if (props.IsRightButtonPressed)
            {
                // Right click - could show context menu for clone
                RepositoryCloneRequested?.Invoke(this, repo);
            }
        }
    }
    
    private async void LoadMore_Click(object? sender, RoutedEventArgs e)
    {
        _loadedRepoCount += 30;
        ShowLoading("Loading more repositories...");
        
        try
        {
            if (_useOAuthService)
            {
                _allRepositories = await _oauthService.GetRepositoriesAsync(_loadedRepoCount);
            }
            else
            {
                _allRepositories = await _accountService.GetRepositoriesAsync(_loadedRepoCount);
            }
            
            // Reapply filter
            var searchBox = this.FindControl<TextBox>("RepoSearchBox");
            var searchText = searchBox?.Text ?? "";
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredRepositories = new List<GitHubRepository>(_allRepositories);
            }
            else
            {
                _filteredRepositories = _allRepositories
                    .Where(r => r.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                               r.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }
            
            UpdateRepositoriesUI();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to load repositories: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }
    
    private void OnAccountChanged(object? sender, GitHubAccountInfo? account)
    {
        if (account == null || !account.IsLoggedIn)
        {
            ShowNotLoggedIn();
        }
        else
        {
            UpdateAccountUI(account);
            ShowLoggedIn();
        }
    }
    
    private void OnErrorOccurred(object? sender, string error)
    {
        StatusChanged?.Invoke(this, error);
    }
    
    #endregion
}

