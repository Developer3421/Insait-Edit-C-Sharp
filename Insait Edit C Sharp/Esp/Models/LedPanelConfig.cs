using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Insait_Edit_C_Sharp.Esp.Models;

/// <summary>
/// Configuration for an LED matrix panel
/// </summary>
public class LedPanelConfig
{
    public int Rows { get; set; } = 8;
    public int Columns { get; set; } = 8;
    public int GpioPin { get; set; } = 18;
    public LedStripType StripType { get; set; } = LedStripType.WS2812B;
    public int Brightness { get; set; } = 50; // 0-255
    public string Name { get; set; } = "LED Panel";

    /// <summary>
    /// LED states stored as [row, col] = color (ARGB hex string like "#FFFF0000")
    /// </summary>
    public Dictionary<string, string> LedColors { get; set; } = new();

    /// <summary>
    /// Row-level settings (apply color to entire row)
    /// </summary>
    public Dictionary<int, string> RowColors { get; set; } = new();

    /// <summary>
    /// Column-level settings (apply color to entire column)
    /// </summary>
    public Dictionary<int, string> ColumnColors { get; set; } = new();

    /// <summary>
    /// Animation patterns
    /// </summary>
    public List<LedPattern> Patterns { get; set; } = new();

    public int TotalLeds => Rows * Columns;

    public string GetLedKey(int row, int col) => $"{row},{col}";

    public string? GetLedColor(int row, int col)
    {
        var key = GetLedKey(row, col);
        if (LedColors.TryGetValue(key, out var color))
            return color;
        return null;
    }

    public void SetLedColor(int row, int col, string color)
    {
        var key = GetLedKey(row, col);
        LedColors[key] = color;
    }

    public void ClearLed(int row, int col)
    {
        var key = GetLedKey(row, col);
        LedColors.Remove(key);
    }

    public void SetRowColor(int row, string color)
    {
        RowColors[row] = color;
        for (int col = 0; col < Columns; col++)
        {
            SetLedColor(row, col, color);
        }
    }

    public void SetColumnColor(int col, string color)
    {
        ColumnColors[col] = color;
        for (int row = 0; row < Rows; row++)
        {
            SetLedColor(row, col, color);
        }
    }

    public void ClearAll()
    {
        LedColors.Clear();
        RowColors.Clear();
        ColumnColors.Clear();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    public static LedPanelConfig? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LedPanelConfig>(json);
        }
        catch
        {
            return null;
        }
    }
}

public enum LedStripType
{
    WS2812B,
    WS2811,
    SK6812,
    APA102,
    NeoPixel
}

/// <summary>
/// Represents an animation pattern frame
/// </summary>
public class LedPattern
{
    public string Name { get; set; } = "Pattern";
    public List<LedFrame> Frames { get; set; } = new();
    public int DelayMs { get; set; } = 100;
    public bool Loop { get; set; } = true;

    /// <summary>
    /// When true, the generated nanoFramework code will loop forever (while(true))
    /// instead of a fixed number of iterations.
    /// </summary>
    public bool InfiniteLoop { get; set; } = false;
}

/// <summary>
/// Single frame in an animation pattern
/// </summary>
public class LedFrame
{
    public int DurationMs { get; set; } = 100;
    public Dictionary<string, string> LedColors { get; set; } = new();

    public void CopyFromConfig(LedPanelConfig config)
    {
        LedColors = new Dictionary<string, string>(config.LedColors);
    }
}

