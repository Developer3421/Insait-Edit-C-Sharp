using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Models;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Floating context-menu window for file-tree nodes.
/// Shows three separate pages depending on the selected node type:
///   • Solution  – for Solution / SolutionFolder nodes
///   • Project   – for Project nodes
///   • Element   – for file and folder nodes
/// </summary>
public partial class ExplorerNodeMenuWindow : Window
{
    // ── State ────────────────────────────────────────────────
    private readonly MainWindow _mainWindow;
    private readonly FileTreeItem? _item;
    private readonly List<FileTreeItem> _selectedItems;

    private readonly Dictionary<string, Button> _pageButtons = new();
    private string _activePage = "Element";

    // ── Page keys ────────────────────────────────────────────
    private const string PageSolution = "Solution";
    private const string PageProject  = "Project";
    private const string PageElement  = "Element";

    // ── Constructor ──────────────────────────────────────────

    /// <summary>Design-time constructor.</summary>
    public ExplorerNodeMenuWindow()
    {
        InitializeComponent();
        _mainWindow   = null!;
        _item         = null;
        _selectedItems = new List<FileTreeItem>();
    }

    /// <summary>
    /// Runtime constructor.
    /// </summary>
    /// <param name="mainWindow">Owning main window (used to dispatch actions).</param>
    /// <param name="item">Primary selected tree item (may be null if nothing is selected).</param>
    /// <param name="selectedItems">All currently selected tree items.</param>
    public ExplorerNodeMenuWindow(MainWindow mainWindow, FileTreeItem? item, List<FileTreeItem> selectedItems)
    {
        InitializeComponent();
        _mainWindow    = mainWindow;
        _item          = item;
        _selectedItems = selectedItems;

        InitializePages();
        ShowPageForItem(item);
    }

    // ── Title bar drag ───────────────────────────────────────

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // ── Page setup ───────────────────────────────────────────

    private void InitializePages()
    {
        var pagesPanel = this.FindControl<StackPanel>("PagesPanel");
        if (pagesPanel == null) return;

        var pages = new[]
        {
            ("🗄️", PageSolution, "Solution"),
            ("📦", PageProject,  "Project"),
            ("📄", PageElement,  "Element"),
        };

        foreach (var (icon, key, label) in pages)
        {
            var btn = new Button { Classes = { "page-category" } };

            btn.Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text             = icon,
                        FontSize         = 16,
                        FontFamily       = new FontFamily("Segoe UI Emoji"),
                        Margin           = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text             = label,
                        FontSize         = 13,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                }
            };

