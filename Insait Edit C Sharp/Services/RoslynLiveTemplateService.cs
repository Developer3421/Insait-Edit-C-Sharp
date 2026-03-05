using System;
using System.Collections.Generic;
using System.Linq;

namespace Insait_Edit_C_Sharp.Services;

// ═══════════════════════════════════════════════════════════════════════════
//  RoslynLiveTemplateService
//  ─────────────────────────────────────────────────────────────────────────
//  Provides Roslyn-style live templates (code snippets) for C# code.
//  Each template has a shortcut, a body with VS-style tab-stop placeholders
//  ($1, ${1:default}, $0 for final cursor), and a human-readable description.
//
//  Templates mirror IntelliJ / ReSharper / Visual Studio built-in snippets
//  and are surfaced as "Snippet" kind in the completion list.
// ═══════════════════════════════════════════════════════════════════════════

public static class RoslynLiveTemplateService
{
    /// <summary>
    /// A single live template definition.
    /// </summary>
    public sealed class LiveTemplate
    {
        /// <summary>Shortcut text the user types (e.g. "for", "prop").</summary>
        public string Shortcut { get; init; } = string.Empty;

        /// <summary>Human-readable description shown in the completion list.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Template body with VS-style placeholders:
        ///   ${N:defaultText}  — tab-stop N with default text
        ///   $N                — tab-stop N (empty)
        ///   $0                — final cursor position
        /// Newlines use \n; the service auto-indents on insertion.
        /// </summary>
        public string Body { get; init; } = string.Empty;
    }

    // ── Built-in template catalogue ──────────────────────────────────────

