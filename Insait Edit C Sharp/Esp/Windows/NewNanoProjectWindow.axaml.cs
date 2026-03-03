using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Insait_Edit_C_Sharp.Esp.Models;
using Insait_Edit_C_Sharp.Esp.Services;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Esp.Windows;

public partial class NewNanoProjectWindow : Window
{
    private string _selectedTemplate = "BlankApp";
    private readonly string _defaultLocation;
    private readonly string? _currentSolutionPath;

    public string? CreatedProjectPath { get; private set; }

    public NewNanoProjectWindow() : this(null)
    {
    }

    public NewNanoProjectWindow(string? currentSolutionPath)
    {
        InitializeComponent();

        _currentSolutionPath = currentSolutionPath;

        _defaultLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos");

        var locationBox = this.FindControl<TextBox>("LocationBox");
        if (locationBox != null)
        {
            if (!string.IsNullOrEmpty(_currentSolutionPath))
            {
                var slnDir = Path.GetDirectoryName(_currentSolutionPath);
                locationBox.Text = !string.IsNullOrEmpty(slnDir) ? slnDir : _defaultLocation;
            }
            else
            {
                locationBox.Text = _defaultLocation;
            }
        }

        // Populate board combo box
        var boardCombo = this.FindControl<ComboBox>("BoardComboBox");
        if (boardCombo != null)
        {
            foreach (var board in EspBoardTypes.All)
            {
                boardCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{board} - {EspBoardTypes.GetDescription(board)}",
                    Tag = board
                });
            }
            boardCombo.SelectedIndex = 0;
        }

        UpdateProjectPathPreview();
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("Nano.Title");
        // Template names & descriptions
        SetTemplateText("BlankAppTemplate",   L("Nano.TplBlankName"),    L("Nano.TplBlankDesc"));
        SetTemplateText("ClassLibTemplate",   L("Nano.TplClassLibName"), L("Nano.TplClassLibDesc"));
        SetTemplateText("GpioBlinkTemplate",  L("Nano.TplGpioName"),     L("Nano.TplGpioDesc"));
        SetTemplateText("WiFiTemplate",       L("Nano.TplWifiName"),     L("Nano.TplWifiDesc"));
        SetTemplateText("HttpTemplate",       L("Nano.TplHttpName"),     L("Nano.TplHttpDesc"));
        SetTemplateText("I2CTemplate",        L("Nano.TplI2CName"),      L("Nano.TplI2CDesc"));
        // Buttons
        var createBtn = this.FindControl<Button>("CreateButton");
        if (createBtn != null) createBtn.Content = L("Nano.Create");
    }

    private void SetTemplateText(string buttonName, string name, string desc)
    {
        var btn = this.FindControl<Button>(buttonName);
        if (btn?.Content is StackPanel sp && sp.Children.Count >= 2)
        {
            if (sp.Children[1] is TextBlock nameBlock) nameBlock.Text = name;
            if (sp.Children.Count >= 3 && sp.Children[2] is TextBlock descBlock) descBlock.Text = desc;
        }
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
            var templateNames = new[]
            {
                "BlankAppTemplate", "ClassLibTemplate", "GpioBlinkTemplate",
                "WiFiTemplate", "HttpTemplate", "I2CTemplate"
            };
            foreach (var name in templateNames)
            {
                var btn = this.FindControl<Button>(name);
                btn?.Classes.Remove("selected");
            }
            button.Classes.Add("selected");
        }
    }

    private void ProjectName_Changed(object? sender, TextChangedEventArgs e)
    {
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

        if (projectNameBox != null && locationBox != null && previewText != null)
        {
            var projectName = projectNameBox.Text ?? "MyEspProject";
            var location = locationBox.Text ?? _defaultLocation;
            var fullPath = Path.Combine(location, projectName);
            previewText.Text = $"ESP project will be created at: {fullPath}";
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
        var boardCombo = this.FindControl<ComboBox>("BoardComboBox");
        var createNewSln = this.FindControl<CheckBox>("CreateNewSolution");
        var addToSln = this.FindControl<CheckBox>("AddToSolution");

        if (projectNameBox == null || locationBox == null) return;

        var projectName = projectNameBox.Text?.Trim() ?? "MyEspProject";
        var location = locationBox.Text?.Trim() ?? _defaultLocation;

        if (string.IsNullOrWhiteSpace(projectName)) return;

        // Get selected board
        var targetBoard = "ESP32";
        if (boardCombo?.SelectedItem is ComboBoxItem selectedBoard && selectedBoard.Tag is string board)
        {
            targetBoard = board;
        }

        // Parse template
        var template = _selectedTemplate switch
        {
            "BlankApp" => NanoProjectTemplate.BlankApp,
            "ClassLibrary" => NanoProjectTemplate.ClassLibrary,
            "GpioBlink" => NanoProjectTemplate.GpioBlink,
            "WiFiConnect" => NanoProjectTemplate.WiFiConnect,
            "HttpClient" => NanoProjectTemplate.HttpClient,
            "I2CSensor" => NanoProjectTemplate.I2CSensor,
            _ => NanoProjectTemplate.BlankApp
        };

        try
        {
            var nanoProjectService = new NanoProjectService();
            string? solutionPath = null;

            // Determine solution handling
            if (addToSln?.IsChecked == true && !string.IsNullOrEmpty(_currentSolutionPath))
            {
                solutionPath = _currentSolutionPath;
            }
            else if (createNewSln?.IsChecked == true)
            {
                // Create a new solution first
                var solutionService = new SolutionService();
                solutionPath = await solutionService.CreateSolutionAsync(
                    location, projectName,
                    createDirectory: false,
                    initGit: false,
                    format: SolutionFormat.Sln);
            }

            // Create the nanoFramework project
            var project = await nanoProjectService.CreateProjectAsync(
                projectName, location, template, targetBoard, solutionPath);

            if (project != null)
            {
                CreatedProjectPath = project.ProjectFilePath;
                Debug.WriteLine($"Created nanoFramework project: {CreatedProjectPath}");
                Close(CreatedProjectPath);
            }
            else
            {
                Debug.WriteLine("Failed to create nanoFramework project");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating ESP project: {ex.Message}");
        }
    }
}

