using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Tags;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for providing C# code completion using Roslyn
/// </summary>
public class CSharpCompletionService
{
    private readonly AdhocWorkspace _workspace;
    private readonly List<MetadataReference> _references;
    private Project? _currentProject;
    private Document? _currentDocument;
    private string? _currentFilePath;

    public CSharpCompletionService()
    {
        // Create MefHostServices for completion features
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        _workspace = new AdhocWorkspace(host);
        _references = GetDefaultReferences();
    }

    /// <summary>
    /// Get default assembly references for compilation
    /// </summary>
    private List<MetadataReference> GetDefaultReferences()
    {
        var references = new List<MetadataReference>();

        try
        {
            // Get the runtime directory
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null)
            {
                var coreAssemblies = new[]
                {
                    "System.Runtime.dll",
                    "System.Console.dll",
                    "System.Collections.dll",
                    "System.Collections.Generic.dll",
                    "System.Linq.dll",
                    "System.Linq.Expressions.dll",
                    "System.Threading.dll",
                    "System.Threading.Tasks.dll",
                    "System.IO.dll",
                    "System.IO.FileSystem.dll",
                    "System.Text.RegularExpressions.dll",
                    "System.Net.Http.dll",
                    "System.Net.Primitives.dll",
                    "netstandard.dll",
                    "System.Private.CoreLib.dll",
                    "System.ObjectModel.dll",
                    "System.ComponentModel.dll",
                    "System.ComponentModel.Primitives.dll",
                    "System.Runtime.Extensions.dll",
                    "System.Runtime.InteropServices.dll",
                    "System.Text.Encoding.dll",
                    "System.Memory.dll",
                    "System.Buffers.dll",
                    "Microsoft.CSharp.dll"
                };

                foreach (var assembly in coreAssemblies)
                {
                    var path = Path.Combine(runtimeDir, assembly);
                    if (File.Exists(path))
                    {
                        try
                        {
                            references.Add(MetadataReference.CreateFromFile(path));
                        }
                        catch { }
                    }
                }
            }

            // Add mscorlib or System.Runtime
            var mscorlibPath = typeof(object).Assembly.Location;
            if (File.Exists(mscorlibPath))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(mscorlibPath));
                }
                catch { }
            }

            // Add System.Core
            var systemCorePath = typeof(Enumerable).Assembly.Location;
            if (File.Exists(systemCorePath))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(systemCorePath));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading default references: {ex.Message}");
        }

        return references;
    }

    /// <summary>
    /// Load additional references from a project directory
    /// </summary>
    public void LoadProjectReferences(string projectPath)
    {
        try
        {
            string? binDir = null;

            if (File.Exists(projectPath) && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                binDir = Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "bin");
            }
            else if (Directory.Exists(projectPath))
            {
                binDir = Path.Combine(projectPath, "bin");
            }

            if (binDir != null && Directory.Exists(binDir))
            {
                var dllFiles = Directory.GetFiles(binDir, "*.dll", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("ref" + Path.DirectorySeparatorChar));

                foreach (var dllFile in dllFiles)
                {
                    try
                    {
                        if (!_references.Any(r => r.Display?.Contains(Path.GetFileName(dllFile)) == true))
                        {
                            _references.Add(MetadataReference.CreateFromFile(dllFile));
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading project references: {ex.Message}");
        }
    }

    /// <summary>
    /// Update or create the document for completion analysis
    /// </summary>
    private Document GetOrUpdateDocument(string filePath, string sourceCode)
    {
        // Check if we need to create a new project
        if (_currentProject == null || _currentFilePath != filePath)
        {
            // Remove old project if exists
            if (_currentProject != null)
            {
                _workspace.TryApplyChanges(_workspace.CurrentSolution.RemoveProject(_currentProject.Id));
            }

            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "TempProject",
                "TempProject",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithNullableContextOptions(NullableContextOptions.Enable),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                metadataReferences: _references);

            var solution = _workspace.CurrentSolution.AddProject(projectInfo);

            var documentInfo = DocumentInfo.Create(
                documentId,
                Path.GetFileName(filePath),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceCode), VersionStamp.Create())),
                filePath: filePath);

            solution = solution.AddDocument(documentInfo);
            _workspace.TryApplyChanges(solution);

            _currentProject = _workspace.CurrentSolution.GetProject(projectId);
            _currentDocument = _workspace.CurrentSolution.GetDocument(documentId);
            _currentFilePath = filePath;
        }
        else
        {
            // Update the document content
            if (_currentDocument != null)
            {
                var newDocument = _currentDocument.WithText(SourceText.From(sourceCode));
                _workspace.TryApplyChanges(newDocument.Project.Solution);
                _currentDocument = _workspace.CurrentSolution.GetDocument(_currentDocument.Id);
            }
        }

        return _currentDocument!;
    }

    /// <summary>
    /// Get completion items at the specified position
    /// </summary>
    public async Task<List<CompletionItemInfo>> GetCompletionsAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        var completionItems = new List<CompletionItemInfo>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            
            var completionService = CompletionService.GetService(document);
            if (completionService == null)
            {
                System.Diagnostics.Debug.WriteLine("CompletionService not available");
                return completionItems;
            }

            var completions = await completionService.GetCompletionsAsync(
                document,
                position,
                cancellationToken: cancellationToken);

            if (completions == null)
            {
                return completionItems;
            }

            foreach (var item in completions.ItemsList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var kind = GetCompletionKind(item);
                var description = await GetItemDescriptionAsync(document, completionService, item, cancellationToken);

                completionItems.Add(new CompletionItemInfo
                {
                    Label = item.DisplayText,
                    Kind = kind,
                    Detail = description,
                    InsertText = item.DisplayText,
                    SortText = item.SortText,
                    FilterText = item.FilterText
                });
            }

            // Sort: items starting with "private" get priority (appear first in list)
            completionItems.Sort((a, b) =>
            {
                bool aPrivate = a.Label.StartsWith("private", StringComparison.OrdinalIgnoreCase);
                bool bPrivate = b.Label.StartsWith("private", StringComparison.OrdinalIgnoreCase);
                if (aPrivate && !bPrivate) return -1;
                if (!aPrivate && bPrivate) return 1;
                return string.Compare(a.SortText ?? a.Label, b.SortText ?? b.Label, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting completions: {ex.Message}");
        }

        return completionItems;
    }

    /// <summary>
    /// Get completion items at line and column position
    /// </summary>
    public async Task<List<CompletionItemInfo>> GetCompletionsAtPositionAsync(
        string filePath,
        string sourceCode,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        // Convert line/column to absolute position
        var position = GetPositionFromLineColumn(sourceCode, line, column);
        return await GetCompletionsAsync(filePath, sourceCode, position, cancellationToken);
    }

    /// <summary>
    /// Convert line and column to absolute position in source code
    /// </summary>
    private int GetPositionFromLineColumn(string sourceCode, int line, int column)
    {
        var lines = sourceCode.Split('\n');
        var position = 0;

        // Line is 1-based, convert to 0-based
        var targetLine = Math.Max(0, line - 1);

        for (int i = 0; i < targetLine && i < lines.Length; i++)
        {
            position += lines[i].Length + 1; // +1 for newline
        }

        // Column is 1-based, convert to 0-based
        position += Math.Max(0, column - 1);

        return Math.Min(position, sourceCode.Length);
    }

    /// <summary>
    /// Get the completion kind for Monaco
    /// </summary>
    private string GetCompletionKind(CompletionItem item)
    {
        // Check tags first
        foreach (var tag in item.Tags)
        {
            switch (tag)
            {
                case "Class":
                case "Record":
                    return "Class";
                case "Struct":
                    return "Struct";
                case "Interface":
                    return "Interface";
                case "Enum":
                    return "Enum";
                case "EnumMember":
                    return "EnumMember";
                case "Delegate":
                    return "Function";
                case "Method":
                case "ExtensionMethod":
                    return "Method";
                case "Property":
                    return "Property";
                case "Field":
                    return "Field";
                case "Event":
                    return "Event";
                case "Local":
                case "Parameter":
                    return "Variable";
                case "Constant":
                    return "Constant";
                case "Namespace":
                    return "Module";
                case "Keyword":
                    return "Keyword";
                case "Snippet":
                    return "Snippet";
                case "TypeParameter":
                    return "TypeParameter";
            }
        }

        // Default based on display text patterns
        var text = item.DisplayText;
        if (text.StartsWith("using") || text.StartsWith("namespace"))
            return "Keyword";

        return "Text";
    }

    /// <summary>
    /// Get item description asynchronously
    /// </summary>
    private async Task<string> GetItemDescriptionAsync(
        Document document,
        CompletionService completionService,
        CompletionItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            var description = await completionService.GetDescriptionAsync(document, item, cancellationToken);
            if (description != null)
            {
                // Extract text from TaggedText
                return string.Join("", description.TaggedParts.Select(p => p.Text));
            }
        }
        catch { }

        return string.Empty;
    }

    /// <summary>
    /// Get signature help for method calls
    /// </summary>
    public async Task<SignatureHelpInfo?> GetSignatureHelpAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            
            if (semanticModel == null)
                return null;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return null;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var token = root.FindToken(position);
            
            // Find if we're in an argument list
            var argumentList = token.Parent?.AncestorsAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentListSyntax>()
                .FirstOrDefault();

            if (argumentList == null)
                return null;

            var invocation = argumentList.Parent as Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
            if (invocation == null)
                return null;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

            if (methodSymbol == null)
                return null;

            // Build signature help
            var signatures = new List<SignatureInfo>();
            
            // Get all overloads
            var containingType = methodSymbol.ContainingType;
            var methodName = methodSymbol.Name;
            
            var allOverloads = containingType.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .ToList();

            foreach (var overload in allOverloads)
            {
                var parameters = overload.Parameters.Select(p => new ParameterInfo
                {
                    Label = $"{p.Type.ToDisplayString()} {p.Name}",
                    Documentation = p.GetDocumentationCommentXml() ?? ""
                }).ToList();

                signatures.Add(new SignatureInfo
                {
                    Label = $"{overload.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
                    Documentation = overload.GetDocumentationCommentXml() ?? "",
                    Parameters = parameters
                });
            }

            // Determine active parameter index
            var activeParameter = 0;
            var separators = argumentList.Arguments.GetSeparators().ToList();
            for (int i = 0; i < separators.Count; i++)
            {
                if (position > separators[i].SpanStart)
                    activeParameter = i + 1;
            }

            return new SignatureHelpInfo
            {
                Signatures = signatures,
                ActiveSignature = 0,
                ActiveParameter = activeParameter
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting signature help: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get hover information for symbol at position
    /// </summary>
    public async Task<HoverInfo?> GetHoverInfoAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return null;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return null;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var token = root.FindToken(position);
            var node = token.Parent;

            if (node == null)
                return null;

            var symbolInfo = semanticModel.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

            if (symbol == null)
            {
                // Try type info for expressions
                var typeInfo = semanticModel.GetTypeInfo(node);
                if (typeInfo.Type != null)
                {
                    return new HoverInfo
                    {
                        Contents = $"(type) {typeInfo.Type.ToDisplayString()}",
                        Range = new TextRange
                        {
                            StartLine = syntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1,
                            StartColumn = syntaxTree.GetLineSpan(node.Span).StartLinePosition.Character + 1,
                            EndLine = syntaxTree.GetLineSpan(node.Span).EndLinePosition.Line + 1,
                            EndColumn = syntaxTree.GetLineSpan(node.Span).EndLinePosition.Character + 1
                        }
                    };
                }
                return null;
            }

            // Build hover content
            var displayString = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var kindString = symbol.Kind.ToString().ToLower();
            var documentation = symbol.GetDocumentationCommentXml();

            var content = $"({kindString}) {displayString}";
            if (!string.IsNullOrEmpty(documentation))
            {
                // Extract summary from XML documentation
                var summaryMatch = System.Text.RegularExpressions.Regex.Match(
                    documentation, 
                    @"<summary>\s*(.*?)\s*</summary>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                
                if (summaryMatch.Success)
                {
                    content += $"\n\n{summaryMatch.Groups[1].Value.Trim()}";
                }
            }

            return new HoverInfo
            {
                Contents = content,
                Range = new TextRange
                {
                    StartLine = syntaxTree.GetLineSpan(token.Span).StartLinePosition.Line + 1,
                    StartColumn = syntaxTree.GetLineSpan(token.Span).StartLinePosition.Character + 1,
                    EndLine = syntaxTree.GetLineSpan(token.Span).EndLinePosition.Line + 1,
                    EndColumn = syntaxTree.GetLineSpan(token.Span).EndLinePosition.Character + 1
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting hover info: {ex.Message}");
        }

        return null;
    }

    #region Go To Definition

    /// <summary>
    /// Get definition location for symbol at position
    /// </summary>
    public async Task<DefinitionResult?> GetDefinitionAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return null;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return null;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var token = root.FindToken(position);
            var node = token.Parent;

            if (node == null)
                return null;

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node, cancellationToken);

            if (symbol == null)
                return null;

            var locations = new List<LocationInfo>();

            foreach (var location in symbol.Locations)
            {
                if (location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    locations.Add(new LocationInfo
                    {
                        FilePath = lineSpan.Path,
                        StartLine = lineSpan.StartLinePosition.Line + 1,
                        StartColumn = lineSpan.StartLinePosition.Character + 1,
                        EndLine = lineSpan.EndLinePosition.Line + 1,
                        EndColumn = lineSpan.EndLinePosition.Character + 1
                    });
                }
                else if (location.IsInMetadata && symbol is INamedTypeSymbol namedType)
                {
                    // For metadata symbols, provide basic info
                    locations.Add(new LocationInfo
                    {
                        FilePath = $"metadata://{namedType.ContainingAssembly?.Name ?? "unknown"}/{symbol.Name}",
                        StartLine = 1,
                        StartColumn = 1,
                        EndLine = 1,
                        EndColumn = 1,
                        IsMetadata = true,
                        MetadataDisplayName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    });
                }
            }

            return new DefinitionResult
            {
                Symbol = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Kind = symbol.Kind.ToString(),
                Locations = locations
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting definition: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get definition at line and column position
    /// </summary>
    public async Task<DefinitionResult?> GetDefinitionAtPositionAsync(
        string filePath,
        string sourceCode,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        var position = GetPositionFromLineColumn(sourceCode, line, column);
        return await GetDefinitionAsync(filePath, sourceCode, position, cancellationToken);
    }

    #endregion

    #region Find References

    /// <summary>
    /// Find all references to symbol at position
    /// </summary>
    public async Task<List<ReferenceInfo>> FindReferencesAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        var references = new List<ReferenceInfo>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return references;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return references;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var token = root.FindToken(position);
            var node = token.Parent;

            if (node == null)
                return references;

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node, cancellationToken);

            if (symbol == null)
                return references;

            // Find all references in the solution
            var solution = document.Project.Solution;
            var referencedSymbols = await SymbolFinder.FindReferencesAsync(
                symbol, solution, cancellationToken);

            foreach (var referencedSymbol in referencedSymbols)
            {
                foreach (var location in referencedSymbol.Locations)
                {
                    var lineSpan = location.Location.GetLineSpan();
                    references.Add(new ReferenceInfo
                    {
                        FilePath = lineSpan.Path,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        EndLine = lineSpan.EndLinePosition.Line + 1,
                        EndColumn = lineSpan.EndLinePosition.Character + 1,
                        IsDefinition = referencedSymbol.Definition.Locations
                            .Any(dl => dl.SourceSpan == location.Location.SourceSpan),
                        Symbol = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error finding references: {ex.Message}");
        }

        return references;
    }

    /// <summary>
    /// Find references at line and column position
    /// </summary>
    public async Task<List<ReferenceInfo>> FindReferencesAtPositionAsync(
        string filePath,
        string sourceCode,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        var position = GetPositionFromLineColumn(sourceCode, line, column);
        return await FindReferencesAsync(filePath, sourceCode, position, cancellationToken);
    }

    #endregion

    #region QuickInfo (Enhanced Hover)

    /// <summary>
    /// Get enhanced quick info using QuickInfoService
    /// </summary>
    public async Task<QuickInfoResult?> GetQuickInfoAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            
            var quickInfoService = QuickInfoService.GetService(document);
            if (quickInfoService != null)
            {
                var quickInfo = await quickInfoService.GetQuickInfoAsync(document, position, cancellationToken);
                if (quickInfo != null)
                {
                    var sections = new List<QuickInfoSection>();
                    
                    foreach (var section in quickInfo.Sections)
                    {
                        var text = string.Join("", section.TaggedParts.Select(p => p.Text));
                        sections.Add(new QuickInfoSection
                        {
                            Kind = section.Kind,
                            Text = text
                        });
                    }

                    return new QuickInfoResult
                    {
                        Sections = sections,
                        Span = new TextRange
                        {
                            StartLine = 0, // Will be set from quickInfo.Span if available
                            StartColumn = 0,
                            EndLine = 0,
                            EndColumn = 0
                        }
                    };
                }
            }
            
            // Fallback to basic hover info
            var hoverInfo = await GetHoverInfoAsync(filePath, sourceCode, position, cancellationToken);
            if (hoverInfo != null)
            {
                return new QuickInfoResult
                {
                    Sections = new List<QuickInfoSection>
                    {
                        new QuickInfoSection { Kind = "Description", Text = hoverInfo.Contents }
                    },
                    Span = hoverInfo.Range
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting quick info: {ex.Message}");
        }

        return null;
    }

    #endregion

    #region Code Actions (CodeFixes + Refactorings)

    /// <summary>
    /// Get available code actions (fixes and refactorings) at position
    /// </summary>
    public async Task<List<CodeActionInfo>> GetCodeActionsAsync(
        string filePath,
        string sourceCode,
        int startPosition,
        int endPosition,
        CancellationToken cancellationToken = default)
    {
        var codeActions = new List<CodeActionInfo>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var text = await document.GetTextAsync(cancellationToken);
            var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                Math.Max(0, startPosition), 
                Math.Min(text.Length, endPosition));

            // Get CodeFix providers
            var diagnostics = await GetDiagnosticsInSpanAsync(document, span, cancellationToken);
            
            foreach (var diagnostic in diagnostics)
            {
                var codeFixProviders = GetCodeFixProviders();
                foreach (var provider in codeFixProviders)
                {
                    if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                        continue;

                    var context = new CodeFixContext(
                        document,
                        diagnostic,
                        (action, _) => codeActions.Add(new CodeActionInfo
                        {
                            Title = action.Title,
                            Kind = "quickfix",
                            DiagnosticCode = diagnostic.Id,
                            IsPreferred = false
                        }),
                        cancellationToken);

                    try
                    {
                        await provider.RegisterCodeFixesAsync(context);
                    }
                    catch { }
                }
            }

            // Get CodeRefactoring providers
            var refactoringProviders = GetCodeRefactoringProviders();
            foreach (var provider in refactoringProviders)
            {
                var context = new CodeRefactoringContext(
                    document,
                    span,
                    action => codeActions.Add(new CodeActionInfo
                    {
                        Title = action.Title,
                        Kind = "refactor",
                        IsPreferred = false
                    }),
                    cancellationToken);

                try
                {
                    await provider.ComputeRefactoringsAsync(context);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting code actions: {ex.Message}");
        }

        return codeActions;
    }

    private async Task<ImmutableArray<Diagnostic>> GetDiagnosticsInSpanAsync(
        Document document, 
        Microsoft.CodeAnalysis.Text.TextSpan span,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
            return ImmutableArray<Diagnostic>.Empty;

        return semanticModel.GetDiagnostics(span, cancellationToken);
    }

    private IEnumerable<CodeFixProvider> GetCodeFixProviders()
    {
        // Get built-in code fix providers from Roslyn
        var assemblies = MefHostServices.DefaultAssemblies;
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (typeof(CodeFixProvider).IsAssignableFrom(type) && 
                    !type.IsAbstract && 
                    type.GetConstructor(Type.EmptyTypes) != null)
                {
                    CodeFixProvider? provider = null;
                    try
                    {
                        provider = Activator.CreateInstance(type) as CodeFixProvider;
                    }
                    catch { }

                    if (provider != null)
                        yield return provider;
                }
            }
        }
    }

    private IEnumerable<CodeRefactoringProvider> GetCodeRefactoringProviders()
    {
        var assemblies = MefHostServices.DefaultAssemblies;
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (typeof(CodeRefactoringProvider).IsAssignableFrom(type) && 
                    !type.IsAbstract && 
                    type.GetConstructor(Type.EmptyTypes) != null)
                {
                    CodeRefactoringProvider? provider = null;
                    try
                    {
                        provider = Activator.CreateInstance(type) as CodeRefactoringProvider;
                    }
                    catch { }

                    if (provider != null)
                        yield return provider;
                }
            }
        }
    }

    #endregion

    #region Rename Symbol

    /// <summary>
    /// Rename symbol at position
    /// </summary>
    public async Task<RenameResult?> RenameSymbolAsync(
        string filePath,
        string sourceCode,
        int position,
        string newName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return null;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return null;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var token = root.FindToken(position);
            var node = token.Parent;

            if (node == null)
                return null;

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node, cancellationToken);

            if (symbol == null)
                return null;

            // Perform rename
            var solution = document.Project.Solution;
            var newSolution = await Renamer.RenameSymbolAsync(
                solution, 
                symbol, 
                new SymbolRenameOptions(), 
                newName, 
                cancellationToken);

            var changes = new List<TextChangeInfo>();
            
            foreach (var projectId in newSolution.ProjectIds)
            {
                var project = newSolution.GetProject(projectId);
                var oldProject = solution.GetProject(projectId);
                
                if (project == null || oldProject == null)
                    continue;

                foreach (var documentId in project.DocumentIds)
                {
                    var newDoc = project.GetDocument(documentId);
                    var oldDoc = oldProject.GetDocument(documentId);
                    
                    if (newDoc == null || oldDoc == null)
                        continue;

                    var newText = await newDoc.GetTextAsync(cancellationToken);
                    var oldText = await oldDoc.GetTextAsync(cancellationToken);

                    if (newText.ToString() != oldText.ToString())
                    {
                        var textChanges = newText.GetTextChanges(oldText);
                        foreach (var change in textChanges)
                        {
                            changes.Add(new TextChangeInfo
                            {
                                FilePath = newDoc.FilePath ?? newDoc.Name,
                                StartPosition = change.Span.Start,
                                EndPosition = change.Span.End,
                                NewText = change.NewText ?? ""
                            });
                        }
                    }
                }
            }

            return new RenameResult
            {
                OldName = symbol.Name,
                NewName = newName,
                Changes = changes
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error renaming symbol: {ex.Message}");
        }

        return null;
    }

    #endregion

    #region Document Highlights

    /// <summary>
    /// Get document highlights for symbol at position (same symbol occurrences)
    /// </summary>
    public async Task<List<DocumentHighlight>> GetDocumentHighlightsAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        var highlights = new List<DocumentHighlight>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return highlights;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return highlights;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var token = root.FindToken(position);
            var node = token.Parent;

            if (node == null)
                return highlights;

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node, cancellationToken);

            if (symbol == null)
                return highlights;

            // Find all references in document
            var solution = document.Project.Solution;
            var referencedSymbols = await SymbolFinder.FindReferencesAsync(
                symbol, solution, ImmutableHashSet.Create(document), cancellationToken);

            foreach (var referencedSymbol in referencedSymbols)
            {
                // Add definition location
                foreach (var location in referencedSymbol.Definition.Locations)
                {
                    if (location.IsInSource && location.SourceTree == syntaxTree)
                    {
                        var lineSpan = location.GetLineSpan();
                        highlights.Add(new DocumentHighlight
                        {
                            Kind = DocumentHighlightKind.Write,
                            Range = new TextRange
                            {
                                StartLine = lineSpan.StartLinePosition.Line + 1,
                                StartColumn = lineSpan.StartLinePosition.Character + 1,
                                EndLine = lineSpan.EndLinePosition.Line + 1,
                                EndColumn = lineSpan.EndLinePosition.Character + 1
                            }
                        });
                    }
                }

                // Add reference locations
                foreach (var refLocation in referencedSymbol.Locations)
                {
                    if (refLocation.Document.Id == document.Id)
                    {
                        var lineSpan = refLocation.Location.GetLineSpan();
                        highlights.Add(new DocumentHighlight
                        {
                            Kind = DocumentHighlightKind.Read,
                            Range = new TextRange
                            {
                                StartLine = lineSpan.StartLinePosition.Line + 1,
                                StartColumn = lineSpan.StartLinePosition.Character + 1,
                                EndLine = lineSpan.EndLinePosition.Line + 1,
                                EndColumn = lineSpan.EndLinePosition.Character + 1
                            }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting document highlights: {ex.Message}");
        }

        return highlights;
    }

    #endregion

    #region Semantic Tokens

    /// <summary>
    /// Get semantic tokens for syntax highlighting
    /// </summary>
    public async Task<List<SemanticToken>> GetSemanticTokensAsync(
        string filePath,
        string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var tokens = new List<SemanticToken>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return tokens;

            var text = await document.GetTextAsync(cancellationToken);
            
            // Use Classification service
            var classifiedSpans = await Classifier.GetClassifiedSpansAsync(
                document, 
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0, text.Length), 
                cancellationToken);

            foreach (var span in classifiedSpans)
            {
                var lineSpan = text.Lines.GetLinePositionSpan(span.TextSpan);
                
                tokens.Add(new SemanticToken
                {
                    Line = lineSpan.Start.Line,
                    StartChar = lineSpan.Start.Character,
                    Length = span.TextSpan.Length,
                    TokenType = MapClassificationToTokenType(span.ClassificationType),
                    TokenModifiers = GetTokenModifiers(span.ClassificationType)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting semantic tokens: {ex.Message}");
        }

        return tokens;
    }

    private string MapClassificationToTokenType(string classificationType)
    {
        return classificationType switch
        {
            ClassificationTypeNames.ClassName => "class",
            ClassificationTypeNames.RecordClassName => "class",
            ClassificationTypeNames.StructName => "struct",
            ClassificationTypeNames.InterfaceName => "interface",
            ClassificationTypeNames.EnumName => "enum",
            ClassificationTypeNames.EnumMemberName => "enumMember",
            ClassificationTypeNames.DelegateName => "type",
            ClassificationTypeNames.TypeParameterName => "typeParameter",
            ClassificationTypeNames.MethodName => "method",
            ClassificationTypeNames.ExtensionMethodName => "method",
            ClassificationTypeNames.PropertyName => "property",
            ClassificationTypeNames.EventName => "event",
            ClassificationTypeNames.FieldName => "variable",
            ClassificationTypeNames.ConstantName => "variable",
            ClassificationTypeNames.LocalName => "variable",
            ClassificationTypeNames.ParameterName => "parameter",
            ClassificationTypeNames.NamespaceName => "namespace",
            ClassificationTypeNames.Keyword => "keyword",
            ClassificationTypeNames.ControlKeyword => "keyword",
            ClassificationTypeNames.NumericLiteral => "number",
            ClassificationTypeNames.StringLiteral => "string",
            ClassificationTypeNames.VerbatimStringLiteral => "string",
            ClassificationTypeNames.Comment => "comment",
            ClassificationTypeNames.XmlDocCommentText => "comment",
            ClassificationTypeNames.Operator => "operator",
            ClassificationTypeNames.Punctuation => "punctuation",
            ClassificationTypeNames.PreprocessorKeyword => "macro",
            ClassificationTypeNames.PreprocessorText => "macro",
            ClassificationTypeNames.StaticSymbol => "variable",
            _ => "text"
        };
    }

    private List<string> GetTokenModifiers(string classificationType)
    {
        var modifiers = new List<string>();

        if (classificationType == ClassificationTypeNames.StaticSymbol)
            modifiers.Add("static");
        if (classificationType == ClassificationTypeNames.ConstantName)
            modifiers.Add("readonly");
        if (classificationType.Contains("Deprecated"))
            modifiers.Add("deprecated");

        return modifiers;
    }

    #endregion

    #region Format Code

    /// <summary>
    /// Format document or selection
    /// </summary>
    public async Task<string?> FormatCodeAsync(
        string filePath,
        string sourceCode,
        int? startPosition = null,
        int? endPosition = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            
            Document formattedDocument;
            if (startPosition.HasValue && endPosition.HasValue)
            {
                var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    startPosition.Value, endPosition.Value);
                formattedDocument = await Formatter.FormatAsync(
                    document, span, cancellationToken: cancellationToken);
            }
            else
            {
                formattedDocument = await Formatter.FormatAsync(
                    document, cancellationToken: cancellationToken);
            }

            var text = await formattedDocument.GetTextAsync(cancellationToken);
            return text.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error formatting code: {ex.Message}");
        }

        return null;
    }

    #endregion

    #region Get Symbols in Document

    /// <summary>
    /// Get all symbols (outline) in document
    /// </summary>
    public async Task<List<DocumentSymbol>> GetDocumentSymbolsAsync(
        string filePath,
        string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var symbols = new List<DocumentSymbol>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return symbols;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (syntaxTree == null)
                return symbols;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            
            // Find all type declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (symbol == null) continue;

                var typeSymbol = new DocumentSymbol
                {
                    Name = symbol.Name,
                    Kind = GetSymbolKind(symbol),
                    Range = GetTextRange(syntaxTree, typeDecl.Span),
                    SelectionRange = GetTextRange(syntaxTree, typeDecl.Identifier.Span),
                    Children = new List<DocumentSymbol>()
                };

                // Add members
                foreach (var member in typeDecl.Members)
                {
                    var memberSymbol = semanticModel.GetDeclaredSymbol(member, cancellationToken);
                    if (memberSymbol == null) continue;

                    var memberDocSymbol = new DocumentSymbol
                    {
                        Name = memberSymbol.Name,
                        Kind = GetSymbolKind(memberSymbol),
                        Range = GetTextRange(syntaxTree, member.Span),
                        Detail = GetSymbolDetail(memberSymbol)
                    };

                    if (member is BaseMethodDeclarationSyntax methodDecl)
                    {
                        memberDocSymbol.SelectionRange = GetTextRange(syntaxTree, 
                            methodDecl is MethodDeclarationSyntax m ? m.Identifier.Span : member.Span);
                    }

                    typeSymbol.Children.Add(memberDocSymbol);
                }

                symbols.Add(typeSymbol);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting document symbols: {ex.Message}");
        }

        return symbols;
    }

    private string GetSymbolKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.NamedType => symbol is INamedTypeSymbol nts ? nts.TypeKind switch
            {
                TypeKind.Class => "Class",
                TypeKind.Interface => "Interface",
                TypeKind.Struct => "Struct",
                TypeKind.Enum => "Enum",
                TypeKind.Delegate => "Function",
                _ => "Class"
            } : "Class",
            SymbolKind.Method => "Method",
            SymbolKind.Property => "Property",
            SymbolKind.Field => "Field",
            SymbolKind.Event => "Event",
            SymbolKind.Namespace => "Namespace",
            SymbolKind.Parameter => "Variable",
            SymbolKind.Local => "Variable",
            _ => "Variable"
        };
    }

    private string GetSymbolDetail(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IPropertySymbol prop => prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IFieldSymbol field => field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            _ => ""
        };
    }

    private TextRange GetTextRange(SyntaxTree syntaxTree, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var lineSpan = syntaxTree.GetLineSpan(span);
        return new TextRange
        {
            StartLine = lineSpan.StartLinePosition.Line + 1,
            StartColumn = lineSpan.StartLinePosition.Character + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            EndColumn = lineSpan.EndLinePosition.Character + 1
        };
    }

    #endregion

    #region Recommendations (What to type next)

    /// <summary>
    /// Get recommended symbols at position
    /// </summary>
    public async Task<List<RecommendedSymbol>> GetRecommendationsAsync(
        string filePath,
        string sourceCode,
        int position,
        CancellationToken cancellationToken = default)
    {
        var recommendations = new List<RecommendedSymbol>();

        try
        {
            var document = GetOrUpdateDocument(filePath, sourceCode);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel == null)
                return recommendations;

            var recommendedSymbols = await Recommender.GetRecommendedSymbolsAtPositionAsync(
                semanticModel, position, _workspace, cancellationToken: cancellationToken);

            foreach (var symbol in recommendedSymbols)
            {
                recommendations.Add(new RecommendedSymbol
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind.ToString(),
                    DisplayString = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ContainingType = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting recommendations: {ex.Message}");
        }

        return recommendations;
    }

    #endregion

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}

