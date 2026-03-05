using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Full Roslyn auto-fix pipeline:
///   Diagnostics → CodeFixProvider → CodeAction → Apply fix
///
/// Uses Microsoft.CodeAnalysis.Features and Microsoft.CodeAnalysis.CSharp.Features
/// to discover built-in Roslyn CodeFixProviders (AddImport, GenerateType,
/// ImplementInterface, etc.) and produce concrete text-change operations.
/// </summary>
public sealed class RoslynAutoFixService : IDisposable
{
    private readonly MefHostServices          _host;
    private readonly AdhocWorkspace           _workspace;
    private readonly List<MetadataReference>  _defaultRefs;
    private readonly List<CodeFixProvider>    _codeFixProviders;
    private readonly List<CodeRefactoringProvider> _refactoringProviders;

    private ProjectId?  _projectId;
    private DocumentId? _documentId;
    private string?     _trackedFilePath;

    public RoslynAutoFixService()
    {
        _host           = MefHostServices.Create(BuildMefAssemblies());
        _workspace      = new AdhocWorkspace(_host);
        _defaultRefs    = RoslynCompletionEngine.CollectPublicDefaultReferences();
        _codeFixProviders    = DiscoverCodeFixProviders().ToList();
        _refactoringProviders = DiscoverRefactoringProviders().ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  1. Get all diagnostics + available fixes for a file
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyze the source code and return all diagnostics paired with their
    /// available Roslyn code-fix actions.
    /// </summary>
    public async Task<List<AutoFixDiagnosticEntry>> GetDiagnosticsWithFixesAsync(
        string filePath, string sourceCode, CancellationToken ct = default)
    {
        var results = new List<AutoFixDiagnosticEntry>();
        if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return results;

        try
        {
            var document = SyncDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(ct);
            var syntaxTree    = await document.GetSyntaxTreeAsync(ct);
            if (semanticModel == null || syntaxTree == null) return results;

            // Gather all diagnostics
            var diagnostics = semanticModel.GetDiagnostics(cancellationToken: ct)
                .Concat(syntaxTree.GetDiagnostics(ct))
                .Where(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden)
                .Where(d => d.Location.IsInSource)
                .ToImmutableArray();

            foreach (var diag in diagnostics)
            {
                ct.ThrowIfCancellationRequested();

                var lineSpan = diag.Location.GetLineSpan();
                var entry = new AutoFixDiagnosticEntry
                {
                    DiagnosticId = diag.Id,
                    Message      = diag.GetMessage(),
                    Severity     = ConvertSeverity(diag.Severity),
                    Line         = lineSpan.StartLinePosition.Line + 1,
                    Column       = lineSpan.StartLinePosition.Character + 1,
                    StartOffset  = diag.Location.SourceSpan.Start,
                    EndOffset    = diag.Location.SourceSpan.End,
                };

                // Find Roslyn CodeFix actions for this diagnostic
                var fixes = await CollectCodeFixesAsync(document, diag, ct);
                entry.AvailableFixes.AddRange(fixes);

                // Also add built-in quick-fix patterns
                AddBuiltInFixes(entry, sourceCode);

                if (entry.AvailableFixes.Count > 0)
                    results.Add(entry);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoFix] analysis error: {ex.Message}");
        }

        return results.OrderBy(e => e.Severity).ThenBy(e => e.Line).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  2. Get refactoring suggestions at a position
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<AutoFixAction>> GetRefactoringsAsync(
        string filePath, string sourceCode, int position, CancellationToken ct = default)
    {
        var actions = new List<AutoFixAction>();
        if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return actions;

        try
        {
            var document = SyncDocument(filePath, sourceCode);
            var text = await document.GetTextAsync(ct);
            var span = TextSpan.FromBounds(
                Math.Max(0, position), Math.Min(text.Length, position + 1));

            foreach (var provider in _refactoringProviders)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var context = new CodeRefactoringContext(
                        document, span,
                        action => actions.Add(new AutoFixAction
                        {
                            Title       = action.Title,
                            Kind        = AutoFixKind.Refactoring,
                            CodeAction  = action,
                        }),
                        ct);
                    await provider.ComputeRefactoringsAsync(context);
                }
                catch { /* skip broken providers */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoFix] refactoring error: {ex.Message}");
        }

        return actions;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  3. Apply a CodeAction — returns the new full source text
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a Roslyn CodeAction and returns the new source text.
    /// </summary>
    public async Task<string?> ApplyCodeActionAsync(
        string filePath, string sourceCode, AutoFixAction action, CancellationToken ct = default)
    {
        if (action.CodeAction == null)
            return null;

        try
        {
            var document = SyncDocument(filePath, sourceCode);
            var operations = await action.CodeAction.GetOperationsAsync(ct);

            foreach (var op in operations)
            {
                if (op is ApplyChangesOperation applyOp)
                {
                    var changedSolution = applyOp.ChangedSolution;
                    var changedDoc = changedSolution.GetDocument(_documentId!);
                    if (changedDoc != null)
                    {
                        var newText = await changedDoc.GetTextAsync(ct);
                        return newText.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoFix] apply error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Apply all fixes for a specific diagnostic code at once.
    /// Returns the new source text after all fixes, or null on failure.
    /// </summary>
    public async Task<string?> ApplyAllFixesForCodeAsync(
        string filePath, string sourceCode, string diagnosticCode, CancellationToken ct = default)
    {
        var entries = await GetDiagnosticsWithFixesAsync(filePath, sourceCode, ct);
        var matching = entries
            .Where(e => e.DiagnosticId == diagnosticCode && e.AvailableFixes.Any(f => f.CodeAction != null))
            .ToList();

        if (matching.Count == 0) return null;

        string current = sourceCode;
        foreach (var entry in matching)
        {
            var fix = entry.AvailableFixes.FirstOrDefault(f => f.CodeAction != null);
            if (fix == null) continue;

            var result = await ApplyCodeActionAsync(filePath, current, fix, ct);
            if (result != null)
            {
                current = result;
                // Re-sync workspace with updated code
                SyncDocument(filePath, current);
            }
        }

        return current != sourceCode ? current : null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  4. Code templates / snippet insertion
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a set of C# code templates that can be inserted at a position.
    /// </summary>
    public static List<CodeTemplate> GetCodeTemplates()
    {
        return new List<CodeTemplate>
        {
            // ── Control flow ─────────────────────────────────────────
            new() { Category = "Control Flow", Name = "if",            Keyword = "if",
                    Snippet = "if ($condition$)\n{\n    $body$\n}",
                    Description = "if statement" },
            new() { Category = "Control Flow", Name = "if-else",       Keyword = "ifelse",
                    Snippet = "if ($condition$)\n{\n    $body$\n}\nelse\n{\n    $else$\n}",
                    Description = "if-else statement" },
            new() { Category = "Control Flow", Name = "switch",        Keyword = "switch",
                    Snippet = "switch ($expression$)\n{\n    case $value$:\n        $body$\n        break;\n    default:\n        break;\n}",
                    Description = "switch statement" },
            new() { Category = "Control Flow", Name = "for",           Keyword = "for",
                    Snippet = "for (int i = 0; i < $length$; i++)\n{\n    $body$\n}",
                    Description = "for loop" },
            new() { Category = "Control Flow", Name = "foreach",       Keyword = "foreach",
                    Snippet = "foreach (var $item$ in $collection$)\n{\n    $body$\n}",
                    Description = "foreach loop" },
            new() { Category = "Control Flow", Name = "while",         Keyword = "while",
                    Snippet = "while ($condition$)\n{\n    $body$\n}",
                    Description = "while loop" },
            new() { Category = "Control Flow", Name = "do-while",      Keyword = "dowhile",
                    Snippet = "do\n{\n    $body$\n} while ($condition$);",
                    Description = "do-while loop" },
            new() { Category = "Control Flow", Name = "try-catch",     Keyword = "try",
                    Snippet = "try\n{\n    $body$\n}\ncatch (Exception ex)\n{\n    $handler$\n}",
                    Description = "try-catch block" },
            new() { Category = "Control Flow", Name = "try-catch-finally", Keyword = "tryf",
                    Snippet = "try\n{\n    $body$\n}\ncatch (Exception ex)\n{\n    $handler$\n}\nfinally\n{\n    $cleanup$\n}",
                    Description = "try-catch-finally block" },
            new() { Category = "Control Flow", Name = "using statement", Keyword = "using",
                    Snippet = "using (var $resource$ = $expression$)\n{\n    $body$\n}",
                    Description = "using resource statement" },

            // ── Types ─────────────────────────────────────────────────
            new() { Category = "Types", Name = "class",               Keyword = "class",
                    Snippet = "public class $ClassName$\n{\n    $body$\n}",
                    Description = "class declaration" },
            new() { Category = "Types", Name = "interface",           Keyword = "interface",
                    Snippet = "public interface I$Name$\n{\n    $members$\n}",
                    Description = "interface declaration" },
            new() { Category = "Types", Name = "record",              Keyword = "record",
                    Snippet = "public record $RecordName$($parameters$);",
                    Description = "record declaration" },
            new() { Category = "Types", Name = "struct",              Keyword = "struct",
                    Snippet = "public struct $StructName$\n{\n    $body$\n}",
                    Description = "struct declaration" },
            new() { Category = "Types", Name = "enum",                Keyword = "enum",
                    Snippet = "public enum $EnumName$\n{\n    $Value1$,\n    $Value2$,\n}",
                    Description = "enum declaration" },

            // ── Members ───────────────────────────────────────────────
            new() { Category = "Members", Name = "property",          Keyword = "prop",
                    Snippet = "public $Type$ $Name$ { get; set; }",
                    Description = "auto property" },
            new() { Category = "Members", Name = "property (full)",   Keyword = "propfull",
                    Snippet = "private $Type$ _$name$;\npublic $Type$ $Name$\n{\n    get => _$name$;\n    set => _$name$ = value;\n}",
                    Description = "full property with backing field" },
            new() { Category = "Members", Name = "method",            Keyword = "method",
                    Snippet = "public $ReturnType$ $MethodName$($parameters$)\n{\n    $body$\n}",
                    Description = "method declaration" },
            new() { Category = "Members", Name = "async method",      Keyword = "asyncmethod",
                    Snippet = "public async Task$<ReturnType>$ $MethodName$Async($parameters$)\n{\n    $body$\n}",
                    Description = "async method declaration" },
            new() { Category = "Members", Name = "constructor",       Keyword = "ctor",
                    Snippet = "public $ClassName$($parameters$)\n{\n    $body$\n}",
                    Description = "constructor" },
            new() { Category = "Members", Name = "event",             Keyword = "event",
                    Snippet = "public event EventHandler$<EventArgs>$? $EventName$;",
                    Description = "event declaration" },

            // ── Patterns ──────────────────────────────────────────────
            new() { Category = "Patterns", Name = "IDisposable",      Keyword = "dispose",
                    Snippet = "private bool _disposed;\n\nprotected virtual void Dispose(bool disposing)\n{\n    if (!_disposed)\n    {\n        if (disposing)\n        {\n            $managed$\n        }\n        _disposed = true;\n    }\n}\n\npublic void Dispose()\n{\n    Dispose(true);\n    GC.SuppressFinalize(this);\n}",
                    Description = "IDisposable pattern" },
            new() { Category = "Patterns", Name = "Singleton",        Keyword = "singleton",
                    Snippet = "private static readonly Lazy<$ClassName$> _instance = new(() => new $ClassName$());\npublic static $ClassName$ Instance => _instance.Value;\n\nprivate $ClassName$() { }",
                    Description = "Thread-safe singleton pattern" },
            new() { Category = "Patterns", Name = "INotifyPropertyChanged", Keyword = "inpc",
                    Snippet = "public event PropertyChangedEventHandler? PropertyChanged;\n\nprotected void OnPropertyChanged([CallerMemberName] string? propertyName = null)\n{\n    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));\n}\n\nprotected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)\n{\n    if (EqualityComparer<T>.Default.Equals(field, value)) return false;\n    field = value;\n    OnPropertyChanged(propertyName);\n    return true;\n}",
                    Description = "INotifyPropertyChanged implementation" },
            new() { Category = "Patterns", Name = "Builder",          Keyword = "builder",
                    Snippet = "public class $Name$Builder\n{\n    private readonly $Name$ _obj = new();\n\n    public $Name$Builder With$Prop$($Type$ value)\n    {\n        _obj.$Prop$ = value;\n        return this;\n    }\n\n    public $Name$ Build() => _obj;\n}",
                    Description = "Builder pattern" },

            // ── LINQ / Async ──────────────────────────────────────────
            new() { Category = "LINQ / Async", Name = "LINQ query",   Keyword = "linq",
                    Snippet = "var result = $source$\n    .Where(x => $condition$)\n    .Select(x => $projection$)\n    .ToList();",
                    Description = "LINQ method chain" },
            new() { Category = "LINQ / Async", Name = "async/await",  Keyword = "asyncawait",
                    Snippet = "var result = await Task.Run(() =>\n{\n    $body$\n    return $result$;\n});",
                    Description = "async/await with Task.Run" },
            new() { Category = "LINQ / Async", Name = "Task.WhenAll", Keyword = "whenall",
                    Snippet = "var tasks = new[]\n{\n    $Task1$,\n    $Task2$,\n};\nawait Task.WhenAll(tasks);",
                    Description = "Task.WhenAll pattern" },

            // ── Test ──────────────────────────────────────────────────
            new() { Category = "Testing", Name = "[Fact] test",       Keyword = "xfact",
                    Snippet = "[Fact]\npublic void $TestName$()\n{\n    // Arrange\n    $arrange$\n\n    // Act\n    $act$\n\n    // Assert\n    $assert$\n}",
                    Description = "xUnit [Fact] test method" },
            new() { Category = "Testing", Name = "[Theory] test",     Keyword = "xtheory",
                    Snippet = "[Theory]\n[InlineData($data$)]\npublic void $TestName$($parameters$)\n{\n    // Arrange & Act\n    $body$\n\n    // Assert\n    $assert$\n}",
                    Description = "xUnit [Theory] test method" },
        };
    }

    /// <summary>
    /// Returns commonly used C# keywords for quick insertion.
    /// </summary>
    public static List<KeywordItem> GetKeywordItems()
    {
        return new List<KeywordItem>
        {
            new("var",       "Implicitly typed local variable"),
            new("async",     "Async method modifier"),
            new("await",     "Await an asynchronous operation"),
            new("yield",     "yield return / yield break"),
            new("nameof",    "nameof() expression"),
            new("typeof",    "typeof() expression"),
            new("default",   "default value expression"),
            new("is",        "Pattern matching / type check"),
            new("as",        "Type cast with null on failure"),
            new("when",      "Filter clause in catch/switch"),
            new("with",      "Record with-expression"),
            new("init",      "Init-only setter"),
            new("required",  "Required member modifier"),
            new("record",    "Record type declaration"),
            new("global",    "global using directive"),
            new("file",      "File-scoped type modifier"),
            new("scoped",    "Scoped ref modifier"),
            new("readonly",  "Read-only modifier"),
            new("sealed",    "Sealed class modifier"),
            new("abstract",  "Abstract member/class"),
            new("virtual",   "Virtual member modifier"),
            new("override",  "Override base member"),
            new("partial",   "Partial type/method"),
            new("static",    "Static member modifier"),
            new("unsafe",    "Unsafe context"),
            new("stackalloc","Stack allocation"),
            new("params",    "Variable-length parameter list"),
            new("ref",       "Reference parameter"),
            new("out",       "Output parameter"),
            new("in",        "Read-only reference parameter"),
            new("new()",     "Constructor constraint"),
            new("where",     "Generic type constraint"),
            new("lock",      "Thread synchronization"),
            new("volatile",  "Volatile field modifier"),
            new("extern",    "External method declaration"),
            new("checked",   "Checked arithmetic context"),
            new("unchecked", "Unchecked arithmetic context"),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Internal: collect Roslyn code fixes
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<List<AutoFixAction>> CollectCodeFixesAsync(
        Document document, Diagnostic diagnostic, CancellationToken ct)
    {
        var actions = new List<AutoFixAction>();

        foreach (var provider in _codeFixProviders)
        {
            ct.ThrowIfCancellationRequested();
            if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                continue;

            try
            {
                var context = new CodeFixContext(
                    document, diagnostic,
                    (codeAction, _) =>
                    {
                        actions.Add(new AutoFixAction
                        {
                            Title        = codeAction.Title,
                            Kind         = AutoFixKind.RoslynCodeFix,
                            CodeAction   = codeAction,
                            ProviderName = provider.GetType().Name,
                        });
                    },
                    ct);

                await provider.RegisterCodeFixesAsync(context);
            }
            catch { /* skip failing providers */ }
        }

        return actions;
    }

    private static void AddBuiltInFixes(AutoFixDiagnosticEntry entry, string sourceCode)
    {
        switch (entry.DiagnosticId)
        {
            case "CS1002": // missing semicolon
                entry.AvailableFixes.Add(new AutoFixAction
                {
                    Title       = "Insert missing semicolon ';'",
                    Kind        = AutoFixKind.InsertText,
                    InsertText  = ";",
                    InsertOffset = entry.EndOffset,
                });
                break;

            case "CS0168": // unused variable
            case "CS0219":
                entry.AvailableFixes.Add(new AutoFixAction
                {
                    Title = "Remove unused variable",
                    Kind  = AutoFixKind.RemoveText,
                });
                break;

            case "CS8600": case "CS8601": case "CS8602": case "CS8603": case "CS8604":
                entry.AvailableFixes.Add(new AutoFixAction
                {
                    Title = "Add null check",
                    Kind  = AutoFixKind.InsertText,
                });
                entry.AvailableFixes.Add(new AutoFixAction
                {
                    Title       = "Use null-forgiving operator (!)",
                    Kind        = AutoFixKind.InsertText,
                    InsertText  = "!",
                    InsertOffset = entry.EndOffset,
                });
                break;

            case "CS0501": // missing method body
            case "CS0161":
                entry.AvailableFixes.Add(new AutoFixAction
                {
                    Title      = "Add method body { throw new NotImplementedException(); }",
                    Kind       = AutoFixKind.InsertText,
                    InsertText = "\n{\n    throw new NotImplementedException();\n}",
                    InsertOffset = entry.EndOffset,
                });
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Workspace
    // ═══════════════════════════════════════════════════════════════════════

    private Document SyncDocument(string filePath, string sourceCode)
    {
        if (_trackedFilePath != filePath)
            RebuildProject(filePath, sourceCode);
        else
        {
            var doc = _workspace.CurrentSolution.GetDocument(_documentId!);
            if (doc != null)
            {
                var updated = doc.WithText(SourceText.From(sourceCode));
                _workspace.TryApplyChanges(updated.Project.Solution);
            }
        }
        return _workspace.CurrentSolution.GetDocument(_documentId!)!;
    }

    private void RebuildProject(string filePath, string sourceCode)
    {
        if (_projectId != null)
            _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(_projectId));

        var pid = ProjectId.CreateNewId();
        var did = DocumentId.CreateNewId(pid);

        var info = ProjectInfo.Create(pid, VersionStamp.Create(),
            "AutoFixProject", "AutoFixProject", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: _defaultRefs);

        var sol = _workspace.CurrentSolution.AddProject(info);
        sol = sol.AddDocument(DocumentInfo.Create(did, Path.GetFileName(filePath),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceCode), VersionStamp.Create())),
            filePath: filePath));
        _workspace.TryApplyChanges(sol);

        _projectId       = pid;
        _documentId      = did;
        _trackedFilePath = filePath;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Discovery
    // ═══════════════════════════════════════════════════════════════════════

    private static IEnumerable<CodeFixProvider> DiscoverCodeFixProviders()
    {
        var assemblies = BuildMefAssemblies();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); } catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(CodeFixProvider).IsAssignableFrom(type) || type.IsAbstract)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                CodeFixProvider? provider = null;
                try { provider = (CodeFixProvider?)Activator.CreateInstance(type); } catch { }
                if (provider != null) yield return provider;
            }
        }
    }

    private static IEnumerable<CodeRefactoringProvider> DiscoverRefactoringProviders()
    {
        var assemblies = BuildMefAssemblies();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); } catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(CodeRefactoringProvider).IsAssignableFrom(type) || type.IsAbstract)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                CodeRefactoringProvider? provider = null;
                try { provider = (CodeRefactoringProvider?)Activator.CreateInstance(type); } catch { }
                if (provider != null) yield return provider;
            }
        }
    }

    private static DiagnosticSeverityKind ConvertSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity s)
        => s switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error   => DiagnosticSeverityKind.Error,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => DiagnosticSeverityKind.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info    => DiagnosticSeverityKind.Info,
            _                                                 => DiagnosticSeverityKind.Hint,
        };

    private static IEnumerable<Assembly> BuildMefAssemblies()
    {
        var set = new HashSet<Assembly>(MefHostServices.DefaultAssemblies);
        foreach (var name in new[]
        {
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Features",
            "Microsoft.CodeAnalysis.Workspaces.Common",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
        })
        {
            try { set.Add(Assembly.Load(name)); } catch { }
        }
        return set;
    }

    public void Dispose() => _workspace.Dispose();
}

