using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

public partial class GitPanelControl : UserControl
{
    private readonly GitService _gitService;
    private string? _repositoryPath;
    private GitStatus? _currentStatus;
    private string _currentTab = "localchanges";
    private StringBuilder _consoleOutput = new();
    
    public event EventHandler<string>? FileOpenRequested;
    public event EventHandler<string>? FileDiffRequested;
    public event EventHandler? CloneRepositoryRequested;
    public event EventHandler<string>? StatusChanged;

    public GitPanelControl()
    {
        InitializeComponent();
        _gitService = new GitService();
        
        // Subscribe to commit message changes
        var commitMessageBox = this.FindControl<TextBox>("CommitMessageBox");
        if (commitMessageBox != null)
        {
            commitMessageBox.TextChanged += CommitMessageBox_TextChanged;
            commitMessageBox.KeyDown += CommitMessageBox_KeyDown;
        }
        
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        var commitMsg = this.FindControl<TextBox>("CommitMessageBox");
        if (commitMsg != null) commitMsg.Watermark = L("GitPanel.CommitPlaceholder");
    }

    public async Task SetRepositoryPathAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            _repositoryPath = null;
            ShowNoRepository();
            return;
        }

        // Ensure path is a directory, not a file
        string directoryPath = path;
        if (System.IO.File.Exists(path))
        {
            directoryPath = System.IO.Path.GetDirectoryName(path) ?? path;
        }
        
        _repositoryPath = directoryPath;
        AppendToConsole($"SetRepositoryPath: {directoryPath}");

        // Try to find repository root
        var repoRoot = await _gitService.FindRepositoryRootAsync(directoryPath);
        AppendToConsole($"Found repository root: {repoRoot ?? "null"}");
        if (repoRoot != null)
        {
            _repositoryPath = repoRoot;
            _gitService.RepositoryPath = repoRoot;
            await RefreshAsync();
        }
        else
        {
            _gitService.RepositoryPath = directoryPath;
            ShowNoRepository();
        }
    }

    public async Task RefreshAsync()
    {
        if (!_gitService.IsRepository)
        {
            ShowNoRepository();
            return;
        }

        ShowLoading("Loading changes...");

        try
        {
            ShowRepository();
            
            // Get status
            _currentStatus = await _gitService.GetStatusAsync();
            
            // Update branch
            UpdateBranchDisplay(_currentStatus.CurrentBranch);
            
            // Update sync status
            UpdateSyncStatus(_currentStatus.AheadCount, _currentStatus.BehindCount);
            
            // Update staged changes
            UpdateStagedChanges(_currentStatus.StagedChanges);
            
            // Update unstaged changes
            UpdateUnstagedChanges(_currentStatus.UnstagedChanges);
            
            // Update changes count badge
            var totalChanges = _currentStatus.TotalChanges;
            var badge = this.FindControl<Border>("ChangesCountBadge");
            var countText = this.FindControl<TextBlock>("TotalChangesCount");
            if (badge != null) badge.IsVisible = totalChanges > 0;
            if (countText != null) countText.Text = totalChanges.ToString();
            
            // Update no changes panel visibility
            var noChanges = this.FindControl<StackPanel>("NoChangesPanel");
            var stagedExpander = this.FindControl<Expander>("StagedChangesExpander");
            var changesExpander = this.FindControl<Expander>("ChangesExpander");
            
            if (noChanges != null && stagedExpander != null && changesExpander != null)
            {
                var hasChanges = _currentStatus.HasStagedChanges || _currentStatus.HasUnstagedChanges;
                noChanges.IsVisible = !hasChanges;
                stagedExpander.IsVisible = hasChanges;
                changesExpander.IsVisible = hasChanges;
            }
            
            // Update commits if on Log tab
            if (_currentTab == "log")
            {
                var commits = await _gitService.GetCommitHistoryAsync(50);
                UpdateCommits(commits);
            }
            
            // Update commit button state
            UpdateCommitButtonState();
            
            StatusChanged?.Invoke(this, $"Branch: {_currentStatus.CurrentBranch} | {_currentStatus.TotalChanges} changes");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
            AppendToConsole($"Error: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    #region Tab Navigation

    private void LocalChangesTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("localchanges");
    }

    private async void LogTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("log");
        // Load commits when switching to log
        if (_gitService.IsRepository)
        {
            var commits = await _gitService.GetCommitHistoryAsync(50);
            UpdateCommits(commits);
        }
    }

    private void ConsoleTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("console");
    }

    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        
        // Get tab buttons
        var localChangesTab = this.FindControl<Button>("LocalChangesTab");
        var logTab = this.FindControl<Button>("LogTab");
        var consoleTab = this.FindControl<Button>("ConsoleTab");
        
        // Get panels
        var localChangesPanel = this.FindControl<Grid>("LocalChangesPanel");
        var logPanel = this.FindControl<Grid>("LogPanel");
        var consolePanel = this.FindControl<Grid>("ConsolePanel");
        var noRepoPanel = this.FindControl<StackPanel>("NoRepositoryPanel");
        
        // Remove active from all tabs
        localChangesTab?.Classes.Remove("active");
        logTab?.Classes.Remove("active");
        consoleTab?.Classes.Remove("active");
        
        // Hide all panels
        if (localChangesPanel != null) localChangesPanel.IsVisible = false;
        if (logPanel != null) logPanel.IsVisible = false;
        if (consolePanel != null) consolePanel.IsVisible = false;
        
        // Show the right panel based on repository state
        bool hasRepo = _gitService.IsRepository;
        
        switch (tab)
        {
            case "localchanges":
                localChangesTab?.Classes.Add("active");
                if (hasRepo && localChangesPanel != null) localChangesPanel.IsVisible = true;
                else if (noRepoPanel != null) noRepoPanel.IsVisible = true;
                break;
            case "log":
                logTab?.Classes.Add("active");
                if (hasRepo && logPanel != null) logPanel.IsVisible = true;
                else if (noRepoPanel != null) noRepoPanel.IsVisible = true;
                break;
            case "console":
                consoleTab?.Classes.Add("active");
                if (consolePanel != null) consolePanel.IsVisible = true;
                break;
        }
    }

    #endregion

    #region Panel States

    private void ShowNoRepository()
    {
        var noRepoPanel = this.FindControl<StackPanel>("NoRepositoryPanel");
        var localChangesPanel = this.FindControl<Grid>("LocalChangesPanel");
        var logPanel = this.FindControl<Grid>("LogPanel");
        
        if (noRepoPanel != null) noRepoPanel.IsVisible = true;
        if (localChangesPanel != null) localChangesPanel.IsVisible = false;
        if (logPanel != null) logPanel.IsVisible = false;
    }

    private void ShowRepository()
    {
        var noRepoPanel = this.FindControl<StackPanel>("NoRepositoryPanel");
        var localChangesPanel = this.FindControl<Grid>("LocalChangesPanel");
        
        if (noRepoPanel != null) noRepoPanel.IsVisible = false;
        if (_currentTab == "localchanges" && localChangesPanel != null) 
            localChangesPanel.IsVisible = true;
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

    #region UI Updates

    private void UpdateBranchDisplay(string branchName)
    {
        var branchText = this.FindControl<TextBlock>("CurrentBranchText");
        var trackingText = this.FindControl<TextBlock>("TrackingBranchText");
        
        if (branchText != null)
        {
            branchText.Text = branchName;
        }
        if (trackingText != null)
        {
            trackingText.Text = $"origin/{branchName}";
        }
    }

    private void UpdateSyncStatus(int ahead, int behind)
    {
        var syncPanel = this.FindControl<StackPanel>("SyncStatusPanel");
        var aheadText = this.FindControl<TextBlock>("AheadCountText");
        var behindText = this.FindControl<TextBlock>("BehindCountText");
        var aheadPanel = this.FindControl<StackPanel>("AheadPanel");
        var behindPanel = this.FindControl<StackPanel>("BehindPanel");
        
        if (syncPanel != null)
        {
            syncPanel.IsVisible = ahead > 0 || behind > 0;
        }
        if (aheadText != null) aheadText.Text = ahead.ToString();
        if (behindText != null) behindText.Text = behind.ToString();
        if (aheadPanel != null) aheadPanel.IsVisible = ahead > 0;
        if (behindPanel != null) behindPanel.IsVisible = behind > 0;
    }

    private void UpdateStagedChanges(IEnumerable<GitFileChange> changes)
    {
        var list = this.FindControl<ItemsControl>("StagedChangesList");
        var countText = this.FindControl<TextBlock>("StagedCountText");
        
        var changesList = new List<GitFileChange>(changes);
        
        if (list != null) list.ItemsSource = changesList;
        if (countText != null) countText.Text = changesList.Count.ToString();
    }

    private void UpdateUnstagedChanges(IEnumerable<GitFileChange> changes)
    {
        var list = this.FindControl<ItemsControl>("ChangesList");
        var countText = this.FindControl<TextBlock>("ChangesCountText");
        
        var changesList = new List<GitFileChange>(changes);
        
        if (list != null) list.ItemsSource = changesList;
        if (countText != null) countText.Text = changesList.Count.ToString();
    }

    private void UpdateCommits(List<GitCommit> commits)
    {
        var list = this.FindControl<ItemsControl>("CommitsList");
        if (list != null) list.ItemsSource = commits;
    }

    private void UpdateCommitButtonState()
    {
        var commitButton = this.FindControl<Button>("CommitButton");
        var commitMessageBox = this.FindControl<TextBox>("CommitMessageBox");
        
        if (commitButton != null && commitMessageBox != null && _currentStatus != null)
        {
            var hasMessage = !string.IsNullOrWhiteSpace(commitMessageBox.Text);
            var hasStagedChanges = _currentStatus.HasStagedChanges;
            commitButton.IsEnabled = hasMessage && hasStagedChanges;
        }
    }

    private void CommitMessageBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateCommitButtonState();
    }

    private async void CommitMessageBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await CommitAsync();
        }
    }

    #endregion

    #region Console

    private void AppendToConsole(string text)
    {
        _consoleOutput.AppendLine($"[{DateTime.Now:HH:mm:ss}] {text}");
        var consoleText = this.FindControl<SelectableTextBlock>("ConsoleOutputText");
        if (consoleText != null)
        {
            consoleText.Text = _consoleOutput.ToString();
        }
        
        // Auto scroll
        var scrollViewer = this.FindControl<ScrollViewer>("ConsoleScrollViewer");
        scrollViewer?.ScrollToEnd();
    }

    private void ClearConsole_Click(object? sender, RoutedEventArgs e)
    {
        _consoleOutput.Clear();
        var consoleText = this.FindControl<SelectableTextBlock>("ConsoleOutputText");
        if (consoleText != null)
        {
            consoleText.Text = "Git console ready...";
        }
    }

    #endregion

    #region Toolbar Actions

    private void CommitToolbar_Click(object? sender, RoutedEventArgs e)
    {
        // Focus commit message box
        var commitBox = this.FindControl<TextBox>("CommitMessageBox");
        commitBox?.Focus();
        SwitchTab("localchanges");
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Updating project...");
        AppendToConsole("Fetching from origin...");
        
        var fetchResult = await _gitService.FetchAsync();
        if (fetchResult.Success)
        {
            AppendToConsole("Fetch completed. Pulling...");
            var pullResult = await _gitService.PullAsync();
            if (pullResult.Success)
            {
                StatusChanged?.Invoke(this, "Project updated successfully");
                AppendToConsole("Pull completed successfully");
            }
            else
            {
                StatusChanged?.Invoke(this, $"Pull failed: {pullResult.Error}");
                AppendToConsole($"Pull failed: {pullResult.Error}");
            }
        }
        else
        {
            StatusChanged?.Invoke(this, $"Fetch failed: {fetchResult.Error}");
            AppendToConsole($"Fetch failed: {fetchResult.Error}");
        }
        
        HideLoading();
        await RefreshAsync();
    }

    private void ShowHistory_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("log");
    }

    private async void Rollback_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentStatus == null || !_currentStatus.HasChanges) return;
        
        ShowLoading("Rolling back changes...");
        AppendToConsole("Rolling back all changes...");
        
        var result = await _gitService.DiscardAllChangesAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "All changes rolled back");
            AppendToConsole("Rollback completed");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Rollback failed: {result.Error}");
            AppendToConsole($"Rollback failed: {result.Error}");
        }
    }

    private async void Shelve_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Shelving changes...");
        AppendToConsole("Stashing changes...");
        
        var result = await _gitService.StashAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Changes shelved (stashed)");
            AppendToConsole("Stash created successfully");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Shelve failed: {result.Error}");
            AppendToConsole($"Stash failed: {result.Error}");
        }
    }

    private async void Unshelve_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Unshelving changes...");
        AppendToConsole("Popping stash...");
        
        var result = await _gitService.StashPopAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Changes unshelved (stash popped)");
            AppendToConsole("Stash popped successfully");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Unshelve failed: {result.Error}");
            AppendToConsole($"Stash pop failed: {result.Error}");
        }
    }

    private void GroupBy_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Toggle grouping mode
        StatusChanged?.Invoke(this, "Group by: Directory (toggle not implemented yet)");
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Open git settings
        StatusChanged?.Invoke(this, "Git settings - not implemented yet");
    }

    #endregion

    #region Repository Actions

    private async void InitRepo_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            StatusChanged?.Invoke(this, "No folder opened");
            return;
        }

        ShowLoading("Initializing repository...");
        AppendToConsole($"git init {_repositoryPath}");
        
        var result = await _gitService.InitAsync(_repositoryPath);
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Git repository initialized");
            AppendToConsole("Repository initialized successfully");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Failed to initialize: {result.Error}");
            AppendToConsole($"Init failed: {result.Error}");
        }
    }

    private void CloneRepo_Click(object? sender, RoutedEventArgs e)
    {
        CloneRepositoryRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Branch Actions

    private async void Branch_Click(object? sender, RoutedEventArgs e)
    {
        var branches = await _gitService.GetBranchesAsync();
        
        var button = sender as Button;
        if (button == null) return;

        var menu = new ContextMenu();
        
        menu.Items.Add(new MenuItem 
        { 
            Header = "➕ New Branch...", 
            Command = new RelayCommand(() => _ = CreateBranchAsync())
        });
        menu.Items.Add(new Separator());

        foreach (var branch in branches)
        {
            if (branch.IsRemote) continue;
            
            var branchNameCapture = branch.Name;
            var item = new MenuItem
            {
                Header = $"{(branch.IsCurrent ? "● " : "")}{branch.Name}",
                Command = new RelayCommand(() => _ = CheckoutBranch(branchNameCapture)),
                IsEnabled = !branch.IsCurrent
            };
            menu.Items.Add(item);
        }

        // Remote branches
        var hasRemoteBranches = false;
        foreach (var branch in branches)
        {
            if (!branch.IsRemote) continue;
            
            if (!hasRemoteBranches)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "Remote Branches", IsEnabled = false });
                hasRemoteBranches = true;
            }
            
            var branchNameCapture = branch.Name;
            var item = new MenuItem
            {
                Header = $"☁ {branch.ShortName}",
                Command = new RelayCommand(() => _ = CheckoutBranch(branchNameCapture))
            };
            menu.Items.Add(item);
        }

        menu.Open(button);
    }

    private async Task CreateBranchAsync()
    {
        // TODO: Implement branch creation dialog
        StatusChanged?.Invoke(this, "Create branch - not implemented yet");
        await Task.CompletedTask;
    }

    private async Task CheckoutBranch(string branchName)
    {
        ShowLoading($"Checking out {branchName}...");
        AppendToConsole($"git checkout {branchName}");
        
        var result = await _gitService.CheckoutBranchAsync(branchName);
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, $"Switched to branch: {branchName}");
            AppendToConsole($"Switched to branch '{branchName}'");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Failed to checkout: {result.Error}");
            AppendToConsole($"Checkout failed: {result.Error}");
        }
    }

    #endregion

    #region Commit Actions

    private async void Commit_Click(object? sender, RoutedEventArgs e)
    {
        await CommitAsync();
    }

    private async void CommitPush_Click(object? sender, RoutedEventArgs e)
    {
        await CommitAsync(andPush: true);
    }

    private async void Amend_Click(object? sender, RoutedEventArgs e)
    {
        await CommitAsync(amend: true);
    }

    private async Task CommitAsync(bool andPush = false, bool amend = false)
    {
        var commitMessageBox = this.FindControl<TextBox>("CommitMessageBox");
        if (commitMessageBox == null) return;

        var message = commitMessageBox.Text?.Trim();
        if (string.IsNullOrEmpty(message) && !amend) return;

        ShowLoading("Committing...");
        AppendToConsole($"git commit {(amend ? "--amend " : "")}-m \"{message}\"");

        var result = await _gitService.CommitAsync(message ?? "", amend);

        if (result.Success)
        {
            commitMessageBox.Text = "";
            StatusChanged?.Invoke(this, amend ? "Commit amended" : "Changes committed");
            AppendToConsole("Commit successful");

            if (andPush)
            {
                ShowLoading("Pushing...");
                AppendToConsole("git push");
                var pushResult = await _gitService.PushAsync();
                if (!pushResult.Success)
                {
                    StatusChanged?.Invoke(this, $"Committed but push failed: {pushResult.Error}");
                    AppendToConsole($"Push failed: {pushResult.Error}");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Changes committed and pushed");
                    AppendToConsole("Push successful");
                }
            }

            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Commit failed: {result.Error}");
            AppendToConsole($"Commit failed: {result.Error}");
        }

        HideLoading();
    }

    private async void Push_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Pushing...");
        AppendToConsole("git push");
        
        var result = await _gitService.PushAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Push completed");
            AppendToConsole("Push successful");
            await RefreshAsync();
        }
        else
        {
            // Check if we need to set upstream
            if (result.Error.Contains("no upstream"))
            {
                var currentBranch = await _gitService.GetCurrentBranchAsync();
                AppendToConsole($"git push -u origin {currentBranch}");
                result = await _gitService.PushAsync("origin", currentBranch, setUpstream: true);
                
                if (result.Success)
                {
                    StatusChanged?.Invoke(this, "Push completed (upstream set)");
                    AppendToConsole("Push successful (upstream set)");
                    await RefreshAsync();
                    return;
                }
            }
            
            StatusChanged?.Invoke(this, $"Push failed: {result.Error}");
            AppendToConsole($"Push failed: {result.Error}");
        }
    }

    #endregion

    #region Staging Actions

    private async void StageAll_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Staging all changes...");
        AppendToConsole("git add -A");
        
        var result = await _gitService.StageAllAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            AppendToConsole("All changes staged");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Stage failed: {result.Error}");
            AppendToConsole($"Stage failed: {result.Error}");
        }
    }

    private async void UnstageAll_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Unstaging all changes...");
        AppendToConsole("git reset HEAD");
        
        var result = await _gitService.UnstageAllAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            AppendToConsole("All changes unstaged");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Unstage failed: {result.Error}");
            AppendToConsole($"Unstage failed: {result.Error}");
        }
    }

    private async void DiscardAll_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Add confirmation dialog
        ShowLoading("Discarding all changes...");
        AppendToConsole("Discarding all untracked files...");
        
        var result = await _gitService.DiscardAllChangesAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "All changes discarded");
            AppendToConsole("All changes discarded");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Discard failed: {result.Error}");
            AppendToConsole($"Discard failed: {result.Error}");
        }
    }

    private async void StageFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GitFileChange change)
        {
            AppendToConsole($"git add \"{change.FilePath}\"");
            var result = await _gitService.StageFileAsync(change.FilePath);
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
    }

    private async void UnstageFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GitFileChange change)
        {
            AppendToConsole($"git reset HEAD \"{change.FilePath}\"");
            var result = await _gitService.UnstageFileAsync(change.FilePath);
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
    }

    private async void DiscardFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GitFileChange change)
        {
            AppendToConsole($"Discarding changes: \"{change.FilePath}\"");
            var result = await _gitService.DiscardChangesAsync(change.FilePath);
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
    }

    #endregion

    #region File Context Menu

    private void StagedFile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 2)
        {
            if (sender is Border border && border.DataContext is GitFileChange change)
            {
                FileDiffRequested?.Invoke(this, change.FullPath);
            }
        }
    }

    private void UnstagedFile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 2)
        {
            if (sender is Border border && border.DataContext is GitFileChange change)
            {
                FileOpenRequested?.Invoke(this, change.FullPath);
            }
        }
    }

    private async void StageFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitFileChange change)
        {
            await _gitService.StageFileAsync(change.FilePath);
            await RefreshAsync();
        }
    }

    private async void UnstageFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitFileChange change)
        {
            await _gitService.UnstageFileAsync(change.FilePath);
            await RefreshAsync();
        }
    }

    private async void DiscardFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitFileChange change)
        {
            await _gitService.DiscardChangesAsync(change.FilePath);
            await RefreshAsync();
        }
    }

    private void ViewChanges_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitFileChange change)
        {
            FileDiffRequested?.Invoke(this, change.FullPath);
        }
    }

    private void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitFileChange change)
        {
            AppendToConsole($"Opening file: {change.FullPath}");
            FileOpenRequested?.Invoke(this, change.FullPath);
        }
    }

    #endregion
}

/// <summary>
/// Simple relay command for menu items
/// </summary>
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

