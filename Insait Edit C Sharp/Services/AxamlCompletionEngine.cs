using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Completion engine for AXAML / XAML files.
/// Provides context-aware completions for Avalonia UI elements, properties,
/// events, attached properties, markup extensions, and namespace prefixes.
/// Uses reflection on loaded Avalonia assemblies to discover real types.
/// </summary>
public sealed class AxamlCompletionEngine
{
    // ── Cached type data ──────────────────────────────────────────────
    private static readonly Lazy<List<AxamlTypeInfo>> _avaloniaTypes = new(DiscoverAvaloniaTypes);
    private static readonly Lazy<List<string>> _markupExtensions = new(DiscoverMarkupExtensions);
    private static readonly Lazy<Dictionary<string, List<AxamlPropertyInfo>>> _propertyCache = new(() => new());

    // Common AXAML namespace URIs
    private static readonly Dictionary<string, string> KnownNamespaces = new()
    {
        ["https://github.com/avaloniaui"] = "Avalonia",
        ["http://schemas.microsoft.com/winfx/2006/xaml"] = "x",
        ["http://schemas.microsoft.com/expression/blend/2008"] = "d",
        ["http://schemas.openxmlformats.org/markup-compatibility/2006"] = "mc",
    };

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Gets completion items for the given AXAML/XAML source at the specified position.
    /// </summary>
    public Task<IReadOnlyList<RoslynCompletionItem>> GetCompletionsAsync(
        string filePath, string sourceCode, int line, int column, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var offset = LineColToOffset(sourceCode, line, column);
            var context = DetermineContext(sourceCode, offset);

            IReadOnlyList<RoslynCompletionItem> result = context.Kind switch
            {
                AxamlContextKind.ElementName      => GetElementCompletions(context.Prefix),
                AxamlContextKind.ClosingTag        => GetClosingTagCompletions(sourceCode, offset, context.Prefix),
                AxamlContextKind.AttributeName     => GetAttributeCompletions(context.ParentElement, context.Prefix),
                AxamlContextKind.AttributeValue    => GetAttributeValueCompletions(context.ParentElement, context.AttributeName, context.Prefix),
                AxamlContextKind.NamespaceUri      => GetNamespaceCompletions(context.Prefix),
                AxamlContextKind.MarkupExtension   => GetMarkupExtensionCompletions(context.Prefix),
                AxamlContextKind.PropertyElement   => GetPropertyElementCompletions(context.ParentElement, context.Prefix),
                _                                  => Array.Empty<RoslynCompletionItem>(),
            };

            return result;
        }, ct);
    }

    // ── Context detection ─────────────────────────────────────────────

    private enum AxamlContextKind
    {
        None,
        ElementName,        // <But|  or  <StackPa|
        ClosingTag,         // </|
        AttributeName,      // <Button Wi|  (inside an element, after a space)
        AttributeValue,     // <Button Width="|  
        NamespaceUri,       // xmlns:x="|
        MarkupExtension,    // {Bind|
        PropertyElement,    // <Button.|
    }

    private sealed class AxamlContext
    {
        public AxamlContextKind Kind { get; init; }
        public string Prefix { get; init; } = "";
        public string? ParentElement { get; init; }
        public string? AttributeName { get; init; }
    }

    private static AxamlContext DetermineContext(string source, int offset)
    {
        if (offset <= 0 || offset > source.Length)
            return new AxamlContext { Kind = AxamlContextKind.None };

        // Walk backward to find context
        int pos = Math.Min(offset, source.Length) - 1;

        // Check if we're inside a markup extension {Binding ...}
        if (IsInsideMarkupExtension(source, pos))
        {
            var prefix = ExtractWordBackward(source, pos);
            return new AxamlContext { Kind = AxamlContextKind.MarkupExtension, Prefix = prefix };
        }

        // Check if inside attribute value (between quotes after =")
        if (IsInsideAttributeValue(source, pos, out var attrName, out var parentEl, out var valPrefix))
        {
            if (attrName?.StartsWith("xmlns") == true)
                return new AxamlContext { Kind = AxamlContextKind.NamespaceUri, Prefix = valPrefix };
            return new AxamlContext
            {
                Kind = AxamlContextKind.AttributeValue,
                Prefix = valPrefix,
                ParentElement = parentEl,
                AttributeName = attrName
            };
        }

        // Find the nearest unmatched '<' to determine if we're in an element tag
        int angleBracket = FindUnmatchedOpenAngle(source, pos);
        if (angleBracket < 0)
            return new AxamlContext { Kind = AxamlContextKind.None };

        var afterAngle = source[(angleBracket + 1)..Math.Min(offset, source.Length)];

        // </Tag — closing tag
        if (angleBracket + 1 < source.Length && source[angleBracket + 1] == '/')
        {
            var closingPrefix = afterAngle.Length > 1 ? afterAngle[1..].Trim() : "";
            return new AxamlContext
            {
                Kind = AxamlContextKind.ClosingTag,
                Prefix = closingPrefix,
                ParentElement = FindParentElement(source, angleBracket)
            };
        }

        // Determine if we're typing element name or attribute name
        var tagContent = afterAngle.TrimStart();

        // Check for property element syntax: <Parent.Property
        if (tagContent.Contains('.'))
        {
            var dotIdx = tagContent.IndexOf('.');
            var elemName = tagContent[..dotIdx].Trim();
            var propPrefix = tagContent[(dotIdx + 1)..].Trim();
            // If no spaces before the dot, it's a property element
            if (!tagContent[..dotIdx].Contains(' '))
                return new AxamlContext
                {
                    Kind = AxamlContextKind.PropertyElement,
                    Prefix = propPrefix,
                    ParentElement = elemName
                };
        }

        // If there's a space, we're in attribute position
        if (tagContent.Contains(' '))
        {
            var elemNameEnd = tagContent.IndexOf(' ');
            var elemName = tagContent[..elemNameEnd].Trim();
            var attrPrefix = ExtractWordBackward(source, pos);
            return new AxamlContext
            {
                Kind = AxamlContextKind.AttributeName,
                Prefix = attrPrefix,
                ParentElement = elemName
            };
        }

        // Otherwise we're typing element name
        return new AxamlContext
        {
            Kind = AxamlContextKind.ElementName,
            Prefix = tagContent
        };
    }

    // ── Completion generators ─────────────────────────────────────────

    private static IReadOnlyList<RoslynCompletionItem> GetElementCompletions(string prefix)
    {
        var types = _avaloniaTypes.Value;
        var result = new List<RoslynCompletionItem>();

        foreach (var t in types)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new RoslynCompletionItem
            {
                Label = t.Name,
                InsertText = t.Name,
                FilterText = t.Name,
                SortText = t.Name,
                Kind = t.IsControl ? "Class" : (t.IsMarkupExtension ? "Snippet" : "Struct"),
                Detail = t.Namespace,
            });
        }

        // Add common structure elements
        var structureElements = new[]
        {
            ("Style", "Avalonia style element", "Keyword"),
            ("Setter", "Style setter", "Property"),
            ("Styles", "Styles collection", "Keyword"),
            ("Resources", "Resources dictionary", "Keyword"),
            ("Design.DataContext", "Design-time data context", "Property"),
        };

        foreach (var (name, detail, kind) in structureElements)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (result.All(r => r.Label != name))
            {
                result.Add(new RoslynCompletionItem
                {
                    Label = name,
                    InsertText = name,
                    FilterText = name,
                    SortText = name,
                    Kind = kind,
                    Detail = detail,
                });
            }
        }

        return result;
    }

    private static IReadOnlyList<RoslynCompletionItem> GetClosingTagCompletions(
        string source, int offset, string prefix)
    {
        // Find the nearest unclosed element
        var unclosed = FindUnclosedElements(source, offset);
        var result = new List<RoslynCompletionItem>();

        foreach (var elem in unclosed)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !elem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new RoslynCompletionItem
            {
                Label = elem,
                InsertText = elem + ">",
                FilterText = elem,
                SortText = "0_" + elem, // prioritize
                Kind = "Class",
                Detail = "Close element",
            });
        }

        return result;
    }

    private static IReadOnlyList<RoslynCompletionItem> GetAttributeCompletions(
        string? elementName, string prefix)
    {
        var result = new List<RoslynCompletionItem>();
        if (string.IsNullOrEmpty(elementName)) return result;

        var props = GetPropertiesForElement(elementName!);
        foreach (var p in props)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new RoslynCompletionItem
            {
                Label = p.Name,
                InsertText = p.Name + "=\"\"",
                FilterText = p.Name,
                SortText = p.Name,
                Kind = p.IsEvent ? "Event" : "Property",
                Detail = p.TypeName,
            });
        }

        // Add common XAML attributes
        var commonAttrs = new[]
        {
            ("x:Name", "Element name", "Keyword"),
            ("x:Key", "Resource key", "Keyword"),
            ("x:Class", "Code-behind class", "Keyword"),
            ("x:DataType", "Data type for binding", "Keyword"),
            ("Classes", "CSS-like classes", "Property"),
            ("DataContext", "Data context", "Property"),
            ("xmlns", "XML namespace", "Keyword"),
        };

        foreach (var (name, detail, kind) in commonAttrs)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (result.All(r => r.Label != name))
            {
                result.Add(new RoslynCompletionItem
                {
                    Label = name,
                    InsertText = name + "=\"\"",
                    FilterText = name,
                    SortText = "z_" + name,
                    Kind = kind,
                    Detail = detail,
                });
            }
        }

        return result;
    }

    private static IReadOnlyList<RoslynCompletionItem> GetAttributeValueCompletions(
        string? elementName, string? attributeName, string prefix)
    {
        var result = new List<RoslynCompletionItem>();
        if (string.IsNullOrEmpty(attributeName)) return result;

        // Common boolean properties
        var boolAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IsVisible", "IsEnabled", "IsReadOnly", "IsHitTestVisible",
            "ClipToBounds", "Focusable", "IsDefault", "IsCancel",
            "ShowInTaskbar", "CanResize", "ShowActivated", "Topmost",
            "AcceptsReturn", "AcceptsTab", "IsChecked", "IsExpanded",
            "IsSelected", "IsOpen", "IsDropDownOpen", "AllowMultiple",
            "UseLayoutRounding", "ShowButtonSpinner",
        };

        if (boolAttrs.Contains(attributeName!))
        {
            foreach (var v in new[] { "True", "False" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "Constant", Detail = "Boolean",
                });
            }
            return result;
        }

        // HorizontalAlignment / VerticalAlignment
        if (attributeName!.Contains("Alignment", StringComparison.OrdinalIgnoreCase))
        {
            var values = attributeName.Contains("Horizontal", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Stretch", "Left", "Center", "Right" }
                : new[] { "Stretch", "Top", "Center", "Bottom" };
            foreach (var v in values)
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "Alignment",
                });
            }
            return result;
        }

        // Orientation
        if (attributeName.Equals("Orientation", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Horizontal", "Vertical" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "Orientation",
                });
            }
            return result;
        }

        // Dock
        if (attributeName.Equals("DockPanel.Dock", StringComparison.OrdinalIgnoreCase) ||
            attributeName.Equals("Dock", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Left", "Top", "Right", "Bottom" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "Dock",
                });
            }
            return result;
        }

        // Visibility
        if (attributeName.Equals("Visibility", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Visible", "Hidden", "Collapsed" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "Visibility",
                });
            }
            return result;
        }

        // ScrollBarVisibility
        if (attributeName.Contains("ScrollBarVisibility", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Auto", "Disabled", "Hidden", "Visible" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "ScrollBarVisibility",
                });
            }
            return result;
        }

        // FontWeight
        if (attributeName.Equals("FontWeight", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Thin", "ExtraLight", "Light", "Normal", "Medium",
                "SemiBold", "Bold", "ExtraBold", "Black" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "FontWeight",
                });
            }
            return result;
        }

        // FontStyle
        if (attributeName.Equals("FontStyle", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Normal", "Italic", "Oblique" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "FontStyle",
                });
            }
            return result;
        }

        // TextAlignment / TextWrapping / TextDecorations / TextTrimming
        if (attributeName.Equals("TextAlignment", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Left", "Center", "Right", "Justify" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "TextAlignment",
                });
            }
            return result;
        }

        if (attributeName.Equals("TextWrapping", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "NoWrap", "Wrap", "WrapWithOverflow" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "TextWrapping",
                });
            }
            return result;
        }

        if (attributeName.Equals("TextTrimming", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "None", "CharacterEllipsis", "WordEllipsis" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "TextTrimming",
                });
            }
            return result;
        }

        // Cursor
        if (attributeName.Equals("Cursor", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Arrow", "Ibeam", "Wait", "Cross", "UpArrow", "SizeWestEast",
                "SizeNorthSouth", "SizeAll", "No", "Hand", "AppStarting", "Help",
                "TopSide", "BottomSide", "LeftSide", "RightSide",
                "TopLeftCorner", "TopRightCorner", "BottomLeftCorner", "BottomRightCorner",
                "DragMove", "DragCopy", "DragLink", "None" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "Cursor",
                });
            }
            return result;
        }

        // WindowStartupLocation
        if (attributeName.Equals("WindowStartupLocation", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Manual", "CenterScreen", "CenterOwner" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "WindowStartupLocation",
                });
            }
            return result;
        }

        // SystemDecorations
        if (attributeName.Equals("SystemDecorations", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "None", "BorderOnly", "Full" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "SystemDecorations",
                });
            }
            return result;
        }

        // SizeToContent
        if (attributeName.Equals("SizeToContent", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "Manual", "Width", "Height", "WidthAndHeight" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "SizeToContent",
                });
            }
            return result;
        }

        // Stretch mode (for Image, etc.)
        if (attributeName.Equals("Stretch", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var v in new[] { "None", "Fill", "Uniform", "UniformToFill" })
            {
                if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RoslynCompletionItem
                {
                    Label = v, InsertText = v, FilterText = v, SortText = v,
                    Kind = "EnumMember", Detail = "Stretch",
                });
            }
            return result;
        }

        // Try to discover enum values via reflection
        var enumValues = TryGetEnumValuesForProperty(elementName, attributeName);
        foreach (var v in enumValues)
        {
            if (!string.IsNullOrEmpty(prefix) && !v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(new RoslynCompletionItem
            {
                Label = v, InsertText = v, FilterText = v, SortText = v,
                Kind = "EnumMember", Detail = attributeName,
            });
        }

        return result;
    }

    private static IReadOnlyList<RoslynCompletionItem> GetNamespaceCompletions(string prefix)
    {
        var result = new List<RoslynCompletionItem>();
        foreach (var (uri, desc) in KnownNamespaces)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new RoslynCompletionItem
            {
                Label = uri, InsertText = uri, FilterText = uri, SortText = uri,
                Kind = "Module", Detail = desc,
            });
        }

        // Add clr-namespace pattern
        if (string.IsNullOrEmpty(prefix) || "clr-namespace".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new RoslynCompletionItem
            {
                Label = "clr-namespace:", InsertText = "clr-namespace:", FilterText = "clr-namespace",
                SortText = "clr-namespace", Kind = "Module", Detail = "CLR namespace reference",
            });
        }

        if (string.IsNullOrEmpty(prefix) || "using:".StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new RoslynCompletionItem
            {
                Label = "using:", InsertText = "using:", FilterText = "using",
                SortText = "using", Kind = "Module", Detail = "Using namespace (Avalonia)",
            });
        }

        return result;
    }

    private static IReadOnlyList<RoslynCompletionItem> GetMarkupExtensionCompletions(string prefix)
    {
        var result = new List<RoslynCompletionItem>();
        var extensions = new[]
        {
            ("Binding", "Data binding markup extension"),
            ("TemplateBinding", "Template binding"),
            ("StaticResource", "Static resource lookup"),
            ("DynamicResource", "Dynamic resource lookup"),
            ("x:Static", "Static member reference"),
            ("x:Type", "Type reference"),
            ("x:Null", "Null value"),
            ("OnPlatform", "Platform-conditional value"),
            ("OnFormFactor", "Form factor conditional"),
            ("CompiledBinding", "Compiled binding"),
            ("ReflectionBinding", "Reflection-based binding"),
        };

        foreach (var (name, detail) in extensions)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new RoslynCompletionItem
            {
                Label = name, InsertText = name + " ", FilterText = name, SortText = name,
                Kind = "Snippet", Detail = detail,
            });
        }

        return result;
    }

    private static IReadOnlyList<RoslynCompletionItem> GetPropertyElementCompletions(
        string? elementName, string prefix)
    {
        var result = new List<RoslynCompletionItem>();
        if (string.IsNullOrEmpty(elementName)) return result;

        var props = GetPropertiesForElement(elementName!);
        foreach (var p in props.Where(p => !p.IsEvent))
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new RoslynCompletionItem
            {
                Label = $"{elementName}.{p.Name}",
                InsertText = $"{elementName}.{p.Name}",
                FilterText = p.Name,
                SortText = p.Name,
                Kind = "Property",
                Detail = p.TypeName,
            });
        }

        return result;
    }

    // ── Avalonia type discovery via reflection ─────────────────────────

    private static List<AxamlTypeInfo> DiscoverAvaloniaTypes()
    {
        var result = new List<AxamlTypeInfo>();
        var seen = new HashSet<string>();

        try
        {
            var avaloniaAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) == true
                         || a.GetName().Name == "Insait_Edit_C_Sharp")
                .ToList();

            foreach (var asm in avaloniaAssemblies)
            {
                try
                {
                    foreach (var type in asm.GetExportedTypes())
                    {
                        if (type.IsAbstract || type.IsGenericTypeDefinition || !type.IsPublic) continue;
                        if (type.Name.StartsWith("<") || type.Name.Contains('`')) continue;
                        if (!seen.Add(type.Name)) continue;

                        bool isControl = typeof(Avalonia.Controls.Control).IsAssignableFrom(type);
                        bool isMarkupExt = type.Name.EndsWith("Extension");
                        bool isAvaloniaObj = typeof(Avalonia.AvaloniaObject).IsAssignableFrom(type);

                        if (!isControl && !isMarkupExt && !isAvaloniaObj) continue;

                        result.Add(new AxamlTypeInfo
                        {
                            Name = type.Name,
                            FullName = type.FullName ?? type.Name,
                            Namespace = type.Namespace ?? "",
                            IsControl = isControl,
                            IsMarkupExtension = isMarkupExt,
                            Type = type,
                        });
                    }
                }
                catch { /* skip assembly */ }
            }
        }
        catch { /* reflection failed */ }

        // Add common Avalonia elements that might not be found via reflection
        var fallbackElements = new[]
        {
            "Window", "UserControl", "ContentControl", "Button", "TextBlock", "TextBox",
            "Border", "StackPanel", "Grid", "DockPanel", "WrapPanel", "Canvas",
            "ScrollViewer", "ListBox", "ListBoxItem", "ComboBox", "ComboBoxItem",
            "CheckBox", "RadioButton", "Slider", "ProgressBar", "TabControl", "TabItem",
            "Menu", "MenuItem", "ContextMenu", "Separator", "Expander",
            "Image", "Path", "Rectangle", "Ellipse", "Line",
            "ItemsControl", "TreeView", "TreeViewItem", "DataGrid",
            "NumericUpDown", "DatePicker", "TimePicker", "Calendar",
            "ToggleSwitch", "ToggleButton", "RepeatButton", "SplitButton",
            "Viewbox", "Panel", "Popup", "FlyoutBase", "Flyout",
            "ToolTip", "HeaderedContentControl", "ContentPresenter",
            "ItemsPresenter", "ScrollContentPresenter",
            "TransitioningContentControl", "Carousel",
            "SplitView", "NavigationView", "TabStrip",
            "AutoCompleteBox", "CalendarDatePicker",
            "SelectableTextBlock", "AccessText", "Label",
            "PathIcon", "DrawingImage", "CroppedBitmap",
            "ColumnDefinition", "RowDefinition",
            "GradientStop", "LinearGradientBrush", "RadialGradientBrush", "SolidColorBrush",
            "DropShadowEffect", "BlurEffect",
            "RotateTransform", "ScaleTransform", "TranslateTransform", "SkewTransform",
            "TransformGroup", "MatrixTransform",
            "Animation", "KeyFrame", "Setter", "Style", "Styles",
            "ResourceDictionary", "MergeResourceInclude",
        };

        foreach (var name in fallbackElements)
        {
            if (seen.Contains(name)) continue;
            seen.Add(name);
            result.Add(new AxamlTypeInfo
            {
                Name = name, FullName = "Avalonia.Controls." + name,
                Namespace = "Avalonia.Controls", IsControl = true, IsMarkupExtension = false,
            });
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static List<string> DiscoverMarkupExtensions()
    {
        var result = new List<string> { "Binding", "TemplateBinding", "StaticResource", "DynamicResource" };
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("Avalonia") == true))
            {
                foreach (var type in asm.GetExportedTypes())
                {
                    if (type.Name.EndsWith("Extension") && !type.IsAbstract)
                        result.Add(type.Name.Replace("Extension", ""));
                }
            }
        }
        catch { }
        return result.Distinct().ToList();
    }

    private static List<AxamlPropertyInfo> GetPropertiesForElement(string elementName)
    {
        var cache = _propertyCache.Value;
        if (cache.TryGetValue(elementName, out var cached))
            return cached;

        var props = new List<AxamlPropertyInfo>();
        var type = FindType(elementName);
        if (type != null)
        {
            var seen = new HashSet<string>();
            try
            {
                // Get all public properties
                foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                {
                    if (!seen.Add(pi.Name)) continue;
                    if (pi.GetIndexParameters().Length > 0) continue; // skip indexers
                    if (!pi.CanWrite && pi.PropertyType != typeof(string)) continue;

                    props.Add(new AxamlPropertyInfo
                    {
                        Name = pi.Name,
                        TypeName = SimplifyTypeName(pi.PropertyType),
                        IsEvent = false,
                    });
                }

                // Get events
                foreach (var ei in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                {
                    if (!seen.Add(ei.Name)) continue;
                    props.Add(new AxamlPropertyInfo
                    {
                        Name = ei.Name,
                        TypeName = "event",
                        IsEvent = true,
                    });
                }

                // Add Avalonia attached properties from common panels
                AddAttachedProperties(props, seen, "Grid", new[]
                {
                    ("Grid.Row", "int"), ("Grid.Column", "int"),
                    ("Grid.RowSpan", "int"), ("Grid.ColumnSpan", "int"),
                });
                AddAttachedProperties(props, seen, "DockPanel", new[]
                {
                    ("DockPanel.Dock", "Dock"),
                });
                AddAttachedProperties(props, seen, "Canvas", new[]
                {
                    ("Canvas.Left", "double"), ("Canvas.Top", "double"),
                    ("Canvas.Right", "double"), ("Canvas.Bottom", "double"),
                });
                AddAttachedProperties(props, seen, "ScrollViewer", new[]
                {
                    ("ScrollViewer.HorizontalScrollBarVisibility", "ScrollBarVisibility"),
                    ("ScrollViewer.VerticalScrollBarVisibility", "ScrollBarVisibility"),
                });
                AddAttachedProperties(props, seen, "ToolTip", new[]
                {
                    ("ToolTip.Tip", "object"), ("ToolTip.Placement", "PlacementMode"),
                });
            }
            catch { }
        }

        // Common properties fallback if type wasn't found
        if (props.Count == 0)
        {
            var commonProps = new[]
            {
                "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
                "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment",
                "Background", "Foreground", "BorderBrush", "BorderThickness",
                "FontSize", "FontFamily", "FontWeight", "FontStyle",
                "Opacity", "IsVisible", "IsEnabled", "IsHitTestVisible",
                "CornerRadius", "ClipToBounds", "RenderTransform",
                "Name", "Tag", "DataContext", "Classes", "Cursor",
                "Focusable", "ZIndex",
            };
            foreach (var name in commonProps)
            {
                props.Add(new AxamlPropertyInfo
                {
                    Name = name, TypeName = "property", IsEvent = false,
                });
            }
        }

        props.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        cache[elementName] = props;
        return props;
    }

    private static void AddAttachedProperties(List<AxamlPropertyInfo> props, HashSet<string> seen,
        string ownerPrefix, (string Name, string Type)[] attached)
    {
        foreach (var (name, typeName) in attached)
        {
            if (!seen.Add(name)) continue;
            props.Add(new AxamlPropertyInfo { Name = name, TypeName = typeName, IsEvent = false });
        }
    }

    private static Type? FindType(string elementName)
    {
        var types = _avaloniaTypes.Value;
        var info = types.FirstOrDefault(t =>
            t.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        return info?.Type;
    }

    private static string[] TryGetEnumValuesForProperty(string? elementName, string? attributeName)
    {
        if (string.IsNullOrEmpty(elementName) || string.IsNullOrEmpty(attributeName))
            return Array.Empty<string>();

        try
        {
            var type = FindType(elementName!);
            if (type == null) return Array.Empty<string>();

            var prop = type.GetProperty(attributeName!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (prop == null) return Array.Empty<string>();

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (propType.IsEnum)
                return Enum.GetNames(propType);
        }
        catch { }

        return Array.Empty<string>();
    }

    private static string SimplifyTypeName(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(float)) return "float";
        if (t == typeof(object)) return "object";
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying != null) return SimplifyTypeName(underlying) + "?";
        return t.Name;
    }

    // ── Text parsing helpers ──────────────────────────────────────────

    private static int LineColToOffset(string text, int line, int column)
    {
        int offset = 0;
        int currentLine = 1;
        for (int i = 0; i < text.Length && currentLine < line; i++)
        {
            if (text[i] == '\n') currentLine++;
            offset++;
        }
        return Math.Min(offset + column - 1, text.Length);
    }

    private static string ExtractWordBackward(string source, int pos)
    {
        int end = pos + 1;
        int start = pos;
        while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_' || source[start - 1] == ':' || source[start - 1] == '.'))
            start--;
        if (start >= end) return "";
        return source[start..end];
    }

    private static int FindUnmatchedOpenAngle(string source, int pos)
    {
        int depth = 0;
        for (int i = pos; i >= 0; i--)
        {
            if (source[i] == '>' && i < pos) depth++;
            else if (source[i] == '<')
            {
                if (depth == 0) return i;
                depth--;
            }
        }
        return -1;
    }

    private static bool IsInsideMarkupExtension(string source, int pos)
    {
        // Walk backward to see if we have an unmatched '{'
        int braces = 0;
        for (int i = pos; i >= 0; i--)
        {
            if (source[i] == '}') braces++;
            else if (source[i] == '{')
            {
                if (braces == 0) return true;
                braces--;
            }
            else if (source[i] == '<' || source[i] == '>') break;
        }
        return false;
    }

    private static bool IsInsideAttributeValue(string source, int pos,
        out string? attrName, out string? parentEl, out string valPrefix)
    {
        attrName = null;
        parentEl = null;
        valPrefix = "";

        // Walk backward to find opening quote
        int quotePos = -1;
        char quoteChar = '\0';
        for (int i = pos; i >= 0; i--)
        {
            if (source[i] == '"' || source[i] == '\'')
            {
                // Check if this is opening or closing quote
                // Count quotes before this to determine
                quotePos = i;
                quoteChar = source[i];
                break;
            }
            if (source[i] == '<' || source[i] == '>') break;
        }

        if (quotePos < 0) return false;

        // Check there's an = before the quote
        int eqPos = quotePos - 1;
        while (eqPos >= 0 && source[eqPos] == ' ') eqPos--;
        if (eqPos < 0 || source[eqPos] != '=') return false;

        // Get attribute name before =
        int nameEnd = eqPos;
        int nameStart = nameEnd - 1;
        while (nameStart >= 0 && (char.IsLetterOrDigit(source[nameStart]) || source[nameStart] == '_'
            || source[nameStart] == ':' || source[nameStart] == '.'))
            nameStart--;
        nameStart++;

        if (nameStart < nameEnd)
            attrName = source[nameStart..nameEnd];

        // Get the value prefix (text after the opening quote up to pos)
        valPrefix = source[(quotePos + 1)..(pos + 1)];

        // Find parent element
        int openAngle = FindUnmatchedOpenAngle(source, nameStart);
        if (openAngle >= 0)
        {
            var afterAngle = source[(openAngle + 1)..nameStart].TrimStart();
            var spaceIdx = afterAngle.IndexOf(' ');
            parentEl = spaceIdx > 0 ? afterAngle[..spaceIdx] : afterAngle.Trim();
        }

        return true;
    }

    private static string? FindParentElement(string source, int beforePos)
    {
        // Find the nearest open element that isn't closed yet
        var unclosed = FindUnclosedElements(source, beforePos);
        return unclosed.FirstOrDefault();
    }

    private static List<string> FindUnclosedElements(string source, int beforePos)
    {
        var stack = new Stack<string>();
        var openTagRegex = new Regex(@"<([A-Za-z_][\w\.]*)", RegexOptions.Compiled);
        var closeTagRegex = new Regex(@"</([A-Za-z_][\w\.]*)[\s>]", RegexOptions.Compiled);
        var selfCloseRegex = new Regex(@"/>", RegexOptions.Compiled);

        var text = source[..Math.Min(beforePos, source.Length)];

        // Simple stack-based matching
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                if (i + 1 < text.Length && text[i + 1] == '/')
                {
                    // Closing tag
                    var m = closeTagRegex.Match(text, i);
                    if (m.Success && m.Index == i)
                    {
                        var name = m.Groups[1].Value;
                        // Pop matching from stack
                        var temp = new Stack<string>();
                        bool found = false;
                        while (stack.Count > 0)
                        {
                            var top = stack.Pop();
                            if (top == name) { found = true; break; }
                            temp.Push(top);
                        }
                        if (!found) { while (temp.Count > 0) stack.Push(temp.Pop()); }
                        i = m.Index + m.Length;
                        continue;
                    }
                }
                else if (i + 1 < text.Length && text[i + 1] != '!' && text[i + 1] != '?')
                {
                    var m = openTagRegex.Match(text, i);
                    if (m.Success && m.Index == i)
                    {
                        var name = m.Groups[1].Value;
                        // Check if self-closing
                        int tagEnd = text.IndexOf('>', m.Index + m.Length);
                        if (tagEnd >= 0 && tagEnd > 0 && text[tagEnd - 1] == '/')
                        {
                            // Self-closing, don't push
                        }
                        else
                        {
                            stack.Push(name);
                        }
                        i = tagEnd >= 0 ? tagEnd + 1 : m.Index + m.Length;
                        continue;
                    }
                }
            }
            i++;
        }

        return stack.ToList(); // Most recent unclosed first
    }
}

// ── Helper types ──────────────────────────────────────────────────────────

internal sealed class AxamlTypeInfo
{
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Namespace { get; init; } = "";
    public bool IsControl { get; init; }
    public bool IsMarkupExtension { get; init; }
    public Type? Type { get; init; }
}

internal sealed class AxamlPropertyInfo
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public bool IsEvent { get; init; }
}

