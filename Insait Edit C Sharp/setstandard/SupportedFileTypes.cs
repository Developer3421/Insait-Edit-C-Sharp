using System.Collections.Generic;
using System.Linq;

namespace Insait_Edit_C_Sharp.SetStandard;

/// <summary>
/// Describes a single file type that Insait Edit can open.
/// </summary>
public sealed record FileTypeInfo(
    string Extension,
    string Description,
    string ContentType,
    FileCategory Category);

/// <summary>
/// Logical categories for file type grouping in UI.
/// </summary>
public enum FileCategory
{
    CSharp,
    FSharp,
    VisualBasic,
    Avalonia,
    Web,
    Script,
    Config,
    Data,
    Markup,
    DotNet,
    NativeCode,
    Other
}

/// <summary>
/// Complete catalogue of every file extension supported by Insait Edit.
/// Used by <see cref="FileAssociationService"/> to register/unregister
/// Windows default-app associations.
/// </summary>
public static class SupportedFileTypes
{
    /// <summary>The ProgId prefix written into the registry (e.g. "InsaitEdit.cs").</summary>
    public const string ProgIdPrefix = "InsaitEdit";

    /// <summary>Friendly application name shown in "Open With" dialogs.</summary>
    public const string AppName = "Insait Edit";

    /// <summary>Application description for RegisteredApplications.</summary>
    public const string AppDescription = "Insait Edit — Code Editor";

    /// <summary>Registry capabilities key path (relative to HKCU\Software).</summary>
    public const string CapabilitiesKeyPath = @"InsaitEdit\Capabilities";

    /// <summary>
    /// Build the ProgId for a given extension, e.g. ".cs" → "InsaitEdit.cs"
    /// </summary>
    public static string GetProgId(string extension)
        => $"{ProgIdPrefix}{extension}";

    /// <summary>
    /// All file types supported by the editor.
    /// Mirrors <c>FileService._supportedExtensions</c>.
    /// </summary>
    public static IReadOnlyList<FileTypeInfo> All { get; } = BuildList();

    /// <summary>Return only the types from a given category.</summary>
    public static IEnumerable<FileTypeInfo> ByCategory(FileCategory cat)
        => All.Where(f => f.Category == cat);

    /// <summary>Return all distinct extensions as a flat array.</summary>
    public static string[] AllExtensions()
        => All.Select(f => f.Extension).ToArray();

