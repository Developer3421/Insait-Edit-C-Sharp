using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Insait_Program_Image_Editor;

public partial class MainWindow : Window
{
    // ── State ────────────────────────────────────────────────────────────
    private string? _currentFilePath;
    private string  _lastError = string.Empty;
    private bool    _darkBg = true;
    private (double W, double H)? _fixedSize;
    private bool    _isMaximized;
    private PixelPoint _restorePos;
    private Size       _restoreSize;

    // ── AXAML runtime loader (reflection-cached) ────────────────────────
    private static MethodInfo? _loaderMethod;
    private static readonly object _loaderLock = new();

    // ── Known event attributes to strip from AXAML ──────────────────────
    private static readonly HashSet<string> EventAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Click","PointerPressed","PointerReleased","PointerMoved","PointerEntered","PointerExited",
        "Tapped","DoubleTapped","KeyDown","KeyUp","TextChanged","TextInput",
        "SelectionChanged","SelectionChanging","ValueChanged","Checked","Unchecked",
        "IsCheckedChanged","Loaded","Unloaded","Initialized","AttachedToVisualTree",
        "DetachedFromVisualTree","SizeChanged","LayoutUpdated","GotFocus","LostFocus",
        "ScrollChanged","ItemsChanged","ContainerPrepared","ContainerIndexChanged",
        "DropDownOpened","DropDownClosed","Opening","Closing","Opened","Closed",
        "ManipulationStarted","ManipulationDelta","ManipulationCompleted",
    };

    // Default AXAML template
    private const string DefaultAxaml = """
        <Border xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Background="#FF1F1A24" Padding="24">
            <StackPanel Spacing="12" HorizontalAlignment="Center" VerticalAlignment="Center">
                <TextBlock Text="🎨 Insait Image Editor"
                           FontSize="28" FontWeight="Bold"
                           Foreground="#FFFFC09F" HorizontalAlignment="Center"/>
                <TextBlock Text="Edit AXAML code on the left, see live preview here."
                           FontSize="14" Foreground="#FF9E90B0"
                           HorizontalAlignment="Center" TextWrapping="Wrap"/>
                <Border Background="#FF8B5CF6" CornerRadius="6" Padding="16,8"
                        HorizontalAlignment="Center">
                    <TextBlock Text="✦ Powered by Avalonia &amp; GitHub Copilot"
                               FontSize="13" Foreground="White" FontWeight="SemiBold"/>
                </Border>
            </StackPanel>
        </Border>
        """;

    // ═════════════════════════════════════════════════════════════════════
    //  Constructor
    // ═════════════════════════════════════════════════════════════════════
    public MainWindow()
    {
        InitializeComponent();

        var editor = this.FindControl<TextBox>("CodeEditor");
        if (editor != null)
        {
            editor.Text = DefaultAxaml;
        }

        _restoreSize = new Size(Width, Height);

        // Initial preview after layout
        Dispatcher.UIThread.Post(() => RefreshPreview(), DispatcherPriority.Loaded);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Title Bar
    // ═════════════════════════════════════════════════════════════════════
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isMaximized)
        {
            _restorePos  = Position;
            _restoreSize = new Size(Width, Height);
            WindowState  = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            Position    = _restorePos;
            Width       = _restoreSize.Width;
            Height      = _restoreSize.Height;
        }
        _isMaximized = !_isMaximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    // ═════════════════════════════════════════════════════════════════════
    //  File Operations
    // ═════════════════════════════════════════════════════════════════════
    private void NewFile_Click(object? sender, RoutedEventArgs e)
    {
        _currentFilePath = null;
        var editor = this.FindControl<TextBox>("CodeEditor");
        if (editor != null) editor.Text = DefaultAxaml;
        UpdateFileLabel("untitled.axaml");
        SetStatus("New file created", "#FFA6E3A1");
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AXAML File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("AXAML Files") { Patterns = new[] { "*.axaml", "*.xaml" } },
                new FilePickerFileType("All Files")   { Patterns = new[] { "*" } },
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            try
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                _currentFilePath = path;
                var editor = this.FindControl<TextBox>("CodeEditor");
                if (editor != null) editor.Text = content;
                UpdateFileLabel(Path.GetFileName(path));
                SetStatus($"Opened: {Path.GetFileName(path)}", "#FFA6E3A1");
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", "#FFF38BA8");
            }
        }
    }

    private async void SaveFile_Click(object? sender, RoutedEventArgs e)
    {
        var editor = this.FindControl<TextBox>("CodeEditor");
        var content = editor?.Text ?? string.Empty;

        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            try
            {
                await File.WriteAllTextAsync(_currentFilePath, content, Encoding.UTF8);
                SetStatus($"Saved: {Path.GetFileName(_currentFilePath)}", "#FFA6E3A1");
                return;
            }
            catch (Exception ex)
            {
                SetStatus($"Save error: {ex.Message}", "#FFF38BA8");
                return;
            }
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AXAML File",
            DefaultExtension = "axaml",
            SuggestedFileName = "design",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("AXAML Files") { Patterns = new[] { "*.axaml" } },
            }
        });

        if (file != null)
        {
            try
            {
                _currentFilePath = file.Path.LocalPath;
                await File.WriteAllTextAsync(_currentFilePath, content, Encoding.UTF8);
                UpdateFileLabel(Path.GetFileName(_currentFilePath));
                SetStatus($"Saved: {Path.GetFileName(_currentFilePath)}", "#FFA6E3A1");
            }
            catch (Exception ex)
            {
                SetStatus($"Save error: {ex.Message}", "#FFF38BA8");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Editor Events
    // ═════════════════════════════════════════════════════════════════════
    private DispatcherTimer? _debounceTimer;

    private void CodeEditor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // Update cursor position
        var editor = this.FindControl<TextBox>("CodeEditor");
        if (editor != null)
        {
            var text = editor.Text ?? string.Empty;
            var pos = editor.CaretIndex;
            int line = 1, col = 1;
            for (int i = 0; i < pos && i < text.Length; i++)
            {
                if (text[i] == '\n') { line++; col = 1; }
                else col++;
            }
            var lbl = this.FindControl<TextBlock>("CursorPosLabel");
            if (lbl != null) lbl.Text = $"Ln {line}, Col {col}";
        }

        // Debounce preview refresh (500ms)
        _debounceTimer?.Stop();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer?.Stop();
            RefreshPreview();
        };
        _debounceTimer.Start();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Preview
    // ═════════════════════════════════════════════════════════════════════
    private void Refresh_Click(object? sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        var editor = this.FindControl<TextBox>("CodeEditor");
        var xaml = editor?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(xaml))
        {
            SetPreviewStatus("Empty");
            return;
        }

        try
        {
            var sanitised = Sanitise(xaml);
            var control = TryLoadXaml(sanitised, out string? error);

            if (control != null)
            {
                InjectAppResources(control);
                SetPreviewContent(control);
                SetPreviewStatus("Live ✓");
                ClearError();
                SetStatus("Preview OK", "#FFA6E3A1");
            }
            else
            {
                SetPreviewContent(BuildFallback(xaml, error));
                SetError(error ?? "Unknown error");
                SetPreviewStatus("Fallback");
                SetStatus("Preview: fallback mode", "#FFFFC09F");
            }
        }
        catch (Exception ex)
        {
            SetPreviewContent(BuildFallback(xaml, ex.Message));
            SetError(ex.Message);
            SetPreviewStatus("Error");
            SetStatus("Preview error", "#FFF38BA8");
        }

        ApplyCanvasSize();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  AXAML Sanitisation (from AxamlLiveHost)
    // ═════════════════════════════════════════════════════════════════════
    private static string Sanitise(string xaml)
    {
        var doc  = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var root = doc.Root!;

        XNamespace xns  = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace mc   = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        XNamespace d    = "http://schemas.microsoft.com/expression/blend/2008";
        XNamespace avns = "https://github.com/avaloniaui";

        root.Attribute(xns + "Class")?.Remove();
        root.Attribute(mc  + "Ignorable")?.Remove();
        foreach (var a in root.Attributes().Where(a => a.Name.Namespace == d).ToList())
            a.Remove();

        StripEventHandlers(root, xns);

        string tag = root.Name.LocalName;
        if (tag is not ("Window" or "UserControl"))
            return doc.ToString(SaveOptions.DisableFormatting);

        // Rewrite Window / UserControl → Border
        var nsDecls     = root.Attributes().Where(a => a.IsNamespaceDeclaration).ToList();
        var styleEls    = root.Elements().Where(e => e.Name.LocalName.EndsWith(".Styles")).ToList();
        var resourceEls = root.Elements().Where(e => e.Name.LocalName.EndsWith(".Resources")).ToList();
        var bgEls       = root.Elements().Where(e => e.Name.LocalName.EndsWith(".Background")).ToList();
        var children    = root.Elements().Where(e => !e.Name.LocalName.Contains('.')).ToList();

        var border = new XElement(avns + "Border");
        foreach (var ns in nsDecls) border.Add(ns);

        foreach (var attr in new[] { "Width","Height","Background","MinWidth","MinHeight" })
        {
            var v = root.Attribute(attr)?.Value;
            if (v != null) border.Add(new XAttribute(attr, v));
        }

        if (styleEls.Any())
        {
            var bs = new XElement(avns + "Border.Styles");
            foreach (var se in styleEls)
                foreach (var c in se.Elements()) bs.Add(new XElement(c));
            border.Add(bs);
        }

        var allRes = resourceEls.SelectMany(r => r.Elements()).ToList();
        if (allRes.Any())
        {
            var br = new XElement(avns + "Border.Resources");
            foreach (var re in allRes) br.Add(new XElement(re));
            border.Add(br);
        }

        foreach (var bg in bgEls)
        {
            var renamed = new XElement(avns + "Border.Background");
            foreach (var c in bg.Elements()) renamed.Add(new XElement(c));
            border.Add(renamed);
        }

        if (children.Count == 1)
            border.Add(new XElement(children[0]));
        else if (children.Count > 1)
        {
            var grid = new XElement(avns + "Grid");
            foreach (var c in children) grid.Add(new XElement(c));
            border.Add(grid);
        }

        return new XDocument(border).ToString(SaveOptions.DisableFormatting);
    }

    private static void StripEventHandlers(XElement el, XNamespace xns)
    {
        XNamespace avns = "https://github.com/avaloniaui";

        foreach (var child in el.Elements().ToList())
        {
            var childNs = child.Name.Namespace.NamespaceName;
            bool isAvalonia = childNs == avns.NamespaceName || childNs == string.Empty;
            bool isPropEl   = child.Name.LocalName.Contains('.');

            if (!isAvalonia && !isPropEl)
            {
                var placeholder = new XElement(avns + "Border",
                    new XAttribute("Background", "#20FFC09F"),
                    new XAttribute("BorderBrush", "#60FFC09F"),
                    new XAttribute("BorderThickness", "1"),
                    new XAttribute("Margin", "2"),
                    new XElement(avns + "TextBlock",
                        new XAttribute("Text", $"[{child.Name.LocalName}]"),
                        new XAttribute("Foreground", "#FFFFC09F"),
                        new XAttribute("FontSize", "11"),
                        new XAttribute("HorizontalAlignment", "Center"),
                        new XAttribute("VerticalAlignment", "Center"),
                        new XAttribute("Margin", "8,4")));

                foreach (var attr in child.Attributes())
                {
                    if (attr.IsNamespaceDeclaration) continue;
                    var name = attr.Name.LocalName;
                    if (name is "Width" or "Height" or "MinWidth" or "MinHeight" or
                        "Margin" or "HorizontalAlignment" or "VerticalAlignment" or
                        "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan" or
                        "DockPanel.Dock")
                        placeholder.Add(new XAttribute(attr.Name, attr.Value));
                }

                child.ReplaceWith(placeholder);
                continue;
            }

            StripEventHandlers(child, xns);
        }

        foreach (var attr in el.Attributes().ToList())
        {
            if (attr.IsNamespaceDeclaration) continue;
            if (attr.Name.Namespace != XNamespace.None) continue;
            if (EventAttributes.Contains(attr.Name.LocalName))
                attr.Remove();
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Runtime XAML Loader (reflection)
    // ═════════════════════════════════════════════════════════════════════
    private static MethodInfo? GetLoaderMethod()
    {
        lock (_loaderLock)
        {
            if (_loaderMethod != null) return _loaderMethod;

            foreach (var name in new[] { "Avalonia.Markup.Xaml.Loader", "Avalonia.Markup.Xaml" })
            {
                try
                {
                    if (AppDomain.CurrentDomain.GetAssemblies().All(a => a.GetName().Name != name))
                        Assembly.Load(new AssemblyName(name));
                }
                catch { }
            }

            string[] candidates =
            {
                "Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml",
                "Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml.Loader",
                "Avalonia.Markup.Xaml.Loader.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml.Loader",
            };

            foreach (var c in candidates)
            {
                var t = Type.GetType(c, throwOnError: false);
                if (t == null) continue;
                var m = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .FirstOrDefault(mi => mi.Name == "Load" &&
                             mi.GetParameters() is { Length: >= 1 } ps &&
                             ps[0].ParameterType == typeof(string));
                if (m != null) { _loaderMethod = m; return m; }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetExportedTypes(); } catch { continue; }
                foreach (var t in types.Where(t => t.Name == "AvaloniaRuntimeXamlLoader"))
                {
                    var m = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .FirstOrDefault(mi => mi.Name == "Load" &&
                                 mi.GetParameters() is { Length: >= 1 } ps &&
                                 ps[0].ParameterType == typeof(string));
                    if (m != null) { _loaderMethod = m; return m; }
                }
            }

            return null;
        }
    }

    private static Control? TryLoadXaml(string xaml, out string? error)
    {
        error = null;
        var method = GetLoaderMethod();
        if (method == null)
        {
            error = "AvaloniaRuntimeXamlLoader not found — Avalonia.Markup.Xaml.Loader package required.";
            return null;
        }

        try
        {
            var prms = method.GetParameters();
            var args = new object?[prms.Length];
            for (int i = 0; i < prms.Length; i++)
            {
                args[i] = prms[i].ParameterType switch
                {
                    var pt when pt == typeof(string)   => xaml,
                    var pt when pt == typeof(Assembly)  => Assembly.GetExecutingAssembly(),
                    var pt when pt == typeof(Uri)       => (Uri?)null,
                    var pt when pt == typeof(bool)      => false,
                    _                                   => null
                };
            }

            var result = method.Invoke(null, args);
            if (result is Control ctrl) return ctrl;

            error = $"Loader returned {result?.GetType().Name ?? "null"}, expected Control.";
            return null;
        }
        catch (TargetInvocationException tie)
        {
            error = tie.InnerException?.Message ?? tie.Message;
            return null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static void InjectAppResources(Control ctrl)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (var res in app.Resources.MergedDictionaries)
                if (!ctrl.Resources.MergedDictionaries.Contains(res))
                    ctrl.Resources.MergedDictionaries.Add(res);
            foreach (var style in app.Styles)
                if (!ctrl.Styles.Contains(style))
                    ctrl.Styles.Add(style);
        }
        catch { }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Fallback XML tree view
    // ═════════════════════════════════════════════════════════════════════
    private static Control BuildFallback(string xaml, string? reason)
    {
        var root = new StackPanel
        {
            Spacing    = 0,
            Background = new SolidColorBrush(Color.Parse("#FF1A1624"))
        };

        if (!string.IsNullOrEmpty(reason))
        {
            root.Children.Add(new Border
            {
                Background      = new SolidColorBrush(Color.Parse("#25F38BA8")),
                BorderBrush     = new SolidColorBrush(Color.Parse("#60F38BA8")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(12, 8),
                Child = new TextBlock
                {
                    Text         = $"⚠  Cannot render — showing structure\n{reason.Split('\n').FirstOrDefault()}",
                    Foreground   = new SolidColorBrush(Color.Parse("#FFF38BA8")),
                    FontSize     = 11,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily   = new FontFamily("Cascadia Code,Consolas,monospace")
                }
            });
        }

        try
        {
            var doc = XDocument.Parse(xaml);
            if (doc.Root != null)
            {
                var panel = new StackPanel { Spacing = 1, Margin = new Thickness(8) };
                RenderNode(panel, doc.Root, 0);
                root.Children.Add(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = panel
                });
            }
        }
        catch (Exception ex)
        {
            root.Children.Add(new TextBlock
            {
                Text       = $"XML error: {ex.Message}",
                Foreground = new SolidColorBrush(Color.Parse("#FFF38BA8")),
                FontSize   = 11,
                Margin     = new Thickness(12),
                FontFamily = new FontFamily("Cascadia Code,Consolas,monospace")
            });
        }

        return root;
    }

    private static void RenderNode(StackPanel panel, XElement el, int depth)
    {
        if (depth > 40) return;
        string tag    = el.Name.LocalName;
        string indent = new string(' ', depth * 2);
        bool   isProp = tag.Contains('.');
        bool   hasKid = el.HasElements;

        string colour = isProp           ? "#FF585B70"
            : depth == 0                 ? "#FFFFC09F"
            : tag is "Grid" or "StackPanel" or "DockPanel" or "WrapPanel" ? "#FF89DCEB"
            : tag is "Border" or "ScrollViewer"                           ? "#FFCBA6F7"
            : tag is "TextBlock" or "Label" or "TextBox"                  ? "#FFF5C2E7"
            : tag is "Button" or "CheckBox" or "RadioButton" or "ComboBox"? "#FFFFC09F"
            : tag is "Image" or "Rectangle" or "Ellipse" or "Canvas"     ? "#FF94E2D5"
                                                                          : "#FFCDD6F4";

        var sb = new StringBuilder();
        foreach (var a in el.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            if (sb.Length > 100) { sb.Append(" …"); break; }
            sb.Append($" {a.Name.LocalName}=\"{a.Value}\"");
        }

        string? inline = !hasKid && !string.IsNullOrWhiteSpace(el.Value)
            ? el.Value.Trim().Replace("\n", " ") : null;

        string line = hasKid      ? $"{indent}<{tag}{sb}>"
            : inline != null      ? $"{indent}<{tag}{sb}>{inline}</{tag}>"
                                  : $"{indent}<{tag}{sb} />";

        panel.Children.Add(new TextBlock
        {
            Text       = line,
            FontSize   = 11,
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            Foreground = new SolidColorBrush(Color.Parse(colour)),
            Opacity    = isProp ? 0.5 : 1.0,
            Margin     = new Thickness(0, 0, 0, 1)
        });

        if (hasKid)
        {
            foreach (var child in el.Elements()) RenderNode(panel, child, depth + 1);
            panel.Children.Add(new TextBlock
            {
                Text       = $"{indent}</{tag}>",
                FontSize   = 11,
                FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
                Foreground = new SolidColorBrush(Color.Parse(colour)),
                Opacity    = isProp ? 0.5 : 0.65,
                Margin     = new Thickness(0, 0, 0, 1)
            });
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Preview Helpers
    // ═════════════════════════════════════════════════════════════════════
    private void SetPreviewContent(Control content)
    {
        var host = this.FindControl<ContentControl>("PreviewContent");
        if (host != null) host.Content = content;
    }

    private void SetPreviewStatus(string text)
    {
        var lbl = this.FindControl<TextBlock>("PreviewStatusLabel");
        if (lbl != null) lbl.Text = text;
    }

    private void SetStatus(string text, string hexColor)
    {
        var lbl = this.FindControl<TextBlock>("StatusLabel");
        if (lbl == null) return;
        lbl.Text       = text;
        lbl.Foreground = new SolidColorBrush(Color.Parse(hexColor));
    }

    private void SetError(string message)
    {
        _lastError = message;
        var btn = this.FindControl<Button>("ErrorButton");
        if (btn != null) btn.IsVisible = true;
    }

    private void ClearError()
    {
        _lastError = string.Empty;
        var btn = this.FindControl<Button>("ErrorButton");
        if (btn != null) btn.IsVisible = false;
    }

    private void ErrorButton_Click(object? sender, RoutedEventArgs e)
    {
        // Show error in a simple dialog-like window
        var errorWin = new Window
        {
            Title      = "Preview Error",
            Width      = 600,
            Height     = 300,
            Background = new SolidColorBrush(Color.Parse("#FF1F1A24")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text         = _lastError,
                        Foreground   = new SolidColorBrush(Color.Parse("#FFF38BA8")),
                        FontSize     = 12,
                        FontFamily   = new FontFamily("Cascadia Code, Consolas, monospace"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        errorWin.Show(this);
    }

    private void UpdateFileLabel(string name)
    {
        var lbl = this.FindControl<TextBlock>("EditorFileLabel");
        if (lbl != null) lbl.Text = name;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Size Presets
    // ═════════════════════════════════════════════════════════════════════
    private void SizePhone_Click(object? sender, RoutedEventArgs e)
    {
        _fixedSize = (360, 640);
        UpdateSizeLabel("360×640");
        ApplyCanvasSize();
    }

    private void SizeTablet_Click(object? sender, RoutedEventArgs e)
    {
        _fixedSize = (768, 1024);
        UpdateSizeLabel("768×1024");
        ApplyCanvasSize();
    }

    private void SizeDesktop_Click(object? sender, RoutedEventArgs e)
    {
        _fixedSize = (1280, 800);
        UpdateSizeLabel("1280×800");
        ApplyCanvasSize();
    }

    private void SizeFree_Click(object? sender, RoutedEventArgs e)
    {
        _fixedSize = null;
        UpdateSizeLabel("Free");
        ApplyCanvasSize();
    }

    private void UpdateSizeLabel(string text)
    {
        var lbl = this.FindControl<TextBlock>("SizeLabel");
        if (lbl != null) lbl.Text = text;
    }

    private void ApplyCanvasSize()
    {
        var canvas = this.FindControl<Border>("PreviewCanvas");
        if (canvas == null) return;
        canvas.Width  = _fixedSize.HasValue ? _fixedSize.Value.W : double.NaN;
        canvas.Height = _fixedSize.HasValue ? _fixedSize.Value.H : double.NaN;
    }

    private void ToggleBackground_Click(object? sender, RoutedEventArgs e)
    {
        _darkBg = !_darkBg;
        var canvas = this.FindControl<Border>("PreviewCanvas");
        if (canvas != null)
            canvas.Background = new SolidColorBrush(_darkBg
                ? Color.Parse("#FF1F1A24")
                : Colors.White);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Export to PNG Image
    // ═════════════════════════════════════════════════════════════════════
    private async void ExportImage_Click(object? sender, RoutedEventArgs e)
    {
        var previewContent = this.FindControl<ContentControl>("PreviewContent");
        var canvas = this.FindControl<Border>("PreviewCanvas");

        if (previewContent?.Content is not Control target)
        {
            SetStatus("Nothing to export", "#FFF38BA8");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Image",
            DefaultExtension = "png",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_currentFilePath ?? "design") + "_export",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG Image")  { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } },
            }
        });

        if (file == null) return;

        try
        {
            SetStatus("Exporting...", "#FFFFC09F");

            // Use the PreviewCanvas border (includes background and shadow context)
            var renderTarget = canvas ?? (Control)target;

            // Ensure layout is up to date
            renderTarget.Measure(new Size(
                double.IsNaN(renderTarget.Width)  ? 2000 : renderTarget.Width,
                double.IsNaN(renderTarget.Height) ? 2000 : renderTarget.Height));
            renderTarget.Arrange(new Rect(renderTarget.DesiredSize));

            var pixelSize = new PixelSize(
                Math.Max(1, (int)renderTarget.Bounds.Width),
                Math.Max(1, (int)renderTarget.Bounds.Height));

            var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            rtb.Render(renderTarget);

            var outputPath = file.Path.LocalPath;
            using (var fs = File.Create(outputPath))
            {
                rtb.Save(fs);  // Saves as PNG by default
            }

            SetStatus($"Exported: {Path.GetFileName(outputPath)} ({pixelSize.Width}×{pixelSize.Height})", "#FFA6E3A1");
        }
        catch (Exception ex)
        {
            SetStatus($"Export error: {ex.Message}", "#FFF38BA8");
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  GitHub Copilot CLI Integration
    // ═════════════════════════════════════════════════════════════════════
    private void CopilotCli_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var wtPath = FindWindowsTerminal();
            ProcessStartInfo startInfo;

            if (!string.IsNullOrEmpty(wtPath))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"--title \"GitHub Copilot CLI\" cmd /k echo. && echo ✦ GitHub Copilot CLI && echo. && echo Type: gh copilot suggest \"create an AXAML login form\" && echo.",
                    UseShellExecute = true,
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k echo ✦ GitHub Copilot CLI && echo.",
                    UseShellExecute = true,
                };
            }

            Process.Start(startInfo);
            AppendTerminalOutput("✦ Opened GitHub Copilot CLI in external terminal.\n");
            SetStatus("Copilot CLI opened", "#FFA6E3A1");
        }
        catch (Exception ex)
        {
            AppendTerminalOutput($"❌ Error opening Copilot CLI: {ex.Message}\n");
            SetStatus("Copilot CLI error", "#FFF38BA8");
        }
    }

    private async void TerminalInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ProcessTerminalCommandAsync();
        }
    }

    private async void TerminalSend_Click(object? sender, RoutedEventArgs e)
    {
        await ProcessTerminalCommandAsync();
    }

    private async Task ProcessTerminalCommandAsync()
    {
        var input = this.FindControl<TextBox>("TerminalInput");
        var command = input?.Text?.Trim();
        if (string.IsNullOrEmpty(command)) return;

        if (input != null) input.Text = string.Empty;

        AppendTerminalOutput($"✦ ❯ {command}\n");

        // Check if it's a direct AXAML generation request
        if (command.StartsWith("gh copilot", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("gh-copilot", StringComparison.OrdinalIgnoreCase))
        {
            await RunGhCopilotCommandAsync(command);
        }
        else
        {
            // Treat as a natural language prompt → wrap it as a Copilot suggest command
            await RunGhCopilotCommandAsync($"gh copilot suggest \"{command}\"");
        }
    }

    private async Task RunGhCopilotCommandAsync(string command)
    {
        var ghPath = await FindGhPathAsync();

        if (string.IsNullOrEmpty(ghPath))
        {
            AppendTerminalOutput("❌ GitHub CLI (gh) not found.\n");
            AppendTerminalOutput("   Install: winget install GitHub.cli\n");
            AppendTerminalOutput("   Then: gh extension install github/gh-copilot\n\n");
            return;
        }

        // Parse the command to get arguments for gh
        string ghArgs;
        if (command.StartsWith("gh ", StringComparison.OrdinalIgnoreCase))
            ghArgs = command[3..].Trim();
        else if (command.StartsWith("gh-copilot", StringComparison.OrdinalIgnoreCase))
            ghArgs = "copilot " + command[10..].Trim();
        else
            ghArgs = command;

        AppendTerminalOutput($"⏳ Running: gh {ghArgs}...\n");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = ghPath,
                Arguments              = ghArgs,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error  = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
            {
                AppendTerminalOutput(output + "\n");

                // If output looks like AXAML, offer to insert into editor
                if (output.Contains("<") && output.Contains("/>") || output.Contains("</"))
                {
                    AppendTerminalOutput("💡 AXAML detected in output. Use 'insert' to put it in the editor.\n");
                }
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                AppendTerminalOutput($"⚠ {error}\n");
            }

            if (process.ExitCode == 0)
                AppendTerminalOutput("✓ Done\n\n");
            else
                AppendTerminalOutput($"✗ Exit code: {process.ExitCode}\n\n");
        }
        catch (Exception ex)
        {
            AppendTerminalOutput($"❌ Error: {ex.Message}\n\n");
        }
    }

    private void AppendTerminalOutput(string text)
    {
        var output = this.FindControl<TextBlock>("TerminalOutput");
        if (output != null)
        {
            output.Text += text;
        }

        // Auto-scroll
        var scroll = this.FindControl<ScrollViewer>("TerminalScroll");
        scroll?.ScrollToEnd();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Utility: Find GitHub CLI
    // ═════════════════════════════════════════════════════════════════════
    private static async Task<string?> FindGhPathAsync()
    {
        // Try common locations
        string[] candidates =
        {
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "GitHub CLI", "gh.exe"),
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Try PATH via where command
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "where",
                Arguments              = "gh",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                    return firstLine;
            }
        }
        catch { }

        return null;
    }

    private static string? FindWindowsTerminal()
    {
        var wtPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(wtPath) ? wtPath : null;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Keyboard Shortcuts
    // ═════════════════════════════════════════════════════════════════════
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.F5)
        {
            RefreshPreview();
            e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SaveFile_Click(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            NewFile_Click(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenFile_Click(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
                 e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ExportImage_Click(null, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}