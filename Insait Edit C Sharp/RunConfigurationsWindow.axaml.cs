using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class RunConfigurationsWindow : Window
{
    private readonly RunConfigurationService _runConfigService;
    private readonly string _projectPath;
    private readonly ObservableCollection<RunConfiguration> _configurations = new();
    private readonly ObservableCollection<CompoundRunConfiguration> _compoundConfigurations = new();
    private RunConfiguration? _selectedConfiguration;
    private CompoundRunConfiguration? _selectedCompound;
    private bool _isDirty;

    public RunConfiguration? SelectedConfiguration => _selectedConfiguration;
    public CompoundRunConfiguration? SelectedCompound => _selectedCompound;

    public RunConfigurationsWindow() : this(string.Empty)
    {
    }

    public RunConfigurationsWindow(string projectPath)
    {
        InitializeComponent();
        
        _projectPath = projectPath;
        _runConfigService = new RunConfigurationService();
        
        SetupEventHandlers();
        LoadConfigurations();
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("RunConfig.Title");
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null) searchBox.Watermark = L("RunConfig.SearchPlaceholder");
        var addBtn = this.FindControl<Button>("AddConfigButton");
        if (addBtn != null) ToolTip.SetTip(addBtn, L("RunConfig.Add"));
        var removeBtn = this.FindControl<Button>("RemoveConfigButton");
        if (removeBtn != null) ToolTip.SetTip(removeBtn, L("RunConfig.Remove"));
        var dupBtn = this.FindControl<Button>("DuplicateConfigButton");
        if (dupBtn != null) ToolTip.SetTip(dupBtn, L("RunConfig.Duplicate"));
        var runBtn = this.FindControl<Button>("RunButton");
        if (runBtn != null) runBtn.Content = L("RunConfig.Run");
        var debugBtn = this.FindControl<Button>("DebugButton");
        if (debugBtn != null) debugBtn.Content = L("RunConfig.Debug");
        var applyBtn = this.FindControl<Button>("ApplyButton");
        if (applyBtn != null) applyBtn.Content = L("RunConfig.Apply");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (cancelBtn != null) cancelBtn.Content = L("RunConfig.Cancel");
        var okBtn = this.FindControl<Button>("OkButton");
        if (okBtn != null) okBtn.Content = L("RunConfig.OK");
        var manageBtn = this.FindControl<Button>("ManageCompoundButton");
        if (manageBtn != null) manageBtn.Content = L("RunConfig.ManageCompound");
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void SetupEventHandlers()
    {
        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += TitleBar_PointerPressed;
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
        {
            closeButton.Click += (s, e) => Close(null);
        }

        var addConfigButton = this.FindControl<Button>("AddConfigButton");
        if (addConfigButton != null)
        {
            addConfigButton.Click += AddConfiguration_Click;
        }

        var removeConfigButton = this.FindControl<Button>("RemoveConfigButton");
        if (removeConfigButton != null)
        {
            removeConfigButton.Click += RemoveConfiguration_Click;
        }

        var duplicateConfigButton = this.FindControl<Button>("DuplicateConfigButton");
        if (duplicateConfigButton != null)
        {
            duplicateConfigButton.Click += DuplicateConfiguration_Click;
        }

        var browseWorkingDirButton = this.FindControl<Button>("BrowseWorkingDirButton");
        if (browseWorkingDirButton != null)
        {
            browseWorkingDirButton.Click += BrowseWorkingDir_Click;
        }

        var addEnvVarButton = this.FindControl<Button>("AddEnvVarButton");
        if (addEnvVarButton != null)
        {
            addEnvVarButton.Click += AddEnvironmentVariable_Click;
        }

        var runButton = this.FindControl<Button>("RunButton");
        if (runButton != null)
        {
            runButton.Click += Run_Click;
        }

        var debugButton = this.FindControl<Button>("DebugButton");
        if (debugButton != null)
        {
            debugButton.Click += Debug_Click;
        }

        var applyButton = this.FindControl<Button>("ApplyButton");
        if (applyButton != null)
        {
            applyButton.Click += Apply_Click;
        }

        var cancelButton = this.FindControl<Button>("CancelButton");
        if (cancelButton != null)
        {
            cancelButton.Click += (s, e) => Close(null);
        }

        var okButton = this.FindControl<Button>("OkButton");
        if (okButton != null)
        {
            okButton.Click += Ok_Click;
        }

        // Compound configs
        var manageCompoundButton = this.FindControl<Button>("ManageCompoundButton");
        if (manageCompoundButton != null)
        {
            manageCompoundButton.Click += ManageCompound_Click;
        }

        // Track changes
        SetupChangeTracking();
    }

    private void SetupChangeTracking()
    {
        var nameBox = this.FindControl<TextBox>("ConfigNameBox");
        var argsBox = this.FindControl<TextBox>("ArgumentsBox");
        var workingDirBox = this.FindControl<TextBox>("WorkingDirBox");

        if (nameBox != null) nameBox.TextChanged += (s, e) => _isDirty = true;
        if (argsBox != null) argsBox.TextChanged += (s, e) => _isDirty = true;
        if (workingDirBox != null) workingDirBox.TextChanged += (s, e) => _isDirty = true;
    }

    private async void LoadConfigurations()
    {
        await _runConfigService.LoadConfigurationsAsync(_projectPath);
        
        _configurations.Clear();
        foreach (var config in _runConfigService.Configurations)
        {
            _configurations.Add(config);
        }

        var listBox = this.FindControl<ListBox>("ConfigurationsList");
        if (listBox != null)
        {
            listBox.ItemsSource = _configurations;
            
            if (_configurations.Count > 0)
            {
                listBox.SelectedIndex = 0;
            }
        }

        // Bind compound configurations
        _compoundConfigurations.Clear();
        foreach (var c in _runConfigService.CompoundConfigurations)
            _compoundConfigurations.Add(c);

        var compoundList = this.FindControl<ListBox>("CompoundConfigsList");
        if (compoundList != null)
            compoundList.ItemsSource = _compoundConfigurations;

        // Also load projects into combo box
        await LoadProjectsComboBox();
    }

    /// <summary>
    /// Compound list selection — deselect single list and update footer buttons label
    /// </summary>
    private void CompoundConfigsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is CompoundRunConfiguration compound)
        {
            _selectedCompound = compound;

            // Deselect single list
            var singleList = this.FindControl<ListBox>("ConfigurationsList");
            if (singleList != null) singleList.SelectedItem = null;

            _selectedConfiguration = null;

            // Update Run button label
            var runBtn = this.FindControl<Button>("RunButton");
            if (runBtn != null) runBtn.Content = "▶▶ Run Compound";
        }
    }

    /// <summary>
    /// Open the Compound Run Configuration window
    /// </summary>
    private async void ManageCompound_Click(object? sender, RoutedEventArgs e)
    {
        var window = new CompoundRunWindow(_runConfigService);
        var result = await window.ShowDialog<CompoundRunConfiguration?>(this);

        // Refresh compound list
        _compoundConfigurations.Clear();
        foreach (var c in _runConfigService.CompoundConfigurations)
            _compoundConfigurations.Add(c);

        // If user clicked "Run All", close this window and signal caller
        if (result != null && window.SelectedToRun != null)
        {
            _selectedCompound = window.SelectedToRun;
            Close(window.SelectedToRun);  // pass compound back to MainWindow
        }
    }

    private async Task LoadProjectsComboBox()
    {
        var projectCombo = this.FindControl<ComboBox>("ProjectComboBox");
        if (projectCombo == null) return;

        var projects = new List<string>();
        
        if (!string.IsNullOrEmpty(_projectPath))
        {
            var ext = Path.GetExtension(_projectPath).ToLowerInvariant();
            if (ext == ".sln" || ext == ".slnx")
            {
                var solutionService = new SolutionService();
                projects = await solutionService.GetSolutionProjectsAsync(_projectPath);
            }
            else if (ext == ".csproj")
            {
                projects.Add(_projectPath);
            }
            else if (Directory.Exists(_projectPath))
            {
                var csprojFiles = Directory.GetFiles(_projectPath, "*.csproj", SearchOption.AllDirectories);
                projects.AddRange(csprojFiles);
            }
        }

        projectCombo.ItemsSource = projects.Select(p => Path.GetFileName(p)).ToList();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void ConfigurationsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is RunConfiguration config)
        {
            // Deselect compound list
            var compoundList = this.FindControl<ListBox>("CompoundConfigsList");
            if (compoundList != null) compoundList.SelectedItem = null;
            _selectedCompound = null;

            // Reset Run button label
            var runBtn = this.FindControl<Button>("RunButton");
            if (runBtn != null) runBtn.Content = "▶ Run";

            // Save current changes first
            if (_isDirty && _selectedConfiguration != null)
            {
                SaveCurrentConfiguration();
            }

            _selectedConfiguration = config;
            LoadConfigurationDetails(config);
            _isDirty = false;
        }
    }

    private void LoadConfigurationDetails(RunConfiguration config)
    {
        var nameBox = this.FindControl<TextBox>("ConfigNameBox");
        var argsBox = this.FindControl<TextBox>("ArgumentsBox");
        var workingDirBox = this.FindControl<TextBox>("WorkingDirBox");
        var buildConfigCombo = this.FindControl<ComboBox>("BuildConfigComboBox");
        var frameworkCombo = this.FindControl<ComboBox>("FrameworkComboBox");
        var launchProfileCombo = this.FindControl<ComboBox>("LaunchProfileComboBox");

        if (nameBox != null) nameBox.Text = config.Name;
        if (argsBox != null) argsBox.Text = config.CommandLineArguments;
        if (workingDirBox != null) workingDirBox.Text = config.WorkingDirectory;
        
        if (buildConfigCombo != null)
        {
            buildConfigCombo.SelectedIndex = config.Configuration == "Release" ? 1 : 0;
        }

        // Load frameworks
        if (frameworkCombo != null && !string.IsNullOrEmpty(config.ProjectPath))
        {
            LoadFrameworks(config.ProjectPath, frameworkCombo, config.Framework);
        }

        // Load launch profiles
        if (launchProfileCombo != null && !string.IsNullOrEmpty(config.ProjectPath))
        {
            LoadLaunchProfiles(config.ProjectPath, launchProfileCombo, config.LaunchProfile);
        }
    }

    private async void LoadFrameworks(string projectPath, ComboBox comboBox, string? selectedFramework)
    {
        var publishService = new PublishService();
        var frameworks = await publishService.GetProjectFrameworksAsync(projectPath);
        
        comboBox.ItemsSource = frameworks;
        
        if (!string.IsNullOrEmpty(selectedFramework) && frameworks.Contains(selectedFramework))
        {
            comboBox.SelectedItem = selectedFramework;
        }
        else if (frameworks.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void LoadLaunchProfiles(string projectPath, ComboBox comboBox, string? selectedProfile)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir)) return;

        var launchSettingsPath = Path.Combine(projectDir, "Properties", "launchSettings.json");
        var profiles = new List<string> { "(None)" };

        if (File.Exists(launchSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(launchSettingsPath);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("profiles", out var profilesElement))
                {
                    foreach (var profile in profilesElement.EnumerateObject())
                    {
                        profiles.Add(profile.Name);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        comboBox.ItemsSource = profiles;
        
        if (!string.IsNullOrEmpty(selectedProfile) && profiles.Contains(selectedProfile))
        {
            comboBox.SelectedItem = selectedProfile;
        }
        else
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void SaveCurrentConfiguration()
    {
        if (_selectedConfiguration == null) return;

        var nameBox = this.FindControl<TextBox>("ConfigNameBox");
        var argsBox = this.FindControl<TextBox>("ArgumentsBox");
        var workingDirBox = this.FindControl<TextBox>("WorkingDirBox");
        var buildConfigCombo = this.FindControl<ComboBox>("BuildConfigComboBox");
        var frameworkCombo = this.FindControl<ComboBox>("FrameworkComboBox");
        var launchProfileCombo = this.FindControl<ComboBox>("LaunchProfileComboBox");

        if (nameBox != null) _selectedConfiguration.Name = nameBox.Text ?? "";
        if (argsBox != null) _selectedConfiguration.CommandLineArguments = argsBox.Text ?? "";
        if (workingDirBox != null) _selectedConfiguration.WorkingDirectory = workingDirBox.Text ?? "";
        
        if (buildConfigCombo?.SelectedItem is ComboBoxItem configItem)
        {
            _selectedConfiguration.Configuration = configItem.Content?.ToString() ?? "Debug";
        }

        if (frameworkCombo?.SelectedItem is string framework)
        {
            _selectedConfiguration.Framework = framework;
        }

        if (launchProfileCombo?.SelectedItem is string profile && profile != "(None)")
        {
            _selectedConfiguration.LaunchProfile = profile;
        }
        else
        {
            _selectedConfiguration.LaunchProfile = null;
        }
    }

    private void AddConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        var newConfig = new RunConfiguration
        {
            Name = "New Configuration",
            ProjectPath = _configurations.FirstOrDefault()?.ProjectPath ?? "",
            WorkingDirectory = Path.GetDirectoryName(_projectPath) ?? "",
            Configuration = "Debug"
        };

        _configurations.Add(newConfig);
        _runConfigService.AddConfiguration(newConfig);

        var listBox = this.FindControl<ListBox>("ConfigurationsList");
        if (listBox != null)
        {
            listBox.SelectedItem = newConfig;
        }
    }

    private void RemoveConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedConfiguration == null) return;

        _configurations.Remove(_selectedConfiguration);
        _runConfigService.RemoveConfiguration(_selectedConfiguration);
        _selectedConfiguration = null;

        var listBox = this.FindControl<ListBox>("ConfigurationsList");
        if (listBox != null && _configurations.Count > 0)
        {
            listBox.SelectedIndex = 0;
        }
    }

    private void DuplicateConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedConfiguration == null) return;

        var newConfig = new RunConfiguration
        {
            Name = _selectedConfiguration.Name + " (Copy)",
            ProjectPath = _selectedConfiguration.ProjectPath,
            WorkingDirectory = _selectedConfiguration.WorkingDirectory,
            Configuration = _selectedConfiguration.Configuration,
            Framework = _selectedConfiguration.Framework,
            LaunchProfile = _selectedConfiguration.LaunchProfile,
            CommandLineArguments = _selectedConfiguration.CommandLineArguments,
            EnvironmentVariables = new Dictionary<string, string>(_selectedConfiguration.EnvironmentVariables)
        };

        _configurations.Add(newConfig);
        _runConfigService.AddConfiguration(newConfig);

        var listBox = this.FindControl<ListBox>("ConfigurationsList");
        if (listBox != null)
        {
            listBox.SelectedItem = newConfig;
        }
    }

    private async void BrowseWorkingDir_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Working Directory",
            AllowMultiple = false
        });

        if (dialog.Count > 0)
        {
            var workingDirBox = this.FindControl<TextBox>("WorkingDirBox");
            if (workingDirBox != null)
            {
                workingDirBox.Text = dialog[0].Path.LocalPath;
            }
        }
    }

    private void AddEnvironmentVariable_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedConfiguration == null) return;

        _selectedConfiguration.EnvironmentVariables["NEW_VAR"] = "";
        _isDirty = true;
        
        // Refresh the environment variables display
        LoadConfigurationDetails(_selectedConfiguration);
    }

    private async void Run_Click(object? sender, RoutedEventArgs e)
    {
        // If a compound is selected, close and signal compound run
        if (_selectedCompound != null)
        {
            Close(_selectedCompound);
            return;
        }

        SaveCurrentConfiguration();

        if (_selectedConfiguration != null)
        {
            _runConfigService.SetActiveConfiguration(_selectedConfiguration);
            Close(_selectedConfiguration);
        }
    }

    private async void Debug_Click(object? sender, RoutedEventArgs e)
    {
        SaveCurrentConfiguration();

        if (_selectedConfiguration != null)
        {
            _runConfigService.SetActiveConfiguration(_selectedConfiguration);
            // Mark for debugging
            Close(_selectedConfiguration);
        }
    }

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        SaveCurrentConfiguration();
        _isDirty = false;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        SaveCurrentConfiguration();
        Close(_selectedConfiguration);
    }
}

