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
using Insait_Edit_C_Sharp.Services;
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

    private static string L(string key) => LocalizationService.Get(key);

    private static string LF(string key, params object[] args) => string.Format(L(key), args);

    private static string FormatLocalizedCount(int count, string singularKey, string pluralKey)
    {
        var format = count == 1 ? L(singularKey) : L(pluralKey);
        return string.Format(format, count);
    }

    private static string BuildLocalizedSelectionSummary(int files, int folders)
    {
        var parts = new List<string>();
        if (files > 0)
            parts.Add(FormatLocalizedCount(files, "ExplorerNodeMenu.Count.File.One", "ExplorerNodeMenu.Count.File.Many"));
        if (folders > 0)
            parts.Add(FormatLocalizedCount(folders, "ExplorerNodeMenu.Count.Folder.One", "ExplorerNodeMenu.Count.Folder.Many"));

        return parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : string.Empty;
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


        var item = GetTreeItemFromSource(e.Source);
        _contextMenuTargetItem = item;

        // Solution / Project nodes are NOT in SelectedItems (IsSelectableInTree == false).
        // Do NOT touch the current file/folder selection when clicking these nodes —
        // each element type is handled independently.
        if (item == null || !IsSelectableTreeItem(item))
            return;

        if (sender is not TreeView tree)
            return;

        // If the right-clicked file/folder is already part of the current selection,
        // keep the whole selection intact (enables multi-select context menu).
        var selectedItems = tree.SelectedItems?.OfType<FileTreeItem>().ToList() ?? new List<FileTreeItem>();
        if (selectedItems.Any(sel => ReferenceEquals(sel, item) || PathsEqual(sel.FullPath, item.FullPath)))
            return;

        // Otherwise select only the right-clicked item.
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
    //  All node types are now handled by ExplorerNodeMenuWindow.
    //  The native ContextMenu is always cancelled.
    // ═══════════════════════════════════════════════════════════
    private void FileTreeContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;

        // _contextMenuTargetItem is set in FileTreeView_PointerPressed (right-click).
        // Because we registered with handledEventsToo: true, it should always be set.
        // As a fallback, read the TreeView's current selection.
        var item        = _contextMenuTargetItem;
        var allSelected = GetSelectedTreeItems();

        // If the clicked node is not already in the selection, keep it as the anchor.
        if (allSelected.Count == 0 && item != null)
            allSelected = new List<FileTreeItem> { item };

        ShowExplorerNodeMenuWindow(item, allSelected);
    }

    // ── Show the floating ExplorerNodeMenuWindow ──────────────────
    //
    //  Rules:
    //  • Solution node  → always Solution page (ignores other selection)
    //  • Project node   → always Project page  (ignores other selection)
    //  • Files/Folders  → individual page when 1 item; MultiSelection when
    //                     the user has Ctrl-selected ≥ 2 real files/folders
    //  • Special nodes (DependenciesFolder, NuGetPackage, SolutionFolder)
    //                   are excluded from multi-select counting.
    //  • Window is centred on screen (CenterScreen in AXAML).
    // ──────────────────────────────────────────────────────────────
    private void ShowExplorerNodeMenuWindow(FileTreeItem? item, IReadOnlyList<FileTreeItem>? allSelected = null)
    {
        if (item == null && (allSelected == null || allSelected.Count == 0))
            return;

        _activeNodeMenuWindow?.Close();

        // ── Solution: own page, always ────────────────────────────
        if (item?.ItemType == FileTreeItemType.Solution)
        {
            _contextMenuTargetItem = item;
            ShowWindow("🏠", LF("ExplorerNodeMenu.PageTitle.Solution", item.Name),
                ExplorerNodeMenuPageType.Solution,
                BuildExplorerNodeMenuActions(item), item.FullPath, item);
            return;
        }

        // ── Project: own page, always ─────────────────────────────
        if (item?.ItemType == FileTreeItemType.Project)
        {
            _contextMenuTargetItem = item;
            ShowWindow("📦", LF("ExplorerNodeMenu.PageTitle.Project", item.Name),
                ExplorerNodeMenuPageType.Project,
                BuildExplorerNodeMenuActions(item), item.FullPath, item);
            return;
        }

        // ── Files / Folders ───────────────────────────────────────
        // Filter out solution/project/special nodes — only count real
        // files and regular directories for multi-select purposes.
        static bool IsRealFileOrFolder(FileTreeItem x) =>
            x.ItemType is not FileTreeItemType.Solution
                       and not FileTreeItemType.SolutionFolder
                       and not FileTreeItemType.Project
                       and not FileTreeItemType.DependenciesFolder
                       and not FileTreeItemType.NuGetPackage;

        var fileItems = (allSelected ?? new List<FileTreeItem>())
            .Where(IsRealFileOrFolder)
            .ToList();

        // Make sure the right-clicked item itself is in the list
        if (item != null && IsRealFileOrFolder(item) && !fileItems.Contains(item))
            fileItems.Insert(0, item);

        if (fileItems.Count == 0)
            return;

        _contextMenuTargetItem = fileItems[0];

        // Multi-selection only when the user explicitly selected ≥2 items
        if (fileItems.Count > 1)
        {
            var files   = fileItems.Count(x => !x.IsDirectory);
            var folders = fileItems.Count(x =>  x.IsDirectory);
            var summary = BuildLocalizedSelectionSummary(files, folders);
            ShowWindow("📋", LF("ExplorerNodeMenu.PageTitle.MultiSelection", fileItems.Count, summary),
                ExplorerNodeMenuPageType.MultiSelection,
                BuildMultiSelectionMenuActions(fileItems), null, fileItems[0]);
            return;
        }

        // Single file / folder
        var target = fileItems[0];
        if (target.IsDirectory)
            ShowWindow("📁", LF("ExplorerNodeMenu.PageTitle.Folder", target.Name), ExplorerNodeMenuPageType.Folder,
                BuildExplorerNodeMenuActions(target), target.FullPath, target);
        else
            ShowWindow("📄", LF("ExplorerNodeMenu.PageTitle.File", target.Name), ExplorerNodeMenuPageType.File,
                BuildExplorerNodeMenuActions(target), target.FullPath, target);
    }

    // ── Helper: create, register and display the window ──────────
    private void ShowWindow(
        string titleIcon, string title,
        ExplorerNodeMenuPageType pageType,
        IReadOnlyList<ExplorerNodeMenuAction> actions,
        string? itemPath,
        FileTreeItem? ownerItem)
    {
        var window = new ExplorerNodeMenuWindow(titleIcon, title, pageType, actions, itemPath);

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeNodeMenuWindow, window))
                _activeNodeMenuWindow = null;
            if (ownerItem != null && ReferenceEquals(_contextMenuTargetItem, ownerItem))
                _contextMenuTargetItem = null;
            Activate();  // restore focus to main window
        };

        _activeNodeMenuWindow = window;
        window.Show();   // show without owner to avoid blocking appearance
    }

    // ── Build actions: Solution page ─────────────────────────────
    // ── Build actions: Project page ──────────────────────────────
    // ── Build actions: File page ─────────────────────────────────
    // ── Build actions: Folder page ───────────────────────────────
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

        // ════════════════════════════════════
        //  PAGE: Solution
        // ════════════════════════════════════
        if (item.ItemType == FileTreeItemType.Solution)
        {
            actions.Add(new ExplorerNodeMenuAction("Open",           L("ExplorerNodeMenu.Action.OpenSolutionFile"),  L("ExplorerNodeMenu.Subtitle.OpenSolutionFile"),      () => Invoke(() => OpenFileInEditor(item.FullPath))));
            actions.Add(new ExplorerNodeMenuAction("Add",            L("Context.AddNewProject"),                     L("ExplorerNodeMenu.Subtitle.AddNewProject"),        () => Invoke(() => AddNewProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",            L("Context.AddExistingProject"),                L("ExplorerNodeMenu.Subtitle.AddExistingProject"),   () => Invoke(() => ContextMenu_AddExistingProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",            L("Context.AddNewItem"),                        L("ExplorerNodeMenu.Subtitle.AddNewItem"),           () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",            L("Context.AddExistingItem"),                   L("ExplorerNodeMenu.Subtitle.AddExistingItem"),      () => Invoke(() => ContextMenu_AddExistingItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Build",          L("ExplorerNodeMenu.Action.BuildSolution"),     L("ExplorerNodeMenu.Subtitle.BuildSolution"),        () => InvokeAsync(() => { ContextMenu_BuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Build",          L("ExplorerNodeMenu.Action.RebuildSolution"),   L("ExplorerNodeMenu.Subtitle.RebuildSolution"),      () => InvokeAsync(() => { ContextMenu_RebuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Build",          L("ExplorerNodeMenu.Action.CleanSolution"),     L("ExplorerNodeMenu.Subtitle.CleanSolution"),        () => InvokeAsync(() => { ContextMenu_CleanProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Cut"),                               L("ExplorerNodeMenu.Subtitle.CutSolutionPath"),      () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Copy"),                              L("ExplorerNodeMenu.Subtitle.CopySolutionPath"),     () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Paste"),                             L("ExplorerNodeMenu.Subtitle.PasteIntoSolutionFolder"), () => Invoke(() => ContextMenu_Paste_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", L("Context.GitCommit"),                         L("ExplorerNodeMenu.Subtitle.OpenGitWindow"),        () => Invoke(() => ContextMenu_GitCommit_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", L("Context.GitHistory"),                        L("ExplorerNodeMenu.Subtitle.ShowGitHistory"),       () => Invoke(() => ContextMenu_GitHistory_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", L("Context.GitRevert"),                         L("ExplorerNodeMenu.Subtitle.OpenGitRevert"),        () => Invoke(() => ContextMenu_GitRevert_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Solution",       L("Context.ReloadFromDisk"),                    L("ExplorerNodeMenu.Subtitle.ReloadFromDisk"),       () => Invoke(() => RefreshTree_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Solution",       L("Context.Properties"),                        L("ExplorerNodeMenu.Subtitle.OpenSolutionProperties"), () => Invoke(() => ContextMenu_Properties_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Solution",       L("Context.Delete"),                            L("ExplorerNodeMenu.Subtitle.DeleteSolutionWorkspace"), () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true));
            return actions;
        }

        // ════════════════════════════════════
        //  PAGE: Project
        // ════════════════════════════════════
        if (item.ItemType == FileTreeItemType.Project)
        {
            actions.Add(new ExplorerNodeMenuAction("Run",          L("ExplorerNodeMenu.Action.RunProject"),          L("ExplorerNodeMenu.Subtitle.RunProject"),            () => Invoke(() => ContextMenu_RunProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewClass"),                            null,                                                  () => Invoke(() => ContextMenu_NewClass_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewInterface"),                        null,                                                  () => Invoke(() => ContextMenu_NewInterface_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewRecord"),                           null,                                                  () => Invoke(() => ContextMenu_NewRecord_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewEnum"),                             null,                                                  () => Invoke(() => ContextMenu_NewEnum_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewAvaloniaWindow"),                   null,                                                  () => Invoke(() => ContextMenu_NewAvaloniaWindow_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewAvaloniaControl"),                  null,                                                  () => Invoke(() => ContextMenu_NewAvaloniaUserControl_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("ExplorerNodeMenu.Action.NewFile"),             null,                                                  () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Create",       L("ExplorerNodeMenu.Action.NewDirectory"),        null,                                                  () => Invoke(() => AddNewFolder_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",          L("Context.AddNewProject"),                       null,                                                  () => Invoke(() => AddNewProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",          L("Context.AddExistingProject"),                  null,                                                  () => Invoke(() => ContextMenu_AddExistingProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",          L("Context.AddNewItem"),                          null,                                                  () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Add",          L("Context.AddExistingItem"),                     null,                                                  () => Invoke(() => ContextMenu_AddExistingItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Build",        L("Context.Build"),                               null,                                                  () => InvokeAsync(() => { ContextMenu_BuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Build",        L("Context.Rebuild"),                             null,                                                  () => InvokeAsync(() => { ContextMenu_RebuildProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Build",        L("Context.Clean"),                               null,                                                  () => InvokeAsync(() => { ContextMenu_CleanProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()); return Task.CompletedTask; })));
            actions.Add(new ExplorerNodeMenuAction("Dependencies",  L("Context.ManageNuGet"),                         null,                                                  () => Invoke(() => ContextMenu_ManageNuGet_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Dependencies",  L("Context.AddReference"),                        null,                                                  () => Invoke(() => ContextMenu_AddReference_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Cut"),                                 null,                                                  () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Copy"),                                null,                                                  () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Paste"),                               null,                                                  () => Invoke(() => ContextMenu_Paste_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control",L("Context.GitCommit"),                           null,                                                  () => Invoke(() => ContextMenu_GitCommit_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control",L("Context.GitHistory"),                          null,                                                  () => Invoke(() => ContextMenu_GitHistory_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control",L("Context.GitRevert"),                           null,                                                  () => Invoke(() => ContextMenu_GitRevert_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Project",      L("Context.RemoveFromSolution"),                  null,                                                  () => Invoke(() => ContextMenu_RemoveFromSolution_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Project",      L("Context.UnloadProject"),                       null,                                                  () => Invoke(() => ContextMenu_UnloadProject_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Project",      L("Context.ReloadFromDisk"),                      null,                                                  () => Invoke(() => RefreshTree_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Project",      L("Context.Properties"),                          null,                                                  () => Invoke(() => ContextMenu_Properties_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Project",      L("Context.Delete"),                              null,                                                  () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true));
            return actions;
        }

        // ════════════════════════════════════
        //  PAGE: File
        // ════════════════════════════════════
        if (!item.IsDirectory)
        {
            actions.Add(new ExplorerNodeMenuAction("Open",           L("ExplorerNodeMenu.Action.OpenInEditor"),      L("ExplorerNodeMenu.Subtitle.OpenInEditor"),         () => Invoke(() => OpenFileInEditor(item.FullPath))));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Cut"),                               null,                                                  () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Copy"),                              null,                                                  () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Rename"),                            null,                                                  () => Invoke(() => RenameItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Edit",           L("Context.Delete"),                            L("ExplorerNodeMenu.Subtitle.DeleteFileSafely"),      () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true));
            actions.Add(new ExplorerNodeMenuAction("Navigate",       L("Context.OpenExplorer"),                      null,                                                  () => Invoke(() => OpenInExplorer_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Navigate",       L("Context.OpenTerminal"),                      null,                                                  () => Invoke(() => OpenInTerminal_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Navigate",       L("Context.AbsolutePath"),                      null,                                                  () => Invoke(() => CopyPath_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Navigate",       L("Context.RelativePath"),                      null,                                                  () => Invoke(() => ContextMenu_CopyRelativePath_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Navigate",       L("Context.FileName"),                          null,                                                  () => Invoke(() => ContextMenu_CopyFileName_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", L("Context.GitCommit"),                         null,                                                  () => Invoke(() => ContextMenu_GitCommit_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", L("Context.GitHistory"),                        null,                                                  () => Invoke(() => ContextMenu_GitHistory_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("Source Control", L("Context.GitRevert"),                         null,                                                  () => Invoke(() => ContextMenu_GitRevert_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("File",           L("Context.ReloadFromDisk"),                    null,                                                  () => Invoke(() => RefreshTree_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            actions.Add(new ExplorerNodeMenuAction("File",           L("Context.Properties"),                        null,                                                  () => Invoke(() => ContextMenu_Properties_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
            return actions;
        }

        // ════════════════════════════════════
        //  PAGE: Folder
        // ════════════════════════════════════
        actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewClass"),                            null,                                                  () => Invoke(() => ContextMenu_NewClass_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewInterface"),                        null,                                                  () => Invoke(() => ContextMenu_NewInterface_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewRecord"),                           null,                                                  () => Invoke(() => ContextMenu_NewRecord_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewEnum"),                             null,                                                  () => Invoke(() => ContextMenu_NewEnum_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewAvaloniaWindow"),                   null,                                                  () => Invoke(() => ContextMenu_NewAvaloniaWindow_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("Context.NewAvaloniaControl"),                  null,                                                  () => Invoke(() => ContextMenu_NewAvaloniaUserControl_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("ExplorerNodeMenu.Action.NewFile"),             null,                                                  () => Invoke(() => AddNewItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Create",       L("ExplorerNodeMenu.Action.NewSubDirectory"),     null,                                                  () => Invoke(() => AddNewFolder_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Cut"),                                 null,                                                  () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Copy"),                                null,                                                  () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Paste"),                               null,                                                  () => Invoke(() => ContextMenu_Paste_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Rename"),                              null,                                                  () => Invoke(() => RenameItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Edit",         L("Context.Delete"),                              L("ExplorerNodeMenu.Subtitle.DeleteFolderSafely"),    () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true));
        actions.Add(new ExplorerNodeMenuAction("Navigate",     L("Context.OpenExplorer"),                        null,                                                  () => Invoke(() => OpenInExplorer_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Navigate",     L("Context.OpenTerminal"),                        null,                                                  () => Invoke(() => OpenInTerminal_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Navigate",     L("Context.AbsolutePath"),                        null,                                                  () => Invoke(() => CopyPath_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Navigate",     L("Context.RelativePath"),                        null,                                                  () => Invoke(() => ContextMenu_CopyRelativePath_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Source Control",L("Context.GitCommit"),                           null,                                                  () => Invoke(() => ContextMenu_GitCommit_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Source Control",L("Context.GitHistory"),                          null,                                                  () => Invoke(() => ContextMenu_GitHistory_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Source Control",L("Context.GitRevert"),                           null,                                                  () => Invoke(() => ContextMenu_GitRevert_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        actions.Add(new ExplorerNodeMenuAction("Folder",       L("Context.ReloadFromDisk"),                      null,                                                  () => Invoke(() => RefreshTree_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))));
        return actions;
    }

    // ── Build actions: MultiSelection page ───────────────────────
    private IReadOnlyList<ExplorerNodeMenuAction> BuildMultiSelectionMenuActions(IReadOnlyList<FileTreeItem> items)
    {
        _contextMenuTargetItem = items.FirstOrDefault();

        Task Invoke(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        var count = items.Count;
        var actions = new List<ExplorerNodeMenuAction>
        {
            new("Edit", L("Context.Cut"), LF("ExplorerNodeMenu.Subtitle.CutSelectedItems", count), () => Invoke(() => ContextMenu_Cut_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))),
            new("Edit", L("Context.Copy"), LF("ExplorerNodeMenu.Subtitle.CopySelectedItems", count), () => Invoke(() => ContextMenu_Copy_Click(this, new Avalonia.Interactivity.RoutedEventArgs()))),
            new("Edit", LF("ExplorerNodeMenu.Action.DeleteSelectedItems", count), LF("ExplorerNodeMenu.Subtitle.DeleteSelectedItems", count), () => Invoke(() => DeleteItem_Click(this, new Avalonia.Interactivity.RoutedEventArgs())), isDestructive: true)
        };
        return actions;
    }


    // ── Допоміжний: показати/приховати пункт меню ───────────
    private void SetMenuItemVisible(string name, bool visible)
    {
        var ctrl = this.FindControl<Control>(name);
        if (ctrl != null) ctrl.IsVisible = visible;
    }
}
