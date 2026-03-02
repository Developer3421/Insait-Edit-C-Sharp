using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class PublishWindow : Window
{
    private readonly PublishService _publishService;
    private readonly string _projectPath;
    private List<string> _projects = new();

    public PublishProfile? Result { get; private set; }

    public PublishWindow() : this(string.Empty)
    {
    }

    public PublishWindow(string projectPath)
    {
        InitializeComponent();
        
        _projectPath = projectPath;
        _publishService = new PublishService();
        
        SetupEventHandlers();
        LoadData();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("Publish.Title");
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

        var cancelButton = this.FindControl<Button>("CancelButton");
        if (cancelButton != null)
        {
            cancelButton.Click += (s, e) => Close(null);
        }

        var publishButton = this.FindControl<Button>("PublishButton");
        if (publishButton != null)
        {
            publishButton.Click += Publish_Click;
        }

        var browseOutputButton = this.FindControl<Button>("BrowseOutputButton");
        if (browseOutputButton != null)
        {
            browseOutputButton.Click += BrowseOutput_Click;
        }

        // Quick preset buttons
        var presetWinX64 = this.FindControl<Button>("PresetWinX64Button");
        var presetWinArm64 = this.FindControl<Button>("PresetWinArm64Button");
        var presetLinuxX64 = this.FindControl<Button>("PresetLinuxX64Button");
        var presetMacX64 = this.FindControl<Button>("PresetMacX64Button");
        var presetMacArm64 = this.FindControl<Button>("PresetMacArm64Button");
        var presetPortable = this.FindControl<Button>("PresetPortableButton");

        if (presetWinX64 != null) presetWinX64.Click += (s, e) => ApplyPreset("win-x64");
        if (presetWinArm64 != null) presetWinArm64.Click += (s, e) => ApplyPreset("win-arm64");
        if (presetLinuxX64 != null) presetLinuxX64.Click += (s, e) => ApplyPreset("linux-x64");
        if (presetMacX64 != null) presetMacX64.Click += (s, e) => ApplyPreset("osx-x64");
        if (presetMacArm64 != null) presetMacArm64.Click += (s, e) => ApplyPreset("osx-arm64");
        if (presetPortable != null) presetPortable.Click += (s, e) => ApplyPreset("");

        // Runtime selection changes
        var selfContainedRadio = this.FindControl<RadioButton>("SelfContainedRadio");
        var frameworkDependentRadio = this.FindControl<RadioButton>("FrameworkDependentRadio");
        
        if (selfContainedRadio != null) 
        {
            selfContainedRadio.IsCheckedChanged += (s, e) => UpdateRuntimeOptions();
        }
        if (frameworkDependentRadio != null) 
        {
            frameworkDependentRadio.IsCheckedChanged += (s, e) => UpdateRuntimeOptions();
        }

        // Project selection changed
        var projectCombo = this.FindControl<ComboBox>("ProjectComboBox");
        if (projectCombo != null)
        {
            projectCombo.SelectionChanged += ProjectCombo_SelectionChanged;
        }
    }

    private async void LoadData()
    {
        await LoadProjects();
        LoadRuntimeIdentifiers();
        SetDefaultOutputPath();
    }

    private async Task LoadProjects()
    {
        var projectCombo = this.FindControl<ComboBox>("ProjectComboBox");
        if (projectCombo == null) return;

        _projects.Clear();

        if (!string.IsNullOrEmpty(_projectPath))
        {
            var ext = Path.GetExtension(_projectPath).ToLowerInvariant();
            if (ext == ".sln" || ext == ".slnx")
            {
                var solutionService = new SolutionService();
                _projects = await solutionService.GetSolutionProjectsAsync(_projectPath);
            }
            else if (ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj")
            {
                _projects.Add(_projectPath);
            }
            else if (Directory.Exists(_projectPath))
            {
                // Find solution first
                var slnxFiles = Directory.GetFiles(_projectPath, "*.slnx", SearchOption.TopDirectoryOnly);
                var slnFiles = Directory.GetFiles(_projectPath, "*.sln", SearchOption.TopDirectoryOnly);

                if (slnxFiles.Length > 0)
                {
                    var solutionService = new SolutionService();
                    _projects = await solutionService.GetSolutionProjectsAsync(slnxFiles[0]);
                }
                else if (slnFiles.Length > 0)
                {
                    var solutionService = new SolutionService();
                    _projects = await solutionService.GetSolutionProjectsAsync(slnFiles[0]);
                }
                else
                {
                    var csprojFiles = Directory.GetFiles(_projectPath, "*.csproj", SearchOption.AllDirectories);
                    _projects.AddRange(csprojFiles);
                }
            }
        }

        var projectNames = _projects.Select(p => Path.GetFileName(p)).ToList();
        projectCombo.ItemsSource = projectNames;

        if (projectNames.Count > 0)
        {
            projectCombo.SelectedIndex = 0;
        }
    }

    private void LoadRuntimeIdentifiers()
    {
        var runtimeCombo = this.FindControl<ComboBox>("RuntimeComboBox");
        if (runtimeCombo == null) return;

        var rids = PublishService.GetAvailableRuntimeIdentifiers();
        var items = new List<string> { "(Portable - Any OS)" };
        items.AddRange(rids.Where(r => r.IsCommon && !string.IsNullOrEmpty(r.Rid)).Select(r => $"{r.Rid} - {r.DisplayName}"));
        items.Add("---");
        items.AddRange(rids.Where(r => !r.IsCommon && !string.IsNullOrEmpty(r.Rid)).Select(r => $"{r.Rid} - {r.DisplayName}"));

        runtimeCombo.ItemsSource = items;
        runtimeCombo.SelectedIndex = 0;
    }

    private async void ProjectCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        var selectedIndex = (sender as ComboBox)?.SelectedIndex ?? -1;
        if (selectedIndex < 0 || selectedIndex >= _projects.Count) return;

        var selectedProject = _projects[selectedIndex];
        await LoadProjectFrameworks(selectedProject);
        await LoadPublishProfiles(selectedProject);
        UpdateOutputPath(selectedProject);
    }

    private async Task LoadProjectFrameworks(string projectPath)
    {
        var frameworkCombo = this.FindControl<ComboBox>("FrameworkComboBox");
        if (frameworkCombo == null) return;

        var frameworks = await _publishService.GetProjectFrameworksAsync(projectPath);
        frameworkCombo.ItemsSource = frameworks;

        if (frameworks.Count > 0)
        {
            frameworkCombo.SelectedIndex = 0;
        }
    }

    private async Task LoadPublishProfiles(string projectPath)
    {
        var profileCombo = this.FindControl<ComboBox>("PublishProfileComboBox");
        if (profileCombo == null) return;

        var profiles = await _publishService.GetPublishProfilesAsync(projectPath);
        var items = new List<string> { "(None - Use settings below)" };
        items.AddRange(profiles);

        profileCombo.ItemsSource = items;
        profileCombo.SelectedIndex = 0;
    }

    private void UpdateOutputPath(string projectPath)
    {
        var outputBox = this.FindControl<TextBox>("OutputPathBox");
        if (outputBox == null) return;

        var projectDir = Path.GetDirectoryName(projectPath) ?? "";
        outputBox.Text = Path.Combine(projectDir, "bin", "publish");
    }

    private void SetDefaultOutputPath()
    {
        var outputBox = this.FindControl<TextBox>("OutputPathBox");
        if (outputBox == null) return;

        if (_projects.Count > 0)
        {
            UpdateOutputPath(_projects[0]);
        }
        else
        {
            outputBox.Text = Path.Combine(_projectPath, "bin", "publish");
        }
    }

    private void UpdateRuntimeOptions()
    {
        var selfContainedRadio = this.FindControl<RadioButton>("SelfContainedRadio");
        var runtimeCombo = this.FindControl<ComboBox>("RuntimeComboBox");
        var singleFileCheck = this.FindControl<CheckBox>("SingleFileCheck");

        var isSelfContained = selfContainedRadio?.IsChecked ?? false;

        // For self-contained, enable single-file and require runtime selection
        if (singleFileCheck != null)
        {
            singleFileCheck.IsEnabled = isSelfContained;
            if (!isSelfContained)
            {
                singleFileCheck.IsChecked = false;
            }
        }
    }

    private void ApplyPreset(string runtimeIdentifier)
    {
        var runtimeCombo = this.FindControl<ComboBox>("RuntimeComboBox");
        var selfContainedRadio = this.FindControl<RadioButton>("SelfContainedRadio");
        var frameworkDependentRadio = this.FindControl<RadioButton>("FrameworkDependentRadio");
        var singleFileCheck = this.FindControl<CheckBox>("SingleFileCheck");
        var readyToRunCheck = this.FindControl<CheckBox>("ReadyToRunCheck");
        var trimCheck = this.FindControl<CheckBox>("TrimCheck");

        if (string.IsNullOrEmpty(runtimeIdentifier))
        {
            // Portable preset
            if (frameworkDependentRadio != null) frameworkDependentRadio.IsChecked = true;
            if (runtimeCombo != null) runtimeCombo.SelectedIndex = 0;
            if (singleFileCheck != null) singleFileCheck.IsChecked = false;
            if (readyToRunCheck != null) readyToRunCheck.IsChecked = false;
            if (trimCheck != null) trimCheck.IsChecked = false;
        }
        else
        {
            // Self-contained preset
            if (selfContainedRadio != null) selfContainedRadio.IsChecked = true;
            
            // Find and select runtime
            if (runtimeCombo?.ItemsSource != null)
            {
                var items = runtimeCombo.ItemsSource as List<string>;
                if (items != null)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].StartsWith(runtimeIdentifier))
                        {
                            runtimeCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }

            if (singleFileCheck != null) singleFileCheck.IsChecked = true;
            if (readyToRunCheck != null) readyToRunCheck.IsChecked = true;
            if (trimCheck != null) trimCheck.IsChecked = false; // Trimming can break things
        }

        // Update output path with runtime
        var projectCombo = this.FindControl<ComboBox>("ProjectComboBox");
        var outputBox = this.FindControl<TextBox>("OutputPathBox");
        
        if (projectCombo?.SelectedIndex >= 0 && projectCombo.SelectedIndex < _projects.Count && outputBox != null)
        {
            var projectPath = _projects[projectCombo.SelectedIndex];
            var projectDir = Path.GetDirectoryName(projectPath) ?? "";
            
            if (string.IsNullOrEmpty(runtimeIdentifier))
            {
                outputBox.Text = Path.Combine(projectDir, "bin", "publish");
            }
            else
            {
                outputBox.Text = Path.Combine(projectDir, "bin", "publish", runtimeIdentifier);
            }
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (dialog.Count > 0)
        {
            var outputBox = this.FindControl<TextBox>("OutputPathBox");
            if (outputBox != null)
            {
                outputBox.Text = dialog[0].Path.LocalPath;
            }
        }
    }

    private void Publish_Click(object? sender, RoutedEventArgs e)
    {
        var profile = BuildPublishProfile();
        if (profile != null)
        {
            Result = profile;
            Close(profile);
        }
    }

    private PublishProfile? BuildPublishProfile()
    {
        var projectCombo = this.FindControl<ComboBox>("ProjectComboBox");
        var configCombo = this.FindControl<ComboBox>("ConfigurationComboBox");
        var runtimeCombo = this.FindControl<ComboBox>("RuntimeComboBox");
        var frameworkCombo = this.FindControl<ComboBox>("FrameworkComboBox");
        var outputBox = this.FindControl<TextBox>("OutputPathBox");
        var selfContainedRadio = this.FindControl<RadioButton>("SelfContainedRadio");
        var singleFileCheck = this.FindControl<CheckBox>("SingleFileCheck");
        var readyToRunCheck = this.FindControl<CheckBox>("ReadyToRunCheck");
        var trimCheck = this.FindControl<CheckBox>("TrimCheck");
        var compressionCheck = this.FindControl<CheckBox>("CompressionCheck");
        var nativeLibsCheck = this.FindControl<CheckBox>("NativeLibrariesCheck");
        var profileCombo = this.FindControl<ComboBox>("PublishProfileComboBox");

        if (projectCombo == null || projectCombo.SelectedIndex < 0 || projectCombo.SelectedIndex >= _projects.Count)
        {
            return null;
        }

        var projectPath = _projects[projectCombo.SelectedIndex];

        // Parse runtime identifier
        string? runtimeIdentifier = null;
        if (runtimeCombo?.SelectedItem is string selectedRuntime)
        {
            if (!selectedRuntime.StartsWith("(") && !selectedRuntime.StartsWith("-"))
            {
                runtimeIdentifier = selectedRuntime.Split(' ')[0];
            }
        }

        // Get configuration
        var configuration = "Release";
        if (configCombo?.SelectedItem is ComboBoxItem configItem)
        {
            configuration = configItem.Content?.ToString() ?? "Release";
        }

        // Get framework
        string? framework = frameworkCombo?.SelectedItem as string;

        // Get publish profile
        string? publishProfile = null;
        if (profileCombo?.SelectedItem is string profile && !profile.StartsWith("("))
        {
            publishProfile = profile;
        }

        return new PublishProfile
        {
            Name = $"Publish - {Path.GetFileNameWithoutExtension(projectPath)}",
            ProjectPath = projectPath,
            Configuration = configuration,
            RuntimeIdentifier = runtimeIdentifier,
            Framework = framework,
            OutputPath = outputBox?.Text ?? Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "bin", "publish"),
            SelfContained = selfContainedRadio?.IsChecked ?? false,
            SingleFile = singleFileCheck?.IsChecked ?? false,
            ReadyToRun = readyToRunCheck?.IsChecked ?? false,
            TrimUnusedAssemblies = trimCheck?.IsChecked ?? false,
            EnableCompressionInSingleFile = compressionCheck?.IsChecked ?? false,
            IncludeNativeLibrariesForSelfExtract = nativeLibsCheck?.IsChecked ?? false,
            PublishProfileName = publishProfile
        };
    }
}

