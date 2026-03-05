using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Controls;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for Copilot CLI commands - handles file creation, deletion, and other project operations
/// </summary>
public class CopilotCliService
{
    private readonly FileService _fileService;
    private string? _workingDirectory;
    
    /// <summary>
    /// Event raised when a command is executed
    /// </summary>
    public event EventHandler<CopilotCommandEventArgs>? CommandExecuted;
    
    /// <summary>
    /// Event raised when output is generated
    /// </summary>
    public event EventHandler<string>? OutputGenerated;
    
    /// <summary>
    /// Current working directory for CLI operations
    /// </summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => _workingDirectory = value;
    }
    
    /// <summary>
    /// Available commands
    /// </summary>
    public IReadOnlyDictionary<string, CopilotCommand> Commands { get; }

    public CopilotCliService()
    {
        _fileService = new FileService();
        Commands = InitializeCommands();
    }

    public CopilotCliService(string workingDirectory) : this()
    {
        _workingDirectory = workingDirectory;
    }

    private Dictionary<string, CopilotCommand> InitializeCommands()
    {
        return new Dictionary<string, CopilotCommand>(StringComparer.OrdinalIgnoreCase)
        {
            // File operations
            ["create"] = new CopilotCommand("create", "Create a new file or directory", "<path> [--content <content>] [--dir]", CreateAsync),
            ["new"] = new CopilotCommand("new", "Alias for create", "<path> [--content <content>] [--dir]", CreateAsync),
            ["delete"] = new CopilotCommand("delete", "Delete a file or directory", "<path> [--force]", DeleteAsync),
            ["rm"] = new CopilotCommand("rm", "Alias for delete", "<path> [--force]", DeleteAsync),
            ["remove"] = new CopilotCommand("remove", "Alias for delete", "<path> [--force]", DeleteAsync),
            
            // Directory operations
            ["mkdir"] = new CopilotCommand("mkdir", "Create a new directory", "<path>", MkdirAsync),
            ["rmdir"] = new CopilotCommand("rmdir", "Remove a directory", "<path> [--force]", RmdirAsync),
            
            // File manipulation
            ["rename"] = new CopilotCommand("rename", "Rename a file or directory", "<old-path> <new-name>", RenameAsync),
            ["mv"] = new CopilotCommand("mv", "Move/rename a file or directory", "<source> <destination>", MoveAsync),
            ["copy"] = new CopilotCommand("copy", "Copy a file or directory", "<source> <destination>", CopyAsync),
            ["cp"] = new CopilotCommand("cp", "Alias for copy", "<source> <destination>", CopyAsync),
            
            // File content operations
            ["touch"] = new CopilotCommand("touch", "Create an empty file or update timestamp", "<path>", TouchAsync),
            ["write"] = new CopilotCommand("write", "Write content to a file", "<path> <content>", WriteAsync),
            ["append"] = new CopilotCommand("append", "Append content to a file", "<path> <content>", AppendAsync),
            ["read"] = new CopilotCommand("read", "Read file content", "<path>", ReadAsync),
            ["cat"] = new CopilotCommand("cat", "Display file content", "<path>", ReadAsync),
            
            // Listing and navigation
            ["ls"] = new CopilotCommand("ls", "List directory contents", "[path] [--all]", ListAsync),
            ["dir"] = new CopilotCommand("dir", "List directory contents", "[path] [--all]", ListAsync),
            ["tree"] = new CopilotCommand("tree", "Display directory tree", "[path] [--depth <n>]", TreeAsync),
            ["pwd"] = new CopilotCommand("pwd", "Print working directory", "", PwdAsync),
            ["cd"] = new CopilotCommand("cd", "Change working directory", "<path>", CdAsync),
            
            // Search
            ["find"] = new CopilotCommand("find", "Find files by name pattern", "<pattern> [--path <dir>]", FindAsync),
            ["search"] = new CopilotCommand("search", "Search for text in files", "<text> [--path <dir>] [--ext <extension>]", SearchAsync),
            
            // Templates
            ["template"] = new CopilotCommand("template", "Create file from template", "<template-name> <path>", TemplateAsync),
            
            // Info
            ["help"] = new CopilotCommand("help", "Show help for commands", "[command]", HelpAsync),
            ["info"] = new CopilotCommand("info", "Show file/directory information", "<path>", InfoAsync),
            ["exists"] = new CopilotCommand("exists", "Check if file/directory exists", "<path>", ExistsAsync),
            
            // GitHub CLI commands - SETUP & STATUS ONLY
            ["gh-install"] = new CopilotCommand("gh-install", "Install GitHub CLI via winget", "", GhInstallAsync),
            ["gh-auth"] = new CopilotCommand("gh-auth", "Authenticate gh and git with GitHub", "<login|logout|status|refresh|setup-git|token> [args...]", GhAuthAsync),
            ["gh-status"] = new CopilotCommand("gh-status", "Show GitHub CLI installation and auth status", "", GhStatusAsync),
            ["gh-config"] = new CopilotCommand("gh-config", "Manage configuration for gh", "<get|set|list> [args...]", GhConfigAsync),
            ["gh-extension"] = new CopilotCommand("gh-extension", "Manage gh extensions (install Copilot, etc.)", "<list|install|upgrade|remove> [args...]", GhExtensionAsync),
            ["gh-copilot"] = new CopilotCommand("gh-copilot", "GitHub Copilot CLI - AI command assistant", "<explain|suggest|config> <args...>", GhCopilotAsync),
            ["gh-t"] = new CopilotCommand("gh-t", "Run GitHub Copilot in agent mode with project context", "<task description>", GhCopilotTaskAsync),
            ["gh-m"] = new CopilotCommand("gh-m", "Set GitHub Copilot model", "<model name>", GhCopilotModelAsync),
            
            // External terminal commands
            ["terminal"] = new CopilotCommand("terminal", "Open external Windows Terminal or CMD", "[command]", OpenExternalTerminalAsync),
            ["cmd-ext"] = new CopilotCommand("cmd-ext", "Open external Command Prompt window", "[command]", OpenExternalTerminalAsync),
            ["wt"] = new CopilotCommand("wt", "Open Windows Terminal", "[command]", OpenExternalTerminalAsync),
            ["powershell-ext"] = new CopilotCommand("powershell-ext", "Open external PowerShell window", "[command]", OpenExternalPowerShellAsync),
        };
    }

    #region Command Execution

    /// <summary>
    /// Execute a CLI command
    /// </summary>
    public async Task<CopilotCliResult> ExecuteAsync(string commandLine)
    {
        var L = LocalizationService.Get;
        if (string.IsNullOrWhiteSpace(commandLine))
            return new CopilotCliResult(false, L("Cli.NoCommand"));

        var parts = ParseCommandLine(commandLine);
        if (parts.Count == 0)
            return new CopilotCliResult(false, L("Cli.InvalidCommand"));

        var commandName = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        if (!Commands.TryGetValue(commandName, out var command))
            return new CopilotCliResult(false, string.Format(L("Cli.UnknownCommand"), commandName));

        try
        {
            var result = await command.Execute(args);
            CommandExecuted?.Invoke(this, new CopilotCommandEventArgs(commandName, args, result));
            if (!string.IsNullOrEmpty(result.Output))
                OutputGenerated?.Invoke(this, result.Output);
            return result;
        }
        catch (Exception ex)
        {
            var errorResult = new CopilotCliResult(false, string.Format(L("Cli.ErrorExecuting"), commandName, ex.Message));
            CommandExecuted?.Invoke(this, new CopilotCommandEventArgs(commandName, args, errorResult));
            return errorResult;
        }
    }

    private List<string> ParseCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var regex = new Regex(@"[\""].+?[\""]|[^ ]+");
        var matches = regex.Matches(commandLine.Trim());
        
        foreach (Match match in matches)
        {
            var value = match.Value;
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2);
            }
            parts.Add(value);
        }
        
        return parts;
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }
        
        if (string.IsNullOrEmpty(_workingDirectory))
        {
            return Path.GetFullPath(path);
        }
        
        return Path.GetFullPath(Path.Combine(_workingDirectory, path));
    }

    #endregion

    #region File Creation Commands

    private async Task<CopilotCliResult> CreateAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Create"));
        }

        var path = ResolvePath(args[0]);
        var content = GetArgValue(args, "--content");
        var isDir = HasFlag(args, "--dir");

        if (isDir)
        {
            return await MkdirAsync(new[] { args[0] });
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            return new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.FileAlreadyExists"), path));
        }

        var fileContent = content ?? GetDefaultContent(path);
        await File.WriteAllTextAsync(path, fileContent);
        
        return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.CreatedFile"), path));
    }

    private async Task<CopilotCliResult> TouchAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Touch"));
        }

        var path = ResolvePath(args[0]);

        if (File.Exists(path))
        {
            File.SetLastWriteTime(path, DateTime.Now);
            return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.UpdatedTimestamp"), path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, string.Empty);
        return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.CreatedFile"), path));
    }

    private Task<CopilotCliResult> MkdirAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Mkdir")));
        }

        var path = ResolvePath(args[0]);

        if (Directory.Exists(path))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryAlreadyExists"), path)));

        Directory.CreateDirectory(path);
        return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.CreatedDirectory"), path)));
    }

    private async Task<CopilotCliResult> TemplateAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Template"));
        }

        var templateName = args[0].ToLowerInvariant();
        var path = ResolvePath(args[1]);
        var fileName = Path.GetFileNameWithoutExtension(path);

        var content = templateName switch
        {
            "class" => GenerateClassTemplate(fileName),
            "interface" => GenerateInterfaceTemplate(fileName),
            "record" => GenerateRecordTemplate(fileName),
            "enum" => GenerateEnumTemplate(fileName),
            "service" => GenerateServiceTemplate(fileName),
            "viewmodel" => GenerateViewModelTemplate(fileName),
            "html" => GenerateHtmlTemplate(fileName),
            "json" => "{\n    \n}",
            "xml" => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n    \n</root>",
            _ => null
        };

        if (content == null)
            return new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.UnknownTemplate"), templateName));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content);
        return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.CreatedFromTemplate"), templateName, path));
    }

    #endregion

    #region File Deletion Commands

    private Task<CopilotCliResult> DeleteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Delete")));
        }

        var path = ResolvePath(args[0]);
        var force = HasFlag(args, "--force") || HasFlag(args, "-f");

        if (Directory.Exists(path))
        {
            if (!force && Directory.GetFileSystemEntries(path).Length > 0)
                return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotEmpty"), path)));
            Directory.Delete(path, recursive: true);
            return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.DeletedDirectory"), path)));
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.DeletedFile"), path)));
        }

        return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.PathNotFound"), path)));
    }

    private Task<CopilotCliResult> RmdirAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Rmdir")));
        }

        var path = ResolvePath(args[0]);
        var force = HasFlag(args, "--force") || HasFlag(args, "-f");

        if (!Directory.Exists(path))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotFound"), path)));

        if (!force && Directory.GetFileSystemEntries(path).Length > 0)
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotEmpty"), path)));

        Directory.Delete(path, recursive: true);
        return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.DeletedDirectory"), path)));
    }

    #endregion

    #region File Manipulation Commands

    private Task<CopilotCliResult> RenameAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Rename")));
        }

        var oldPath = ResolvePath(args[0]);
        var newName = args[1];
        var directory = Path.GetDirectoryName(oldPath);
        var newPath = Path.Combine(directory ?? string.Empty, newName);

        if (!File.Exists(oldPath) && !Directory.Exists(oldPath))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.PathNotFound"), oldPath)));

        if (File.Exists(newPath) || Directory.Exists(newPath))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DestinationExists"), newPath)));

        if (Directory.Exists(oldPath))
            Directory.Move(oldPath, newPath);
        else
            File.Move(oldPath, newPath);

        return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.Renamed"), oldPath, newPath)));
    }

    private Task<CopilotCliResult> MoveAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Mv")));
        }

        var source = ResolvePath(args[0]);
        var destination = ResolvePath(args[1]);

        if (!File.Exists(source) && !Directory.Exists(source))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.SourceNotFound"), source)));

        // If destination is a directory, move into it
        if (Directory.Exists(destination))
            destination = Path.Combine(destination, Path.GetFileName(source));

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else
            File.Move(source, destination);

        return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.Moved"), source, destination)));
    }

    private Task<CopilotCliResult> CopyAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.Copy")));
        }

        var source = ResolvePath(args[0]);
        var destination = ResolvePath(args[1]);

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.SourceNotFound"), source)));
        }

        if (Directory.Exists(destination))
            destination = Path.Combine(destination, Path.GetFileName(source));

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        if (Directory.Exists(source))
            CopyDirectory(source, destination);
        else
            File.Copy(source, destination, overwrite: false);

        return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.Copied"), source, destination)));
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    #endregion

    #region File Content Commands

    private async Task<CopilotCliResult> WriteAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return new CopilotCliResult(false, "Usage: write <path> <content>");
        }

        var path = ResolvePath(args[0]);
        var content = string.Join(" ", args.Skip(1));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content);
        return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.WrittenTo"), path));
    }

    private async Task<CopilotCliResult> AppendAsync(string[] args)
    {
        if (args.Length < 2)
            return new CopilotCliResult(false, "Usage: append <path> <content>");

        var path = ResolvePath(args[0]);
        var content = string.Join(" ", args.Skip(1));

        if (!File.Exists(path))
            return new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.FileNotFound"), path));

        await File.AppendAllTextAsync(path, content);
        return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.AppendedTo"), path));
    }

    private async Task<CopilotCliResult> ReadAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, "Usage: read <path>");

        var path = ResolvePath(args[0]);

        if (!File.Exists(path))
            return new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.FileNotFound"), path));

        var content = await File.ReadAllTextAsync(path);
        return new CopilotCliResult(true, content);
    }

    #endregion

    #region Listing Commands

    private Task<CopilotCliResult> ListAsync(string[] args)
    {
        var path = args.Length > 0 && !args[0].StartsWith("-") 
            ? ResolvePath(args[0]) 
            : _workingDirectory ?? Directory.GetCurrentDirectory();
        var showAll = HasFlag(args, "--all") || HasFlag(args, "-a");

        if (!Directory.Exists(path))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotFound"), path)));

        var output = new List<string>();
        output.Add(string.Format(LocalizationService.Get("Cli.Directory"), path) + "\n");

        var dirs = Directory.GetDirectories(path).Select(d => new DirectoryInfo(d))
            .Where(d => showAll || !d.Name.StartsWith(".")).OrderBy(d => d.Name).ToArray();
        var files = Directory.GetFiles(path).Select(f => new FileInfo(f))
            .Where(f => showAll || !f.Name.StartsWith(".")).OrderBy(f => f.Name).ToArray();

        foreach (var dir in dirs) output.Add($"  [DIR]  {dir.Name}/");
        foreach (var file in files) output.Add($"  {FormatFileSize(file.Length),10}  {file.Name}");

        output.Add("\n" + string.Format(LocalizationService.Get("Cli.DirsAndFiles"), dirs.Length, files.Length));
        return Task.FromResult(new CopilotCliResult(true, string.Join("\n", output)));
    }

    private Task<CopilotCliResult> TreeAsync(string[] args)
    {
        var path = args.Length > 0 && !args[0].StartsWith("-") 
            ? ResolvePath(args[0]) 
            : _workingDirectory ?? Directory.GetCurrentDirectory();
        var depth = GetArgValueInt(args, "--depth", 3);

        if (!Directory.Exists(path))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotFound"), path)));

        var output = new List<string>();
        output.Add(Path.GetFileName(path) + "/");
        BuildTree(path, "", depth, output);

        return Task.FromResult(new CopilotCliResult(true, string.Join("\n", output)));
    }

    private void BuildTree(string path, string indent, int depth, List<string> output)
    {
        if (depth <= 0) return;

        var entries = new List<string>();
        entries.AddRange(Directory.GetDirectories(path).OrderBy(d => d));
        entries.AddRange(Directory.GetFiles(path).OrderBy(f => f));

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var isLast = i == entries.Count - 1;
            var prefix = isLast ? "└── " : "├── ";
            var childIndent = indent + (isLast ? "    " : "│   ");

            var name = Path.GetFileName(entry);
            if (name.StartsWith(".")) continue;

            if (Directory.Exists(entry))
            {
                output.Add($"{indent}{prefix}{name}/");
                BuildTree(entry, childIndent, depth - 1, output);
            }
            else
            {
                output.Add($"{indent}{prefix}{name}");
            }
        }
    }

    private Task<CopilotCliResult> PwdAsync(string[] args)
    {
        var path = _workingDirectory ?? Directory.GetCurrentDirectory();
        return Task.FromResult(new CopilotCliResult(true, path));
    }

    private Task<CopilotCliResult> CdAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: cd <path>"));
        }

        var path = ResolvePath(args[0]);

        if (!Directory.Exists(path))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotFound"), path)));

        _workingDirectory = path;
        return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.ChangedDirectory"), path)));
    }

    #endregion

    #region Search Commands

    private Task<CopilotCliResult> FindAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: find <pattern> [--path <dir>]"));
        }

        var pattern = args[0];
        var searchPath = GetArgValue(args, "--path") ?? _workingDirectory ?? Directory.GetCurrentDirectory();
        searchPath = ResolvePath(searchPath);

        if (!Directory.Exists(searchPath))
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotFound"), searchPath)));

        var results = new List<string>();
        FindFiles(searchPath, pattern, results);

        if (results.Count == 0)
            return Task.FromResult(new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.NoFilesFound"), pattern)));

        var output = string.Format(LocalizationService.Get("Cli.FoundFiles"), results.Count) + "\n" + string.Join("\n", results.Select(r => "  " + r));
        return Task.FromResult(new CopilotCliResult(true, output));
    }

    private void FindFiles(string directory, string pattern, List<string> results)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (MatchWildcard(name, pattern))
                {
                    results.Add(file);
                }
            }

            foreach (var dir in Directory.GetDirectories(directory))
            {
                if (Path.GetFileName(dir).StartsWith(".")) continue;
                FindFiles(dir, pattern, results);
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<CopilotCliResult> SearchAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, "Usage: search <text> [--path <dir>] [--ext <extension>]");
        }

        var searchText = args[0];
        var searchPath = GetArgValue(args, "--path") ?? _workingDirectory ?? Directory.GetCurrentDirectory();
        searchPath = ResolvePath(searchPath);
        var extension = GetArgValue(args, "--ext");

        if (!Directory.Exists(searchPath))
            return new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.DirectoryNotFound"), searchPath));

        var results = new List<string>();
        await SearchInFiles(searchPath, searchText, extension, results);

        if (results.Count == 0)
            return new CopilotCliResult(true, string.Format(LocalizationService.Get("Cli.NoMatchesFound"), searchText));

        var output2 = string.Format(LocalizationService.Get("Cli.FoundMatches"), results.Count) + "\n" + string.Join("\n", results);
        return new CopilotCliResult(true, output2);
    }

    private async Task SearchInFiles(string directory, string searchText, string? extension, List<string> results)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                if (extension != null && !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var lines = content.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add($"  {file}:{i + 1}: {lines[i].Trim()}");
                        }
                    }
                }
                catch { }
            }

            foreach (var dir in Directory.GetDirectories(directory))
            {
                if (Path.GetFileName(dir).StartsWith(".")) continue;
                if (Path.GetFileName(dir) == "bin" || Path.GetFileName(dir) == "obj" || Path.GetFileName(dir) == "node_modules")
                    continue;
                await SearchInFiles(dir, searchText, extension, results);
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    #endregion

    #region Info Commands

    private Task<CopilotCliResult> InfoAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: info <path>"));
        }

        var path = ResolvePath(args[0]);

        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            var fileCount = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
            var dirCount = Directory.GetDirectories(path, "*", SearchOption.AllDirectories).Length;
            
            var output = $"Directory: {dirInfo.FullName}\n" +
                         $"Created: {dirInfo.CreationTime}\n" +
                         $"Modified: {dirInfo.LastWriteTime}\n" +
                         $"Contains: {fileCount} files, {dirCount} subdirectories";
            
            return Task.FromResult(new CopilotCliResult(true, output));
        }

        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            var output = $"File: {fileInfo.FullName}\n" +
                         $"Size: {FormatFileSize(fileInfo.Length)}\n" +
                         $"Created: {fileInfo.CreationTime}\n" +
                         $"Modified: {fileInfo.LastWriteTime}\n" +
                         $"Extension: {fileInfo.Extension}";
            
            return Task.FromResult(new CopilotCliResult(true, output));
        }

        return Task.FromResult(new CopilotCliResult(false, $"Path not found: {path}"));
    }

    private Task<CopilotCliResult> ExistsAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: exists <path>"));
        }

        var path = ResolvePath(args[0]);
        var exists = File.Exists(path) || Directory.Exists(path);
        var type = Directory.Exists(path) ? "directory" : (File.Exists(path) ? "file" : "nothing");

        return Task.FromResult(new CopilotCliResult(true, exists ? $"Yes ({type}): {path}" : $"No: {path}"));
    }

    private Task<CopilotCliResult> HelpAsync(string[] args)
    {
        var L = LocalizationService.Get;

        if (args.Length > 0 && Commands.TryGetValue(args[0], out var cmd))
        {
            var output = string.Format(L("Cli.Help.Command"), cmd.Name) + "\n" +
                         string.Format(L("Cli.Help.Description"), cmd.Description) + "\n" +
                         string.Format(L("Cli.Help.Usage"), cmd.Name, cmd.Usage);
            return Task.FromResult(new CopilotCliResult(true, output));
        }

        var lines = new List<string>
        {
            L("Cli.Help.Title"),
            "=====================",
            "",
            L("Cli.Help.FileOps"),
            L("Cli.Help.Create"),
            L("Cli.Help.Delete"),
            L("Cli.Help.Touch"),
            L("Cli.Help.Template"),
            "",
            L("Cli.Help.DirOps"),
            L("Cli.Help.Mkdir"),
            L("Cli.Help.Rmdir"),
            "",
            L("Cli.Help.FileManip"),
            L("Cli.Help.Rename"),
            L("Cli.Help.Mv"),
            L("Cli.Help.Copy"),
            "",
            L("Cli.Help.FileContent"),
            L("Cli.Help.Write"),
            L("Cli.Help.Append"),
            L("Cli.Help.Read"),
            "",
            L("Cli.Help.NavList"),
            L("Cli.Help.Ls"),
            L("Cli.Help.Tree"),
            L("Cli.Help.Pwd"),
            L("Cli.Help.Cd"),
            "",
            L("Cli.Help.SearchSec"),
            L("Cli.Help.Find"),
            L("Cli.Help.Search"),
            "",
            L("Cli.Help.InfoSec"),
            L("Cli.Help.Info"),
            L("Cli.Help.Exists"),
            L("Cli.Help.Help"),
            "",
            L("Cli.Help.GhSec"),
            L("Cli.Help.GhInstall"),
            L("Cli.Help.GhAuth"),
            L("Cli.Help.GhStatus"),
            L("Cli.Help.GhConfig"),
            L("Cli.Help.GhExtension"),
            "",
            L("Cli.Help.TermSec"),
            L("Cli.Help.Terminal"),
            L("Cli.Help.CmdExt"),
            L("Cli.Help.Wt"),
            L("Cli.Help.PsExt"),
            "",
            L("Cli.Help.Footer")
        };

        return Task.FromResult(new CopilotCliResult(true, string.Join("\n", lines)));
    }

    #endregion

    #region Helper Methods

    private string GetDefaultContent(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(path);

        return extension switch
        {
            ".cs" => $"namespace MyNamespace;\n\npublic class {fileName}\n{{\n    \n}}\n",
            ".json" => "{\n    \n}\n",
            ".xml" => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n    \n</root>\n",
            ".html" => "<!DOCTYPE html>\n<html>\n<head>\n    <title></title>\n</head>\n<body>\n    \n</body>\n</html>\n",
            ".css" => "/* Styles */\n",
            ".js" => "// JavaScript\n",
            ".ts" => "// TypeScript\n",
            ".md" => $"# {fileName}\n\n",
            _ => string.Empty
        };
    }

    private string GenerateClassTemplate(string name) => 
        $"namespace MyNamespace;\n\n/// <summary>\n/// {name} class\n/// </summary>\npublic class {name}\n{{\n    public {name}()\n    {{\n    }}\n}}\n";

    private string GenerateInterfaceTemplate(string name) => 
        $"namespace MyNamespace;\n\n/// <summary>\n/// {name} interface\n/// </summary>\npublic interface {name}\n{{\n}}\n";

    private string GenerateRecordTemplate(string name) => 
        $"namespace MyNamespace;\n\n/// <summary>\n/// {name} record\n/// </summary>\npublic record {name}();\n";

    private string GenerateEnumTemplate(string name) => 
        $"namespace MyNamespace;\n\n/// <summary>\n/// {name} enumeration\n/// </summary>\npublic enum {name}\n{{\n    None = 0,\n}}\n";

    private string GenerateServiceTemplate(string name) => 
        $"namespace MyNamespace.Services;\n\n/// <summary>\n/// {name} service\n/// </summary>\npublic class {name}\n{{\n    public {name}()\n    {{\n    }}\n}}\n";

    private string GenerateViewModelTemplate(string name) => 
        $"using System.ComponentModel;\nusing System.Runtime.CompilerServices;\n\nnamespace MyNamespace.ViewModels;\n\n/// <summary>\n/// {name} ViewModel\n/// </summary>\npublic class {name} : INotifyPropertyChanged\n{{\n    public event PropertyChangedEventHandler? PropertyChanged;\n\n    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)\n    {{\n        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));\n    }}\n\n    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)\n    {{\n        if (EqualityComparer<T>.Default.Equals(field, value)) return false;\n        field = value;\n        OnPropertyChanged(propertyName);\n        return true;\n    }}\n}}\n";

    private string GenerateHtmlTemplate(string title) => 
        $"<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"UTF-8\">\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n    <title>{title}</title>\n</head>\n<body>\n    <h1>{title}</h1>\n</body>\n</html>\n";

    #endregion
    
    #region GitHub CLI Commands

    private async Task<CopilotCliResult> GhInstallAsync(string[] args)
    {
        var L = LocalizationService.Get;
        var checkResult = await RunGitHubCliAsync("--version");
        if (checkResult.ExitCode == 0)
            return new CopilotCliResult(true, L("Cli.GhAlreadyInstalled") + "\n" + checkResult.Output);

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install --id GitHub.cli --accept-package-agreements --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();
            _ = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
                return new CopilotCliResult(true, L("Cli.GhInstallSuccess") + "\n\n" + L("Cli.GhInstallRestart"));
            else
                return new CopilotCliResult(false, L("Cli.GhInstallFailed") + ":\n" + error + "\n\n" + L("Cli.GhInstallManual"));
        }
        catch (Exception ex)
        {
            return new CopilotCliResult(false, L("Cli.GhInstallFailed") + ": " + ex.Message + "\n\n" + L("Cli.GhInstallManual"));
        }
    }

    private async Task<CopilotCliResult> GhAuthAsync(string[] args)
    {
        var L = LocalizationService.Get;
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        switch (action)
        {
            case "login":
                var checkResult = await RunGitHubCliAsync("auth status");
                if (checkResult.ExitCode == 0)
                {
                    var currentUser = await RunGitHubCliAsync("api user --jq .login");
                    return new CopilotCliResult(true, string.Format(L("Cli.GhAuthAlreadyLoggedIn"), currentUser.Output.Trim()));
                }
                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = await FindGhPathAsync() ?? "gh",
                        Arguments = "auth login --web -h github.com",
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    return new CopilotCliResult(true, L("Cli.GhAuthStarted") + "\n\n" + L("Cli.GhAuthStartedHint") + "\n\n" + L("Cli.GhAuthVerifyHint"));
                }
                catch (Exception ex)
                {
                    return new CopilotCliResult(false, string.Format(L("Cli.GhAuthFailed"), ex.Message) + "\n" + L("Cli.GhAuthFailedHint"));
                }
            case "logout":
                var logoutResult = await RunGitHubCliAsync("auth logout -h github.com -y");
                return new CopilotCliResult(logoutResult.ExitCode == 0,
                    logoutResult.ExitCode == 0 ? L("Cli.GhLoggedOut") : string.Format(L("Cli.GhLogoutFailed"), logoutResult.Error));
            case "refresh":
                var refreshResult = await RunGitHubCliAsync("auth refresh");
                return new CopilotCliResult(refreshResult.ExitCode == 0,
                    refreshResult.ExitCode == 0 ? L("Cli.GhRefreshed") : string.Format(L("Cli.GhRefreshFailed"), refreshResult.Error));
            case "setup-git":
                var setupResult = await RunGitHubCliAsync("auth setup-git");
                return new CopilotCliResult(setupResult.ExitCode == 0,
                    setupResult.ExitCode == 0 ? L("Cli.GhSetupGit") : string.Format(L("Cli.GhSetupGitFailed"), setupResult.Error));
            case "token":
                var tokenResult = await RunGitHubCliAsync("auth token");
                return new CopilotCliResult(tokenResult.ExitCode == 0,
                    tokenResult.ExitCode == 0 ? L("Cli.GhToken") + "\n" + tokenResult.Output : string.Format(L("Cli.GhTokenFailed"), tokenResult.Error));
            case "status":
            default:
                var statusResult = await RunGitHubCliAsync("auth status");
                if (statusResult.ExitCode == 0)
                {
                    var userResult = await RunGitHubCliAsync("api user --jq .login");
                    var user = userResult.ExitCode == 0 ? userResult.Output.Trim() : "unknown";
                    return new CopilotCliResult(true, string.Format(L("Cli.GhLoggedInAs"), user) + "\n\n" + statusResult.Output);
                }
                return new CopilotCliResult(false, L("Cli.GhNotLoggedIn"));
        }
    }

    private async Task<CopilotCliResult> GhStatusAsync(string[] args)
    {
        var L = LocalizationService.Get;
        var versionResult = await RunGitHubCliAsync("--version");
        if (versionResult.ExitCode != 0)
            return new CopilotCliResult(false, L("Cli.GhNotInstalled") + "\n  gh-install\n  winget install GitHub.cli");

        var authResult = await RunGitHubCliAsync("auth status");
        var authenticated = authResult.ExitCode == 0;

        var output = L("Cli.GhStatusTitle") + "\n━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                     string.Format(L("Cli.GhStatusInstalled"), versionResult.Output.Split('\n')[0]) + "\n";

        if (authenticated)
        {
            var userResult = await RunGitHubCliAsync("api user --jq .login");
            var user = userResult.ExitCode == 0 ? userResult.Output.Trim() : "unknown";
            output += string.Format(L("Cli.GhStatusAuthAs"), user) + "\n";
        }
        else
        {
            output += L("Cli.GhStatusNotAuth") + "\n";
        }

        if (!string.IsNullOrEmpty(_workingDirectory))
        {
            output += "\n" + L("Cli.GhStatusWorkDir") + "\n   " + _workingDirectory + "\n";
            var repoResult = await RunGitHubCliAsync("repo view --json name,owner --jq \".owner.login + \\\"/\\\" + .name\"", _workingDirectory);
            if (repoResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(repoResult.Output))
                output += string.Format(L("Cli.GhStatusRepo"), repoResult.Output.Trim()) + "\n";
        }

        return new CopilotCliResult(true, output);
    }

    private async Task<CopilotCliResult> GhConfigAsync(string[] args)
    {
        var L = LocalizationService.Get;
        if (args.Length == 0)
        {
            var r = await RunGitHubCliAsync("config list", _workingDirectory);
            return new CopilotCliResult(r.ExitCode == 0,
                r.ExitCode == 0 ? L("Cli.GhConfigList") + "\n\n" + r.Output : string.Format(L("Cli.GhConfigFailed"), r.Error));
        }
        var r2 = await RunGitHubCliAsync("config " + string.Join(" ", args), _workingDirectory);
        return new CopilotCliResult(r2.ExitCode == 0,
            r2.ExitCode == 0 ? L("Cli.GhConfigDone") + "\n" + r2.Output : string.Format(L("Cli.GhConfigFailed"), r2.Error));
    }

    private async Task<CopilotCliResult> GhExtensionAsync(string[] args)
    {
        var L = LocalizationService.Get;
        if (args.Length == 0)
        {
            var r = await RunGitHubCliAsync("extension list", _workingDirectory);
            return new CopilotCliResult(r.ExitCode == 0,
                r.ExitCode == 0 ? L("Cli.GhExtensionList") + "\n\n" + r.Output : string.Format(L("Cli.GhExtensionFailed"), r.Error));
        }
        var r2 = await RunGitHubCliAsync("extension " + string.Join(" ", args), _workingDirectory);
        return new CopilotCliResult(r2.ExitCode == 0,
            r2.ExitCode == 0 ? L("Cli.GhExtensionDone") + "\n" + r2.Output : string.Format(L("Cli.GhExtensionFailed"), r2.Error));
    }

    private async Task<CopilotCliResult> GhCopilotAsync(string[] args)
    {
        var L = LocalizationService.Get;
        if (args.Length == 0)
            return await RunGitHubCopilotInteractiveAsync();

        var subCommand = args[0].ToLowerInvariant();
        if (subCommand == "suggest" || subCommand == "explain")
            return await RunGitHubCopilotInExternalTerminalAsync(args);

        var ghArgs = "copilot " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0
                ? L("Cli.GhCopilotSuggestion") + "\n" + result.Output
                : string.Format(L("Cli.GhCopilotFailed"), result.Error));
    }

    private async Task<CopilotCliResult> GhCopilotTaskAsync(string[] args)
    {
        var L = LocalizationService.Get;
        if (args.Length == 0)
            return new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.GhTask"));

        var taskDescription = string.Join(" ", args).Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(taskDescription))
            return new CopilotCliResult(false, L("Cli.NoCommand"));

        var ghArgs = $"copilot suggest -t shell \"{taskDescription.Replace("\"", "\\\"")}\"";
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 ? L("Cli.GhCopilotSuggestion") + "\n" + result.Output
                                 : string.Format(L("Cli.GhCopilotFailed"), result.Error) + "\n" + result.Output);
    }

    private async Task<CopilotCliResult> GhCopilotModelAsync(string[] args)
    {
        var L = LocalizationService.Get;
        if (args.Length == 0)
            return new CopilotCliResult(false, LocalizationService.Get("Cli.Usage.GhModel"));

        var model = string.Join(" ", args).Trim();
        var result = await RunGitHubCliAsync($"copilot config set model {model}", _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 ? L("Cli.GhModelUpdated") + "\n" + result.Output : string.Format(L("Cli.GhModelFailed"), result.Error));
    }

    private async Task<CopilotCliResult> RunGitHubCopilotInExternalTerminalAsync(string[] args)
    {
        var L = LocalizationService.Get;
        try
        {
            var ghPath = await FindGhPathAsync();
            if (string.IsNullOrEmpty(ghPath))
                return new CopilotCliResult(false, L("Cli.GhNotFound"));

            var checkExtension = await RunGitHubCliAsync("extension list");
            if (!checkExtension.Output.Contains("copilot", StringComparison.OrdinalIgnoreCase))
                return new CopilotCliResult(false, L("Cli.GhExtensionNotInstalled") + "\n\n" + L("Cli.GhExtensionInstallHint"));

            var ghArgs = "copilot " + string.Join(" ", args);
            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            OutputGenerated?.Invoke(this, L("Cli.GhCopilotLaunching") + "\n");

            var wtPath = FindWindowsTerminal();
            System.Diagnostics.ProcessStartInfo startInfo;
            string terminalName;

            if (!string.IsNullOrEmpty(wtPath))
            {
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{workDir}\" --title \"GitHub Copilot CLI\" cmd /k \"{ghPath}\" {ghArgs}",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Windows Terminal";
            }
            else
            {
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"{ghPath}\" {ghArgs}",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Command Prompt";
            }

            System.Diagnostics.Process.Start(startInfo);
            return new CopilotCliResult(true,
                string.Format(L("Cli.GhCopilotOpened"), terminalName) + "\n\n" + L("Cli.GhCopilotTip"));
        }
        catch (Exception ex)
        {
            return new CopilotCliResult(false, string.Format(L("Cli.GhCopilotError"), ex.Message));
        }
    }

    private async Task<CopilotCliResult> RunGitHubCopilotInteractiveAsync()
    {
        var L = LocalizationService.Get;
        try
        {
            var ghPath = await FindGhPathAsync();
            if (string.IsNullOrEmpty(ghPath))
                return new CopilotCliResult(false, L("Cli.GhNotFound"));

            var checkExtension = await RunGitHubCliAsync("extension list");
            if (!checkExtension.Output.Contains("copilot", StringComparison.OrdinalIgnoreCase))
                return new CopilotCliResult(false, L("Cli.GhExtensionNotInstalled") + "\n\n" + L("Cli.GhExtensionInstallHint"));

            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            OutputGenerated?.Invoke(this, L("Cli.GhCopilotLaunching") + "\n");

            var wtPath = FindWindowsTerminal();
            System.Diagnostics.ProcessStartInfo startInfo;
            string terminalName;

            if (!string.IsNullOrEmpty(wtPath))
            {
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{workDir}\" --title \"GitHub Copilot CLI\" cmd /k echo. && echo GitHub Copilot CLI && echo.",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Windows Terminal";
            }
            else
            {
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d \"{workDir}\"",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Command Prompt";
            }

            System.Diagnostics.Process.Start(startInfo);
            return new CopilotCliResult(true,
                string.Format(L("Cli.GhCopilotOpened"), terminalName) + "\n\n" +
                L("Cli.GhCopilotTip"));
        }
        catch (Exception ex)
        {
            return new CopilotCliResult(false, string.Format(L("Cli.GhCopilotError"), ex.Message));
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGitHubCliAsync(string arguments, string? workingDirectory = null)
    {
        try
        {
            var ghPath = await FindGhPathAsync();
            if (string.IsNullOrEmpty(ghPath))
                return (-1, "", LocalizationService.Get("Cli.GhNotFound"));

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ghPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? _workingDirectory ?? Environment.CurrentDirectory
            };
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, output.Trim(), error.Trim());
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private async Task<string?> FindGhPathAsync()
    {
        // Check user settings first
        var fromSettings = SettingsPanelControl.ResolveGhExe();
        if (fromSettings != "gh" && File.Exists(fromSettings))
            return fromSettings;

        var possiblePaths = new[]
        {
            "gh",
            @"C:\Program Files\GitHub CLI\gh.exe",
            @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\gh\gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"GitHub CLI\gh.exe")
        };
        foreach (var path in possiblePaths)
        {
            if (path == "gh")
            {
                try
                {
                    var testProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "gh", Arguments = "--version",
                            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                        }
                    };
                    testProcess.Start();
                    await testProcess.WaitForExitAsync();
                    if (testProcess.ExitCode == 0) return "gh";
                }
                catch { continue; }
            }
            else if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string? FindWindowsTerminal()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var path in pathEnv.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var wtPath = Path.Combine(path.Trim(), "wt.exe");
            if (File.Exists(wtPath)) return wtPath;
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wt = Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe");
        return File.Exists(wt) ? wt : null;
    }

    #endregion

    #region External Terminal Commands

    private Task<CopilotCliResult> OpenExternalTerminalAsync(string[] args)
    {
        var L = LocalizationService.Get;
        try
        {
            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            var command = args.Length > 0 ? string.Join(" ", args) : null;
            var wtPath = FindWindowsTerminal();
            System.Diagnostics.ProcessStartInfo startInfo;
            string terminalName;
            if (!string.IsNullOrEmpty(wtPath))
            {
                var wtArgs = $"-d \"{workDir}\"";
                if (!string.IsNullOrEmpty(command)) wtArgs += $" cmd /k {command}";
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wtPath, Arguments = wtArgs,
                    UseShellExecute = true, WorkingDirectory = workDir
                };
                terminalName = "Windows Terminal";
            }
            else
            {
                var cmdArgs = string.IsNullOrEmpty(command) ? $"/k cd /d \"{workDir}\"" : $"/k cd /d \"{workDir}\" && {command}";
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe", Arguments = cmdArgs,
                    UseShellExecute = true, WorkingDirectory = workDir
                };
                terminalName = "Command Prompt";
            }
            System.Diagnostics.Process.Start(startInfo);
            var message = string.Format(L("Cli.TerminalOpened"), terminalName) + "\n" +
                          string.Format(L("Cli.TerminalWorkDir"), workDir) + "\n";
            if (!string.IsNullOrEmpty(command)) message += string.Format(L("Cli.TerminalCommand"), command) + "\n";
            return Task.FromResult(new CopilotCliResult(true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CopilotCliResult(false, string.Format(LocalizationService.Get("Cli.TerminalError"), ex.Message)));
        }
    }

    private Task<CopilotCliResult> OpenExternalPowerShellAsync(string[] args)
    {
        var L = LocalizationService.Get;
        try
        {
            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            var command = args.Length > 0 ? string.Join(" ", args) : null;
            string pwshArgs = string.IsNullOrEmpty(command)
                ? $"-NoExit -Command \"Set-Location '{workDir}'\""
                : $"-NoExit -Command \"Set-Location '{workDir}'; {command}\"";
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe", Arguments = pwshArgs,
                UseShellExecute = true, WorkingDirectory = workDir
            };
            System.Diagnostics.Process.Start(startInfo);
            var message = L("Cli.PowerShellOpened") + "\n" + string.Format(L("Cli.TerminalWorkDir"), workDir) + "\n";
            if (!string.IsNullOrEmpty(command)) message += string.Format(L("Cli.TerminalCommand"), command) + "\n";
            return Task.FromResult(new CopilotCliResult(true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CopilotCliResult(false, string.Format(L("Cli.PowerShellError"), ex.Message)));
        }
    }

    #endregion

    private string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private int GetArgValueInt(string[] args, string flag, int defaultValue)
    {
        var value = GetArgValue(args, flag);
        return value != null && int.TryParse(value, out var result) ? result : defaultValue;
    }

    private bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    bool MatchWildcard(string text, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }
}

