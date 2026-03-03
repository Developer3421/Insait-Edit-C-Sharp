using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class NewProjectWindow : Window
{
    private string _selectedTemplate = "console";
    private readonly string _defaultLocation;
    private readonly string? _currentSolutionPath;

    public string? CreatedProjectPath { get; private set; }

    /// <summary>
    /// Path of the solution file (.sln or .slnx) that was created or used
    /// </summary>
    public string? CreatedSolutionPath { get; private set; }

    public NewProjectWindow() : this(null)
    {
    }

    public NewProjectWindow(string? currentSolutionPath)
    {
        InitializeComponent();
        
        _currentSolutionPath = currentSolutionPath;
        
        _defaultLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos");
        
        var locationBox = this.FindControl<TextBox>("LocationBox");
        if (locationBox != null)
        {
            // If we have a current solution, use its directory as default location
            if (!string.IsNullOrEmpty(_currentSolutionPath))
            {
                var slnDir = Path.GetDirectoryName(_currentSolutionPath);
                if (!string.IsNullOrEmpty(slnDir))
                {
                    locationBox.Text = slnDir;
                }
                else
                {
                    locationBox.Text = _defaultLocation;
                }
            }
            else
            {
                locationBox.Text = _defaultLocation;
            }
        }

        // Attach format change handlers
        var slnxFormatRadio = this.FindControl<RadioButton>("SlnxFormat");
        var slnFormatRadio = this.FindControl<RadioButton>("SlnFormat");
        if (slnxFormatRadio != null)
            slnxFormatRadio.IsCheckedChanged += (_, _) => UpdateProjectPathPreview();
        if (slnFormatRadio != null)
            slnFormatRadio.IsCheckedChanged += (_, _) => UpdateProjectPathPreview();
        
        UpdateProjectPathPreview();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("NewProject.Title");
        var titleBar = this.FindControl<TextBlock>("TitleBarText");
        if (titleBar != null) titleBar.Text = L("NewProject.Title");
        var selectTemplate = this.FindControl<TextBlock>("SelectTemplateText");
        if (selectTemplate != null) selectTemplate.Text = L("NewProject.SelectTemplate");
        var configure = this.FindControl<TextBlock>("ConfigureText");
        if (configure != null) configure.Text = L("NewProject.Configure");
        var projLabel = this.FindControl<TextBlock>("ProjectNameLabel");
        if (projLabel != null) projLabel.Text = L("NewProject.ProjectName");
        var locLabel = this.FindControl<TextBlock>("LocationLabel");
        if (locLabel != null) locLabel.Text = L("NewProject.Location");
        var slnLabel = this.FindControl<TextBlock>("SolutionNameLabel");
        if (slnLabel != null) slnLabel.Text = L("NewProject.SolutionName");
        var fmtLabel = this.FindControl<TextBlock>("SolutionFormatLabel");
        if (fmtLabel != null) fmtLabel.Text = L("NewProject.SolutionFormat");
        var sameDir = this.FindControl<TextBlock>("PlaceSameDirText");
        if (sameDir != null) sameDir.Text = L("NewProject.PlaceSameDir");
        var gitRepo = this.FindControl<TextBlock>("CreateGitRepoText");
        if (gitRepo != null) gitRepo.Text = L("NewProject.CreateGitRepo");
        var browseBtn = this.FindControl<Button>("BrowseButton");
        if (browseBtn != null) browseBtn.Content = L("Common.Browse");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (cancelBtn != null) cancelBtn.Content = L("NewProject.Cancel");
        var createBtn = this.FindControl<Button>("CreateButton");
        if (createBtn != null) createBtn.Content = L("NewProject.Create");
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Template_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string template)
        {
            // nanoFramework opens its own dedicated window
            if (template == "nano")
            {
                var espWindow = new Insait_Edit_C_Sharp.Esp.Windows.NewNanoProjectWindow(_currentSolutionPath);
                var result = await espWindow.ShowDialog<string?>(this);
                if (!string.IsNullOrEmpty(result))
                {
                    Close(result);
                }
                return;
            }

            _selectedTemplate = template;
            
            // Update visual selection
            var templates = new[] { "ConsoleTemplate", "ClassLibTemplate", "AvaloniaTemplate",
                                    "NanoTemplate", "FSharpConsoleTemplate", "FSharpEmptyTemplate",
                                    "WinFormsTemplate", "CSharpEmptyTemplate" };
            foreach (var name in templates)
            {
                var btn = this.FindControl<Button>(name);
                if (btn != null)
                {
                    btn.Classes.Remove("selected");
                }
            }
            button.Classes.Add("selected");
        }
    }

    private void SolutionFormat_Changed(object? sender, RoutedEventArgs e)
    {
        UpdateProjectPathPreview();
    }

    private void ProjectName_Changed(object? sender, TextChangedEventArgs e)
    {
        var projectNameBox = this.FindControl<TextBox>("ProjectNameBox");
        var solutionNameBox = this.FindControl<TextBox>("SolutionNameBox");
        
        if (projectNameBox != null && solutionNameBox != null)
        {
            solutionNameBox.Text = projectNameBox.Text;
        }
        
        UpdateProjectPathPreview();
    }

    private async void BrowseLocation_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Location",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var locationBox = this.FindControl<TextBox>("LocationBox");
            if (locationBox != null)
            {
                locationBox.Text = folders[0].Path.LocalPath;
            }
            UpdateProjectPathPreview();
        }
    }

    private void UpdateProjectPathPreview()
    {
        var projectNameBox = this.FindControl<TextBox>("ProjectNameBox");
        var locationBox = this.FindControl<TextBox>("LocationBox");
        var previewText = this.FindControl<TextBlock>("ProjectPathPreview");
        var sameDir = this.FindControl<CheckBox>("CreateSolutionDir");
        var slnxFormat = this.FindControl<RadioButton>("SlnxFormat");
        
        if (projectNameBox != null && locationBox != null && previewText != null)
        {
            var projectName = projectNameBox.Text ?? "MyProject";
            var location = locationBox.Text ?? _defaultLocation;
            var useSlnx = slnxFormat?.IsChecked != false; // default to slnx
            var slnExt = useSlnx ? ".slnx" : ".sln";
            var solutionNameBox = this.FindControl<TextBox>("SolutionNameBox");
            var solutionName = solutionNameBox?.Text ?? projectName;

            string slnDir;
            if (sameDir?.IsChecked == true)
            {
                // Project and solution share the same folder: location\projectName
                slnDir = Path.Combine(location, projectName);
            }
            else
            {
                // Solution folder contains project subfolder: location\solutionName\projectName
                slnDir = Path.Combine(location, solutionName);
            }

            var solutionPath = Path.Combine(slnDir, $"{solutionName}{slnExt}");
            previewText.Text = $"Solution: {solutionPath}";
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        var projectNameBox = this.FindControl<TextBox>("ProjectNameBox");
        var locationBox = this.FindControl<TextBox>("LocationBox");
        var solutionNameBox = this.FindControl<TextBox>("SolutionNameBox");
        var sameDir = this.FindControl<CheckBox>("CreateSolutionDir");
        var createGit = this.FindControl<CheckBox>("CreateGitRepo");
        var slnxFormatRadio = this.FindControl<RadioButton>("SlnxFormat");

        if (projectNameBox == null || locationBox == null) return;

        var projectName = projectNameBox.Text?.Trim() ?? "MyProject";
        var location = locationBox.Text?.Trim() ?? _defaultLocation;
        var solutionName = solutionNameBox?.Text?.Trim() ?? projectName;
        var useSlnx = slnxFormatRadio?.IsChecked != false; // default to slnx

        if (string.IsNullOrWhiteSpace(projectName))
        {
            // Show error
            return;
        }

        // Create project directory
        string projectDir;
        string slnDir;
        
        if (sameDir?.IsChecked == true)
        {
            projectDir = Path.Combine(location, projectName);
            slnDir = projectDir;
        }
        else
        {
            slnDir = Path.Combine(location, solutionName);
            projectDir = Path.Combine(slnDir, projectName);
        }

        try
        {
            Directory.CreateDirectory(projectDir);

            // Run dotnet new
            var templateName = _selectedTemplate switch
            {
                "console"        => "console",
                "classlib"       => "classlib",
                "avalonia"       => "avalonia.app",
                "fsharp-console" => "console",
                "fsharp-empty"   => "classlib",
                "winforms"       => "winforms",
                "csharp-empty"   => "classlib",
                _                => "console"
            };

            // F# templates need the --language flag
            var isFSharp = _selectedTemplate is "fsharp-console" or "fsharp-empty";
            var langArg  = isFSharp ? " --language F#" : string.Empty;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"new {templateName} -n \"{projectName}\" -o \"{projectDir}\"{langArg}",
                    WorkingDirectory = location,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Debug.WriteLine($"dotnet new output: {output}");
            Debug.WriteLine($"dotnet new error: {error}");
            Debug.WriteLine($"dotnet new exit code: {process.ExitCode}");

            if (process.ExitCode == 0)
            {
                // F# projects use .fsproj; everything else uses .csproj.
                string csprojPath;
                {
                    var projExt = _selectedTemplate is "fsharp-console" or "fsharp-empty" ? ".fsproj" : ".csproj";
                    csprojPath = Path.Combine(projectDir, $"{projectName}{projExt}");
                }
                string? slnFilePath = null;
                
                // If we have a current solution, use it
                if (!string.IsNullOrEmpty(_currentSolutionPath) && File.Exists(_currentSolutionPath))
                {
                    slnFilePath = _currentSolutionPath;
                    slnDir = Path.GetDirectoryName(_currentSolutionPath) ?? location;
                }
                else
                {
                    // Check if solution file already exists in the directory (both .sln and .slnx formats)
                    if (Directory.Exists(slnDir))
                    {
                        var existingSlnFiles = Directory.GetFiles(slnDir, "*.sln");
                        var existingSlnxFiles = Directory.GetFiles(slnDir, "*.slnx");
                        
                        if (existingSlnFiles.Length > 0)
                        {
                            slnFilePath = existingSlnFiles[0];
                        }
                        else if (existingSlnxFiles.Length > 0)
                        {
                            slnFilePath = existingSlnxFiles[0];
                        }
                    }
                }

                // Use SolutionService for creating/updating solution
                var solutionService = new SolutionService();
                
                if (slnFilePath == null)
                {
                    // Create new solution using SolutionService with selected format.
                    // slnDir is already the correct final directory (not the parent):
                    //   sameDir==true  => slnDir = location\projectName  (project folder)
                    //   sameDir==false => slnDir = location\solutionName  (solution folder)
                    // So always pass slnDir with createDirectory:false to avoid double-nesting.
                    Debug.WriteLine($"=== Creating new solution ===");
                    Debug.WriteLine($"slnDir: {slnDir}");
                    Debug.WriteLine($"solutionName: {solutionName}");
                    Debug.WriteLine($"useSlnx: {useSlnx}");
                    
                    Directory.CreateDirectory(slnDir); // ensure dir exists
                    var selectedFormat = useSlnx ? SolutionFormat.Slnx : SolutionFormat.Sln;
                    slnFilePath = await solutionService.CreateSolutionAsync(
                        slnDir,
                        solutionName, 
                        createDirectory: false,  // slnDir is already the target directory
                        initGit: false, 
                        format: selectedFormat);
                    
                    Debug.WriteLine($"slnFilePath: {slnFilePath}");
                    Debug.WriteLine($"Created solution file exists: {(slnFilePath != null ? File.Exists(slnFilePath).ToString() : "slnFilePath is null")}");
                    
                    if (slnFilePath == null)
                    {
                        Debug.WriteLine("Failed to create solution file");
                        return;
                    }
                }
                
                // Add project to solution using SolutionService
                if (File.Exists(csprojPath) && File.Exists(slnFilePath))
                {
                    Debug.WriteLine($"=== Adding project to solution ===");
                    Debug.WriteLine($"csprojPath: {csprojPath}");
                    Debug.WriteLine($"slnFilePath: {slnFilePath}");
                    
                    var added = await solutionService.AddProjectToSolutionAsync(slnFilePath, csprojPath);
                    Debug.WriteLine($"Project added to solution: {added}");
                }

                // Initialize git if requested
                if (createGit?.IsChecked == true)
                {
                    var gitService = new GitService();
                    var initResult = await gitService.InitAsync(slnDir);
                    if (initResult.Success)
                    {
                        // Make a full initial commit with every generated file so that
                        // "Revert Commit" in GitWindow has a valid parent state to return to.
                        await gitService.MakeInitialCommitAsync("Initial commit");
                    }
                }

                // Set result paths and close — pass slnFilePath as the result so MainWindow can open the solution
                CreatedProjectPath = csprojPath;
                CreatedSolutionPath = slnFilePath;
                // Return the solution file path so the IDE loads the solution (shows it in explorer)
                Close(slnFilePath);
            }
            else
            {
                Debug.WriteLine($"Failed to create project. Exit code: {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            // Show error dialog
            Debug.WriteLine($"Error creating project: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}

