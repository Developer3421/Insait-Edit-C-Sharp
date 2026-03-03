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

public partial class NewSolutionWindow : Window
{
    private readonly string _defaultLocation;

    public string? CreatedSolutionPath { get; private set; }

    public NewSolutionWindow()
    {
        InitializeComponent();
        
        _defaultLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos");
        
        var locationBox = this.FindControl<TextBox>("LocationBox");
        if (locationBox != null)
        {
            locationBox.Text = _defaultLocation;
        }
        
        // Attach format change handlers
        var slnxFormat = this.FindControl<RadioButton>("SlnxFormat");
        var slnFormat = this.FindControl<RadioButton>("SlnFormat");
        if (slnxFormat != null)
        {
            slnxFormat.IsCheckedChanged += (s, e) => UpdateSolutionPathPreview();
        }
        if (slnFormat != null)
        {
            slnFormat.IsCheckedChanged += (s, e) => UpdateSolutionPathPreview();
        }
        
        UpdateSolutionPathPreview();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("NewSolution.Title");
        var titleBar = this.FindControl<TextBlock>("TitleBarText");
        if (titleBar != null) titleBar.Text = L("NewSolution.Title");
        var slnLabel = this.FindControl<TextBlock>("SolutionNameLabel");
        if (slnLabel != null) slnLabel.Text = L("NewSolution.SolutionName");
        var locLabel = this.FindControl<TextBlock>("LocationLabel");
        if (locLabel != null) locLabel.Text = L("NewSolution.Location");
        var browseBtn = this.FindControl<Button>("BrowseButton");
        if (browseBtn != null) browseBtn.Content = L("Common.Browse");
        var createDirText = this.FindControl<TextBlock>("CreateSolutionDirText");
        if (createDirText != null) createDirText.Text = L("NewSolution.CreateSolutionDir");
        var initGitText = this.FindControl<TextBlock>("InitGitRepoText");
        if (initGitText != null) initGitText.Text = L("NewSolution.InitGitRepo");
        var fmtLabel = this.FindControl<TextBlock>("SolutionFormatLabel");
        if (fmtLabel != null) fmtLabel.Text = L("NewSolution.SolutionFormat");
        var createdAt = this.FindControl<TextBlock>("CreatedAtLabel");
        if (createdAt != null) createdAt.Text = L("NewSolution.CreatedAt");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (cancelBtn != null) cancelBtn.Content = L("NewSolution.Cancel");
        var createBtn = this.FindControl<Button>("CreateButton");
        if (createBtn != null) createBtn.Content = L("NewSolution.Create");
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

    private void SolutionName_Changed(object? sender, TextChangedEventArgs e)
    {
        UpdateSolutionPathPreview();
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
            UpdateSolutionPathPreview();
        }
    }

