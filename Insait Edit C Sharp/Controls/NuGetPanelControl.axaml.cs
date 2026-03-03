using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.Controls;

public partial class NuGetPanelControl : UserControl
{
    private readonly NuGetService _nugetService;
    private readonly HttpClient _httpClient;
    
    private string? _projectPath;
    private string _currentTab = "browse";
    private bool _isNanoFrameworkProject;
    private List<NuGetPackageInfo> _searchResults = new();
    private List<InstalledNuGetPackage> _installedPackages = new();
    private List<InstalledNuGetPackage> _updatablePackages = new();
    private NuGetPackageInfo? _selectedPackage;
    private CancellationTokenSource? _searchCts;
    
    private static readonly Dictionary<string, Bitmap?> _iconCache = new();
    
    // Cached brushes
    private IBrush? _textBrush;
    private IBrush? _textMutedBrush;
    private IBrush? _accentBrush;
    private IBrush? _orangeBrush;
    private IBrush? _greenBrush;
    private IBrush? _blueBrush;
    
    // Emoji font family for consistent rendering
    private static readonly FontFamily EmojiFont = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Apple Color Emoji, Segoe UI Symbol");
    
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public NuGetPanelControl()
    {
        InitializeComponent();
        _nugetService = new NuGetService();
        _httpClient = new HttpClient();
        
        _nugetService.StatusChanged += (_, status) => 
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusChanged?.Invoke(this, status));
        _nugetService.ErrorOccurred += (_, error) => 
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ErrorOccurred?.Invoke(this, error));
        
        // Initialize brushes
        InitializeBrushes();
        ApplyLocalization();
        LocalizationService.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLocalization);
    }
    
    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        var header = this.FindControl<TextBlock>("HeaderTitleText");
        if (header != null) header.Text = _isNanoFrameworkProject ? L("NuGet.NanoFrameworkMode") : L("NuGet.Title");
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null) searchBox.Watermark = L("NuGet.SearchPlaceholder");
        var searchPlaceholder = this.FindControl<TextBlock>("SearchPlaceholder");
        if (searchPlaceholder != null) searchPlaceholder.Text = L("NuGet.SearchAbove");
        var projectName = this.FindControl<TextBlock>("ProjectNameText");
        if (projectName != null && projectName.Text == "No project loaded") projectName.Text = L("NuGet.NoProject");
        var browseTab = this.FindControl<Button>("BrowseTabBtn");
        if (browseTab != null) browseTab.Content = L("NuGet.Browse");
        var installedTab = this.FindControl<Button>("InstalledTabBtn");
        if (installedTab != null) installedTab.Content = L("NuGet.Installed");
        var updatesTab = this.FindControl<Button>("UpdatesTabBtn");
        if (updatesTab != null) updatesTab.Content = L("NuGet.Updates");
        var updateAllBtn = this.FindControl<Button>("UpdateAllBtn");
        if (updateAllBtn != null) updateAllBtn.Content = L("NuGet.UpdateAll");
        // Update tooltip strings for icon buttons
        var espBtn = this.FindControl<Button>("EspSuggestionsButton");
        if (espBtn != null) ToolTip.SetTip(espBtn, L("NuGet.Tooltip.NanoSuggestions"));
        var refreshBtn = this.FindControl<Button>("RefreshButton");
        if (refreshBtn != null) ToolTip.SetTip(refreshBtn, L("NuGet.Tooltip.Refresh"));
        // Update installed/updates placeholders
        var installedPlaceholder = this.FindControl<TextBlock>("InstalledPlaceholder");
        if (installedPlaceholder != null) installedPlaceholder.Text = L("NuGet.NoPackages");
        var updatesPlaceholder = this.FindControl<TextBlock>("UpdatesPlaceholder");
        if (updatesPlaceholder != null) updatesPlaceholder.Text = L("NuGet.AllUpToDate");
        // Update details panel static labels
        var detailsHeaderText = this.FindControl<TextBlock>("PackageDetailsHeaderText");
        if (detailsHeaderText != null) detailsHeaderText.Text = L("NuGet.PackageDetails");
        var nugetOrgBtn = this.FindControl<Button>("NuGetOrgLinkButton");
        if (nugetOrgBtn != null) ToolTip.SetTip(nugetOrgBtn, L("NuGet.ViewOnNuGetOrg"));
        var versionLabel = this.FindControl<TextBlock>("VersionLabelText");
        if (versionLabel != null) versionLabel.Text = L("NuGet.Version");
        var downloadsLabel = this.FindControl<TextBlock>("DownloadsLabelText");
        if (downloadsLabel != null) downloadsLabel.Text = L("NuGet.Downloads");
        var publishedLabel = this.FindControl<TextBlock>("PublishedLabelText");
        if (publishedLabel != null) publishedLabel.Text = L("NuGet.Published");
        var descLabel = this.FindControl<TextBlock>("DescriptionLabelText");
        if (descLabel != null) descLabel.Text = L("NuGet.Description");
        var depsLabel = this.FindControl<TextBlock>("DependenciesLabelText");
        if (depsLabel != null) depsLabel.Text = L("NuGet.Dependencies");
        var tagsLabel = this.FindControl<TextBlock>("TagsLabelText");
        if (tagsLabel != null) tagsLabel.Text = L("NuGet.Tags");
        var linksLabel = this.FindControl<TextBlock>("LinksLabelText");
        if (linksLabel != null) linksLabel.Text = L("NuGet.Links");
        var projectLinkText = this.FindControl<TextBlock>("ProjectLinkText");
        if (projectLinkText != null) projectLinkText.Text = L("NuGet.ProjectLink");
        var verifiedBadgeText = this.FindControl<TextBlock>("VerifiedBadgeText");
        if (verifiedBadgeText != null) verifiedBadgeText.Text = L("NuGet.Verified");
        var uninstallBtnText = this.FindControl<TextBlock>("UninstallButtonText");
        if (uninstallBtnText != null) uninstallBtnText.Text = L("NuGet.Uninstall");
    }
    
    private void InitializeBrushes()
    {
        _textBrush = new SolidColorBrush(Color.Parse("#FFF0E8F4"));
        _textMutedBrush = new SolidColorBrush(Color.Parse("#FF9E90B0"));
        _accentBrush = new SolidColorBrush(Color.Parse("#FF9B7DCF"));
        _orangeBrush = new SolidColorBrush(Color.Parse("#FFBB9A6F"));
        _greenBrush = new SolidColorBrush(Color.Parse("#FF6AAB73"));
        _blueBrush = new SolidColorBrush(Color.Parse("#FF548AF7"));
    }

    /// <summary>
    /// Set the project path for package operations
    /// </summary>
    public async Task SetProjectPathAsync(string projectPath)
    {
        _projectPath = projectPath;
        
        // Detect if this is a nanoFramework project
        _nugetService.ConfigureForProject(projectPath);
        _isNanoFrameworkProject = _nugetService.IsNanoFrameworkProject;
        
        var projectNameText = this.FindControl<TextBlock>("ProjectNameText");
        if (projectNameText != null)
        {
            var name = Path.GetFileName(projectPath);
            projectNameText.Text = _isNanoFrameworkProject ? $"🔌 {name} (nanoFramework)" : name;
        }
        
        // Update header to show nanoFramework mode
        UpdateHeaderForProjectType();
        
        // Show common nanoFramework packages if no search has been done yet
        if (_isNanoFrameworkProject)
        {
            ShowNanoFrameworkSuggestions();
        }
        
        await RefreshInstalledPackagesAsync();
    }
    
    /// <summary>
    /// Update the panel header to indicate nanoFramework project
    /// </summary>
    private void UpdateHeaderForProjectType()
    {
        var headerText = this.FindControl<TextBlock>("HeaderTitleText");
        if (headerText != null)
        {
            headerText.Text = _isNanoFrameworkProject 
                ? LocalizationService.Get("NuGet.NanoFrameworkMode") 
                : LocalizationService.Get("NuGet.Title");
        }
        
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null)
        {
            searchBox.Watermark = LocalizationService.Get("NuGet.SearchPlaceholder");
        }
        
        var espSuggestionsBtn = this.FindControl<Button>("EspSuggestionsButton");
        if (espSuggestionsBtn != null)
        {
            espSuggestionsBtn.IsVisible = _isNanoFrameworkProject;
        }
    }
    
    /// <summary>
    /// Show common nanoFramework packages as suggestions
    /// </summary>
    private void ShowNanoFrameworkSuggestions()
    {
        var panel = this.FindControl<StackPanel>("SearchResultsPanel");
        if (panel == null) return;
        
        panel.Children.Clear();
        
        var headerPanel = new StackPanel
        {
            Margin = new Thickness(8, 8, 8, 4)
        };
        
        headerPanel.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("NuGet.NanoFramework"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = _accentBrush,
            FontFamily = EmojiFont,
            Margin = new Thickness(0, 0, 0, 4)
        });
        
        headerPanel.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("NuGet.NanoFrameworkDesc"),
            FontSize = 11,
            Foreground = _textMutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        
        panel.Children.Add(headerPanel);
        
        var commonPackages = NuGetService.GetCommonNanoFrameworkPackages();
        _searchResults = commonPackages;
        
        foreach (var package in commonPackages)
        {
            panel.Children.Add(CreatePackageItem(package));
        }
    }
    
    /// <summary>
    /// nanoFramework suggestions button click handler
    /// </summary>
    private void EspSuggestionsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowNanoFrameworkSuggestions();
    }

    #region Tab Switching

    private void BrowseTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("browse");
    }

    private void InstalledTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("installed");
    }

    private void UpdatesTab_Click(object? sender, RoutedEventArgs e)
    {
        SwitchTab("updates");
    }

    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        
        var browseTab    = this.FindControl<Button>("BrowseTab");
        var installedTab = this.FindControl<Button>("InstalledTab");
        var updatesTab   = this.FindControl<Button>("UpdatesTab");
        
        var browsePanel    = this.FindControl<Grid>("BrowsePanel");
        var installedPanel = this.FindControl<Grid>("InstalledPanel");
        var updatesPanel   = this.FindControl<Grid>("UpdatesPanel");
        
        // Hide details panel when switching tabs
        var detailsPanel = this.FindControl<Border>("PackageDetailsPanel");
        if (detailsPanel != null) detailsPanel.IsVisible = false;
        
        // Remove active class from all tabs
        browseTab?.Classes.Remove("active");
        installedTab?.Classes.Remove("active");
        updatesTab?.Classes.Remove("active");
        
        // Hide all panels
        if (browsePanel    != null) browsePanel.IsVisible    = false;
        if (installedPanel != null) installedPanel.IsVisible = false;
        if (updatesPanel   != null) updatesPanel.IsVisible   = false;
        
        // Show selected panel
        switch (tab)
        {
            case "browse":
                browseTab?.Classes.Add("active");
                if (browsePanel != null) browsePanel.IsVisible = true;
                break;
            case "installed":
                installedTab?.Classes.Add("active");
                if (installedPanel != null) installedPanel.IsVisible = true;
                _ = RefreshInstalledPackagesAsync();
                break;
            case "updates":
                updatesTab?.Classes.Add("active");
                if (updatesPanel != null) updatesPanel.IsVisible = true;
                UpdateUpdatesPanel();
                break;
        }
    }

    #endregion

    #region Search

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = PerformSearchAsync();
        }
    }

    private void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = PerformSearchAsync();
    }

    private async Task PerformSearchAsync()
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        var searchTerm = searchBox?.Text?.Trim() ?? "";
        
        if (string.IsNullOrEmpty(searchTerm))
        {
            if (_isNanoFrameworkProject)
            {
                ShowNanoFrameworkSuggestions();
            }
            return;
        }
        
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        
        ShowLoading(string.Format(LocalizationService.Get("NuGet.Searching"), searchTerm));
        
        try
        {
            _searchResults = await _nugetService.SearchPackagesAsync(searchTerm, 0, 50, false, _searchCts.Token);
            UpdateSearchResultsUI();
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled searches
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, string.Format(LocalizationService.Get("NuGet.SearchFailed"), ex.Message));
        }
        finally
        {
            HideLoading();
        }
    }

    private void UpdateSearchResultsUI()
    {
        var panel = this.FindControl<StackPanel>("SearchResultsPanel");
        
        if (panel == null) return;
        
        panel.Children.Clear();
        
        if (_searchResults.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("NuGet.NoPackagesFound"),
                FontSize = 12,
                Foreground = _textMutedBrush,
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(8, 16)
            });
            return;
        }
        
        foreach (var package in _searchResults)
        {
            panel.Children.Add(CreatePackageItem(package));
        }
    }

    private Border CreatePackageItem(NuGetPackageInfo package)
    {
        var border = new Border
        {
            Classes = { "package-item" },
            Tag = package
        };
        
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("40,*,Auto")
        };
        
        // Icon with fallback using Panel overlay pattern
        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#12CBA6F7")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            ClipToBounds = true
        };
        
        var iconPanel = new Panel();
        var defaultIcon = new TextBlock
        {
            Text = "📦",
            FontSize = 16,
            FontFamily = EmojiFont,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var iconImage = new Image
        {
            Stretch = Stretch.Uniform,
            Width = 32,
            Height = 32
        };
        iconPanel.Children.Add(defaultIcon);
        iconPanel.Children.Add(iconImage);
        iconBorder.Child = iconPanel;
        
        // Load icon asynchronously
        _ = LoadPackageIconAsync(package.IconUrl, iconImage, defaultIcon);
        
        // Info
        var infoPanel = new StackPanel
        {
            Margin = new Thickness(8, 0, 0, 0)
        };
        
        // Title row with verified badge
        var titleRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(new TextBlock
        {
            Text = package.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = _textBrush
        });
        
        if (package.IsVerified)
        {
            titleRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#309B7DCF")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "✓",
                    FontSize = 10,
                    Foreground = _accentBrush
                }
            });
        }
        
        infoPanel.Children.Add(titleRow);
        
        // ID and version
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"{package.Id} • {package.Version}",
            FontSize = 11,
            Foreground = _textMutedBrush,
            Margin = new Thickness(0, 2, 0, 0)
        });
        
        // Description (truncated)
        if (!string.IsNullOrEmpty(package.Description))
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = package.Description,
                FontSize = 11,
                Foreground = _textMutedBrush,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        
        // Stats row
        var statsRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        statsRow.Children.Add(new TextBlock
        {
            Text = package.TotalDownloads > 0 ? $"📥 {package.FormattedDownloads}" : "📥 N/A",
            FontSize = 10,
            FontFamily = EmojiFont,
            Foreground = _textMutedBrush
        });
        
        if (!string.IsNullOrEmpty(package.Authors))
        {
            statsRow.Children.Add(new TextBlock
            {
                Text = $"👤 {package.Authors}",
                FontSize = 10,
                FontFamily = EmojiFont,
                Foreground = _textMutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            });
        }
        
        // Published date in search results
        if (package.Published != DateTime.MinValue)
        {
            statsRow.Children.Add(new TextBlock
            {
                Text = $"📅 {FormatRelativeDate(package.Published)}",
                FontSize = 10,
                FontFamily = EmojiFont,
                Foreground = _textMutedBrush
            });
        }
        
        infoPanel.Children.Add(statsRow);
        
        Grid.SetColumn(iconBorder, 0);
        Grid.SetColumn(infoPanel, 1);
        
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel);
        
        border.Child = grid;
        
        border.PointerPressed += (_, _) => _ = ShowSearchPackageDetailsAsync(package);
        
        return border;
    }

    #endregion

    #region Installed Packages

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshInstalledPackagesAsync();
    }

    private async Task RefreshInstalledPackagesAsync()
    {
        if (string.IsNullOrEmpty(_projectPath))
            return;
        
        ShowLoading(LocalizationService.Get("NuGet.LoadingInstalled"));
        
        try
        {
            _installedPackages = await _nugetService.GetInstalledPackagesAsync(_projectPath);
            _updatablePackages = _installedPackages.Where(p => p.HasUpdate).ToList();
            
            UpdateInstalledPackagesUI();
            UpdateUpdatesTabBadge();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, string.Format(LocalizationService.Get("NuGet.LoadPackagesFailed"), ex.Message));
        }
        finally
        {
            HideLoading();
        }
    }

    private void UpdateInstalledPackagesUI()
    {
        var panel = this.FindControl<StackPanel>("InstalledPackagesPanel");
        
        if (panel == null) return;        
        panel.Children.Clear();
        
        if (_installedPackages.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("NuGet.NoPackages"),
                FontSize = 12,
                Foreground = _textMutedBrush,
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(8, 16)
            });
            return;
        }
        
        // Show count header
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(LocalizationService.Get("NuGet.InstalledCount"), _installedPackages.Count),
            FontSize = 11,
            Foreground = _textMutedBrush,
            Margin = new Thickness(8, 4, 0, 4)
        });
        
        foreach (var package in _installedPackages)
        {
            panel.Children.Add(CreateInstalledPackageItem(package));
        }
    }

    private Border CreateInstalledPackageItem(InstalledNuGetPackage package)
    {
        var border = new Border
        {
            Classes = { "package-item" },
            Tag = package
        };
        
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("40,*,Auto")
        };
        
        // Icon with fallback using Panel overlay pattern
        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#12CBA6F7")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            ClipToBounds = true
        };
        
        var iconPanel = new Panel();
        var defaultIcon = new TextBlock
        {
            Text = "📦",
            FontSize = 16,
            FontFamily = EmojiFont,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var iconImage = new Image
        {
            Stretch = Stretch.Uniform,
            Width = 32,
            Height = 32
        };
        iconPanel.Children.Add(defaultIcon);
        iconPanel.Children.Add(iconImage);
        iconBorder.Child = iconPanel;
        
        // Load icon asynchronously
        _ = LoadPackageIconAsync(package.IconUrl, iconImage, defaultIcon);
        
        // Info
        var infoPanel = new StackPanel
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        
        // Title
        infoPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(package.Title) ? package.Id : package.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = _textBrush
        });
        
        // Version with update indicator
        var versionText = package.Version;
        if (package.HasUpdate)
        {
            versionText += $" → {package.LatestVersion}";
        }
        
        var versionTextBlock = new TextBlock
        {
            Text = versionText,
            FontSize = 11,
            Foreground = package.HasUpdate ? _orangeBrush : _textMutedBrush,
            Margin = new Thickness(0, 2, 0, 0)
        };
        infoPanel.Children.Add(versionTextBlock);
        
        // Description
        if (!string.IsNullOrEmpty(package.Description))
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = package.Description,
                FontSize = 10,
                Foreground = _textMutedBrush,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }
        
        // Actions
        var actionsPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        
        if (package.HasUpdate)
        {
            var updateBtn = new Button
            {
                Classes = { "nuget-update" },
                Content = LocalizationService.Get("NuGet.Update"),
                Tag = package,
                FontSize = 11
            };
            updateBtn.Click += async (_, _) =>
            {
                await UpdatePackageAsync(package);
            };
            actionsPanel.Children.Add(updateBtn);
        }
        
        var uninstallBtn = new Button
        {
            Classes = { "nuget-secondary" },
            Tag = package,
            Padding = new Thickness(6, 4)
        };
        uninstallBtn.Content = new TextBlock
        {
            Text = "🗑️",
            FontSize = 12,
            FontFamily = EmojiFont
        };
        ToolTip.SetTip(uninstallBtn, LocalizationService.Get("NuGet.Uninstall"));
        uninstallBtn.Click += async (_, _) =>
        {
            await UninstallPackageAsync(package);
        };
        actionsPanel.Children.Add(uninstallBtn);
        
        Grid.SetColumn(iconBorder, 0);
        Grid.SetColumn(infoPanel, 1);
        Grid.SetColumn(actionsPanel, 2);
        
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel);
        grid.Children.Add(actionsPanel);
        
        border.Child = grid;
        
        // Click on installed package to open details view
        border.PointerPressed += (_, e) =>
        {
            // Only open details on left click, not when clicking buttons
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                _ = ShowInstalledPackageDetailsAsync(package);
            }
        };
        
        return border;
    }
    
    /// <summary>
    /// Show details for a search result package, fetching full metadata (including published date) from NuGet
    /// </summary>
    private async Task ShowSearchPackageDetailsAsync(NuGetPackageInfo package)
    {
        // Show immediately with what we have, then update with full details
        ShowPackageDetails(package);

        // If published date is missing, fetch full details to get it
        if (package.Published == DateTime.MinValue)
        {
            try
            {
                var details = await _nugetService.GetPackageDetailsAsync(package.Id);
                if (details != null && details.Published != DateTime.MinValue)
                {
                    // Update the package object and refresh the displayed date
                    package.Published = details.Published;
                    var publishedText = this.FindControl<TextBlock>("DetailPublishedText");
                    if (publishedText != null && _selectedPackage?.Id == package.Id)
                    {
                        publishedText.Text = package.Published.ToString("MMM dd, yyyy");
                    }
                }
            }
            catch
            {
                // Ignore — date will remain "—"
            }
        }
    }

    /// <summary>
    /// Fetch full details for an installed package and show the details panel
    /// </summary>
    private async Task ShowInstalledPackageDetailsAsync(InstalledNuGetPackage installedPackage)
    {
        ShowLoading(string.Format(LocalizationService.Get("NuGet.LoadingDetails"), installedPackage.Id));
        
        try
        {
            // Try cache but only if it has a valid published date
            var cachedInfo = _searchResults.FirstOrDefault(p => 
                p.Id.Equals(installedPackage.Id, StringComparison.OrdinalIgnoreCase) &&
                p.Published != DateTime.MinValue);
            
            if (cachedInfo != null)
            {
                HideLoading();
                ShowPackageDetails(cachedInfo);
                return;
            }
            
            // Fetch full details from NuGet (includes correct published date)
            var details = await _nugetService.GetPackageDetailsAsync(installedPackage.Id);
            if (details != null)
            {
                HideLoading();
                ShowPackageDetails(details);
            }
            else
            {
                // Create a minimal NuGetPackageInfo from installed package data
                var minimalInfo = new NuGetPackageInfo
                {
                    Id = installedPackage.Id,
                    Version = installedPackage.Version,
                    Title = string.IsNullOrEmpty(installedPackage.Title) ? installedPackage.Id : installedPackage.Title,
                    Description = installedPackage.Description ?? "",
                    IconUrl = installedPackage.IconUrl ?? "",
                    AllVersions = new List<string> { installedPackage.Version }
                };
                
                if (!string.IsNullOrEmpty(installedPackage.LatestVersion) && installedPackage.HasUpdate)
                {
                    minimalInfo.AllVersions.Insert(0, installedPackage.LatestVersion);
                }
                
                HideLoading();
                ShowPackageDetails(minimalInfo);
            }
        }
        catch (Exception ex)
        {
            HideLoading();
            ErrorOccurred?.Invoke(this, string.Format(LocalizationService.Get("NuGet.LoadDetailsFailed"), ex.Message));
        }
    }

    private void UpdateUpdatesTabBadge()
    {
        var updatesTabText = this.FindControl<TextBlock>("UpdatesTabText");
        if (updatesTabText != null)
        {
            updatesTabText.Text = _updatablePackages.Count > 0 
                ? string.Format(LocalizationService.Get("NuGet.UpdatesTabBadge"), _updatablePackages.Count)
                : LocalizationService.Get("NuGet.Updates");
        }
    }

    private void UpdateUpdatesPanel()
    {
        var panel = this.FindControl<StackPanel>("UpdatesPackagesPanel");
        var countText = this.FindControl<TextBlock>("UpdatesCountText");
        var updateAllBtn = this.FindControl<Button>("UpdateAllButton");
        
        if (panel == null) return;
        
        panel.Children.Clear();
        
        if (countText != null)
        {
            countText.Text = string.Format(LocalizationService.Get("NuGet.UpdatesAvailable"), _updatablePackages.Count);
        }
        
        if (updateAllBtn != null)
        {
            updateAllBtn.IsVisible = _updatablePackages.Count > 0;
        }
        
        if (_updatablePackages.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("NuGet.AllUpToDateCheck"),
                FontSize = 12,
                FontFamily = EmojiFont,
                Foreground = _greenBrush,
                Margin = new Thickness(8, 16)
            });
            return;
        }
        
        foreach (var package in _updatablePackages)
        {
            panel.Children.Add(CreateInstalledPackageItem(package));
        }
    }

    #endregion

    #region Package Details

    private void ShowPackageDetails(NuGetPackageInfo package)
    {
        _selectedPackage = package;
        
        var detailsPanel = this.FindControl<Border>("PackageDetailsPanel");
        if (detailsPanel == null) return;
        
        // Update UI
        var titleText = this.FindControl<TextBlock>("DetailTitleText");
        var authorText = this.FindControl<TextBlock>("DetailAuthorText");
        var descText = this.FindControl<TextBlock>("DetailDescriptionText");
        var downloadsText = this.FindControl<TextBlock>("DetailDownloadsText");
        var publishedText = this.FindControl<TextBlock>("DetailPublishedText");
        var versionCombo = this.FindControl<ComboBox>("VersionComboBox");
        var iconImage = this.FindControl<Image>("DetailIconImage");
        var defaultIcon = this.FindControl<TextBlock>("DetailDefaultIcon");
        var installBtn = this.FindControl<Button>("InstallButton");
        var installBtnText = this.FindControl<TextBlock>("InstallButtonText");
        var uninstallBtn = this.FindControl<Button>("UninstallButton");
        var verifiedBadge = this.FindControl<Border>("DetailVerifiedBadge");
        var installedVersionText = this.FindControl<TextBlock>("DetailInstalledVersionText");
        
        if (titleText != null) titleText.Text = package.Title;
        if (authorText != null) authorText.Text = !string.IsNullOrEmpty(package.Authors) ? string.Format(LocalizationService.Get("NuGet.ByAuthor"), package.Authors) : "";
        if (descText != null) descText.Text = !string.IsNullOrEmpty(package.Description) ? package.Description : LocalizationService.Get("NuGet.NoDescription");
        
        // Downloads: show formatted number or "N/A" when unavailable
        if (downloadsText != null)
        {
            downloadsText.Text = package.TotalDownloads > 0 ? package.FormattedDownloads : "N/A";
        }
        
        // Format published date properly
        if (publishedText != null)
        {
            publishedText.Text = package.Published != DateTime.MinValue
                ? package.Published.ToString("MMM dd, yyyy")
                : "—";
        }
        
        // Verified badge
        if (verifiedBadge != null)
        {
            verifiedBadge.IsVisible = package.IsVerified;
        }
        
        // Load icon with fallback
        if (iconImage != null)
        {
            _ = LoadPackageIconAsync(package.IconUrl, iconImage, defaultIcon);
        }
        
        // Populate versions
        if (versionCombo != null)
        {
            versionCombo.ItemsSource = package.AllVersions;
            versionCombo.SelectedIndex = 0;
        }
        
        // Update tags
        UpdateTagsPanel(package.Tags);
        
        // Check if already installed
        var installedPkg = _installedPackages.FirstOrDefault(p => 
            p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
        var isInstalled = installedPkg != null;
        
        // Configure Install/Uninstall buttons
        if (isInstalled)
        {
            if (installBtnText != null) installBtnText.Text = installedPkg!.HasUpdate ? LocalizationService.Get("NuGet.Update") : LocalizationService.Get("NuGet.Reinstall");
            if (installBtn != null)
            {
                installBtn.Classes.Clear();
                installBtn.Classes.Add(installedPkg!.HasUpdate ? "nuget-update" : "nuget-secondary");
            }
            if (uninstallBtn != null) uninstallBtn.IsVisible = true;
            if (installedVersionText != null)
            {
                installedVersionText.Text = string.Format(LocalizationService.Get("NuGet.InstalledVersion"), installedPkg!.Version);
                installedVersionText.IsVisible = true;
            }
        }
        else
        {
            if (installBtnText != null) installBtnText.Text = LocalizationService.Get("NuGet.Install");
            if (installBtn != null)
            {
                installBtn.Classes.Clear();
                installBtn.Classes.Add("nuget-primary");
            }
            if (uninstallBtn != null) uninstallBtn.IsVisible = false;
            if (installedVersionText != null) installedVersionText.IsVisible = false;
        }
        
        detailsPanel.IsVisible = true;
    }

    private void UpdateTagsPanel(string tags)
    {
        var tagsPanel = this.FindControl<StackPanel>("TagsPanel");
        var wrapPanel = this.FindControl<WrapPanel>("TagsWrapPanel");
        
        if (wrapPanel == null || tagsPanel == null) return;
        
        wrapPanel.Children.Clear();
        
        if (string.IsNullOrEmpty(tags))
        {
            tagsPanel.IsVisible = false;
            return;
        }
        
        tagsPanel.IsVisible = true;
        var tagList = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(10);
        
        foreach (var tag in tagList)
        {
            var tagBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#209B7DCF")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3),
                Margin = new Thickness(0, 0, 4, 4)
            };
            
            tagBorder.Child = new TextBlock
            {
                Text = tag,
                FontSize = 10,
                Foreground = _accentBrush
            };
            
            wrapPanel.Children.Add(tagBorder);
        }
    }

    private void CloseDetailsButton_Click(object? sender, RoutedEventArgs e)
    {
        var detailsPanel = this.FindControl<Border>("PackageDetailsPanel");
        if (detailsPanel != null) detailsPanel.IsVisible = false;
    }

    private void ProjectLinkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedPackage != null && !string.IsNullOrEmpty(_selectedPackage.ProjectUrl))
        {
            OpenUrl(_selectedPackage.ProjectUrl);
        }
    }
    
    private void NuGetOrgLink_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedPackage != null)
        {
            OpenUrl($"https://www.nuget.org/packages/{_selectedPackage.Id}");
        }
    }

    private async void InstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedPackage == null || string.IsNullOrEmpty(_projectPath))
            return;
        
        var versionCombo = this.FindControl<ComboBox>("VersionComboBox");
        var version = versionCombo?.SelectedItem?.ToString();
        
        await InstallPackageAsync(_selectedPackage.Id, version);
    }
    
    private async void UninstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedPackage == null || string.IsNullOrEmpty(_projectPath))
            return;
        
        await UninstallPackageAsync(_selectedPackage.Id);
    }

    #endregion

    #region Package Operations

    private async Task InstallPackageAsync(string packageId, string? version = null)
    {
        if (string.IsNullOrEmpty(_projectPath))
        {
            ErrorOccurred?.Invoke(this, LocalizationService.Get("NuGet.NoProject.Error"));
            return;
        }
        
        ShowLoading(string.Format(LocalizationService.Get("NuGet.Installing"), packageId));
        
        try
        {
            var success = await _nugetService.InstallPackageAsync(_projectPath, packageId, version);
            
            if (success)
            {
                StatusChanged?.Invoke(this, string.Format(LocalizationService.Get("NuGet.SuccessInstalled"), packageId));
                await RefreshInstalledPackagesAsync();
                
                // Close details panel
                var detailsPanel = this.FindControl<Border>("PackageDetailsPanel");
                if (detailsPanel != null) detailsPanel.IsVisible = false;
            }
        }
        finally
        {
            HideLoading();
        }
    }

    private async Task UninstallPackageAsync(InstalledNuGetPackage package)
    {
        await UninstallPackageAsync(package.Id);
    }

    private async Task UninstallPackageAsync(string packageId)
    {
        if (string.IsNullOrEmpty(_projectPath))
        {
            ErrorOccurred?.Invoke(this, LocalizationService.Get("NuGet.NoProject.Error"));
            return;
        }
        
        ShowLoading(string.Format(LocalizationService.Get("NuGet.Uninstalling"), packageId));
        
        try
        {
            var success = await _nugetService.UninstallPackageAsync(_projectPath, packageId);
            
            if (success)
            {
                StatusChanged?.Invoke(this, string.Format(LocalizationService.Get("NuGet.SuccessUninstalled"), packageId));
                await RefreshInstalledPackagesAsync();
                
                // Close details panel
                var detailsPanel = this.FindControl<Border>("PackageDetailsPanel");
                if (detailsPanel != null) detailsPanel.IsVisible = false;
            }
        }
        finally
        {
            HideLoading();
        }
    }

    private async Task UpdatePackageAsync(InstalledNuGetPackage package)
    {
        if (string.IsNullOrEmpty(_projectPath))
        {
            ErrorOccurred?.Invoke(this, LocalizationService.Get("NuGet.NoProject.Error"));
            return;
        }
        
        ShowLoading(string.Format(LocalizationService.Get("NuGet.Updating"), package.Id));
        
        try
        {
            var success = await _nugetService.UpdatePackageAsync(_projectPath, package.Id, package.LatestVersion);
            
            if (success)
            {
                StatusChanged?.Invoke(this, string.Format(LocalizationService.Get("NuGet.SuccessUpdated"), package.Id));
                await RefreshInstalledPackagesAsync();
            }
        }
        finally
        {
            HideLoading();
        }
    }

    private async void UpdateAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_updatablePackages.Count == 0)
            return;
        
        ShowLoading(LocalizationService.Get("NuGet.UpdatingAll"));
        
        try
        {
            var total = _updatablePackages.Count;
            var current = 0;
            
            foreach (var package in _updatablePackages.ToList())
            {
                current++;
                ShowLoading(string.Format(LocalizationService.Get("NuGet.UpdatingCount"), package.Id, current, total));
                
                await _nugetService.UpdatePackageAsync(_projectPath!, package.Id, package.LatestVersion);
            }
            
            StatusChanged?.Invoke(this, string.Format(LocalizationService.Get("NuGet.SuccessUpdatedCount"), total));
            await RefreshInstalledPackagesAsync();
        }
        finally
        {
            HideLoading();
        }
    }

    #endregion

    #region Helpers

    private void ShowLoading(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var overlay = this.FindControl<Border>("LoadingOverlay");
            var loadingText = this.FindControl<TextBlock>("LoadingText");
            
            if (loadingText != null) loadingText.Text = message;
            if (overlay != null) overlay.IsVisible = true;
        });
    }

    private void HideLoading()
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var overlay = this.FindControl<Border>("LoadingOverlay");
            if (overlay != null) overlay.IsVisible = false;
        });
    }

    private async Task LoadPackageIconAsync(string? iconUrl, Image imageControl, TextBlock? fallbackIcon = null)
    {
        if (string.IsNullOrEmpty(iconUrl))
        {
            // Show fallback, hide image
            if (fallbackIcon != null) fallbackIcon.IsVisible = true;
            return;
        }
        
        try
        {
            // Check cache
            if (_iconCache.TryGetValue(iconUrl, out var cachedBitmap))
            {
                if (cachedBitmap != null)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        imageControl.Source = cachedBitmap;
                        if (fallbackIcon != null) fallbackIcon.IsVisible = false;
                    });
                }
                return;
            }
            
            var response = await _httpClient.GetAsync(iconUrl);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                
                var bitmap = new Bitmap(memoryStream);
                
                _iconCache[iconUrl] = bitmap;
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    imageControl.Source = bitmap;
                    if (fallbackIcon != null) fallbackIcon.IsVisible = false;
                });
            }
            else
            {
                _iconCache[iconUrl] = null;
                // Keep fallback visible
            }
        }
        catch
        {
            _iconCache[iconUrl] = null;
            // Keep fallback visible
        }
    }
    
    /// <summary>
    /// Format a date as relative time (e.g., "2 days ago", "3 months ago")
    /// </summary>
    private static string FormatRelativeDate(DateTime date)
    {
        var span = DateTime.Now - date;
        
        if (span.TotalDays < 1) return LocalizationService.Get("NuGet.RelativeDate.Today");
        if (span.TotalDays < 2) return LocalizationService.Get("NuGet.RelativeDate.Yesterday");
        if (span.TotalDays < 7) return string.Format(LocalizationService.Get("NuGet.RelativeDate.DaysAgo"), (int)span.TotalDays);
        if (span.TotalDays < 30) return string.Format(LocalizationService.Get("NuGet.RelativeDate.WeeksAgo"), (int)(span.TotalDays / 7));
        if (span.TotalDays < 365) return string.Format(LocalizationService.Get("NuGet.RelativeDate.MonthsAgo"), (int)(span.TotalDays / 30));
        return string.Format(LocalizationService.Get("NuGet.RelativeDate.YearsAgo"), (int)(span.TotalDays / 365));
    }

    private void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, string.Format(LocalizationService.Get("NuGet.OpenUrlFailed"), ex.Message));
        }
    }

    #endregion
}

