using System;

namespace Insait_Edit_C_Sharp.Esp.Models;

/// <summary>
/// Represents a connected ESP device
/// </summary>
public class EspDevice
{
    public string ComPort { get; set; } = string.Empty;
    public string BoardType { get; set; } = "ESP32";
    public string? FirmwareVersion { get; set; }
    public bool IsConnected { get; set; }
    public string? Description { get; set; }
    public DateTime? LastSeen { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Description)
        ? $"{BoardType} ({ComPort})"
        : $"{Description} ({ComPort})";
}

/// <summary>
/// Supported ESP32 board types for nanoFramework
/// </summary>
public static class EspBoardTypes
{
    public const string ESP32 = "ESP32";
    public const string ESP32_S3 = "ESP32_S3";
    public const string ESP32_C3 = "ESP_WROVER_KIT";
    public const string ESP32_WROVER = "ESP32_WROVER_KIT";
    public const string ESP32_S2 = "ESP32_S2";
    public const string ESP32_PICO = "ESP32_PICO";
    public const string M5Stack = "M5Stack";
    public const string M5StickC = "M5StickC";
    public const string M5StickCPlus = "M5StickCPlus";

    public static readonly string[] All =
    {
        ESP32, ESP32_S3, ESP32_C3, ESP32_WROVER, ESP32_S2, ESP32_PICO,
        M5Stack, M5StickC, M5StickCPlus
    };

    public static string GetDescription(string boardType)
    {
        return boardType switch
        {
            ESP32 => "ESP32 DevKit (generic)",
            ESP32_S3 => "ESP32-S3 (Wi-Fi + BLE 5)",
            ESP32_C3 => "ESP32-C3 (RISC-V)",
            ESP32_WROVER => "ESP32-WROVER (PSRAM)",
            ESP32_S2 => "ESP32-S2 (USB native)",
            ESP32_PICO => "ESP32-PICO-D4 (compact)",
            M5Stack => "M5Stack Core",
            M5StickC => "M5StickC",
            M5StickCPlus => "M5StickC Plus",
            _ => boardType
        };
    }
}

