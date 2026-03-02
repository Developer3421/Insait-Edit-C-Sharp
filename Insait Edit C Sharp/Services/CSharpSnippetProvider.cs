namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Snippet expansion helper. Actual snippet items are provided by Roslyn's
/// CompletionService (Microsoft.CodeAnalysis.Features) — we do NOT maintain
/// a custom hardcoded snippet list.
/// </summary>
public static class CSharpSnippetProvider
{
    /// <summary>
    /// Expands a snippet body that uses VS-style placeholders:
    ///   $0  — final cursor position
    ///   $1, $2 …  — tab-stop (removed)
    ///   ${1:placeholder} — tab-stop with default text (keeps the default)
    /// Returns the plain text and the offset where the cursor should land.
    /// </summary>
    public static (string text, int cursorOffset) ExpandSnippetBody(string body, string currentIndent)
    {
        // Newlines → newline + indent
        var expanded = body.Replace("\n", "\n" + currentIndent);

        // Use a marker so we can find cursor position after all replacements
        const string cursorMarker = "\x00CURSOR\x00";
        var withMarker = System.Text.RegularExpressions.Regex.Replace(expanded,
            @"\$\{(\d+):([^}]*)\}|\$(\d+)",
            m =>
            {
                if (m.Groups[1].Success)          // ${N:placeholder}
                {
                    if (m.Groups[1].Value == "0") return cursorMarker;
                    return m.Groups[2].Value;     // keep default text
                }
                // $N
                if (m.Groups[3].Value == "0") return cursorMarker;
                return "";
            });

        int cursorPos = withMarker.IndexOf(cursorMarker);
        var finalText = withMarker.Replace(cursorMarker, "");
        return (finalText, cursorPos >= 0 ? cursorPos : finalText.Length);
    }
}
