using System;
using System.IO;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Service for generating code templates
/// </summary>
public class FileTemplateService
{
    /// <summary>
    /// Create a new C# class file
    /// </summary>
    public async Task<string> CreateClassAsync(string directory, string className, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateClass(className, ns);
        var filePath = Path.Combine(directory, $"{className}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new C# interface file
    /// </summary>
    public async Task<string> CreateInterfaceAsync(string directory, string interfaceName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateInterface(interfaceName, ns);
        var filePath = Path.Combine(directory, $"{interfaceName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new C# record file
    /// </summary>
    public async Task<string> CreateRecordAsync(string directory, string recordName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateRecord(recordName, ns);
        var filePath = Path.Combine(directory, $"{recordName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new C# struct file
    /// </summary>
    public async Task<string> CreateStructAsync(string directory, string structName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateStruct(structName, ns);
        var filePath = Path.Combine(directory, $"{structName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new C# enum file
    /// </summary>
    public async Task<string> CreateEnumAsync(string directory, string enumName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateEnum(enumName, ns);
        var filePath = Path.Combine(directory, $"{enumName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new Avalonia Window
    /// </summary>
    public async Task<string> CreateAvaloniaWindowAsync(string directory, string windowName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        
        // Create AXAML file
        var axamlContent = GenerateAvaloniaWindow(windowName, ns);
        var axamlPath = Path.Combine(directory, $"{windowName}.axaml");
        await File.WriteAllTextAsync(axamlPath, axamlContent);

        // Create code-behind file
        var codeContent = GenerateAvaloniaWindowCodeBehind(windowName, ns);
        var codePath = Path.Combine(directory, $"{windowName}.axaml.cs");
        await File.WriteAllTextAsync(codePath, codeContent);

        return axamlPath;
    }

    /// <summary>
    /// Create a new Avalonia UserControl
    /// </summary>
    public async Task<string> CreateAvaloniaUserControlAsync(string directory, string controlName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        
        // Create AXAML file
        var axamlContent = GenerateAvaloniaUserControl(controlName, ns);
        var axamlPath = Path.Combine(directory, $"{controlName}.axaml");
        await File.WriteAllTextAsync(axamlPath, axamlContent);

        // Create code-behind file
        var codeContent = GenerateAvaloniaUserControlCodeBehind(controlName, ns);
        var codePath = Path.Combine(directory, $"{controlName}.axaml.cs");
        await File.WriteAllTextAsync(codePath, codeContent);

        return axamlPath;
    }

    /// <summary>
    /// Create a new JSON file
    /// </summary>
    public async Task<string> CreateJsonFileAsync(string directory, string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var content = "{\n  \n}";
        var filePath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new XML file
    /// </summary>
    public async Task<string> CreateXmlFileAsync(string directory, string fileName)
    {
        if (!fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            fileName += ".xml";

        var content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n  \n</root>";
        var filePath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new C# delegate file
    /// </summary>
    public async Task<string> CreateDelegateAsync(string directory, string delegateName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateDelegate(delegateName, ns);
        var filePath = Path.Combine(directory, $"{delegateName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a new C# exception file
    /// </summary>
    public async Task<string> CreateExceptionAsync(string directory, string exceptionName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateException(exceptionName, ns);
        var filePath = Path.Combine(directory, $"{exceptionName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create a global usings file
    /// </summary>
    public async Task<string> CreateGlobalUsingsAsync(string directory)
    {
        var content = GenerateGlobalUsings();
        var filePath = Path.Combine(directory, "GlobalUsings.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create an MVC Controller
    /// </summary>
    public async Task<string> CreateControllerAsync(string directory, string controllerName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateController(controllerName, ns);
        var filePath = Path.Combine(directory, $"{controllerName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Create an API Controller
    /// </summary>
    public async Task<string> CreateApiControllerAsync(string directory, string controllerName, string? namespaceName = null)
    {
        var ns = namespaceName ?? GetNamespaceFromPath(directory);
        var content = GenerateApiController(controllerName, ns);
        var filePath = Path.Combine(directory, $"{controllerName}.cs");
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    private string GetNamespaceFromPath(string path)
    {
        var dir = new DirectoryInfo(path);
        while (dir != null)
        {
            var csprojFiles = dir.GetFiles("*.csproj");
            if (csprojFiles.Length > 0)
            {
                var baseName = Path.GetFileNameWithoutExtension(csprojFiles[0].Name).Replace("-", "_").Replace(" ", "_");
                
                var relativePath = Path.GetRelativePath(dir.FullName, path);
                if (relativePath != "." && !string.IsNullOrEmpty(relativePath))
                {
                    var subNamespace = relativePath
                        .Replace(Path.DirectorySeparatorChar, '.')
                        .Replace("-", "_")
                        .Replace(" ", "_");
                    return $"{baseName}.{subNamespace}";
                }
                return baseName;
            }
            dir = dir.Parent;
        }
        return "MyNamespace";
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

    private string GenerateAvaloniaWindowCodeBehind(string name, string ns)
    {
        return $@"using Avalonia.Controls;

namespace {ns};

public partial class {name} : Window
{{
    public {name}()
    {{
        InitializeComponent();
    }}
}}
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

    private string GenerateAvaloniaUserControlCodeBehind(string name, string ns)
    {
        return $@"using Avalonia.Controls;

namespace {ns};

public partial class {name} : UserControl
{{
    public {name}()
    {{
        InitializeComponent();
    }}
}}
";
    }

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
}
