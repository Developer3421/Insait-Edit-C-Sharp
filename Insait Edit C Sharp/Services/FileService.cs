using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Models;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for file system operations
/// </summary>
public class FileService
{
    private readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".vb",
        ".xaml", ".axaml",
        ".json", ".xml", ".config",
        ".html", ".htm", ".css", ".scss", ".less",
        ".js", ".ts", ".jsx", ".tsx",
        ".md", ".txt", ".log",
        ".yaml", ".yml",
        ".sql",
        ".sln", ".csproj", ".vbproj", ".fsproj"
    };

    /// <summary>
    /// Reads file content asynchronously
    /// </summary>
    public async Task<string> ReadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        return await File.ReadAllTextAsync(filePath);
    }

    /// <summary>
    /// Writes content to file asynchronously
    /// </summary>
    public async Task WriteFileAsync(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, content);
    }

    /// <summary>
    /// Gets the file tree for a directory
    /// </summary>
    public ProjectFile GetFileTree(string rootPath)
    {
        var rootInfo = new DirectoryInfo(rootPath);
        return BuildFileTree(rootInfo);
    }

    private ProjectFile BuildFileTree(DirectoryInfo directory)
    {
        var file = new ProjectFile
        {
            Name = directory.Name,
            FullPath = directory.FullName,
            IsDirectory = true,
            IsExpanded = false
        };

        try
        {
            // Add directories
            foreach (var subDir in directory.GetDirectories())
            {
                // Skip hidden and system directories
                if ((subDir.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    continue;
                if (subDir.Name.StartsWith(".") || subDir.Name == "bin" || subDir.Name == "obj" || subDir.Name == "node_modules")
                    continue;

                file.Children.Add(BuildFileTree(subDir));
            }

            // Add files
            foreach (var fileInfo in directory.GetFiles())
            {
                // Skip hidden files
                if ((fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    continue;

                file.Children.Add(new ProjectFile
                {
                    Name = fileInfo.Name,
                    FullPath = fileInfo.FullName,
                    IsDirectory = false,
                    Type = GetFileType(fileInfo.Extension)
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore directories we can't access
        }

        return file;
    }

    private FileType GetFileType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" or ".csx" => FileType.CSharp,
            ".xaml" or ".axaml" => FileType.Xaml,
            ".json" => FileType.Json,
            ".xml" => FileType.Xml,
            ".config" => FileType.Config,
            ".sln" => FileType.Solution,
            ".csproj" or ".vbproj" or ".fsproj" => FileType.Project,
            ".md" => FileType.Markdown,
            ".txt" or ".log" => FileType.Text,
            _ => FileType.Unknown
        };
    }

    /// <summary>
    /// Checks if a file is a supported code file
    /// </summary>
    public bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return _supportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Creates a new file
    /// </summary>
    public async Task<string> CreateFileAsync(string directory, string fileName)
    {
        var filePath = Path.Combine(directory, fileName);
        
        if (File.Exists(filePath))
        {
            throw new IOException($"File already exists: {fileName}");
        }

        await File.WriteAllTextAsync(filePath, GetDefaultContent(fileName));
        return filePath;
    }

    private string GetDefaultContent(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".cs" => $"namespace MyNamespace;\n\npublic class {Path.GetFileNameWithoutExtension(fileName)}\n{{\n    \n}}\n",
            ".json" => "{\n    \n}\n",
            ".xml" => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n    \n</root>\n",
            ".html" => "<!DOCTYPE html>\n<html>\n<head>\n    <title></title>\n</head>\n<body>\n    \n</body>\n</html>\n",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Creates a new directory
    /// </summary>
    public void CreateDirectory(string parentPath, string directoryName)
    {
        var path = Path.Combine(parentPath, directoryName);
        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Deletes a file or directory
    /// </summary>
    public void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Renames a file or directory
    /// </summary>
    public void Rename(string oldPath, string newName)
    {
        var directory = Path.GetDirectoryName(oldPath);
        var newPath = Path.Combine(directory ?? string.Empty, newName);

        if (Directory.Exists(oldPath))
        {
            Directory.Move(oldPath, newPath);
        }
        else if (File.Exists(oldPath))
        {
            File.Move(oldPath, newPath);
        }
    }
}

