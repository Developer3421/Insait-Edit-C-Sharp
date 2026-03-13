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

    private void ClearRunOutput()
    {
        var runText = this.FindControl<SelectableTextBlock>("RunOutputText");
        if (runText != null)
            runText.Text = string.Empty;
    }

    private void AppendRunOutput(string output)
    {
        var runText = this.FindControl<SelectableTextBlock>("RunOutputText");
        if (runText != null)
            runText.Text += output;

        this.FindControl<ScrollViewer>("RunOutputScrollViewer")?.ScrollToEnd();
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
        string[] panels = { "TerminalContainer", "ProblemsPanel", "BuildPanel", "RunPanel" };
        string[] buttons = { "TerminalTabButton", "ProblemsTabButton", "BuildTabButton", "RunTabButton" };

        foreach (var p in panels) this.FindControl<Control>(p)?.SetValue(IsVisibleProperty, false);
        foreach (var b in buttons) SetPanelTabActive(b, false);

        var (panel, btn) = panelName switch
        {
            "terminal" => ("TerminalContainer", "TerminalTabButton"),
            "problems" => ("ProblemsPanel", "ProblemsTabButton"),
            "build" => ("BuildPanel", "BuildTabButton"),
            "run" => ("RunPanel", "RunTabButton"),
            _ => ("TerminalContainer", "TerminalTabButton")
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
            Title = "Save File As",
            DefaultExtension = "cs",
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
                _viewModel.ActiveTab.IsDirty = false;
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
            Title = "Unsaved Changes",
            Width = 420,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly
        };
        var result = SaveConfirmationResult.Cancel;
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        var msg = new TextBlock { Text = $"'{fileName}' has unsaved changes. Save before closing?", TextWrapping = TextWrapping.Wrap };
        var btns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var saveBtn = new Button { Content = "Save", Width = 80 };
        var dontBtn = new Button { Content = "Don't Save", Width = 100 };
        var cnclBtn = new Button { Content = "Cancel", Width = 80 };
        saveBtn.Click += (_, _) => { result = SaveConfirmationResult.Save; dialog.Close(); };
        dontBtn.Click += (_, _) => { result = SaveConfirmationResult.DontSave; dialog.Close(); };
        cnclBtn.Click += (_, _) => { result = SaveConfirmationResult.Cancel; dialog.Close(); };
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

    // All project-file extensions that the Properties window understands.
    // MSBuild-based (.csproj, .fsproj, .vbproj, .nfproj) get full editing;
    // others (.pyproj, .esproj, .njsproj, .sqlproj, …) open with basic info.
    private static readonly string[] _projectExtensions =
    {
        ".csproj", ".fsproj", ".vbproj", ".nfproj",
        ".pyproj", ".esproj", ".njsproj", ".sqlproj",
        ".vcxproj", ".wixproj", ".shproj",
    };

    private string? FindProjectFile(string directoryOrFile)
    {
        if (File.Exists(directoryOrFile))
        {
            var ext = Path.GetExtension(directoryOrFile).ToLowerInvariant();
            if (_projectExtensions.Contains(ext))
                return directoryOrFile;
        }

        var dir = File.Exists(directoryOrFile) ? Path.GetDirectoryName(directoryOrFile) : directoryOrFile;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

        // Search in priority order: C# first, then others
        foreach (var ext in _projectExtensions)
        {
            var found = Directory.GetFiles(dir, $"*{ext}", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (found != null) return found;
        }
        return null;
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
        await _buildService.BuildAsync(path);
    }

    private async Task RebuildProjectAsync()
    {
        if (_isBuildInProgress) { _viewModel.StatusText = "Build already in progress"; return; }
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        await SaveAllFilesAsync();
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        await _buildService.CleanAsync(path);
        await _buildService.BuildAsync(path);
    }

    private async Task CleanProjectAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        _buildOutput.Clear(); UpdateBuildOutput(); SwitchToolWindowPanel("build");
        await _buildService.CleanAsync(path);
    }

    private async Task RunProjectAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        SwitchToolWindowPanel("run");
        var cfg = await GetRunConfigurationAsync();
        if (cfg != null) await RunWithConfigurationAsync(cfg);
        else
        {
            _viewModel.StatusText = "Running project (Release)...";
            await _buildService.BuildAndRunAsync(path, "Release");
        }
    }

    private async Task RunProjectInDebugModeAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrEmpty(path)) { _viewModel.StatusText = "No project loaded"; return; }
        SwitchToolWindowPanel("run");
        var cfg = await GetDebugRunConfigurationAsync();
        if (cfg != null) await RunWithConfigurationAsync(cfg);
        else
        {
            _viewModel.StatusText = "Running project (Debug)...";
            await _buildService.BuildAndRunAsync(path, "Debug");
        }
    }

    private async Task RunWithConfigurationAsync(RunConfiguration config)
    {
        SwitchToolWindowPanel("run");
        _viewModel.StatusText = $"Running: {config.Name}";
        ClearRunOutput();
        _runConfigService.OutputReceived += OnRunOutput;
        _runConfigService.RunCompleted += OnRunCompleted;
        await _runConfigService.RunConfigurationAsync(config);
        _runConfigService.OutputReceived -= OnRunOutput;
        _runConfigService.RunCompleted -= OnRunCompleted;
    }


    private async Task<RunConfiguration?> GetRunConfigurationAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        await _runConfigService.LoadConfigurationsAsync(path);

        var activeConfiguration = _runConfigService.ActiveConfiguration;
        if (activeConfiguration != null)
        {
            if (!string.Equals(activeConfiguration.Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
                return activeConfiguration;

            return CloneWithConfiguration(activeConfiguration, "Release");
        }

        var projectFile = FindProjectFile(path);
        if (string.IsNullOrWhiteSpace(projectFile))
            return null;

        var projectDirectory = Path.GetDirectoryName(projectFile) ?? string.Empty;
        return new RunConfiguration
        {
            Name = $"{Path.GetFileNameWithoutExtension(projectFile)} (Release)",
            ProjectPath = projectFile,
            WorkingDirectory = projectDirectory,
            Configuration = "Release",
            OutputType = "Exe",
            IsDefault = true
        };
    }

    private async Task<RunConfiguration?> GetDebugRunConfigurationAsync()
    {
        var path = GetCurrentProjectPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        await _runConfigService.LoadConfigurationsAsync(path);

        var activeConfiguration = _runConfigService.ActiveConfiguration;
        if (activeConfiguration != null)
        {
            if (string.Equals(activeConfiguration.Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
                return activeConfiguration;

            return CloneWithConfiguration(activeConfiguration, "Debug");
        }

        var projectFile = FindProjectFile(path);
        if (string.IsNullOrWhiteSpace(projectFile))
            return null;

        var projectDirectory = Path.GetDirectoryName(projectFile) ?? string.Empty;
        return new RunConfiguration
        {
            Name = Path.GetFileNameWithoutExtension(projectFile),
            ProjectPath = projectFile,
            WorkingDirectory = projectDirectory,
            Configuration = "Debug",
            OutputType = "Exe",
            IsDefault = true
        };
    }

    private static RunConfiguration CloneWithConfiguration(RunConfiguration config, string configuration)
    {
        var suffix = $" ({configuration})";
        var baseName = config.Name.EndsWith(" (Debug)", StringComparison.OrdinalIgnoreCase)
            ? config.Name[..^8]
            : config.Name.EndsWith(" (Release)", StringComparison.OrdinalIgnoreCase)
                ? config.Name[..^10]
                : config.Name;

        return new RunConfiguration
        {
            Name = baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? baseName : baseName + suffix,
            ProjectPath = config.ProjectPath,
            WorkingDirectory = config.WorkingDirectory,
            Configuration = configuration,
            Framework = config.Framework,
            LaunchProfile = config.LaunchProfile,
            ApplicationUrl = config.ApplicationUrl,
            EnvironmentVariables = new Dictionary<string, string>(config.EnvironmentVariables),
            CommandLineArguments = config.CommandLineArguments,
            OutputType = config.OutputType,
            IsDefault = config.IsDefault
        };
    }

    private void OnRunOutput(object? sender, RunOutputEventArgs e) =>
        Dispatcher.UIThread.Post(() => AppendRunOutput(e.Output));

    private void OnRunCompleted(object? sender, RunCompletedEventArgs e) =>
        Dispatcher.UIThread.Post(() => _viewModel.StatusText = e.Result.Success ? "Run completed" : "Run failed");

    private void StopRunningProcess()
    {

        _runConfigService.Stop();
        _viewModel.StatusText = "Stopped";
    }

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
            Title = "Open Solution or Project",
            AllowMultiple = false,
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
            await LoadWorkspaceDirectoryAsync(dir);
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
    private bool _leftPanelVisible = true;
    private bool _rightPanelVisible = true;
    private bool _bottomPanelVisible = true;
    private bool _activityBarVisible = true;

    // ── Saved dimensions (last non-zero values) ───────────────
    private double _leftPanelWidth = 250;
    private double _rightPanelWidth = 300;
    private double _bottomPanelHeight = 200;

    // ── Zen mode ─────────────────────────────────────────────
    private bool _isZenMode = false;

    // ── Grid / control accessors ──────────────────────────────
    private Grid? GetMainGrid() => this.FindControl<Grid>("MainContentGrid");
    private Grid? GetEditorGrid() => this.FindControl<Grid>("EditorGrid");
    private Border? GetSidePanel() => this.FindControl<Border>("SidePanelBorder");
    private Border? GetAIPanel() => this.FindControl<Border>("AIPanelBorder");
    private GridSplitter? LeftSplitter => this.FindControl<GridSplitter>("LeftPanelSplitter");
    private GridSplitter? RightSplitter => this.FindControl<GridSplitter>("RightPanelSplitter");
    private GridSplitter? BottomSplitter => this.FindControl<GridSplitter>("BottomPanelSplitter");

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
        col2.Width = visible ? new GridLength(5) : new GridLength(0);

        var border = GetSidePanel();
        var splitter = LeftSplitter;
        if (border != null) border.IsVisible = visible;
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
        col4.Width = visible ? new GridLength(5) : new GridLength(0);
        col5.Width = visible ? new GridLength(_rightPanelWidth) : new GridLength(0);

        var border = GetAIPanel();
        var splitter = RightSplitter;
        if (border != null) border.IsVisible = visible;
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
        row2.Height = visible ? new GridLength(5) : new GridLength(0);
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
            if (lw > 50) _leftPanelWidth = lw;
            if (rw > 50) _rightPanelWidth = rw;
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
            _zenSavedLeft = _leftPanelVisible;
            _zenSavedRight = _rightPanelVisible;
            _zenSavedBottom = _bottomPanelVisible;
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

        _copilotCliService.WorkingDirectory = _projectPath ?? Environment.CurrentDirectory;
        var result = await _copilotCliService.ExecuteAsync(cmd);
        AddAIMessage(result.Success ? result.Output : $"❌ {result.Output}", isUser: false);

        this.FindControl<ScrollViewer>("AIChatScrollViewer")?.ScrollToEnd();
    }

    private void AddAIMessage(string text, bool isUser)
    {
        var messages = this.FindControl<StackPanel>("AIChatMessages");
        if (messages == null) return;
        messages.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse(isUser ? "#FF3D3D4D" : "#40FAB387")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4),
            MaxWidth = 280,
            HorizontalAlignment = isUser ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
            Child = new SelectableTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Brushes.White,
                SelectionBrush = new SolidColorBrush(Color.Parse("#60FFC09F")),
                SelectionForegroundBrush = new SolidColorBrush(Color.Parse("#FF1F1A24"))
            }
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Dialog helpers
    // ═══════════════════════════════════════════════════════════
    private async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "")
    {
        var icon = title.Contains("Rename") ? "✏️"
                 : title.Contains("Folder") ? "📁"
                 : title.StartsWith("New ") ? "📄"
                 : "✏️";
        var dialog = new InputDialog(title, prompt, defaultValue, icon);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly
        };
        bool result = false;
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        var msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var btns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
        var yes = new Button { Content = "Yes", Width = 80 };
        var no = new Button { Content = "No", Width = 80 };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => { result = false; dialog.Close(); };
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
    private async void BuildProject_Click(object? sender, RoutedEventArgs e) => await BuildProjectAsync();
    private async void RunProject_Click(object? sender, RoutedEventArgs e) => await RunProjectAsync();
    private async void DebugProject_Click(object? sender, RoutedEventArgs e) => await RunProjectInDebugModeAsync();
    private async void Publish_Click(object? sender, RoutedEventArgs e) => await ShowPublishWindowAsync();
    private async void MsixManager_Click(object? sender, RoutedEventArgs e) => await ShowMsixManagerWindowAsync();
    private void CancelBuild_Click(object? sender, RoutedEventArgs e) { _buildService.CancelBuild(); _publishService.Cancel(); StopRunningProcess(); _viewModel.StatusText = "Build cancelled"; }
    private void RunConfigDropdown_Click(object? sender, RoutedEventArgs e) => _ = ShowRunConfigurationsAsync();
    private void EditConfigurations_Click(object? sender, RoutedEventArgs e) => _ = ShowRunConfigurationsAsync();

    // Tool-window tabs
    private void TerminalTab_Click(object? sender, RoutedEventArgs e) => SwitchToolWindowPanel("terminal");
    private void ProblemsTab_Click(object? sender, RoutedEventArgs e) => SwitchToolWindowPanel("problems");
    private void BuildTab_Click(object? sender, RoutedEventArgs e) => SwitchToolWindowPanel("build");
    private void RunTab_Click(object? sender, RoutedEventArgs e) => SwitchToolWindowPanel("run");
    private void StatusProblems_Click(object? sender, RoutedEventArgs e) => SwitchToolWindowPanel("problems");
    private void NewTerminal_Click(object? sender, RoutedEventArgs e)
    {
        // Open a real external terminal (Windows Terminal or cmd.exe)
        _terminalControl?.OpenExternalTerminal(
            title: "Insait Edit — Terminal",
            command: null);
        _viewModel.StatusText = LocalizationService.Get("Menu.NewTerminal");
    }
    private void ClearTerminal_Click(object? sender, RoutedEventArgs e) => _terminalControl?.ExecuteCommand("cls");
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
            if (res == SaveConfirmationResult.Save) await SaveCurrentFileAsync();
            else if (res == SaveConfirmationResult.Cancel) return;
        }
        _viewModel.CloseTab(tab);
        if (_insaitEditor != null)
        {
            if (_viewModel.ActiveTab != null)
                ShowTabInEditor(_viewModel.ActiveTab);
            else
                _insaitEditor.SetContent(string.Empty, "plaintext");
        }
        UpdateWelcomeScreenVisibility();
        UpdateAxamlPreviewButton();
        UpdateTabButtonStyles();
    }

    // AI Chat
    private void GitHubTui_Click(object? sender, RoutedEventArgs e) { _terminalControl?.OpenGitHubCopilotTerminal(); SwitchToolWindowPanel("terminal"); }
    private void ClearAIChat_Click(object? sender, RoutedEventArgs e)
    {
        var m = this.FindControl<StackPanel>("AIChatMessages");
        if (m == null) return;
        // Keep only the welcome banner (first child)
        while (m.Children.Count > 1) m.Children.RemoveAt(m.Children.Count - 1);
    }
    private void CloseAIPanel_Click(object? sender, RoutedEventArgs e) => ToggleAIPanel();
    private async void AIChatInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; await ExecuteAIChatCommandAsync(); }
    }
    private async void SendAI_Click(object? sender, RoutedEventArgs e) => await ExecuteAIChatCommandAsync();

    // Context menu
    private void ContextMenu_ManageNuGet_Click(object? sender, RoutedEventArgs e) => NuGet_Click(sender, e);
    private void ContextMenu_AddReference_Click(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "Add Reference coming soon...";
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
    private void ContextMenu_Paste_Click(object? sender, RoutedEventArgs e) { }
    private void ContextMenu_RemoveFromSolution_Click(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "Remove from Solution coming soon...";
    private void ContextMenu_UnloadProject_Click(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "Unload Project coming soon...";
    private void ContextMenu_GitCommit_Click(object? sender, RoutedEventArgs e) => _ = OpenGitWindowAsync();
    private void ContextMenu_GitHistory_Click(object? sender, RoutedEventArgs e) => _ = OpenGitWindowAsync();
    private void ContextMenu_GitRevert_Click(object? sender, RoutedEventArgs e) => _ = OpenGitWindowAsync();

    private void ContextMenu_CopyRelativePath_Click(object? sender, RoutedEventArgs e)
    {
        var item = GetSelectedTreeItem();
        if (item == null) return;
        var root = _projectPath ?? "";
        var rel = item.FullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
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
        if (item == null) return;

        // Solution → SolutionPropertiesWindow
        if (item.ItemType == FileTreeItemType.Solution)
        {
            new SolutionPropertiesWindow(item.FullPath).ShowDialog(this);
            return;
        }

        // Determine the project file:
        //   1. Item is itself a project node
        //   2. Walk the tree model's ParentItem chain looking for a Project ancestor
        //   3. Fallback: walk the file-system directory tree upward
        string? projectFile = null;

        if (item.ItemType == FileTreeItemType.Project)
        {
            projectFile = FindProjectFile(item.FullPath);
        }
        else
        {
            // Traverse tree parent chain
            var current = item.ParentItem;
            while (current != null)
            {
                if (current.ItemType == FileTreeItemType.Project)
                {
                    projectFile = FindProjectFile(current.FullPath);
                    break;
                }
                current = current.ParentItem;
            }

            // Filesystem fallback
            if (string.IsNullOrEmpty(projectFile))
            {
                var startDir = item.IsDirectory
                    ? item.FullPath
                    : Path.GetDirectoryName(item.FullPath);
                if (!string.IsNullOrEmpty(startDir))
                    projectFile = FindProjectFileInParents(startDir);
            }
        }

        if (!string.IsNullOrEmpty(projectFile))
        {
            new ProjectPropertiesWindow(projectFile).ShowDialog(this);
            return;
        }

        _viewModel.StatusText = "No project file found for selected item";
    }

    // ═══════════════════════════════════════════════════════════
    //  AXAML Preview
    // ═══════════════════════════════════════════════════════════

    /// <summary>Show or hide the AXAML preview button based on the active tab.</summary>
    internal void UpdateAxamlPreviewButton()
    {
        var btn = this.FindControl<Button>("AxamlPreviewButton");
        if (btn == null) return;

        var tab = _viewModel.ActiveTab;
        var ext = Path.GetExtension(tab?.FilePath ?? string.Empty).ToLowerInvariant();
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

