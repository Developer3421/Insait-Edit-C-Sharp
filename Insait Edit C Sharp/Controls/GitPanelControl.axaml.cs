using System;
using System.Collections.Generic;
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
    }

    public async Task SetRepositoryPathAsync(string? path)
    {
        _repositoryPath = path;
        
        if (string.IsNullOrEmpty(path))
        {
            ShowNoRepository();
            return;
        }

        // Try to find repository root
        var repoRoot = await _gitService.FindRepositoryRootAsync(path);
        if (repoRoot != null)
        {
            _repositoryPath = repoRoot;
            _gitService.RepositoryPath = repoRoot;
            await RefreshAsync();
        }
        else
        {
            _gitService.RepositoryPath = path;
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
            
            // Update stashes
            var stashes = await _gitService.GetStashListAsync();
            UpdateStashes(stashes);
            
            // Update commits
            var commits = await _gitService.GetCommitHistoryAsync(10);
            UpdateCommits(commits);
            
            // Update commit button state
            UpdateCommitButtonState();
            
            StatusChanged?.Invoke(this, $"Branch: {_currentStatus.CurrentBranch} | {_currentStatus.TotalChanges} changes");
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

    private void ShowNoRepository()
    {
        var noRepoPanel = this.FindControl<StackPanel>("NoRepositoryPanel");
        var repoPanel = this.FindControl<StackPanel>("RepositoryPanel");
        
        if (noRepoPanel != null) noRepoPanel.IsVisible = true;
        if (repoPanel != null) repoPanel.IsVisible = false;
    }

    private void ShowRepository()
    {
        var noRepoPanel = this.FindControl<StackPanel>("NoRepositoryPanel");
        var repoPanel = this.FindControl<StackPanel>("RepositoryPanel");
        
        if (noRepoPanel != null) noRepoPanel.IsVisible = false;
        if (repoPanel != null) repoPanel.IsVisible = true;
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

    private void UpdateBranchDisplay(string branchName)
    {
        var branchText = this.FindControl<TextBlock>("CurrentBranchText");
        if (branchText != null)
        {
            branchText.Text = branchName;
        }
    }

    private void UpdateSyncStatus(int ahead, int behind)
    {
        var syncBorder = this.FindControl<Border>("SyncStatusBorder");
        var aheadText = this.FindControl<TextBlock>("AheadCountText");
        var behindText = this.FindControl<TextBlock>("BehindCountText");
        
        if (syncBorder != null)
        {
            syncBorder.IsVisible = ahead > 0 || behind > 0;
        }
        if (aheadText != null) aheadText.Text = ahead.ToString();
        if (behindText != null) behindText.Text = behind.ToString();
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

    private void UpdateStashes(List<GitStash> stashes)
    {
        var list = this.FindControl<ItemsControl>("StashesList");
        var countBorder = this.FindControl<Border>("StashCountBorder");
        var countText = this.FindControl<TextBlock>("StashCountText");
        var noStashesPanel = this.FindControl<StackPanel>("NoStashesPanel");
        
        if (list != null) list.ItemsSource = stashes;
        if (countBorder != null) countBorder.IsVisible = stashes.Count > 0;
        if (countText != null) countText.Text = stashes.Count.ToString();
        if (noStashesPanel != null) noStashesPanel.IsVisible = stashes.Count == 0;
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

    #region Header Actions

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void MoreActions_Click(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;

        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "🔀 Create Branch...", Command = new RelayCommand(() => _ = CreateBranchAsync()) },
                new MenuItem { Header = "🏷️ Create Tag...", Command = new RelayCommand(() => _ = CreateTagAsync()) },
                new Separator(),
                new MenuItem { Header = "↩️ Undo Last Commit", Command = new RelayCommand(() => _ = UndoLastCommitAsync()) },
                new MenuItem { Header = "🔄 Reset Branch...", Command = new RelayCommand(() => _ = ResetBranchAsync()) },
                new Separator(),
                new MenuItem { Header = "⚙️ Git Settings", Command = new RelayCommand(OpenGitSettings) }
            }
        };
        
        menu.Open(button);
    }

    private async Task CreateBranchAsync()
    {
        // TODO: Implement branch creation dialog
        StatusChanged?.Invoke(this, "Create branch - not implemented yet");
        await Task.CompletedTask;
    }

    private async Task CreateTagAsync()
    {
        // TODO: Implement tag creation dialog
        StatusChanged?.Invoke(this, "Create tag - not implemented yet");
        await Task.CompletedTask;
    }

    private async Task UndoLastCommitAsync()
    {
        ShowLoading("Undoing last commit...");
        var result = await _gitService.RunGitCommandInternalAsync("reset --soft HEAD~1");
        HideLoading();
        if (result)
        {
            await RefreshAsync();
            StatusChanged?.Invoke(this, "Last commit undone");
        }
        else
        {
            StatusChanged?.Invoke(this, "Failed to undo last commit");
        }
    }

    private async Task ResetBranchAsync()
    {
        // TODO: Implement reset dialog
        StatusChanged?.Invoke(this, "Reset branch - not implemented yet");
        await Task.CompletedTask;
    }

    private void OpenGitSettings()
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
        
        var result = await _gitService.InitAsync(_repositoryPath);
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Git repository initialized");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Failed to initialize: {result.Error}");
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

    private async Task CheckoutBranch(string branchName)
    {
        ShowLoading($"Checking out {branchName}...");
        
        var result = await _gitService.CheckoutBranchAsync(branchName);
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, $"Switched to branch: {branchName}");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Failed to checkout: {result.Error}");
        }
    }

    private async void Fetch_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Fetching...");
        
        var result = await _gitService.FetchAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Fetch completed");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Fetch failed: {result.Error}");
        }
    }

    private async void Pull_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Pulling...");
        
        var result = await _gitService.PullAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Pull completed");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Pull failed: {result.Error}");
        }
    }

    private async void Push_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Pushing...");
        
        var result = await _gitService.PushAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Push completed");
            await RefreshAsync();
        }
        else
        {
            // Check if we need to set upstream
            if (result.Error.Contains("no upstream"))
            {
                var currentBranch = await _gitService.GetCurrentBranchAsync();
                result = await _gitService.PushAsync("origin", currentBranch, setUpstream: true);
                
                if (result.Success)
                {
                    StatusChanged?.Invoke(this, "Push completed (upstream set)");
                    await RefreshAsync();
                    return;
                }
            }
            
            StatusChanged?.Invoke(this, $"Push failed: {result.Error}");
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

        var result = await _gitService.CommitAsync(message ?? "", amend);

        if (result.Success)
        {
            commitMessageBox.Text = "";
            StatusChanged?.Invoke(this, amend ? "Commit amended" : "Changes committed");

            if (andPush)
            {
                ShowLoading("Pushing...");
                var pushResult = await _gitService.PushAsync();
                if (!pushResult.Success)
                {
                    StatusChanged?.Invoke(this, $"Committed but push failed: {pushResult.Error}");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Changes committed and pushed");
                }
            }

            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Commit failed: {result.Error}");
        }

        HideLoading();
    }

    #endregion

    #region Staging Actions

    private async void StageAll_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Staging all changes...");
        
        var result = await _gitService.StageAllAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Stage failed: {result.Error}");
        }
    }

    private async void UnstageAll_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Unstaging all changes...");
        
        var result = await _gitService.UnstageAllAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Unstage failed: {result.Error}");
        }
    }

    private async void DiscardAll_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Add confirmation dialog
        ShowLoading("Discarding all changes...");
        
        var result = await _gitService.DiscardAllChangesAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "All changes discarded");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Discard failed: {result.Error}");
        }
    }

    private async void StageFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GitFileChange change)
        {
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
            FileOpenRequested?.Invoke(this, change.FullPath);
        }
    }

    #endregion

    #region Stash Actions

    private async void Stash_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Stashing changes...");
        
        var result = await _gitService.StashAsync();
        
        HideLoading();
        
        if (result.Success)
        {
            StatusChanged?.Invoke(this, "Changes stashed");
            await RefreshAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, $"Stash failed: {result.Error}");
        }
    }

    private async void ApplyStash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitStash stash)
        {
            await _gitService.StashApplyAsync(stash.Name);
            await RefreshAsync();
        }
    }

    private async void PopStash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitStash stash)
        {
            await _gitService.StashPopAsync(stash.Name);
            await RefreshAsync();
        }
    }

    private async void DropStash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GitStash stash)
        {
            await _gitService.StashDropAsync(stash.Name);
            await RefreshAsync();
        }
    }

    private async void ApplyStashButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GitStash stash)
        {
            await _gitService.StashApplyAsync(stash.Name);
            await RefreshAsync();
        }
    }

    private async void DropStashButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GitStash stash)
        {
            await _gitService.StashDropAsync(stash.Name);
            await RefreshAsync();
        }
    }

    #endregion

    #region History

    private void ViewHistory_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Open full history view
        StatusChanged?.Invoke(this, "Full history view - not implemented yet");
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

