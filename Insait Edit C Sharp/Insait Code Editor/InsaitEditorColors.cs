using System.Collections.Generic;
using Avalonia.Media;
using Microsoft.CodeAnalysis.Classification;

namespace Insait_Edit_C_Sharp.InsaitCodeEditor;

// ═══════════════════════════════════════════════════════════════════════════
//  Purple Fluent палітра + кольори токенів Roslyn Classification
// ═══════════════════════════════════════════════════════════════════════════

internal static class InsaitEditorColors
{
    // ── Editor chrome ────────────────────────────────────────────────────
    public static readonly Color Background    = Color.Parse("#FF1F1A24");
    public static readonly Color GutterBg      = Color.Parse("#FF241E2C");
    public static readonly Color GutterFg      = Color.Parse("#FF9E90B0");
    public static readonly Color GutterBorder  = Color.Parse("#FF3E3050");
    public static readonly Color Cursor        = Color.Parse("#FFDCC4FF");
    public static readonly Color CurrentLine   = Color.FromArgb(0x30, 0x8B, 0x5C, 0xF6);
    public static readonly Color Selection     = Color.FromArgb(0x55, 0xDC, 0xC4, 0xFF);
    public static readonly Color DefaultText   = Color.Parse("#FFF0E8F4");
    public static readonly Color ScrollBar     = Color.FromArgb(0x60, 0xDC, 0xC4, 0xFF);

    // ── Diagnostic severity ──────────────────────────────────────────────
    public static readonly Color DiagError   = Color.Parse("#FFF38BA8");
    public static readonly Color DiagWarning = Color.Parse("#FFF5A623");
    public static readonly Color DiagInfo    = Color.Parse("#FF89B4FA");

    // ── Token Colors (Roslyn Classification) ─────────────────────────────
    public static readonly IReadOnlyDictionary<string, Color> TokenColors =
        new Dictionary<string, Color>
        {
            [ClassificationTypeNames.Keyword]               = Color.Parse("#FF569CD6"),
            [ClassificationTypeNames.ControlKeyword]        = Color.Parse("#FFC586C0"),
            [ClassificationTypeNames.Comment]               = Color.Parse("#FF6A9955"),
            [ClassificationTypeNames.XmlDocCommentText]     = Color.Parse("#FF6A9955"),
            [ClassificationTypeNames.XmlDocCommentAttributeValue] = Color.Parse("#FFCE9178"),
            [ClassificationTypeNames.StringLiteral]         = Color.Parse("#FFCE9178"),
            [ClassificationTypeNames.VerbatimStringLiteral] = Color.Parse("#FFCE9178"),
            [ClassificationTypeNames.NumericLiteral]        = Color.Parse("#FFB5CEA8"),
            [ClassificationTypeNames.Operator]              = Color.Parse("#FFD4D4D4"),
            [ClassificationTypeNames.ClassName]             = Color.Parse("#FF4EC9B0"),
            [ClassificationTypeNames.RecordClassName]       = Color.Parse("#FF4EC9B0"),
            [ClassificationTypeNames.StructName]            = Color.Parse("#FF86C691"),
            [ClassificationTypeNames.RecordStructName]      = Color.Parse("#FF86C691"),
            [ClassificationTypeNames.InterfaceName]         = Color.Parse("#FFB8D7A3"),
            [ClassificationTypeNames.EnumName]              = Color.Parse("#FFB5CEA8"),
            [ClassificationTypeNames.EnumMemberName]        = Color.Parse("#FF4FC1FF"),
            [ClassificationTypeNames.MethodName]            = Color.Parse("#FFDCDCAA"),
            [ClassificationTypeNames.ExtensionMethodName]   = Color.Parse("#FFDCDCAA"),
            [ClassificationTypeNames.PropertyName]          = Color.Parse("#FF9CDCFE"),
            [ClassificationTypeNames.FieldName]             = Color.Parse("#FF9CDCFE"),
            [ClassificationTypeNames.EventName]             = Color.Parse("#FFFFE066"),
            [ClassificationTypeNames.DelegateName]          = Color.Parse("#FF4EC9B0"),
            [ClassificationTypeNames.TypeParameterName]     = Color.Parse("#FF4EC9B0"),
            [ClassificationTypeNames.NamespaceName]         = Color.Parse("#FFD7BA7D"),
            [ClassificationTypeNames.LocalName]             = Color.Parse("#FF9CDCFE"),
            [ClassificationTypeNames.ParameterName]         = Color.Parse("#FF9CDCFE"),
            [ClassificationTypeNames.ConstantName]          = Color.Parse("#FF4FC1FF"),
            [ClassificationTypeNames.LabelName]             = Color.Parse("#FFFF8C00"),
            [ClassificationTypeNames.PreprocessorKeyword]   = Color.Parse("#FF808080"),
            [ClassificationTypeNames.PreprocessorText]      = Color.Parse("#FFD4D4D4"),
            [ClassificationTypeNames.Punctuation]           = Color.Parse("#FFD4D4D4"),
            [ClassificationTypeNames.StaticSymbol]          = Color.Parse("#FFDCDCAA"),
            [ClassificationTypeNames.ExcludedCode]          = Color.Parse("#FF808080"),
            [ClassificationTypeNames.RegexText]             = Color.Parse("#FFD16969"),
            [ClassificationTypeNames.StringEscapeCharacter] = Color.Parse("#FFD7BA7D"),

            // ── XML / XAML tokens ─────────────────────────────────────────
            [ClassificationTypeNames.XmlDocCommentName]           = Color.Parse("#FF608B4E"),
            [ClassificationTypeNames.XmlDocCommentAttributeName]  = Color.Parse("#FF92CAF4"),
            [ClassificationTypeNames.XmlDocCommentDelimiter]      = Color.Parse("#FF808080"),
            [ClassificationTypeNames.XmlDocCommentComment]        = Color.Parse("#FF6A9955"),
            [ClassificationTypeNames.XmlDocCommentCDataSection]   = Color.Parse("#FFCE9178"),
            [ClassificationTypeNames.XmlDocCommentEntityReference]= Color.Parse("#FFD7BA7D"),
            [ClassificationTypeNames.XmlDocCommentProcessingInstruction] = Color.Parse("#FF808080"),
            ["xml name"]               = Color.Parse("#FF569CD6"),
            ["xml attribute name"]     = Color.Parse("#FF92CAF4"),
            ["xml attribute value"]    = Color.Parse("#FFCE9178"),
            ["xml delimiter"]          = Color.Parse("#FF808080"),
            ["xml comment"]            = Color.Parse("#FF6A9955"),
            ["xml cdata section"]      = Color.Parse("#FFCE9178"),
            ["xml text"]               = Color.Parse("#FFD4D4D4"),
        };

    public static Color GetTokenColor(string classification) =>
        TokenColors.TryGetValue(classification, out var c) ? c : DefaultText;
}