    private void UpdateSolutionPathPreview()
    {
        var solutionNameBox = this.FindControl<TextBox>("SolutionNameBox");
        var locationBox = this.FindControl<TextBox>("LocationBox");
        var previewText = this.FindControl<TextBlock>("SolutionPathPreview");
        var createDir = this.FindControl<CheckBox>("CreateSolutionDir");
        var slnxFormat = this.FindControl<RadioButton>("SlnxFormat");
        
        if (solutionNameBox != null && locationBox != null && previewText != null)
        {
            var solutionName = solutionNameBox.Text ?? "MySolution";
            var location = locationBox.Text ?? _defaultLocation;
            var extension = slnxFormat?.IsChecked == true ? ".slnx" : ".sln";
            
            string fullPath;
            if (createDir?.IsChecked == true)
            {
                fullPath = Path.Combine(location, solutionName, $"{solutionName}{extension}");
            }
            else
            {
                fullPath = Path.Combine(location, $"{solutionName}{extension}");
            }
            
            previewText.Text = fullPath;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        var solutionNameBox = this.FindControl<TextBox>("SolutionNameBox");
        var locationBox = this.FindControl<TextBox>("LocationBox");
        var createDir = this.FindControl<CheckBox>("CreateSolutionDir");
        var createGit = this.FindControl<CheckBox>("CreateGitRepo");
        var previewText = this.FindControl<TextBlock>("SolutionPathPreview");
        var slnxFormat = this.FindControl<RadioButton>("SlnxFormat");

        if (solutionNameBox == null || locationBox == null) return;

        var solutionName = solutionNameBox.Text?.Trim() ?? "MySolution";
        var location = locationBox.Text?.Trim() ?? _defaultLocation;
        var useSlnx = slnxFormat?.IsChecked == true;
        var extension = useSlnx ? ".slnx" : ".sln";

        if (string.IsNullOrWhiteSpace(solutionName))
        {
            if (previewText != null)
            {
                previewText.Text = "Error: Solution name cannot be empty";
                previewText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFF38BA8"));
            }
            return;
        }

        try
        {
            string solutionDir;
            if (createDir?.IsChecked == true)
            {
                solutionDir = Path.Combine(location, solutionName);
            }
            else
            {
                solutionDir = location;
            }

            // Create directory
            Directory.CreateDirectory(solutionDir);
            
            // Show progress
            if (previewText != null)
            {
                previewText.Text = "Creating solution...";
            }

            var solutionFilePath = Path.Combine(solutionDir, $"{solutionName}{extension}");

            if (useSlnx)
            {
                // Create slnx file directly (modern XML format)
                await CreateSlnxFileAsync(solutionFilePath, solutionName);
            }
            else
            {
                // Write .sln file directly (dotnet new sln creates .slnx in .NET 10+)
                await CreateSlnFileAsync(solutionFilePath);
            }

            // Wait a bit for file system to sync
            await Task.Delay(200);
            
            // Verify the solution file exists
            if (!File.Exists(solutionFilePath))
            {
                // Try again after another delay
                await Task.Delay(500);
            }

            // Initialize git if requested
            if (createGit?.IsChecked == true)
            {
                try
                {
                    var gitProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = "init",
                            WorkingDirectory = solutionDir,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    gitProcess.Start();
                    await gitProcess.WaitForExitAsync();

                    // Create .gitignore
                    var gitignorePath = Path.Combine(solutionDir, ".gitignore");
                    await File.WriteAllTextAsync(gitignorePath, GetDotNetGitIgnore());
                }
                catch (Exception gitEx)
                {
                    Debug.WriteLine($"Git init failed: {gitEx.Message}");
                    // Don't fail the whole operation for git errors
                }
            }

            CreatedSolutionPath = solutionFilePath;
            Close(CreatedSolutionPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating solution: {ex.Message}");
            if (previewText != null)
            {
                previewText.Text = $"Error: {ex.Message}";
                previewText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFF38BA8"));
            }
        }
    }

    /// <summary>
    /// Create a slnx file with modern XML format
    /// </summary>
    private async Task CreateSlnxFileAsync(string filePath, string solutionName)
    {
        var content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Solution>\r\n</Solution>\r\n";
        Debug.WriteLine($"CreateSlnxFileAsync: Writing to {filePath}");
        await File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);
        await Task.Delay(50);
        Debug.WriteLine($"CreateSlnxFileAsync: File exists after write: {File.Exists(filePath)}");
    }

    /// <summary>
    /// Create a .sln file with legacy Visual Studio format
    /// </summary>
    private async Task CreateSlnFileAsync(string filePath, string _ = "")
    {
        var content = "\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n" +
                      "# Visual Studio Version 17\r\n" +
                      "VisualStudioVersion = 17.0.31903.59\r\n" +
                      "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
                      "Global\r\n" +
                      "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
                      "\t\tDebug|Any CPU = Debug|Any CPU\r\n" +
                      "\t\tRelease|Any CPU = Release|Any CPU\r\n" +
                      "\tEndGlobalSection\r\n" +
                      "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
                      "\tEndGlobalSection\r\n" +
                      "\tGlobalSection(SolutionProperties) = preSolution\r\n" +
                      "\t\tHideSolutionNode = FALSE\r\n" +
                      "\tEndGlobalSection\r\n" +
                      "EndGlobal\r\n";
        Debug.WriteLine($"CreateSlnFileAsync: Writing to {filePath}");
        await File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);
        await Task.Delay(50);
        Debug.WriteLine($"CreateSlnFileAsync: File exists after write: {File.Exists(filePath)}");
    }

    private static string GetDotNetGitIgnore()
    {
        return @"## .NET
bin/
obj/
*.user
*.suo
*.userosscache
*.sln.docstates

## Visual Studio
.vs/
*.rsuser
*.vspscc
*.vssscc
.builds

## JetBrains Rider
.idea/
*.sln.iml

## User-specific files
*.userprefs

## Build results
[Dd]ebug/
[Rr]elease/
x64/
x86/

## NuGet
packages/
*.nupkg
project.lock.json
project.fragment.lock.json
artifacts/
";
    }
}
