using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class GitWindow : Window
{
    // ─── Services ─────────────────────────────────────────────
    private readonly GitService _git = new();
    private readonly GitHubAccountService _ghService = new();
    private readonly StringBuilder _console = new();

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

    // ─── Events ───────────────────────────────────────────────
    public event EventHandler<string>? FileOpenRequested;

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

    public GitWindow() { InitializeComponent(); ApplyLocalization(); }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("Git.Title");
        var scopeLabel = this.FindControl<TextBlock>("ScopeLabel");
        if (scopeLabel != null) scopeLabel.Text = L("Git.Scope");
        var refreshBtn = this.FindControl<Button>("RefreshBtn");
        if (refreshBtn != null) ToolTip.SetTip(refreshBtn, L("Git.Refresh"));
        var pullBtn = this.FindControl<Button>("PullBtn");
        if (pullBtn != null) ToolTip.SetTip(pullBtn, L("Git.Pull"));
        var pushBtn = this.FindControl<Button>("PushBtn");
        if (pushBtn != null) ToolTip.SetTip(pushBtn, L("Git.Push"));
        var fetchBtn = this.FindControl<Button>("FetchBtn");
        if (fetchBtn != null) ToolTip.SetTip(fetchBtn, L("Git.Fetch"));
        var stashBtn = this.FindControl<Button>("StashBtn");
        if (stashBtn != null) ToolTip.SetTip(stashBtn, L("Git.Stash"));
        var popStashBtn = this.FindControl<Button>("PopStashBtn");
        if (popStashBtn != null) ToolTip.SetTip(popStashBtn, L("Git.PopStash"));
        var rollbackBtn = this.FindControl<Button>("RollbackBtn");
        if (rollbackBtn != null) ToolTip.SetTip(rollbackBtn, L("Git.Rollback"));
        var createRepoBtn = this.FindControl<Button>("CreateRepoBtn");
        if (createRepoBtn != null) ToolTip.SetTip(createRepoBtn, L("Git.CreateRepo"));
    }

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
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = LocalizationService.Get("Git.SolutionAll"), Tag = "solution" });
        foreach (var p in _projectPaths)
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"📦 {Path.GetFileNameWithoutExtension(p)}",
                Tag = $"project:{p}"
            });
        combo.SelectedIndex = 0;
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

    public async Task RefreshAsync()
    {
        if (!_git.IsRepository) { ShowNoRepo(); return; }

        ShowLoading("Refreshing…");
        try
        {
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

            // Preserve existing selection
            foreach (var f in _allFiles) f.IsSelected = true;

            RefreshFileList();

            bool hasFiles = _allFiles.Count > 0;
            SetVisible("NoRepoPanel", false);
            SetVisible("CleanPanel",  !hasFiles);
            SetVisible("CommitAreaBorder", hasFiles);

            if (_rightTab == "log") await RefreshLogAsync();
            UpdateCommitBtn();
        }
        catch (Exception ex) { AppendConsole($"Error: {ex.Message}"); }
        finally { HideLoading(); }
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
        ShowLoading("Staging…");
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
            await RefreshAsync();
        }
    }

    private async void File_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && sender is StackPanel sp
            && sp.DataContext is GitFileChange c)
        {
            SwitchRightTab("diff");
            await ShowFileDiffAsync(c);
        }
    }

    // Context menu
    private async void CtxViewDiff_Click(object? sender, RoutedEventArgs e)
    {
        if (GetCtxChange(sender) is GitFileChange c)
        {
            SwitchRightTab("diff");
            await ShowFileDiffAsync(c);
        }
    }

    private async void CtxDiscard_Click(object? sender, RoutedEventArgs e)
    {
        if (GetCtxChange(sender) is GitFileChange c)
        {
            await _git.DiscardChangesAsync(c.FilePath);
            await RefreshAsync();
        }
    }

    private void CtxOpenFile_Click(object? sender, RoutedEventArgs e)
    {
        if (GetCtxChange(sender) is GitFileChange c) FileOpenRequested?.Invoke(this, c.FullPath);
    }

    private static GitFileChange? GetCtxChange(object? sender)
        => (sender as MenuItem)?.DataContext as GitFileChange;

    // ─── Diff ─────────────────────────────────────────────────
    private async Task ShowFileDiffAsync(GitFileChange change)
    {
        SetText("DiffFileLabel", change.FilePath);
        var diff = await _git.GetFileDiffAsync(change.FilePath, staged: false);
        if (string.IsNullOrEmpty(diff))
            diff = await _git.GetFileDiffAsync(change.FilePath, staged: true);
        var t = this.FindControl<SelectableTextBlock>("DiffOutputText");
        if (t != null) t.Text = string.IsNullOrEmpty(diff) ? "(No diff available)" : diff;
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
        if (_selectedCommit == null) return;
        ShowLoading("Checking out…");
        AppendConsole($"git checkout {_selectedCommit.Hash}");
        await _git.CheckoutBranchAsync(_selectedCommit.Hash);
        HideLoading(); await RefreshAsync();
    }

    private void CtxNewBranchFromHere_Click(object? sender, RoutedEventArgs e)
        => AppendConsole("Create branch from commit — use Branch menu");

    private async void CtxCherryPick_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedCommit == null) return;
        ShowLoading("Cherry-picking…");
        AppendConsole($"git cherry-pick {_selectedCommit.Hash}");
        await _git.RunGitCommandInternalAsync($"cherry-pick {_selectedCommit.Hash}");
        HideLoading(); await RefreshAsync();
    }

    private async void CtxRevertCommit_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedCommit == null) return;

        bool isRoot = await _git.IsRootCommitAsync(_selectedCommit.Hash);

        // ── Ask the user what they really want ────────────────────────────
        var dialog = new Window
        {
            Title = "Revert / Reset commit",
            Width = 460, Height = isRoot ? 220 : 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

        sp.Children.Add(new TextBlock
        {
            Text = $"Commit: {_selectedCommit.ShortHash} — {_selectedCommit.Message}",
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC09F")),
            TextWrapping = TextWrapping.Wrap
        });

        string? choice = null;

        if (!isRoot)
        {
            // Option A — safe revert (creates a new "undo" commit)
            var revertBtn = MakeBtn("↩ Revert (create undo-commit)", "#FF3E3050", "#FFF0E8F4");
            revertBtn.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            revertBtn.Click += (_, _) => { choice = "revert"; dialog.Close(); };

            sp.Children.Add(new TextBlock
            {
                Text = "↩  Revert — applies the inverse of this commit as a new commit (safe, keeps history).",
                FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0")),
                TextWrapping = TextWrapping.Wrap
            });
            sp.Children.Add(revertBtn);
        }

        // Option B — hard reset to this commit (dangerous but "correct" rollback)
        var resetBtn = MakeBtn(
            isRoot ? "⚠ Reset to initial state (hard reset)" : "⚠ Reset TO this commit (hard reset — loses newer commits)",
            "#FF4A2020", "#FFF38BA8");
        resetBtn.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        resetBtn.Click += (_, _) => { choice = "reset"; dialog.Close(); };

        sp.Children.Add(new TextBlock
        {
            Text = isRoot
                ? "Reset — restores the project exactly to the initial commit state. All subsequent commits and local changes will be lost."
                : "Reset — moves HEAD (and the branch) back to this commit. All newer commits and local changes WILL BE LOST permanently.",
            FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#FFF38BA8")),
            TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(resetBtn);

        var cancelBtn = MakeBtn("Cancel", "#FF3E3050", "#FFA0A0B0");
        cancelBtn.Click += (_, _) => dialog.Close();
        sp.Children.Add(cancelBtn);

        dialog.Content = sp;
        await dialog.ShowDialog(this);

        if (choice == null) return;

        // ── Execute chosen action ──────────────────────────────────────────
        if (choice == "revert")
        {
            ShowLoading("Reverting…");
            AppendConsole($"git revert --no-edit {_selectedCommit.Hash}");
            await _git.RunGitCommandInternalAsync($"revert --no-edit {_selectedCommit.Hash}");
            HideLoading();
        }
        else // reset
        {
            if (isRoot)
            {
                // Hard reset to root commit brings back the exact initial state
                ShowLoading("Resetting to initial commit…");
                AppendConsole($"git reset --hard {_selectedCommit.Hash}");
                var r = await _git.ResetHardAsync(_selectedCommit.Hash);
                HideLoading();
                AppendConsole(r.Success
                    ? $"✅ Reset to initial commit {_selectedCommit.ShortHash}"
                    : $"❌ Reset failed: {r.Error}");
            }
            else
            {
                ShowLoading("Resetting…");
                AppendConsole($"git reset --hard {_selectedCommit.Hash}");
                var r = await _git.ResetHardAsync(_selectedCommit.Hash);
                HideLoading();
                AppendConsole(r.Success
                    ? $"✅ Reset to {_selectedCommit.ShortHash}"
                    : $"❌ Reset failed: {r.Error}");
            }
        }

        await RefreshAsync();
    }

    private async void CtxCopyHash_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedCommit == null) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null) await clip.SetTextAsync(_selectedCommit.Hash);
        AppendConsole($"Copied: {_selectedCommit.Hash}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Toolbar buttons
    // ═══════════════════════════════════════════════════════════

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private void CommitToolbar_Click(object? sender, RoutedEventArgs e)
        => this.FindControl<TextBox>("CommitMsgBox")?.Focus();

    private async void Pull_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Pulling…"); AppendConsole("git pull");
        var r = await _git.PullAsync();
        HideLoading();
        AppendConsole(r.Success ? "Pull completed" : $"Pull error: {r.Error}");
        await RefreshAsync();
    }

    private async void Push_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Pushing…");
        var br = await _git.GetCurrentBranchAsync();
        AppendConsole($"git push origin {br}");
        var r = await _git.PushAsync("origin", br);
        if (!r.Success && r.Error.Contains("no upstream"))
            r = await _git.PushAsync("origin", br, setUpstream: true);
        HideLoading();
        AppendConsole(r.Success ? "Push completed" : $"Push error: {r.Error}");
        await RefreshAsync();
    }

    private async void Fetch_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Fetching…"); AppendConsole("git fetch --all");
        var r = await _git.FetchAsync();
        HideLoading();
        AppendConsole(r.Success ? "Fetch completed" : $"Fetch error: {r.Error}");
        await RefreshAsync();
    }

    private async void Stash_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Stashing…"); AppendConsole("git stash push");
        var r = await _git.StashAsync();
        HideLoading();
        AppendConsole(r.Success ? "Stash created" : $"Stash error: {r.Error}");
        await RefreshAsync();
    }

    private async void PopStash_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Popping stash…"); AppendConsole("git stash pop");
        var r = await _git.StashPopAsync();
        HideLoading();
        AppendConsole(r.Success ? "Stash popped" : $"Pop error: {r.Error}");
        await RefreshAsync();
    }

    private async void Rollback_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Rolling back…"); AppendConsole("git checkout -- .");
        var r = await _git.DiscardAllChangesAsync();
        HideLoading();
        AppendConsole(r.Success ? "Rollback completed" : $"Error: {r.Error}");
        await RefreshAsync();
    }

    private void ConsoleTabFromToolbar_Click(object? sender, RoutedEventArgs e)
        => SwitchRightTab("console");

    // ═══════════════════════════════════════════════════════════
    //  Create GitHub Repository — using GitHubAccountService
    // ═══════════════════════════════════════════════════════════

    private async void CreateRepo_Click(object? sender, RoutedEventArgs e)
    {
        // Check gh CLI is available via GitHubAccountService
        bool cliOk = await _ghService.IsGitHubCliInstalledAsync();
        bool loggedIn = cliOk && await _ghService.IsLoggedInAsync();

        // Build dialog
        var dialog = new Window
        {
            Title = "Create GitHub Repository",
            Width = 460, Height = loggedIn ? 300 : 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };

        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

        sp.Children.Add(new TextBlock
        {
            Text = "🐙 Create GitHub Repository",
            FontSize = 14, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC09F"))
        });

        if (!cliOk)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "⚠ GitHub CLI ('gh') not found.\nInstall from https://cli.github.com",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#FFF38BA8"))
            });
            var closeBtn = new Button { Content = "Close", Width = 80 };
            closeBtn.Click += (_, _) => dialog.Close();
            sp.Children.Add(closeBtn);
            dialog.Content = sp;
            await dialog.ShowDialog(this);
            return;
        }

        if (!loggedIn)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "⚠ Not logged in to GitHub.\nRun: gh auth login",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#FFF9E2AF"))
            });
            var loginBtn = MakeBtn("Login with GitHub", "#FFFFC09F", "#FF1F1A24");
            loginBtn.Click += async (_, _) =>
            {
                dialog.Close();
                await _ghService.LoginAsync();
                AppendConsole("GitHub login opened in browser");
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
                Text = $"Logged in as @{account.Username}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#FFA6E3A1"))
            });

        sp.Children.Add(new TextBlock
        {
            Text = "Repository name:",
            FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0"))
        });

        var defaultName = Path.GetFileName(_solutionPath ?? "my-project");
        var nameBox = new TextBox
        {
            Text = defaultName, FontSize = 12,
            Background = new SolidColorBrush(Color.Parse("#FF1F1A24")),
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6)
        };
        sp.Children.Add(nameBox);

        sp.Children.Add(new TextBlock
        {
            Text = "Description (optional):",
            FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0"))
        });
        var descBox = new TextBox
        {
            FontSize = 12, Watermark = "Short description",
            Background = new SolidColorBrush(Color.Parse("#FF1F1A24")),
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6)
        };
        sp.Children.Add(descBox);

        var privateCheck = new CheckBox
        {
            Content = "Private repository", IsChecked = true, FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4"))
        };
        sp.Children.Add(privateCheck);

        var btns = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelBtn = MakeBtn("Cancel",  "#FF3E3050", "#FFF0E8F4");
        var createBtn = MakeBtn("Create",  "#FFFFC09F", "#FF1F1A24");

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

    private async Task CreateGitHubRepoAsync(string name, string desc, bool isPrivate)
    {
        ShowLoading("Creating repository…");
        SwitchRightTab("console");

        var vis     = isPrivate ? "--private" : "--public";
        var descArg = string.IsNullOrWhiteSpace(desc) ? "" : $"--description \"{desc}\"";
        var cmd     = $"repo create \"{name}\" {vis} {descArg} --source . --remote origin --push";
        AppendConsole($"gh {cmd}");

        // GitHubAccountService verified the login; now run gh CLI directly
        bool ok = await RunGhCommandAsync(cmd);

        HideLoading();
        AppendConsole(ok
            ? $"✅ Repository '{name}' created and pushed to GitHub!"
            : "❌ Failed. Make sure 'gh' CLI is installed and authenticated (gh auth login).");
        await RefreshAsync();
    }

    private async Task<bool> RunGhCommandAsync(string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gh", Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _currentRepoRoot ?? _solutionPath ?? Environment.CurrentDirectory
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (!string.IsNullOrWhiteSpace(stdout)) AppendConsole(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) AppendConsole(stderr.Trim());
            return p.ExitCode == 0;
        }
        catch (Exception ex) { AppendConsole($"gh error: {ex.Message}"); return false; }
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
            Header = "➕ New Branch…",
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
            menu.Items.Add(new MenuItem { Header = "☁ Remote Branches", IsEnabled = false });
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
        ShowLoading($"Checking out {branch}…");
        AppendConsole($"git checkout {branch}");
        var r = await _git.CheckoutBranchAsync(branch);
        HideLoading();
        AppendConsole(r.Success ? $"Switched to {branch}" : $"Error: {r.Error}");
        await RefreshAsync();
    }

    private async Task CreateBranchDialogAsync()
    {
        var dialog = new Window
        {
            Title = "New Branch", Width = 360, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly, CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#FF2A2230"))
        };
        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = "Branch name:", FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")) });
        var box = new TextBox { FontSize = 12,
            Background = new SolidColorBrush(Color.Parse("#FF1F1A24")),
            Foreground = new SolidColorBrush(Color.Parse("#FFF0E8F4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6) };
        var btns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var ok  = MakeBtn("Create", "#FFFFC09F", "#FF1F1A24");
        var can = MakeBtn("Cancel", "#FF3E3050", "#FFF0E8F4");
        ok.Click  += async (_, _) => { dialog.Close(); await CreateBranchAsync(box.Text ?? ""); };
        can.Click += (_, _) => dialog.Close();
        btns.Children.Add(can); btns.Children.Add(ok);
        sp.Children.Add(box); sp.Children.Add(btns);
        dialog.Content = sp;
        await dialog.ShowDialog(this);
    }

    private async Task CreateBranchAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        ShowLoading($"Creating {name}…");
        AppendConsole($"git checkout -b {name}");
        var r = await _git.CreateBranchAsync(name, checkout: true);
        HideLoading();
        AppendConsole(r.Success ? $"Created {name}" : $"Error: {r.Error}");
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
    private void ConsoleTab_Click(object? sender, RoutedEventArgs e) => SwitchRightTab("console");

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
        if (toStage.Count == 0 && !amend) { AppendConsole("No files selected"); return; }

        ShowLoading("Staging…");
        foreach (var f in toStage)
        {
            AppendConsole($"git add \"{f.FilePath}\"");
            await _git.StageFileAsync(f.FilePath);
        }

        ShowLoading("Committing…");
        AppendConsole($"git commit{(amend ? " --amend" : "")} -m \"{msg}\"");
        var r = await _git.CommitAsync(msg, amend);

        if (r.Success)
        {
            if (box != null) box.Text = "";
            AppendConsole("Commit successful");
            if (andPush)
            {
                ShowLoading("Pushing…");
                var br = await _git.GetCurrentBranchAsync();
                AppendConsole($"git push origin {br}");
                var pr = await _git.PushAsync("origin", br);
                if (!pr.Success && pr.Error.Contains("no upstream"))
                    pr = await _git.PushAsync("origin", br, setUpstream: true);
                AppendConsole(pr.Success ? "Push completed" : $"Push error: {pr.Error}");
            }
        }
        else AppendConsole($"Commit error: {r.Error}");

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
        ShowLoading("Initializing…");
        AppendConsole($"git init \"{_solutionPath}\"");
        var r = await _git.InitAsync(_solutionPath);
        HideLoading();
        if (r.Success)
        {
            _currentRepoRoot = _solutionPath;
            _git.RepositoryPath = _solutionPath;
            AppendConsole("Repository initialized");
        }
        else AppendConsole($"Init error: {r.Error}");
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
        if (t != null) t.Text = "Git console ready…";
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
        string[] tabBtns   = { "LogTabBtn", "DiffTabBtn", "ConsoleTabBtn" };
        string[] tabPanels = { "LogPanel",  "DiffPanel",  "ConsolePanel"  };
        for (int i = 0; i < tabBtns.Length; i++)
        {
            this.FindControl<Button>(tabBtns[i])?.Classes.Remove("active");
            this.FindControl<Control>(tabPanels[i])?.SetValue(IsVisibleProperty, false);
        }
        var (btn, panel) = tab switch
        {
            "diff"    => ("DiffTabBtn",    "DiffPanel"),
            "console" => ("ConsoleTabBtn", "ConsolePanel"),
            _         => ("LogTabBtn",     "LogPanel")
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

    private void ShowLoading(string text = "Loading…")
    {
        var o = this.FindControl<Border>("LoadingOverlay");
        if (o != null) o.IsVisible = true;
        SetText("LoadingText", text);
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
