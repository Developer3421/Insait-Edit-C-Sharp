using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Insait_Edit_C_Sharp;

/// <summary>
/// Compiled Avalonia UserControl (avares:// resource) that acts as a host
/// for runtime AXAML previews.
///
/// Загружає довільний .axaml файл або рядок через AvaloniaRuntimeXamlLoader,
/// санітизує його (прибирає x:Class, переписує Window → Border),
/// ін'єктує App-рівневі ресурси і стилі,
/// а поверх рендерованого контенту кладе прозорий оверлей що блокує
/// всі pointer-події — превью тільки для читання.
/// </summary>
public partial class AxamlLiveHost : UserControl
{
    // ── cached reflection ──────────────────────────────────────────────
    private static MethodInfo? _loaderMethod;
    private static readonly object _loaderLock = new();

    // ── constructor ────────────────────────────────────────────────────
    public AxamlLiveHost()
    {
        InitializeComponent();
    }

    // ── public API ─────────────────────────────────────────────────────

    /// <summary>Result of last load attempt.</summary>
    public string? LastError { get; private set; }
    public bool    IsLiveRender { get; private set; }

    /// <summary>Load from a file path on disk.</summary>
    public void LoadFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LastError    = $"File not found: {filePath}";
            IsLiveRender = false;
            SetContent(BuildFallback(string.Empty, LastError));
            return;
        }
        try
        {
            var text = File.ReadAllText(filePath, Encoding.UTF8);
            LoadXaml(text, filePath);
        }
        catch (Exception ex)
        {
            LastError    = ex.Message;
            IsLiveRender = false;
            SetContent(BuildFallback(string.Empty, LastError));
        }
    }

    /// <summary>Load from a XAML string (unsaved buffer content).</summary>
    public void LoadXaml(string xaml, string? sourceFilePath = null)
    {
        LastError    = null;
        IsLiveRender = false;

        if (string.IsNullOrWhiteSpace(xaml))
        {
            LastError = "Empty XAML content.";
            SetContent(BuildFallback(string.Empty, LastError));
            return;
        }

        // Step 1 — sanitise
        string sanitised;
        try   { sanitised = Sanitise(xaml); }
        catch (Exception ex)
        {
            LastError = $"XML error: {ex.Message}";
            SetContent(BuildFallback(xaml, LastError));
            return;
        }

        // Step 2 — runtime load
        Uri? baseUri = !string.IsNullOrEmpty(sourceFilePath) && File.Exists(sourceFilePath)
            ? new Uri(sourceFilePath, UriKind.Absolute) : null;

        Control? live = TryLoad(sanitised, baseUri, out string? err);

        if (live != null)
        {
            InjectAppResources(live);
            SetContent(live);
            IsLiveRender = true;
        }
        else
        {
            LastError = err;
            SetContent(BuildFallback(xaml, err));
        }
    }

    // ── XAML sanitisation ──────────────────────────────────────────────

    // Known Avalonia event attribute names — strip these from all elements
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

    private static string Sanitise(string xaml)
    {
        var doc  = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var root = doc.Root!;

        XNamespace xns  = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace mc   = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        XNamespace d    = "http://schemas.microsoft.com/expression/blend/2008";
        XNamespace avns = "https://github.com/avaloniaui";

        // ── strip compile-time root attributes ─────────────────────────
        root.Attribute(xns + "Class")?.Remove();
        root.Attribute(mc  + "Ignorable")?.Remove();
        foreach (var a in root.Attributes().Where(a => a.Name.Namespace == d).ToList())
            a.Remove();

        // ── walk entire tree: strip events + unknown-ns elements ───────
        StripEventHandlersAndUnknownElements(root, xns);

        string tag = root.Name.LocalName;
        if (tag is not ("Window" or "UserControl"))
            return doc.ToString(SaveOptions.DisableFormatting);

        // ── rewrite Window / UserControl → Border ─────────────────────
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

    /// <summary>
    /// Recursively walks the element tree:
    /// • removes all event-handler attributes (Click=, ValueChanged=, etc.)
    /// • removes x:Name on elements whose type is unknown (non-Avalonia namespace)
    /// • removes entire elements whose namespace is not Avalonia / XAML
    ///   (e.g. clr-namespace:MyProject2 custom controls like WireframeViewport)
    ///   and replaces them with a placeholder Border so layout is preserved.
    /// </summary>
    private static void StripEventHandlersAndUnknownElements(XElement el, XNamespace xns)
    {
        XNamespace avns = "https://github.com/avaloniaui";

        // Process children first (bottom-up so removals don't break iteration)
        foreach (var child in el.Elements().ToList())
        {
            var childNs = child.Name.Namespace.NamespaceName;
            bool isAvalonia = childNs == avns.NamespaceName || childNs == string.Empty;
            bool isPropEl   = child.Name.LocalName.Contains('.');   // e.g. Grid.RowDefinitions

            if (!isAvalonia && !isPropEl)
            {
                // Replace unknown custom control with a placeholder Border
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

                // Copy layout-only attributes that Avalonia Border understands
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

            // Recurse into known elements
            StripEventHandlersAndUnknownElements(child, xns);
        }

        // Strip event handler attributes from this element
        foreach (var attr in el.Attributes().ToList())
        {
            if (attr.IsNamespaceDeclaration) continue;
            if (attr.Name.Namespace != XNamespace.None) continue;   // keep x:, d: etc.
            if (EventAttributes.Contains(attr.Name.LocalName))
                attr.Remove();
        }

        // Strip x:Name on root-level to avoid duplicate name registration crashes
        // (keep x:Name so FindControl works inside the preview — actually leave it)
    }

    // ── runtime loader via reflection ──────────────────────────────────

    private static MethodInfo? GetLoaderMethod()
    {
        lock (_loaderLock)
        {
            if (_loaderMethod != null) return _loaderMethod;

            // Pre-load assemblies so Type.GetType finds them
            foreach (var name in new[] { "Avalonia.Markup.Xaml.Loader", "Avalonia.Markup.Xaml" })
            {
                try
                {
                    if (AppDomain.CurrentDomain.GetAssemblies().All(a => a.GetName().Name != name))
                        Assembly.Load(new AssemblyName(name));
                }
                catch { /* ok */ }
            }

            // Known type names across Avalonia 11.x
            string[] candidates =
            [
                "Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml",
                "Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml.Loader",
                "Avalonia.Markup.Xaml.Loader.AvaloniaRuntimeXamlLoader, Avalonia.Markup.Xaml.Loader",
            ];

            foreach (var c in candidates)
            {
                var t = Type.GetType(c, throwOnError: false);
                if (t == null) continue;
                var m = FindLoadMethod(t);
                if (m != null) { _loaderMethod = m; return m; }
            }

            // Brute-force scan
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetExportedTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name != "AvaloniaRuntimeXamlLoader") continue;
                    var m = FindLoadMethod(t);
                    if (m != null) { _loaderMethod = m; return m; }
                }
            }

            return null;
        }
    }

    private static MethodInfo? FindLoadMethod(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Static)
         .FirstOrDefault(m =>
             m.Name == "Load" &&
             m.GetParameters() is { Length: >= 1 } ps &&
             ps[0].ParameterType == typeof(string));

    private static Control? TryLoad(string xaml, Uri? baseUri, out string? error)
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
                    var pt when pt == typeof(Assembly) => Assembly.GetExecutingAssembly(),
                    var pt when pt == typeof(Uri)      => baseUri,
                    var pt when pt == typeof(bool)     => false,   // designMode = false
                    _                                  => null
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

    // ── resource injection ─────────────────────────────────────────────

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
        catch { /* non-critical */ }
    }

    // ── content helpers ────────────────────────────────────────────────

    private void SetContent(Control content)
    {
        var host = this.FindControl<ContentControl>("PreviewHost");
        if (host != null) host.Content = content;
    }

    // ── fallback XML tree ──────────────────────────────────────────────

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

    // ── input blocker event handlers ───────────────────────────────────
    // All pointer events are consumed here — they never reach child controls.

    private void InputBlocker_PointerPressed(object? sender, PointerPressedEventArgs e)
        => e.Handled = true;
    private void InputBlocker_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => e.Handled = true;
    private void InputBlocker_PointerMoved(object? sender, PointerEventArgs e)
        => e.Handled = true;
    private void InputBlocker_Tapped(object? sender, TappedEventArgs e)
        => e.Handled = true;
    private void InputBlocker_DoubleTapped(object? sender, TappedEventArgs e)
        => e.Handled = true;
}