// ═══════════════════════════════════════════════════════════════════════════
//  Data models
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A diagnostic entry together with all available auto-fix actions.
/// </summary>
public sealed class AutoFixDiagnosticEntry
{
    public string               DiagnosticId   { get; init; } = string.Empty;
    public string               Message        { get; init; } = string.Empty;
    public DiagnosticSeverityKind Severity     { get; init; }
    public int                  Line           { get; init; }
    public int                  Column         { get; init; }
    public int                  StartOffset    { get; init; }
    public int                  EndOffset      { get; init; }
    public List<AutoFixAction>  AvailableFixes { get; } = new();

    public string SeverityIcon => Severity switch
    {
        DiagnosticSeverityKind.Error   => "⛔",
        DiagnosticSeverityKind.Warning => "⚠",
        DiagnosticSeverityKind.Info    => "ℹ",
        _                              => "💡",
    };
}

/// <summary>
/// A single fix action that can be applied.
/// </summary>
public sealed class AutoFixAction
{
    public string       Title        { get; init; } = string.Empty;
    public AutoFixKind  Kind         { get; init; }
    public CodeAction?  CodeAction   { get; init; }
    public string?      InsertText   { get; init; }
    public int          InsertOffset { get; init; }
    public string?      ProviderName { get; init; }

    public string KindIcon => Kind switch
    {
        AutoFixKind.RoslynCodeFix => "🔧",
        AutoFixKind.Refactoring   => "🔄",
        AutoFixKind.InsertText    => "✏",
        AutoFixKind.RemoveText    => "✂",
        AutoFixKind.AddUsing      => "📦",
        _                         => "💡",
    };
}

public enum AutoFixKind
{
    RoslynCodeFix,
    Refactoring,
    InsertText,
    RemoveText,
    AddUsing,
    Other,
}

/// <summary>Code template / snippet.</summary>
public sealed class CodeTemplate
{
    public string Category    { get; init; } = string.Empty;
    public string Name        { get; init; } = string.Empty;
    public string Keyword     { get; init; } = string.Empty;
    public string Snippet     { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>Keyword quick-insert item.</summary>
public sealed class KeywordItem
{
    public string Keyword     { get; }
    public string Description { get; }

    public KeywordItem(string keyword, string description)
    {
        Keyword     = keyword;
        Description = description;
    }
}

