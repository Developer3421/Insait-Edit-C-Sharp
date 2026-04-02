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
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("AddProject.Title");
        var titleBar = this.FindControl<TextBlock>("TitleBarText");
        if (titleBar != null) titleBar.Text = L("AddProject.Title");
        var slnLabel = this.FindControl<TextBlock>("SolutionLabel");
        if (slnLabel != null) slnLabel.Text = L("AddProject.Solution");
        var tplLabel = this.FindControl<TextBlock>("TemplateLabel");
        if (tplLabel != null) tplLabel.Text = L("AddProject.Template");
        var projLabel = this.FindControl<TextBlock>("ProjectNameLabel");
        if (projLabel != null) projLabel.Text = L("AddProject.ProjectName");
        var gitRepo = this.FindControl<TextBlock>("CreateGitRepoText");
        if (gitRepo != null) gitRepo.Text = L("AddProject.CreateGitRepo");
        var createdAt = this.FindControl<TextBlock>("CreatedAtLabel");
        if (createdAt != null) createdAt.Text = L("AddProject.CreatedAt");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (cancelBtn != null) cancelBtn.Content = L("AddProject.Cancel");
        var createBtn = this.FindControl<Button>("CreateButton");
        if (createBtn != null) createBtn.Content = L("AddProject.Add");
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
                                   "FSharpConsoleTemplate", "FSharpEmptyTemplate",
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
        var previewText = this.FindControl<TextBlock>("ProjectPathPreview");
        var createGitRepo = this.FindControl<CheckBox>("CreateGitRepo");
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

            var result = await SolutionService.RunDotNetNewAsync(_solutionDir, projectDir, projectName, _selectedTemplate);

            if (result.ExitCode == 0)
            {
                var projectPath = SolutionService.FindCreatedProjectFile(projectDir, _selectedTemplate, projectName);
                if (projectPath == null)
                {
                    Debug.WriteLine($"Project file not found after creation in '{projectDir}'.");
                    if (previewText != null)
                    {
                        previewText.Text = $"Error: project file was not created in {projectDir}";
                    }
                    return;
                }
                
                // Add project to solution using SolutionService (supports both sln and slnx)
                var solutionService = new SolutionService();
                var added = await solutionService.AddProjectToSolutionAsync(_solutionPath, projectPath);

                if (added)
                {
                    if (createGitRepo?.IsChecked == true)
                    {
                        var gitSetupService = new ProjectCreationGitService();
                        var gitSetupResult = await gitSetupService.EnsureRepositoryWithInitialCommitAsync(projectDir);
                        if (!gitSetupResult.Success)
                        {
                            Debug.WriteLine($"Git setup failed for '{projectDir}': {gitSetupResult.Error}");
                        }
                    }

                    CreatedProjectPath = projectPath;
                    Close(CreatedProjectPath);
                }
                else
                {
                    Debug.WriteLine("Failed to add project to solution");
                    if (createGitRepo?.IsChecked == true)
                    {
                        var gitSetupService = new ProjectCreationGitService();
                        var gitSetupResult = await gitSetupService.EnsureRepositoryWithInitialCommitAsync(projectDir);
                        if (!gitSetupResult.Success)
                        {
                            Debug.WriteLine($"Git setup failed for '{projectDir}': {gitSetupResult.Error}");
                        }
                    }

                    // Still return the project path since project was created
                    CreatedProjectPath = projectPath;
                    Close(CreatedProjectPath);
                }
            }
            else
            {
                var errorText = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                Debug.WriteLine($"Error creating project: {errorText}");
                if (previewText != null)
                {
                    previewText.Text = $"Error: {errorText}";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating project: {ex.Message}");
            if (previewText != null)
            {
                previewText.Text = $"Error: {ex.Message}";
            }
        }
    }
}
