using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Full-featured Auto Fix window.
/// Pipeline: Diagnostics → CodeFixProvider → CodeAction → Apply fix.
///
/// Three tabs:
///   1. Diagnostics &amp; Fixes — shows all Roslyn diagnostics with available code fixes
///   2. Code Templates — insertable C# code snippets / patterns
///   3. Keywords — quick keyword insertion
/// </summary>
public partial class AutoFixWindow : Window
{
    // ── Colours ──────────────────────────────────────────────────────────
    private static readonly Color BgSel     = Color.Parse("#FF3E3050");
    private static readonly Color BgHover   = Color.Parse("#FF352840");
    private static readonly Color ErrorFg   = Color.Parse("#FFF38BA8");
    private static readonly Color WarningFg = Color.Parse("#FFF5A623");
    private static readonly Color InfoFg    = Color.Parse("#FF89B4FA");
    private static readonly Color HintFg    = Color.Parse("#FFA6E3A1");
    private static readonly Color TextFg    = Color.Parse("#FFF0E8F4");
    private static readonly Color DimFg     = Color.Parse("#FF9E90B0");
    private static readonly Color AccentFg  = Color.Parse("#FFDCC4FF");
    private static readonly Color OrangeFg  = Color.Parse("#FFFFC09F");

    // ── State ────────────────────────────────────────────────────────────
    private readonly RoslynAutoFixService _autoFixService;
    private string _filePath = string.Empty;
    private string _sourceCode = string.Empty;
    private CancellationTokenSource? _cts;

    private List<AutoFixDiagnosticEntry> _allDiagEntries = new();
    private List<CodeTemplate> _allTemplates;
    private List<KeywordItem> _allKeywords;

    private AutoFixDiagnosticEntry? _selectedDiagEntry;
    private AutoFixAction? _selectedFixAction;
    private CodeTemplate? _selectedTemplate;
    private KeywordItem? _selectedKeyword;

    /// <summary>
    /// Fired when a fix changes the source code.
    /// Handler receives the new full source text.
    /// </summary>
    public event EventHandler<AutoFixAppliedEventArgs>? FixApplied;

    /// <summary>
    /// Fired when a template or keyword should be inserted at the cursor.
    /// </summary>
    public event EventHandler<string>? InsertTextRequested;

    /// <summary>
    /// Fired when the window wants to navigate the editor to a specific line.
    /// </summary>
    public event EventHandler<int>? NavigateToLineRequested;