    private static readonly List<LiveTemplate> _templates = new()
    {
        // ── Iteration ────────────────────────────────────────────────────
        new()
        {
            Shortcut    = "for",
            Description = "for loop",
            Body        = "for (int ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++)\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "forr",
            Description = "for loop (reverse)",
            Body        = "for (int ${1:i} = ${2:length} - 1; ${1:i} >= 0; ${1:i}--)\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "foreach",
            Description = "foreach loop",
            Body        = "foreach (var ${1:item} in ${2:collection})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "while",
            Description = "while loop",
            Body        = "while (${1:condition})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "do",
            Description = "do-while loop",
            Body        = "do\n{\n    $0\n} while (${1:condition});",
        },

        // ── Conditionals ─────────────────────────────────────────────────
        new()
        {
            Shortcut    = "if",
            Description = "if statement",
            Body        = "if (${1:condition})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "ife",
            Description = "if-else statement",
            Body        = "if (${1:condition})\n{\n    $2\n}\nelse\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "else",
            Description = "else block",
            Body        = "else\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "switch",
            Description = "switch statement",
            Body        = "switch (${1:expression})\n{\n    case ${2:value}:\n        $0\n        break;\n    default:\n        break;\n}",
        },

        // ── Exception handling ───────────────────────────────────────────
        new()
        {
            Shortcut    = "try",
            Description = "try-catch block",
            Body        = "try\n{\n    $0\n}\ncatch (${1:Exception} ${2:ex})\n{\n    ${3:throw;}\n}",
        },
        new()
        {
            Shortcut    = "tryf",
            Description = "try-finally block",
            Body        = "try\n{\n    $0\n}\nfinally\n{\n    $1\n}",
        },
        new()
        {
            Shortcut    = "trycf",
            Description = "try-catch-finally block",
            Body        = "try\n{\n    $0\n}\ncatch (${1:Exception} ${2:ex})\n{\n    ${3:throw;}\n}\nfinally\n{\n    $4\n}",
        },

        // ── Properties ──────────────────────────────────────────────────
        new()
        {
            Shortcut    = "prop",
            Description = "auto-property",
            Body        = "public ${1:int} ${2:MyProperty} { get; set; }$0",
        },
        new()
        {
            Shortcut    = "propfull",
            Description = "property with backing field",
            Body        = "private ${1:int} _${2:myField};\npublic ${1:int} ${3:MyProperty}\n{\n    get => _${2:myField};\n    set => _${2:myField} = value;\n}$0",
        },
        new()
        {
            Shortcut    = "propg",
            Description = "auto-property (get-only)",
            Body        = "public ${1:int} ${2:MyProperty} { get; }$0",
        },
        new()
        {
            Shortcut    = "propi",
            Description = "auto-property with init",
            Body        = "public ${1:int} ${2:MyProperty} { get; init; }$0",
        },

        // ── Type declarations ────────────────────────────────────────────
        new()
        {
            Shortcut    = "class",
            Description = "class declaration",
            Body        = "public class ${1:ClassName}\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "interface",
            Description = "interface declaration",
            Body        = "public interface ${1:IInterfaceName}\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "struct",
            Description = "struct declaration",
            Body        = "public struct ${1:StructName}\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "record",
            Description = "record declaration",
            Body        = "public record ${1:RecordName}(${2:int Value})$0;",
        },
        new()
        {
            Shortcut    = "enum",
            Description = "enum declaration",
            Body        = "public enum ${1:EnumName}\n{\n    ${2:Value1},\n    $0\n}",
        },

        // ── Members ─────────────────────────────────────────────────────
        new()
        {
            Shortcut    = "ctor",
            Description = "constructor",
            Body        = "public ${1:ClassName}(${2:})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "mth",
            Description = "method",
            Body        = "public ${1:void} ${2:MethodName}(${3:})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "svm",
            Description = "static void Main",
            Body        = "static void Main(string[] args)\n{\n    $0\n}",
        },

        // ── Common patterns ─────────────────────────────────────────────
        new()
        {
            Shortcut    = "cw",
            Description = "Console.WriteLine",
            Body        = "Console.WriteLine(${1:});$0",
        },
        new()
        {
            Shortcut    = "cr",
            Description = "Console.ReadLine",
            Body        = "Console.ReadLine()$0",
        },
        new()
        {
            Shortcut    = "cwl",
            Description = "Console.WriteLine with string interpolation",
            Body        = "Console.WriteLine($\"${1:}\");$0",
        },

        // ── Using / Namespace ───────────────────────────────────────────
        new()
        {
            Shortcut    = "using",
            Description = "using statement",
            Body        = "using (${1:var resource = new Resource()})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "namespace",
            Description = "namespace declaration",
            Body        = "namespace ${1:MyNamespace}\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "ns",
            Description = "file-scoped namespace",
            Body        = "namespace ${1:MyNamespace};$0",
        },

        // ── Async ───────────────────────────────────────────────────────
        new()
        {
            Shortcut    = "task",
            Description = "async Task method",
            Body        = "public async Task ${1:MethodNameAsync}(${2:})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "taskr",
            Description = "async Task<T> method",
            Body        = "public async Task<${1:int}> ${2:MethodNameAsync}(${3:})\n{\n    $0\n}",
        },

        // ── LINQ ────────────────────────────────────────────────────────
        new()
        {
            Shortcut    = "linq",
            Description = "LINQ query expression",
            Body        = "var ${1:result} = from ${2:item} in ${3:collection}\n              where ${4:condition}\n              select ${5:item};$0",
        },

        // ── Lock / Dispose ──────────────────────────────────────────────
        new()
        {
            Shortcut    = "lock",
            Description = "lock statement",
            Body        = "lock (${1:lockObject})\n{\n    $0\n}",
        },
        new()
        {
            Shortcut    = "dispose",
            Description = "IDisposable pattern",
            Body        = "protected virtual void Dispose(bool disposing)\n{\n    if (disposing)\n    {\n        $0\n    }\n}\n\npublic void Dispose()\n{\n    Dispose(true);\n    GC.SuppressFinalize(this);\n}",
        },

        // ── Testing ─────────────────────────────────────────────────────
        new()
        {
            Shortcut    = "test",
            Description = "[Test] method (NUnit / xUnit style)",
            Body        = "[${1:Fact}]\npublic void ${2:TestMethodName}()\n{\n    // Arrange\n    $0\n\n    // Act\n\n    // Assert\n}",
        },

        // ── Miscellaneous ───────────────────────────────────────────────
        new()
        {
            Shortcut    = "region",
            Description = "#region block",
            Body        = "#region ${1:RegionName}\n$0\n#endregion",
        },
        new()
        {
            Shortcut    = "todo",
            Description = "TODO comment",
            Body        = "// TODO: ${1:description}$0",
        },
        new()
        {
            Shortcut    = "summary",
            Description = "XML doc summary",
            Body        = "/// <summary>\n/// ${1:Description}\n/// </summary>$0",
        },
        new()
        {
            Shortcut    = "param",
            Description = "XML doc param",
            Body        = "/// <param name=\"${1:name}\">${2:Description}</param>$0",
        },
        new()
        {
            Shortcut    = "returns",
            Description = "XML doc returns",
            Body        = "/// <returns>${1:Description}</returns>$0",
        },
        new()
        {
            Shortcut    = "exception",
            Description = "throw new exception",
            Body        = "throw new ${1:Exception}(${2:\"message\"});$0",
        },
        new()
        {
            Shortcut    = "null",
            Description = "null check with throw",
            Body        = "if (${1:value} is null)\n    throw new ArgumentNullException(nameof(${1:value}));$0",
        },
        new()
        {
            Shortcut    = "equals",
            Description = "Equals override",
            Body        = "public override bool Equals(object? obj)\n{\n    if (obj is not ${1:ClassName} other) return false;\n    return ${2:this.Property == other.Property};\n}\n\npublic override int GetHashCode()\n{\n    return ${3:HashCode.Combine(Property)};\n}$0",
        },
        new()
        {
            Shortcut    = "tostring",
            Description = "ToString override",
            Body        = "public override string ToString()\n{\n    return $\"${1:}\";$0\n}",
        },
    };

    /// <summary>
    /// Returns all registered live templates.
    /// </summary>
    public static IReadOnlyList<LiveTemplate> GetAllTemplates() => _templates;

    /// <summary>
    /// Returns templates whose shortcut starts with the given prefix (case-insensitive).
    /// </summary>
    public static IReadOnlyList<LiveTemplate> GetTemplatesForPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return Array.Empty<LiveTemplate>();

        return _templates
            .Where(t => t.Shortcut.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Finds a template by exact shortcut match (case-insensitive).
    /// </summary>
    public static LiveTemplate? FindByShortcut(string shortcut)
    {
        return _templates.FirstOrDefault(
            t => t.Shortcut.Equals(shortcut, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Converts templates to <see cref="RoslynCompletionItem"/> so they can be
    /// merged into the standard completion list.
    /// </summary>
    public static IReadOnlyList<RoslynCompletionItem> ToCompletionItems(string prefix)
    {
        var matching = GetTemplatesForPrefix(prefix);
        return matching.Select(t => new RoslynCompletionItem
        {
            Label      = t.Shortcut,
            InsertText = t.Body,
            Kind       = "Snippet",
            Detail     = $"[Template] {t.Description}",
            FilterText = t.Shortcut,
            SortText   = $"0000_{t.Shortcut}", // sort templates near the top
        }).ToList();
    }
}

