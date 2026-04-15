using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Insait_Edit_C_Sharp.Services;

namespace Insait_Edit_C_Sharp;

public partial class AddNewItemWindow : Window
{
    private string _selectedItemType = "class";
    private readonly string _targetDirectory;
    private readonly string? _namespace;

    public string? CreatedFilePath { get; private set; }

    public AddNewItemWindow(string targetDirectory, string? defaultNamespace = null)
    {
        InitializeComponent();
        
        _targetDirectory = targetDirectory;
        _namespace = defaultNamespace ?? GetNamespaceFromPath(targetDirectory);
        
        var locationText = this.FindControl<TextBlock>("LocationPreviewText");
        if (locationText != null)
        {
            locationText.Text = _targetDirectory;
        }
        
        UpdatePreview();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var L = (Func<string, string>)LocalizationService.Get;
        Title = L("AddItem.Title");
        var titleBar = this.FindControl<TextBlock>("TitleBarText");
        if (titleBar != null) titleBar.Text = L("AddItem.Title");
        var csHeader = this.FindControl<TextBlock>("CSharpTypesHeader");
        if (csHeader != null) csHeader.Text = L("AddItem.CSharpTypes");
        var fsHeader = this.FindControl<TextBlock>("FSharpTypesHeader");
        if (fsHeader != null) fsHeader.Text = L("AddItem.FSharpTypes");
        var avHeader = this.FindControl<TextBlock>("AvaloniaUIHeader");
        if (avHeader != null) avHeader.Text = L("AddItem.AvaloniaUI");
        var cfgHeader = this.FindControl<TextBlock>("ConfigDataHeader");
        if (cfgHeader != null) cfgHeader.Text = L("AddItem.ConfigData");
        var dotNetHeader = this.FindControl<TextBlock>("DotNetConfigHeader");
        if (dotNetHeader != null) dotNetHeader.Text = L("AddItem.DotNetConfig");
        var gitHeader = this.FindControl<TextBlock>("GitHeader");
        if (gitHeader != null) gitHeader.Text = L("AddItem.Git");
        var nameLabel = this.FindControl<TextBlock>("NameLabel");
        if (nameLabel != null) nameLabel.Text = L("AddItem.Name");
        var previewLabel = this.FindControl<TextBlock>("PreviewLabel");
        if (previewLabel != null) previewLabel.Text = L("AddItem.Preview");
        var locLabel = this.FindControl<TextBlock>("LocationLabel");
        if (locLabel != null) locLabel.Text = L("AddItem.Location");
        var addToProject = this.FindControl<TextBlock>("AddToProjectText");
        if (addToProject != null) addToProject.Text = L("AddItem.AddToProject");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (cancelBtn != null) cancelBtn.Content = L("AddItem.Cancel");
        var createBtn = this.FindControl<Button>("CreateButton");
        if (createBtn != null) createBtn.Content = L("AddItem.Add");
    }

    private string GetNamespaceFromPath(string path)
    {
        // Check if the path exists
        if (!Directory.Exists(path))
        {
            // Try to use the parent directory or return default namespace
            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                path = parentDir;
            }
            else
            {
                return "MyNamespace";
            }
        }
        
        // Try to find the .csproj or .fsproj file and use its name as base namespace
        try
        {
            var dir = new DirectoryInfo(path);
            while (dir != null)
            {
                if (!dir.Exists)
                {
                    dir = dir.Parent;
                    continue;
                }
                
                var csprojFiles = dir.GetFiles("*.csproj");
                var fsprojFiles = dir.GetFiles("*.fsproj");
                var projectFiles = csprojFiles.Concat(fsprojFiles).ToArray();
                if (projectFiles.Length > 0)
                {
                    var baseName = Path.GetFileNameWithoutExtension(projectFiles[0].Name);
                    
                    // Calculate relative path from project root
                    var relativePath = Path.GetRelativePath(dir.FullName, path);
                    if (relativePath != "." && !string.IsNullOrEmpty(relativePath))
                    {
                        var subNamespace = relativePath.Replace(Path.DirectorySeparatorChar, '.').Replace("-", "_");
                        return $"{baseName}.{subNamespace}";
                    }
                    return baseName;
                }
                dir = dir.Parent;
            }
        }
        catch (Exception)
        {
            // Silently ignore any IO errors and return default namespace
        }
        
