using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class AddProjectToSolutionWindow : Window
{
    private string _selectedTemplate = "console";
    private readonly string _solutionPath;
    private readonly string _solutionDir;

    public string? CreatedProjectPath { get; private set; }

    public AddProjectToSolutionWindow(string solutionPath)
    {
        InitializeComponent();
        
        _solutionPath = solutionPath;
        _solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        
        var solutionText = this.FindControl<TextBlock>("SolutionPathText");
        if (solutionText != null)
        {
            solutionText.Text = solutionPath;
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
            
            var templates = new[] { "ConsoleTemplate", "ClassLibTemplate", "AvaloniaTemplate", 
                                   "WebApiTemplate", "XUnitTemplate", "WpfTemplate" };
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
        UpdateProjectPathPreview();
    }

    private void UpdateProjectPathPreview()
    {
        var projectNameBox = this.FindControl<TextBox>("ProjectNameBox");
        var previewText = this.FindControl<TextBlock>("ProjectPathPreview");
        
        if (projectNameBox != null && previewText != null)
        {
            var projectName = projectNameBox.Text ?? "NewProject";
            var fullPath = Path.Combine(_solutionDir, projectName, $"{projectName}.csproj");
            previewText.Text = fullPath;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        var projectNameBox = this.FindControl<TextBox>("ProjectNameBox");
        if (projectNameBox == null) return;

        var projectName = projectNameBox.Text?.Trim() ?? "NewProject";

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return;
        }

        try
        {
            var projectDir = Path.Combine(_solutionDir, projectName);
            Directory.CreateDirectory(projectDir);

            // Determine template name
            var templateName = _selectedTemplate switch
            {
                "console" => "console",
                "classlib" => "classlib",
                "avalonia" => "avalonia.app",
                "webapi" => "webapi",
                "xunit" => "xunit",
                "wpf" => "wpf",
                _ => "console"
            };

            // Create project
            var createProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"new {templateName} -n \"{projectName}\" -o \"{projectDir}\"",
                    WorkingDirectory = _solutionDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            createProcess.Start();
            await createProcess.WaitForExitAsync();

            if (createProcess.ExitCode == 0)
            {
                var projectPath = Path.Combine(projectDir, $"{projectName}.csproj");
                
                // Add project to solution using SolutionService (supports both sln and slnx)
                var solutionService = new SolutionService();
                var added = await solutionService.AddProjectToSolutionAsync(_solutionPath, projectPath);

                if (added)
                {
                    CreatedProjectPath = projectPath;
                    Close(CreatedProjectPath);
                }
                else
                {
                    Debug.WriteLine("Failed to add project to solution");
                    // Still return the project path since project was created
                    CreatedProjectPath = projectPath;
                    Close(CreatedProjectPath);
                }
            }
            else
            {
                var error = await createProcess.StandardError.ReadToEndAsync();
                Debug.WriteLine($"Error creating project: {error}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating project: {ex.Message}");
        }
    }
}
