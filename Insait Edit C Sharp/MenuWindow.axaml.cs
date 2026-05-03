using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class MenuWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly Dictionary<string, Button> _categoryButtons = new();
    private string _activeCategory = "File";

    public MenuWindow()
    {
        InitializeComponent();
        _mainWindow = null!;
    }

    public MenuWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        // Restore focus to main window when this window closes
        this.Closed += (_, _) => _mainWindow.Activate();

        InitializeCategories();
        ShowCategory("File");
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

    private void InitializeCategories()
    {
        var categoriesPanel = this.FindControl<StackPanel>("CategoriesPanel");
        if (categoriesPanel == null) return;

        var categories = new[]
        {
            ("📁", "File", LocalizationService.Get("Menu.File")),
            ("✏️", "Edit", LocalizationService.Get("Menu.Edit")),
            ("👁️", "View", LocalizationService.Get("Menu.View")),
            ("🔨", "Build", LocalizationService.Get("Menu.Build")),
            ("🔧", "Tools", LocalizationService.Get("Menu.Tools")),
            ("🌐", "Language", LocalizationService.Get("Lang.Language")),
        };

        foreach (var (icon, key, label) in categories)
        {
            var btn = new Button
            {
                Classes = { "menu-category" },
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = icon,
                            FontSize = 16,
                            FontFamily = new FontFamily("Segoe UI Emoji"),
                            Margin = new Thickness(0, 0, 10, 0),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = label,
                            FontSize = 13,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    }
                }
            };

            btn.Click += (s, e) => ShowCategory(key);
            _categoryButtons[key] = btn;
            categoriesPanel.Children.Add(btn);
        }

        // Set first category as active
        if (_categoryButtons.ContainsKey("File"))
        {
            _categoryButtons["File"].Classes.Add("active");
        }
    }

    private void ShowCategory(string categoryName)
    {
        // Update active button
        foreach (var kvp in _categoryButtons)
        {
            kvp.Value.Classes.Remove("active");
        }
        if (_categoryButtons.ContainsKey(categoryName))
        {
            _categoryButtons[categoryName].Classes.Add("active");
        }

        _activeCategory = categoryName;

        // Update content
        var contentPanel = this.FindControl<StackPanel>("ContentPanel");
        if (contentPanel == null) return;

        contentPanel.Children.Clear();

        switch (categoryName)
        {
            case "File":
                CreateFileContent(contentPanel);
                break;
            case "Edit":
                CreateEditContent(contentPanel);
                break;
            case "View":
                CreateViewContent(contentPanel);
                break;
            case "Build":
                CreateBuildContent(contentPanel);
                break;
            case "Tools":
                CreateToolsContent(contentPanel);
                break;
            case "Language":
                CreateLanguageContent(contentPanel);
                break;
        }
    }

    private void CreateFileContent(StackPanel panel)
    {
        AddHeader(panel, LocalizationService.Get("Menu.SolutionProject"));
        AddMenuItem(panel, LocalizationService.Get("Menu.NewSolution"), "", () => _mainWindow.ExecuteMenuAction("NewSolution"));
        AddMenuItem(panel, LocalizationService.Get("Menu.NewProject"), "Ctrl+Shift+N", () => _mainWindow.ExecuteMenuAction("NewProject"));
        AddMenuItem(panel, LocalizationService.Get("Menu.AddProjectToSolution"), "", () => _mainWindow.ExecuteMenuAction("AddProjectToSolution"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.FileOperations"));
        AddMenuItem(panel, LocalizationService.Get("Menu.NewFile"), "Ctrl+N", () => _mainWindow.ExecuteMenuAction("NewFile"));
        AddMenuItem(panel, LocalizationService.Get("Menu.OpenFile"), "Ctrl+O", () => _mainWindow.ExecuteMenuAction("OpenFile"));
        AddMenuItem(panel, LocalizationService.Get("Menu.OpenFolder"), "", () => _mainWindow.ExecuteMenuAction("OpenFolder"));
        AddMenuItem(panel, LocalizationService.Get("Menu.OpenSolution"), "", () => _mainWindow.ExecuteMenuAction("OpenSolution"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.Save"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Save"), "Ctrl+S", () => _mainWindow.ExecuteMenuAction("Save"));
        AddMenuItem(panel, LocalizationService.Get("Menu.SaveAs"), "", () => _mainWindow.ExecuteMenuAction("SaveAs"));
        AddMenuItem(panel, LocalizationService.Get("Menu.SaveAll"), "Ctrl+Shift+S", () => _mainWindow.ExecuteMenuAction("SaveAll"));

        if (SetStandard.SetDefaultManager.IsSupported)
        {
            AddSeparator(panel);
            AddHeader(panel, LocalizationService.Get("Menu.FileAssociations"));
            AddMenuItem(panel, LocalizationService.Get("Menu.SetDefault"), "", () => _mainWindow.ExecuteMenuAction("SetDefaultForFiles"));
            AddMenuItem(panel, LocalizationService.Get("Menu.OpenDefaultApps"), "", () => _mainWindow.ExecuteMenuAction("OpenDefaultApps"));
        }
    }

    private void CreateEditContent(StackPanel panel)
    {
        AddHeader(panel, LocalizationService.Get("Menu.UndoRedo"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Undo"), "Ctrl+Z", () => _mainWindow.ExecuteMenuAction("Undo"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Redo"), "Ctrl+Y", () => _mainWindow.ExecuteMenuAction("Redo"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.FindReplace"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Find"), "Ctrl+F", () => _mainWindow.ExecuteMenuAction("Find"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Replace"), "Ctrl+H", () => _mainWindow.ExecuteMenuAction("Replace"));
        AddMenuItem(panel, LocalizationService.Get("Menu.FindInFiles"), "Ctrl+Shift+F", () => _mainWindow.ExecuteMenuAction("FindInFiles"));


    }

    private void CreateViewContent(StackPanel panel)
    {
        // ── Side panels ──────────────────────────────────────
        AddHeader(panel, LocalizationService.Get("Menu.Panels"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Explorer"), "Ctrl+Shift+E", () => { Close(); _mainWindow.ExecuteMenuAction("ShowExplorer"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.AIAssistant"), "Ctrl+Shift+I", () => { Close(); _mainWindow.ExecuteMenuAction("ToggleAI"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.Search"), "Ctrl+Shift+F", () => { Close(); _mainWindow.ExecuteMenuAction("ShowSearch"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.SourceControl"), "Ctrl+Shift+G", () => { Close(); _mainWindow.ExecuteMenuAction("ShowSourceControl"); });

        AddSeparator(panel);

        // ── Bottom panel / terminal tabs ─────────────────────
        AddHeader(panel, LocalizationService.Get("Menu.BottomPanel"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Terminal"), "Ctrl+`", () => { Close(); _mainWindow.ExecuteMenuAction("ShowTerminal"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.NewTerminal"), "", () => { Close(); _mainWindow.ExecuteMenuAction("NewTerminal"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.Problems"), "", () => { Close(); _mainWindow.ExecuteMenuAction("ShowProblems"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.BuildOutput"), "", () => { Close(); _mainWindow.ExecuteMenuAction("ShowBuildOutput"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.RunOutput"), "", () => { Close(); _mainWindow.ExecuteMenuAction("ShowRunOutput"); });

        AddSeparator(panel);

        // ── Panel toggles (Focus layout) ─────────────────────
        AddHeader(panel, LocalizationService.Get("Menu.FocusLayout"));
        AddMenuItem(panel, LocalizationService.Get("Menu.ToggleLeftPanel"), "Ctrl+Shift+E", () => { Close(); _mainWindow.ExecuteMenuAction("ToggleLeftPanel"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.ToggleBottomPanel"), "Ctrl+`", () => { Close(); _mainWindow.ExecuteMenuAction("ToggleBottomPanel"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.ToggleAIPanel"), "Ctrl+Shift+I", () => { Close(); _mainWindow.ExecuteMenuAction("ToggleRightPanel"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.ZenMode"), "Ctrl+Shift+Z", () => { Close(); _mainWindow.ExecuteMenuAction("ToggleZenMode"); });

        AddSeparator(panel);

        // ── Preview ───────────────────────────────────────────
        AddHeader(panel, LocalizationService.Get("Menu.Preview"));
        AddMenuItem(panel, LocalizationService.Get("Menu.PreviewAxaml"), "Ctrl+Shift+P", () => { Close(); _mainWindow.ExecuteMenuAction("PreviewAxaml"); });

        AddSeparator(panel);

        // ── Window ────────────────────────────────────────────
        AddHeader(panel, LocalizationService.Get("Menu.Window"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Minimize"), "", () => { Close(); _mainWindow.ExecuteMenuAction("Minimize"); });
        AddMenuItem(panel, LocalizationService.Get("Menu.MaximizeRestore"), "", () => { Close(); _mainWindow.ExecuteMenuAction("ToggleMaximize"); });
    }

    private void CreateBuildContent(StackPanel panel)
    {
        AddHeader(panel, LocalizationService.Get("Menu.BuildHeader"));
        AddMenuItem(panel, LocalizationService.Get("Menu.BuildProject"), "Ctrl+B", () => _mainWindow.ExecuteMenuAction("Build"));
        AddMenuItem(panel, LocalizationService.Get("Menu.RebuildProject"), "Ctrl+Shift+B", () => _mainWindow.ExecuteMenuAction("Rebuild"));
        AddMenuItem(panel, LocalizationService.Get("Menu.CleanProject"), "", () => _mainWindow.ExecuteMenuAction("Clean"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.Analysis"));
        AddMenuItem(panel, LocalizationService.Get("Menu.AnalyzeCode"), "Ctrl+Shift+A", () => _mainWindow.ExecuteMenuAction("Analyze"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.RunHeader"));
        AddMenuItem(panel, LocalizationService.Get("Menu.RunProject"), "F5", () => _mainWindow.ExecuteMenuAction("Run"));
        AddMenuItem(panel, LocalizationService.Get("Menu.StopProject"), "Shift+F5", () => _mainWindow.ExecuteMenuAction("Stop"));
        AddMenuItem(panel, LocalizationService.Get("Menu.RunConfigurations"), "Shift+Alt+F10", () => _mainWindow.ExecuteMenuAction("RunConfigurations"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.PackagesDeploy"));
        AddMenuItem(panel, LocalizationService.Get("Menu.RestoreNuGet"), "", () => _mainWindow.ExecuteMenuAction("RestorePackages"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Publish"), "", () => _mainWindow.ExecuteMenuAction("Publish"));
    }


    private void CreateToolsContent(StackPanel panel)
    {
        AddHeader(panel, LocalizationService.Get("Menu.ToolsHeader"));
        AddMenuItem(panel, LocalizationService.Get("Menu.OpenTerminal"), "Ctrl+`", () => _mainWindow.ExecuteMenuAction("OpenTerminal"));
        AddMenuItem(panel, LocalizationService.Get("Menu.RefreshFileTree"), "", () => _mainWindow.ExecuteMenuAction("RefreshFileTree"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.SettingsHeader"));
        AddMenuItem(panel, LocalizationService.Get("Menu.Settings"), "Ctrl+,", () => _mainWindow.ExecuteMenuAction("OpenSettings"));

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("Menu.NuGetHeader"));
        AddMenuItem(panel, LocalizationService.Get("Menu.ManageNuGet"), "Ctrl+Shift+N", () => _mainWindow.ExecuteMenuAction("ManageNuGetPackages"));
    }


    private void CreateLanguageContent(StackPanel panel)
    {
        AddHeader(panel, LocalizationService.Get("Lang.Language"));

        var languages = new[]
        {
            (LocalizationService.AppLanguage.English,   "🇬🇧", LocalizationService.Get("Lang.English")),
            (LocalizationService.AppLanguage.Ukrainian,  "🇺🇦", LocalizationService.Get("Lang.Ukrainian")),
            (LocalizationService.AppLanguage.German,     "🇩🇪", LocalizationService.Get("Lang.German")),
            (LocalizationService.AppLanguage.Russian,    "🇷🇺", LocalizationService.Get("Lang.Russian")),
            (LocalizationService.AppLanguage.Turkish,    "🇹🇷", LocalizationService.Get("Lang.Turkish")),
        };

        foreach (var (lang, flag, label) in languages)
        {
            // A standard language is only "active" when no custom language is loaded
            var isActive = LocalizationService.CurrentLanguage == lang
                           && GitHubCopilotService.LoadedLanguageName == null;
            var text = $"{flag}  {label}" + (isActive ? "  ✓" : "");
            AddMenuItem(panel, text, "", () =>
            {
                LocalizationService.CurrentLanguage = lang;
                Close();
            });
        }

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("GitHub.CustomLanguages"));

        // List custom languages from the translations folder
        var customLangs = GitHubCopilotService.GetAvailableCustomLanguages();
        foreach (var langName in customLangs)
        {
            var name = langName;
            var isLoaded = GitHubCopilotService.LoadedLanguageName == name;
            var label = $"🌐  {name}" + (isLoaded ? "  ✓" : "");
            AddMenuItem(panel, label, "", () =>
            {
                GitHubCopilotService.LoadCustomDictionary(name);
                LocalizationService.SaveCustomLanguageName(name);
                LocalizationService.NotifyLanguageChanged();
                Close();
            });
        }

        AddMenuItem(panel, $"➕  {LocalizationService.Get("GitHub.AddCustomLanguage")}", "", async () =>
        {
            var dialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = LocalizationService.Get("GitHub.SelectAxamlFile"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("AXAML files")
                    {
                        Patterns = new[] { "*.axaml" }
                    }
                }
            };

            var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(dialog);
            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                var imported = GitHubCopilotService.ImportDictionaryFile(path);
                if (imported != null)
                {
                    GitHubCopilotService.LoadCustomDictionary(imported);
                    LocalizationService.SaveCustomLanguageName(imported);
                    LocalizationService.NotifyLanguageChanged();
                }
            }

            Close();
        });

        AddSeparator(panel);
        AddHeader(panel, LocalizationService.Get("GitHub.ControlPanel"));

        AddMenuItem(panel, $"🚀  {LocalizationService.Get("GitHub.LaunchCopilot")}", "", async () =>
        {
            try { await GitHubCopilotService.EnsureEnglishDictionaryAsync(); } catch { /* non-fatal */ }
            GitHubCopilotService.LaunchCopilotCli();
            Close();
        });


        AddMenuItem(panel, $"📂  {LocalizationService.Get("GitHub.OpenTranslationsFolder")}", "", async () =>
        {
            try { await GitHubCopilotService.EnsureEnglishDictionaryAsync(); } catch { /* non-fatal */ }
            GitHubCopilotService.OpenTranslationsFolder();
            Close();
        });

        AddMenuItem(panel, $"📄  {LocalizationService.Get("GitHub.OpenEnglishLocalization")}", "", async () =>
        {
            try { await GitHubCopilotService.EnsureEnglishDictionaryAsync(); } catch { /* non-fatal */ }
            GitHubCopilotService.OpenEnglishLocalizationFile();
            Close();
        });
    }

    private void AddHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFAB387")),
            Margin = new Thickness(0, panel.Children.Count == 0 ? 2 : 6, 0, 2)
        });
    }

    private void AddSeparator(StackPanel panel)
    {
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            Margin = new Thickness(0, 5, 0, 5)
        });
    }

    private void AddMenuItem(StackPanel panel, string text, string shortcut, Action action)
    {
        var btn = new Button
        {
            Classes = { "menu-item" }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(textBlock, 0);
        grid.Children.Add(textBlock);

        if (!string.IsNullOrEmpty(shortcut))
        {
            var shortcutBlock = new TextBlock
            {
                Text = shortcut,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            Grid.SetColumn(shortcutBlock, 1);
            grid.Children.Add(shortcutBlock);
        }

        btn.Content = grid;
        btn.Click += (s, e) =>
        {
            action();
            Close();
        };

        panel.Children.Add(btn);
    }
}
