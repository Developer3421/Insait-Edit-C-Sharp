using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            
            // GitHub CLI commands - CORE COMMANDS
            ["gh"] = new CopilotCommand("gh", "Run GitHub CLI command", "<command> [args...]", GhCommandAsync),
            ["gh-install"] = new CopilotCommand("gh-install", "Install GitHub CLI via winget", "", GhInstallAsync),
            
            // CORE COMMANDS
            ["gh-auth"] = new CopilotCommand("gh-auth", "Authenticate gh and git with GitHub", "<login|logout|status|refresh|setup-git|token> [args...]", GhAuthAsync),
            ["gh-browse"] = new CopilotCommand("gh-browse", "Open the repository in the browser", "[path] [--branch <branch>] [--commit] [--projects] [--settings] [--wiki]", GhBrowseAsync),
            ["gh-codespace"] = new CopilotCommand("gh-codespace", "Connect to and manage codespaces", "<create|code|cp|delete|edit|jupyter|list|logs|ports|rebuild|ssh|stop|view> [args...]", GhCodespaceAsync),
            ["gh-gist"] = new CopilotCommand("gh-gist", "Manage gists", "<create|list|view|edit|delete|clone> [args...]", GhGistAsync),
            ["gh-issue"] = new CopilotCommand("gh-issue", "Manage issues", "<create|list|view|close|reopen|edit|delete|comment|status|transfer|lock|unlock|pin|unpin|develop> [args...]", GhIssueAsync),
            ["gh-org"] = new CopilotCommand("gh-org", "Manage organizations", "<list> [args...]", GhOrgAsync),
            ["gh-pr"] = new CopilotCommand("gh-pr", "Manage pull requests", "<create|list|view|checkout|merge|close|reopen|edit|review|comment|diff|checks|ready|status> [args...]", GhPrAsync),
            ["gh-project" ] = new CopilotCommand("gh-project", "Work with GitHub Projects", "<create|list|view|edit|delete|close|reopen|field-create|field-delete|field-list|item-add|item-archive|item-create|item-delete|item-edit|item-list|link|unlink|mark-template|copy> [args...]", GhProjectAsync),
            ["gh-release" ] = new CopilotCommand("gh-release", "Manage releases", "<create|list|view|download|upload|edit|delete|delete-asset|download-asset|view-asset> [args...]", GhReleaseAsync),
            ["gh-repo"] = new CopilotCommand("gh-repo", "Manage repositories", "<create|clone|view|list|fork|delete|archive|unarchive|edit|rename|sync|deploy-key|garden|set-default> [args...]", GhRepoAsync),
            
            // GITHUB ACTIONS COMMANDS
            ["gh-cache"] = new CopilotCommand("gh-cache", "Manage GitHub Actions caches", "<list|delete> [args...]", GhCacheAsync),
            ["gh-run"] = new CopilotCommand("gh-run", "View details about workflow runs", "<list|view|watch|rerun|cancel|download|delete> [args...]", GhRunAsync),
            ["gh-workflow"] = new CopilotCommand("gh-workflow", "View details about GitHub Actions workflows", "<list|view|run|enable|disable> [args...]", GhWorkflowAsync),
            
            // ALIAS COMMANDS
            ["gh-co"] = new CopilotCommand("gh-co", "Alias for 'pr checkout'", "<pr-number> [args...]", GhCoAsync),
            
            // ADDITIONAL COMMANDS
            ["gh-agent-task" ] = new CopilotCommand("gh-agent-task", "Work with agent tasks (preview)", "<list|view|create> [args...]", GhAgentTaskAsync),
            ["gh-alias"] = new CopilotCommand("gh-alias", "Create command shortcuts", "<list|set|delete> [args...]", GhAliasAsync),
            ["gh-api"] = new CopilotCommand("gh-api", "Make an authenticated GitHub API request", "<endpoint> [flags...]", GhApiAsync),
            ["gh-attestation"] = new CopilotCommand("gh-attestation", "Work with artifact attestations", "<verify|download|inspect|tuf-root-verify> [args...]", GhAttestationAsync),
            ["gh-completion"] = new CopilotCommand("gh-completion", "Generate shell completion scripts", "<bash|zsh|fish|powershell> [args...]", GhCompletionAsync),
            ["gh-config"] = new CopilotCommand("gh-config", "Manage configuration for gh", "<get|set|list> [args...]", GhConfigAsync),
            ["gh-copilot"] = new CopilotCommand("gh-copilot", "GitHub Copilot CLI - AI command assistant", "<explain|suggest|config> <args...>", GhCopilotAsync),
            ["gh-t"] = new CopilotCommand("gh-t", "Run GitHub Copilot in agent mode with project context", "<task description>", GhCopilotTaskAsync),
            ["gh-m"] = new CopilotCommand("gh-m", "Set GitHub Copilot model", "<model name>", GhCopilotModelAsync),
            ["gh-extension"] = new CopilotCommand("gh-extension", "Manage gh extensions", "<list|install|upgrade|remove|create|search|browse|exec> [args...]", GhExtensionAsync),
            ["gh-gpg-key"] = new CopilotCommand("gh-gpg-key", "Manage GPG keys", "<list|add|delete> [args...]", GhGpgKeyAsync),
            ["gh-label"] = new CopilotCommand("gh-label", "Manage labels", "<list|create|edit|delete|clone> [args...]", GhLabelAsync),
            ["gh-preview"] = new CopilotCommand("gh-preview", "Execute previews for gh features", "[args...]", GhPreviewAsync),
            ["gh-ruleset"] = new CopilotCommand("gh-ruleset", "View info about repo rulesets", "<list|view|check> [args...]", GhRulesetAsync),
            ["gh-search"] = new CopilotCommand("gh-search", "Search for repositories, issues, and pull requests", "<repos|issues|prs|code|commits> [args...]", GhSearchAsync),
            ["gh-secret" ] = new CopilotCommand("gh-secret", "Manage GitHub secrets", "<list|set|remove> [--org|--env|--user] [args...]", GhSecretAsync),
            ["gh-ssh-key"] = new CopilotCommand("gh-ssh-key", "Manage SSH keys", "<list|add|delete> [args...]", GhSshKeyAsync),
            ["gh-status"] = new CopilotCommand("gh-status", "Print information about relevant issues, pull requests, and notifications", "[--org <org>] [--exclude <repos>]", GhStatusAsync),
            ["gh-variable"] = new CopilotCommand("gh-variable", "Manage GitHub Actions variables", "<list|set|get|delete> [--org|--env] [args...]", GhVariableAsync),
            
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
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new CopilotCliResult(false, "No command provided");
        }

        var parts = ParseCommandLine(commandLine);
        if (parts.Count == 0)
        {
            return new CopilotCliResult(false, "Invalid command");
        }

        var commandName = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        if (!Commands.TryGetValue(commandName, out var command))
        {
            return new CopilotCliResult(false, $"Unknown command: {commandName}. Use 'help' to see available commands.");
        }

        try
        {
            var result = await command.Execute(args);
            
            CommandExecuted?.Invoke(this, new CopilotCommandEventArgs(commandName, args, result));
            
            if (!string.IsNullOrEmpty(result.Output))
            {
                OutputGenerated?.Invoke(this, result.Output);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            var errorResult = new CopilotCliResult(false, $"Error executing '{commandName}': {ex.Message}");
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
            return new CopilotCliResult(false, "Usage: create <path> [--content <content>] [--dir]");
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
            return new CopilotCliResult(false, $"File already exists: {path}");
        }

        var fileContent = content ?? GetDefaultContent(path);
        await File.WriteAllTextAsync(path, fileContent);
        
        return new CopilotCliResult(true, $"Created file: {path}");
    }

    private async Task<CopilotCliResult> TouchAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, "Usage: touch <path>");
        }

        var path = ResolvePath(args[0]);

        if (File.Exists(path))
        {
            File.SetLastWriteTime(path, DateTime.Now);
            return new CopilotCliResult(true, $"Updated timestamp: {path}");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, string.Empty);
        return new CopilotCliResult(true, $"Created file: {path}");
    }

    private Task<CopilotCliResult> MkdirAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: mkdir <path>"));
        }

        var path = ResolvePath(args[0]);

        if (Directory.Exists(path))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory already exists: {path}"));
        }

        Directory.CreateDirectory(path);
        return Task.FromResult(new CopilotCliResult(true, $"Created directory: {path}"));
    }

    private async Task<CopilotCliResult> TemplateAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return new CopilotCliResult(false, "Usage: template <template-name> <path>\n\nAvailable templates:\n  class - C# class file\n  interface - C# interface file\n  record - C# record file\n  enum - C# enum file\n  service - C# service class\n  viewmodel - MVVM ViewModel\n  html - HTML file\n  json - JSON file\n  xml - XML file");
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
        {
            return new CopilotCliResult(false, $"Unknown template: {templateName}");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content);
        return new CopilotCliResult(true, $"Created from template '{templateName}': {path}");
    }

    #endregion

    #region File Deletion Commands

    private Task<CopilotCliResult> DeleteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: delete <path> [--force]"));
        }

        var path = ResolvePath(args[0]);
        var force = HasFlag(args, "--force") || HasFlag(args, "-f");

        if (Directory.Exists(path))
        {
            if (!force && Directory.GetFileSystemEntries(path).Length > 0)
            {
                return Task.FromResult(new CopilotCliResult(false, $"Directory is not empty: {path}. Use --force to delete anyway."));
            }
            Directory.Delete(path, recursive: true);
            return Task.FromResult(new CopilotCliResult(true, $"Deleted directory: {path}"));
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return Task.FromResult(new CopilotCliResult(true, $"Deleted file: {path}"));
        }

        return Task.FromResult(new CopilotCliResult(false, $"Path not found: {path}"));
    }

    private Task<CopilotCliResult> RmdirAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: rmdir <path> [--force]"));
        }

        var path = ResolvePath(args[0]);
        var force = HasFlag(args, "--force") || HasFlag(args, "-f");

        if (!Directory.Exists(path))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory not found: {path}"));
        }

        if (!force && Directory.GetFileSystemEntries(path).Length > 0)
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory is not empty: {path}. Use --force to delete anyway."));
        }

        Directory.Delete(path, recursive: true);
        return Task.FromResult(new CopilotCliResult(true, $"Deleted directory: {path}"));
    }

    #endregion

    #region File Manipulation Commands

    private Task<CopilotCliResult> RenameAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: rename <old-path> <new-name>"));
        }

        var oldPath = ResolvePath(args[0]);
        var newName = args[1];
        var directory = Path.GetDirectoryName(oldPath);
        var newPath = Path.Combine(directory ?? string.Empty, newName);

        if (!File.Exists(oldPath) && !Directory.Exists(oldPath))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Path not found: {oldPath}"));
        }

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Destination already exists: {newPath}"));
        }

        if (Directory.Exists(oldPath))
        {
            Directory.Move(oldPath, newPath);
        }
        else
        {
            File.Move(oldPath, newPath);
        }

        return Task.FromResult(new CopilotCliResult(true, $"Renamed: {oldPath} -> {newPath}"));
    }

    private Task<CopilotCliResult> MoveAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: mv <source> <destination>"));
        }

        var source = ResolvePath(args[0]);
        var destination = ResolvePath(args[1]);

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Source not found: {source}"));
        }

        // If destination is a directory, move into it
        if (Directory.Exists(destination))
        {
            destination = Path.Combine(destination, Path.GetFileName(source));
        }

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }

        return Task.FromResult(new CopilotCliResult(true, $"Moved: {source} -> {destination}"));
    }

    private Task<CopilotCliResult> CopyAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CopilotCliResult(false, "Usage: copy <source> <destination>"));
        }

        var source = ResolvePath(args[0]);
        var destination = ResolvePath(args[1]);

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Source not found: {source}"));
        }

        if (Directory.Exists(destination))
        {
            destination = Path.Combine(destination, Path.GetFileName(source));
        }

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
        }
        else
        {
            File.Copy(source, destination, overwrite: false);
        }

        return Task.FromResult(new CopilotCliResult(true, $"Copied: {source} -> {destination}"));
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
        return new CopilotCliResult(true, $"Written to: {path}");
    }

    private async Task<CopilotCliResult> AppendAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return new CopilotCliResult(false, "Usage: append <path> <content>");
        }

        var path = ResolvePath(args[0]);
        var content = string.Join(" ", args.Skip(1));

        if (!File.Exists(path))
        {
            return new CopilotCliResult(false, $"File not found: {path}");
        }

        await File.AppendAllTextAsync(path, content);
        return new CopilotCliResult(true, $"Appended to: {path}");
    }

    private async Task<CopilotCliResult> ReadAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, "Usage: read <path>");
        }

        var path = ResolvePath(args[0]);

        if (!File.Exists(path))
        {
            return new CopilotCliResult(false, $"File not found: {path}");
        }

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
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory not found: {path}"));
        }

        var output = new List<string>();
        output.Add($"Directory: {path}\n");

        var dirs = Directory.GetDirectories(path)
            .Select(d => new DirectoryInfo(d))
            .Where(d => showAll || !d.Name.StartsWith("."))
            .OrderBy(d => d.Name);

        var files = Directory.GetFiles(path)
            .Select(f => new FileInfo(f))
            .Where(f => showAll || !f.Name.StartsWith("."))
            .OrderBy(f => f.Name);

        foreach (var dir in dirs)
        {
            output.Add($"  [DIR]  {dir.Name}/");
        }

        foreach (var file in files)
        {
            var size = FormatFileSize(file.Length);
            output.Add($"  {size,10}  {file.Name}");
        }

        output.Add($"\n{dirs.Count()} directories, {files.Count()} files");

        return Task.FromResult(new CopilotCliResult(true, string.Join("\n", output)));
    }

    private Task<CopilotCliResult> TreeAsync(string[] args)
    {
        var path = args.Length > 0 && !args[0].StartsWith("-") 
            ? ResolvePath(args[0]) 
            : _workingDirectory ?? Directory.GetCurrentDirectory();
        var depth = GetArgValueInt(args, "--depth", 3);

        if (!Directory.Exists(path))
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory not found: {path}"));
        }

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
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory not found: {path}"));
        }

        _workingDirectory = path;
        return Task.FromResult(new CopilotCliResult(true, $"Changed directory to: {path}"));
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
        {
            return Task.FromResult(new CopilotCliResult(false, $"Directory not found: {searchPath}"));
        }

        var results = new List<string>();
        FindFiles(searchPath, pattern, results);

        if (results.Count == 0)
        {
            return Task.FromResult(new CopilotCliResult(true, $"No files found matching '{pattern}'"));
        }

        var output = $"Found {results.Count} file(s):\n" + string.Join("\n", results.Select(r => "  " + r));
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
        {
            return new CopilotCliResult(false, $"Directory not found: {searchPath}");
        }

        var results = new List<string>();
        await SearchInFiles(searchPath, searchText, extension, results);

        if (results.Count == 0)
        {
            return new CopilotCliResult(true, $"No matches found for '{searchText}'");
        }

        var output = $"Found {results.Count} match(es):\n" + string.Join("\n", results);
        return new CopilotCliResult(true, output);
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
        if (args.Length > 0 && Commands.TryGetValue(args[0], out var cmd))
        {
            var output = $"Command: {cmd.Name}\n" +
                         $"Description: {cmd.Description}\n" +
                         $"Usage: {cmd.Name} {cmd.Usage}";
            return Task.FromResult(new CopilotCliResult(true, output));
        }

        var lines = new List<string>
        {
            "Copilot CLI Commands:",
            "=====================",
            "",
            "File Operations:",
            "  create, new     - Create a new file or directory",
            "  delete, rm      - Delete a file or directory",
            "  touch           - Create an empty file or update timestamp",
            "  template        - Create file from template",
            "",
            "Directory Operations:",
            "  mkdir           - Create a new directory",
            "  rmdir           - Remove a directory",
            "",
            "File Manipulation:",
            "  rename          - Rename a file or directory",
            "  mv              - Move a file or directory",
            "  copy, cp        - Copy a file or directory",
            "",
            "File Content:",
            "  write           - Write content to a file",
            "  append          - Append content to a file",
            "  read, cat       - Display file content",
            "",
            "Navigation & Listing:",
            "  ls, dir         - List directory contents",
            "  tree            - Display directory tree",
            "  pwd             - Print working directory",
            "  cd              - Change working directory",
            "",
            "Search:",
            "  find            - Find files by name pattern",
            "  search          - Search for text in files",
            "",
            "Information:",
            "  info            - Show file/directory information",
            "  exists          - Check if path exists",
            "  help            - Show this help or command details",
            "",
            "GitHub CLI Commands:",
            "  gh-install      - Install GitHub CLI via winget",
            "  gh-auth         - Authenticate with GitHub",
            "  gh-repo         - Manage repositories",
            "  gh-pr           - Manage pull requests",
            "  gh-issue        - Manage issues",
            "  gh-workflow     - Manage GitHub Actions workflows",
            "  gh-run          - View workflow runs",
            "  gh-cache        - Manage GitHub Actions caches",
            "  gh-release      - Manage releases",
            "  gh-gist         - Manage gists",
            "  gh-browse       - Open repo in browser",
            "  gh-codespace    - Manage codespaces",
            "  gh-org          - Manage organizations",
            "  gh-project      - Work with GitHub Projects",
            "  gh-status       - Show GitHub CLI status",
            "  gh-search       - Search GitHub",
            "  gh-secret       - Manage secrets",
            "  gh-variable     - Manage variables",
            "  gh-label        - Manage labels",
            "  gh-api          - Make GitHub API requests",
            "  gh-extension    - Manage gh extensions",
            "  gh-alias        - Manage command shortcuts",
            "  gh-config       - Manage gh configuration",
            "  gh-copilot      - GitHub Copilot CLI (explain, suggest, config)",
            "",
            "🤖 GitHub Copilot Commands:",
            "  gh-copilot explain \"cmd\" - Get explanation of a command",
            "  gh-copilot suggest \"task\" - Generate command for a task",
            "  gh-t \"task\"              - Run Copilot with project context",
            "  gh-m <model>             - Change Copilot model (gpt-4, etc.)",
            "",
            "🖥️ External Terminal Commands:",
            "  terminal [cmd]      - Open Windows Terminal or CMD with optional command",
            "  cmd-ext [cmd]       - Open external Command Prompt window",
            "  wt [cmd]            - Open Windows Terminal",
            "  powershell-ext [cmd] - Open external PowerShell window",
            "",
            "  ... and 15+ more gh commands!",
            "",
            "Use 'help <command>' for more details on a specific command."
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
    
    private async Task<CopilotCliResult> GhCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, "Usage: gh <command> [args...]\n\nExamples:\n  gh repo view\n  gh pr list\n  gh issue create --title \"Bug fix\"");
        }
        
        var command = string.Join(" ", args);
        var result = await RunGitHubCliAsync(command);
        
        return new CopilotCliResult(result.ExitCode == 0, 
            result.ExitCode == 0 ? result.Output : $"Error: {result.Error}");
    }
    
    private async Task<CopilotCliResult> GhInstallAsync(string[] args)
    {
        // Check if already installed
        var checkResult = await RunGitHubCliAsync("--version");
        if (checkResult.ExitCode == 0)
        {
            return new CopilotCliResult(true, $"✅ GitHub CLI is already installed\n{checkResult.Output}");
        }
        
        // Try to install via winget
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
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                return new CopilotCliResult(true, "✅ GitHub CLI installed successfully!\n\n⚠️ You may need to restart the application or open a new terminal for changes to take effect.\n\nRun 'gh-auth login' to authenticate.");
            }
            else
            {
                return new CopilotCliResult(false, $"❌ Installation failed:\n{error}\n\nTry installing manually:\n  winget install GitHub.cli");
            }
        }
        catch (Exception ex)
        {
            return new CopilotCliResult(false, $"❌ Could not run winget: {ex.Message}\n\nTry installing manually:\n  1. Open PowerShell as Administrator\n  2. Run: winget install GitHub.cli");
        }
    }
    
    private async Task<CopilotCliResult> GhAuthAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        
        switch (action)
        {
            case "login":
                // First check if already logged in
                var checkResult = await RunGitHubCliAsync("auth status");
                if (checkResult.ExitCode == 0)
                {
                    var currentUser = await RunGitHubCliAsync("api user --jq .login");
                    return new CopilotCliResult(true, $"✅ Already logged in as: {currentUser.Output.Trim()}\n\nUse 'gh-auth logout' first if you want to switch accounts.");
                }
                
                // Open browser for authentication - run in separate process
                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = await FindGhPathAsync() ?? "gh",
                        Arguments = "auth login --web -h github.com",
                        UseShellExecute = true, // Opens in new window
                        CreateNoWindow = false
                    };
                    
                    System.Diagnostics.Process.Start(startInfo);
                    
                    return new CopilotCliResult(true, 
                        "🔐 GitHub authentication started!\n\n" +
                        "A terminal window has opened.\n" +
                        "Follow the instructions there to complete authentication.\n\n" +
                        "After completing, run 'gh-auth status' to verify.");
                }
                catch (Exception ex)
                {
                    return new CopilotCliResult(false, $"❌ Could not start authentication: {ex.Message}\n\nTry running manually in terminal:\n  gh auth login");
                }
                
            case "logout":
                var logoutResult = await RunGitHubCliAsync("auth logout -h github.com -y");
                return new CopilotCliResult(logoutResult.ExitCode == 0, 
                    logoutResult.ExitCode == 0 ? "✅ Logged out from GitHub" : $"❌ Logout failed: {logoutResult.Error}");
            
            case "refresh":
                var refreshResult = await RunGitHubCliAsync("auth refresh");
                return new CopilotCliResult(refreshResult.ExitCode == 0, 
                    refreshResult.ExitCode == 0 ? "✅ Authentication refreshed" : $"❌ Refresh failed: {refreshResult.Error}");
            
            case "setup-git":
                var setupResult = await RunGitHubCliAsync("auth setup-git");
                return new CopilotCliResult(setupResult.ExitCode == 0, 
                    setupResult.ExitCode == 0 ? "✅ Git configured to use GitHub CLI as credential helper" : $"❌ Setup failed: {setupResult.Error}");
            
            case "token":
                var tokenResult = await RunGitHubCliAsync("auth token");
                return new CopilotCliResult(tokenResult.ExitCode == 0, 
                    tokenResult.ExitCode == 0 ? $"🔑 GitHub Token:\n{tokenResult.Output}" : $"❌ Failed to get token: {tokenResult.Error}");
                
            case "status":
            default:
                var statusResult = await RunGitHubCliAsync("auth status");
                if (statusResult.ExitCode == 0)
                {
                    var userResult = await RunGitHubCliAsync("api user --jq .login");
                    var user = userResult.ExitCode == 0 ? userResult.Output.Trim() : "unknown";
                    return new CopilotCliResult(true, $"✅ Logged in to GitHub as: {user}\n\n{statusResult.Output}");
                }
                return new CopilotCliResult(false, "❌ Not logged in to GitHub\n\nRun 'gh-auth login' to authenticate.");
        }
    }
    
    private async Task<CopilotCliResult> GhRepoAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, 
                "Usage: gh-repo <action> [args...]\n\n" +
                "Actions:\n" +
                "  create <name> [--private] [--description \"...\"]  - Create new repository\n" +
                "  clone <owner/repo> [path]                         - Clone a repository\n" +
                "  view [--web]                                      - View current repository\n" +
                "  list [--limit N]                                  - List your repositories\n" +
                "  fork <owner/repo>                                 - Fork a repository\n" +
                "  delete <owner/repo>                               - Delete a repository\n" +
                "  archive <owner/repo>                              - Archive a repository\n" +
                "  unarchive <owner/repo>                            - Unarchive a repository\n" +
                "  edit [--description \"...\"]                        - Edit repository settings\n" +
                "  rename <new-name>                                 - Rename repository\n" +
                "  sync                                              - Sync forked repository\n" +
                "  deploy-key <list|add|delete>                      - Manage deploy keys\n" +
                "  garden                                            - Repository garden view\n" +
                "  set-default <owner/repo>                          - Set default repository");
        }
        
        var action = args[0].ToLowerInvariant();
        var restArgs = args.Skip(1).ToArray();
        
        switch (action)
        {
            case "create":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-repo create <name> [--private] [--description \"...\"]");
                
                var repoName = restArgs[0];
                var isPrivate = HasFlag(restArgs, "--private");
                var description = GetArgValue(restArgs, "--description") ?? GetArgValue(restArgs, "-d");
                
                var createArgs = $"repo create {repoName}";
                createArgs += isPrivate ? " --private" : " --public";
                if (!string.IsNullOrEmpty(description))
                    createArgs += $" --description \"{description}\"";
                
                // Add source directory if we have a working directory
                if (!string.IsNullOrEmpty(_workingDirectory))
                    createArgs += $" --source=\"{_workingDirectory}\" --push";
                
                var createResult = await RunGitHubCliAsync(createArgs);
                return new CopilotCliResult(createResult.ExitCode == 0,
                    createResult.ExitCode == 0 
                        ? $"✅ Repository '{repoName}' created!\n{createResult.Output}" 
                        : $"❌ Failed to create repository: {createResult.Error}");
                
            case "clone":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-repo clone <owner/repo> [path]");
                
                var cloneRepo = restArgs[0];
                var clonePath = restArgs.Length > 1 ? restArgs[1] : null;
                
                var cloneArgs = $"repo clone {cloneRepo}";
                if (!string.IsNullOrEmpty(clonePath))
                    cloneArgs += $" \"{clonePath}\"";
                
                var cloneResult = await RunGitHubCliAsync(cloneArgs, _workingDirectory);
                return new CopilotCliResult(cloneResult.ExitCode == 0,
                    cloneResult.ExitCode == 0 
                        ? $"✅ Repository cloned successfully!\n{cloneResult.Output}" 
                        : $"❌ Clone failed: {cloneResult.Error}");
                
            case "view":
                var viewWeb = HasFlag(restArgs, "--web") || HasFlag(restArgs, "-w");
                var viewArgs = viewWeb ? "repo view --web" : "repo view";
                var viewResult = await RunGitHubCliAsync(viewArgs, _workingDirectory);
                return new CopilotCliResult(viewResult.ExitCode == 0,
                    viewResult.ExitCode == 0 
                        ? (viewWeb ? "🌐 Opening repository in browser..." : viewResult.Output)
                        : $"❌ Not a GitHub repository or not logged in: {viewResult.Error}");
                
            case "list":
                var limit = GetArgValue(restArgs, "--limit") ?? "10";
                var listResult = await RunGitHubCliAsync($"repo list --limit {limit}");
                return new CopilotCliResult(listResult.ExitCode == 0,
                    listResult.ExitCode == 0 
                        ? $"📚 Your repositories:\n\n{listResult.Output}" 
                        : $"❌ Failed to list repositories: {listResult.Error}");
            
            case "fork":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-repo fork <owner/repo>");
                var forkResult = await RunGitHubCliAsync($"repo fork {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(forkResult.ExitCode == 0,
                    forkResult.ExitCode == 0 
                        ? $"✅ Repository forked!\n{forkResult.Output}" 
                        : $"❌ Failed to fork: {forkResult.Error}");
            
            case "delete":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-repo delete <owner/repo>");
                var deleteResult = await RunGitHubCliAsync($"repo delete {restArgs[0]} --yes", _workingDirectory);
                return new CopilotCliResult(deleteResult.ExitCode == 0,
                    deleteResult.ExitCode == 0 
                        ? $"✅ Repository deleted" 
                        : $"❌ Failed to delete: {deleteResult.Error}");
            
            case "archive":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-repo archive <owner/repo>");
                var archiveResult = await RunGitHubCliAsync($"repo archive {restArgs[0]} --yes", _workingDirectory);
                return new CopilotCliResult(archiveResult.ExitCode == 0,
                    archiveResult.ExitCode == 0 
                        ? $"✅ Repository archived" 
                        : $"❌ Failed to archive: {archiveResult.Error}");
            
            case "unarchive":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-repo unarchive <owner/repo>");
                var unarchiveResult = await RunGitHubCliAsync($"repo unarchive {restArgs[0]} --yes", _workingDirectory);
                return new CopilotCliResult(unarchiveResult.ExitCode == 0,
                    unarchiveResult.ExitCode == 0 
                        ? $"✅ Repository unarchived" 
                        : $"❌ Failed to unarchive: {unarchiveResult.Error}");
            
            case "edit":
            case "rename":
            case "sync":
            case "deploy-key":
            case "garden":
            case "set-default":
                var ghArgs = "repo " + string.Join(" ", args);
                var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
                return new CopilotCliResult(result.ExitCode == 0,
                    result.ExitCode == 0 
                        ? $"✅ Repository operation completed:\n{result.Output}" 
                        : $"❌ Failed: {result.Error}");
                
            default:
                return new CopilotCliResult(false, $"Unknown action: {action}. Use 'help' for available actions.");
        }
    }
    
    private async Task<CopilotCliResult> GhPrAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, 
                "Usage: gh-pr <action> [args...]\n\n" +
                "Actions:\n" +
                "  create --title \"...\" [--body \"...\"]  - Create a pull request\n" +
                "  list [--state open|closed|merged]     - List pull requests\n" +
                "  view <number> [--web]                 - View a pull request\n" +
                "  checkout <number>                     - Checkout a pull request\n" +
                "  merge <number>                        - Merge a pull request\n" +
                "  close <number>                        - Close a pull request\n" +
                "  reopen <number>                       - Reopen a pull request\n" +
                "  edit <number>                         - Edit a pull request\n" +
                "  review <number>                       - Review a pull request\n" +
                "  comment <number>                      - Comment on a pull request\n" +
                "  diff <number>                         - Show diff\n" +
                "  checks <number>                       - Show checks status\n" +
                "  ready <number>                        - Mark as ready for review\n" +
                "  status                                - Show status");
        }
        
        var action = args[0].ToLowerInvariant();
        var restArgs = args.Skip(1).ToArray();
        
        switch (action)
        {
            case "create":
                var title = GetArgValue(restArgs, "--title") ?? GetArgValue(restArgs, "-t");
                if (string.IsNullOrEmpty(title))
                    return new CopilotCliResult(false, "Usage: gh-pr create --title \"PR title\" [--body \"description\"]");
                
                var body = GetArgValue(restArgs, "--body") ?? GetArgValue(restArgs, "-b");
                var prCreateArgs = $"pr create --title \"{title}\"";
                if (!string.IsNullOrEmpty(body))
                    prCreateArgs += $" --body \"{body}\"";
                
                var prCreateResult = await RunGitHubCliAsync(prCreateArgs, _workingDirectory);
                return new CopilotCliResult(prCreateResult.ExitCode == 0,
                    prCreateResult.ExitCode == 0 
                        ? $"✅ Pull request created!\n{prCreateResult.Output}" 
                        : $"❌ Failed to create PR: {prCreateResult.Error}");
                
            case "list":
                var state = GetArgValue(restArgs, "--state") ?? "open";
                var prListResult = await RunGitHubCliAsync($"pr list --state {state}", _workingDirectory);
                return new CopilotCliResult(prListResult.ExitCode == 0,
                    prListResult.ExitCode == 0 
                        ? (string.IsNullOrWhiteSpace(prListResult.Output) ? $"📋 No {state} pull requests" : $"📋 Pull Requests ({state}):\n\n{prListResult.Output}")
                        : $"❌ Failed to list PRs: {prListResult.Error}");
                
            case "view":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-pr view <number> [--web]");
                var prNumber = restArgs[0];
                var prWeb = HasFlag(restArgs, "--web") || HasFlag(restArgs, "-w");
                var prViewArgs = prWeb ? $"pr view {prNumber} --web" : $"pr view {prNumber}";
                var prViewResult = await RunGitHubCliAsync(prViewArgs, _workingDirectory);
                return new CopilotCliResult(prViewResult.ExitCode == 0,
                    prViewResult.ExitCode == 0 
                        ? (prWeb ? "🌐 Opening PR in browser..." : prViewResult.Output)
                        : $"❌ Failed to view PR: {prViewResult.Error}");
                
            case "checkout":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-pr checkout <number>");
                var prCheckoutResult = await RunGitHubCliAsync($"pr checkout {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(prCheckoutResult.ExitCode == 0,
                    prCheckoutResult.ExitCode == 0 
                        ? $"✅ Checked out PR #{restArgs[0]}" 
                        : $"❌ Failed to checkout PR: {prCheckoutResult.Error}");
            
            case "merge":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-pr merge <number> [--merge|--squash|--rebase]");
                var prMergeResult = await RunGitHubCliAsync($"pr merge {string.Join(" ", restArgs)}", _workingDirectory);
                return new CopilotCliResult(prMergeResult.ExitCode == 0,
                    prMergeResult.ExitCode == 0 
                        ? $"✅ PR #{restArgs[0]} merged!" 
                        : $"❌ Failed to merge PR: {prMergeResult.Error}");
            
            case "close":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-pr close <number>");
                var prCloseResult = await RunGitHubCliAsync($"pr close {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(prCloseResult.ExitCode == 0,
                    prCloseResult.ExitCode == 0 
                        ? $"✅ PR #{restArgs[0]} closed" 
                        : $"❌ Failed to close PR: {prCloseResult.Error}");
            
            case "reopen":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-pr reopen <number>");
                var prReopenResult = await RunGitHubCliAsync($"pr reopen {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(prReopenResult.ExitCode == 0,
                    prReopenResult.ExitCode == 0 
                        ? $"✅ PR #{restArgs[0]} reopened" 
                        : $"❌ Failed to reopen PR: {prReopenResult.Error}");
            
            case "edit":
            case "review":
            case "comment":
            case "diff":
            case "checks":
            case "ready":
            case "status":
                var ghArgs = "pr " + string.Join(" ", args);
                var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
                return new CopilotCliResult(result.ExitCode == 0,
                    result.ExitCode == 0 
                        ? $"✅ PR operation completed:\n{result.Output}" 
                        : $"❌ Failed: {result.Error}");
                
            default:
                return new CopilotCliResult(false, $"Unknown action: {action}. Use 'help' for available actions.");
        }
    }
    
    private async Task<CopilotCliResult> GhIssueAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, 
                "Usage: gh-issue <action> [args...]\n\n" +
                "Actions:\n" +
                "  create --title \"...\" [--body \"...\"]  - Create an issue\n" +
                "  list [--state open|closed|all]        - List issues\n" +
                "  view <number> [--web]                 - View an issue\n" +
                "  close <number>                        - Close an issue\n" +
                "  reopen <number>                       - Reopen an issue\n" +
                "  edit <number>                         - Edit an issue\n" +
                "  delete <number>                       - Delete an issue\n" +
                "  comment <number>                      - Comment on an issue\n" +
                "  status                                - Show status\n" +
                "  transfer <number> <repo>              - Transfer an issue\n" +
                "  lock <number>                         - Lock conversation\n" +
                "  unlock <number>                       - Unlock conversation\n" +
                "  pin <number>                          - Pin an issue\n" +
                "  unpin <number>                        - Unpin an issue\n" +
                "  develop <number>                      - Open in codespace");
        }
        
        var action = args[0].ToLowerInvariant();
        var restArgs = args.Skip(1).ToArray();
        
        switch (action)
        {
            case "create":
                var title = GetArgValue(restArgs, "--title") ?? GetArgValue(restArgs, "-t");
                if (string.IsNullOrEmpty(title))
                    return new CopilotCliResult(false, "Usage: gh-issue create --title \"Issue title\" [--body \"description\"]");
                
                var body = GetArgValue(restArgs, "--body") ?? GetArgValue(restArgs, "-b");
                var issueCreateArgs = $"issue create --title \"{title}\"";
                if (!string.IsNullOrEmpty(body))
                    issueCreateArgs += $" --body \"{body}\"";
                
                var issueCreateResult = await RunGitHubCliAsync(issueCreateArgs, _workingDirectory);
                return new CopilotCliResult(issueCreateResult.ExitCode == 0,
                    issueCreateResult.ExitCode == 0 
                        ? $"✅ Issue created!\n{issueCreateResult.Output}" 
                        : $"❌ Failed to create issue: {issueCreateResult.Error}");
                
            case "list":
                var state = GetArgValue(restArgs, "--state") ?? "open";
                var issueListResult = await RunGitHubCliAsync($"issue list --state {state}", _workingDirectory);
                return new CopilotCliResult(issueListResult.ExitCode == 0,
                    issueListResult.ExitCode == 0 
                        ? (string.IsNullOrWhiteSpace(issueListResult.Output) ? "📋 No issues found" : $"📋 Issues ({state}):\n\n{issueListResult.Output}")
                        : $"❌ Failed to list issues: {issueListResult.Error}");
                
            case "view":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-issue view <number> [--web]");
                var issueNumber = restArgs[0];
                var issueWeb = HasFlag(restArgs, "--web") || HasFlag(restArgs, "-w");
                var issueViewArgs = issueWeb ? $"issue view {issueNumber} --web" : $"issue view {issueNumber}";
                var issueViewResult = await RunGitHubCliAsync(issueViewArgs, _workingDirectory);
                return new CopilotCliResult(issueViewResult.ExitCode == 0,
                    issueViewResult.ExitCode == 0 
                        ? (issueWeb ? "🌐 Opening issue in browser..." : issueViewResult.Output)
                        : $"❌ Failed to view issue: {issueViewResult.Error}");
                
            case "close":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-issue close <number>");
                var issueCloseResult = await RunGitHubCliAsync($"issue close {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(issueCloseResult.ExitCode == 0,
                    issueCloseResult.ExitCode == 0 
                        ? $"✅ Issue #{restArgs[0]} closed" 
                        : $"❌ Failed to close issue: {issueCloseResult.Error}");
            
            case "reopen":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-issue reopen <number>");
                var issueReopenResult = await RunGitHubCliAsync($"issue reopen {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(issueReopenResult.ExitCode == 0,
                    issueReopenResult.ExitCode == 0 
                        ? $"✅ Issue #{restArgs[0]} reopened" 
                        : $"❌ Failed to reopen issue: {issueReopenResult.Error}");
            
            case "edit":
            case "delete":
            case "comment":
            case "transfer":
            case "lock":
            case "unlock":
            case "pin":
            case "unpin":
            case "develop":
            case "status":
                var ghArgs = "issue " + string.Join(" ", args);
                var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
                return new CopilotCliResult(result.ExitCode == 0,
                    result.ExitCode == 0 
                        ? $"✅ Issue operation completed:\n{result.Output}" 
                        : $"❌ Failed: {result.Error}");
                
            default:
                return new CopilotCliResult(false, $"Unknown action: {action}. Use 'help' for available actions.");
        }
    }
    
    private async Task<CopilotCliResult> GhWorkflowAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return new CopilotCliResult(false, 
                "Usage: gh-workflow <action> [args...]\n\n" +
                "Actions:\n" +
                "  list                     - List workflows\n" +
                "  view <workflow> [--web]  - View workflow runs\n" +
                "  run <workflow>           - Run a workflow\n" +
                "  enable <workflow>        - Enable a workflow\n" +
                "  disable <workflow>       - Disable a workflow");
        }
        
        var action = args[0].ToLowerInvariant();
        var restArgs = args.Skip(1).ToArray();
        
        switch (action)
        {
            case "list":
                var wfListResult = await RunGitHubCliAsync("workflow list", _workingDirectory);
                return new CopilotCliResult(wfListResult.ExitCode == 0,
                    wfListResult.ExitCode == 0 
                        ? (string.IsNullOrWhiteSpace(wfListResult.Output) ? "⚙️ No workflows found" : $"⚙️ Workflows:\n\n{wfListResult.Output}")
                        : $"❌ Failed to list workflows: {wfListResult.Error}");
                
            case "run":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-workflow run <workflow-name>");
                var wfRunResult = await RunGitHubCliAsync($"workflow run {string.Join(" ", restArgs)}", _workingDirectory);
                return new CopilotCliResult(wfRunResult.ExitCode == 0,
                    wfRunResult.ExitCode == 0 
                        ? $"✅ Workflow '{restArgs[0]}' triggered!" 
                        : $"❌ Failed to run workflow: {wfRunResult.Error}");
                
            case "view":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-workflow view <workflow-name> [--web]");
                var wfWeb = HasFlag(restArgs, "--web") || HasFlag(restArgs, "-w");
                var wfViewArgs = wfWeb ? $"workflow view {restArgs[0]} --web" : $"workflow view {restArgs[0]}";
                var wfViewResult = await RunGitHubCliAsync(wfViewArgs, _workingDirectory);
                return new CopilotCliResult(wfViewResult.ExitCode == 0,
                    wfViewResult.ExitCode == 0 
                        ? (wfWeb ? "🌐 Opening workflow in browser..." : $"⚙️ Workflow:\n\n{wfViewResult.Output}")
                        : $"❌ Failed to view workflow: {wfViewResult.Error}");
            
            case "enable":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-workflow enable <workflow-name>");
                var wfEnableResult = await RunGitHubCliAsync($"workflow enable {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(wfEnableResult.ExitCode == 0,
                    wfEnableResult.ExitCode == 0 
                        ? $"✅ Workflow '{restArgs[0]}' enabled" 
                        : $"❌ Failed to enable workflow: {wfEnableResult.Error}");
            
            case "disable":
                if (restArgs.Length == 0)
                    return new CopilotCliResult(false, "Usage: gh-workflow disable <workflow-name>");
                var wfDisableResult = await RunGitHubCliAsync($"workflow disable {restArgs[0]}", _workingDirectory);
                return new CopilotCliResult(wfDisableResult.ExitCode == 0,
                    wfDisableResult.ExitCode == 0 
                        ? $"✅ Workflow '{restArgs[0]}' disabled" 
                        : $"❌ Failed to disable workflow: {wfDisableResult.Error}");
                
            default:
                return new CopilotCliResult(false, $"Unknown action: {action}. Use 'list', 'view', 'run', 'enable', or 'disable'.");
        }
    }
    
    private async Task<CopilotCliResult> GhStatusAsync(string[] args)
    {
        var versionResult = await RunGitHubCliAsync("--version");
        var installed = versionResult.ExitCode == 0;
        
        if (!installed)
        {
            return new CopilotCliResult(false, 
                "❌ GitHub CLI is not installed\n\n" +
                "To install, run:\n  gh-install\n\n" +
                "Or install manually:\n  winget install GitHub.cli");
        }
        
        var authResult = await RunGitHubCliAsync("auth status");
        var authenticated = authResult.ExitCode == 0;
        
        var output = $"📊 GitHub CLI Status\n" +
                    $"━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"✅ Installed: {versionResult.Output.Split('\n')[0]}\n";
        
        if (authenticated)
        {
            var userResult = await RunGitHubCliAsync("api user --jq .login");
            var user = userResult.ExitCode == 0 ? userResult.Output.Trim() : "unknown";
            output += $"✅ Authenticated as: {user}\n";
        }
        else
        {
            output += "❌ Not authenticated\n   Run 'gh-auth login' to sign in\n";
        }
        
        if (!string.IsNullOrEmpty(_workingDirectory))
        {
            output += $"\n📁 Working directory:\n   {_workingDirectory}\n";
            
            // Check if it's a git repo
            var repoResult = await RunGitHubCliAsync("repo view --json name,owner --jq \".owner.login + \\\"/\\\" + .name\"", _workingDirectory);
            if (repoResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(repoResult.Output))
            {
                output += $"📦 Repository: {repoResult.Output.Trim()}\n";
            }
        }
        
        return new CopilotCliResult(true, output);
    }

    private async Task<CopilotCliResult> GhBrowseAsync(string[] args)
    {
        var ghArgs = "browse " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? "🌐 Opening repository in browser..." 
                : $"❌ Failed to open repository: {result.Error}");
    }

    private async Task<CopilotCliResult> GhCodespaceAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "codespace list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"💻 Codespaces:\n\n{result.Output}" 
                    : $"❌ Failed to list codespaces: {result.Error}");
        }
        
        var ghArgs2 = "codespace " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Codespace operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhGistAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "gist list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"📝 Gists:\n\n{result.Output}" 
                    : $"❌ Failed to list gists: {result.Error}");
        }
        
        var ghArgs2 = "gist " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Gist operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhOrgAsync(string[] args)
    {
        var ghArgs = "org " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? $"🏢 Organizations:\n\n{result.Output}" 
                : $"❌ Failed: {result.Error}");
    }

    private async Task<CopilotCliResult> GhProjectAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "project list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"📋 Projects:\n\n{result.Output}" 
                    : $"❌ Failed to list projects: {result.Error}");
        }
        
        var ghArgs2 = "project " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Project operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhReleaseAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "release list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"🚀 Releases:\n\n{result.Output}" 
                    : $"❌ Failed to list releases: {result.Error}");
        }
        
        var ghArgs2 = "release " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Release operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhCacheAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "cache list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"💾 Caches:\n\n{result.Output}" 
                    : $"❌ Failed to list caches: {result.Error}");
        }
        
        var ghArgs2 = "cache " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Cache operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhRunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "run list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"🏃 Workflow runs:\n\n{result.Output}" 
                    : $"❌ Failed to list runs: {result.Error}");
        }
        
        var ghArgs2 = "run " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Run operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhCoAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, "Usage: gh-co <pr-number>");
        
        var ghArgs = "pr checkout " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? $"✅ Checked out PR #{args[0]}" 
                : $"❌ Failed to checkout PR: {result.Error}");
    }

    private async Task<CopilotCliResult> GhAgentTaskAsync(string[] args)
    {
        var ghArgs = "agent-task " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? $"🤖 Agent task:\n{result.Output}" 
                : $"❌ Failed: {result.Error}");
    }

    private async Task<CopilotCliResult> GhAliasAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "alias list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"🔗 Aliases:\n\n{result.Output}" 
                    : $"❌ Failed to list aliases: {result.Error}");
        }
        
        var ghArgs2 = "alias " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Alias operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhApiAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, "Usage: gh-api <endpoint> [flags...]");
        
        var ghArgs = "api " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? $"📡 API Response:\n{result.Output}" 
                : $"❌ API request failed: {result.Error}");
    }

    private async Task<CopilotCliResult> GhAttestationAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, "Usage: gh-attestation <verify|download|inspect|tuf-root-verify> [args...]");
        
        var ghArgs = "attestation " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? $"🔐 Attestation:\n{result.Output}" 
                : $"❌ Failed: {result.Error}");
    }

    private async Task<CopilotCliResult> GhCompletionAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, "Usage: gh-completion <bash|zsh|fish|powershell>");
        
        var ghArgs = "completion " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 
                ? $"✅ Shell completion script generated:\n{result.Output}" 
                : $"❌ Failed: {result.Error}");
    }

    private async Task<CopilotCliResult> GhConfigAsync(string[] args)
    {
        if (args.Length == 0)
        {
            var ghArgs = "config list";
            var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
            return new CopilotCliResult(result.ExitCode == 0,
                result.ExitCode == 0 
                    ? $"⚙️ Configuration:\n\n{result.Output}" 
                    : $"❌ Failed to list config: {result.Error}");
        }
        
        var ghArgs2 = "config " + string.Join(" ", args);
        var result2 = await RunGitHubCliAsync(ghArgs2, _workingDirectory);
        return new CopilotCliResult(result2.ExitCode == 0,
            result2.ExitCode == 0 
                ? $"✅ Config operation completed:\n{result2.Output}" 
                : $"❌ Failed: {result2.Error}");
    }

    private async Task<CopilotCliResult> GhCopilotAsync(string[] args)
    {
        if (args.Length == 0)
        {
            // NOTE: `gh copilot` does NOT provide a standalone interactive TUI when run without subcommands.
            // In our app, we treat `gh-copilot` (no args) as a UX shortcut: we guide the user and open the help
            // inside the embedded terminal (or fall back to a new console window).
            return await RunGitHubCopilotInteractiveAsync();
        }

        var subCommand = args[0].ToLowerInvariant();
        
        // Interactive TUI commands (suggest, explain) require a real terminal with proper PTY support.
        // These commands don't work well in embedded terminals due to size constraints and input handling.
        // Launch them in an external terminal window instead.
        if (subCommand == "suggest" || subCommand == "explain")
        {
            return await RunGitHubCopilotInExternalTerminalAsync(args);
        }

        // Non-interactive commands (config, etc.) can run in the embedded terminal
        var ghArgs = "copilot " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0
                ? $"🤖 GitHub Copilot:\n{result.Output}"
                : $"❌ Failed: {result.Error}");
    }
    
    /// <summary>
    /// Runs interactive GitHub Copilot commands (suggest, explain) in an external terminal window.
    /// These commands require a full PTY terminal for their TUI to work correctly.
    /// </summary>
    private async Task<CopilotCliResult> RunGitHubCopilotInExternalTerminalAsync(string[] args)
    {
        try
        {
            var ghPath = await FindGhPathAsync();
            if (string.IsNullOrEmpty(ghPath))
            {
                return new CopilotCliResult(false,
                    "❌ GitHub CLI (gh) not found. Run 'gh-install' to install it.");
            }

            // Перевіряємо чи встановлено розширення copilot
            var checkExtension = await RunGitHubCliAsync("extension list");
            if (!checkExtension.Output.Contains("copilot", StringComparison.OrdinalIgnoreCase))
            {
                return new CopilotCliResult(false,
                    "❌ GitHub Copilot extension is not installed.\n\n" +
                    "To install it, run:\n" +
                    "  gh-extension install github/gh-copilot\n\n" +
                    "Or manually:\n" +
                    "  gh extension install github/gh-copilot");
            }

            var ghArgs = "copilot " + string.Join(" ", args);
            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            
            OutputGenerated?.Invoke(this,
                $"🚀 Launching `gh {ghArgs}` in external terminal...\n" +
                $"📁 Working directory: {workDir}\n\n" +
                "ℹ️ Interactive Copilot commands require a real Windows terminal.\n\n");

            // Use Windows Terminal (wt.exe) if available, otherwise fall back to cmd.exe
            // cmd.exe provides better TTY support than PowerShell for interactive CLI tools
            var wtPath = FindWindowsTerminal();
            
            // Build command with full path to gh.exe
            var ghFullCommand = $"\"{ghPath}\" {ghArgs}";
            
            System.Diagnostics.ProcessStartInfo startInfo;
            string terminalName;
            
            if (!string.IsNullOrEmpty(wtPath))
            {
                // Windows Terminal provides the best experience for interactive CLIs
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{workDir}\" --title \"GitHub Copilot CLI\" cmd /k {ghFullCommand}",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Windows Terminal";
            }
            else
            {
                // Fallback to cmd.exe
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d \"{workDir}\" && {ghFullCommand}",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = workDir
                };
                terminalName = "Command Prompt";
            }

            System.Diagnostics.Process.Start(startInfo);
            
            return new CopilotCliResult(true, 
                $"✅ Opened `gh {ghArgs}` in {terminalName}.\n\n" +
                "💡 Tip: After using Copilot, you can copy the suggested command and paste it back here.");
        }
        catch (Exception ex)
        {
            return new CopilotCliResult(false,
                $"❌ Error starting GitHub Copilot: {ex.Message}\n\n" +
                "Make sure GitHub CLI and Copilot extension are properly installed:\n" +
                "  1. gh-install\n" +
                "  2. gh-auth login\n" +
                "  3. gh-extension install github/gh-copilot");
        }
    }

    /// <summary>
    /// Run GitHub Copilot UX inside the editor.
    /// GitHub Copilot CLI doesn't have a true interactive mode without subcommands.
    /// So we:
    ///  1) validate gh + extension,
    ///  2) emit guidance,
    ///  3) ask the host UI to run a helpful command inside the embedded terminal session.
    /// </summary>
    private async Task<CopilotCliResult> RunGitHubCopilotInteractiveAsync()
    {
        try
        {
            var ghPath = await FindGhPathAsync();
            if (string.IsNullOrEmpty(ghPath))
            {
                return new CopilotCliResult(false,
                    "❌ GitHub CLI (gh) not found. Run 'gh-install' to install it.");
            }

            // Перевіряємо чи встановлено розширення copilot
            var checkExtension = await RunGitHubCliAsync("extension list");
            if (!checkExtension.Output.Contains("copilot", StringComparison.OrdinalIgnoreCase))
            {
                return new CopilotCliResult(false,
                    "❌ GitHub Copilot extension is not installed.\n\n" +
                    "To install it, run:\n" +
                    "  gh-extension install github/gh-copilot\n\n" +
                    "Or manually:\n" +
                    "  gh extension install github/gh-copilot");
            }

            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            
            OutputGenerated?.Invoke(this,
                "🚀 Launching GitHub Copilot CLI in external terminal...\n\n");

            // Use Windows Terminal (wt.exe) if available, otherwise fall back to cmd.exe
            var wtPath = FindWindowsTerminal();
            
            // Build welcome message and start gh copilot suggest interactively
            var welcomeCmd = $"echo. && echo ╔══════════════════════════════════════════════════════════════╗ && " +
                            $"echo ║            🤖 GitHub Copilot CLI - Interactive Mode          ║ && " +
                            $"echo ╚══════════════════════════════════════════════════════════════╝ && " +
                            $"echo. && echo 📁 Working directory: {workDir.Replace("\\", "/")} && " +
                            $"echo 🔧 gh path: {ghPath} && echo. && " +
                            $"echo 💡 Commands: && " +
                            $"echo    \"{ghPath}\" copilot suggest \"your question\" && " +
                            $"echo    \"{ghPath}\" copilot explain \"command to explain\" && " +
                            $"echo. && echo ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ && echo.";
            
            System.Diagnostics.ProcessStartInfo startInfo;
            string terminalName;
            
            if (!string.IsNullOrEmpty(wtPath))
            {
                // Windows Terminal provides the best experience for interactive CLIs
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = $"-d \"{workDir}\" --title \"GitHub Copilot CLI\" cmd /k \"{welcomeCmd}\"",
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Windows Terminal";
            }
            else
            {
                // Fallback to cmd.exe
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d \"{workDir}\" && {welcomeCmd}",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = workDir
                };
                terminalName = "Command Prompt";
            }

            System.Diagnostics.Process.Start(startInfo);
            return new CopilotCliResult(true, 
                $"✅ Opened GitHub Copilot CLI in {terminalName}.\n\n" +
                $"📁 Working directory: {workDir}\n" +
                $"🔧 gh path: {ghPath}\n\n" +
                "💡 In the terminal, run:\n" +
                $"   \"{ghPath}\" copilot suggest \"your question\"\n" +
                $"   \"{ghPath}\" copilot explain \"command to explain\"");
        }
        catch (Exception ex)
        {
            return new CopilotCliResult(false,
                $"❌ Error starting GitHub Copilot: {ex.Message}\n\n" +
                "Make sure GitHub CLI and Copilot extension are properly installed:\n" +
                "  1. gh-install\n" +
                "  2. gh-auth login\n" +
                "  3. gh-extension install github/gh-copilot");
        }
    }

    // -----------------------------
    // Missing gh-* handlers (forwarders)
    // -----------------------------

    private async Task<CopilotCliResult> GhCopilotTaskAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false,
                "Usage: gh-t \"<task description>\"\n\nExample:\n  gh-t \"Explain this compiler error\"\n  gh-t \"Generate a build command for this project\"");

        // Keep it simple: use Copilot suggest for shell commands.
        var taskDescription = string.Join(" ", args).Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(taskDescription))
            return new CopilotCliResult(false, "Task description cannot be empty");

        var ghArgs = $"copilot suggest -t shell \"{taskDescription.Replace("\"", "\\\"")}\"";
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 ? $"🤖 Copilot suggestion:\n{result.Output}" : $"❌ Copilot task failed: {result.Error}\n{result.Output}");
    }

    private async Task<CopilotCliResult> GhCopilotModelAsync(string[] args)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, "Usage: gh-m <model name>\n\nExample:\n  gh-m gpt-4o\n  gh-m gpt-4.1");

        var model = string.Join(" ", args).Trim();
        var ghArgs = $"copilot config set model {model}";
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 ? $"✅ Copilot model updated.\n{result.Output}" : $"❌ Failed to set model: {result.Error}\n{result.Output}");
    }

    private Task<CopilotCliResult> GhExtensionAsync(string[] args) =>
        GhPassthroughAsync("extension", args, "Usage: gh-extension <list|install|upgrade|remove|create|search|browse|exec> [args...]");

    private Task<CopilotCliResult> GhGpgKeyAsync(string[] args) =>
        GhPassthroughAsync("gpg-key", args, "Usage: gh-gpg-key <list|add|delete> [args...]");

    private Task<CopilotCliResult> GhLabelAsync(string[] args) =>
        GhPassthroughAsync("label", args, "Usage: gh-label <list|create|edit|delete|clone> [args...]");

    private Task<CopilotCliResult> GhPreviewAsync(string[] args) =>
        GhPassthroughAsync("preview", args, "Usage: gh-preview [args...]");

    private Task<CopilotCliResult> GhRulesetAsync(string[] args) =>
        GhPassthroughAsync("ruleset", args, "Usage: gh-ruleset <list|view|check> [args...]");

    private Task<CopilotCliResult> GhSearchAsync(string[] args) =>
        GhPassthroughAsync("search", args, "Usage: gh-search <repos|issues|prs|code|commits> [args...]");

    private Task<CopilotCliResult> GhSecretAsync(string[] args) =>
        GhPassthroughAsync("secret", args, "Usage: gh-secret <list|set|remove> [--org|--env|--user] [args...]");

    private Task<CopilotCliResult> GhSshKeyAsync(string[] args) =>
        GhPassthroughAsync("ssh-key", args, "Usage: gh-ssh-key <list|add|delete> [args...]");

    private Task<CopilotCliResult> GhVariableAsync(string[] args) =>
        GhPassthroughAsync("variable", args, "Usage: gh-variable <list|set|get|delete> [--org|--env] [args...]");

    private async Task<CopilotCliResult> GhPassthroughAsync(string subcommand, string[] args, string usage)
    {
        if (args.Length == 0)
            return new CopilotCliResult(false, usage);

        var ghArgs = subcommand + " " + string.Join(" ", args);
        var result = await RunGitHubCliAsync(ghArgs, _workingDirectory);
        return new CopilotCliResult(result.ExitCode == 0,
            result.ExitCode == 0 ? result.Output : $"❌ Failed: {result.Error}\n{result.Output}");
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGitHubCliAsync(string arguments, string? workingDirectory = null)
    {
        try
        {
            var ghPath = await FindGhPathAsync();
            if (string.IsNullOrEmpty(ghPath))
            {
                return (-1, "", "GitHub CLI (gh) not found. Run 'gh-install' to install it.");
            }
            
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
        // Try common locations
        var possiblePaths = new[]
        {
            "gh", // In PATH
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
                            FileName = "gh",
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    testProcess.Start();
                    await testProcess.WaitForExitAsync();
                    if (testProcess.ExitCode == 0)
                        return "gh";
                }
                catch
                {
                    continue;
                }
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Finds Windows Terminal (wt.exe) if installed.
    /// Returns the path to wt.exe or null if not found.
    /// </summary>
    private static string? FindWindowsTerminal()
    {
        // Check if wt.exe is in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var path in pathEnv.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var wtPath = Path.Combine(path.Trim(), "wt.exe");
            if (File.Exists(wtPath))
                return wtPath;
        }
        
        // Check common installation locations
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var possiblePaths = new[]
        {
            Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe"),
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        return null;
    }

    #endregion
    
    #region External Terminal Commands
    
    /// <summary>
    /// Opens an external Windows Terminal or CMD window
    /// </summary>
    private Task<CopilotCliResult> OpenExternalTerminalAsync(string[] args)
    {
        try
        {
            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            var command = args.Length > 0 ? string.Join(" ", args) : null;
            
            var wtPath = FindWindowsTerminal();
            
            System.Diagnostics.ProcessStartInfo startInfo;
            string terminalName;
            
            if (!string.IsNullOrEmpty(wtPath))
            {
                // Windows Terminal available
                var wtArgs = $"-d \"{workDir}\"";
                if (!string.IsNullOrEmpty(command))
                    wtArgs += $" cmd /k {command}";
                
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wtPath,
                    Arguments = wtArgs,
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Windows Terminal";
            }
            else
            {
                // Fallback to cmd.exe
                var cmdArgs = string.IsNullOrEmpty(command) 
                    ? $"/k cd /d \"{workDir}\"" 
                    : $"/k cd /d \"{workDir}\" && {command}";
                
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    WorkingDirectory = workDir
                };
                terminalName = "Command Prompt";
            }
            
            System.Diagnostics.Process.Start(startInfo);
            
            var message = $"✅ Opened {terminalName}\n" +
                         $"📁 Working directory: {workDir}\n";
            if (!string.IsNullOrEmpty(command))
                message += $"🔧 Command: {command}\n";
            
            return Task.FromResult(new CopilotCliResult(true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CopilotCliResult(false, 
                $"❌ Error opening terminal: {ex.Message}"));
        }
    }
    
    /// <summary>
    /// Opens an external PowerShell window
    /// </summary>
    private Task<CopilotCliResult> OpenExternalPowerShellAsync(string[] args)
    {
        try
        {
            var workDir = _workingDirectory ?? Environment.CurrentDirectory;
            var command = args.Length > 0 ? string.Join(" ", args) : null;
            
            string pwshArgs;
            if (string.IsNullOrEmpty(command))
            {
                pwshArgs = $"-NoExit -Command \"Set-Location '{workDir}'\"";
            }
            else
            {
                pwshArgs = $"-NoExit -Command \"Set-Location '{workDir}'; {command}\"";
            }
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = pwshArgs,
                UseShellExecute = true,
                WorkingDirectory = workDir
            };
            
            System.Diagnostics.Process.Start(startInfo);
            
            var message = $"✅ Opened PowerShell\n" +
                         $"📁 Working directory: {workDir}\n";
            if (!string.IsNullOrEmpty(command))
                message += $"🔧 Command: {command}\n";
            
            return Task.FromResult(new CopilotCliResult(true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CopilotCliResult(false, 
                $"❌ Error opening PowerShell: {ex.Message}"));
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
