using System;

namespace Insait_Edit_C_Sharp.Models;

/// <summary>
/// Represents an open editor tab
/// </summary>
public class EditorTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = "plaintext";
    public bool IsDirty { get; set; }
    public bool IsActive { get; set; }
    public int CursorLine { get; set; } = 1;
    public int CursorColumn { get; set; } = 1;
    public DateTime LastModified { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Gets the language identifier based on file extension
    /// </summary>
    public static string GetLanguageFromExtension(string filePath)
    {
        var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "typescriptreact",
            ".jsx" => "javascriptreact",
            ".json" => "json",
            ".xml" => "xml",
            ".axaml" => "xml",
            ".xaml" => "xml",
            ".html" => "html",
            ".htm" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".less" => "less",
            ".md" => "markdown",
            ".yaml" => "yaml",
            ".yml" => "yaml",
            ".sql" => "sql",
            ".py" => "python",
            ".rb" => "ruby",
            ".go" => "go",
            ".rs" => "rust",
            ".cpp" => "cpp",
            ".c" => "c",
            ".h" => "c",
            ".hpp" => "cpp",
            ".java" => "java",
            ".php" => "php",
            ".sh" => "shell",
            ".ps1" => "powershell",
            ".bat" => "bat",
            ".cmd" => "bat",
            _ => "plaintext"
        };
    }
}

