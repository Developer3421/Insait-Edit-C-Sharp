using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FSharp.Compiler.CodeAnalysis;
using FSharp.Compiler.EditorServices;
using FSharp.Compiler.Text;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// F# autocompletion engine powered by <b>FSharp.Compiler.Service</b> (≥ 43.x).
///
/// Required NuGet package: FSharp.Compiler.Service
///
/// Provides:
///  • Completion (declaration list) at caret position
///  • Quick-info / tooltip for the symbol under caret
/// </summary>
public sealed class FSharpCompletionEngine : IDisposable
{
    private readonly FSharpChecker _checker;

    private FSharpProjectOptions? _projectOptions;
    private string?               _trackedFilePath;

    private static FSharpOption<T> None<T>() => FSharpOption<T>.None;

    public FSharpCompletionEngine()
    {
        // All parameters of FSharpChecker.Create are optional in F# but required in C#.
        // We call via reflection so this compiles regardless of the FCS version.
        _checker = CreateCheckerViaReflection();
    }

    // ── public API ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FSharpLspCompletionItem>> GetCompletionsAsync(
        string filePath,
        string sourceCode,
        int line,
        int column,
        CancellationToken ct = default)
    {
        try
        {
            var opts       = GetOrCreateProjectOptions(filePath);
            var sourceText = SourceText.ofString(sourceCode);
            var lineText   = GetLineText(sourceCode, line);

            var (parseResults, checkAnswer) = await RunAsync(
                _checker.ParseAndCheckFileInProject(
                    filePath, 0, sourceText, opts, None<string>()), ct);

            ct.ThrowIfCancellationRequested();

            if (checkAnswer is not FSharpCheckFileAnswer.Succeeded checkSucceeded)
                return Array.Empty<FSharpLspCompletionItem>();
            var checkResults = checkSucceeded.Item;

            var partialLongName = QuickParse.GetPartialLongNameEx(lineText, column - 1);

            // Use reflection to call GetDeclarationListInfo / GetDeclarationListAsync
            // (method name varies across FCS versions)
            var decls = await GetDeclarationsViaReflection(
                checkResults, parseResults, line, lineText, partialLongName, ct);

            ct.ThrowIfCancellationRequested();

            return decls;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FSharpCompletion] {ex.Message}");
            return Array.Empty<FSharpLspCompletionItem>();
        }
    }

    public async Task<string?> GetQuickInfoAsync(
        string filePath,
        string sourceCode,
        int line,
        int column,
        CancellationToken ct = default)
    {
        try
        {
            var opts       = GetOrCreateProjectOptions(filePath);
            var sourceText = SourceText.ofString(sourceCode);

            var (_, checkAnswer) = await RunAsync(
                _checker.ParseAndCheckFileInProject(
                    filePath, 0, sourceText, opts, None<string>()), ct);

            ct.ThrowIfCancellationRequested();

            if (checkAnswer is not FSharpCheckFileAnswer.Succeeded checkSucceeded2)
                return null;
            var checkResults2 = checkSucceeded2.Item;

            var lineText = GetLineText(sourceCode, line);
            var (startCol, endCol) = GetWordBounds(lineText, column - 1);
            if (startCol >= endCol) return null;

            var names = ListModule.OfSeq(
                new[] { lineText.Substring(startCol, endCol - startCol) });

            return await GetToolTipViaReflection(
                checkResults2, line, endCol, lineText, names, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FSharpQuickInfo] {ex.Message}");
            return null;
        }
    }

    // ── project options ────────────────────────────────────────────────────

    private FSharpProjectOptions GetOrCreateProjectOptions(string filePath)
    {
        if (_projectOptions is not null && _trackedFilePath == filePath)
            return _projectOptions;

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
        var refs = new[]
        {
            "System.Runtime.dll", "System.Console.dll", "System.Collections.dll",
            "System.Linq.dll", "System.Private.CoreLib.dll", "netstandard.dll",
        }
        .Select(n => Path.Combine(runtimeDir, n))
        .Where(File.Exists)
        .Select(p => $"-r:{p}")
        .Concat(new[] { "--noframework", "--optimize-", "--debug+" })
        .ToArray();

        // Build FSharpProjectOptions via reflection to handle field additions across versions
        _projectOptions = BuildProjectOptions(filePath, refs);
        _trackedFilePath = filePath;
        return _projectOptions;
    }

    // ── reflection-based calls for version-agnostic FCS API ────────────────

    private static FSharpChecker CreateCheckerViaReflection()
    {
        var method = typeof(FSharpChecker).GetMethod("Create",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("FSharpChecker.Create not found");
        var args = method.GetParameters()
            .Select(p => p.DefaultValue is DBNull or null
                ? GetNoneOrDefaultForType(p.ParameterType)
                : p.DefaultValue)
            .ToArray();
        return (FSharpChecker)method.Invoke(null, args)!;
    }

    private static FSharpProjectOptions BuildProjectOptions(string filePath, string[] refs)
    {
        try
        {
            var ctor = typeof(FSharpProjectOptions).GetConstructors().First();
            var ps   = ctor.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                args[i] = p.Name switch
                {
                    "projectFileName" or "ProjectFileName" => filePath + ".fsproj",
                    "projectId"       or "ProjectId"       => GetNoneForType(p.ParameterType),
                    "sourceFiles"     or "SourceFiles"     => new[] { filePath },
                    "otherOptions"    or "OtherOptions"    => refs,
                    "referencedProjects" or "ReferencedProjects" =>
                        Array.CreateInstance(p.ParameterType.GetElementType()!, 0),
                    "isIncompleteTypeCheckEnvironment" => true,
                    "useScriptResolutionRules"         => true,
                    "loadTime"                         => DateTime.UtcNow,
                    _                                  => GetNoneOrDefaultForType(p.ParameterType),
                };
            }
            return (FSharpProjectOptions)ctor.Invoke(args);
        }
        catch
        {
            // Last-resort: build via reflection with all-None fallback
            var ctor  = typeof(FSharpProjectOptions).GetConstructors().First();
            var parms = ctor.GetParameters();
            var fargs = new object?[parms.Length];
            for (int i = 0; i < parms.Length; i++)
                fargs[i] = GetNoneOrDefaultForType(parms[i].ParameterType);
            // override the fields we know
            for (int i = 0; i < parms.Length; i++)
            {
                fargs[i] = parms[i].Name switch
                {
                    "projectFileName" or "ProjectFileName" => filePath + ".fsproj",
                    "sourceFiles" or "SourceFiles"         => new[] { filePath },
                    "otherOptions" or "OtherOptions"       => refs,
                    "isIncompleteTypeCheckEnvironment"     => (object)true,
                    "useScriptResolutionRules"             => (object)true,
                    "loadTime" or "LoadTime"               => (object)DateTime.UtcNow,
                    "referencedProjects" or "ReferencedProjects" =>
                        Array.CreateInstance(parms[i].ParameterType.GetElementType()!, 0),
                    _ => fargs[i],
                };
            }
            return (FSharpProjectOptions)ctor.Invoke(fargs);
        }
    }

    private static async Task<IReadOnlyList<FSharpLspCompletionItem>> GetDeclarationsViaReflection(
        FSharpCheckFileResults checkResults,
        FSharpParseFileResults parseResults,
        int line,
        string lineText,
        PartialLongName partialLongName,
        CancellationToken ct)
    {
        try
        {
            // Try GetDeclarationListInfo first, then GetDeclarationListAsync
            var type = checkResults.GetType();
            MethodInfo? method =
                type.GetMethod("GetDeclarationListInfo") ??
                type.GetMethod("GetDeclarationListAsync");

            if (method is null) return Array.Empty<FSharpLspCompletionItem>();

            var ps   = method.GetParameters();
            var args = BuildDeclarationArgs(ps, parseResults, line, lineText, partialLongName);

            var returnVal = method.Invoke(checkResults, args);

            // Await if it's an async result
            DeclarationListInfo? decls = null;
            if (returnVal is FSharpAsync<DeclarationListInfo> asyncDecls)
                decls = await RunAsync(asyncDecls, ct);
            else if (returnVal is DeclarationListInfo di)
                decls = di;

            if (decls is null) return Array.Empty<FSharpLspCompletionItem>();

            return decls.Items.Select(item => new FSharpLspCompletionItem
            {
                Label      = item.NameInList,
                InsertText = item.NameInCode,
                Kind       = MapGlyph(item.Glyph),
                Detail     = item.FullName,
            }).ToList();
        }
        catch { return Array.Empty<FSharpLspCompletionItem>(); }
    }

    private static object?[] BuildDeclarationArgs(
        System.Reflection.ParameterInfo[] ps,
        FSharpParseFileResults parseResults,
        int line,
        string lineText,
        PartialLongName partialLongName)
    {
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            args[i] = p.Name switch
            {
                "parsedFileResultsOpt" or "parseResultsOpt" =>
                    MakeSomeOfType(p.ParameterType, parseResults),
                "line"              => (object)line,
                "lineText"          => (object)lineText,
                "partialLongName"   => (object)partialLongName,
                _                   => GetNoneOrDefaultForType(p.ParameterType),
            };
        }
        return args;
    }

    private static async Task<string?> GetToolTipViaReflection(
        FSharpCheckFileResults checkResults,
        int line,
        int colAtEnd,
        string lineText,
        FSharpList<string> names,
        CancellationToken ct)
    {
        try
        {
            var type   = checkResults.GetType();
            var method = type.GetMethod("GetToolTip") ??
                         type.GetMethod("GetStructuredToolTip") ??
                         type.GetMethod("GetToolTipText");

            if (method is null) return null;

            var ps   = method.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                args[i] = p.Name switch
                {
                    "line"                              => (object)line,
                    "colAtEndOfNames" or "colAtEnd"
                        or "column" or "col"            => (object)colAtEnd,
                    "lineText" or "lineStr"             => (object)lineText,
                    "names"                             => (object)names,
                    _                                   => GetNoneOrDefaultForType(p.ParameterType),
                };
            }

            var returnVal = method.Invoke(checkResults, args);

            // Unwrap async or sync result
            if (returnVal is null) return null;
            var retType = returnVal.GetType();
            if (retType.IsGenericType && retType.GetGenericTypeDefinition() == typeof(FSharpAsync<>))
            {
                // Await the FSharpAsync via StartAsTask
                var startAsTask = typeof(FSharpAsync).GetMethods()
                    .First(m => m.Name == "StartAsTask" && m.GetParameters().Length == 3)
                    .MakeGenericMethod(retType.GetGenericArguments()[0]);

                var task = (Task)startAsTask.Invoke(null, new object?[]
                {
                    returnVal,
                    FSharpOption<TaskCreationOptions>.None,
                    FSharpOption<CancellationToken>.Some(ct)
                })!;
                await task;
                var resultProp = task.GetType().GetProperty("Result");
                returnVal = resultProp?.GetValue(task);
            }

            return returnVal?.ToString();
        }
        catch { return null; }
    }

    // ── FSharpOption helpers via reflection ────────────────────────────────

    private static object? GetNoneForType(Type t)
    {
        if (!t.IsGenericType) return null;
        var inner = t.GetGenericArguments()[0];
        return typeof(FSharpOption<>).MakeGenericType(inner)
            .GetProperty("None")?.GetValue(null);
    }

    private static object? GetNoneOrDefaultForType(Type t)
    {
        if (t.IsGenericType &&
            t.GetGenericTypeDefinition() == typeof(FSharpOption<>))
            return GetNoneForType(t);
        if (t.IsValueType)
            return Activator.CreateInstance(t);
        if (t.IsArray)
            return Array.CreateInstance(t.GetElementType()!, 0);
        return null;
    }

    private static object? MakeSomeOfType(Type optionType, object value)
    {
        if (!optionType.IsGenericType) return null;
        var inner = optionType.GetGenericArguments()[0];
        return typeof(FSharpOption<>).MakeGenericType(inner)
            .GetMethod("Some")?.Invoke(null, new[] { value });
    }

    // ── shared helpers ─────────────────────────────────────────────────────

    private static Task<T> RunAsync<T>(FSharpAsync<T> computation, CancellationToken ct)
        => FSharpAsync.StartAsTask(computation,
               FSharpOption<TaskCreationOptions>.None,
               FSharpOption<CancellationToken>.Some(ct));

    private static string GetLineText(string source, int line)
    {
        var lines = source.Split('\n');
        var idx   = line - 1;
        if (idx < 0 || idx >= lines.Length) return string.Empty;
        return lines[idx].TrimEnd('\r');
    }

    private static (int start, int end) GetWordBounds(string line, int col)
    {
        if (col < 0 || col >= line.Length) return (col, col);
        int start = col, end = col;
        while (start > 0 && IsIdentChar(line[start - 1])) start--;
        while (end < line.Length && IsIdentChar(line[end])) end++;
        return (start, end);
    }

    private static bool IsIdentChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '\'';

    private static string MapGlyph(FSharpGlyph glyph)
    {
        if      (glyph.IsClass)           return "Class";
        else if (glyph.IsStruct)          return "Struct";
        else if (glyph.IsInterface)       return "Interface";
        else if (glyph.IsEnum)            return "Enum";
        else if (glyph.IsEnumMember)      return "EnumMember";
        else if (glyph.IsMethod)          return "Method";
        else if (glyph.IsOverridenMethod) return "Method";
        else if (glyph.IsExtensionMethod) return "Method";
        else if (glyph.IsProperty)        return "Property";
        else if (glyph.IsField)           return "Field";
        else if (glyph.IsEvent)           return "Event";
        else if (glyph.IsVariable)        return "Variable";
        else if (glyph.IsConstant)        return "Constant";
        else if (glyph.IsModule)          return "Module";
        else if (glyph.IsDelegate)        return "Function";
        else if (glyph.IsType)            return "Class";
        else if (glyph.IsTypedef)         return "Class";
        else if (glyph.IsException)       return "Class";
        else if (glyph.IsUnion)           return "Enum";
        else                              return "Text";
    }

    public void Dispose() { }
}

// ── DTO ───────────────────────────────────────────────────────────────────

/// <summary>A single F# completion item produced by <see cref="FSharpCompletionEngine"/>.</summary>
public sealed class FSharpLspCompletionItem
{
    public string  Label      { get; init; } = string.Empty;
    public string  InsertText { get; init; } = string.Empty;
    public string  Kind       { get; init; } = "Text";
    public string? Detail     { get; init; }
}


