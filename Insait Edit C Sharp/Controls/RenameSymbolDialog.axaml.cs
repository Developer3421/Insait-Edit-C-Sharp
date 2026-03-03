using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Dialog for renaming a symbol using Roslyn Renamer API.
/// Launched via F2 / Ctrl+R,R from InsaitEditor.
/// Shows preview of affected locations before applying.
/// </summary>
public partial class RenameSymbolDialog : Window
{
    private readonly CSharpCompletionService _completionService;
    private readonly string _filePath;
    private readonly string _sourceCode;
    private readonly int _position;
    private readonly string _oldName;
    private RenameResult? _renameResult;
    private CancellationTokenSource? _previewCts;

    /// <summary>Fired when rename is confirmed with the result.</summary>
    public event EventHandler<RenameResult>? RenameConfirmed;

    public RenameSymbolDialog()
    {
        InitializeComponent();
        _completionService = new CSharpCompletionService();
        _filePath = string.Empty;
        _sourceCode = string.Empty;
        _oldName = string.Empty;
    }

    public RenameSymbolDialog(
        CSharpCompletionService completionService,
        string filePath,
        string sourceCode,
        int position,
        string oldName)
    {
        InitializeComponent();
        _completionService = completionService;
        _filePath = filePath;
        _sourceCode = sourceCode;
        _position = position;
        _oldName = oldName;

        OldNameText.Text = $"Current: {oldName}";
        NewNameInput.Text = oldName;
        NewNameInput.SelectAll();

        // Live preview on text change
        NewNameInput.TextChanged += async (_, _) => await UpdatePreviewAsync();

        // Focus input when loaded
        this.Opened += (_, _) =>
        {
            NewNameInput.Focus();
            NewNameInput.SelectAll();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async Task UpdatePreviewAsync()
    {
        var newName = NewNameInput?.Text?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == _oldName)
        {
            PreviewPanel?.Children.Clear();
            _renameResult = null;
            return;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            var result = await _completionService.RenameSymbolAsync(
                _filePath, _sourceCode, _position, newName, ct);

            if (ct.IsCancellationRequested) return;
            _renameResult = result;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                PreviewPanel.Children.Clear();
                if (result == null || !result.Changes.Any())
                {
                    PreviewPanel.Children.Add(new TextBlock
                    {
                        Text = "No changes found",
                        Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0")),
                        FontSize = 11, Margin = new Thickness(4)
                    });
                    return;
                }

                // Group changes by file
                var byFile = result.Changes.GroupBy(c => c.FilePath);
                foreach (var group in byFile)
                {
                    PreviewPanel.Children.Add(new TextBlock
                    {
                        Text = $"📄 {System.IO.Path.GetFileName(group.Key)}",
                        FontSize = 12, FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#FFDCC4FF")),
                        Margin = new Thickness(0, 4, 0, 2)
                    });

                    foreach (var change in group.Take(20))
                    {
                        PreviewPanel.Children.Add(new TextBlock
                        {
                            Text = $"  offset {change.StartPosition}–{change.EndPosition} → \"{change.NewText}\"",
                            FontSize = 11,
                            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                            Foreground = new SolidColorBrush(Color.Parse("#FFA6E3A1")),
                            Margin = new Thickness(8, 1)
                        });
                    }

                    if (group.Count() > 20)
                    {
                        PreviewPanel.Children.Add(new TextBlock
                        {
                            Text = $"  ... and {group.Count() - 20} more",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#FF9E90B0")),
                            Margin = new Thickness(8, 1)
                        });
                    }
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RenameDialog] Preview: {ex.Message}");
        }
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_renameResult != null && _renameResult.Changes.Any())
        {
            RenameConfirmed?.Invoke(this, _renameResult);
            Close();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