#region Models

/// <summary>
/// Represents a CLI command
/// </summary>
public class CopilotCommand
{
    public string Name { get; }
    public string Description { get; }
    public string Usage { get; }
    private readonly Func<string[], Task<CopilotCliResult>> _executeFunc;

    public CopilotCommand(string name, string description, string usage, Func<string[], Task<CopilotCliResult>> executeFunc)
    {
        Name = name;
        Description = description;
        Usage = usage;
        _executeFunc = executeFunc;
    }

    public Task<CopilotCliResult> Execute(string[] args) => _executeFunc(args);
}

/// <summary>
/// Result of a CLI command execution
/// </summary>
public class CopilotCliResult
{
    public bool Success { get; }
    public string Output { get; }
    public string? CreatedPath { get; set; }
    public string? DeletedPath { get; set; }

    public CopilotCliResult(bool success, string output)
    {
        Success = success;
        Output = output;
    }
}

/// <summary>
/// Event args for command execution
/// </summary>
public class CopilotCommandEventArgs : EventArgs
{
    public string Command { get; }
    public string[] Arguments { get; }
    public CopilotCliResult Result { get; }

    public CopilotCommandEventArgs(string command, string[] arguments, CopilotCliResult result)
    {
        Command = command;
        Arguments = arguments;
        Result = result;
    }
}

#endregion
