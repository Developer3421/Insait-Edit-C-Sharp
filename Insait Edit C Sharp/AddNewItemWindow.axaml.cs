using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
        
        // Try to find the .csproj file and use its name as base namespace
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
                if (csprojFiles.Length > 0)
                {
                    var baseName = Path.GetFileNameWithoutExtension(csprojFiles[0].Name);
                    
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
                // ASP.NET
                "RazorItem", "RazorPageItem", "RazorViewItem", "ControllerItem", "ApiControllerItem", "MinimalApiItem",
                // Avalonia
                "AxamlItem", "UserControlItem", "TemplatedControlItem", "StylesItem", "ResourceDictItem",
                // Web
                "HtmlItem", "CssItem", "ScssItem", "JavaScriptItem", "TypeScriptItem",
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
            // ASP.NET
            "razor" => "NewComponent",
            "razorpage" => "NewPage",
            "razorview" => "Index",
            "controller" => "HomeController",
            "apicontroller" => "ApiController",
            "minimalapi" => "Endpoints",
            // Avalonia
            "axaml" => "NewWindow",
            "usercontrol" => "NewControl",
            "templatedcontrol" => "NewTemplatedControl",
            "avaloniastyles" => "Styles",
            "resourcedictionary" => "Resources",
            // Web
            "html" => "index",
            "css" => "styles",
            "scss" => "styles",
            "javascript" => "script",
            "typescript" => "script",
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
            // ASP.NET
            "razor" => ".razor",
            "razorpage" => ".cshtml",
            "razorview" => ".cshtml",
            "controller" => ".cs",
            "apicontroller" => ".cs",
            "minimalapi" => ".cs",
            // Avalonia
            "axaml" => ".axaml",
            "usercontrol" => ".axaml",
            "templatedcontrol" => ".cs",
            "avaloniastyles" => ".axaml",
            "resourcedictionary" => ".axaml",
            // Web
            "html" => ".html",
            "css" => ".css",
            "scss" => ".scss",
            "javascript" => ".js",
            "typescript" => ".ts",
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

            // For Razor Pages, also create the .cshtml.cs file
            if (_selectedItemType == "razorpage")
            {
                var codeFilePath = filePath + ".cs";
                var codeContent = GenerateRazorPageCodeBehind(name);
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
            // ASP.NET
            "razor" => GenerateRazorComponent(name),
            "razorpage" => GenerateRazorPage(name),
            "razorview" => GenerateRazorView(name),
            "controller" => GenerateController(name, ns),
            "apicontroller" => GenerateApiController(name, ns),
            "minimalapi" => GenerateMinimalApi(name, ns),
            // Avalonia
            "axaml" => GenerateAvaloniaWindow(name, ns),
            "usercontrol" => GenerateAvaloniaUserControl(name, ns),
            "templatedcontrol" => GenerateTemplatedControl(name, ns),
            "avaloniastyles" => GenerateAvaloniaStyles(ns),
            "resourcedictionary" => GenerateResourceDictionary(ns),
            // Web
            "html" => GenerateHtml(name),
            "css" => GenerateCss(name),
            "scss" => GenerateScss(name),
            "javascript" => GenerateJavaScript(name),
            "typescript" => GenerateTypeScript(name),
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

    private string GenerateRazorComponent(string name)
    {
        return $@"@namespace MyNamespace

<div class=""{name.ToLower()}"">
    <h3>{name}</h3>
</div>

@code {{
    [Parameter]
    public string Title {{ get; set; }} = ""{name}"";
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

    // ASP.NET Types
    private string GenerateRazorPage(string name)
    {
        return $@"@page
@model {name}Model

<div class=""text-center"">
    <h1>{name}</h1>
</div>
";
    }

    private string GenerateRazorPageCodeBehind(string name)
    {
        var ns = _namespace?.Replace("-", "_") ?? "MyNamespace";
        return $@"using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace {ns};

public class {name}Model : PageModel
{{
    public void OnGet()
    {{
    }}

    public IActionResult OnPost()
    {{
        return Page();
    }}
}}
";
    }

    private string GenerateRazorView(string name)
    {
        return $@"@{{
    ViewData[""Title""] = ""{name}"";
}}

<div class=""container"">
    <h1>@ViewData[""Title""]</h1>
</div>
";
    }

    private string GenerateController(string name, string ns)
    {
        return $@"using Microsoft.AspNetCore.Mvc;

namespace {ns};

public class {name} : Controller
{{
    public IActionResult Index()
    {{
        return View();
    }}
}}
";
    }

    private string GenerateApiController(string name, string ns)
    {
        return $@"using Microsoft.AspNetCore.Mvc;

namespace {ns};

[ApiController]
[Route(""api/[controller]"")]
public class {name} : ControllerBase
{{
    [HttpGet]
    public IActionResult Get()
    {{
        return Ok();
    }}

    [HttpGet(""{{id}}"")]
    public IActionResult Get(int id)
    {{
        return Ok();
    }}

    [HttpPost]
    public IActionResult Post([FromBody] object value)
    {{
        return CreatedAtAction(nameof(Get), new {{ id = 1 }}, value);
    }}

    [HttpPut(""{{id}}"")]
    public IActionResult Put(int id, [FromBody] object value)
    {{
        return NoContent();
    }}

    [HttpDelete(""{{id}}"")]
    public IActionResult Delete(int id)
    {{
        return NoContent();
    }}
}}
";
    }

    private string GenerateMinimalApi(string name, string ns)
    {
        return $@"namespace {ns};

public static class {name}
{{
    public static void Map{name}(this WebApplication app)
    {{
        var group = app.MapGroup(""api/{name.ToLower()}"");
        
        group.MapGet(""/"", GetAll);
        group.MapGet(""/{{id}}"", GetById);
        group.MapPost(""/"", Create);
        group.MapPut(""/{{id}}"", Update);
        group.MapDelete(""/{{id}}"", Delete);
    }}

    private static IResult GetAll()
    {{
        return Results.Ok();
    }}

    private static IResult GetById(int id)
    {{
        return Results.Ok();
    }}

    private static IResult Create(object item)
    {{
        return Results.Created($""/api/{name.ToLower()}/1"", item);
    }}

    private static IResult Update(int id, object item)
    {{
        return Results.NoContent();
    }}

    private static IResult Delete(int id)
    {{
        return Results.NoContent();
    }}
}}
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

    // Web Files
    private string GenerateHtml(string name)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{name}</title>
</head>
<body>
    
</body>
</html>
";
    }

    private string GenerateCss(string name)
    {
        return $@"/* {name} stylesheet */

* {{
    margin: 0;
    padding: 0;
    box-sizing: border-box;
}}

body {{
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
}}
";
    }

    private string GenerateScss(string name)
    {
        return $@"// {name} SCSS stylesheet

$primary-color: #007bff;
$secondary-color: #6c757d;

* {{
    margin: 0;
    padding: 0;
    box-sizing: border-box;
}}

body {{
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
}}
";
    }

    private string GenerateJavaScript(string name)
    {
        return $@"// {name}.js

'use strict';

document.addEventListener('DOMContentLoaded', () => {{
    // Your code here
}});
";
    }

    private string GenerateTypeScript(string name)
    {
        return $@"// {name}.ts

export class {char.ToUpper(name[0])}{name[1..]} {{
    constructor() {{
        // Initialize
    }}
}}
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
}


