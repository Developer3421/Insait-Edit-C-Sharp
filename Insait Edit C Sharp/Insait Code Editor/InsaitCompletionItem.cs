using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.InsaitCodeEditor;

internal sealed class InsaitCompletionItem
{
    public string Label      { get; }
    public string InsertText { get; }
    public string Kind       { get; }
    public string Detail     { get; }

    /// <summary>The original Roslyn item — used to resolve the real text change.</summary>
    public RoslynCompletionItem Source { get; }

    public InsaitCompletionItem(RoslynCompletionItem item)
    {
        Label      = item.Label;
        InsertText = item.InsertText;
        Kind       = item.Kind;
        Detail     = item.Detail ?? string.Empty;
        Source     = item;
    }

    public override string ToString()
    {
        var icon = KindIcon(Kind);
        return Detail.Length > 0 ? $"{icon}  {Label}  —  {Detail}" : $"{icon}  {Label}";
    }

    private static string KindIcon(string k) => k switch
    {
        "Method"    => "⚙",
        "Class"     => "◈",
        "Interface" => "◇",
        "Property"  => "▸",
        "Field"     => "▫",
        "Keyword"   => "✦",
        "Variable"  => "○",
        "Enum"      => "◆",
        "Event"     => "⚡",
        "Namespace" => "▣",
        "Struct"    => "◫",
        "Delegate"  => "⊕",
        "Constant"  => "◉",
        "Snippet"   => "✂",
        _           => "·",
    };
}