#region Data Transfer Objects

/// <summary>
/// Completion item information for the editor
/// </summary>
public class CompletionItemInfo
{
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "Text";
    public string? Detail { get; set; }
    public string? InsertText { get; set; }
    public string? SortText { get; set; }
    public string? FilterText { get; set; }
}

/// <summary>
/// Signature help information
/// </summary>
public class SignatureHelpInfo
{
    public List<SignatureInfo> Signatures { get; set; } = new();
    public int ActiveSignature { get; set; }
    public int ActiveParameter { get; set; }
}

/// <summary>
/// Single signature information
/// </summary>
public class SignatureInfo
{
    public string Label { get; set; } = string.Empty;
    public string? Documentation { get; set; }
    public List<ParameterInfo> Parameters { get; set; } = new();
}

/// <summary>
/// Parameter information
/// </summary>
public class ParameterInfo
{
    public string Label { get; set; } = string.Empty;
    public string? Documentation { get; set; }
}

/// <summary>
/// Hover information
/// </summary>
public class HoverInfo
{
    public string Contents { get; set; } = string.Empty;
    public TextRange? Range { get; set; }
}

/// <summary>
/// Text range information
/// </summary>
public class TextRange
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

/// <summary>
/// Definition result with multiple locations
/// </summary>
public class DefinitionResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public List<LocationInfo> Locations { get; set; } = new();
}

