using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Insait_Edit_C_Sharp.Models;

/// <summary>
/// Enumeration of file tree item types for professional visualization
/// </summary>
public enum FileTreeItemType
{
    // Root items
    Solution,
    SolutionFolder,
    Project,
    
    // Folders
    Folder,
    SpecialFolder,      // Properties, wwwroot, etc.
    DependenciesFolder, // NuGet/Package references
    
    // C# code files
    CSharpFile,
    CSharpClass,
    CSharpInterface,
    CSharpRecord,
    CSharpStruct,
    CSharpEnum,
    CSharpDelegate,
    
    // UI files
    AxamlWindow,
    AxamlUserControl,
    AxamlPage,
    AxamlResourceDict,
    AxamlStyles,
    XamlFile,
    RazorComponent,
    RazorPage,
    RazorView,
    
    // Web files
    HtmlFile,
    CssFile,
    ScssFile,
    JavaScriptFile,
    TypeScriptFile,
    
    // Config files
    JsonFile,
    XmlFile,
    YamlFile,
    EditorConfig,
    AppSettings,
    LaunchSettings,
    
    // Project files
    CsProjFile,
    NugetConfig,
    DirectoryBuildProps,
    DirectoryBuildTargets,
    GlobalJson,
    
    // Other
    MarkdownFile,
    TextFile,
    ImageFile,
    FontFile,
    BinaryFile,
    UnknownFile,
    
    // Special nested items
    CodeBehind,        // .axaml.cs, .razor.cs, etc.
    DesignerFile,      // .Designer.cs
    GeneratedFile      // Auto-generated files
}

