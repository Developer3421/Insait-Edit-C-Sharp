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
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Insait_Edit_C_Sharp;

public partial class MainWindow
{
    // ── Rubber-band state ────────────────────────────────────
    private bool _isDraggingSelection;
    private Point _dragStart;
    private bool _dragStartedOnItem;   // true → normal TreeView click, no rubber-band
    private bool _isAdjustingFileTreeSelection;

    // ── Last right-click screen position (for ExplorerNodeMenuWindow placement) ──
    private PixelPoint _lastContextMenuPixelPoint;

    private static bool IsSelectableTreeItem(FileTreeItem? item) => item?.IsSelectableInTree == true;

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
        // Track right-click screen position so FileTreeContextMenu_Opening can place ExplorerNodeMenuWindow
        if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            _lastContextMenuPixelPoint = this.PointToScreen(e.GetPosition(this));
            return; // let the ContextMenu mechanism handle the rest
        }

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

    // ═══════════════════════════════════════════════════════════
    //  FileTreeView — DoubleTapped
    // ═══════════════════════════════════════════════════════════
    private void FileTreeView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        FileTreeItem? item = null;

        if (e.Source is Visual src2)
        {
            var tvi = src2.GetSelfAndVisualAncestors()
                          .OfType<TreeViewItem>()
                          .FirstOrDefault();
            if (tvi?.DataContext is FileTreeItem fi) item = fi;
        }

        item ??= GetSelectedTreeItem();
        if (item == null) return;

        if (item.IsDirectory) item.IsExpanded = !item.IsExpanded;
        else OpenFileInEditor(item.FullPath);
    }

    // ═══════════════════════════════════════════════════════════
    //  FileTreeContextMenu — Opening
    //  Cancels the native Avalonia ContextMenu and opens the
    //  dedicated ExplorerNodeMenuWindow instead.
    // ═══════════════════════════════════════════════════════════
    private void FileTreeContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        // Prevent the native ContextMenu from appearing
        e.Cancel = true;

        var allSelected = GetSelectedTreeItems();
        var item        = allSelected.FirstOrDefault();

        var win = new ExplorerNodeMenuWindow(this, item, allSelected);

        // Position the window near the right-click point, clamped to screen bounds
        var screenBounds = Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        int x = _lastContextMenuPixelPoint.X;
        int y = _lastContextMenuPixelPoint.Y;
        x = Math.Max(screenBounds.X, Math.Min(x, screenBounds.Right  - (int)win.Width));
        y = Math.Max(screenBounds.Y, Math.Min(y, screenBounds.Bottom - (int)win.Height));
        win.Position = new PixelPoint(x, y);

        _ = win.ShowDialog(this);
    }
}
