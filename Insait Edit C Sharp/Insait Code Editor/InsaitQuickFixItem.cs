using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp.InsaitCodeEditor;

internal sealed class InsaitQuickFixItem
{
    public QuickFixSuggestion Suggestion      { get; }
    public DiagnosticSpan     SourceDiagnostic { get; }

    public InsaitQuickFixItem(QuickFixSuggestion suggestion, DiagnosticSpan diag)
    {
        Suggestion       = suggestion;
        SourceDiagnostic = diag;
    }

    public override string ToString()
    {
        var icon = Suggestion.Kind switch
        {
            QuickFixKind.AddUsing     => "📦",
            QuickFixKind.InstallNuGet => "⬇",
            QuickFixKind.InsertCode   => "✏",
            QuickFixKind.RemoveCode   => "🗑",
            QuickFixKind.RoslynFix    => "🔧",
            _                         => "💡",
        };
        return $"{icon}  {Suggestion.Title}";
    }
}

