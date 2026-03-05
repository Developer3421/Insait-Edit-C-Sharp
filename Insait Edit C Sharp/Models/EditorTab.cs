using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Insait_Edit_C_Sharp.Models;

/// <summary>
/// Represents an open editor tab
/// </summary>
public class EditorTab : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _fileName = string.Empty;
    private string _filePath = string.Empty;
    private string _content = string.Empty;
    private string _language = "plaintext";
    private bool _isDirty;
    private bool _isActive;
    private int _cursorLine = 1;
    private int _cursorColumn = 1;
    private DateTime _lastModified = DateTime.Now;
    private bool _hasErrors;
    private bool _hasWarnings;
    private int _errorCount;
    private int _warningCount;

    public string Id 
    { 
        get => _id; 
        set => SetProperty(ref _id, value); 
    }
    
    public string FileName 
    { 
        get => _fileName; 
        set => SetProperty(ref _fileName, value); 
    }
    
    public string FilePath 
    { 
        get => _filePath; 
        set => SetProperty(ref _filePath, value); 
    }
    
    public string Content 
    { 
        get => _content; 
        set => SetProperty(ref _content, value); 
    }
    
    public string Language 
    { 
        get => _language; 
        set => SetProperty(ref _language, value); 
    }
    
    public bool IsDirty 
    { 
        get => _isDirty; 
        set 
        { 
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(DisplayFileName));
            }
        } 
    }
    
    public bool IsActive 
    { 
        get => _isActive; 
        set => SetProperty(ref _isActive, value); 
    }
    
    public int CursorLine 
    { 
        get => _cursorLine; 
        set => SetProperty(ref _cursorLine, value); 
    }
    
    public int CursorColumn 
    { 
        get => _cursorColumn; 
        set => SetProperty(ref _cursorColumn, value); 
    }
    
    public DateTime LastModified 
    { 
        get => _lastModified; 
        set => SetProperty(ref _lastModified, value); 
    }

    /// <summary>
    /// Gets the display name for the tab (with * if dirty)
    /// </summary>
    public string DisplayFileName => IsDirty ? $"● {FileName}" : FileName;
    
    /// <summary>Whether this file has errors in the Problems list.</summary>
    public bool HasErrors
    {
        get => _hasErrors;
        set
        {
            if (SetProperty(ref _hasErrors, value))
                OnPropertyChanged(nameof(DiagnosticIndicator));
        }
    }
    
    /// <summary>Whether this file has warnings in the Problems list.</summary>
    public bool HasWarnings
    {
        get => _hasWarnings;
        set
        {
            if (SetProperty(ref _hasWarnings, value))
                OnPropertyChanged(nameof(DiagnosticIndicator));
        }
    }
    
    /// <summary>Number of errors.</summary>
    public int ErrorCount
    {
        get => _errorCount;
        set
        {
            if (SetProperty(ref _errorCount, value))
                OnPropertyChanged(nameof(DiagnosticIndicator));
        }
    }
    
    /// <summary>Number of warnings.</summary>
    public int WarningCount
    {
        get => _warningCount;
        set
        {
            if (SetProperty(ref _warningCount, value))
                OnPropertyChanged(nameof(DiagnosticIndicator));
        }
    }
    
    /// <summary>
    /// Compact diagnostic indicator text for display on the tab.
    /// Shows "⛔3" for errors, "⚠2" for warnings, or "" if clean.
    /// </summary>
    public string DiagnosticIndicator
    {
        get
        {
            if (_errorCount > 0) return $"⛔{_errorCount}";
            if (_warningCount > 0) return $"⚠{_warningCount}";
            return string.Empty;
        }
    }
    
    #region INotifyPropertyChanged
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return false;
        
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    
    #endregion
    
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
            ".csproj" or ".fsproj" or ".vbproj" or ".nfproj" => "xml",
            ".props" or ".targets" or ".nuspec" or ".config" => "xml",
            ".sln" or ".slnx" => "plaintext",
            ".txt" or ".log" or ".csv" or ".cfg" or ".ini" or ".conf" => "plaintext",
            ".toml" => "toml",
            ".kt" or ".kts" => "kotlin",
            ".swift" => "swift",
            ".razor" or ".cshtml" => "html",
            _ => "plaintext"
        };
    }
}

