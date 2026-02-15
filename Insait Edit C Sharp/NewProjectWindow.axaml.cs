using System;
using System.Diagnostics;
using System.IO;
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

    public string? CreatedProjectPath { get; private set; }

    public NewProjectWindow()
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
        
        UpdateProjectPathPreview();
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

    private void Template_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string template)
        {
            _selectedTemplate = template;
            
            // Update visual selection
            var templates = new[] { "ConsoleTemplate", "ClassLibTemplate", "AvaloniaTemplate", "WebApiTemplate" };
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
        
        if (projectNameBox != null && locationBox != null && previewText != null)
        {
            var projectName = projectNameBox.Text ?? "MyProject";
            var location = locationBox.Text ?? _defaultLocation;
            
            string fullPath;
            if (sameDir?.IsChecked == true)
            {
                fullPath = Path.Combine(location, projectName);
            }
            else
            {
                fullPath = Path.Combine(location, projectName, projectName);
            }
            
            previewText.Text = $"Project will be created at: {fullPath}";
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

        if (projectNameBox == null || locationBox == null) return;

        var projectName = projectNameBox.Text?.Trim() ?? "MyProject";
        var location = locationBox.Text?.Trim() ?? _defaultLocation;
        var solutionName = solutionNameBox?.Text?.Trim() ?? projectName;

        if (string.IsNullOrWhiteSpace(projectName))
        {
            // Show error
            return;
        }

        // Create project directory
        string projectDir;
        if (sameDir?.IsChecked == true)
        {
            projectDir = Path.Combine(location, projectName);
        }
        else
        {
            projectDir = Path.Combine(location, solutionName, projectName);
        }

        try
        {
            Directory.CreateDirectory(projectDir);

            // Run dotnet new
            var templateName = _selectedTemplate switch
            {
                "console" => "console",
                "classlib" => "classlib",
                "avalonia" => "avalonia.app",
                "webapi" => "webapi",
                _ => "console"
            };

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"new {templateName} -n \"{projectName}\" -o \"{projectDir}\"",
                    WorkingDirectory = location,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // Create solution if needed
                if (sameDir?.IsChecked != true)
                {
                    var slnDir = Path.Combine(location, solutionName);
                    var slnProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"new sln -n \"{solutionName}\"",
                            WorkingDirectory = slnDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    slnProcess.Start();
                    await slnProcess.WaitForExitAsync();

                    // Add project to solution
                    var addProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"sln add \"{projectDir}\"",
                            WorkingDirectory = slnDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    addProcess.Start();
                    await addProcess.WaitForExitAsync();
                }

                // Initialize git if requested
                if (createGit?.IsChecked == true)
                {
                    var gitDir = sameDir?.IsChecked == true ? projectDir : Path.Combine(location, solutionName);
                    var gitProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = "init",
                            WorkingDirectory = gitDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    gitProcess.Start();
                    await gitProcess.WaitForExitAsync();
                }

                // Set result and close
                CreatedProjectPath = Path.Combine(projectDir, $"{projectName}.csproj");
                Close(CreatedProjectPath);
            }
        }
        catch (Exception ex)
        {
            // Show error dialog
            Debug.WriteLine($"Error creating project: {ex.Message}");
        }
    }
}

