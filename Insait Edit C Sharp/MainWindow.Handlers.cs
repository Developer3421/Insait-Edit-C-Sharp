// ============================================================
//  MainWindow.Handlers.cs  — partial class
//  Build / Run / Save / AI / Dialog handlers
// ============================================================
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Services;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Esp.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

// Single definition of the enum for the whole partial class.
public enum SaveConfirmationResult { Save, DontSave, Cancel }

public partial class MainWindow
{
    // ═══════════════════════════════════════════════════════════
    //  Build service wiring
    // ═══════════════════════════════════════════════════════════
    private void InitializeBuildService()
    {
        _buildService.OutputReceived += (_, e) =>
            Dispatcher.UIThread.Post(() => { _buildOutput.Append(e.Output); UpdateBuildOutput(); });
        _buildService.BuildStarted += (_, _) =>
            Dispatcher.UIThread.Post(() => { _isBuildInProgress = true; UpdateBuildButtons(); _viewModel.StatusText = "Building..."; });
        _buildService.BuildCompleted += (_, e) =>
            Dispatcher.UIThread.Post(() => { _isBuildInProgress = false; UpdateBuildButtons(); _viewModel.StatusText = e.Result.Success ? "Build succeeded" : "Build failed — see Build output"; });

        _nanoBuildService.OutputReceived += (_, e) =>
            Dispatcher.UIThread.Post(() => { _buildOutput.Append(e.Output); UpdateBuildOutput(); });
        _nanoBuildService.BuildStarted += (_, _) =>
            Dispatcher.UIThread.Post(() => { _isBuildInProgress = true; UpdateBuildButtons(); _viewModel.StatusText = "Building nanoFramework project..."; });
        _nanoBuildService.BuildCompleted += (_, e) =>
            Dispatcher.UIThread.Post(() => { _isBuildInProgress = false; UpdateBuildButtons(); _viewModel.StatusText = e.Result.Success ? "nanoFramework build succeeded" : "nanoFramework build failed"; });

        // Publish service wiring — output goes to the Build panel
        _publishService.OutputReceived += (_, e) =>
            Dispatcher.UIThread.Post(() => { _buildOutput.Append(e.Output); UpdateBuildOutput(); });
        _publishService.PublishStarted += (_, _) =>
            Dispatcher.UIThread.Post(() => { _isBuildInProgress = true; UpdateBuildButtons(); _viewModel.StatusText = "Publishing..."; });
        _publishService.PublishCompleted += (_, e) =>
            Dispatcher.UIThread.Post(() => { _isBuildInProgress = false; UpdateBuildButtons(); _viewModel.StatusText = e.Result.Success ? "Publish succeeded" : "Publish failed — see Build output"; });
    }

    private void UpdateBuildOutput()
    {
        var t = this.FindControl<SelectableTextBlock>("BuildOutputText");
        if (t != null) t.Text = _buildOutput.ToString();
        this.FindControl<ScrollViewer>("BuildOutputScrollViewer")?.ScrollToEnd();
    }

    private void UpdateBuildButtons()
    {
        var b = this.FindControl<Button>("BuildProjectButton");
        var c = this.FindControl<Button>("CancelBuildButton");
        if (b != null) b.IsEnabled = !_isBuildInProgress;
        if (c != null) c.IsVisible = _isBuildInProgress;
    }

    // ═══════════════════════════════════════════════════════════
    //  Terminal
    // ═══════════════════════════════════════════════════════════
    private void InitializeTerminal()
    {
        var container = this.FindControl<Border>("TerminalContainer");
        if (container == null) return;
        _terminalControl = new TerminalControl();
        container.Child = _terminalControl;
        _terminalControl.WorkingDirectory = _projectPath ?? Environment.CurrentDirectory;
    }

    // ═══════════════════════════════════════════════════════════
    //  Tool-window panel switching
    // ═══════════════════════════════════════════════════════════
    private void SwitchToolWindowPanel(string panelName)
    {
        string[] panels  = { "TerminalContainer", "ProblemsPanel", "BuildPanel", "RunPanel" };
        string[] buttons = { "TerminalTabButton", "ProblemsTabButton", "BuildTabButton", "RunTabButton" };

        foreach (var p in panels)  this.FindControl<Control>(p)?.SetValue(IsVisibleProperty, false);
        foreach (var b in buttons) SetPanelTabActive(b, false);

        var (panel, btn) = panelName switch
        {
            "terminal" => ("TerminalContainer", "TerminalTabButton"),
            "problems" => ("ProblemsPanel",     "ProblemsTabButton"),
            "build"    => ("BuildPanel",         "BuildTabButton"),
            "run"      => ("RunPanel",           "RunTabButton"),
            _          => ("TerminalContainer", "TerminalTabButton")
        };
        this.FindControl<Control>(panel)?.SetValue(IsVisibleProperty, true);
        SetPanelTabActive(btn, true);

        // Always ensure the bottom panel rows are visible when switching tabs
        EnsureBottomPanelVisible();
    }

