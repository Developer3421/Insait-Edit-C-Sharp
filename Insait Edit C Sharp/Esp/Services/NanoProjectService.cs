using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Controls;
using Insait_Edit_C_Sharp.Esp.Models;

namespace Insait_Edit_C_Sharp.Esp.Services;

/// <summary>
/// Service for creating and managing nanoFramework projects (.nfproj)
/// </summary>
public class NanoProjectService
{
    /// <summary>
    /// Create a new nanoFramework project
    /// </summary>
    public async Task<NanoProject?> CreateProjectAsync(
        string projectName, string location, NanoProjectTemplate template,
        string targetBoard = "ESP32", string? solutionPath = null)
    {
        try
        {
            var projectDir = Path.Combine(location, projectName);
            Directory.CreateDirectory(projectDir);

            var templateInfo = NanoTemplateInfo.GetAllTemplates()
                .FirstOrDefault(t => t.Template == template);
            if (templateInfo == null) return null;

            var project = new NanoProject
            {
                Name = projectName,
                Path = projectDir,
                ProjectFilePath = Path.Combine(projectDir, $"{projectName}.csproj"),
                Template = template,
                TargetBoard = targetBoard,
                Packages = templateInfo.RequiredPackages.ToList()
            };

            // Generate modern SDK-style .csproj file targeting net10.0
            await GenerateProjectFileAsync(project);

            // Generate source files based on template
            await GenerateSourceFilesAsync(project, template);

            // Restore NuGet packages after project creation
            await RestoreNuGetPackagesAsync(project);

            // Add to solution if provided
            if (!string.IsNullOrEmpty(solutionPath) && File.Exists(solutionPath))
            {
                await AddToSolutionAsync(solutionPath, project);
            }

            return project;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating nanoFramework project: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate a modern SDK-style .csproj file targeting net10.0.
    /// Uses PackageReference format — no packages.config or AssemblyInfo.cs needed.
    /// </summary>
    private async Task GenerateProjectFileAsync(NanoProject project)
    {
        var ns = SanitizeNamespace(project.Name);
        var isExe = project.Template != NanoProjectTemplate.ClassLibrary;
        var outputType = isExe ? "Exe" : "Library";

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();

        // ── Project Properties ──────────────────────────────────────────
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <OutputType>{outputType}</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine($"    <RootNamespace>{ns}</RootNamespace>");
        sb.AppendLine($"    <AssemblyName>{project.Name}</AssemblyName>");
        sb.AppendLine("    <LangVersion>latest</LangVersion>");
        // Disable features that cause nanoFramework MetadataProcessor failures:
        // ImplicitUsings generates a hidden .cs file that embeds resources.
        // Nullable annotations embed attribute types unknown to nanoFramework CLR.
        // GenerateAssemblyInfo embeds version/culture resources.
        // NoDefaultContentItems prevents accidental .resx / resource embedding.
        sb.AppendLine("    <Nullable>disable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
        sb.AppendLine("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>");
        sb.AppendLine("    <NoDefaultContentItems>true</NoDefaultContentItems>");
        sb.AppendLine("    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>");
        // Mark as nanoFramework project for identification
        sb.AppendLine($"    <NanoFrameworkProject>true</NanoFrameworkProject>");
        sb.AppendLine($"    <TargetBoard>{project.TargetBoard}</TargetBoard>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();

        // ── NuGet Package References ────────────────────────────────────
        if (project.Packages.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in project.Packages)
            {
                sb.AppendLine($"    <PackageReference Include=\"{pkg.Id}\" Version=\"{pkg.Version}\" />");
            }
            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine();
        }

        sb.AppendLine("</Project>");

        await File.WriteAllTextAsync(project.ProjectFilePath, sb.ToString(), Encoding.UTF8);
    }



    /// <summary>
    /// Generate source files based on template
    /// </summary>
    private async Task GenerateSourceFilesAsync(NanoProject project, NanoProjectTemplate template)
    {
        var ns = SanitizeNamespace(project.Name);

        switch (template)
        {
            case NanoProjectTemplate.BlankApp:
                await WriteMainProgramAsync(project.Path, ns, GetBlankAppCode(ns));
                break;
            case NanoProjectTemplate.ClassLibrary:
                await WriteClassLibAsync(project.Path, ns);
                break;
            case NanoProjectTemplate.GpioBlink:
                await WriteMainProgramAsync(project.Path, ns, GetGpioBlinkCode(ns));
                break;
            case NanoProjectTemplate.WiFiConnect:
                await WriteMainProgramAsync(project.Path, ns, GetWiFiConnectCode(ns));
                break;
            case NanoProjectTemplate.HttpClient:
                await WriteMainProgramAsync(project.Path, ns, GetHttpClientCode(ns));
                break;
            case NanoProjectTemplate.I2CSensor:
                await WriteMainProgramAsync(project.Path, ns, GetI2CSensorCode(ns));
                break;
        }
    }

    private async Task WriteMainProgramAsync(string projectDir, string ns, string code)
    {
        var filePath = Path.Combine(projectDir, "Program.cs");
        await File.WriteAllTextAsync(filePath, code, Encoding.UTF8);
    }

    private async Task WriteClassLibAsync(string projectDir, string ns)
    {
        var code = $@"namespace {ns}
{{
    /// <summary>
    /// Sample class for nanoFramework class library
    /// </summary>
    public class Class1
    {{
        /// <summary>
        /// Sample method
        /// </summary>
        public static int Add(int a, int b)
        {{
            return a + b;
        }}
    }}
}}
";
        var filePath = Path.Combine(projectDir, "Class1.cs");
        await File.WriteAllTextAsync(filePath, code, Encoding.UTF8);
    }


    /// <summary>
    /// Add the nanoFramework project to an existing solution
    /// </summary>
    private async Task AddToSolutionAsync(string solutionPath, NanoProject project)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = SettingsPanelControl.ResolveDotNetExe(),
                    Arguments = $"sln \"{solutionPath}\" add \"{project.ProjectFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Debug.WriteLine($"Warning: Could not add project to solution via dotnet CLI. Manual add may be needed.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error adding project to solution: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore NuGet packages for the nanoFramework project.
    /// Uses dotnet restore for modern SDK-style .csproj projects.
    /// </summary>
    private async Task RestoreNuGetPackagesAsync(NanoProject project)
    {
        try
        {
            Debug.WriteLine($"Restoring NuGet packages for {project.Name}...");

            // Use dotnet restore directly for SDK-style .csproj
            var success = await RunRestoreCommandAsync(
                "dotnet",
                $"restore \"{project.ProjectFilePath}\"",
                project.Path);

            if (success)
            {
                Debug.WriteLine($"NuGet packages restored successfully for {project.Name}.");
            }
            else
            {
                Debug.WriteLine($"Warning: NuGet restore failed for {project.Name}. Packages may need to be restored manually.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error restoring NuGet packages: {ex.Message}");
        }
    }

    /// <summary>
    /// Run a restore command (dotnet restore or nuget restore) and return whether it succeeded.
    /// </summary>
    private async Task<bool> RunRestoreCommandAsync(string fileName, string arguments, string workingDirectory)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(stdout))
                Debug.WriteLine($"[NuGet Restore] {stdout}");
            if (!string.IsNullOrWhiteSpace(stderr))
                Debug.WriteLine($"[NuGet Restore Error] {stderr}");

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to run {fileName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a project file is a nanoFramework project
    /// </summary>
    public static bool IsNanoFrameworkProject(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath)) return false;
        
        var ext = Path.GetExtension(projectPath).ToLowerInvariant();
        if (ext == ".nfproj") return true;

        // Check for nanoFramework markers in .csproj
        if (ext == ".csproj" && File.Exists(projectPath))
        {
            try
            {
                var content = File.ReadAllText(projectPath);
                return content.Contains("<NanoFrameworkProject>true</NanoFrameworkProject>", StringComparison.OrdinalIgnoreCase) ||
                       content.Contains("nanoFramework", StringComparison.OrdinalIgnoreCase) ||
                       content.Contains("NFProjectSystem", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Find .nfproj files in a directory
    /// </summary>
    public static string[] FindNfprojFiles(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        return Directory.GetFiles(directory, "*.nfproj", SearchOption.AllDirectories);
    }

    private string SanitizeNamespace(string name)
    {
        return name.Replace(" ", "_").Replace("-", "_").Replace(".", "_");
    }
    
    /// <summary>
    /// Get the actual DLL filename for a nanoFramework NuGet package.
    /// Some packages have DLL names that differ from their package ID.
    /// </summary>
    public static string GetNanoFrameworkDllName(string packageId)
    {
        // Known mappings where the DLL name differs from the package ID
        var knownMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "nanoFramework.CoreLibrary", "mscorlib.dll" },
        };
        
        if (knownMappings.TryGetValue(packageId, out var dllName))
            return dllName;
        
        // For "nanoFramework.System.X" packages, the DLL is usually "System.X.dll"
        if (packageId.StartsWith("nanoFramework.System.", StringComparison.OrdinalIgnoreCase))
            return packageId.Substring("nanoFramework.".Length) + ".dll";
        
        // For "nanoFramework.Runtime.Events" -> "nanoFramework.Runtime.Events.dll"
        // Default: use the package ID as-is
        return packageId + ".dll";
    }
    
    /// <summary>
    /// Resolve the actual HintPath for a nanoFramework NuGet package DLL.
    /// Official nanoFramework convention: ..\\packages\\{id}.{version}\\lib\\{dll}
    /// Packages are restored one level above the project directory.
    /// </summary>
    public static string ResolveNuGetHintPath(string projectDir, string packageId, string version, string dllName)
    {
        var parentDir = Path.GetDirectoryName(projectDir) ?? projectDir;
        var packagesDir = Path.Combine(parentDir, "packages");
        var packageDir = Path.Combine(packagesDir, $"{packageId}.{version}");
        var libDir = Path.Combine(packageDir, "lib");
        
        // If the packages directory exists, search for the actual DLL
        if (Directory.Exists(libDir))
        {
            // DLL might be directly in lib/ (flat layout, common for nanoFramework)
            if (File.Exists(Path.Combine(libDir, dllName)))
                return $"..\\packages\\{packageId}.{version}\\lib\\{dllName}";

            // Probe known target framework subdirectories
            var tfmPriority = new[] { "netnano1.0", "netnanoframework1.0", "netnanoframework10" };
            foreach (var tfm in tfmPriority)
            {
                var candidate = Path.Combine(libDir, tfm, dllName);
                if (File.Exists(candidate))
                    return $"..\\packages\\{packageId}.{version}\\lib\\{tfm}\\{dllName}";
            }
            
            // Search any subdirectory for the DLL
            try
            {
                var found = Directory.GetFiles(libDir, dllName, SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    var relativePath = Path.GetRelativePath(projectDir, found[0]);
                    return relativePath.Replace("/", "\\");
                }
            }
            catch { /* Ignore search errors */ }
        }
        
        // Packages not restored yet — use the official flat lib layout as default
        return $"..\\packages\\{packageId}.{version}\\lib\\{dllName}";
    }

    #region Template Source Code

    private string GetBlankAppCode(string ns) => $@"using System;
using System.Diagnostics;
using System.Threading;

namespace {ns}
{{
    public class Program
    {{
        public static void Main()
        {{
            Debug.WriteLine(""Hello from nanoFramework!"");

            // Keep the application running
            Thread.Sleep(Timeout.Infinite);
        }}
    }}
}}
";

    private string GetGpioBlinkCode(string ns) => $@"using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Threading;

namespace {ns}
{{
    public class Program
    {{
        // Пін 2 — стандарт для вбудованого світлодіода на більшості плат ESP32
        private const int LED_PIN = 2;

        public static void Main()
        {{
            Debug.WriteLine(""Запуск Blink на nanoFramework v2..."");

            var controller = new GpioController();
            var led = controller.OpenPin(LED_PIN, PinMode.Output);

            while (true)
            {{
                led.Write(PinValue.High);
                Debug.WriteLine(""LED Увімкнено"");
                Thread.Sleep(500);

                led.Write(PinValue.Low);
                Debug.WriteLine(""LED Вимкнено"");
                Thread.Sleep(500);
            }}
        }}
    }}
}}
";

    private string GetWiFiConnectCode(string ns) => $@"using System;
using System.Device.Wifi;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;

namespace {ns}
{{
    public class Program
    {{
        // Wi-Fi credentials - change these to your network
        private const string WIFI_SSID = ""YourSSID"";
        private const string WIFI_PASSWORD = ""YourPassword"";

        public static void Main()
        {{
            Debug.WriteLine(""Wi-Fi Connect - nanoFramework"");

            // Get the first WiFi adapter
            var wifiAdapter = WifiAdapter.FindAllAdapters()[0];

            Debug.WriteLine($""Connecting to {{WIFI_SSID}}..."");

            var result = wifiAdapter.Connect(
                WIFI_SSID, 
                WifiReconnectionKind.Automatic, 
                WIFI_PASSWORD);

            if (result.ConnectionStatus == WifiConnectionStatus.Success)
            {{
                Debug.WriteLine(""Connected to Wi-Fi!"");
                var networkInterface = NetworkInterface.GetAllNetworkInterfaces()[0];
                Debug.WriteLine($""IP Address: {{networkInterface.IPv4Address}}"");
            }}
            else
            {{
                Debug.WriteLine($""Failed to connect: {{result.ConnectionStatus}}"");
            }}

            Thread.Sleep(Timeout.Infinite);
        }}
    }}
}}
";

    private string GetHttpClientCode(string ns) => $@"using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;

namespace {ns}
{{
    public class Program
    {{
        public static void Main()
        {{
            Debug.WriteLine(""HTTP Client - nanoFramework"");

            // Note: Connect to Wi-Fi first before making HTTP requests
            
            using (var httpClient = new HttpClient())
            {{
                try
                {{
                    var response = httpClient.Get(""http://httpbin.org/get"");
                    response.EnsureSuccessStatusCode();
                    
                    var content = response.Content.ReadAsString();
                    Debug.WriteLine($""Response: {{content}}"");
                }}
                catch (Exception ex)
                {{
                    Debug.WriteLine($""HTTP Error: {{ex.Message}}"");
                }}
            }}

            Thread.Sleep(Timeout.Infinite);
        }}
    }}
}}
";

    private string GetI2CSensorCode(string ns) => $@"using System;
using System.Device.I2c;
using System.Diagnostics;
using System.Threading;

namespace {ns}
{{
    public class Program
    {{
        // I2C address of your sensor (common addresses: 0x76, 0x77 for BME280)
        private const int SENSOR_ADDRESS = 0x76;
        private const int I2C_BUS = 1;

        public static void Main()
        {{
            Debug.WriteLine(""I2C Sensor - nanoFramework"");

            var i2cSettings = new I2cConnectionSettings(I2C_BUS, SENSOR_ADDRESS);
            var i2cDevice = I2cDevice.Create(i2cSettings);

            // Read sensor ID register (register 0xD0 for BME280)
            var writeBuffer = new byte[] {{ 0xD0 }};
            var readBuffer = new byte[1];

            i2cDevice.WriteRead(writeBuffer, readBuffer);
            Debug.WriteLine($""Sensor ID: 0x{{readBuffer[0]:X2}}"");

            // Continuous reading loop
            while (true)
            {{
                try
                {{
                    // Read data from sensor
                    // Implement sensor-specific reading logic here
                    Debug.WriteLine(""Reading sensor data..."");
                }}
                catch (Exception ex)
                {{
                    Debug.WriteLine($""I2C Error: {{ex.Message}}"");
                }}

                Thread.Sleep(1000);
            }}
        }}
    }}
}}
";

    #endregion
}

