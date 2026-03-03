using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Insait_Edit_C_Sharp.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Compound Run Configuration window — lets users create, edit and run
/// compound configurations that launch multiple projects simultaneously,
/// exactly like JetBrains Rider's Compound run configuration.
/// </summary>
public partial class CompoundRunWindow : Window
{
    private readonly RunConfigurationService _runConfigService;
    private readonly ObservableCollection<CompoundRunConfiguration> _compounds = new();
    private CompoundRunConfiguration? _selected;
    private bool _isDirty;

    /// <summary>Compound config the user confirmed to run (null if window cancelled)</summary>
    public CompoundRunConfiguration? SelectedToRun { get; private set; }

    public CompoundRunWindow() : this(new RunConfigurationService()) { }

    public CompoundRunWindow(RunConfigurationService runConfigService)
    {
        InitializeComponent();
        _runConfigService = runConfigService;

        SetupEventHandlers();
        LoadData();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("Compound.Title");
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Setup
    // ──────────────────────────────────────────────────────────────────────────

    private void SetupEventHandlers()
    {
        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
            titleBar.PointerPressed += TitleBar_PointerPressed;

        BindButton("CloseButton",    (_, _) => Close(null));
        BindButton("AddCompoundButton",       AddCompound_Click);
        BindButton("RemoveCompoundButton",    RemoveCompound_Click);
        BindButton("DuplicateCompoundButton", DuplicateCompound_Click);
        BindButton("RunAllButton",   RunAll_Click);
        BindButton("StopAllButton",  StopAll_Click);
        BindButton("ApplyButton",    Apply_Click);
        BindButton("CancelButton",   (_, _) => Close(null));
        BindButton("OkButton",       Ok_Click);

        // Name change tracking
        var nameBox = this.FindControl<TextBox>("CompoundNameBox");
        if (nameBox != null)
            nameBox.TextChanged += (_, _) => _isDirty = true;

        // Delay box
        var delayBox = this.FindControl<TextBox>("DelayBox");
        if (delayBox != null)
            delayBox.TextChanged += (_, _) => _isDirty = true;
    }

    private void BindButton(string name, EventHandler<RoutedEventArgs> handler)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Click += handler;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Data loading
    // ──────────────────────────────────────────────────────────────────────────

    private void LoadData()
    {
        // Load existing compound configurations
        _compounds.Clear();
        foreach (var c in _runConfigService.CompoundConfigurations)
            _compounds.Add(c);

        var list = this.FindControl<ListBox>("CompoundList");
        if (list != null)
            list.ItemsSource = _compounds;

        // Select first or none
        if (_compounds.Count > 0 && list != null)
            list.SelectedIndex = 0;
        else
            ShowEmptyState(true);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ListBox selection
    // ──────────────────────────────────────────────────────────────────────────

    private void CompoundList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isDirty && _selected != null)
            SaveCurrentToModel();

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is CompoundRunConfiguration compound)
        {
            _selected = compound;
            LoadCompoundDetails(compound);
            ShowEmptyState(false);
            _isDirty = false;
        }
        else
        {
            _selected = null;
            ShowEmptyState(true);
        }
    }

    private void ShowEmptyState(bool empty)
    {
        var emptyPanel  = this.FindControl<Border>("EmptyStatePanel");
        var detailPanel = this.FindControl<StackPanel>("ConfigDetailsPanel");
        if (emptyPanel  != null) emptyPanel.IsVisible  = empty;
        if (detailPanel != null) detailPanel.IsVisible = !empty;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Load compound config details into the right panel
    // ──────────────────────────────────────────────────────────────────────────

    private void LoadCompoundDetails(CompoundRunConfiguration compound)
    {
        var nameBox = this.FindControl<TextBox>("CompoundNameBox");
        if (nameBox != null)
        {
            nameBox.TextChanged -= OnNameChanged; // prevent dirty trigger while loading
            nameBox.Text = compound.Name;
            nameBox.TextChanged += OnNameChanged;
        }

        var seqCheck = this.FindControl<CheckBox>("StartSequentiallyCheck");
        if (seqCheck != null) seqCheck.IsChecked = compound.StartSequentially;

        var stopCheck = this.FindControl<CheckBox>("StopOnFailureCheck");
        if (stopCheck != null)
        {
            stopCheck.IsChecked = compound.StopOnFailure;
            stopCheck.IsEnabled = compound.StartSequentially;
        }

        var delayBox = this.FindControl<TextBox>("DelayBox");
        if (delayBox != null)
        {
            delayBox.Text = compound.DelayBetweenStartsMs.ToString();
            delayBox.IsEnabled = compound.StartSequentially;
        }

        // Rebuild the available configs checkboxes
        RebuildAvailableConfigsPanel(compound);

        // Update preview
        UpdatePreview(compound);
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e) => _isDirty = true;

    // ──────────────────────────────────────────────────────────────────────────
    // Available run configs checkboxes
    // ──────────────────────────────────────────────────────────────────────────

    private void RebuildAvailableConfigsPanel(CompoundRunConfiguration compound)
    {
        var panel = this.FindControl<StackPanel>("AvailableConfigsPanel");
        if (panel == null) return;

        panel.Children.Clear();

        if (_runConfigService.Configurations.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No run configurations available. Open a solution with runnable projects first.",
                Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
                FontSize = 12,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(8)
            });
            return;
        }

        foreach (var cfg in _runConfigService.Configurations)
        {
            var isIncluded = compound.Configurations.Contains(cfg.Name, StringComparer.OrdinalIgnoreCase);

            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6),
                Background = isIncluded
                    ? new SolidColorBrush(Color.Parse("#30A6E3A1"))
                    : Brushes.Transparent
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var check = new CheckBox
            {
                IsChecked = isIncluded,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = cfg.Name
            };
            check.IsCheckedChanged += ConfigCheckbox_Changed;
            Grid.SetColumn(check, 0);

            var info = new StackPanel { Margin = new Thickness(8, 0) };
            info.Children.Add(new TextBlock
            {
                Text = cfg.Name,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4"))
            });

            var projectName = System.IO.Path.GetFileName(cfg.ProjectPath);
            if (!string.IsNullOrEmpty(projectName))
            {
                info.Children.Add(new TextBlock
                {
                    Text = $"📁 {projectName}  •  {cfg.Configuration}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#FF9399B2"))
                });
            }
            Grid.SetColumn(info, 1);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.Parse(
                    cfg.Configuration == "Release" ? "#30F38BA8" : "#30A6E3A1")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = cfg.Configuration,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse(
                    cfg.Configuration == "Release" ? "#FFF38BA8" : "#FFA6E3A1"))
            };
            Grid.SetColumn(badge, 2);

            grid.Children.Add(check);
            grid.Children.Add(info);
            grid.Children.Add(badge);

            row.Child = grid;
            panel.Children.Add(row);
        }
    }

    private void ConfigCheckbox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_selected == null || sender is not CheckBox check) return;

        var configName = check.Tag as string ?? "";
        if (check.IsChecked == true)
        {
            if (!_selected.Configurations.Contains(configName, StringComparer.OrdinalIgnoreCase))
                _selected.Configurations.Add(configName);
        }
        else
        {
            _selected.Configurations.RemoveAll(n =>
                n.Equals(configName, StringComparison.OrdinalIgnoreCase));
        }

        // Update row background
        if (check.Parent is Grid grid && grid.Parent is Border border)
        {
            border.Background = check.IsChecked == true
                ? new SolidColorBrush(Color.Parse("#30A6E3A1"))
                : Brushes.Transparent;
        }

        UpdatePreview(_selected);
        _isDirty = true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Preview panel
    // ──────────────────────────────────────────────────────────────────────────

    private void UpdatePreview(CompoundRunConfiguration compound)
    {
        var panel = this.FindControl<StackPanel>("PreviewItemsPanel");
        if (panel == null) return;

        panel.Children.Clear();

        if (compound.Configurations.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No projects selected — check at least one configuration above.",
                Foreground = new SolidColorBrush(Color.Parse("#FF6C7086")),
                FontSize = 11,
                FontStyle = FontStyle.Italic
            });
            return;
        }

        var mode = compound.StartSequentially ? "Sequential" : "Parallel";
        panel.Children.Add(new TextBlock
        {
            Text = $"Mode: {mode}  •  {compound.Configurations.Count} project(s)",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#FFA6ADC8")),
            Margin = new Thickness(0, 0, 0, 8)
        });

        for (int i = 0; i < compound.Configurations.Count; i++)
        {
            var name = compound.Configurations[i];
            var connector = compound.StartSequentially && i < compound.Configurations.Count - 1 ? "  ↓" : "";

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 2)
            };

            row.Children.Add(new TextBlock
            {
                Text = compound.StartSequentially ? $"{i + 1}." : "▶",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#FFA6E3A1")),
                Width = 20
            });
            row.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#FFCDD6F4"))
            });

            panel.Children.Add(row);

            if (!string.IsNullOrEmpty(connector))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = connector,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#FF6C7086")),
                    Margin = new Thickness(20, 0)
                });
            }
        }

        if (compound.StartSequentially && compound.DelayBetweenStartsMs > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"⏱ {compound.DelayBetweenStartsMs} ms delay between starts",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#FF9399B2")),
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        if (compound.StartSequentially && compound.StopOnFailure)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "⚠️ Stops on first failure",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#FFFAB387")),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sequential option toggling
    // ──────────────────────────────────────────────────────────────────────────

    private void StartSequentially_Changed(object? sender, RoutedEventArgs e)
    {
        var isSeq = this.FindControl<CheckBox>("StartSequentiallyCheck")?.IsChecked == true;

        var stopCheck = this.FindControl<CheckBox>("StopOnFailureCheck");
        var delayBox  = this.FindControl<TextBox>("DelayBox");
        if (stopCheck != null) stopCheck.IsEnabled = isSeq;
        if (delayBox  != null) delayBox.IsEnabled  = isSeq;

        _isDirty = true;

        if (_selected != null)
        {
            _selected.StartSequentially = isSeq;
            UpdatePreview(_selected);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CRUD operations
    // ──────────────────────────────────────────────────────────────────────────

    private void AddCompound_Click(object? sender, RoutedEventArgs e)
    {
        var newCompound = new CompoundRunConfiguration
        {
            Name = $"Compound #{_compounds.Count + 1}"
        };

        _compounds.Add(newCompound);
        _runConfigService.AddCompoundConfiguration(newCompound);

        var list = this.FindControl<ListBox>("CompoundList");
        if (list != null)
            list.SelectedItem = newCompound;
    }

    private void RemoveCompound_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        _runConfigService.RemoveCompoundConfiguration(_selected);
        _compounds.Remove(_selected);
        _selected = null;
        ShowEmptyState(true);
    }

    private void DuplicateCompound_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        var dup = new CompoundRunConfiguration
        {
            Name = _selected.Name + " (Copy)",
            StartSequentially = _selected.StartSequentially,
            StopOnFailure = _selected.StopOnFailure,
            DelayBetweenStartsMs = _selected.DelayBetweenStartsMs,
            Configurations = new System.Collections.Generic.List<string>(_selected.Configurations)
        };

        _compounds.Add(dup);
        _runConfigService.AddCompoundConfiguration(dup);

        var list = this.FindControl<ListBox>("CompoundList");
        if (list != null)
            list.SelectedItem = dup;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save model from UI controls
    // ──────────────────────────────────────────────────────────────────────────

    private void SaveCurrentToModel()
    {
        if (_selected == null) return;

        var nameBox = this.FindControl<TextBox>("CompoundNameBox");
        if (nameBox != null)
            _selected.Name = nameBox.Text ?? _selected.Name;

        var seqCheck = this.FindControl<CheckBox>("StartSequentiallyCheck");
        if (seqCheck != null)
            _selected.StartSequentially = seqCheck.IsChecked == true;

        var stopCheck = this.FindControl<CheckBox>("StopOnFailureCheck");
        if (stopCheck != null)
            _selected.StopOnFailure = stopCheck.IsChecked == true;

        var delayBox = this.FindControl<TextBox>("DelayBox");
        if (delayBox != null && int.TryParse(delayBox.Text, out var delay))
            _selected.DelayBetweenStartsMs = delay;

        _isDirty = false;

        // Notify list to refresh display
        var list = this.FindControl<ListBox>("CompoundList");
        var idx = list?.SelectedIndex ?? -1;
        if (list != null && idx >= 0)
        {
            // Force refresh by re-assigning ItemsSource
            list.ItemsSource = null;
            list.ItemsSource = _compounds;
            list.SelectedIndex = idx;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Run / Stop
    // ──────────────────────────────────────────────────────────────────────────

    private async void RunAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        SaveCurrentToModel();

        if (_selected.Configurations.Count == 0)
        {
            // Nothing selected — warn user
            return;
        }

        SelectedToRun = _selected;
        Close(_selected);
    }

    private void StopAll_Click(object? sender, RoutedEventArgs e)
    {
        _runConfigService.Stop();
        ToggleStopButton(false);
    }

    private void ToggleStopButton(bool running)
    {
        var runBtn  = this.FindControl<Button>("RunAllButton");
        var stopBtn = this.FindControl<Button>("StopAllButton");
        if (runBtn  != null) runBtn.IsVisible  = !running;
        if (stopBtn != null) stopBtn.IsVisible = running;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Apply / OK / Cancel
    // ──────────────────────────────────────────────────────────────────────────

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        SaveCurrentToModel();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        SaveCurrentToModel();
        Close(_selected);
    }
}

