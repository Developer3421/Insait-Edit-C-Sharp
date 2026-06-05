using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Analyzer;

public static class BuiltInAnalyzerProvider
{
    private static ImmutableArray<DiagnosticAnalyzer>? _cachedCSharp;
    private static readonly object _lock = new();

    public static ImmutableArray<DiagnosticAnalyzer> GetCSharpAnalyzers()
    {
        if (_cachedCSharp.HasValue)
            return _cachedCSharp.Value;

        lock (_lock)
        {
            if (_cachedCSharp.HasValue)
                return _cachedCSharp.Value;

            var analyzers = new List<DiagnosticAnalyzer>();
            var targetNames = new[]
            {
                "Microsoft.CodeAnalysis.CSharp.Features",
                "Microsoft.CodeAnalysis.Features",
                "Microsoft.CodeAnalysis.CSharp.Workspaces",
                "Microsoft.CodeAnalysis.Workspaces.Common",
            };

            foreach (var name in targetNames)
            {
                try { Assembly.Load(name); } catch { }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var asmName = asm.GetName().Name ?? string.Empty;
                    if (!asmName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        types = rtle.Types.OfType<Type>().ToArray();
                    }

                    foreach (var type in types)
                    {
                        try
                        {
                            if (type.IsAbstract || type.IsInterface) continue;
                            if (!typeof(DiagnosticAnalyzer).IsAssignableFrom(type)) continue;

                            var attr = type.GetCustomAttribute<DiagnosticAnalyzerAttribute>();
                            if (attr == null) continue;
                            if (!attr.Languages.Contains(LanguageNames.CSharp, StringComparer.Ordinal))
                                continue;

                            var instance = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
                            analyzers.Add(instance);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            _cachedCSharp = analyzers.ToImmutableArray();
            System.Diagnostics.Debug.WriteLine(
                $"[BuiltInAnalyzerProvider] Discovered {_cachedCSharp.Value.Length} C# analyzers.");

            return _cachedCSharp.Value;
        }
    }

    public static ImmutableArray<DiagnosticAnalyzer> Merge(
        IEnumerable<DiagnosticAnalyzer> projectAnalyzers)
    {
        var builtIn = GetCSharpAnalyzers();
        var result = new Dictionary<string, DiagnosticAnalyzer>(StringComparer.Ordinal);

        foreach (var a in builtIn)
            result[a.GetType().FullName ?? a.GetType().Name] = a;

        foreach (var a in projectAnalyzers)
            result[a.GetType().FullName ?? a.GetType().Name] = a;

        return result.Values.ToImmutableArray();
    }
}
