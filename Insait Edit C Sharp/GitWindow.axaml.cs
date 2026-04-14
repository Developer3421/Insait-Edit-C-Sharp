using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class GitWindow : Window
{
    private static readonly IBrush WhiteWatermarkBrush = new SolidColorBrush(Colors.White);

    // ─── Services ─────────────────────────────────────────────
    private readonly GitService _git = new();
    private readonly GitHubAccountService _ghService = new();
    private readonly StringBuilder _console = new();
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    // ─── State ────────────────────────────────────────────────
    private string? _solutionPath;
    private List<string> _projectPaths = new();
    private string? _currentRepoRoot;
    private string _currentScope = "solution";
    private List<GitFileChange> _allFiles = new();   // merged staged + unstaged
    private GitCommit? _selectedCommit;
    private string _rightTab = "log";
    private bool _showAllBranches;
    private string _logFilter = "";
    private bool _isRefreshing;

    // ─── Events ───────────────────────────────────────────────
    public event EventHandler<string>? FileOpenRequested;
    public event EventHandler? WorkspaceRefreshRequested;

    // ─── File filter ──────────────────────────────────────────
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",".axaml",".xaml",".razor",".html",".css",".scss",
        ".js",".ts",".json",".xml",".yaml",".yml",
        ".md",".txt",".editorconfig",".csproj",".nfproj",
        ".props",".targets",".sln",".gitignore",".gitattributes",
        ".png",".jpg",".jpeg",".gif",".svg",".ico",
        ".ttf",".otf",".woff",".woff2",".fsproj",".vbproj"
    };

    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    { "bin","obj",".git",".vs","node_modules",".idea","packages" };

    // ═══════════════════════════════════════════════════════════
    //  Init
    // ═══════════════════════════════════════════════════════════

    public GitWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        HookWatermarkForeground("LogFilterBox");
        HookWatermarkForeground("CommitMsgBox");
        _ghService.ErrorOccurred += (_, message) => AppendConsole(message);
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Opened += (_, _) => _autoRefreshTimer.Start();
        Closed += (_, _) => _autoRefreshTimer.Stop();
        Activated += async (_, _) => await RefreshAsync(showLoadingOverlay: false);
    }

    private void ApplyLocalization()
    {
        Title = L("Git.Title");
        BuildScopeCombo();
    }

    private static string L(string key) => LocalizationService.Get(key);

    private static string FormatLocalized(string key, params object?[] args)
        => string.Format(LocalizationService.Get(key), args);

    public async Task InitializeAsync(string? projectPath, IEnumerable<string>? allProjects = null)
    {
        _solutionPath = projectPath is not null && File.Exists(projectPath)
            ? Path.GetDirectoryName(projectPath) ?? projectPath
            : projectPath;

        _projectPaths = allProjects?.ToList() ?? new();
        BuildScopeCombo();

        if (!string.IsNullOrEmpty(_solutionPath))
        {
            _currentRepoRoot = await _git.FindRepositoryRootAsync(_solutionPath);
            if (_currentRepoRoot != null)
            {
                _git.RepositoryPath = _currentRepoRoot;
                var repoLbl = this.FindControl<TextBlock>("TitleRepoText");
                if (repoLbl != null) repoLbl.Text = _currentRepoRoot;
            }
        }

        await RefreshAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Scope combo
    // ═══════════════════════════════════════════════════════════

    private void BuildScopeCombo()
    {
        var combo = this.FindControl<ComboBox>("ScopeCombo");
        if (combo == null) return;

        var desiredTag = string.IsNullOrWhiteSpace(_currentScope) ? "solution" : _currentScope;
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = L("Git.SolutionAll"), Tag = "solution" });
        foreach (var p in _projectPaths)
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"📦 {Path.GetFileNameWithoutExtension(p)}",
                Tag = $"project:{p}"
            });

        var selectedIndex = 0;
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, desiredTag, StringComparison.Ordinal))
            {
                selectedIndex = i;
                break;
            }
        }

        combo.SelectedIndex = selectedIndex;
    }

    private void ScopeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox c && c.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentScope = tag;
            _ = RefreshAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Refresh
    // ═══════════════════════════════════════════════════════════

    public async Task RefreshAsync(bool showLoadingOverlay = true)
    {
        if (_isRefreshing)
            return;

        if (!_git.IsRepository) { ShowNoRepo(); return; }

        _isRefreshing = true;
        if (showLoadingOverlay)
            ShowLoading(L("Git.Refreshing"));

        try
        {
            var previouslySelectedPaths = _allFiles
                .Where(f => f.IsSelected)
                .Select(f => f.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var status = await _git.GetStatusAsync();

            // Branch display
            var branch = status.CurrentBranch;
            SetText("BranchNameText", branch);
            SetText("TitleBranchText", branch);

            // Ahead/behind
            var abPanel = this.FindControl<StackPanel>("AheadBehindPanel");
            var aS = this.FindControl<StackPanel>("AheadStack");
            var bS = this.FindControl<StackPanel>("BehindStack");
            if (abPanel != null) abPanel.IsVisible = status.AheadCount > 0 || status.BehindCount > 0;
            if (aS != null) aS.IsVisible = status.AheadCount > 0;
            if (bS != null) bS.IsVisible = status.BehindCount > 0;
            SetText("AheadText",  status.AheadCount.ToString());
            SetText("BehindText", status.BehindCount.ToString());

            // Merge all changes into one list, deduplicate by path
            var all = new Dictionary<string, GitFileChange>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in status.StagedChanges.Concat(status.UnstagedChanges))
                all[f.FilePath] = f;

            // Apply scope filter
            _allFiles = all.Values.Where(IsAllowedFile).ToList();

            // Preserve existing selection when possible; default to selecting all on first load.
            foreach (var f in _allFiles)
                f.IsSelected = previouslySelectedPaths.Count == 0 || previouslySelectedPaths.Contains(f.FilePath);

            RefreshFileList();

            bool hasFiles = _allFiles.Count > 0;
            SetVisible("NoRepoPanel", false);
            SetVisible("CleanPanel",  !hasFiles);
            SetVisible("CommitAreaBorder", hasFiles);

            if (_rightTab == "log") await RefreshLogAsync();
            UpdateCommitBtn();
        }
        catch (Exception ex) { AppendConsole(FormatLocalized("Git.Error", ex.Message)); }
        finally
        {
            _isRefreshing = false;
            if (showLoadingOverlay)
                HideLoading();
        }
    }

    private void RefreshFileList()
    {
        this.FindControl<ItemsControl>("FileCheckList")
            ?.SetValue(ItemsControl.ItemsSourceProperty, _allFiles.ToList());

        int sel   = _allFiles.Count(f => f.IsSelected);
        int total = _allFiles.Count;
        SetText("SelectedCountLbl", $"{sel}/{total}");

        // Update select-all checkbox state
        var chk = this.FindControl<CheckBox>("SelectAllCheck");
        if (chk != null)
            chk.IsChecked = total > 0 && sel == total ? true
                          : sel == 0                  ? false
                          : null; // indeterminate
    }

    // ─── File allowed? ────────────────────────────────────────
    private bool IsAllowedFile(GitFileChange c)
    {
        if (IsExcludedPath(c.FilePath)) return false;
        var ext = Path.GetExtension(c.FilePath);
        if (!AllowedExt.Contains(ext)) return false;

        if (_currentScope == "solution" || _currentRepoRoot == null) return true;
        if (!_currentScope.StartsWith("project:")) return true;

        var projPath = _currentScope["project:".Length..];
        var projDir  = File.Exists(projPath) ? Path.GetDirectoryName(projPath) ?? projPath : projPath;
        var relProj  = Path.GetRelativePath(_currentRepoRoot, projDir).Replace('\\','/').TrimEnd('/');
        var fw       = c.FilePath.Replace('\\','/');
        return relProj == "." || fw.StartsWith(relProj + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedPath(string rel)
        => rel.Replace('\\','/').Split('/').Any(p => ExcludedFolders.Contains(p));

    // ═══════════════════════════════════════════════════════════
    //  File checklist handlers
    // ═══════════════════════════════════════════════════════════

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        bool check = (sender as CheckBox)?.IsChecked == true;
        foreach (var f in _allFiles) f.IsSelected = check;
        RefreshFileList();
        UpdateCommitBtn();
    }

    private void FileCheck_Click(object? sender, RoutedEventArgs e)
    {
        RefreshFileList();
        UpdateCommitBtn();
    }

    private async void StageSelected_Click(object? sender, RoutedEventArgs e)
    {
        var selected = _allFiles.Where(f => f.IsSelected).ToList();
        if (!selected.Any()) return;
        ShowLoading(L("Git.Staging"));
        foreach (var f in selected)
        {
            AppendConsole($"git add \"{f.FilePath}\"");
            await _git.StageFileAsync(f.FilePath);
        }
        HideLoading();
        await RefreshAsync();
    }

    private async void DiscardFileBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is GitFileChange c)
        {
            AppendConsole($"git checkout -- \"{c.FilePath}\"");
            await _git.DiscardChangesAsync(c.FilePath);
            NotifyWorkspaceRefreshRequested();
            await RefreshAsync();
        }
    }

    private async void File_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && sender is StyledElement element
            && element.DataContext is GitFileChange c)
        {
            SwitchRightTab("diff");
            if (sender is Border && _selectedCommit != null)
                await ShowCommitFileDiffAsync(c);
            else
                await ShowFileDiffAsync(c);
        }
    }

    // Context menu
    private async void CtxViewDiff_Click(object? sender, RoutedEventArgs e)
    {
        if (GetCtxChange(sender) is { } c)
        {
            SwitchRightTab("diff");
            await ShowFileDiffAsync(c);
        }
    }

    private async void CtxDiscard_Click(object? sender, RoutedEventArgs e)
    {
        if (GetCtxChange(sender) is { } c)
        {
            await _git.DiscardChangesAsync(c.FilePath);
            NotifyWorkspaceRefreshRequested();
            await RefreshAsync();
        }
    }

    private void CtxOpenFile_Click(object? sender, RoutedEventArgs e)
    {
        if (GetCtxChange(sender) is { } c) FileOpenRequested?.Invoke(this, c.FullPath);
    }

    private static GitFileChange? GetCtxChange(object? sender)
        => (sender as MenuItem)?.DataContext as GitFileChange;

    // ─── Diff ─────────────────────────────────────────────────
    private async Task ShowFileDiffAsync(GitFileChange change)
    {
        SetText("DiffFileLabel", change.FilePath);
        string diff;

        if (change.WorkTreeStatus == GitFileStatus.Untracked || change.IndexStatus == GitFileStatus.Untracked)
        {
            diff = await _git.GetUntrackedFileDiffAsync(change.FilePath);
        }
        else
        {
            diff = await _git.GetFileDiffAsync(change.FilePath, staged: false);
            if (string.IsNullOrEmpty(diff))
                diff = await _git.GetFileDiffAsync(change.FilePath, staged: true);
        }

        var t = this.FindControl<SelectableTextBlock>("DiffOutputText");
        if (t != null) t.Text = string.IsNullOrEmpty(diff) ? L("Git.NoDiffAvailable") : diff;
    }


    private async Task ShowCommitFileDiffAsync(GitFileChange change)
    {
        if (_selectedCommit == null)
            return;

        SetText("DiffFileLabel", $"{change.FilePath} ({_selectedCommit.ShortHash})");
        var diff = await _git.GetCommitFileDiffAsync(_selectedCommit.Hash, change.FilePath);
        var t = this.FindControl<SelectableTextBlock>("DiffOutputText");
        if (t != null) t.Text = string.IsNullOrEmpty(diff) ? L("Git.NoDiffAvailable") : diff;
    }

    // ═══════════════════════════════════════════════════════════
    //  Log
    // ═══════════════════════════════════════════════════════════

    private async Task RefreshLogAsync()
    {
        var currentBranch = await _git.GetCurrentBranchAsync();
        var branchArg     = _showAllBranches ? null : currentBranch;

        var local = await _git.GetCommitHistoryAsync(100, branchArg);
        if (!string.IsNullOrWhiteSpace(_logFilter))
            local = local.Where(c =>
                c.Message.Contains(_logFilter, StringComparison.OrdinalIgnoreCase) ||
                c.ShortHash.Contains(_logFilter, StringComparison.OrdinalIgnoreCase) ||
                c.AuthorName.Contains(_logFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        this.FindControl<ItemsControl>("LocalCommitsList")
            ?.SetValue(ItemsControl.ItemsSourceProperty, local);

        var remote = await _git.GetCommitHistoryAsync(100, $"origin/{currentBranch}");
        if (!string.IsNullOrWhiteSpace(_logFilter))
            remote = remote.Where(c =>
                c.Message.Contains(_logFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        this.FindControl<ItemsControl>("RemoteCommitsList")
            ?.SetValue(ItemsControl.ItemsSourceProperty, remote);

        SetText("LocalBranchLabel",  $"({currentBranch})");
        SetText("RemoteBranchLabel", $"(origin/{currentBranch})");
        SetText("TitleBranchText",   currentBranch);
    }

    // ─── Commit row clicks ────────────────────────────────────
    private async void LocalCommit_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && sender is Border b && b.DataContext is GitCommit commit)
            await ShowCommitDetails(commit);
    }

    private async void RemoteCommit_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && sender is Border b && b.DataContext is GitCommit commit)
            await ShowCommitDetails(commit);
    }

    private async Task ShowCommitDetails(GitCommit commit)
    {
        _selectedCommit = commit;
        var details = await _git.GetCommitDetailsAsync(commit.Hash);
        if (details == null) return;

        SetVisible("CommitDetailHeader",     true);
        SetVisible("CommitDetailPlaceholder", false);
        SetText("CommitDetailMsg",    details.Message);
        SetText("CommitDetailHash",   details.Hash);
        SetText("CommitDetailAuthor", details.AuthorName);
        SetText("CommitDetailDate",   details.DateFormatted);

        var files = details.ChangedFiles.Where(IsAllowedFile).ToList();
        this.FindControl<ItemsControl>("CommitFilesList")
            ?.SetValue(ItemsControl.ItemsSourceProperty, files);
    }

    // Commit context menu
    private async void CtxCheckoutRevision_Click(object? sender, RoutedEventArgs e)
    {
        var commit = ResolveCommitContext(sender);
        if (commit == null) return;
        ShowLoading(L("Git.CheckingOut"));
        AppendConsole($"git checkout {commit.Hash}");
        await _git.CheckoutBranchAsync(commit.Hash);
        HideLoading(); await RefreshAsync();
        NotifyWorkspaceRefreshRequested();
    }

    private async void CtxNewBranchFromHere_Click(object? sender, RoutedEventArgs e)
    {
        var commit = ResolveCommitContext(sender);
        if (commit == null) return;

        var dialog = new InputDialog(
            FormatLocalized("Git.CreateBranchFromCommit", commit.ShortHash),
            FormatLocalized("Git.NewBranchFromCommitName", commit.ShortHash),
            string.Empty,
            "🌿");

        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(dialog.Result))
        {
            var name = dialog.Result;
            ShowLoading(FormatLocalized("Git.CreatingBranchFromCommit", name, commit.ShortHash));
            AppendConsole($"git checkout -b {name} {commit.Hash}");
            var r = await _git.RunGitCommandInternalAsync($"checkout -b \"{name}\" {commit.Hash}");
            HideLoading();
            AppendConsole(r ? FormatLocalized("Git.CreatedBranch", name) : L("Git.CreateRepoFailed"));
            NotifyWorkspaceRefreshRequested();
            await RefreshAsync();
        }
    }

    private async void CtxCherryPick_Click(object? sender, RoutedEventArgs e)
    {
        var commit = ResolveCommitContext(sender);
        if (commit == null) return;
        ShowLoading(L("Git.CherryPicking"));
        AppendConsole($"git cherry-pick {commit.Hash}");
        await _git.RunGitCommandInternalAsync($"cherry-pick {commit.Hash}");
        HideLoading(); await RefreshAsync();
        NotifyWorkspaceRefreshRequested();
    }

    private async void CtxRevertCommit_Click(object? sender, RoutedEventArgs e)
    {
        var commit = ResolveCommitContext(sender);
        if (commit == null) return;

        bool isRoot = await _git.IsRootCommitAsync(commit.Hash);

        // ── Ask the user what they really want ────────────────────────────
        var dialog = new Window
        {
            Title = L("Git.RevertResetTitle"),
            Width = 460, Height = isRoot ? 220 : 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = WindowDecorations.BorderOnly,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

        sp.Children.Add(new TextBlock
        {
            Text = FormatLocalized("Git.RevertResetCommit", commit.ShortHash, commit.Message),
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC09F")),
            TextWrapping = TextWrapping.Wrap
        });

        string? choice = null;

        if (!isRoot)
        {
            // Option A — safe revert (creates a new "undo" commit)
            var revertBtn = MakeBtn(L("Git.RevertOptionButton"), "#FF3E3050", "#FFF0E8F4");
            revertBtn.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            revertBtn.Click += (_, _) => { choice = "revert"; dialog.Close(); };

            sp.Children.Add(new TextBlock
            {
                Text = L("Git.RevertOptionDescription"),
                FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0")),
                TextWrapping = TextWrapping.Wrap
            });
            sp.Children.Add(revertBtn);
        }

        // Option B — hard reset to this commit (dangerous but "correct" rollback)
        var resetBtn = MakeBtn(
            isRoot ? L("Git.ResetRootButton") : L("Git.ResetButton"),
            "#FF4A2020", "#FFF38BA8");
        resetBtn.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        resetBtn.Click += (_, _) => { choice = "reset"; dialog.Close(); };

        sp.Children.Add(new TextBlock
        {
            Text = isRoot
                ? L("Git.ResetRootDescription")
                : L("Git.ResetDescription"),
            FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#FFF38BA8")),
            TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(resetBtn);

        var cancelBtn = MakeBtn(LocalizationService.Get("Common.Cancel"), "#FF3E3050", "#FFA0A0B0");
        cancelBtn.Click += (_, _) => dialog.Close();
        sp.Children.Add(cancelBtn);

        dialog.Content = sp;
        await dialog.ShowDialog(this);

        if (choice == null) return;

        // ── Execute chosen action ──────────────────────────────────────────
        if (choice == "revert")
        {
            ShowLoading(L("Git.Reverting"));
            AppendConsole($"git revert --no-edit {commit.Hash}");
            await _git.RunGitCommandInternalAsync($"revert --no-edit {commit.Hash}");
            HideLoading();
        }
        else // reset
        {
            if (isRoot)
            {
                // Hard reset to root commit brings back the exact initial state
                ShowLoading(L("Git.ResettingInitialCommit"));
                AppendConsole($"git reset --hard {commit.Hash}");
                AppendConsole("git clean -fd");
                var r = await _git.ResetHardAsync(commit.Hash);
                HideLoading();
                AppendConsole(r.Success
                    ? FormatLocalized("Git.ResetToInitialSuccess", commit.ShortHash)
                    : FormatLocalized("Git.ResetFailed", r.Error));
            }
            else
            {
                ShowLoading(L("Git.Resetting"));
                AppendConsole($"git reset --hard {commit.Hash}");
                AppendConsole("git clean -fd");
                var r = await _git.ResetHardAsync(commit.Hash);
                HideLoading();
                AppendConsole(r.Success
                    ? FormatLocalized("Git.ResetSuccess", commit.ShortHash)
                    : FormatLocalized("Git.ResetFailed", r.Error));
            }
        }

        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    private async void CtxCopyHash_Click(object? sender, RoutedEventArgs e)
    {
        var commit = ResolveCommitContext(sender);
        if (commit == null) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null) await clip.SetTextAsync(commit.Hash);
        AppendConsole(FormatLocalized("Git.Copied", commit.Hash));
    }

    // ═══════════════════════════════════════════════════════════
    //  Toolbar buttons
    // ═══════════════════════════════════════════════════════════

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private void CommitToolbar_Click(object? sender, RoutedEventArgs e)
        => this.FindControl<TextBox>("CommitMsgBox")?.Focus();

    private async void Pull_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading(L("Git.Pulling")); AppendConsole("git pull");
        var r = await _git.PullAsync();
        HideLoading();
        AppendConsole(r.Success ? L("Git.PullCompleted") : FormatLocalized("Git.PullError", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    private async void Push_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading(L("Git.Pushing"));
        var br = await _git.GetCurrentBranchAsync();
        AppendConsole($"git push origin {br}");
        var r = await _git.PushAsync("origin", br);
        if (!r.Success && r.Error.Contains("no upstream"))
            r = await _git.PushAsync("origin", br, setUpstream: true);
        HideLoading();
        AppendConsole(r.Success ? L("Git.PushCompleted") : FormatLocalized("Git.PushError", r.Error));
        await RefreshAsync();
    }

    private async void Fetch_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading(L("Git.Fetching")); AppendConsole("git fetch --all");
        var r = await _git.FetchAsync();
        HideLoading();
        AppendConsole(r.Success ? L("Git.FetchCompleted") : FormatLocalized("Git.FetchError", r.Error));
        await RefreshAsync();
    }

    private async void Stash_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading(L("Git.Stashing")); AppendConsole("git stash push");
        var r = await _git.StashAsync();
        HideLoading();
        AppendConsole(r.Success ? L("Git.StashCreated") : FormatLocalized("Git.StashError", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    private async void PopStash_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading(L("Git.PoppingStash")); AppendConsole("git stash pop");
        var r = await _git.StashPopAsync();
        HideLoading();
        AppendConsole(r.Success ? L("Git.StashPopped") : FormatLocalized("Git.PopError", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    private async void Rollback_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading(L("Git.RollingBack"));
        AppendConsole("git reset --hard HEAD");
        AppendConsole("git clean -fd");
        var r = await _git.DiscardAllChangesAsync();
        HideLoading();
        AppendConsole(r.Success ? L("Git.RollbackCompleted") : FormatLocalized("Git.Error", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    private void ConsoleTabFromToolbar_Click(object? sender, RoutedEventArgs e)
        => SwitchRightTab("console");

    // ═══════════════════════════════════════════════════════════
    //  Create GitHub Repository — using GitHubAccountService
    // ═══════════════════════════════════════════════════════════

    private async void CreateRepo_Click(object? sender, RoutedEventArgs e)
    {
        bool loggedIn = await _ghService.IsLoggedInAsync();

        // Build dialog
        var dialog = new Window
        {
            Title = L("Git.CreateRepoDialogTitle"),
            Width = 460, Height = loggedIn ? 300 : 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = WindowDecorations.BorderOnly,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

        sp.Children.Add(new TextBlock
        {
            Text = L("Git.CreateRepoDialogHeader"),
            FontSize = 14, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC09F"))
        });

        if (!loggedIn)
        {
            sp.Children.Add(new TextBlock
            {
                Text = L("Git.NotLoggedIn"),
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#FFF9E2AF"))
            });
            var loginBtn = MakeBtn(L("Git.LoginWithGitHub"), "#FFFFC09F", "#FF1F1A24");
            loginBtn.Click += async (_, _) =>
            {
                dialog.Close();
                var success = await ShowGitHubLoginDialogAsync();
                AppendConsole(success
                    ? L("Git.GitHubLoginOpened")
                    : L("Git.NotLoggedIn"));
            };
            sp.Children.Add(loginBtn);
            dialog.Content = sp;
            await dialog.ShowDialog(this);
            return;
        }

        // Logged in — show full form
        var account = await _ghService.GetAccountInfoAsync();
        if (account != null)
            sp.Children.Add(new TextBlock
            {
                Text = FormatLocalized("Git.LoggedInAs", account.Username),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#FFA6E3A1"))
            });

        sp.Children.Add(new TextBlock
        {
            Text = L("Git.RepositoryName"),
            FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0"))
        });

        var defaultName = Path.GetFileName(_solutionPath ?? "my-project");
        var nameBox = new TextBox
        {
            Classes = { "dark" },
            Text = defaultName, FontSize = 12,
            Background = new SolidColorBrush(Color.Parse("#FF1F1A24")),
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6)
        };
        sp.Children.Add(nameBox);

        sp.Children.Add(new TextBlock
        {
            Text = L("Git.DescriptionOptional"),
            FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0"))
        });
        var descBox = new TextBox
        {
            Classes = { "dark" },
            FontSize = 12, Watermark = L("Git.ShortDescription"),
            Background = new SolidColorBrush(Color.Parse("#FF1F1A24")),
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6)
        };
        ForceWatermarkForeground(descBox);
        sp.Children.Add(descBox);

        var privateCheck = new CheckBox
        {
            Content = L("Git.PrivateRepository"), IsChecked = true, FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4"))
        };
        sp.Children.Add(privateCheck);

        var btns = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelBtn = MakeBtn(LocalizationService.Get("Common.Cancel"),  "#FF3E3050", "#FFF0E8F4");
        var createBtn = MakeBtn(L("Git.Create"),  "#FFFFC09F", "#FF1F1A24");

        cancelBtn.Click += (_, _) => dialog.Close();
        createBtn.Click += async (_, _) =>
        {
            dialog.Close();
            await CreateGitHubRepoAsync(nameBox.Text ?? defaultName,
                                        descBox.Text ?? "",
                                        privateCheck.IsChecked == true);
        };
        btns.Children.Add(cancelBtn);
        btns.Children.Add(createBtn);
        sp.Children.Add(btns);

        dialog.Content = sp;
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ShowGitHubLoginDialogAsync()
    {
        DeviceCodeInfo? currentDeviceCode = _ghService.GetCurrentDeviceCode();

        var dialog = new Window
        {
            Title = L("Git.LoginWithGitHub"),
            Width = 520,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = WindowDecorations.BorderOnly,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        var header = new TextBlock
        {
            Text = L("Git.LoginWithGitHub"),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC09F"))
        };
        var statusText = new TextBlock
        {
            Text = "Requesting GitHub device code...",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4"))
        };

        var codePanel = new StackPanel { Spacing = 8, IsVisible = false };
        var codeText = new TextBlock
        {
            Text = "---- ----",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC09F"))
        };
        var linkText = new TextBlock
        {
            Text = "https://github.com/login/device",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#FFA6E3A1"))
        };

        var openBtn = MakeBtn("Open GitHub", "#FFFFC09F", "#FF1F1A24");
        var copyCodeBtn = MakeBtn("Copy code", "#FF3E3050", "#FFF0E8F4");
        var copyLinkBtn = MakeBtn("Copy link", "#FF3E3050", "#FFF0E8F4");
        var cancelBtn = MakeBtn(LocalizationService.Get("Common.Cancel"), "#FF3E3050", "#FFF0E8F4");

        var actionRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        actionRow.Children.Add(openBtn);
        actionRow.Children.Add(copyCodeBtn);
        actionRow.Children.Add(copyLinkBtn);
        actionRow.Children.Add(cancelBtn);

        codePanel.Children.Add(new TextBlock
        {
            Text = "If the browser did not open, open the link below and enter the code:",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0"))
        });
        codePanel.Children.Add(codeText);
        codePanel.Children.Add(linkText);
        codePanel.Children.Add(actionRow);

        root.Children.Add(header);
        root.Children.Add(statusText);
        root.Children.Add(codePanel);
        dialog.Content = root;

        void ApplyDeviceCode(DeviceCodeInfo deviceCode)
        {
            currentDeviceCode = deviceCode;
            codePanel.IsVisible = true;
            codeText.Text = deviceCode.UserCode;
            linkText.Text = !string.IsNullOrWhiteSpace(deviceCode.VerificationUriComplete)
                ? deviceCode.VerificationUriComplete
                : deviceCode.VerificationUri;
            statusText.Text = deviceCode.BrowserOpenSucceeded
                ? "Browser opened. Complete sign-in on GitHub."
                : "Browser did not open automatically. Open the link manually and enter the code.";
        }

        void OnDeviceCodeReady(object? _, DeviceCodeInfo deviceCode)
            => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyDeviceCode(deviceCode));

        void OnLoginStatusChanged(object? _, string status)
            => Avalonia.Threading.Dispatcher.UIThread.Post(() => statusText.Text = status);

        _ghService.DeviceCodeReady += OnDeviceCodeReady;
        _ghService.LoginStatusChanged += OnLoginStatusChanged;

        if (currentDeviceCode != null)
            ApplyDeviceCode(currentDeviceCode);

        openBtn.Click += (_, _) =>
        {
            var url = !string.IsNullOrWhiteSpace(currentDeviceCode?.VerificationUriComplete)
                ? currentDeviceCode.VerificationUriComplete
                : currentDeviceCode?.VerificationUri;

            if (string.IsNullOrWhiteSpace(url))
            {
                statusText.Text = "GitHub login link is not available yet.";
                return;
            }

            statusText.Text = _ghService.TryOpenBrowser(url)
                ? "GitHub sign-in page opened in browser."
                : $"Open this URL manually: {url}";
        };

        copyCodeBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(currentDeviceCode?.UserCode))
                return;

            var clip = TopLevel.GetTopLevel(dialog)?.Clipboard;
            if (clip != null)
            {
                await clip.SetTextAsync(currentDeviceCode.UserCode);
                statusText.Text = $"Copied code: {currentDeviceCode.UserCode}";
            }
        };

        copyLinkBtn.Click += async (_, _) =>
        {
            var url = !string.IsNullOrWhiteSpace(currentDeviceCode?.VerificationUriComplete)
                ? currentDeviceCode.VerificationUriComplete
                : currentDeviceCode?.VerificationUri;

            if (string.IsNullOrWhiteSpace(url))
                return;

            var clip = TopLevel.GetTopLevel(dialog)?.Clipboard;
            if (clip != null)
            {
                await clip.SetTextAsync(url);
                statusText.Text = "GitHub sign-in link copied.";
            }
        };

        cancelBtn.Click += (_, _) =>
        {
            _ghService.CancelLogin();
            dialog.Close(false);
        };

        var loginTask = _ghService.LoginAsync();
        _ = loginTask.ContinueWith(t =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (dialog.IsVisible)
                    dialog.Close(t.Status == TaskStatus.RanToCompletion && t.Result);
            });
        });

        bool success;
        try
        {
            var dialogResult = await dialog.ShowDialog<bool?>(this);
            success = dialogResult == true && await loginTask;
        }
        finally
        {
            _ghService.DeviceCodeReady -= OnDeviceCodeReady;
            _ghService.LoginStatusChanged -= OnLoginStatusChanged;
        }

        return success;
    }

    private async Task CreateGitHubRepoAsync(string name, string desc, bool isPrivate)
    {
        ShowLoading(L("Git.CreatingRepository"));
        SwitchRightTab("console");

        string? remoteUrlToRestore = null;
        bool shouldRestoreOrigin = false;

        try
        {
            AppendConsole($"GitHub API: create repository \"{name}\"");

            var repo = await _ghService.CreateRepositoryAsync(name, desc, isPrivate);
            if (repo == null)
            {
                AppendConsole(L("Git.CreateRepoFailed"));
                return;
            }

            var cleanRemoteUrl = !string.IsNullOrWhiteSpace(repo.CloneUrl)
                ? repo.CloneUrl
                : _ghService.NormalizeGitHubRepositoryUrl(repo.Url);
            var origin = (await _git.GetRemotesAsync())
                .FirstOrDefault(r => string.Equals(r.Name, "origin", StringComparison.OrdinalIgnoreCase));

            if (origin != null && !AreSameGitHubRemote(origin.Url, cleanRemoteUrl))
            {
                AppendConsole($"GitHub repository created: {repo.FullName}");
                AppendConsole("Remote 'origin' already exists and points to another URL. Repository was created on GitHub, but origin was not changed automatically.");
                return;
            }

            var tempRemoteUrl = _ghService.GetAuthenticatedGitUrl(cleanRemoteUrl) ?? cleanRemoteUrl;
            var originalOriginUrl = origin?.Url;

            if (origin == null)
            {
                AppendConsole($"git remote add origin \"{cleanRemoteUrl}\"");
                var addRemoteResult = await _git.AddRemoteAsync("origin", tempRemoteUrl);
                if (!addRemoteResult.Success)
                {
                    AppendConsole(addRemoteResult.Error);
                    AppendConsole(L("Git.CreateRepoFailed"));
                    return;
                }

                remoteUrlToRestore = cleanRemoteUrl;
                shouldRestoreOrigin = !string.Equals(tempRemoteUrl, cleanRemoteUrl, StringComparison.Ordinal);
            }
            else if (!string.Equals(origin.Url, tempRemoteUrl, StringComparison.Ordinal))
            {
                AppendConsole("git remote set-url origin [secure GitHub URL]");
                var setRemoteOk = await _git.RunGitCommandInternalAsync($"remote set-url origin \"{tempRemoteUrl}\"");
                if (!setRemoteOk)
                {
                    AppendConsole(L("Git.CreateRepoFailed"));
                    return;
                }

                remoteUrlToRestore = originalOriginUrl;
                shouldRestoreOrigin = !string.IsNullOrWhiteSpace(remoteUrlToRestore) &&
                                      !string.Equals(tempRemoteUrl, remoteUrlToRestore, StringComparison.Ordinal);
            }

            var branch = await _git.GetCurrentBranchAsync();
            var branchToPush = string.IsNullOrWhiteSpace(branch) ? "HEAD" : branch;
            AppendConsole($"git push -u origin {branchToPush}");
            var pushOk = await _git.RunGitCommandInternalAsync($"push -u origin {branchToPush}");

            AppendConsole(pushOk
                ? FormatLocalized("Git.CreateRepoSuccess", name)
                : L("Git.CreateRepoFailed"));
        }
        catch (Exception ex)
        {
            AppendConsole(FormatLocalized("Git.GhError", ex.Message));
        }
        finally
        {
            if (shouldRestoreOrigin && !string.IsNullOrWhiteSpace(remoteUrlToRestore))
                await _git.RunGitCommandInternalAsync($"remote set-url origin \"{remoteUrlToRestore}\"");

            HideLoading();
            await RefreshAsync();
        }
    }

    private static bool AreSameGitHubRemote(string left, string right)
    {
        static string Normalize(string value)
        {
            var normalized = value.Trim();

            if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
                normalized = $"https://github.com/{normalized["git@github.com:".Length..]}";

            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                normalized = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
            }

            normalized = normalized.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

            var githubMarker = "github.com/";
            var markerIndex = normalized.IndexOf(githubMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
                normalized = normalized[(markerIndex + githubMarker.Length)..];

            normalized = normalized.TrimEnd('/');
            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^4];

            return normalized.ToLowerInvariant();
        }

        return Normalize(left) == Normalize(right);
    }

    private static Button MakeBtn(string content, string bg, string fg)
        => new Button
        {
            Content = content,
            Background = new SolidColorBrush(Color.Parse(bg)),
            Foreground = new SolidColorBrush(Color.Parse(fg)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 7),
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

    // ═══════════════════════════════════════════════════════════
    //  Branch menu
    // ═══════════════════════════════════════════════════════════

    private async void Branch_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        if (btn == null) return;
        var branches = await _git.GetBranchesAsync(includeRemote: true);
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = L("Git.NewBranchMenu"),
            Command = new RelayCommand(() => _ = CreateBranchDialogAsync())
        });
        menu.Items.Add(new Separator());
        foreach (var b in branches.Where(x => !x.IsRemote))
        {
            var name = b.Name;
            menu.Items.Add(new MenuItem
            {
                Header = $"{(b.IsCurrent ? "● " : "  ")}{b.Name}",
                IsEnabled = !b.IsCurrent,
                Command = new RelayCommand(() => _ = CheckoutAsync(name))
            });
        }
        var remotes = branches.Where(x => x.IsRemote).ToList();
        if (remotes.Count > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = L("Git.RemoteBranches"), IsEnabled = false });
            foreach (var b in remotes)
            {
                var name = b.Name;
                menu.Items.Add(new MenuItem
                {
                    Header = $"  {b.ShortName}",
                    Command = new RelayCommand(() => _ = CheckoutAsync(name))
                });
            }
        }
        menu.Open(btn);
    }

    private async Task CheckoutAsync(string branch)
    {
        ShowLoading(FormatLocalized("Git.CheckingOutBranch", branch));
        AppendConsole($"git checkout {branch}");
        var r = await _git.CheckoutBranchAsync(branch);
        HideLoading();
        AppendConsole(r.Success ? FormatLocalized("Git.SwitchedTo", branch) : FormatLocalized("Git.Error", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    private async Task CreateBranchDialogAsync()
    {
        var dialog = new InputDialog(
            L("Git.NewBranch"),
            L("Git.BranchName"),
            string.Empty,
            "🌿");

        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(dialog.Result))
            await CreateBranchAsync(dialog.Result);
    }

    private async Task CreateBranchAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        ShowLoading(FormatLocalized("Git.CreatingBranch", name));
        AppendConsole($"git checkout -b {name}");
        var r = await _git.CreateBranchAsync(name, checkout: true);
        HideLoading();
        AppendConsole(r.Success ? FormatLocalized("Git.CreatedBranch", name) : FormatLocalized("Git.Error", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Log tab buttons
    // ═══════════════════════════════════════════════════════════

    private async void AllBranches_Click(object? sender, RoutedEventArgs e)
    {
        _showAllBranches = !_showAllBranches;
        var btn = this.FindControl<Button>("AllBranchesBtn");
        if (btn != null)
        {
            if (_showAllBranches) btn.Classes.Add("active"); else btn.Classes.Remove("active");
        }
        await RefreshLogAsync();
    }

    private async void LogFilter_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _logFilter = (sender as TextBox)?.Text ?? "";
        await RefreshLogAsync();
    }

    private async void LogTab_Click(object? sender, RoutedEventArgs e)
    { SwitchRightTab("log"); await RefreshLogAsync(); }

    private void DiffTab_Click(object? sender, RoutedEventArgs e)    => SwitchRightTab("diff");
    private async void BranchesTab_Click(object? sender, RoutedEventArgs e)
    { SwitchRightTab("branches"); await RefreshBranchesListAsync(); }
    private void ConsoleTab_Click(object? sender, RoutedEventArgs e) => SwitchRightTab("console");

    // ═══════════════════════════════════════════════════════════
    //  Branches panel
    // ═══════════════════════════════════════════════════════════

    private async Task RefreshBranchesListAsync()
    {
        try
        {
            var branches = await _git.GetBranchesAsync(includeRemote: true);
            var local  = branches.Where(b => !b.IsRemote).ToList();
            var remote = branches.Where(b => b.IsRemote).ToList();

            this.FindControl<ItemsControl>("LocalBranchesList")
                ?.SetValue(ItemsControl.ItemsSourceProperty, local);
            this.FindControl<ItemsControl>("RemoteBranchesList")
                ?.SetValue(ItemsControl.ItemsSourceProperty, remote);

            SetVisible("NoBranchesPanel", local.Count == 0 && remote.Count == 0);
        }
        catch (Exception ex)
        {
            AppendConsole(FormatLocalized("Git.Error", ex.Message));
        }
    }

    private async void NewBranchFromPanel_Click(object? sender, RoutedEventArgs e)
        => await CreateBranchDialogAsync();

    private async void RefreshBranches_Click(object? sender, RoutedEventArgs e)
        => await RefreshBranchesListAsync();

    private async void BranchItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && sender is Border b && b.DataContext is GitBranch branch && !branch.IsCurrent)
        {
            await CheckoutAsync(branch.IsRemote ? branch.Name : branch.Name);
            await RefreshBranchesListAsync();
        }
    }

    private async void CtxCheckoutBranch_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is GitBranch branch && !branch.IsCurrent)
        {
            await CheckoutAsync(branch.Name);
            await RefreshBranchesListAsync();
        }
    }

    private async void CtxDeleteBranch_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not GitBranch branch) return;
        if (branch.IsCurrent) return; // cannot delete current branch

        await DeleteBranchDialogAsync(branch.Name);
    }

    private async Task DeleteBranchDialogAsync(string branchName)
    {
        var dialog = new Window
        {
            Title = L("Git.DeleteBranch"),
            Width = 420, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = WindowDecorations.BorderOnly,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

        sp.Children.Add(new TextBlock
        {
            Text = FormatLocalized("Git.DeleteBranchConfirm", branchName),
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFF38BA8")),
            TextWrapping = TextWrapping.Wrap
        });

        var forceCheck = new CheckBox
        {
            Content = L("Git.DeleteBranchForce"), IsChecked = false, FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4"))
        };
        sp.Children.Add(forceCheck);

        bool? result = null;
        var btns = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelBtn = MakeBtn(LocalizationService.Get("Common.Cancel"), "#FF3E3050", "#FFF0E8F4");
        var deleteBtn = MakeBtn(L("Git.DeleteBranch"), "#FF4A2020", "#FFF38BA8");

        cancelBtn.Click += (_, _) => dialog.Close();
        deleteBtn.Click += (_, _) => { result = true; dialog.Close(); };

        btns.Children.Add(cancelBtn);
        btns.Children.Add(deleteBtn);
        sp.Children.Add(btns);

        dialog.Content = sp;
        await dialog.ShowDialog(this);

        if (result != true) return;

        bool force = forceCheck.IsChecked == true;
        ShowLoading(FormatLocalized("Git.DeletingBranch", branchName));
        AppendConsole($"git branch {(force ? "-D" : "-d")} {branchName}");
        var r = await _git.DeleteBranchAsync(branchName, force);
        HideLoading();
        AppendConsole(r.Success
            ? FormatLocalized("Git.DeletedBranch", branchName)
            : FormatLocalized("Git.Error", r.Error));
        await RefreshBranchesListAsync();
        await RefreshAsync(showLoadingOverlay: false);
    }

    private async void CtxRenameBranch_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not GitBranch branch) return;
        if (!branch.IsCurrent) // git branch -m only works for the current branch
        {
            // Checkout first, then rename
            await CheckoutAsync(branch.Name);
        }

        var dialog = new InputDialog(
            L("Git.RenameBranch"),
            L("Git.NewBranchName"),
            branch.Name,
            "✏️");

        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(dialog.Result) && dialog.Result != branch.Name)
        {
            var newName = dialog.Result;
            ShowLoading(FormatLocalized("Git.RenamingBranch", newName));
            AppendConsole($"git branch -m \"{newName}\"");
            var r = await _git.RenameBranchAsync(newName);
            HideLoading();
            AppendConsole(r.Success
                ? FormatLocalized("Git.RenamedBranch", newName)
                : FormatLocalized("Git.Error", r.Error));
            NotifyWorkspaceRefreshRequested();
            await RefreshBranchesListAsync();
            await RefreshAsync(showLoadingOverlay: false);
        }
    }

    private async void CtxMergeBranch_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not GitBranch branch) return;
        if (branch.IsCurrent) return; // can't merge into itself

        ShowLoading(FormatLocalized("Git.MergingBranch", branch.Name));
        AppendConsole($"git merge \"{branch.Name}\"");
        var r = await _git.MergeBranchAsync(branch.Name);
        HideLoading();
        AppendConsole(r.Success
            ? FormatLocalized("Git.MergedBranch", branch.Name)
            : FormatLocalized("Git.MergeError", r.Error));
        NotifyWorkspaceRefreshRequested();
        await RefreshBranchesListAsync();
        await RefreshAsync(showLoadingOverlay: false);
    }

    // ═══════════════════════════════════════════════════════════
    //  Commit
    // ═══════════════════════════════════════════════════════════

    private void CommitMsgBox_TextChanged(object? sender, TextChangedEventArgs e) => UpdateCommitBtn();

    private async void Commit_Click(object? sender, RoutedEventArgs e)     => await CommitAsync();
    private async void CommitPush_Click(object? sender, RoutedEventArgs e) => await CommitAsync(andPush: true);
    private async void Amend_Click(object? sender, RoutedEventArgs e)      => await CommitAsync(amend: true);

    private async Task CommitAsync(bool andPush = false, bool amend = false)
    {
        var box = this.FindControl<TextBox>("CommitMsgBox");
        var msg = box?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(msg) && !amend) return;

        // Stage selected files first
        var toStage = _allFiles.Where(f => f.IsSelected).ToList();
        if (toStage.Count == 0 && !amend) { AppendConsole(L("Git.NoFilesSelected")); return; }

        ShowLoading(L("Git.Staging"));
        foreach (var f in toStage)
        {
            AppendConsole($"git add \"{f.FilePath}\"");
            await _git.StageFileAsync(f.FilePath);
        }

        ShowLoading(L("Git.Committing"));
        AppendConsole($"git commit{(amend ? " --amend" : "")} -m \"{msg}\"");
        var r = await _git.CommitAsync(msg, amend);

        if (r.Success)
        {
            if (box != null) box.Text = "";
            AppendConsole(L("Git.CommitSuccessful"));
            if (andPush)
            {
                ShowLoading(L("Git.Pushing"));
                var br = await _git.GetCurrentBranchAsync();
                AppendConsole($"git push origin {br}");
                var pr = await _git.PushAsync("origin", br);
                if (!pr.Success && pr.Error.Contains("no upstream"))
                    pr = await _git.PushAsync("origin", br, setUpstream: true);
                AppendConsole(pr.Success ? L("Git.PushCompleted") : FormatLocalized("Git.PushError", pr.Error));
            }
        }
        else AppendConsole(FormatLocalized("Git.CommitError", r.Error));

        HideLoading();
        await RefreshAsync();
    }

    private void UpdateCommitBtn()
    {
        var btn = this.FindControl<Button>("CommitBtn");
        var msg = this.FindControl<TextBox>("CommitMsgBox");
        bool anySelected = _allFiles.Any(f => f.IsSelected);
        if (btn != null)
            btn.IsEnabled = !string.IsNullOrWhiteSpace(msg?.Text) && anySelected;
    }

    // ═══════════════════════════════════════════════════════════
    //  Init / Clone
    // ═══════════════════════════════════════════════════════════

    private async void InitRepo_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_solutionPath)) return;
        ShowLoading(L("Git.Initializing"));
        AppendConsole($"git init \"{_solutionPath}\"");
        var gitSetupService = new ProjectCreationGitService(_git);
        var r = await gitSetupService.EnsureRepositoryWithInitialCommitAsync(_solutionPath);
        HideLoading();
        if (r.Success)
        {
            _currentRepoRoot = await _git.FindRepositoryRootAsync(_solutionPath) ?? Path.GetFullPath(_solutionPath);
            _git.RepositoryPath = _currentRepoRoot;
            AppendConsole(L("Git.RepositoryInitialized"));
            AppendConsole("Initial commit created.");
        }
        else AppendConsole(FormatLocalized("Git.InitError", r.Error));
        await RefreshAsync();
    }

    private async void Clone_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cloneWin = new CloneRepositoryWindow();
            var result   = await cloneWin.ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(result))
            {
                _currentRepoRoot = result;
                _git.RepositoryPath = result;
                await RefreshAsync();
            }
        }
        catch { /* no CloneWindow available */ }
    }

    // ═══════════════════════════════════════════════════════════
    //  Console
    // ═══════════════════════════════════════════════════════════

    private void ClearConsole_Click(object? sender, RoutedEventArgs e)
    {
        _console.Clear();
        var t = this.FindControl<SelectableTextBlock>("ConsoleText");
        if (t != null) t.Text = L("Git.ConsoleReady");
    }

    private void AppendConsole(string text)
    {
        _console.AppendLine($"[{DateTime.Now:HH:mm:ss}] {text}");
        var t = this.FindControl<SelectableTextBlock>("ConsoleText");
        if (t != null) t.Text = _console.ToString();
        this.FindControl<ScrollViewer>("ConsoleSV")?.ScrollToEnd();
    }

    // ═══════════════════════════════════════════════════════════
    //  UI helpers
    // ═══════════════════════════════════════════════════════════

    private void SwitchRightTab(string tab)
    {
        _rightTab = tab;
        string[] tabBtns   = { "LogTabBtn", "DiffTabBtn", "BranchesTabBtn", "ConsoleTabBtn" };
        string[] tabPanels = { "LogPanel",  "DiffPanel",  "BranchesPanel",  "ConsolePanel"  };
        for (int i = 0; i < tabBtns.Length; i++)
        {
            this.FindControl<Button>(tabBtns[i])?.Classes.Remove("active");
            this.FindControl<Control>(tabPanels[i])?.SetValue(IsVisibleProperty, false);
        }
        var (btn, panel) = tab switch
        {
            "diff"     => ("DiffTabBtn",     "DiffPanel"),
            "branches" => ("BranchesTabBtn", "BranchesPanel"),
            "console"  => ("ConsoleTabBtn",  "ConsolePanel"),
            _          => ("LogTabBtn",      "LogPanel")
        };
        this.FindControl<Button>(btn)?.Classes.Add("active");
        this.FindControl<Control>(panel)?.SetValue(IsVisibleProperty, true);
    }

    private void ShowNoRepo()
    {
        SetVisible("NoRepoPanel",      true);
        SetVisible("CleanPanel",       false);
        SetVisible("CommitAreaBorder", false);
        this.FindControl<ItemsControl>("FileCheckList")
            ?.SetValue(ItemsControl.ItemsSourceProperty, null);
    }

    private void ShowLoading(string? text = null)
    {
        var o = this.FindControl<Border>("LoadingOverlay");
        if (o != null) o.IsVisible = true;
        SetText("LoadingText", text ?? L("Git.Loading"));
    }

    private void HideLoading()
    {
        var o = this.FindControl<Border>("LoadingOverlay");
        if (o != null) o.IsVisible = false;
    }

    private void SetText(string name, string text)
    {
        var t = this.FindControl<TextBlock>(name);
        if (t != null) t.Text = text;
    }

    private void SetVisible(string name, bool visible)
    {
        var c = this.FindControl<Control>(name);
        if (c != null) c.IsVisible = visible;
    }

    private void HookWatermarkForeground(string textBoxName)
    {
        void Apply()
        {
            var textBox = this.FindControl<TextBox>(textBoxName);
            if (textBox != null)
                ForceWatermarkForeground(textBox);
        }

        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
        Opened += (_, _) => Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
    }

    private static void ForceWatermarkForeground(TextBox textBox)
    {
        void Apply()
        {
            var watermark = textBox
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => string.Equals(tb.Name, "PART_Watermark", StringComparison.Ordinal));

            if (watermark == null)
                return;

            watermark.Foreground = WhiteWatermarkBrush;
            watermark.Opacity = 1;
        }

        textBox.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
        textBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
        };

        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
    }

    private void NotifyWorkspaceRefreshRequested()
        => WorkspaceRefreshRequested?.Invoke(this, EventArgs.Empty);

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || !IsActive)
            return;

        await RefreshAsync(showLoadingOverlay: false);
    }

    private GitCommit? ResolveCommitContext(object? sender)
    {
        var commit = (sender as MenuItem)?.DataContext as GitCommit ?? _selectedCommit;
        if (commit != null)
            _selectedCommit = commit;

        return commit;
    }

    // ═══════════════════════════════════════════════════════════
    //  Title bar
    // ═══════════════════════════════════════════════════════════

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object? sender, RoutedEventArgs e)    => Close();
}
