using System;
using System.Collections.Generic;

namespace Insait_Edit_C_Sharp.Esp.Models;

/// <summary>
/// Represents a nanoFramework project targeting ESP microcontrollers
/// </summary>
public class NanoProject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ProjectFilePath { get; set; } = string.Empty;
    public NanoProjectTemplate Template { get; set; } = NanoProjectTemplate.BlankApp;
    public string TargetBoard { get; set; } = EspBoardTypes.ESP32;
    public string? ComPort { get; set; }
    public string NanoFrameworkVersion { get; set; } = "2.0.0-preview.35";
    public List<NanoNuGetPackage> Packages { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.Now;
}

public enum NanoProjectTemplate
{
    BlankApp,
    ClassLibrary,
    GpioBlink,
    WiFiConnect,
    HttpClient,
    I2CSensor
}

public class NanoNuGetPackage
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public NanoNuGetPackage() { }
    public NanoNuGetPackage(string id, string version) { Id = id; Version = version; }
}

public class NanoTemplateInfo
{
    public NanoProjectTemplate Template { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🔌";
    public List<NanoNuGetPackage> RequiredPackages { get; set; } = new();

    /// <summary>
    /// Core packages included in every ESP32 nanoFramework project template.
    /// These three packages are mandatory for all templates:
    ///   - nanoFramework.Runtime.Native   : native runtime interop (NativeEventDispatcher, etc.)
    ///   - nanoFramework.Hardware.Esp32   : ESP32-specific APIs (sleep, touch, Hall sensor, …)
    ///   - nanoFramework.System.Device.Gpio : GPIO pin access (required by nearly every project)
    /// </summary>
    private static List<NanoNuGetPackage> CoreEsp32Packages => new()
    {
        new("nanoFramework.CoreLibrary",          "2.0.0-preview.35"),
        new("nanoFramework.Runtime.Native",       "2.0.0-preview.5"),
        new("nanoFramework.Hardware.Esp32",       "2.0.0-preview.1"),
        new("nanoFramework.System.Device.Gpio",   "2.0.0-preview.9"),
    };

    public static List<NanoTemplateInfo> GetAllTemplates()
    {
        return new List<NanoTemplateInfo>
        {
            new()
            {
                Template = NanoProjectTemplate.BlankApp, Name = "Blank App",
                Description = "Empty nanoFramework application for ESP32", Icon = "📱",
                RequiredPackages = CoreEsp32Packages
            },
            new()
            {
                Template = NanoProjectTemplate.ClassLibrary, Name = "Class Library",
                Description = "Reusable class library for nanoFramework", Icon = "📚",
                RequiredPackages = CoreEsp32Packages
            },
            new()
            {
                Template = NanoProjectTemplate.GpioBlink, Name = "GPIO Blink",
                Description = "LED blink example using GPIO pins", Icon = "💡",
                RequiredPackages = CoreEsp32Packages   // GPIO already included in core
            },
            new()
            {
                Template = NanoProjectTemplate.WiFiConnect, Name = "Wi-Fi Connect",
                Description = "Wi-Fi connection example for ESP32", Icon = "📡",
                RequiredPackages = new(CoreEsp32Packages)
                {
                    new("nanoFramework.System.Device.Wifi", "2.0.0-preview.7"),
                    new("nanoFramework.Runtime.Events",     "2.0.1"),
                    new("nanoFramework.System.Net",         "2.0.0-preview.1")
                }
            },
            new()
            {
                Template = NanoProjectTemplate.HttpClient, Name = "HTTP Client",
                Description = "HTTP client example with Wi-Fi", Icon = "🌐",
                RequiredPackages = new(CoreEsp32Packages)
                {
                    new("nanoFramework.System.Device.Wifi", "2.0.0-preview.7"),
                    new("nanoFramework.System.Net.Http",    "2.0.0-preview.8"),
                    new("nanoFramework.Runtime.Events",     "2.0.1")
                }
            },
            new()
            {
                Template = NanoProjectTemplate.I2CSensor, Name = "I2C Sensor",
                Description = "I2C sensor reading example", Icon = "🌡️",
                RequiredPackages = new(CoreEsp32Packages)
                {
                    new("nanoFramework.System.Device.I2c", "2.0.0-preview.5"),
                    new("nanoFramework.Runtime.Events",    "2.0.1")
                }
            }
        };
    }
}