/// <summary>
/// Represents a file or folder in the file tree with professional IDE-like visualization
/// </summary>
public class FileTreeItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _fullPath = string.Empty;
    private bool _isDirectory;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservableCollection<FileTreeItem> _children;
    private bool _isLoaded;
    private FileTreeItemType _itemType = FileTreeItemType.UnknownFile;
    private FileTreeItem? _parentItem;
    private bool _isCodeBehind;
    private string? _associatedFile;
    private string? _description;
    private string? _projectGuid;
    private bool _isSolutionItem;
    private string? _targetFramework;

    public FileTreeItem()
    {
        _children = new ObservableCollection<FileTreeItem>();
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string FullPath
    {
        get => _fullPath;
        set
        {
            if (SetProperty(ref _fullPath, value))
            {
                UpdateItemType();
                OnPropertyChanged(nameof(Extension));
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IconColor));
                OnPropertyChanged(nameof(IconBackgroundColor));
                OnPropertyChanged(nameof(NameColor));
                OnPropertyChanged(nameof(FontWeight));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public bool IsDirectory
    {
        get => _isDirectory;
        set
        {
            if (SetProperty(ref _isDirectory, value))
            {
                UpdateItemType();
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IconColor));
                OnPropertyChanged(nameof(IconBackgroundColor));
                OnPropertyChanged(nameof(NameColor));
                OnPropertyChanged(nameof(FontWeight));
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                if (value && IsDirectory && !_isLoaded)
                {
                    LoadChildren();
                }
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ObservableCollection<FileTreeItem> Children
    {
        get => _children;
        set => SetProperty(ref _children, value);
    }

    public FileTreeItemType ItemType
    {
        get => _itemType;
        set
        {
            if (SetProperty(ref _itemType, value))
            {
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IconColor));
                OnPropertyChanged(nameof(IconBackgroundColor));
                OnPropertyChanged(nameof(NameColor));
                OnPropertyChanged(nameof(FontWeight));
            }
        }
    }

    public FileTreeItem? ParentItem
    {
        get => _parentItem;
        set => SetProperty(ref _parentItem, value);
    }

    public bool IsCodeBehind
    {
        get => _isCodeBehind;
        set => SetProperty(ref _isCodeBehind, value);
    }

    public string? AssociatedFile
    {
        get => _associatedFile;
        set => SetProperty(ref _associatedFile, value);
    }

    public string? Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value))
            {
                OnPropertyChanged(nameof(HasDescription));
            }
        }
    }

    public string? ProjectGuid
    {
        get => _projectGuid;
        set => SetProperty(ref _projectGuid, value);
    }

    public bool IsSolutionItem
    {
        get => _isSolutionItem;
        set => SetProperty(ref _isSolutionItem, value);
    }

    public string? TargetFramework
    {
        get => _targetFramework;
        set
        {
            if (SetProperty(ref _targetFramework, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public bool HasDescription => !string.IsNullOrEmpty(Description);

    public string DisplayName
    {
        get
        {
            if (ItemType == FileTreeItemType.CodeBehind && !string.IsNullOrEmpty(_associatedFile))
            {
                var mainFileName = Path.GetFileName(_associatedFile);
                if (_name.StartsWith(mainFileName))
                {
                    return _name.Substring(mainFileName.Length);
                }
            }
            return _name;
        }
    }

    public string Icon => GetItemIcon();
    public IBrush IconColor => GetIconColorBrush();
    public IBrush IconBackgroundColor => GetIconBackgroundColorBrush();
    public IBrush NameColor => GetNameColorBrush();
    public FontWeight FontWeight => GetFontWeightValue();
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();

    private void UpdateItemType()
    {
        if (IsDirectory)
            _itemType = DetermineDirectoryType();
        else
            _itemType = DetermineFileType();
    }

    private FileTreeItemType DetermineDirectoryType()
    {
        var name = Name.ToLowerInvariant();
        
        if (Directory.Exists(FullPath))
        {
            try
            {
                var hasSln = Directory.GetFiles(FullPath, "*.sln").Length > 0 ||
                             Directory.GetFiles(FullPath, "*.slnx").Length > 0;
                if (hasSln && ParentItem == null)
                    return FileTreeItemType.Solution;
            }
            catch { }
        }
        
        return name switch
        {
            "properties" => FileTreeItemType.SpecialFolder,
            "wwwroot" => FileTreeItemType.SpecialFolder,
            "models" => FileTreeItemType.SpecialFolder,
            "viewmodels" => FileTreeItemType.SpecialFolder,
            "views" => FileTreeItemType.SpecialFolder,
            "controllers" => FileTreeItemType.SpecialFolder,
            "services" => FileTreeItemType.SpecialFolder,
            "pages" => FileTreeItemType.SpecialFolder,
            "components" => FileTreeItemType.SpecialFolder,
            "controls" => FileTreeItemType.SpecialFolder,
            "assets" => FileTreeItemType.SpecialFolder,
            "resources" => FileTreeItemType.SpecialFolder,
            "dependencies" or "packages" => FileTreeItemType.DependenciesFolder,
            _ => FileTreeItemType.Folder
        };
    }

    private FileTreeItemType DetermineFileType()
    {
        var ext = Extension;
        var name = Name.ToLowerInvariant();

        if (name.EndsWith(".axaml.cs") || name.EndsWith(".xaml.cs"))
        {
            _isCodeBehind = true;
            return FileTreeItemType.CodeBehind;
        }
        if (name.EndsWith(".razor.cs") || name.EndsWith(".cshtml.cs"))
        {
            _isCodeBehind = true;
            return FileTreeItemType.CodeBehind;
        }
        if (name.EndsWith(".designer.cs"))
            return FileTreeItemType.DesignerFile;
        if (name.EndsWith(".g.cs") || name.EndsWith(".generated.cs"))
            return FileTreeItemType.GeneratedFile;

        if (ext == ".sln" || ext == ".slnx")
            return FileTreeItemType.Solution;
        if (ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj")
            return FileTreeItemType.CsProjFile;
        if (name == "nuget.config")
            return FileTreeItemType.NugetConfig;
        if (name == "global.json")
            return FileTreeItemType.GlobalJson;
        if (name == "directory.build.props")
            return FileTreeItemType.DirectoryBuildProps;
        if (name == "directory.build.targets")
            return FileTreeItemType.DirectoryBuildTargets;

        if (ext == ".cs")
            return DetermineCSharpFileType();

        if (ext == ".axaml" || ext == ".xaml")
            return DetermineAxamlFileType();

        if (ext == ".razor")
            return FileTreeItemType.RazorComponent;
        if (ext == ".cshtml")
            return name.Contains("page") ? FileTreeItemType.RazorPage : FileTreeItemType.RazorView;

        if (name == ".editorconfig")
            return FileTreeItemType.EditorConfig;
        if (name.Contains("appsettings") && ext == ".json")
            return FileTreeItemType.AppSettings;
        if (name == "launchsettings.json")
            return FileTreeItemType.LaunchSettings;

        return ext switch
        {
            ".html" or ".htm" => FileTreeItemType.HtmlFile,
            ".css" => FileTreeItemType.CssFile,
            ".scss" or ".sass" => FileTreeItemType.ScssFile,
            ".js" => FileTreeItemType.JavaScriptFile,
            ".ts" or ".tsx" => FileTreeItemType.TypeScriptFile,
            ".json" => FileTreeItemType.JsonFile,
            ".xml" => FileTreeItemType.XmlFile,
            ".yaml" or ".yml" => FileTreeItemType.YamlFile,
            ".md" or ".markdown" => FileTreeItemType.MarkdownFile,
            ".txt" => FileTreeItemType.TextFile,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".svg" or ".bmp" => FileTreeItemType.ImageFile,
            ".ttf" or ".otf" or ".woff" or ".woff2" => FileTreeItemType.FontFile,
            ".dll" or ".exe" or ".pdb" => FileTreeItemType.BinaryFile,
            _ => FileTreeItemType.UnknownFile
        };
    }

    private FileTreeItemType DetermineCSharpFileType()
    {
        if (!File.Exists(FullPath))
            return FileTreeItemType.CSharpFile;

        try
        {
            var lines = File.ReadLines(FullPath).Take(50).ToList();
            var content = string.Join(" ", lines);

            if (content.Contains("interface ") && content.Contains("public interface"))
                return FileTreeItemType.CSharpInterface;
            if (content.Contains("record ") && (content.Contains("public record") || content.Contains("internal record")))
                return FileTreeItemType.CSharpRecord;
            if (content.Contains("struct ") && (content.Contains("public struct") || content.Contains("internal struct")))
                return FileTreeItemType.CSharpStruct;
            if (content.Contains("enum ") && (content.Contains("public enum") || content.Contains("internal enum")))
                return FileTreeItemType.CSharpEnum;
            if (content.Contains("delegate "))
                return FileTreeItemType.CSharpDelegate;

            return FileTreeItemType.CSharpClass;
        }
        catch
        {
            return FileTreeItemType.CSharpFile;
        }
    }

    private FileTreeItemType DetermineAxamlFileType()
    {
        if (!File.Exists(FullPath))
            return FileTreeItemType.XamlFile;

        try
        {
            var content = File.ReadAllText(FullPath);

            if (content.Contains("<Window "))
                return FileTreeItemType.AxamlWindow;
            if (content.Contains("<UserControl "))
                return FileTreeItemType.AxamlUserControl;
            if (content.Contains("<Page "))
                return FileTreeItemType.AxamlPage;
            if (content.Contains("<ResourceDictionary "))
                return FileTreeItemType.AxamlResourceDict;
            if (content.Contains("<Styles "))
                return FileTreeItemType.AxamlStyles;

            return FileTreeItemType.XamlFile;
        }
        catch
        {
            return FileTreeItemType.XamlFile;
        }
    }

    private string GetItemIcon()
    {
        if (IsDirectory)
        {
            return _itemType switch
            {
                FileTreeItemType.Solution => "🗂️",
                FileTreeItemType.SolutionFolder => "📁",
                FileTreeItemType.Project => "📦",
                FileTreeItemType.SpecialFolder => IsExpanded ? "📂" : "📁",
                FileTreeItemType.DependenciesFolder => "📚",
                _ => IsExpanded ? "📂" : "📁"
            };
        }

        return _itemType switch
        {
            FileTreeItemType.Solution => "🗂️",
            FileTreeItemType.CsProjFile => "📦",
            FileTreeItemType.NugetConfig => "📦",
            FileTreeItemType.GlobalJson => "⚙️",
            FileTreeItemType.DirectoryBuildProps => "🔧",
            FileTreeItemType.DirectoryBuildTargets => "🎯",
            FileTreeItemType.CSharpClass => "C#",
            FileTreeItemType.CSharpInterface => "I#",
            FileTreeItemType.CSharpRecord => "R#",
            FileTreeItemType.CSharpStruct => "S#",
            FileTreeItemType.CSharpEnum => "E#",
            FileTreeItemType.CSharpDelegate => "D#",
            FileTreeItemType.CSharpFile => "C#",
            FileTreeItemType.CodeBehind => "C#",
            FileTreeItemType.DesignerFile => "🔧",
            FileTreeItemType.GeneratedFile => "⚡",
            FileTreeItemType.AxamlWindow => "🪟",
            FileTreeItemType.AxamlUserControl => "🎛️",
            FileTreeItemType.AxamlPage => "📄",
            FileTreeItemType.AxamlResourceDict => "🎨",
            FileTreeItemType.AxamlStyles => "🎨",
            FileTreeItemType.XamlFile => "📐",
            FileTreeItemType.RazorComponent => "⚡",
            FileTreeItemType.RazorPage => "📃",
            FileTreeItemType.RazorView => "👁️",
            FileTreeItemType.HtmlFile => "🌐",
            FileTreeItemType.CssFile => "🎨",
            FileTreeItemType.ScssFile => "🎨",
            FileTreeItemType.JavaScriptFile => "JS",
            FileTreeItemType.TypeScriptFile => "TS",
            FileTreeItemType.JsonFile => "{ }",
            FileTreeItemType.XmlFile => "📋",
            FileTreeItemType.YamlFile => "📋",
            FileTreeItemType.EditorConfig => "⚙️",
            FileTreeItemType.AppSettings => "⚙️",
            FileTreeItemType.LaunchSettings => "🚀",
            FileTreeItemType.MarkdownFile => "📝",
            FileTreeItemType.TextFile => "📄",
            FileTreeItemType.ImageFile => "🖼️",
            FileTreeItemType.FontFile => "🔤",
            FileTreeItemType.BinaryFile => "📦",
            _ => "📄"
        };
    }

    private IBrush GetIconColorBrush()
    {
        var colorStr = _itemType switch
        {
            FileTreeItemType.Solution => "#FFCBA6F7",
            FileTreeItemType.SolutionFolder => "#FFCBA6F7",
            FileTreeItemType.Project => "#FFCBA6F7",
            FileTreeItemType.CsProjFile => "#FFCBA6F7",
            FileTreeItemType.CSharpClass => "#FFA6E3A1",
            FileTreeItemType.CSharpInterface => "#FF94E2D5",
            FileTreeItemType.CSharpRecord => "#FFA6E3A1",
            FileTreeItemType.CSharpStruct => "#FFFAB387",
            FileTreeItemType.CSharpEnum => "#FFF9E2AF",
            FileTreeItemType.CSharpDelegate => "#FFF38BA8",
            FileTreeItemType.CSharpFile => "#FFA6E3A1",
            FileTreeItemType.CodeBehind => "#FF89B4FA",
            FileTreeItemType.DesignerFile => "#FF9399B2",
            FileTreeItemType.GeneratedFile => "#FF9399B2",
            FileTreeItemType.AxamlWindow => "#FF89B4FA",
            FileTreeItemType.AxamlUserControl => "#FF89B4FA",
            FileTreeItemType.AxamlPage => "#FF89B4FA",
            FileTreeItemType.AxamlResourceDict => "#FF89DCEB",
            FileTreeItemType.AxamlStyles => "#FF89DCEB",
            FileTreeItemType.XamlFile => "#FF89B4FA",
            FileTreeItemType.RazorComponent => "#FFCBA6F7",
            FileTreeItemType.RazorPage => "#FFCBA6F7",
            FileTreeItemType.RazorView => "#FFCBA6F7",
            FileTreeItemType.HtmlFile => "#FFFAB387",
            FileTreeItemType.CssFile => "#FF89DCEB",
            FileTreeItemType.ScssFile => "#FFF38BA8",
            FileTreeItemType.JavaScriptFile => "#FFF9E2AF",
            FileTreeItemType.TypeScriptFile => "#FF89B4FA",
            FileTreeItemType.JsonFile => "#FFF9E2AF",
            FileTreeItemType.XmlFile => "#FFFAB387",
            FileTreeItemType.YamlFile => "#FFF38BA8",
            FileTreeItemType.EditorConfig => "#FF9399B2",
            FileTreeItemType.AppSettings => "#FFF9E2AF",
            FileTreeItemType.LaunchSettings => "#FFA6E3A1",
            FileTreeItemType.NugetConfig => "#FFCBA6F7",
            FileTreeItemType.GlobalJson => "#FF9399B2",
            FileTreeItemType.DirectoryBuildProps => "#FF9399B2",
            FileTreeItemType.DirectoryBuildTargets => "#FF9399B2",
            FileTreeItemType.Folder => "#FFFAB387",
            FileTreeItemType.SpecialFolder => "#FF89DCEB",
            FileTreeItemType.DependenciesFolder => "#FFCBA6F7",
            FileTreeItemType.MarkdownFile => "#FFCDD6F4",
            FileTreeItemType.TextFile => "#FFCDD6F4",
            FileTreeItemType.ImageFile => "#FFF38BA8",
            FileTreeItemType.FontFile => "#FF9399B2",
            FileTreeItemType.BinaryFile => "#FF9399B2",
            _ => "#FFCDD6F4"
        };
        return SolidColorBrush.Parse(colorStr);
    }

    private IBrush GetIconBackgroundColorBrush()
    {
        var colorStr = _itemType switch
        {
            FileTreeItemType.CSharpClass => "#20A6E3A1",
            FileTreeItemType.CSharpInterface => "#2094E2D5",
            FileTreeItemType.CSharpRecord => "#20A6E3A1",
            FileTreeItemType.CSharpStruct => "#20FAB387",
            FileTreeItemType.CSharpEnum => "#20F9E2AF",
            FileTreeItemType.CSharpDelegate => "#20F38BA8",
            FileTreeItemType.CSharpFile => "#20A6E3A1",
            FileTreeItemType.CodeBehind => "#2089B4FA",
            FileTreeItemType.JavaScriptFile => "#20F9E2AF",
            FileTreeItemType.TypeScriptFile => "#2089B4FA",
            FileTreeItemType.JsonFile => "#20F9E2AF",
            FileTreeItemType.Solution => "#20CBA6F7",
            FileTreeItemType.Project => "#20CBA6F7",
            FileTreeItemType.CsProjFile => "#20CBA6F7",
            _ => "#00000000"
        };
        return SolidColorBrush.Parse(colorStr);
    }

    private IBrush GetNameColorBrush()
    {
        var colorStr = _itemType switch
        {
            FileTreeItemType.Solution => "#FFCBA6F7",
            FileTreeItemType.Project => "#FFCBA6F7",
            FileTreeItemType.CsProjFile => "#FFCBA6F7",
            FileTreeItemType.Folder => "#FFFAB387",
            FileTreeItemType.SpecialFolder => "#FF89DCEB",
            FileTreeItemType.DependenciesFolder => "#FFCBA6F7",
            FileTreeItemType.CodeBehind => "#FFA0A8C0",
            FileTreeItemType.DesignerFile => "#FF9399B2",
            FileTreeItemType.GeneratedFile => "#FF9399B2",
            _ => "#FFCDD6F4"
        };
        return SolidColorBrush.Parse(colorStr);
    }

    private FontWeight GetFontWeightValue()
    {
        return _itemType switch
        {
            FileTreeItemType.Solution => FontWeight.Bold,
            FileTreeItemType.Project => FontWeight.SemiBold,
            FileTreeItemType.CsProjFile => FontWeight.SemiBold,
            _ => FontWeight.Normal
        };
    }

    public void LoadChildren(bool forceReload = false)
    {
        if (!IsDirectory) return;
        if (_isLoaded && !forceReload) return;
        if (!Directory.Exists(FullPath))
        {
            _isLoaded = true;
            return;
        }

        try
        {
            _isLoaded = true;
            Children.Clear();

            var directories = new List<FileTreeItem>();
            var files = new List<FileTreeItem>();
            var groupedFiles = new Dictionary<string, List<FileTreeItem>>();

            foreach (var dir in Directory.GetDirectories(FullPath))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".") || 
                    dirName == "bin" || 
                    dirName == "obj" || 
                    dirName == "node_modules" ||
                    dirName == ".git" ||
                    dirName == ".vs" ||
                    dirName == ".idea")
                    continue;

                directories.Add(new FileTreeItem
                {
                    Name = dirName,
                    FullPath = dir,
                    IsDirectory = true,
                    ParentItem = this
                });
            }

            foreach (var file in Directory.GetFiles(FullPath))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith(".")) continue;

                var item = new FileTreeItem
                {
                    Name = fileName,
                    FullPath = file,
                    IsDirectory = false,
                    ParentItem = this
                };

                var groupKey = GetGroupKey(fileName);
                if (!string.IsNullOrEmpty(groupKey))
                {
                    if (!groupedFiles.ContainsKey(groupKey))
                        groupedFiles[groupKey] = new List<FileTreeItem>();
                    groupedFiles[groupKey].Add(item);
                }
                else
                {
                    files.Add(item);
                }
            }

            directories = directories.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var dir in directories)
                Children.Add(dir);

            foreach (var group in groupedFiles.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var items = group.Value.OrderBy(f => GetFilePriority(f.Name)).ToList();
                if (items.Count == 1)
                {
                    Children.Add(items[0]);
                }
                else
                {
                    var mainFile = items[0];
                    for (int i = 1; i < items.Count; i++)
                    {
                        items[i].IsCodeBehind = true;
                        items[i].AssociatedFile = mainFile.FullPath;
                        mainFile.Children.Add(items[i]);
                    }
                    Children.Add(mainFile);
                }
            }

            files = files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var file in files)
                Children.Add(file);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading directory contents: {ex.Message}");
        }
    }

    private string? GetGroupKey(string fileName)
    {
        var lowerName = fileName.ToLowerInvariant();
        
        if (lowerName.EndsWith(".axaml") || lowerName.EndsWith(".axaml.cs"))
            return Path.GetFileNameWithoutExtension(lowerName.Replace(".axaml.cs", ".axaml"));
        if (lowerName.EndsWith(".xaml") || lowerName.EndsWith(".xaml.cs"))
            return Path.GetFileNameWithoutExtension(lowerName.Replace(".xaml.cs", ".xaml"));
        if (lowerName.EndsWith(".razor") || lowerName.EndsWith(".razor.cs") || lowerName.EndsWith(".razor.css"))
            return Path.GetFileNameWithoutExtension(lowerName.Replace(".razor.cs", ".razor").Replace(".razor.css", ".razor"));
        if (lowerName.EndsWith(".cshtml") || lowerName.EndsWith(".cshtml.cs"))
            return Path.GetFileNameWithoutExtension(lowerName.Replace(".cshtml.cs", ".cshtml"));
        if (lowerName.EndsWith(".designer.cs"))
            return lowerName.Replace(".designer.cs", "");

        return null;
    }

    private int GetFilePriority(string fileName)
    {
        var lowerName = fileName.ToLowerInvariant();
        
        if (lowerName.EndsWith(".axaml") || lowerName.EndsWith(".xaml") || 
            lowerName.EndsWith(".razor") || lowerName.EndsWith(".cshtml"))
            return 0;
        if (lowerName.EndsWith(".razor.css"))
            return 1;
        if (lowerName.EndsWith(".cs"))
            return 2;
        return 99;
    }

    public void Refresh()
    {
        if (!IsDirectory) return;
        LoadChildren(forceReload: true);
    }
    
    public void RefreshRecursive()
    {
        if (!IsDirectory) return;
        LoadChildren(forceReload: true);
        foreach (var child in Children)
        {
            if (child.IsDirectory && child.IsExpanded)
                child.RefreshRecursive();
        }
    }

    public FileTreeItem? FindByPath(string path)
    {
        if (FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            return this;
        foreach (var child in Children)
        {
            var found = child.FindByPath(path);
            if (found != null) return found;
        }
        return null;
    }

    public void AddChild(FileTreeItem item)
    {
        item.ParentItem = this;
        var insertIndex = Children.Count;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (item.IsDirectory && !child.IsDirectory)
            {
                insertIndex = i;
                break;
            }
            if (item.IsDirectory == child.IsDirectory)
            {
                if (string.Compare(item.Name, child.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    insertIndex = i;
                    break;
                }
            }
        }
        Children.Insert(insertIndex, item);
    }

    public bool RemoveChild(FileTreeItem item) => Children.Remove(item);

    public static FileTreeItem FromDirectory(string path, bool loadChildren = false)
    {
        var item = new FileTreeItem
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            IsDirectory = true
        };
        if (loadChildren && Directory.Exists(path))
            item.LoadChildren();
        return item;
    }

    public static FileTreeItem FromFile(string path)
    {
        return new FileTreeItem
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            IsDirectory = false
        };
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