            var capturedKey = key;
            btn.Click += (_, _) => ShowPage(capturedKey);
            _pageButtons[key] = btn;
            pagesPanel.Children.Add(btn);
        }
    }

    /// <summary>Auto-select the correct page based on <paramref name="item"/>.</summary>
    private void ShowPageForItem(FileTreeItem? item)
    {
        if (item == null)
        {
            ShowPage(PageElement);
            return;
        }

        var page = item.ItemType switch
        {
            FileTreeItemType.Solution or
            FileTreeItemType.SolutionFolder => PageSolution,

            FileTreeItemType.Project => PageProject,

            _ => PageElement
        };

        ShowPage(page);
    }

    private void ShowPage(string pageKey)
    {
        // Update sidebar button states
        foreach (var kvp in _pageButtons)
            kvp.Value.Classes.Remove("active");

        if (_pageButtons.TryGetValue(pageKey, out var activeBtn))
            activeBtn.Classes.Add("active");

        _activePage = pageKey;

        // Update title
        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText != null)
        {
            titleText.Text = pageKey switch
            {
                PageSolution => "Solution Menu",
                PageProject  => "Project Menu",
                _            => "Element Menu"
            };
        }

        // Render content
        var contentPanel = this.FindControl<StackPanel>("ContentPanel");
        if (contentPanel == null) return;
        contentPanel.Children.Clear();

        switch (pageKey)
        {
            case PageSolution: CreateSolutionPage(contentPanel); break;
            case PageProject:  CreateProjectPage(contentPanel);  break;
            default:           CreateElementPage(contentPanel);  break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Page: Solution
    // ═══════════════════════════════════════════════════════════

    private void CreateSolutionPage(StackPanel panel)
    {
        bool isMulti = _selectedItems.Count > 1;

        AddHeader(panel, "Add");
        AddMenuItem(panel, "📦 New Project...",      () => Dispatch("AddNewProject"));
        AddMenuItem(panel, "📦 Existing Project...", () => Dispatch("AddExistingProject"));

        AddSeparator(panel);
        AddHeader(panel, "Build");
        AddMenuItem(panel, "🔨 Build",   () => Dispatch("Build"));
        AddMenuItem(panel, "🔄 Rebuild", () => Dispatch("Rebuild"));
        AddMenuItem(panel, "🧹 Clean",   () => Dispatch("Clean"));

        AddSeparator(panel);
        AddHeader(panel, "Git");
        AddMenuItem(panel, "📝 Commit...",    () => Dispatch("GitCommit"));
        AddMenuItem(panel, "📜 Show History", () => Dispatch("GitHistory"));
        AddMenuItem(panel, "↩️ Revert...",    () => Dispatch("GitRevert"));

        AddSeparator(panel);
        AddHeader(panel, "Actions");
        AddMenuItem(panel, "🔄 Reload from Disk", () => Dispatch("Refresh"));
        AddMenuItem(panel, "⚙️ Properties",        () => Dispatch("Properties"));
    }

    // ═══════════════════════════════════════════════════════════
    //  Page: Project
    // ═══════════════════════════════════════════════════════════

    private void CreateProjectPage(StackPanel panel)
    {
        bool isMulti = _selectedItems.Count > 1;

        AddHeader(panel, "Run");
        AddMenuItem(panel, "▶ Run Project", () => Dispatch("Run"));

        AddSeparator(panel);
        AddHeader(panel, "New");
        AddMenuItem(panel, "📄 Class...",                  () => Dispatch("NewClass"));
        AddMenuItem(panel, "📄 Interface...",              () => Dispatch("NewInterface"));
        AddMenuItem(panel, "📄 Record...",                 () => Dispatch("NewRecord"));
        AddMenuItem(panel, "📄 Enum...",                   () => Dispatch("NewEnum"));
        AddMenuItem(panel, "🪟 Avalonia Window...",        () => Dispatch("NewAvaloniaWindow"));
        AddMenuItem(panel, "🎛️ Avalonia UserControl...",   () => Dispatch("NewAvaloniaUserControl"));

        AddSeparator(panel);
        AddHeader(panel, "Add");
        AddMenuItem(panel, "📦 New Project...",      () => Dispatch("AddNewProject"));
        AddMenuItem(panel, "📦 Existing Project...", () => Dispatch("AddExistingProject"));
        AddMenuItem(panel, "📄 New Item...",         () => Dispatch("NewFile"));
        AddMenuItem(panel, "📄 Existing Item...",    () => Dispatch("AddExistingItem"));

        AddSeparator(panel);
        AddHeader(panel, "Build");
        AddMenuItem(panel, "🔨 Build",   () => Dispatch("Build"));
        AddMenuItem(panel, "🔄 Rebuild", () => Dispatch("Rebuild"));
        AddMenuItem(panel, "🧹 Clean",   () => Dispatch("Clean"));

        AddSeparator(panel);
        AddHeader(panel, "Packages");
        AddMenuItem(panel, "📦 Manage NuGet Packages...", () => Dispatch("NuGet"));
        AddMenuItem(panel, "🔗 Add Reference...",         () => Dispatch("AddReference"));

        AddSeparator(panel);
        AddHeader(panel, "Edit");
        AddMenuItem(panel, "✂️ Cut",   () => Dispatch("Cut"));
        AddMenuItem(panel, "📋 Copy",  () => Dispatch("Copy"));
        AddMenuItem(panel, "📄 Paste", () => Dispatch("Paste"));
        AddMenuItem(panel, "✏️ Rename",          () => Dispatch("Rename"));
        if (!isMulti)
            AddMenuItem(panel, "🗑️ Safe Delete...", () => Dispatch("Delete"));
        else
            AddMenuItem(panel, $"🗑️ Delete {_selectedItems.Count} Items...", () => Dispatch("Delete"));

        AddSeparator(panel);
        AddHeader(panel, "Solution");
        AddMenuItem(panel, "🗑️ Remove from Solution", () => Dispatch("RemoveFromSolution"));
        AddMenuItem(panel, "⬇️ Unload Project",        () => Dispatch("UnloadProject"));

        AddSeparator(panel);
        AddHeader(panel, "Git");
        AddMenuItem(panel, "📝 Commit...",    () => Dispatch("GitCommit"));
        AddMenuItem(panel, "📜 Show History", () => Dispatch("GitHistory"));
        AddMenuItem(panel, "↩️ Revert...",    () => Dispatch("GitRevert"));

        AddSeparator(panel);
        AddHeader(panel, "Actions");
        AddMenuItem(panel, "🔄 Reload from Disk", () => Dispatch("Refresh"));
        AddMenuItem(panel, "⚙️ Properties",        () => Dispatch("Properties"));
    }

    // ═══════════════════════════════════════════════════════════
    //  Page: Element (file / folder)
    // ═══════════════════════════════════════════════════════════

    private void CreateElementPage(StackPanel panel)
    {
        bool isMulti  = _selectedItems.Count > 1;
        bool isFolder = _item?.IsDirectory == true;

        if (!isMulti && isFolder)
        {
            AddHeader(panel, "New");
            AddMenuItem(panel, "📄 Class...",                () => Dispatch("NewClass"));
            AddMenuItem(panel, "📄 Interface...",            () => Dispatch("NewInterface"));
            AddMenuItem(panel, "📄 Record...",               () => Dispatch("NewRecord"));
            AddMenuItem(panel, "📄 Enum...",                 () => Dispatch("NewEnum"));
            AddMenuItem(panel, "🪟 Avalonia Window...",      () => Dispatch("NewAvaloniaWindow"));
            AddMenuItem(panel, "🎛️ Avalonia UserControl...", () => Dispatch("NewAvaloniaUserControl"));
            AddMenuItem(panel, "📄 File...",                 () => Dispatch("NewFile"));
            AddMenuItem(panel, "📁 Directory",               () => Dispatch("NewFolder"));
            AddSeparator(panel);
        }

        AddHeader(panel, "Edit");
        AddMenuItem(panel, "✂️ Cut",   () => Dispatch("Cut"));
        AddMenuItem(panel, "📋 Copy",  () => Dispatch("Copy"));
        if (!isMulti)
            AddMenuItem(panel, "📄 Paste", () => Dispatch("Paste"));
        if (!isMulti)
            AddMenuItem(panel, "✏️ Rename", () => Dispatch("Rename"));
        if (!isMulti)
            AddMenuItem(panel, "🗑️ Safe Delete...", () => Dispatch("Delete"));
        else
            AddMenuItem(panel, $"🗑️ Delete {_selectedItems.Count} Items...", () => Dispatch("Delete"));

        if (!isMulti)
        {
            AddSeparator(panel);
            AddHeader(panel, "Copy Path");
            AddMenuItem(panel, "📋 Absolute Path", () => Dispatch("CopyAbsolutePath"));
            AddMenuItem(panel, "📋 Relative Path", () => Dispatch("CopyRelativePath"));
            AddMenuItem(panel, "📋 File Name",     () => Dispatch("CopyFileName"));

            AddSeparator(panel);
            AddHeader(panel, "Open");
            AddMenuItem(panel, "📂 Open in Explorer", () => Dispatch("OpenInExplorer"));
            AddMenuItem(panel, "💻 Open in Terminal",  () => Dispatch("OpenInTerminal"));
        }

        AddSeparator(panel);
        AddHeader(panel, "Git");
        AddMenuItem(panel, "📝 Commit...",    () => Dispatch("GitCommit"));
        AddMenuItem(panel, "📜 Show History", () => Dispatch("GitHistory"));
        AddMenuItem(panel, "↩️ Revert...",    () => Dispatch("GitRevert"));

        AddSeparator(panel);
        AddHeader(panel, "Actions");
        AddMenuItem(panel, "🔄 Reload from Disk", () => Dispatch("Refresh"));
        if (!isMulti && !isFolder)
            AddMenuItem(panel, "⚙️ Properties", () => Dispatch("Properties"));
    }

    // ═══════════════════════════════════════════════════════════
    //  Action dispatcher
    // ═══════════════════════════════════════════════════════════

    private void Dispatch(string action)
    {
        Close();
        _mainWindow.ExecuteContextMenuAction(action);
    }

    // ═══════════════════════════════════════════════════════════
    //  UI helpers
    // ═══════════════════════════════════════════════════════════

    private static void AddHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text       = text,
            FontSize   = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFFAB387")),
            Margin     = new Thickness(0, 8, 0, 2)
        });
    }

    private static void AddSeparator(StackPanel panel)
    {
        panel.Children.Add(new Border
        {
            Height     = 1,
            Background = new SolidColorBrush(Color.Parse("#FF3D3D4D")),
            Margin     = new Thickness(0, 6, 0, 6)
        });
    }

    private void AddMenuItem(StackPanel panel, string text, Action action)
    {
        var btn = new Button { Classes = { "menu-item" } };

        btn.Content = new TextBlock
        {
            Text             = text,
            FontSize         = 13,
            Foreground       = new SolidColorBrush(Color.Parse("#FFCDD6F4")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        btn.Click += (_, _) => action();
        panel.Children.Add(btn);
    }
}