    private void SetPanelTabActive(string name, bool active)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;
        if (active) btn.Classes.Add("active"); else btn.Classes.Remove("active");
    }

    // ═══════════════════════════════════════════════════════════
    //  Cursor position
    // ═══════════════════════════════════════════════════════════
    private void UpdateCursorPositionDisplay(int line, int column)
    {
        var tb = this.FindControl<TextBlock>("CursorPositionText");
        if (tb != null) tb.Text = $"Ln {line}, Col {column}";
    }

    // ═══════════════════════════════════════════════════════════
    //  Save
    // ═══════════════════════════════════════════════════════════
    private async Task SaveCurrentFileAsync()
    {
        if (_viewModel.ActiveTab == null) return;
        var tab = _viewModel.ActiveTab;
        if (string.IsNullOrEmpty(tab.FilePath)) { await SaveCurrentFileAsAsync(); return; }
        try
        {
            if (_insaitEditor != null) tab.Content = await _insaitEditor.GetContentAsync();
            await File.WriteAllTextAsync(tab.FilePath, tab.Content);
            tab.IsDirty = false; _insaitEditor?.MarkAsSaved();
            _viewModel.StatusText = $"Saved: {tab.FileName}";
        }
        catch (Exception ex) { _viewModel.StatusText = $"Error saving: {ex.Message}"; }
    }

    private async Task SaveCurrentFileAsAsync()
    {
        var tl = GetTopLevel(this);
        if (tl == null) return;
        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File As", DefaultExtension = "cs",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("C# Files")    { Patterns = new[] { "*.cs" } },
                new("AXAML Files") { Patterns = new[] { "*.axaml" } },
                new("All Files")   { Patterns = new[] { "*.*" } }
            }
        });
        if (file == null) return;
        try
        {
            if (_insaitEditor != null && _viewModel.ActiveTab != null)
                _viewModel.ActiveTab.Content = await _insaitEditor.GetContentAsync();
            var content = _viewModel.ActiveTab?.Content ?? string.Empty;
            await File.WriteAllTextAsync(file.Path.LocalPath, content);
            if (_viewModel.ActiveTab != null)
            {
                _viewModel.ActiveTab.FilePath = file.Path.LocalPath;
                _viewModel.ActiveTab.FileName = Path.GetFileName(file.Path.LocalPath);
                _viewModel.ActiveTab.IsDirty  = false;
            }
            _insaitEditor?.MarkAsSaved();
            _viewModel.StatusText = $"Saved as: {Path.GetFileName(file.Path.LocalPath)}";
        }
        catch (Exception ex) { _viewModel.StatusText = $"Error saving: {ex.Message}"; }
    }

    private async Task SaveAllFilesAsync()
    {
        foreach (var tab in _viewModel.Tabs.Where(t => t.IsDirty).ToList())
        {
            if (string.IsNullOrEmpty(tab.FilePath)) continue;
            try { await File.WriteAllTextAsync(tab.FilePath, tab.Content); tab.IsDirty = false; }
            catch (Exception ex) { _viewModel.StatusText = $"Error saving {tab.FileName}: {ex.Message}"; }
        }
        _insaitEditor?.MarkAsSaved();
        _viewModel.StatusText = "All files saved";
    }

    private async Task<SaveConfirmationResult> ShowSaveConfirmationDialogAsync(string fileName)
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes", Width = 420, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false, ShowInTaskbar = false, SystemDecorations = SystemDecorations.BorderOnly
        };
        var result = SaveConfirmationResult.Cancel;
        var grid   = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        var msg    = new TextBlock { Text = $"'{fileName}' has unsaved changes. Save before closing?", TextWrapping = TextWrapping.Wrap };
        var btns   = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var saveBtn = new Button { Content = "Save", Width = 80 };
        var dontBtn = new Button { Content = "Don't Save", Width = 100 };
        var cnclBtn = new Button { Content = "Cancel", Width = 80 };
        saveBtn.Click += (_, _) => { result = SaveConfirmationResult.Save;     dialog.Close(); };
        dontBtn.Click += (_, _) => { result = SaveConfirmationResult.DontSave; dialog.Close(); };
        cnclBtn.Click += (_, _) => { result = SaveConfirmationResult.Cancel;   dialog.Close(); };
        btns.Children.Add(saveBtn); btns.Children.Add(dontBtn); btns.Children.Add(cnclBtn);
        Grid.SetRow(msg, 0); Grid.SetRow(btns, 1);
        grid.Children.Add(msg); grid.Children.Add(btns);
        dialog.Content = grid;
        await dialog.ShowDialog(this);
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  Solution / project finders
    // ═══════════════════════════════════════════════════════════
    private string? FindSolutionFile()
    {
        var dir = _projectPath ?? _viewModel.CurrentProjectPath;
        return string.IsNullOrEmpty(dir) ? null : FindSolutionFileFromPath(dir);
    }

    private string? FindSolutionFileFromPath(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        var di = new DirectoryInfo(directory);
        while (di != null)
        {
            var f = di.GetFiles("*.slnx").FirstOrDefault() ?? di.GetFiles("*.sln").FirstOrDefault();
            if (f != null) return f.FullName;
            di = di.Parent;
        }
        return null;
    }

    private string? FindProjectFile(string directoryOrFile)
    {
        if (File.Exists(directoryOrFile))
        {
            var ext = Path.GetExtension(directoryOrFile).ToLowerInvariant();
            if (ext is ".csproj" or ".nfproj" or ".fsproj" or ".vbproj")
                return directoryOrFile;
        }

        var dir = File.Exists(directoryOrFile) ? Path.GetDirectoryName(directoryOrFile) : directoryOrFile;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? Directory.GetFiles(dir, "*.fsproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? Directory.GetFiles(dir, "*.nfproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? Directory.GetFiles(dir, "*.vbproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════
    //  Build / Run / Clean tasks
    // ═══════════════════════════════════════════════════════════
    private async Task BuildProjectAsync()
    {
        if (_isBuildInProgress) { _viewModel.StatusText = "Build already in progress"; return; }
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        await SaveAllFilesAsync();
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        if (IsNanoFrameworkProject(path)) await _nanoBuildService.BuildAsync(path);
        else                              await _buildService.BuildAsync(path);
    }

    private async Task RebuildProjectAsync()
    {
        if (_isBuildInProgress) { _viewModel.StatusText = "Build already in progress"; return; }
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        await SaveAllFilesAsync();
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        if (IsNanoFrameworkProject(path)) await _nanoBuildService.BuildAsync(path);
        else { await _buildService.CleanAsync(path); await _buildService.BuildAsync(path); }
    }

    private async Task CleanProjectAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        if (IsNanoFrameworkProject(path)) _viewModel.StatusText = "Clean not supported for nanoFramework projects";
        else await _buildService.CleanAsync(path);
    }

    private async Task RunProjectAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        SwitchToolWindowPanel("run");
        var cfg = _runConfigService.ActiveConfiguration;
        if (cfg != null) await RunWithConfigurationAsync(cfg);
        else { _viewModel.StatusText = "Running project..."; await _buildService.BuildAndRunAsync(path); }
    }

    private async Task RunWithConfigurationAsync(RunConfiguration config)
    {
        SwitchToolWindowPanel("run");
        _viewModel.StatusText = $"Running: {config.Name}";
        var rt = this.FindControl<SelectableTextBlock>("RunOutputText");
        if (rt != null) rt.Text = string.Empty;
        _runConfigService.OutputReceived += OnRunOutput;
        _runConfigService.RunCompleted   += OnRunCompleted;
        await _runConfigService.RunConfigurationAsync(config);
        _runConfigService.OutputReceived -= OnRunOutput;
        _runConfigService.RunCompleted   -= OnRunCompleted;
    }

    private void OnRunOutput(object? sender, RunOutputEventArgs e) =>
        Dispatcher.UIThread.Post(() => { var t = this.FindControl<SelectableTextBlock>("RunOutputText"); if (t != null) t.Text += e.Output; });

    private void OnRunCompleted(object? sender, RunCompletedEventArgs e) =>
        Dispatcher.UIThread.Post(() => _viewModel.StatusText = e.Result.Success ? "Run completed" : "Run failed");

    private void StopRunningProcess() { _runConfigService.Stop(); _viewModel.StatusText = "Stopped"; }

    // ═══════════════════════════════════════════════════════════
    //  Run configs / Publish / Solution
    // ═══════════════════════════════════════════════════════════
    private async Task ShowRunConfigurationsAsync()
    {
        var path = GetCurrentProjectPath() ?? string.Empty;
        await new RunConfigurationsWindow(path).ShowDialog(this);
        var nt = this.FindControl<TextBlock>("RunConfigNameText");
        if (nt != null) nt.Text = _runConfigService.ActiveConfiguration?.Name ?? "Default";
    }

    private async Task ShowPublishWindowAsync()
    {
        var publishWindow = new PublishWindow(GetCurrentProjectPath() ?? string.Empty);
        await publishWindow.ShowDialog(this);

        var profile = publishWindow.Result;
        if (profile == null) return;

        // Save all open files before publishing
        await SaveAllFilesAsync();

        // Clear build panel output and switch to it (mirror output there too)
        _buildOutput.Clear();
        UpdateBuildOutput();
        SwitchToolWindowPanel("build");
        EnsureBottomPanelVisible();

        // Open the progress window and start publish inside it
        var progressWindow = new PublishProgressWindow(_publishService, profile);
        progressWindow.Opened += (_, _) => progressWindow.StartPublish();
        await progressWindow.ShowDialog(this);
    }

    private async Task ShowMsixManagerWindowAsync()
    {
        await SaveAllFilesAsync();
        await new MsixManagerWindow(GetCurrentProjectPath()).ShowDialog(this);
    }

    private async Task OpenSolutionAsync()
    {
        var tl = GetTopLevel(this);
        if (tl == null) return;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Solution or Project", AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Solution / Project Files") { Patterns = new[] { "*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.nfproj", "*.vbproj" } },
                new("All Files") { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count > 0) { var fp = files[0].Path.LocalPath; _projectPath = fp; LoadProject(fp); UpdateTitle(); }
    }

    private async Task CreateNewSolutionAsync()
    {
        var result = await new NewSolutionWindow().ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(result))
        {
            var dir = File.Exists(result) ? Path.GetDirectoryName(result) ?? result : result;
            _projectPath = dir; _viewModel.CurrentProjectPath = dir;
            _viewModel.FileTreeItems.Clear();
            await _viewModel.LoadProjectFolderAsync(dir);
            UpdateTitle(); _viewModel.StatusText = $"Created solution: {Path.GetFileName(result)}";
        }
    }

    private async Task AddNewProjectToSolutionAsync()
    {
        var sln = FindSolutionFile();
        if (string.IsNullOrEmpty(sln)) { _viewModel.StatusText = "No solution found"; return; }
        var result = await new AddProjectToSolutionWindow(sln).ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(result)) { RefreshFileTree(); _viewModel.StatusText = $"Added project: {Path.GetFileNameWithoutExtension(result)}"; }
    }

    // ═══════════════════════════════════════════════════════════
    //  Layout Manager — unified panel show/hide logic
    //  Tracks every panel's visibility + size so that Zen Mode
    //  and individual toggles never desynchronise.
    // ═══════════════════════════════════════════════════════════

    // ── Runtime state (single source of truth) ───────────────
    private bool   _leftPanelVisible    = true;
    private bool   _rightPanelVisible   = true;
    private bool   _bottomPanelVisible  = true;
    private bool   _activityBarVisible  = true;

    // ── Saved dimensions (last non-zero values) ───────────────
    private double _leftPanelWidth      = 250;
    private double _rightPanelWidth     = 300;
    private double _bottomPanelHeight   = 200;

    // ── Zen mode ─────────────────────────────────────────────
    private bool   _isZenMode           = false;

    // ── Grid / control accessors ──────────────────────────────
    private Grid?         GetMainGrid()    => this.FindControl<Grid>("MainContentGrid");
    private Grid?         GetEditorGrid()  => this.FindControl<Grid>("EditorGrid");
    private Border?       GetSidePanel()   => this.FindControl<Border>("SidePanelBorder");
    private Border?       GetAIPanel()     => this.FindControl<Border>("AIPanelBorder");
    private GridSplitter? LeftSplitter     => this.FindControl<GridSplitter>("LeftPanelSplitter");
    private GridSplitter? RightSplitter    => this.FindControl<GridSplitter>("RightPanelSplitter");
    private GridSplitter? BottomSplitter   => this.FindControl<GridSplitter>("BottomPanelSplitter");

    // ── Low-level helpers ─────────────────────────────────────

    /// <summary>Apply left-panel visibility + column widths atomically.</summary>
    private void ApplyLeftPanel(bool visible)
    {
        var grid = GetMainGrid();
        if (grid == null || grid.ColumnDefinitions.Count < 3) return;

        if (visible && _leftPanelWidth < 50) _leftPanelWidth = 250;

        var col1 = grid.ColumnDefinitions[1];
        var col2 = grid.ColumnDefinitions[2];
        col1.MinWidth = 0;
        col2.MinWidth = 0;
        col1.Width = visible ? new GridLength(_leftPanelWidth) : new GridLength(0);
        col2.Width = visible ? new GridLength(5)               : new GridLength(0);

        var border   = GetSidePanel();
        var splitter = LeftSplitter;
        if (border   != null) border.IsVisible   = visible;
        if (splitter != null) splitter.IsVisible = visible;

        _leftPanelVisible = visible;
    }

    /// <summary>Apply activity-bar visibility + column width atomically.</summary>
    private void ApplyActivityBar(bool visible)
    {
        var grid = GetMainGrid();
        if (grid == null || grid.ColumnDefinitions.Count < 1) return;

        var col0 = grid.ColumnDefinitions[0];
        col0.MinWidth = 0;
        col0.Width = visible ? new GridLength(52) : new GridLength(0);

        var bar = grid.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 0);
        if (bar != null) bar.IsVisible = visible;

        _activityBarVisible = visible;
    }

    /// <summary>Apply right-panel (AI) visibility + column widths atomically.</summary>
    private void ApplyRightPanel(bool visible)
    {
        var grid = GetMainGrid();
        if (grid == null || grid.ColumnDefinitions.Count < 6) return;

        if (visible && _rightPanelWidth < 50) _rightPanelWidth = 300;

        var col4 = grid.ColumnDefinitions[4];
        var col5 = grid.ColumnDefinitions[5];
        col4.MinWidth = 0;
        col5.MinWidth = 0;
        col4.Width = visible ? new GridLength(5)                : new GridLength(0);
        col5.Width = visible ? new GridLength(_rightPanelWidth) : new GridLength(0);

        var border   = GetAIPanel();
        var splitter = RightSplitter;
        if (border   != null) border.IsVisible   = visible;
        if (splitter != null) splitter.IsVisible = visible;

        _rightPanelVisible = visible;
    }

    /// <summary>Apply bottom-panel visibility + row heights atomically.</summary>
    private void ApplyBottomPanel(bool visible)
    {
        var grid = GetEditorGrid();
        if (grid == null || grid.RowDefinitions.Count < 4) return;

        if (visible && _bottomPanelHeight < 30) _bottomPanelHeight = 200;

        var row2 = grid.RowDefinitions[2];
        var row3 = grid.RowDefinitions[3];
        row2.MinHeight = 0;
        row3.MinHeight = 0;
        row2.Height = visible ? new GridLength(5)                  : new GridLength(0);
        row3.Height = visible ? new GridLength(_bottomPanelHeight) : new GridLength(0);

        var splitter = BottomSplitter;
        if (splitter != null) splitter.IsVisible = visible;

        _bottomPanelVisible = visible;
    }

    /// <summary>Snapshot current real sizes from the live grid before hiding.</summary>
    private void SnapshotSizes()
    {
        var mg = GetMainGrid();
        var eg = GetEditorGrid();

        if (mg != null && mg.ColumnDefinitions.Count >= 6)
        {
            var lw = mg.ColumnDefinitions[1].ActualWidth;
            var rw = mg.ColumnDefinitions[5].ActualWidth;
            if (lw > 50)  _leftPanelWidth  = lw;
            if (rw > 50)  _rightPanelWidth = rw;
        }
        if (eg != null && eg.RowDefinitions.Count >= 4)
        {
            var bh = eg.RowDefinitions[3].ActualHeight;
            if (bh > 30) _bottomPanelHeight = bh;
        }
    }

    // ── Public toggle methods ─────────────────────────────────

    /// <summary>Toggle left explorer/side panel.</summary>
    private void ToggleLeftPanel()
    {
        if (_leftPanelVisible) SnapshotSizes();
        ApplyLeftPanel(!_leftPanelVisible);
    }

    /// <summary>Toggle right AI panel.</summary>
    internal void ToggleAIPanel()
    {
        if (_rightPanelVisible) SnapshotSizes();
        ApplyRightPanel(!_rightPanelVisible);
    }

    /// <summary>Toggle bottom terminal/output panel.</summary>
    private void ToggleBottomPanel()
    {
        if (_bottomPanelVisible) SnapshotSizes();
        ApplyBottomPanel(!_bottomPanelVisible);
    }

    /// <summary>Ensure left panel is visible (restores if collapsed).</summary>
    private void EnsureLeftPanelVisible()
    {
        if (!_leftPanelVisible) ApplyLeftPanel(true);
    }

    /// <summary>Ensure bottom panel is visible with a minimum height.</summary>
    private void EnsureBottomPanelVisible()
    {
        if (!_bottomPanelVisible || _bottomPanelHeight < 30)
        {
            if (_bottomPanelHeight < 30) _bottomPanelHeight = 200;
            ApplyBottomPanel(true);
        }
        else
        {
            // Already logically visible; ensure rows are not physically zero
            // (e.g. after restore from Zen Mode before layout pass ran)
            var grid = GetEditorGrid();
            if (grid != null && grid.RowDefinitions.Count >= 4)
            {
                if (grid.RowDefinitions[3].Height.Value < 10)
                    grid.RowDefinitions[3].Height = new GridLength(_bottomPanelHeight);
                if (grid.RowDefinitions[2].Height.Value < 4)
                    grid.RowDefinitions[2].Height = new GridLength(5);
            }
            var sp = BottomSplitter;
            if (sp != null) sp.IsVisible = true;
        }
    }

    // ── Zen Mode ─────────────────────────────────────────────

    // Saved pre-zen panel visibility
    private bool _zenSavedLeft, _zenSavedRight, _zenSavedBottom, _zenSavedActivity;

    private void ToggleZenMode()
    {
        if (GetMainGrid() == null || GetEditorGrid() == null) return;

        _isZenMode = !_isZenMode;

        if (_isZenMode)
        {
            // Save current visibility before hiding
            _zenSavedLeft     = _leftPanelVisible;
            _zenSavedRight    = _rightPanelVisible;
            _zenSavedBottom   = _bottomPanelVisible;
            _zenSavedActivity = _activityBarVisible;

            // Snapshot real sizes before collapsing
            SnapshotSizes();

            // Hide everything
            ApplyActivityBar(false);
            ApplyLeftPanel(false);
            ApplyRightPanel(false);
            ApplyBottomPanel(false);

            _viewModel.StatusText = LocalizationService.Get("Menu.ZenMode");
        }
        else
        {
            // Restore exactly what was visible before Zen mode
            ApplyActivityBar(_zenSavedActivity);
            ApplyLeftPanel(_zenSavedLeft);
            ApplyRightPanel(_zenSavedRight);
            ApplyBottomPanel(_zenSavedBottom);

            _viewModel.StatusText = LocalizationService.Get("Menu.MaximizeRestore");
        }
    }

    private async Task ExecuteAIChatCommandAsync()
    {
        var input = this.FindControl<TextBox>("AIChatInput");
        if (input == null) return;
        var cmd = input.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(cmd)) return;
        input.Text = string.Empty;
        AddAIMessage(cmd, isUser: true);

        if (_isCliMode)
        {
            // CLI mode — use CopilotCliService
            _copilotCliService.WorkingDirectory = _projectPath ?? Environment.CurrentDirectory;
            var result = await _copilotCliService.ExecuteAsync(cmd);
            AddAIMessage(result.Success ? result.Output : $"❌ {result.Output}", isUser: false);
        }
        else
        {
            // Copilot Chat mode — use GitHub Copilot SDK with streaming
            if (!_copilotSdkService.IsAvailable)
            {
                AddAIMessage("⏳ GitHub Copilot is initializing, please wait...", isUser: false);
                await InitializeCopilotSdkAsync();
            }

            // Build context from current file if available
            string? systemPrompt = null;
            if (_viewModel.ActiveTab != null)
            {
                var lang = _viewModel.ActiveTab.Language;
                var fileName = _viewModel.ActiveTab.FileName;
                systemPrompt =
                    "You are GitHub Copilot, an AI coding assistant embedded in Insait Edit IDE. " +
                    $"The user currently has '{fileName}' open (language: {lang}). " +
                    "Help the user with their code, answer questions, suggest improvements, and provide examples. " +
                    "Be concise and helpful.";
            }

            // Show abort button
            var abortBtn = this.FindControl<Button>("AbortRequestButton");
            if (abortBtn != null) abortBtn.IsVisible = true;

            if (_copilotSdkService.IsStreamingEnabled)
            {
                // Create a streaming response bubble
                var (bubble, textBlock) = AddStreamingBubble();
                string accumulated = "";

                // Wire streaming tokens to update the bubble in real-time
                void OnToken(object? s, string token)
                {
                    accumulated += token;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (textBlock != null) textBlock.Text = accumulated;
                        this.FindControl<ScrollViewer>("AIChatScrollViewer")?.ScrollToEnd();
                    });
                }

                _copilotSdkService.StreamingTokenReceived += OnToken;

                string reply;
                if (!string.IsNullOrEmpty(_attachedFilePath))
                {
                    reply = await _copilotSdkService.ChatWithAttachmentsAsync(
                        cmd, new string[] { _attachedFilePath! }, systemPrompt);
                    ClearAttachment();
                }
                else
                {
                    reply = await _copilotSdkService.ChatAsync(cmd, systemPrompt);
                }

                _copilotSdkService.StreamingTokenReceived -= OnToken;

                // Update final content
                Dispatcher.UIThread.Post(() =>
                {
                    if (textBlock != null)
                        textBlock.Text = string.IsNullOrWhiteSpace(accumulated) ? reply : accumulated;
                });
            }
            else
            {
                // Non-streaming: show thinking bubble
                var thinkingBubble = AddAIThinkingBubble();

                string reply;
                if (!string.IsNullOrEmpty(_attachedFilePath))
                {
                    reply = await _copilotSdkService.ChatWithAttachmentsAsync(
                        cmd, new string[] { _attachedFilePath! }, systemPrompt);
                    ClearAttachment();
                }
                else
                {
                    reply = await _copilotSdkService.ChatAsync(cmd, systemPrompt);
                }

                RemoveThinkingBubble(thinkingBubble);
                AddAIMessage(reply, isUser: false);
            }

            // Hide abort button and reset status label
            if (abortBtn != null) abortBtn.IsVisible = false;
            var lbl = this.FindControl<TextBlock>("CopilotStatusLabel");
            if (lbl != null) lbl.Text = "Copilot Chat";
        }

        this.FindControl<ScrollViewer>("AIChatScrollViewer")?.ScrollToEnd();
    }

    /// <summary>Initialize the GitHub Copilot SDK service and update UI status.</summary>
    private async Task InitializeCopilotSdkAsync()
    {
        var statusText  = this.FindControl<TextBlock>("CopilotStatusText");
        var statusBar   = this.FindControl<Border>("CopilotStatusBar");
        var statusIcon  = this.FindControl<TextBlock>("CopilotStatusIcon");
        var statusLabel = this.FindControl<TextBlock>("CopilotStatusLabel");
        var modelLabel  = this.FindControl<TextBlock>("CopilotModelLabel");
        var compactionIndicator = this.FindControl<Border>("CompactionIndicator");
        var compactionText = this.FindControl<TextBlock>("CompactionText");

        // Load persisted settings
        _copilotSdkService.LoadSettings();

        // Update model label
        if (modelLabel != null) modelLabel.Text = _copilotSdkService.CurrentModel;

        // Update streaming toggle visual
        UpdateStreamingToggleVisual();

        // Status messages → UI thread
        _copilotSdkService.StatusChanged += (_, msg) =>
            Dispatcher.UIThread.Post(() => { if (statusText != null) statusText.Text = msg; });

        // Streaming reasoning tokens — update status label in real time
        _copilotSdkService.ReasoningTokenReceived += (_, token) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (statusLabel != null && !string.IsNullOrWhiteSpace(token))
                    statusLabel.Text = "💭 " + (token.Length > 35 ? token[..35] + "…" : token);
            });

        // Tool execution events
        _copilotSdkService.ToolExecutionStarted += (_, toolName) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (statusLabel != null) statusLabel.Text = $"🔧 {toolName}";
            });

        _copilotSdkService.ToolExecutionCompleted += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (statusLabel != null) statusLabel.Text = "Copilot Chat";
            });

        // Compaction events
        _copilotSdkService.CompactionEvent += (_, msg) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (compactionIndicator != null && compactionText != null)
                {
                    compactionText.Text = msg;
                    compactionIndicator.IsVisible = msg.Contains("Compacting");
                }
            });

        // Error events
        _copilotSdkService.ErrorOccurred += (_, msg) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (statusIcon != null) statusIcon.Text = "⚠️";
                if (statusLabel != null) statusLabel.Text = "Error occurred";
            });

        var ok = await _copilotSdkService.InitializeAsync();

        Dispatcher.UIThread.Post(() =>
        {
            if (statusText != null)
                statusText.Text = ok
                    ? $"✅ Connected via GitHub.Copilot.SDK — model: {_copilotSdkService.CurrentModel}"
                    : "⚠️ Not available. Configure GitHub CLI path in Settings, or install: winget install GitHub.cli";
            if (statusBar != null)
                statusBar.Background = ok
                    ? new SolidColorBrush(Color.Parse("#20DCC4FF"))
                    : new SolidColorBrush(Color.Parse("#30F38BA8"));
            if (statusIcon != null)
                statusIcon.Text = ok ? "✨" : "⚠️";
            if (statusLabel != null)
                statusLabel.Text = ok ? "Copilot Chat" : "Copilot unavailable";
            if (modelLabel != null)
                modelLabel.Text = _copilotSdkService.CurrentModel;
        });
    }

    /// <summary>Switch AI panel to GitHub Copilot Chat mode.</summary>
    private void CopilotChatMode_Click(object? sender, RoutedEventArgs e) => SetAIMode(isCli: false);

    /// <summary>Switch AI panel to CLI Commands mode.</summary>
    private void CliMode_Click(object? sender, RoutedEventArgs e) => SetAIMode(isCli: true);

    private void SetAIMode(bool isCli)
    {
        _isCliMode = isCli;

        // Update tab button styles
        var copilotBtn = this.FindControl<Button>("CopilotChatModeButton");
        var cliBtn     = this.FindControl<Button>("CliModeButton");
        var statusBar  = this.FindControl<Border>("CopilotStatusBar");
        var aiInput    = this.FindControl<TextBox>("AIChatInput");
        var copilotBanner = this.FindControl<Border>("CopilotWelcomeBanner");
        var cliBanner     = this.FindControl<Border>("CliWelcomeBanner");
        var panelTitle    = this.FindControl<TextBlock>("AIPanelTitle");

        if (copilotBtn != null)
        {
            copilotBtn.Background = isCli
                ? new SolidColorBrush(Colors.Transparent)
                : new SolidColorBrush(Color.Parse("#FFFFC09F"));
            copilotBtn.Foreground = isCli
                ? new SolidColorBrush(Color.Parse("#FF9E90B0"))
                : new SolidColorBrush(Color.Parse("#FF1F1A24"));
        }
        if (cliBtn != null)
        {
            cliBtn.Background = isCli
                ? new SolidColorBrush(Color.Parse("#FFFFC09F"))
                : new SolidColorBrush(Colors.Transparent);
            cliBtn.Foreground = isCli
                ? new SolidColorBrush(Color.Parse("#FF1F1A24"))
                : new SolidColorBrush(Color.Parse("#FF9E90B0"));
        }
        if (statusBar != null) statusBar.IsVisible = !isCli;
        if (aiInput   != null) aiInput.Watermark = isCli ? "Enter command (create, ls, help...)" : "Ask Copilot anything...";
        if (copilotBanner != null) copilotBanner.IsVisible = !isCli;
        if (cliBanner     != null) cliBanner.IsVisible = isCli;
        if (panelTitle    != null) panelTitle.Text = isCli ? "COPILOT CLI" : "COPILOT CHAT";

        // Hide attachment & copilot-specific controls in CLI mode
        var attachBtn = this.FindControl<Button>("AttachFileButton");
        if (attachBtn != null) attachBtn.IsVisible = !isCli;
        var compaction = this.FindControl<Border>("CompactionIndicator");
        if (compaction != null) compaction.IsVisible = false;
        if (isCli) ClearAttachment();
    }

    /// <summary>Add a "thinking" indicator bubble and return it for later removal.</summary>
    private Border AddAIThinkingBubble()
    {
        var messages = this.FindControl<StackPanel>("AIChatMessages");
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#30DCC4FF")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4), MaxWidth = 280,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Name = "ThinkingBubble",
            Child = new TextBlock
            {
                Text = "⏳ Thinking...", TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = Brushes.White, FontStyle = Avalonia.Media.FontStyle.Italic
            }
        };
        messages?.Children.Add(bubble);
        this.FindControl<ScrollViewer>("AIChatScrollViewer")?.ScrollToEnd();
        return bubble;
    }

    /// <summary>Remove the thinking indicator bubble.</summary>
    private void RemoveThinkingBubble(Border? bubble)
    {
        if (bubble == null) return;
        var messages = this.FindControl<StackPanel>("AIChatMessages");
        messages?.Children.Remove(bubble);
    }

    private void AddAIMessage(string text, bool isUser)
    {
        var messages = this.FindControl<StackPanel>("AIChatMessages");
        if (messages == null) return;
        messages.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse(isUser ? "#FF3D3D4D" : "#40FAB387")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4), MaxWidth = 280,
            HorizontalAlignment = isUser ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
            Child = new SelectableTextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = Brushes.White, SelectionBrush = new SolidColorBrush(Color.Parse("#664FC3F7"))
            }
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Dialog helpers
    // ═══════════════════════════════════════════════════════════
    private async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "")
    {
        var dialog = new Window
        {
            Title = title, Width = 420, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false, ShowInTaskbar = false, SystemDecorations = SystemDecorations.BorderOnly
        };
        string? result = null;
        var grid  = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(20) };
        var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
        var input = new TextBox { Text = defaultValue };
        var btns  = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var ok    = new Button { Content = "OK", Width = 80 };
        var cncl  = new Button { Content = "Cancel", Width = 80 };
        ok.Click   += (_, _) => { result = input.Text; dialog.Close(); };
        cncl.Click += (_, _) => dialog.Close();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = input.Text; dialog.Close(); } };
        btns.Children.Add(ok); btns.Children.Add(cncl);
        Grid.SetRow(label, 0); Grid.SetRow(input, 1); Grid.SetRow(btns, 2);
        grid.Children.Add(label); grid.Children.Add(input); grid.Children.Add(btns);
        dialog.Content = grid;
        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title, Width = 420, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false, ShowInTaskbar = false, SystemDecorations = SystemDecorations.BorderOnly
        };
        bool result = false;
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        var msg  = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var btns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var yes  = new Button { Content = "Yes", Width = 80 };
        var no   = new Button { Content = "No",  Width = 80 };
        yes.Click += (_, _) => { result = true;  dialog.Close(); };
        no.Click  += (_, _) => { result = false; dialog.Close(); };
        btns.Children.Add(yes); btns.Children.Add(no);
        Grid.SetRow(msg, 0); Grid.SetRow(btns, 1);
        grid.Children.Add(msg); grid.Children.Add(btns);
        dialog.Content = grid;
        await dialog.ShowDialog(this);
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  AXAML event handlers
    // ═══════════════════════════════════════════════════════════

    // Toolbar
    private async void BuildProject_Click(object? sender, RoutedEventArgs e)    => await BuildProjectAsync();
    private async void RunProject_Click(object? sender, RoutedEventArgs e)      => await RunProjectAsync();
    private async void DebugProject_Click(object? sender, RoutedEventArgs e)    => await RunProjectAsync();
    private async void Publish_Click(object? sender, RoutedEventArgs e)         => await ShowPublishWindowAsync();
    private async void MsixManager_Click(object? sender, RoutedEventArgs e)     => await ShowMsixManagerWindowAsync();
    private void CancelBuild_Click(object? sender, RoutedEventArgs e)           { _buildService.CancelBuild(); _publishService.Cancel(); StopRunningProcess(); _viewModel.StatusText = "Build cancelled"; }
    private void RunConfigDropdown_Click(object? sender, RoutedEventArgs e)     => _ = ShowRunConfigurationsAsync();
    private void EditConfigurations_Click(object? sender, RoutedEventArgs e)    => _ = ShowRunConfigurationsAsync();
    private void LedPanelDesigner_Click(object? sender, RoutedEventArgs e) => _ = OpenLedPanelDesignerAsync();

    // Tool-window tabs
    private void TerminalTab_Click(object? sender, RoutedEventArgs e)           => SwitchToolWindowPanel("terminal");
    private void ProblemsTab_Click(object? sender, RoutedEventArgs e)           => SwitchToolWindowPanel("problems");
    private void BuildTab_Click(object? sender, RoutedEventArgs e)              => SwitchToolWindowPanel("build");
    private void RunTab_Click(object? sender, RoutedEventArgs e)                => SwitchToolWindowPanel("run");
    private void StatusProblems_Click(object? sender, RoutedEventArgs e)        => SwitchToolWindowPanel("problems");
    private void NewTerminal_Click(object? sender, RoutedEventArgs e)
    {
        // Open a real external terminal (Windows Terminal or cmd.exe)
        _terminalControl?.OpenExternalTerminal(
            title: "Insait Edit — Terminal",
            command: null);
        _viewModel.StatusText = LocalizationService.Get("Menu.NewTerminal");
    }
    private void ClearTerminal_Click(object? sender, RoutedEventArgs e)         => _terminalControl?.ExecuteCommand("cls");
    private void MinimizePanel_Click(object? sender, RoutedEventArgs e)
    {
        SnapshotSizes();
        ApplyBottomPanel(false);
    }
    private void MaximizePanel_Click(object? sender, RoutedEventArgs e)
    {
        _bottomPanelHeight = Math.Max(_bottomPanelHeight, 350);
        ApplyBottomPanel(true);
    }
    private void HidePanel_Click(object? sender, RoutedEventArgs e) => MinimizePanel_Click(sender, e);

    // Editor tabs
    private void TabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is EditorTab tab)
        {
            _viewModel.ActiveTab = tab;
            ShowTabInEditor(tab);
            UpdateAxamlPreviewButton();
            UpdateTabButtonStyles();
        }
    }
    private async void CloseTabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not EditorTab tab) return;
        if (tab.IsDirty)
        {
            var res = await ShowSaveConfirmationDialogAsync(tab.FileName);
            if (res == SaveConfirmationResult.Save)       await SaveCurrentFileAsync();
            else if (res == SaveConfirmationResult.Cancel) return;
        }
        _viewModel.CloseTab(tab);
        if (_insaitEditor != null)
        {
            if (_viewModel.ActiveTab != null) _insaitEditor.SetContent(_viewModel.ActiveTab.Content, _viewModel.ActiveTab.Language);
            else                              _insaitEditor.SetContent(string.Empty, "plaintext");
        }
        UpdateWelcomeScreenVisibility();
        UpdateAxamlPreviewButton();
        UpdateTabButtonStyles();
    }

    // AI Chat
    private void GitHubTui_Click(object? sender, RoutedEventArgs e)             { _terminalControl?.OpenGitHubCopilotTerminal(); SwitchToolWindowPanel("terminal"); }
    private void ClearAIChat_Click(object? sender, RoutedEventArgs e)
    {
        var m = this.FindControl<StackPanel>("AIChatMessages");
        if (m == null) return;
        // Keep only the welcome banners (first 2 children: Copilot banner + CLI banner)
        while (m.Children.Count > 2) m.Children.RemoveAt(m.Children.Count - 1);
        // Clear Copilot SDK conversation history
        _copilotSdkService.ClearHistory();
    }
    private void CloseAIPanel_Click(object? sender, RoutedEventArgs e)          => ToggleAIPanel();
    private async void AIChatInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; await ExecuteAIChatCommandAsync(); }
    }
    private async void SendAI_Click(object? sender, RoutedEventArgs e)          => await ExecuteAIChatCommandAsync();

    // ── Copilot Model Selector ───────────────────────────────────────────
    private void CopilotModelSelector_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var menu = new ContextMenu();
        foreach (var model in CopilotSdkService.AvailableModels)
        {
            var item = new MenuItem
            {
                Header = $"{model.DisplayName}  —  {model.Description}",
                Tag = model.Id,
                FontSize = 11,
                Icon = _copilotSdkService.CurrentModel == model.Id
                    ? new TextBlock { Text = "✓", FontSize = 11, Foreground = Brushes.LimeGreen }
                    : null
            };
            item.Click += async (_, _) =>
            {
                var modelId = model.Id;
                var modelLabel = this.FindControl<TextBlock>("CopilotModelLabel");
                if (modelLabel != null) modelLabel.Text = modelId;
                _viewModel.StatusText = $"Switching model to {model.DisplayName}...";
                await _copilotSdkService.SwitchModelAsync(modelId);
                _viewModel.StatusText = $"Model: {model.DisplayName}";
            };
            menu.Items.Add(item);
        }

        menu.Open(btn);
    }

    // ── Streaming Toggle ─────────────────────────────────────────────────
    private void StreamingToggle_Click(object? sender, RoutedEventArgs e)
    {
        var newState = !_copilotSdkService.IsStreamingEnabled;
        _copilotSdkService.SetStreamingEnabled(newState);
        UpdateStreamingToggleVisual();
        _viewModel.StatusText = newState ? "Streaming enabled ⚡" : "Streaming disabled";
    }

    private void UpdateStreamingToggleVisual()
    {
        var icon = this.FindControl<TextBlock>("StreamingToggleIcon");
        if (icon != null)
        {
            icon.Foreground = _copilotSdkService.IsStreamingEnabled
                ? new SolidColorBrush(Color.Parse("#FFDCC4FF"))
                : new SolidColorBrush(Color.Parse("#50DCC4FF"));
        }
        var btn = this.FindControl<Button>("StreamingToggleButton");
        if (btn != null)
        {
            ToolTip.SetTip(btn,
                _copilotSdkService.IsStreamingEnabled ? "Streaming ON — click to disable" : "Streaming OFF — click to enable");
        }
    }

    // ── Abort Request ────────────────────────────────────────────────────
    private async void AbortRequest_Click(object? sender, RoutedEventArgs e)
    {
        await _copilotSdkService.AbortCurrentRequestAsync();
        var abortBtn = this.FindControl<Button>("AbortRequestButton");
        if (abortBtn != null) abortBtn.IsVisible = false;
        _viewModel.StatusText = "Request aborted";
    }

    // ── File Attachment ──────────────────────────────────────────────────

    private void AttachFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveTab == null)
        {
            _viewModel.StatusText = "No file open to attach";
            return;
        }

        var tab = _viewModel.ActiveTab;
        _attachedFilePath = tab.FilePath;

        var indicator = this.FindControl<Border>("AttachmentIndicator");
        var fileName  = this.FindControl<TextBlock>("AttachmentFileName");
        var attachIcon = this.FindControl<TextBlock>("AttachFileIcon");

        if (indicator != null) indicator.IsVisible = true;
        if (fileName  != null) fileName.Text = tab.FileName;
        if (attachIcon != null) attachIcon.Foreground = new SolidColorBrush(Color.Parse("#FFDCC4FF"));

        _viewModel.StatusText = $"📎 Attached: {tab.FileName}";
    }

    private void RemoveAttachment_Click(object? sender, RoutedEventArgs e)
    {
        ClearAttachment();
        _viewModel.StatusText = "Attachment removed";
    }

    private void ClearAttachment()
    {
        _attachedFilePath = null;
        var indicator = this.FindControl<Border>("AttachmentIndicator");
        var attachIcon = this.FindControl<TextBlock>("AttachFileIcon");
        if (indicator != null) indicator.IsVisible = false;
        if (attachIcon != null) attachIcon.Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0"));
    }

    // ── Session Management ───────────────────────────────────────────────
    private void SessionMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var menu = new ContextMenu();

        // New Session
        var newItem = new MenuItem
        {
            Header = "✨ New Session",
            FontSize = 11
        };
        newItem.Click += async (_, _) =>
        {
            var m = this.FindControl<StackPanel>("AIChatMessages");
            if (m != null) while (m.Children.Count > 2) m.Children.RemoveAt(m.Children.Count - 1);
            await _copilotSdkService.NewSessionAsync();
            _viewModel.StatusText = "New Copilot session started";
        };
        menu.Items.Add(newItem);

        menu.Items.Add(new Separator());

        // List Sessions
        var listItem = new MenuItem
        {
            Header = "📋 List Sessions...",
            FontSize = 11
        };
        listItem.Click += async (_, _) =>
        {
            var sessions = await _copilotSdkService.ListSessionsAsync();
            if (sessions == null || sessions.Count == 0)
            {
                AddAIMessage("📋 No saved sessions found.", isUser: false);
                return;
            }

            var sessionList = string.Join("\n", sessions.Select((s, i) =>
                $"  {i + 1}. {s.SessionId}"));
            AddAIMessage($"📋 Sessions ({sessions.Count}):\n{sessionList}", isUser: false);
        };
        menu.Items.Add(listItem);

        // Current Session ID
        if (_copilotSdkService.CurrentSessionId != null)
        {
            var currentItem = new MenuItem
            {
                Header = $"📌 Current: {_copilotSdkService.CurrentSessionId[..Math.Min(12, _copilotSdkService.CurrentSessionId.Length)]}...",
                FontSize = 11,
                IsEnabled = false
            };
            menu.Items.Add(currentItem);
        }

        menu.Open(btn);
    }

    // ── Streaming Bubble ─────────────────────────────────────────────────
    /// <summary>Create a streaming response bubble and return (bubble, textBlock) for real-time updates.</summary>
    private (Border bubble, SelectableTextBlock textBlock) AddStreamingBubble()
    {
        var messages = this.FindControl<StackPanel>("AIChatMessages");
        var textBlock = new SelectableTextBlock
        {
            Text = "", TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.Parse("#664FC3F7"))
        };
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#40FAB387")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4), MaxWidth = 280,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Child = textBlock
        };
        messages?.Children.Add(bubble);
        this.FindControl<ScrollViewer>("AIChatScrollViewer")?.ScrollToEnd();
        return (bubble, textBlock);
    }

    // Context menu
    private void ContextMenu_ManageNuGet_Click(object? sender, RoutedEventArgs e)        => NuGet_Click(sender, e);
    private void ContextMenu_AddReference_Click(object? sender, RoutedEventArgs e)       => _viewModel.StatusText = "Add Reference coming soon...";
    private void ContextMenu_Cut_Click(object? sender, RoutedEventArgs e)
    {
        var items = GetSelectedTreeItems();
        if (items.Count == 0) return;
        var paths = string.Join(Environment.NewLine, items.Select(x => x.FullPath));
        _ = CopyToClipboardAsync(paths);
        _viewModel.StatusText = items.Count == 1
            ? "Cut: " + items[0].Name
            : "Cut " + items.Count + " items";
    }
    private void ContextMenu_Copy_Click(object? sender, RoutedEventArgs e)
    {
        var items = GetSelectedTreeItems();
        if (items.Count == 0) return;
        var paths = string.Join(Environment.NewLine, items.Select(x => x.FullPath));
        _ = CopyToClipboardAsync(paths);
        _viewModel.StatusText = items.Count == 1
            ? "Copied path: " + items[0].FullPath
            : "Copied " + items.Count + " paths to clipboard";
    }
    private void ContextMenu_Paste_Click(object? sender, RoutedEventArgs e)              { }
    private void ContextMenu_RemoveFromSolution_Click(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "Remove from Solution coming soon...";
    private void ContextMenu_UnloadProject_Click(object? sender, RoutedEventArgs e)      => _viewModel.StatusText = "Unload Project coming soon...";
    private void ContextMenu_GitCommit_Click(object? sender, RoutedEventArgs e)  => _ = OpenGitWindowAsync();
    private void ContextMenu_GitHistory_Click(object? sender, RoutedEventArgs e) => _ = OpenGitWindowAsync();
    private void ContextMenu_GitRevert_Click(object? sender, RoutedEventArgs e)  => _ = OpenGitWindowAsync();

    private void ContextMenu_CopyRelativePath_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        if (item == null) return;
        var root = _projectPath ?? "";
        var rel  = item.FullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? item.FullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar)
            : item.FullPath;
        _ = CopyToClipboardAsync(rel);
        _viewModel.StatusText = "Copied relative path: " + rel;
    }

    private void ContextMenu_CopyFileName_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        if (item == null) return;
        _ = CopyToClipboardAsync(item.Name);
        _viewModel.StatusText = "Copied: " + item.Name;
    }

    private void ContextMenu_Properties_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        if (item?.ItemType == FileTreeItemType.Project || item?.ItemType == FileTreeItemType.EspProject)
        {
            var pf = FindProjectFile(item.FullPath);
            if (!string.IsNullOrEmpty(pf)) { new ProjectPropertiesWindow(pf).ShowDialog(this); return; }
        }
        _viewModel.StatusText = "Properties coming soon...";
    }

    // ═══════════════════════════════════════════════════════════
    //  LED Panel Designer
    // ═══════════════════════════════════════════════════════════
    private async Task OpenLedPanelDesignerAsync()
    {
        var projectPath = GetCurrentProjectPath();

        // Only available for nano framework projects
        if (!string.IsNullOrEmpty(projectPath) && !IsNanoFrameworkProject(projectPath))
        {
            _viewModel.StatusText = "LED Panel Designer is only available for nanoFramework projects.";
            return;
        }

        var designer = new LedPanelDesignerWindow();
        if (!string.IsNullOrEmpty(projectPath))
            designer.SetProjectPath(projectPath);

        await designer.ShowDialog(this);

        // After closing, re-focus the editor if a tab is active
        if (_viewModel.ActiveTab != null)
            _insaitEditor?.FocusEditor();
    }

    // ═══════════════════════════════════════════════════════════
    //  AXAML Preview
    // ═══════════════════════════════════════════════════════════

    /// <summary>Show or hide the AXAML preview button based on the active tab.</summary>
    internal void UpdateAxamlPreviewButton()
    {
        var btn  = this.FindControl<Button>("AxamlPreviewButton");
        if (btn == null) return;

        var tab  = _viewModel.ActiveTab;
        var ext  = Path.GetExtension(tab?.FilePath ?? string.Empty).ToLowerInvariant();
        btn.IsVisible = ext is ".axaml" or ".xaml";
    }

    private async void AxamlPreview_Click(object? sender, RoutedEventArgs e)
        => await OpenAxamlPreviewAsync();

    private async Task OpenAxamlPreviewAsync()
    {
        var tab = _viewModel.ActiveTab;
        if (tab == null)
        {
            _viewModel.StatusText = "No file open";
            return;
        }

        var ext = Path.GetExtension(tab.FilePath ?? string.Empty).ToLowerInvariant();
        if (ext is not (".axaml" or ".xaml"))
        {
            _viewModel.StatusText = "Preview is available only for .axaml / .xaml files";
            return;
        }

        // Get the latest content from the editor (may not be saved yet)
        string content = tab.Content;
        if (_insaitEditor != null)
        {
            try { content = await _insaitEditor.GetContentAsync(); }
            catch { /* use tab.Content as fallback */ }
        }

        _viewModel.StatusText = $"Opening preview: {tab.FileName}";

        var preview = new AxamlPreviewWindow(tab.FilePath ?? string.Empty, content);
        preview.Show(this);   // non-modal so the user can still edit
    }
}

