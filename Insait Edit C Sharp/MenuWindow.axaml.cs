using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;

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
            ("📁", "File"),
            ("✏️", "Edit"),
            ("👁️", "View"),
            ("🔨", "Build"),
            ("🐛", "Debug"),
            ("🔧", "Tools"),
            ("❓", "Help")
        };

        foreach (var (icon, name) in categories)
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
                            Text = name, 
                            FontSize = 13, 
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center 
                        }
                    }
                }
            };

            btn.Click += (s, e) => ShowCategory(name);
            _categoryButtons[name] = btn;
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
            case "Debug":
                CreateDebugContent(contentPanel);
                break;
            case "Tools":
                CreateToolsContent(contentPanel);
                break;
            case "Help":
                CreateHelpContent(contentPanel);
                break;
        }
    }

    private void CreateFileContent(StackPanel panel)
    {
        AddHeader(panel, "📦 Solution & Project");
        AddMenuItem(panel, "📦 New Solution...", "", () => _mainWindow.ExecuteMenuAction("NewSolution"));
        AddMenuItem(panel, "📁 New Project...", "Ctrl+Shift+N", () => _mainWindow.ExecuteMenuAction("NewProject"));
        AddMenuItem(panel, "🔌 New nanoFramework Project...", "", () => _mainWindow.ExecuteMenuAction("NewEspProject"));
        AddMenuItem(panel, "➕ Add Project to Solution...", "", () => _mainWindow.ExecuteMenuAction("AddProjectToSolution"));
        
        AddSeparator(panel);
        AddHeader(panel, "📁 File Operations");
        AddMenuItem(panel, "📄 New File", "Ctrl+N", () => _mainWindow.ExecuteMenuAction("NewFile"));
        AddMenuItem(panel, "📂 Open File...", "Ctrl+O", () => _mainWindow.ExecuteMenuAction("OpenFile"));
        AddMenuItem(panel, "📁 Open Folder...", "", () => _mainWindow.ExecuteMenuAction("OpenFolder"));
        AddMenuItem(panel, "📦 Open Solution...", "", () => _mainWindow.ExecuteMenuAction("OpenSolution"));
        
        AddSeparator(panel);
        AddHeader(panel, "💾 Save");
        AddMenuItem(panel, "💾 Save", "Ctrl+S", () => _mainWindow.ExecuteMenuAction("Save"));
        AddMenuItem(panel, "💾 Save As...", "", () => _mainWindow.ExecuteMenuAction("SaveAs"));
        AddMenuItem(panel, "💾 Save All", "Ctrl+Shift+S", () => _mainWindow.ExecuteMenuAction("SaveAll"));
        
        AddSeparator(panel);
        AddMenuItem(panel, "🚪 Exit", "Alt+F4", () => _mainWindow.ExecuteMenuAction("Exit"));
    }

    private void CreateEditContent(StackPanel panel)
    {
        AddHeader(panel, "↩️ Undo/Redo");
        AddMenuItem(panel, "↩️ Undo", "Ctrl+Z", () => _mainWindow.ExecuteMenuAction("Undo"));
        AddMenuItem(panel, "↪️ Redo", "Ctrl+Y", () => _mainWindow.ExecuteMenuAction("Redo"));
        
        AddSeparator(panel);
        AddHeader(panel, "🔍 Find & Replace");
        AddMenuItem(panel, "🔍 Find", "Ctrl+F", () => _mainWindow.ExecuteMenuAction("Find"));
        AddMenuItem(panel, "🔄 Replace", "Ctrl+H", () => _mainWindow.ExecuteMenuAction("Replace"));
        AddMenuItem(panel, "🔍 Find in Files", "Ctrl+Shift+F", () => _mainWindow.ExecuteMenuAction("FindInFiles"));
        
        AddSeparator(panel);
        AddHeader(panel, "📝 Code");
        AddMenuItem(panel, "📋 Format Document", "Ctrl+Shift+F", () => _mainWindow.ExecuteMenuAction("FormatDocument"));
        AddMenuItem(panel, "💬 Toggle Comment", "Ctrl+/", () => _mainWindow.ExecuteMenuAction("ToggleComment"));
    }

    private void CreateViewContent(StackPanel panel)
    {
        AddHeader(panel, "📊 Panels");
        AddMenuItem(panel, "🤖 AI Assistant", "", () => _mainWindow.ExecuteMenuAction("ToggleAI"));
        AddMenuItem(panel, "📁 Explorer", "Ctrl+Shift+E", () => _mainWindow.ExecuteMenuAction("ShowExplorer"));
        AddMenuItem(panel, "🔍 Search", "Ctrl+Shift+F", () => _mainWindow.ExecuteMenuAction("ShowSearch"));
        AddMenuItem(panel, "🔀 Source Control", "Ctrl+Shift+G", () => _mainWindow.ExecuteMenuAction("ShowSourceControl"));
        
        AddSeparator(panel);
        AddHeader(panel, "⬇️ Bottom Panel");
        AddMenuItem(panel, "💻 Terminal", "Ctrl+`", () => _mainWindow.ExecuteMenuAction("ShowTerminal"));
        AddMenuItem(panel, "⚠️ Problems", "", () => _mainWindow.ExecuteMenuAction("ShowProblems"));
        AddMenuItem(panel, "📋 Build Output", "", () => _mainWindow.ExecuteMenuAction("ShowBuildOutput"));
        AddMenuItem(panel, "▶️ Run Output", "", () => _mainWindow.ExecuteMenuAction("ShowRunOutput"));
        AddMenuItem(panel, "🐛 Debug Console", "", () => _mainWindow.ExecuteMenuAction("ShowDebugConsole"));
        
        AddSeparator(panel);
        AddHeader(panel, "🖥️ Window");
        AddMenuItem(panel, "➖ Minimize", "", () => _mainWindow.ExecuteMenuAction("Minimize"));
        AddMenuItem(panel, "🔲 Maximize/Restore", "", () => _mainWindow.ExecuteMenuAction("ToggleMaximize"));
    }

    private void CreateBuildContent(StackPanel panel)
    {
        AddHeader(panel, "🔨 Build");
        AddMenuItem(panel, "🔨 Build Project", "Ctrl+B", () => _mainWindow.ExecuteMenuAction("Build"));
        AddMenuItem(panel, "🔄 Rebuild Project", "Ctrl+Shift+B", () => _mainWindow.ExecuteMenuAction("Rebuild"));
        AddMenuItem(panel, "🧹 Clean Project", "", () => _mainWindow.ExecuteMenuAction("Clean"));
        
        AddSeparator(panel);
        AddHeader(panel, "🔍 Analysis");
        AddMenuItem(panel, "🔍 Analyze Code", "Ctrl+Shift+A", () => _mainWindow.ExecuteMenuAction("Analyze"));
        
        AddSeparator(panel);
        AddHeader(panel, "▶️ Run");
        AddMenuItem(panel, "▶️ Run Project", "F5", () => _mainWindow.ExecuteMenuAction("Run"));
        AddMenuItem(panel, "⏹️ Stop", "Shift+F5", () => _mainWindow.ExecuteMenuAction("Stop"));
        AddMenuItem(panel, "⚙️ Run Configurations...", "Shift+Alt+F10", () => _mainWindow.ExecuteMenuAction("RunConfigurations"));
        
        AddSeparator(panel);
        AddHeader(panel, "📦 Packages & Deploy");
        AddMenuItem(panel, "📦 Restore NuGet Packages", "", () => _mainWindow.ExecuteMenuAction("RestorePackages"));
        AddMenuItem(panel, "📤 Publish...", "", () => _mainWindow.ExecuteMenuAction("Publish"));
    }

    private void CreateDebugContent(StackPanel panel)
    {
        AddHeader(panel, "🐛 Debug");
        AddMenuItem(panel, "▶️ Start Debugging", "F5", () => _mainWindow.ExecuteMenuAction("StartDebugging"));
        AddMenuItem(panel, "⏸️ Start Without Debugging", "Ctrl+F5", () => _mainWindow.ExecuteMenuAction("StartWithoutDebugging"));
        AddMenuItem(panel, "⏹️ Stop Debugging", "Shift+F5", () => _mainWindow.ExecuteMenuAction("StopDebugging"));
        
        AddSeparator(panel);
        AddHeader(panel, "🔴 Breakpoints");
        AddMenuItem(panel, "🔴 Toggle Breakpoint", "F9", () => _mainWindow.ExecuteMenuAction("ToggleBreakpoint"));
        AddMenuItem(panel, "❌ Delete All Breakpoints", "Ctrl+Shift+F9", () => _mainWindow.ExecuteMenuAction("DeleteAllBreakpoints"));
        
        AddSeparator(panel);
        AddHeader(panel, "👣 Step");
        AddMenuItem(panel, "➡️ Step Over", "F10", () => _mainWindow.ExecuteMenuAction("StepOver"));
        AddMenuItem(panel, "⬇️ Step Into", "F11", () => _mainWindow.ExecuteMenuAction("StepInto"));
        AddMenuItem(panel, "⬆️ Step Out", "Shift+F11", () => _mainWindow.ExecuteMenuAction("StepOut"));
    }

    private void CreateToolsContent(StackPanel panel)
    {
        AddHeader(panel, "🔧 Tools");
        AddMenuItem(panel, "💻 Open Terminal", "Ctrl+`", () => _mainWindow.ExecuteMenuAction("OpenTerminal"));
        AddMenuItem(panel, "🔄 Refresh File Tree", "", () => _mainWindow.ExecuteMenuAction("RefreshFileTree"));
        
        AddSeparator(panel);
        AddHeader(panel, "⚙️ Settings");
        AddMenuItem(panel, "⚙️ Settings", "Ctrl+,", () => _mainWindow.ExecuteMenuAction("OpenSettings"));
        AddMenuItem(panel, "🎨 Theme", "", () => _mainWindow.ExecuteMenuAction("OpenTheme"));
        AddMenuItem(panel, "⌨️ Keyboard Shortcuts", "Ctrl+K Ctrl+S", () => _mainWindow.ExecuteMenuAction("OpenKeyboardShortcuts"));
        
        AddSeparator(panel);
        AddHeader(panel, "📦 NuGet");
        AddMenuItem(panel, "📦 Manage NuGet Packages", "Ctrl+Shift+N", () => _mainWindow.ExecuteMenuAction("ManageNuGetPackages"));
    }

    private void CreateHelpContent(StackPanel panel)
    {
        AddHeader(panel, "❓ Help");
        AddMenuItem(panel, "📖 Documentation", "F1", () => _mainWindow.ExecuteMenuAction("OpenDocumentation"));
        AddMenuItem(panel, "🎓 Getting Started", "", () => _mainWindow.ExecuteMenuAction("GettingStarted"));
        AddMenuItem(panel, "⌨️ Keyboard Shortcuts", "", () => _mainWindow.ExecuteMenuAction("ShowKeyboardShortcuts"));
        
        AddSeparator(panel);
        AddHeader(panel, "📣 Feedback");
        AddMenuItem(panel, "🐛 Report Issue", "", () => _mainWindow.ExecuteMenuAction("ReportIssue"));
        AddMenuItem(panel, "💡 Feature Request", "", () => _mainWindow.ExecuteMenuAction("FeatureRequest"));
        
        AddSeparator(panel);
        AddHeader(panel, "ℹ️ About");
        AddMenuItem(panel, "ℹ️ About Insait Edit", "", () => _mainWindow.ExecuteMenuAction("ShowAbout"));
        AddMenuItem(panel, "📋 Check for Updates", "", () => _mainWindow.ExecuteMenuAction("CheckForUpdates"));
    }

    private void AddHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFAB387")),
            Margin = new Thickness(0, 8, 0, 4)
        });
    }

    private void AddSeparator(StackPanel panel)
    {
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            Margin = new Thickness(0, 8, 0, 8)
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
