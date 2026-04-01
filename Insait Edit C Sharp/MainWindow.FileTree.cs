// ============================================================
//  MainWindow.FileTree.cs  — partial class
//  Обробники подій файлового дерева (TreeView)
//  • FileTreeView_SelectionChanged
//  • FileTreeView_DoubleTapped
//  • FileTreeContextMenu_Opening
//  • Rubber-band (lasso) drag-selection — Rider style
// ============================================================

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Insait_Edit_C_Sharp.Models;
using Insait_Edit_C_Sharp.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow
{
    // ── Rubber-band state ────────────────────────────────────
    private bool _isDraggingSelection;
    private Point _dragStart;
    private bool _dragStartedOnItem;   // true → normal TreeView click, no rubber-band
    private bool _isAdjustingFileTreeSelection;
    private FileTreeItem? _contextMenuTargetItem;
    private PixelPoint _contextMenuAnchorScreenPoint;
    private ExplorerNodeMenuWindow? _activeNodeMenuWindow;

    private static bool IsSelectableTreeItem(FileTreeItem? item) => item?.IsSelectableInTree == true;

    private static FileTreeItem? GetTreeItemFromSource(object? source)
    {
        if (source is not Visual src)
            return null;

        var tvi = src.GetSelfAndVisualAncestors()
            .OfType<TreeViewItem>()
            .FirstOrDefault();

        return tvi?.DataContext as FileTreeItem;
    }

    private static void RemoveTreeSelectionItems(TreeView tree, IEnumerable<FileTreeItem> items)
    {
        if (tree.SelectedItems == null) return;

        foreach (var item in items.ToList())
        {
            item.IsSelected = false;
            tree.SelectedItems.Remove(item);
        }
    }

    private static void AddTreeSelectionItems(TreeView tree, IEnumerable<FileTreeItem> items)
    {
        if (tree.SelectedItems == null) return;

        foreach (var item in items.Where(IsSelectableTreeItem).Distinct().ToList())
        {
            item.IsSelected = true;
            if (!tree.SelectedItems.Contains(item))
                tree.SelectedItems.Add(item);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Panel pointer events — rubber-band drag selection
    // ═══════════════════════════════════════════════════════════

    private void FileTreePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only left-button, no modifier keys that would be normal selection
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        var panel = this.FindControl<Panel>("FileTreePanel");
        if (panel == null) return;

        _dragStart = e.GetPosition(panel);
        _dragStartedOnItem = false;

        // Check whether the press landed directly on a TreeViewItem row
        // If yes → let TreeView handle normal click/Ctrl/Shift selection; no rubber-band
        if (e.Source is Visual src)
        {
            var tvi = src.GetSelfAndVisualAncestors()
                         .OfType<TreeViewItem>()
                         .FirstOrDefault();
            _dragStartedOnItem = tvi != null;
        }

        // Capture the pointer so we receive moved/released even outside the control
        if (!_dragStartedOnItem)
        {
            _isDraggingSelection = false;   // will become true once movement starts
            e.Pointer.Capture(panel);
        }
    }

    private void FileTreePanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartedOnItem) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        var panel = this.FindControl<Panel>("FileTreePanel");
        if (panel == null) return;

        var current = e.GetPosition(panel);
        var delta = current - _dragStart;

        // Start rubber-band only after moving > 4 px (avoid accidental drags)
        if (!_isDraggingSelection && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4))
        {
            _isDraggingSelection = true;
            var canvas = this.FindControl<Canvas>("SelectionRectCanvas");
            if (canvas != null) canvas.IsVisible = true;
        }

        if (!_isDraggingSelection) return;

        UpdateSelectionRect(panel, _dragStart, current);
        SelectItemsInRect(panel, _dragStart, current, e.KeyModifiers);
    }

    private void FileTreePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStartedOnItem)
        {
            _dragStartedOnItem = false;
            return;
        }

        var panel = this.FindControl<Panel>("FileTreePanel");
        e.Pointer.Capture(null);

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            var canvas = this.FindControl<Canvas>("SelectionRectCanvas");
            if (canvas != null) canvas.IsVisible = false;

            // Do a final selection pass at the released position
            var current = e.GetPosition(panel);
            if (panel != null)
                SelectItemsInRect(panel, _dragStart, current, e.KeyModifiers);
        }

        _dragStartedOnItem = false;
        _isDraggingSelection = false;
    }

    // ── Draw the rubber-band rectangle ───────────────────────
    private void UpdateSelectionRect(Panel panel, Point a, Point b)
    {
        var canvas = this.FindControl<Canvas>("SelectionRectCanvas");
        var border = this.FindControl<Border>("SelectionRectBorder");
        if (canvas == null || border == null) return;

        // Make canvas fill the panel
        canvas.Width = panel.Bounds.Width;
        canvas.Height = panel.Bounds.Height;

        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X);
        double h = Math.Abs(b.Y - a.Y);

        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        border.Width = Math.Max(w, 1);
        border.Height = Math.Max(h, 1);
    }

    // ── Collect all TreeViewItems whose rows intersect the rect ──
    private void SelectItemsInRect(Panel panel, Point a, Point b, KeyModifiers modifiers)
    {
        var tree = this.FindControl<TreeView>("FileTreeView");
        if (tree == null) return;

        double y1 = Math.Min(a.Y, b.Y);
        double y2 = Math.Max(a.Y, b.Y);

        // Collect all visible TreeViewItems
        var allTvi = tree.GetVisualDescendants().OfType<TreeViewItem>().ToList();
        var toSelect = new List<FileTreeItem>();

        foreach (var tvi in allTvi)
        {
            if (tvi.DataContext is not FileTreeItem fi || !IsSelectableTreeItem(fi)) continue;

            // Get the row bounds relative to the panel
            var bounds = tvi.Bounds;
            // Translate from tvi's parent coordinate space to panel space
            try
            {
                var topLeft = tvi.TranslatePoint(new Point(0, 0), panel);
                var bottomRight = tvi.TranslatePoint(new Point(tvi.Bounds.Width, tvi.Bounds.Height), panel);
                if (topLeft == null || bottomRight == null) continue;

                double rowY1 = topLeft.Value.Y;
                double rowY2 = bottomRight.Value.Y;

                // Intersect vertically (horizontal span is always full width)
                if (rowY2 >= y1 && rowY1 <= y2)
                    toSelect.Add(fi);
            }
            catch { /* layout not ready */ }
        }

        // Apply selection:
        //  • Ctrl held  → add to existing selection
        //  • otherwise  → replace selection
        bool addToExisting = modifiers.HasFlag(KeyModifiers.Control);

        foreach (var root in _viewModel.FileTreeItems)
            ClearSelectionInTree(root, addToExisting ? (IEnumerable<FileTreeItem>)toSelect : null);

        foreach (var fi in toSelect)
            fi.IsSelected = true;

        // Sync the TreeView's SelectedItems list
        SyncTreeViewSelection(tree, toSelect, addToExisting);
    }

    // ── Clear IsSelected in the model tree ──────────────────
    private static void ClearSelectionInTree(FileTreeItem item, IEnumerable<FileTreeItem>? keep)
    {
        if (keep == null || !keep.Contains(item))
            item.IsSelected = false;
        foreach (var child in item.Children)
            ClearSelectionInTree(child, keep);
    }

    // ── Push new selection into TreeView.SelectedItems ──────
    private static void SyncTreeViewSelection(TreeView tree, List<FileTreeItem> toSelect, bool addToExisting)
    {
        if (!addToExisting)
            tree.SelectedItems?.Clear();

        if (tree.SelectedItems == null) return;
        foreach (var fi in toSelect.Where(IsSelectableTreeItem).Distinct())
        {
            if (!tree.SelectedItems.Contains(fi))
                tree.SelectedItems.Add(fi);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  FileTreeView — SelectionChanged
    //  Підтримує мультивибір (Ctrl+клік, Shift+клік, drag).
    //  При одиночному виборі відкриває файл.
    // ═══════════════════════════════════════════════════════════
    private void FileTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isAdjustingFileTreeSelection)
            return;

        if (sender is TreeView tree)
        {
            var invalidAdded = e.AddedItems
                .OfType<FileTreeItem>()
                .Where(item => !IsSelectableTreeItem(item))
                .ToList();

            if (invalidAdded.Count > 0)
            {
                var removedSelectable = e.RemovedItems
                    .OfType<FileTreeItem>()
                    .Where(IsSelectableTreeItem)
                    .ToList();

                var hasValidAdditions = e.AddedItems
                    .OfType<FileTreeItem>()
                    .Any(IsSelectableTreeItem);

                _isAdjustingFileTreeSelection = true;
                try
                {
                    RemoveTreeSelectionItems(tree, invalidAdded);

                    if (!hasValidAdditions && removedSelectable.Count > 0)
                        AddTreeSelectionItems(tree, removedSelectable);
                }
                finally
                {
                    _isAdjustingFileTreeSelection = false;
                }
            }
        }

        // Зняти позначку з попередньо вибраних елементів
        foreach (var removed in e.RemovedItems)
        {
            if (removed is FileTreeItem oldItem && IsSelectableTreeItem(oldItem))
                oldItem.IsSelected = false;
        }

        // Позначити нові вибрані елементи
        foreach (var added in e.AddedItems)
        {
            if (added is FileTreeItem newItem && IsSelectableTreeItem(newItem))
                newItem.IsSelected = true;
        }

        var allSelected = GetSelectedTreeItems();
        var count = allSelected.Count;
        if (count == 0) return;

        if (count == 1)
        {
            var single = allSelected[0];

            // Project/Solution nodes — just show path, don't open in editor
            bool isProjectNode = single.ItemType is FileTreeItemType.Solution
                              or FileTreeItemType.Project;

            _viewModel.StatusText = single.IsDirectory || isProjectNode
                ? "📁 " + single.FullPath
                : "📄 " + single.FullPath;

            if (!single.IsDirectory && !isProjectNode && File.Exists(single.FullPath))
                OpenFileInEditor(single.FullPath);
        }
        else
        {
            // Count only real files/folders, ignore project nodes in the summary
            var files = allSelected.Count(x => !x.IsDirectory
                && x.ItemType is not FileTreeItemType.Solution
                and not FileTreeItemType.Project);
            var folders = allSelected.Count(x => x.IsDirectory);
            var parts = new List<string>();
            if (files > 0) parts.Add(files + " file" + (files > 1 ? "s" : ""));
            if (folders > 0) parts.Add(folders + " folder" + (folders > 1 ? "s" : ""));
            _viewModel.StatusText = "🗂️ " + count + " items selected (" + string.Join(", ", parts) + ")";
        }
    }

    private void FileTreeView_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsRightButtonPressed)
            return;

        _contextMenuAnchorScreenPoint = this.PointToScreen(e.GetPosition(this));

        var item = GetTreeItemFromSource(e.Source);
        _contextMenuTargetItem = item;

        if (item == null || !IsSelectableTreeItem(item) || sender is not TreeView tree)
            return;

        var selectedItems = tree.SelectedItems?.OfType<FileTreeItem>().ToList() ?? new List<FileTreeItem>();
        if (selectedItems.Any(selected => ReferenceEquals(selected, item) || PathsEqual(selected.FullPath, item.FullPath)))
            return;

        _isAdjustingFileTreeSelection = true;
        try
        {
            foreach (var root in _viewModel.FileTreeItems)
                ClearSelectionInTree(root, new[] { item });

            item.IsSelected = true;
            SyncTreeViewSelection(tree, new List<FileTreeItem> { item }, addToExisting: false);
        }
        finally
        {
            _isAdjustingFileTreeSelection = false;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  FileTreeView — DoubleTapped
    // ═══════════════════════════════════════════════════════════
    private void FileTreeView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeItemFromSource(e.Source) ?? GetSelectedTreeItem();
        if (item == null) return;

        if (item.IsDirectory)
            item.IsExpanded = !item.IsExpanded;
        else
            OpenFileInEditor(item.FullPath);
    }

    // ═══════════════════════════════════════════════════════════
    //  FileTreeContextMenu — Opening
    // ═══════════════════════════════════════════════════════════
    private void FileTreeContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        var allSelected = GetSelectedTreeItems();
        var count = allSelected.Count;
        var item = allSelected.FirstOrDefault();

        bool isMulti = count > 1;

        if (!isMulti && item?.ItemType is FileTreeItemType.Solution or FileTreeItemType.Project)
        {
            e.Cancel = true;
            ShowExplorerNodeMenuWindow(item);
            return;
        }

        // ── Multi-selection info header ──────────────────────
        var infoLabel = this.FindControl<MenuItem>("ContextMenuMultiInfo");
        var infoSep = this.FindControl<Control>("ContextMenuMultiInfoSeparator");
        var infoText = this.FindControl<TextBlock>("ContextMenuMultiInfoText");
        if (infoLabel != null) infoLabel.IsVisible = isMulti;
        if (infoSep != null) infoSep.IsVisible = isMulti;
        if (infoText != null && isMulti)
        {
            var files = allSelected.Count(x => !x.IsDirectory);
            var folders = allSelected.Count(x => x.IsDirectory);
            var parts = new List<string>();
            if (files > 0) parts.Add(files + " file" + (files > 1 ? "s" : ""));
            if (folders > 0) parts.Add(folders + " folder" + (folders > 1 ? "s" : ""));
            infoText.Text = count + " items selected — " + string.Join(", ", parts);
        }

        bool isSolution = item?.ItemType is FileTreeItemType.Solution or FileTreeItemType.SolutionFolder;
        bool isProject = item?.ItemType is FileTreeItemType.Project;
        bool isFolder = item?.IsDirectory == true && !isSolution && !isProject;
        bool isFile = item != null && !item.IsDirectory;
        bool hasProject = isSolution || isProject;

        bool multiEditable = isMulti && allSelected.All(x =>
            x.ItemType is not FileTreeItemType.Solution and
                         not FileTreeItemType.SolutionFolder);

        SetMenuItemVisible("ContextMenuRun", isProject && !isMulti);
        SetMenuItemVisible("ContextMenuRunSeparator", isProject && !isMulti);

        SetMenuItemVisible("ContextMenuNew", !isMulti && (isFile || isFolder || isProject));
        SetMenuItemVisible("ContextMenuAdd", !isMulti && (hasProject || isSolution));
        SetMenuItemVisible("ContextMenuAddSeparator", !isMulti && (hasProject || isSolution));

        SetMenuItemVisible("ContextMenuBuild", hasProject && !isMulti);
        SetMenuItemVisible("ContextMenuRebuild", hasProject && !isMulti);
        SetMenuItemVisible("ContextMenuClean", hasProject && !isMulti);
        SetMenuItemVisible("ContextMenuBuildSeparator", hasProject && !isMulti);

        SetMenuItemVisible("ContextMenuNuGet", isProject && !isMulti);
        SetMenuItemVisible("ContextMenuAddReference", isProject && !isMulti);
        SetMenuItemVisible("ContextMenuNuGetSeparator", isProject && !isMulti);

        SetMenuItemVisible("ContextMenuRemoveFromSolution", isProject && !isMulti);
        SetMenuItemVisible("ContextMenuUnloadProject", isProject && !isMulti);
        SetMenuItemVisible("ContextMenuRemoveSeparator", isProject && !isMulti);

        bool canEdit = isFile || isFolder || isSolution || isProject || (isMulti && multiEditable);
        SetMenuItemVisible("ContextMenuCut", canEdit);
        SetMenuItemVisible("ContextMenuCopy", canEdit);
        SetMenuItemVisible("ContextMenuPaste", !isMulti && (isFolder || isSolution || isProject));

        SetMenuItemVisible("ContextMenuRename", !isMulti && (isFile || isFolder));

        var deleteMenu = this.FindControl<MenuItem>("ContextMenuDelete");
        if (deleteMenu != null)
        {
            deleteMenu.Header = isMulti ? "🗑️ Delete " + count + " Items..." : "🗑️ Safe Delete...";
            deleteMenu.IsVisible = isFile || isFolder || isSolution || isProject || (isMulti && multiEditable);
        }

        SetMenuItemVisible("ContextMenuCopyPath", !isMulti && (isFile || isFolder));
        SetMenuItemVisible("ContextMenuOpenExplorer", !isMulti && (isFile || isFolder));
        SetMenuItemVisible("ContextMenuOpenTerminal", !isMulti && (isFile || isFolder));
        SetMenuItemVisible("ContextMenuProperties", !isMulti && (isProject || isFile));
        SetMenuItemVisible("ContextMenuGit", !isMulti && item != null);
    }

    private void ShowExplorerNodeMenuWindow(FileTreeItem item)
    {
        _contextMenuTargetItem = item;

        _activeNodeMenuWindow?.Close();

        var isSolution = item.ItemType == FileTreeItemType.Solution;
        var window = new ExplorerNodeMenuWindow(
            isSolution ? "🏠" : "📦",
            isSolution ? $"Solution · {item.Name}" : $"Project · {item.Name}",
            BuildExplorerNodeMenuActions(item));

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeNodeMenuWindow, window))
                _activeNodeMenuWindow = null;

            if (ReferenceEquals(_contextMenuTargetItem, item))
                _contextMenuTargetItem = null;
        };

        _activeNodeMenuWindow = window;
        window.Position = new PixelPoint(_contextMenuAnchorScreenPoint.X + 8, _contextMenuAnchorScreenPoint.Y + 8);
        window.Show(this);
    }

    private IReadOnlyList<ExplorerNodeMenuAction> BuildExplorerNodeMenuActions(FileTreeItem item)
    {
        Task Invoke(Action action)
        {
            _contextMenuTargetItem = item;
            action();
            return Task.CompletedTask;
        }

        Task InvokeAsync(Func<Task> action)
        {
            _contextMenuTargetItem = item;
            return action();
        }

        var actions = new List<ExplorerNodeMenuAction>();

        if (item.ItemType == FileTreeItemType.Solution)
        {
            actions.Add(new ExplorerNodeMenuAction("Open", "📄 Open Solution File", "Open the .sln / .slnx file in the editor", () => Invoke(() => OpenFileInEditor(item.FullPath))));
            actions.Add(new ExplorerNodeMenuAction("Add", "📦 Add New Project...", "Create a project inside the current solution", () => Invoke(() => AddNewProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add", "📦 Add Existing Project...", "Attach an existing project to the solution", () => Invoke(() => ContextMenu_AddExistingProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add", "📄 Add New Item...", "Create a new file in the solution folder", () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add", "📄 Add Existing Item...", "Copy an existing file into the solution folder", () => Invoke(() => ContextMenu_AddExistingItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Build", "🔨 Build Solution", "Build the selected solution", () => InvokeAsync(() => { ContextMenu_BuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Build", "🔄 Rebuild Solution", "Clean and build the solution again", () => InvokeAsync(() => { ContextMenu_RebuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Build", "🧹 Clean Solution", "Remove build artifacts for the solution", () => InvokeAsync(() => { ContextMenu_CleanProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Edit", "✂️ Cut", "Copy selected solution path for move operation", () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit", "📋 Copy", "Copy selected solution path", () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit", "📄 Paste", "Paste into the solution folder", () => Invoke(() => ContextMenu_Paste_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", "📝 Git Commit...", "Open the Git window", () => Invoke(() => ContextMenu_GitCommit_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", "📜 Git History", "Show history in the Git window", () => Invoke(() => ContextMenu_GitHistory_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", "↩️ Git Revert...", "Open revert options in the Git window", () => Invoke(() => ContextMenu_GitRevert_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Solution", "🔄 Reload from Disk", "Refresh the explorer tree", () => Invoke(() => RefreshTree_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Solution", "⚙️ Properties", "Open solution properties", () => Invoke(() => ContextMenu_Properties_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Solution", "🗑️ Safe Delete...", "Delete the solution/workspace", () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true));
            return actions;
        }

        actions.Add(new ExplorerNodeMenuAction("Run", "▶ Run Project", "Run the selected project", () => Invoke(() => ContextMenu_RunProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "📄 New Class...", null, () => Invoke(() => ContextMenu_NewClass_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "📄 New Interface...", null, () => Invoke(() => ContextMenu_NewInterface_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "📄 New Record...", null, () => Invoke(() => ContextMenu_NewRecord_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "📄 New Enum...", null, () => Invoke(() => ContextMenu_NewEnum_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "🪟 New Avalonia Window...", null, () => Invoke(() => ContextMenu_NewAvaloniaWindow_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "🎛️ New Avalonia UserControl...", null, () => Invoke(() => ContextMenu_NewAvaloniaUserControl_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "📄 New File...", null, () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create", "📁 New Directory", null, () => Invoke(() => AddNewFolder_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Add", "📦 Add New Project...", null, () => Invoke(() => AddNewProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Add", "📦 Add Existing Project...", null, () => Invoke(() => ContextMenu_AddExistingProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Add", "📄 Add New Item...", null, () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Add", "📄 Add Existing Item...", null, () => Invoke(() => ContextMenu_AddExistingItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Build", "🔨 Build Project", null, () => InvokeAsync(() => { ContextMenu_BuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
        actions.Add(new ExplorerNodeMenuAction("Build", "🔄 Rebuild Project", null, () => InvokeAsync(() => { ContextMenu_RebuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
        actions.Add(new ExplorerNodeMenuAction("Build", "🧹 Clean Project", null, () => InvokeAsync(() => { ContextMenu_CleanProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
        actions.Add(new ExplorerNodeMenuAction("Dependencies", "📦 Manage NuGet Packages...", null, () => Invoke(() => ContextMenu_ManageNuGet_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Dependencies", "🔗 Add Reference...", null, () => Invoke(() => ContextMenu_AddReference_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit", "✂️ Cut", null, () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit", "📋 Copy", null, () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit", "📄 Paste", null, () => Invoke(() => ContextMenu_Paste_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Source Control", "📝 Git Commit...", null, () => Invoke(() => ContextMenu_GitCommit_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Source Control", "📜 Git History", null, () => Invoke(() => ContextMenu_GitHistory_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Source Control", "↩️ Git Revert...", null, () => Invoke(() => ContextMenu_GitRevert_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Project", "🗑️ Remove from Solution", null, () => Invoke(() => ContextMenu_RemoveFromSolution_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Project", "⬇️ Unload Project", null, () => Invoke(() => ContextMenu_UnloadProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Project", "🔄 Reload from Disk", null, () => Invoke(() => RefreshTree_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Project", "⚙️ Properties", null, () => Invoke(() => ContextMenu_Properties_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Project", "🗑️ Safe Delete...", null, () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true));

        return actions;
    }


    // ── Допоміжний: показати/приховати пункт меню ───────────
    private void SetMenuItemVisible(string name, bool visible)
    {
        var ctrl = this.FindControl<Control>(name);
        if (ctrl != null) ctrl.IsVisible = visible;
    }
}