    // ─────────────────────────────────────────────────────────────
    private static List<FileTypeInfo> BuildList() =>
    [
        // ── C# ──────────────────────────────────────────────────
        new(".cs",   "C# Source File",              "text/x-csharp",        FileCategory.CSharp),
        new(".csx",  "C# Script File",              "text/x-csharp",        FileCategory.CSharp),

        // ── Visual Basic ────────────────────────────────────────
        new(".vb",   "Visual Basic Source File",     "text/x-vb",            FileCategory.VisualBasic),

        // ── F# ──────────────────────────────────────────────────
        new(".fs",   "F# Source File",               "text/x-fsharp",        FileCategory.FSharp),
        new(".fsx",  "F# Script File",               "text/x-fsharp",        FileCategory.FSharp),
        new(".fsi",  "F# Signature File",            "text/x-fsharp",        FileCategory.FSharp),

        // ── Avalonia / XAML ─────────────────────────────────────
        new(".xaml",  "XAML File",                   "application/xaml+xml", FileCategory.Avalonia),
        new(".axaml", "Avalonia XAML File",           "application/xaml+xml", FileCategory.Avalonia),

        // ── Config / Data ───────────────────────────────────────
        new(".json",   "JSON File",                  "application/json",     FileCategory.Data),
        new(".xml",    "XML File",                   "application/xml",      FileCategory.Data),
        new(".config", ".NET Configuration File",    "application/xml",      FileCategory.Config),
        new(".yaml",   "YAML File",                  "application/x-yaml",   FileCategory.Config),
        new(".yml",    "YAML File",                  "application/x-yaml",   FileCategory.Config),
        new(".toml",   "TOML File",                  "application/toml",     FileCategory.Config),
        new(".ini",    "INI Configuration File",     "text/plain",           FileCategory.Config),
        new(".cfg",    "Configuration File",         "text/plain",           FileCategory.Config),
        new(".conf",   "Configuration File",         "text/plain",           FileCategory.Config),
        new(".env",    "Environment Variables File",  "text/plain",           FileCategory.Config),
        new(".csv",    "CSV File",                   "text/csv",             FileCategory.Data),
        new(".sql",    "SQL File",                   "application/sql",      FileCategory.Data),

        // ── Web ─────────────────────────────────────────────────
        new(".html",  "HTML File",                   "text/html",            FileCategory.Web),
        new(".htm",   "HTML File",                   "text/html",            FileCategory.Web),
        new(".css",   "CSS Stylesheet",              "text/css",             FileCategory.Web),
        new(".scss",  "SCSS Stylesheet",             "text/x-scss",          FileCategory.Web),
        new(".less",  "LESS Stylesheet",             "text/x-less",          FileCategory.Web),
        new(".js",    "JavaScript File",             "application/javascript",FileCategory.Web),
        new(".ts",    "TypeScript File",             "application/typescript",FileCategory.Web),
        new(".jsx",   "JSX File",                    "text/jsx",             FileCategory.Web),
        new(".tsx",   "TSX File",                    "text/tsx",             FileCategory.Web),

        // ── Markup / Text ───────────────────────────────────────
        new(".md",   "Markdown File",                "text/markdown",        FileCategory.Markup),
        new(".txt",  "Text File",                    "text/plain",           FileCategory.Markup),
        new(".log",  "Log File",                     "text/plain",           FileCategory.Markup),

        // ── .NET Solution / Project ─────────────────────────────
        new(".sln",    "Visual Studio Solution",          "text/plain",      FileCategory.DotNet),
        new(".slnx",   "Visual Studio Solution (XML)",    "text/plain",      FileCategory.DotNet),
        new(".csproj", "C# Project File",                 "application/xml", FileCategory.DotNet),
        new(".vbproj", "VB.NET Project File",             "application/xml", FileCategory.DotNet),
        new(".fsproj", "F# Project File",                 "application/xml", FileCategory.DotNet),
        new(".nfproj", "nanoFramework Project File",      "application/xml", FileCategory.DotNet),
        new(".props",  "MSBuild Properties File",         "application/xml", FileCategory.DotNet),
        new(".targets","MSBuild Targets File",            "application/xml", FileCategory.DotNet),
        new(".nuspec", "NuGet Specification File",        "application/xml", FileCategory.DotNet),

        // ── Razor ───────────────────────────────────────────────
        new(".razor",  "Razor Component File",            "text/x-cshtml",   FileCategory.Web),
        new(".cshtml", "Razor View File",                 "text/x-cshtml",   FileCategory.Web),

        // ── Scripts ─────────────────────────────────────────────
        new(".py",   "Python File",                  "text/x-python",        FileCategory.Script),
        new(".rb",   "Ruby File",                    "text/x-ruby",          FileCategory.Script),
        new(".go",   "Go File",                      "text/x-go",            FileCategory.Script),
        new(".rs",   "Rust File",                    "text/x-rust",          FileCategory.Script),
        new(".java", "Java File",                    "text/x-java",          FileCategory.Script),
        new(".kt",   "Kotlin File",                  "text/x-kotlin",        FileCategory.Script),
        new(".kts",  "Kotlin Script File",           "text/x-kotlin",        FileCategory.Script),
        new(".swift","Swift File",                   "text/x-swift",         FileCategory.Script),
        new(".php",  "PHP File",                     "text/x-php",           FileCategory.Script),

        // ── Native C/C++ ────────────────────────────────────────
        new(".cpp",  "C++ Source File",              "text/x-c++src",        FileCategory.NativeCode),
        new(".c",    "C Source File",                "text/x-csrc",          FileCategory.NativeCode),
        new(".h",    "C/C++ Header File",            "text/x-chdr",          FileCategory.NativeCode),
        new(".hpp",  "C++ Header File",              "text/x-c++hdr",        FileCategory.NativeCode),

        // ── Shell Scripts ───────────────────────────────────────
        new(".sh",   "Shell Script",                 "application/x-sh",     FileCategory.Script),
        new(".bat",  "Windows Batch File",           "application/x-bat",    FileCategory.Script),
        new(".cmd",  "Windows Command Script",       "application/x-bat",    FileCategory.Script),
        new(".ps1",  "PowerShell Script",            "text/x-powershell",    FileCategory.Script),

        // ── Other ───────────────────────────────────────────────
        new(".editorconfig", "EditorConfig File",    "text/plain",           FileCategory.Config),
        new(".gitignore",    ".gitignore File",      "text/plain",           FileCategory.Config),
        new(".gitattributes",".gitattributes File",  "text/plain",           FileCategory.Config),
        new(".dockerfile",   "Dockerfile",           "text/plain",           FileCategory.Config),
    ];
}