    public AutoFixWindow(string filePath, string sourceCode)
    {
        InitializeComponent();

        _autoFixService = new RoslynAutoFixService();
        _filePath       = filePath;
        _sourceCode     = sourceCode;
        _allTemplates   = RoslynAutoFixService.GetCodeTemplates();
        _allKeywords    = RoslynAutoFixService.GetKeywordItems();

        var fileName = System.IO.Path.GetFileName(filePath);
        var fileNameText = this.FindControl<TextBlock>("FileNameText");
        if (fileNameText != null) fileNameText.Text = fileName;

        ApplyLocalization();
        BuildTemplatesList(_allTemplates);
        BuildKeywordsList(_allKeywords);

        // Start analysis
        _ = RefreshDiagnosticsAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Localization
    // ═══════════════════════════════════════════════════════════════════════

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;

        Title = L("AutoFix.Title");
        var titleBar = this.FindControl<TextBlock>("TitleBarText");
        if (titleBar != null) titleBar.Text = L("AutoFix.Title");

        var tabDiag = this.FindControl<TextBlock>("TabDiagText");
        if (tabDiag != null) tabDiag.Text = L("AutoFix.TabDiagnostics");

        var tabTempl = this.FindControl<TextBlock>("TabTemplText");
        if (tabTempl != null) tabTempl.Text = L("AutoFix.TabTemplates");

        var tabKw = this.FindControl<TextBlock>("TabKeywordsText");
        if (tabKw != null) tabKw.Text = L("AutoFix.TabKeywords");

        var fixAll = this.FindControl<TextBlock>("FixAllText");
        if (fixAll != null) fixAll.Text = L("AutoFix.FixAll");

        var refresh = this.FindControl<TextBlock>("RefreshText");
        if (refresh != null) refresh.Text = L("AutoFix.Refresh");

        var insTempl = this.FindControl<TextBlock>("InsertTemplateText");
        if (insTempl != null) insTempl.Text = L("AutoFix.Insert");

        var insKw = this.FindControl<TextBlock>("InsertKeywordText");
        if (insKw != null) insKw.Text = L("AutoFix.Insert");

        var closeBtn = this.FindControl<TextBlock>("CloseBtnText");
        if (closeBtn != null) closeBtn.Text = L("AutoFix.Close");

        var status = this.FindControl<TextBlock>("StatusText");
        if (status != null) status.Text = L("AutoFix.Ready");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Window chrome
    // ═══════════════════════════════════════════════════════════════════════

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // ═══════════════════════════════════════════════════════════════════════
    //  Tab switching
    // ═══════════════════════════════════════════════════════════════════════

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        var tabs = new[] { "TabDiagnostics", "TabTemplates", "TabKeywords" };
        foreach (var t in tabs)
        {
            var b = this.FindControl<Button>(t);
            b?.Classes.Remove("selected");
        }
        btn.Classes.Add("selected");

        var diagPanel = this.FindControl<Grid>("DiagnosticsPanel");
        var templPanel = this.FindControl<Grid>("TemplatesPanel");
        var kwPanel = this.FindControl<Grid>("KeywordsPanel");

        if (diagPanel != null) diagPanel.IsVisible = tag == "diagnostics";
        if (templPanel != null) templPanel.IsVisible = tag == "templates";
        if (kwPanel != null) kwPanel.IsVisible = tag == "keywords";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Diagnostics & Fixes
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RefreshDiagnosticsAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetStatus(LocalizationService.Get("AutoFix.Analyzing"));

        try
        {
            var entries = await _autoFixService.GetDiagnosticsWithFixesAsync(_filePath, _sourceCode, ct);
            if (ct.IsCancellationRequested) return;

            _allDiagEntries = entries;

            var badge = this.FindControl<TextBlock>("DiagCountBadge");
            if (badge != null) badge.Text = entries.Count.ToString();

            var btnFixAll = this.FindControl<Button>("BtnFixAll");
            if (btnFixAll != null) btnFixAll.IsEnabled = entries.Count > 0;

            BuildDiagnosticsList(entries);
            SetStatus(string.Format(LocalizationService.Get("AutoFix.FoundDiagnostics"), entries.Count));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void BuildDiagnosticsList(List<AutoFixDiagnosticEntry> entries)
    {
        var stack = this.FindControl<StackPanel>("DiagnosticsStack");
        if (stack == null) return;
        stack.Children.Clear();

        var searchBox = this.FindControl<TextBox>("DiagSearchBox");
        var filter = searchBox?.Text?.Trim() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(filter)
            ? entries
            : entries.Where(e =>
                e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.DiagnosticId.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var entry in filtered)
        {
            var row = BuildDiagRow(entry);
            stack.Children.Add(row);
        }

        if (filtered.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("AutoFix.NoDiagnostics"),
                FontSize = 12, Foreground = new SolidColorBrush(DimFg),
                Margin = new Thickness(12, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }

    private Border BuildDiagRow(AutoFixDiagnosticEntry entry)
    {
        var severityColor = entry.Severity switch
        {
            DiagnosticSeverityKind.Error   => ErrorFg,
            DiagnosticSeverityKind.Warning => WarningFg,
            DiagnosticSeverityKind.Info    => InfoFg,
            _                              => HintFg,
        };

        // Header: icon + code + message
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerStack.Children.Add(new TextBlock
        {
            Text = entry.SeverityIcon, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = entry.DiagnosticId, FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(severityColor),
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = $"({entry.Line}:{entry.Column})", FontSize = 10,
            Foreground = new SolidColorBrush(DimFg),
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var messageText = new TextBlock
        {
            Text = entry.Message, FontSize = 11,
            Foreground = new SolidColorBrush(TextFg),
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 700,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var contentStack = new StackPanel { Spacing = 4 };
        contentStack.Children.Add(headerStack);
        contentStack.Children.Add(messageText);

        // Fixes
        if (entry.AvailableFixes.Count > 0)
        {
            var fixLabel = new TextBlock
            {
                Text = $"💡 {entry.AvailableFixes.Count} fix(es) available",
                FontSize = 10, Foreground = new SolidColorBrush(OrangeFg),
                Margin = new Thickness(0, 2, 0, 0),
            };
            contentStack.Children.Add(fixLabel);

            var fixesStack = new StackPanel { Spacing = 1, Margin = new Thickness(16, 2, 0, 0) };
            foreach (var fix in entry.AvailableFixes)
            {
                var fixRow = BuildFixRow(entry, fix);
                fixesStack.Children.Add(fixRow);
            }
            contentStack.Children.Add(fixesStack);
        }

        var border = new Border
        {
            Child = contentStack,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Tag = entry,
        };

        border.PointerEntered += (_, _) => border.Background = new SolidColorBrush(BgHover);
        border.PointerExited  += (_, _) =>
            border.Background = border.Tag == _selectedDiagEntry
                ? new SolidColorBrush(BgSel) : Brushes.Transparent;

        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                _selectedDiagEntry = entry;
                NavigateToLineRequested?.Invoke(this, entry.Line);
            }
        };

        // Double click to apply first fix
        border.DoubleTapped += async (_, _) =>
        {
            var firstFix = entry.AvailableFixes.FirstOrDefault(f => f.CodeAction != null)
                           ?? entry.AvailableFixes.FirstOrDefault();
            if (firstFix != null)
                await ApplyFixAsync(entry, firstFix);
        };

        return border;
    }

    private Border BuildFixRow(AutoFixDiagnosticEntry diagEntry, AutoFixAction fix)
    {
        var iconText = new TextBlock
        {
            Text = fix.KindIcon, FontSize = 12, Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var titleText = new TextBlock
        {
            Text = fix.Title, FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(TextFg),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var providerText = new TextBlock
        {
            Text = fix.ProviderName ?? fix.Kind.ToString(),
            FontSize = 9, Foreground = new SolidColorBrush(DimFg),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var applyBtn = new Button
        {
            Content = new TextBlock { Text = "Apply", FontSize = 10 },
            Background = new SolidColorBrush(Color.Parse("#FF352840")),
            Foreground = new SolidColorBrush(OrangeFg),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(OrangeFg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };

        applyBtn.Click += async (_, _) => await ApplyFixAsync(diagEntry, fix);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Grid.SetColumn(iconText, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(providerText, 2);
        Grid.SetColumn(applyBtn, 3);

        grid.Children.Add(iconText);
        grid.Children.Add(titleText);
        grid.Children.Add(providerText);
        grid.Children.Add(applyBtn);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(4, 3),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
        };

        row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(BgSel);
        row.PointerExited  += (_, _) => row.Background = Brushes.Transparent;

        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                _selectedFixAction = fix;
                ShowFixPreview(fix);
                e.Handled = true;
            }
        };

        return row;
    }

    private async Task ApplyFixAsync(AutoFixDiagnosticEntry entry, AutoFixAction fix)
    {
        try
        {
            SetStatus(LocalizationService.Get("AutoFix.Applying"));

            if (fix.CodeAction != null)
            {
                // Roslyn CodeAction — produces actual code changes
                var newSource = await _autoFixService.ApplyCodeActionAsync(_filePath, _sourceCode, fix);
                if (newSource != null)
                {
                    _sourceCode = newSource;
                    FixApplied?.Invoke(this, new AutoFixAppliedEventArgs(_filePath, newSource));
                    SetStatus(string.Format(LocalizationService.Get("AutoFix.Applied"), fix.Title));
                    await RefreshDiagnosticsAsync();
                    return;
                }
            }

            // Built-in text fix
            if (fix.Kind == AutoFixKind.InsertText && !string.IsNullOrEmpty(fix.InsertText))
            {
                var offset = fix.InsertOffset > 0 ? fix.InsertOffset : entry.EndOffset;
                var newSource = _sourceCode.Insert(offset, fix.InsertText);
                _sourceCode = newSource;
                FixApplied?.Invoke(this, new AutoFixAppliedEventArgs(_filePath, newSource));
                SetStatus(string.Format(LocalizationService.Get("AutoFix.Applied"), fix.Title));
                await RefreshDiagnosticsAsync();
                return;
            }

            if (fix.Kind == AutoFixKind.RemoveText)
            {
                if (entry.StartOffset >= 0 && entry.EndOffset > entry.StartOffset &&
                    entry.EndOffset <= _sourceCode.Length)
                {
                    var newSource = _sourceCode.Remove(entry.StartOffset, entry.EndOffset - entry.StartOffset);
                    _sourceCode = newSource;
                    FixApplied?.Invoke(this, new AutoFixAppliedEventArgs(_filePath, newSource));
                    SetStatus(string.Format(LocalizationService.Get("AutoFix.Applied"), fix.Title));
                    await RefreshDiagnosticsAsync();
                    return;
                }
            }

            SetStatus(LocalizationService.Get("AutoFix.CannotApply"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void ShowFixPreview(AutoFixAction fix)
    {
        var pane = this.FindControl<Border>("FixPreviewPane");
        var title = this.FindControl<TextBlock>("PreviewTitle");
        var provider = this.FindControl<TextBlock>("PreviewProvider");
        var code = this.FindControl<TextBlock>("PreviewCode");

        if (pane == null) return;
        pane.IsVisible = true;

        if (title != null) title.Text = fix.Title;
        if (provider != null) provider.Text = fix.ProviderName ?? fix.Kind.ToString();
        if (code != null)
        {
            code.Text = fix.CodeAction != null
                ? $"Roslyn CodeAction: {fix.Title}\nProvider: {fix.ProviderName ?? "built-in"}\nClick Apply to see the result."
                : fix.InsertText ?? "(text modification)";
        }
    }

    private async void FixAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_allDiagEntries.Count == 0) return;
        SetStatus(LocalizationService.Get("AutoFix.ApplyingAll"));

        try
        {
            var current = _sourceCode;
            int applied = 0;

            foreach (var entry in _allDiagEntries.ToList())
            {
                var fix = entry.AvailableFixes.FirstOrDefault(f => f.CodeAction != null)
                          ?? entry.AvailableFixes.FirstOrDefault(f =>
                              f.Kind == AutoFixKind.InsertText && !string.IsNullOrEmpty(f.InsertText));

                if (fix?.CodeAction != null)
                {
                    var result = await _autoFixService.ApplyCodeActionAsync(_filePath, current, fix);
                    if (result != null) { current = result; applied++; }
                }
                else if (fix is { Kind: AutoFixKind.InsertText, InsertText: not null })
                {
                    var offset = Math.Min(fix.InsertOffset > 0 ? fix.InsertOffset : entry.EndOffset, current.Length);
                    current = current.Insert(offset, fix.InsertText);
                    applied++;
                }
            }

            if (applied > 0)
            {
                _sourceCode = current;
                FixApplied?.Invoke(this, new AutoFixAppliedEventArgs(_filePath, current));
            }

            SetStatus(string.Format(LocalizationService.Get("AutoFix.AppliedCount"), applied));
            await RefreshDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshDiagnosticsAsync();
    }

    private void DiagSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        BuildDiagnosticsList(_allDiagEntries);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Code Templates
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildTemplatesList(List<CodeTemplate> templates)
    {
        var stack = this.FindControl<StackPanel>("TemplatesStack");
        if (stack == null) return;
        stack.Children.Clear();

        var searchBox = this.FindControl<TextBox>("TemplateSearchBox");
        var filter = searchBox?.Text?.Trim() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(filter)
            ? templates
            : templates.Where(t =>
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Keyword.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        string? lastCategory = null;
        foreach (var templ in filtered)
        {
            if (templ.Category != lastCategory)
            {
                lastCategory = templ.Category;
                stack.Children.Add(new TextBlock
                {
                    Text = $"— {templ.Category} —",
                    FontSize = 10, FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(AccentFg),
                    Margin = new Thickness(10, 8, 0, 2),
                });
            }

            var row = BuildTemplateRow(templ);
            stack.Children.Add(row);
        }
    }

    private Border BuildTemplateRow(CodeTemplate templ)
    {
        var keywordBadge = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#30FFC09F")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1),
            Child = new TextBlock
            {
                Text = templ.Keyword, FontSize = 10,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(OrangeFg),
            },
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameText = new TextBlock
        {
            Text = templ.Name, FontSize = 12,
            Foreground = new SolidColorBrush(TextFg),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var descText = new TextBlock
        {
            Text = templ.Description, FontSize = 10,
            Foreground = new SolidColorBrush(DimFg),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        header.Children.Add(keywordBadge);
        header.Children.Add(nameText);
        header.Children.Add(descText);

        var snippetPreview = new TextBlock
        {
            Text = templ.Snippet.Length > 80 ? templ.Snippet[..80] + "…" : templ.Snippet,
            FontSize = 10, FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(HintFg),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(header);
        content.Children.Add(snippetPreview);

        var border = new Border
        {
            Child = content,
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Tag = templ,
        };

        border.PointerEntered += (_, _) => border.Background = new SolidColorBrush(BgHover);
        border.PointerExited  += (_, _) =>
            border.Background = _selectedTemplate == templ
                ? new SolidColorBrush(BgSel) : Brushes.Transparent;

        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                _selectedTemplate = templ;
                var btnInsert = this.FindControl<Button>("BtnInsertTemplate");
                if (btnInsert != null) btnInsert.IsEnabled = true;
                e.Handled = true;
            }
        };

        border.DoubleTapped += (_, _) =>
        {
            InsertTextRequested?.Invoke(this, templ.Snippet);
            SetStatus(string.Format(LocalizationService.Get("AutoFix.Inserted"), templ.Name));
        };

        return border;
    }

    private void TemplateSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        BuildTemplatesList(_allTemplates);
    }

    private void InsertTemplate_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedTemplate == null) return;
        InsertTextRequested?.Invoke(this, _selectedTemplate.Snippet);
        SetStatus(string.Format(LocalizationService.Get("AutoFix.Inserted"), _selectedTemplate.Name));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Keywords
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildKeywordsList(List<KeywordItem> keywords)
    {
        var stack = this.FindControl<StackPanel>("KeywordsStack");
        if (stack == null) return;
        stack.Children.Clear();

        var searchBox = this.FindControl<TextBox>("KeywordSearchBox");
        var filter = searchBox?.Text?.Trim() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(filter)
            ? keywords
            : keywords.Where(k =>
                k.Keyword.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                k.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Render as a wrapped panel of keyword chips
        var wrapPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4) };

        foreach (var kw in filtered)
        {
            var chip = BuildKeywordChip(kw);
            wrapPanel.Children.Add(chip);
        }

        stack.Children.Add(wrapPanel);
    }

    private Border BuildKeywordChip(KeywordItem kw)
    {
        var text = new TextBlock
        {
            Text = kw.Keyword, FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(OrangeFg),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var border = new Border
        {
            Child = text,
            Padding = new Thickness(10, 5),
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#FF352840")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF3E3050")),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = kw,
        };

        ToolTip.SetTip(border, kw.Description);

        border.PointerEntered += (_, _) =>
        {
            border.Background = new SolidColorBrush(BgSel);
            border.BorderBrush = new SolidColorBrush(OrangeFg);
        };
        border.PointerExited += (_, _) =>
        {
            border.Background = new SolidColorBrush(Color.Parse("#FF352840"));
            border.BorderBrush = _selectedKeyword == kw
                ? new SolidColorBrush(OrangeFg)
                : new SolidColorBrush(Color.Parse("#FF3E3050"));
        };

        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                _selectedKeyword = kw;
                var btnInsert = this.FindControl<Button>("BtnInsertKeyword");
                if (btnInsert != null) btnInsert.IsEnabled = true;
                e.Handled = true;
            }
        };

        border.DoubleTapped += (_, _) =>
        {
            InsertTextRequested?.Invoke(this, kw.Keyword);
            SetStatus(string.Format(LocalizationService.Get("AutoFix.Inserted"), kw.Keyword));
        };

        return border;
    }

    private void KeywordSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        BuildKeywordsList(_allKeywords);
    }

    private void InsertKeyword_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedKeyword == null) return;
        InsertTextRequested?.Invoke(this, _selectedKeyword.Keyword);
        SetStatus(string.Format(LocalizationService.Get("AutoFix.Inserted"), _selectedKeyword.Keyword));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private void SetStatus(string text)
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status != null) status.Text = text;
    }

    /// <summary>
    /// Update the source code (e.g. when editor content changes while window is open).
    /// </summary>
    public void UpdateSource(string sourceCode)
    {
        _sourceCode = sourceCode;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _autoFixService.Dispose();
        base.OnClosed(e);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Event args
// ═══════════════════════════════════════════════════════════════════════════

public sealed class AutoFixAppliedEventArgs : EventArgs
{
    public string FilePath   { get; }
    public string NewSource  { get; }

    public AutoFixAppliedEventArgs(string filePath, string newSource)
    {
        FilePath  = filePath;
        NewSource = newSource;
    }
}