/// <summary>
/// Location information
/// </summary>
public class LocationInfo
{
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public bool IsMetadata { get; set; }
    public string? MetadataDisplayName { get; set; }
}

/// <summary>
/// Reference information
/// </summary>
public class ReferenceInfo
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public bool IsDefinition { get; set; }
    public string Symbol { get; set; } = string.Empty;
}

/// <summary>
/// Quick info result with sections
/// </summary>
public class QuickInfoResult
{
    public List<QuickInfoSection> Sections { get; set; } = new();
    public TextRange? Span { get; set; }
}

/// <summary>
/// Quick info section
/// </summary>
public class QuickInfoSection
{
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Code action information
/// </summary>
public class CodeActionInfo
{
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? DiagnosticCode { get; set; }
    public bool IsPreferred { get; set; }
}

/// <summary>
/// Rename result with text changes
/// </summary>
public class RenameResult
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public List<TextChangeInfo> Changes { get; set; } = new();
}

/// <summary>
/// Text change information
/// </summary>
public class TextChangeInfo
{
    public string FilePath { get; set; } = string.Empty;
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public string NewText { get; set; } = string.Empty;
}

/// <summary>
/// Document highlight
/// </summary>
public class DocumentHighlight
{
    public DocumentHighlightKind Kind { get; set; }
    public TextRange Range { get; set; } = new();
}

/// <summary>
/// Document highlight kind
/// </summary>
public enum DocumentHighlightKind
{
    Text = 1,
    Read = 2,
    Write = 3
}

/// <summary>
/// Semantic token for syntax highlighting
/// </summary>
public class SemanticToken
{
    public int Line { get; set; }
    public int StartChar { get; set; }
    public int Length { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public List<string> TokenModifiers { get; set; } = new();
}

/// <summary>
/// Document symbol (for outline)
/// </summary>
public class DocumentSymbol
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public TextRange Range { get; set; } = new();
    public TextRange? SelectionRange { get; set; }
    public List<DocumentSymbol>? Children { get; set; }
}

/// <summary>
/// Recommended symbol
/// </summary>
public class RecommendedSymbol
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string DisplayString { get; set; } = string.Empty;
    public string? ContainingType { get; set; }
}

#endregion