        return "MyNamespace";
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ItemType_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string itemType)
        {
            _selectedItemType = itemType;
            
            var items = new[] { 
                // C# Types
                "ClassItem", "InterfaceItem", "RecordItem", "StructItem", "EnumItem", 
                "DelegateItem", "ExceptionItem", "GlobalUsingsItem",
                // F# Types
                "FsModuleItem", "FsClassItem", "FsRecordItem", "FsUnionItem",
                "FsInterfaceItem", "FsScriptItem", "FsSignatureItem",
                // Avalonia
                "AxamlItem", "UserControlItem", "TemplatedControlItem", "StylesItem", "ResourceDictItem",
                // Config/Data
                "JsonItem", "XmlItem", "YamlItem", "MarkdownItem", "TextItem",
                // .NET Config
                "EditorConfigItem", "GlobalJsonItem", "NugetConfigItem", "DirBuildPropsItem", "DirBuildTargetsItem",
                "AppSettingsItem", "LaunchSettingsItem",
                // Git
                "GitIgnoreItem", "GitAttributesItem"
            };
            foreach (var name in items)
            {
                var btn = this.FindControl<Button>(name);
                if (btn != null)
                {
                    btn.Classes.Remove("selected");
                }
            }
            button.Classes.Add("selected");
            
            UpdateDefaultName();
            UpdatePreview();
        }
    }

    private void UpdateDefaultName()
    {
        var nameBox = this.FindControl<TextBox>("ItemNameBox");
        if (nameBox == null) return;

        var defaultName = _selectedItemType switch
        {
            // C# Types
            "class" => "NewClass",
            "interface" => "INewInterface",
            "record" => "NewRecord",
            "struct" => "NewStruct",
            "enum" => "NewEnum",
            "delegate" => "NewDelegate",
            "exception" => "NewException",
            "globalusings" => "GlobalUsings",
            // F# Types
            "fsmodule" => "MyModule",
            "fsclass" => "MyClass",
            "fsrecord" => "MyRecord",
            "fsunion" => "MyUnion",
            "fsinterface" => "IMyInterface",
            "fsscript" => "Script",
            "fssignature" => "Module",
            // Avalonia
            "axaml" => "NewWindow",
            "usercontrol" => "NewControl",
            "templatedcontrol" => "NewTemplatedControl",
            "avaloniastyles" => "Styles",
            "resourcedictionary" => "Resources",
            // Config/Data
            "json" => "settings",
            "xml" => "config",
            "yaml" => "config",
            "markdown" => "README",
            "text" => "readme",
            // .NET Config
            "editorconfig" => ".editorconfig",
            "globaljson" => "global",
            "nugetconfig" => "NuGet",
            "dirbuildprops" => "Directory.Build",
            "dirbuildtargets" => "Directory.Build",
            "appsettings" => "appsettings",
            "launchsettings" => "launchSettings",
            // Git
            "gitignore" => ".gitignore",
            "gitattributes" => ".gitattributes",
            _ => "NewItem"
        };
        
        nameBox.Text = defaultName;
    }

    private void ItemName_Changed(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var nameBox = this.FindControl<TextBox>("ItemNameBox");
        var previewText = this.FindControl<TextBlock>("FilePreviewText");
        
        if (nameBox == null || previewText == null) return;

        var name = nameBox.Text?.Trim() ?? "NewItem";
        var extension = GetExtension();
        var fileName = $"{name}{extension}";
        
        previewText.Text = fileName;
    }

    private string GetExtension()
    {
        return _selectedItemType switch
        {
            // C# Types
            "class" => ".cs",
            "interface" => ".cs",
            "record" => ".cs",
            "struct" => ".cs",
            "enum" => ".cs",
            "delegate" => ".cs",
            "exception" => ".cs",
            "globalusings" => ".cs",
            // F# Types
            "fsmodule" => ".fs",
            "fsclass" => ".fs",
            "fsrecord" => ".fs",
            "fsunion" => ".fs",
            "fsinterface" => ".fs",
            "fsscript" => ".fsx",
            "fssignature" => ".fsi",
            // Avalonia
            "axaml" => ".axaml",
            "usercontrol" => ".axaml",
            "templatedcontrol" => ".cs",
            "avaloniastyles" => ".axaml",
            "resourcedictionary" => ".axaml",
            // Config/Data
            "json" => ".json",
            "xml" => ".xml",
            "yaml" => ".yaml",
            "markdown" => ".md",
            "text" => ".txt",
            // .NET Config (special - full filename)
            "editorconfig" => "",
            "globaljson" => ".json",
            "nugetconfig" => ".config",
            "dirbuildprops" => ".props",
            "dirbuildtargets" => ".targets",
            "appsettings" => ".json",
            "launchsettings" => ".json",
            // Git (special - full filename)
            "gitignore" => "",
            "gitattributes" => "",
            _ => ".cs"
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var nameBox = this.FindControl<TextBox>("ItemNameBox");
        if (nameBox == null) return;

        var name = nameBox.Text?.Trim() ?? "NewItem";
        if (string.IsNullOrWhiteSpace(name)) return;

        var extension = GetExtension();
        
        // Handle special file names (files that are already complete names like .gitignore)
        string fileName;
        if (_selectedItemType == "gitignore" || _selectedItemType == "gitattributes" || _selectedItemType == "editorconfig")
        {
            fileName = name.StartsWith(".") ? name : $".{name}";
        }
        else
        {
            fileName = $"{name}{extension}";
        }
        
        var filePath = Path.Combine(_targetDirectory, fileName);

        try
        {
            // Generate content based on item type
            var content = GenerateContent(name);
            
            // Create the file
            File.WriteAllText(filePath, content);

            // For Avalonia controls, also create the .axaml.cs file
            if (_selectedItemType == "axaml" || _selectedItemType == "usercontrol")
            {
                var codeFilePath = filePath + ".cs";
                var codeContent = GenerateCodeBehind(name);
                File.WriteAllText(codeFilePath, codeContent);
            }


            CreatedFilePath = filePath;
            Close(CreatedFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating file: {ex.Message}");
        }
    }

    private string GenerateContent(string name)
    {
        var ns = _namespace?.Replace("-", "_") ?? "MyNamespace";
        
        return _selectedItemType switch
        {
            // C# Types
            "class" => GenerateClass(name, ns),
            "interface" => GenerateInterface(name, ns),
            "record" => GenerateRecord(name, ns),
            "struct" => GenerateStruct(name, ns),
            "enum" => GenerateEnum(name, ns),
            "delegate" => GenerateDelegate(name, ns),
            "exception" => GenerateException(name, ns),
            "globalusings" => GenerateGlobalUsings(),
            // F# Types
            "fsmodule" => GenerateFsModule(name),
            "fsclass" => GenerateFsClass(name),
            "fsrecord" => GenerateFsRecord(name),
            "fsunion" => GenerateFsUnion(name),
            "fsinterface" => GenerateFsInterface(name),
            "fsscript" => GenerateFsScript(name),
            "fssignature" => GenerateFsSignature(name),
            // Avalonia
            "axaml" => GenerateAvaloniaWindow(name, ns),
            "usercontrol" => GenerateAvaloniaUserControl(name, ns),
            "templatedcontrol" => GenerateTemplatedControl(name, ns),
            "avaloniastyles" => GenerateAvaloniaStyles(ns),
            "resourcedictionary" => GenerateResourceDictionary(ns),
            // Config/Data
            "json" => "{\n  \n}",
            "xml" => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n  \n</root>",
            "yaml" => "# Configuration\n",
            "markdown" => $"# {name}\n\n",
            "text" => "",
            // .NET Config
            "editorconfig" => GenerateEditorConfig(),
            "globaljson" => GenerateGlobalJson(),
            "nugetconfig" => GenerateNuGetConfig(),
            "dirbuildprops" => GenerateDirectoryBuildProps(),
            "dirbuildtargets" => GenerateDirectoryBuildTargets(),
            "appsettings" => GenerateAppSettings(),
            "launchsettings" => GenerateLaunchSettings(),
            // Git
            "gitignore" => GenerateGitIgnore(),
            "gitattributes" => GenerateGitAttributes(),
            _ => ""
        };
    }

    private string GenerateClass(string name, string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// {name} class
/// </summary>
public class {name}
{{
    public {name}()
    {{
    }}
}}
";
    }

    private string GenerateInterface(string name, string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// {name} interface
/// </summary>
public interface {name}
{{
}}
";
    }

    private string GenerateRecord(string name, string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// {name} record
/// </summary>
public record {name}
{{
}}
";
    }

    private string GenerateStruct(string name, string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// {name} struct
/// </summary>
public struct {name}
{{
}}
";
    }

    private string GenerateEnum(string name, string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// {name} enumeration
/// </summary>
public enum {name}
{{
    None = 0,
}}
";
    }


    private string GenerateAvaloniaWindow(string name, string ns)
    {
        return $@"<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        xmlns:d=""http://schemas.microsoft.com/expression/blend/2008""
        xmlns:mc=""http://schemas.openxmlformats.org/markup-compatibility/2006""
        mc:Ignorable=""d"" d:DesignWidth=""800"" d:DesignHeight=""450""
        x:Class=""{ns}.{name}""
        Title=""{name}"">
    <Grid>
        
    </Grid>
</Window>
";
    }

    private string GenerateAvaloniaUserControl(string name, string ns)
    {
        return $@"<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
             xmlns:d=""http://schemas.microsoft.com/expression/blend/2008""
             xmlns:mc=""http://schemas.openxmlformats.org/markup-compatibility/2006""
             mc:Ignorable=""d"" d:DesignWidth=""800"" d:DesignHeight=""450""
             x:Class=""{ns}.{name}"">
    <Grid>
        
    </Grid>
</UserControl>
";
    }

    private string GenerateCodeBehind(string name)
    {
        var ns = _namespace?.Replace("-", "_") ?? "MyNamespace";
        var baseClass = _selectedItemType switch
        {
            "axaml" => "Window",
            "usercontrol" => "UserControl",
            "templatedcontrol" => "TemplatedControl",
            _ => "UserControl"
        };
        
        return $@"using Avalonia.Controls;

namespace {ns};

public partial class {name} : {baseClass}
{{
    public {name}()
    {{
        InitializeComponent();
    }}
}}
";
    }

    // New C# Types
    private string GenerateDelegate(string name, string ns)
    {
        return $@"namespace {ns};

/// <summary>
/// {name} delegate
/// </summary>
public delegate void {name}(object sender, EventArgs e);
";
    }

    private string GenerateException(string name, string ns)
    {
        return $@"using System;

namespace {ns};

/// <summary>
/// {name} exception
/// </summary>
[Serializable]
public class {name} : Exception
{{
    public {name}()
    {{
    }}

    public {name}(string message) : base(message)
    {{
    }}

    public {name}(string message, Exception innerException) : base(message, innerException)
    {{
    }}
}}
";
    }

    private string GenerateGlobalUsings()
    {
        return @"// Global using directives

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
";
    }


    // Avalonia Types
    private string GenerateTemplatedControl(string name, string ns)
    {
        return $@"using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace {ns};

public class {name} : TemplatedControl
{{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<{name}, string>(nameof(Text), ""Default"");

    public string Text
    {{
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }}

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {{
        base.OnApplyTemplate(e);
    }}
}}
";
    }

    private string GenerateAvaloniaStyles(string _)
    {
        return $@"<Styles xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Design.PreviewWith>
        <Border Padding=""20"">
            <!-- Add preview controls here -->
        </Border>
    </Design.PreviewWith>

    <!-- Add Styles Here -->
    
</Styles>
";
    }

    private string GenerateResourceDictionary(string _)
    {
        return $@"<ResourceDictionary xmlns=""https://github.com/avaloniaui""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <!-- Add resources here -->
    
</ResourceDictionary>
";
    }


    // .NET Config Files
    private string GenerateEditorConfig()
    {
        return @"# EditorConfig helps maintain consistent coding styles
# https://editorconfig.org

root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{cs,csx}]
indent_size = 4

[*.{json,xml,yml,yaml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
";
    }

    private string GenerateGlobalJson()
    {
        return @"{
  ""sdk"": {
    ""version"": ""9.0.100"",
    ""rollForward"": ""latestMinor""
  }
}
";
    }

    private string GenerateNuGetConfig()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" protocolVersion=""3"" />
  </packageSources>
</configuration>
";
    }

    private string GenerateDirectoryBuildProps()
    {
        return @"<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
";
    }

    private string GenerateDirectoryBuildTargets()
    {
        return @"<Project>
  <Target Name=""CustomTarget"" AfterTargets=""Build"">
    <!-- Custom build targets here -->
  </Target>
</Project>
";
    }

    private string GenerateAppSettings()
    {
        return @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }
  },
  ""AllowedHosts"": ""*""
}
";
    }

    private string GenerateLaunchSettings()
    {
        return @"{
  ""profiles"": {
    ""Development"": {
      ""commandName"": ""Project"",
      ""dotnetRunMessages"": true,
      ""launchBrowser"": true,
      ""applicationUrl"": ""https://localhost:5001;http://localhost:5000"",
      ""environmentVariables"": {
        ""ASPNETCORE_ENVIRONMENT"": ""Development""
      }
    }
  }
}
";
    }

    // Git Files
    private string GenerateGitIgnore()
    {
        return @"## .NET
bin/
obj/
*.user
*.suo
*.userosscache
*.sln.docstates

## Visual Studio
.vs/
*.userprefs
*.cache

## JetBrains Rider
.idea/
*.sln.iml

## Build results
[Dd]ebug/
[Rr]elease/
x64/
x86/
bld/
[Bb]in/
[Oo]bj/

## NuGet
*.nupkg
**/packages/*
!**/packages/build/

## Logs
*.log
logs/

## OS generated files
.DS_Store
Thumbs.db
";
    }

    private string GenerateGitAttributes()
    {
        return @"# Auto detect text files and perform LF normalization
* text=auto

# C# files
*.cs text diff=csharp

# Project files
*.csproj text
*.sln text eol=crlf

# Graphics
*.png binary
*.jpg binary
*.gif binary
*.ico binary

# Documents
*.pdf binary
";
    }

    // ═══════════════════════════════════════════════════════════
    //  F# Templates
    // ═══════════════════════════════════════════════════════════

    private string GenerateFsModule(string name)
    {
        return $@"module {name}

// Add your functions here

let greet (name: string) =
    printfn ""Hello, %s!"" name
";
    }

    private string GenerateFsClass(string name)
    {
        return $@"namespace MyNamespace

type {name}() =
    let mutable _value = 0

    member this.Value
        with get() = _value
        and set(v) = _value <- v

    member this.DoSomething() =
        printfn ""Doing something with value: %d"" _value
";
    }

    private string GenerateFsRecord(string name)
    {
        return $@"namespace MyNamespace

type {name} =
    {{
        Id: int
        Name: string
        Description: string option
    }}

module {name}Module =
    let create id name =
        {{ Id = id; Name = name; Description = None }}
";
    }

    private string GenerateFsUnion(string name)
    {
        return $@"namespace MyNamespace

type {name} =
    | Case1
    | Case2 of string
    | Case3 of int * string

module {name}Module =
    let describe (value: {name}) =
        match value with
        | Case1 -> ""Case1""
        | Case2 s -> sprintf ""Case2: %s"" s
        | Case3 (i, s) -> sprintf ""Case3: %d, %s"" i s
";
    }

    private string GenerateFsInterface(string name)
    {
        return $@"namespace MyNamespace

type {name} =
    abstract member DoSomething: unit -> unit
    abstract member GetValue: unit -> int
";
    }

    private string GenerateFsScript(string name)
    {
        return $@"// {name}.fsx — F# Script file

printfn ""Hello from {name}!""

let add x y = x + y

let result = add 3 4
printfn ""3 + 4 = %d"" result
";
    }

    private string GenerateFsSignature(string name)
    {
        return $@"module {name}

/// <summary>Greets a person.</summary>
val greet: name: string -> unit
";
    }
}


