using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Unified Roslyn workspace service — singleton facade wrapping a single
/// AdhocWorkspace and MefHostServices instance.
/// All Roslyn features (completion, diagnostics, hover, go-to-definition,
/// rename, quick-fix) share this workspace for consistency.
///
/// Architecture:
///   Editor Component
///         │
///         ▼
///   RoslynWorkspaceService (this)
///         │
///         ▼
///   Completion / Diagnostics / Symbol API
/// </summary>
public sealed class RoslynWorkspaceService : IDisposable
{
    // ── Singleton ──────────────────────────────────────────────────────────
    private static readonly Lazy<RoslynWorkspaceService> _instance =
        new(() => new RoslynWorkspaceService());

    public static RoslynWorkspaceService Instance => _instance.Value;

    // ── Internal state ─────────────────────────────────────────────────────
    private readonly AdhocWorkspace _workspace;
    private readonly MefHostServices _host;
    private readonly List<MetadataReference> _defaultRefs;
    private readonly object _syncLock = new();

    private ProjectId? _projectId;
    private DocumentId? _activeDocumentId;
    private string? _trackedFilePath;
    private string? _projectDir;
    private readonly Dictionary<string, DocumentId> _documentIds = new(StringComparer.OrdinalIgnoreCase);

    // ── Public properties ──────────────────────────────────────────────────
    public AdhocWorkspace Workspace => _workspace;
    public MefHostServices Host => _host;
    public IReadOnlyList<MetadataReference> DefaultReferences => _defaultRefs;

    // ── Events ─────────────────────────────────────────────────────────────
    public event EventHandler<WorkspaceDocumentChangedEventArgs>? DocumentChanged;

    // ── Constructor ────────────────────────────────────────────────────────
    private RoslynWorkspaceService()
    {
        _host = MefHostServices.Create(BuildMefAssemblies());
        _workspace = new AdhocWorkspace(_host);
        _defaultRefs = CollectDefaultReferences();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronizes a document in the workspace: creates or updates.
    /// Returns the up-to-date Document for Roslyn queries.
    /// </summary>
    public Document SyncDocument(string filePath, string sourceCode)
    {
        lock (_syncLock)
        {
            if (_trackedFilePath != filePath)
            {
                RebuildProject(filePath, sourceCode);
            }
            else
            {
                // Update only the active document text
                var doc = _workspace.CurrentSolution.GetDocument(_activeDocumentId!);
                if (doc is not null)
                {
                    var newDoc = doc.WithText(SourceText.From(sourceCode));
                    _workspace.TryApplyChanges(newDoc.Project.Solution);
                }
            }
            return _workspace.CurrentSolution.GetDocument(_activeDocumentId!)!;
        }
    }

    /// <summary>
    /// Sets the project directory for cross-file context resolution.
    /// All .cs files (excluding bin/obj) are loaded as additional documents.
    /// </summary>
    public void SetProjectContext(string? projectDir)
    {
        lock (_syncLock)
        {
            if (string.Equals(_projectDir, projectDir, StringComparison.OrdinalIgnoreCase))
                return;
            _projectDir = projectDir;
            _trackedFilePath = null; // force rebuild on next SyncDocument
        }
    }

    /// <summary>
    /// Adds a DLL reference (e.g. from project's bin folder).
    /// </summary>
    public void AddReference(string dllPath)
    {
        lock (_syncLock)
        {
            if (!File.Exists(dllPath)) return;
            if (_defaultRefs.Any(r => string.Equals(r.Display, dllPath, StringComparison.OrdinalIgnoreCase)))
                return;
            try { _defaultRefs.Add(MetadataReference.CreateFromFile(dllPath)); }
            catch { }
            _trackedFilePath = null; // force rebuild
        }
    }

    /// <summary>
    /// Gets the current ProjectId (or null if not yet initialized).
    /// </summary>
    public ProjectId? CurrentProjectId => _projectId;

    /// <summary>
    /// Gets the current active DocumentId.
    /// </summary>
    public DocumentId? ActiveDocumentId => _activeDocumentId;

    /// <summary>
    /// Gets the current solution from the workspace.
    /// </summary>
    public Solution CurrentSolution => _workspace.CurrentSolution;

    // ── Project rebuild ────────────────────────────────────────────────────

    private void RebuildProject(string filePath, string sourceCode)
    {
        // Remove old project
        if (_projectId is not null)
        {
            _workspace.TryApplyChanges(
                _workspace.CurrentSolution.RemoveProject(_projectId));
        }

        _documentIds.Clear();

        var build = RoslynProjectFactory.CreateBuild(_projectDir, _defaultRefs, filePath, sourceCode);
        var solution = _workspace.CurrentSolution.AddProject(build.ProjectInfo);

        _workspace.TryApplyChanges(solution);

        _projectId = build.ProjectInfo.Id;
        _activeDocumentId = build.ActiveDocumentId;
        _trackedFilePath = filePath;
        foreach (var pair in build.DocumentIds)
            _documentIds[pair.Key] = pair.Value;

        DocumentChanged?.Invoke(this, new WorkspaceDocumentChangedEventArgs(filePath));
    }


    // ── MEF assembly list ──────────────────────────────────────────────────
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

    // ── Default metadata references ────────────────────────────────────────
    public static List<MetadataReference> CollectDefaultReferences()
    {
        return RoslynCompletionEngine.CollectPublicDefaultReferences();
    }

    private static void TryAdd(List<MetadataReference> list, string path)
    {
        if (!File.Exists(path)) return;
        if (list.Any(r => string.Equals(r.Display, path, StringComparison.OrdinalIgnoreCase))) return;
        try { list.Add(MetadataReference.CreateFromFile(path)); } catch { }
    }

    // ── IDisposable ────────────────────────────────────────────────────────
    public void Dispose() => _workspace.Dispose();
}

/// <summary>Event args when the workspace document changes.</summary>
public sealed class WorkspaceDocumentChangedEventArgs : EventArgs
{
    public string FilePath { get; }
    public WorkspaceDocumentChangedEventArgs(string filePath) => FilePath = filePath;
}

