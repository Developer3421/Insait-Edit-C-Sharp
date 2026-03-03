using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Esp.Models;

namespace Insait_Edit_C_Sharp.Esp.Services;

/// <summary>
/// Generates nanoFramework C# code for ESP32 LED panel control
/// </summary>
public class LedPanelCodeGenerator
{
    /// <summary>
    /// Generate complete ESP32 nanoFramework LED panel controller code
    /// </summary>
    public string GenerateLedPanelCode(LedPanelConfig config, string namespaceName = "LedPanelApp")
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Device.Gpio;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// LED Panel Controller — generated from LED Designer.");
        sb.AppendLine("    /// Call LedPanelController.Run() from Program.Main().");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class LedPanelController");
        sb.AppendLine("    {");
        sb.AppendLine($"        // LED Panel Configuration: {config.Rows}x{config.Columns}");
        sb.AppendLine($"        private const int ROWS = {config.Rows};");
        sb.AppendLine($"        private const int COLS = {config.Columns};");
        sb.AppendLine($"        private const int TOTAL_LEDS = {config.TotalLeds};");
        sb.AppendLine($"        private const int LED_PIN = {config.GpioPin};");
        sb.AppendLine($"        private const int BRIGHTNESS = {config.Brightness};");
        sb.AppendLine();
        sb.AppendLine("        // Color data: R, G, B interleaved for each LED (flat array, nanoFramework compatible)");
        sb.AppendLine("        private static byte[] _ledData = new byte[TOTAL_LEDS * 3];");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Run the LED panel (call this from Program.Main)");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static void Run()");
        sb.AppendLine("        {");
        sb.AppendLine("            // Initialize GPIO for LED strip (nanoFramework API)");
        sb.AppendLine("            var gpio = new GpioController();");
        sb.AppendLine("            var ledPin = gpio.OpenPin(LED_PIN, PinMode.Output);");
        sb.AppendLine();
        sb.AppendLine("            // Set initial pattern");
        sb.AppendLine("            SetInitialPattern();");
        sb.AppendLine();

        if (config.Patterns.Count > 0)
        {
            var firstPattern = config.Patterns[0];
            var loopComment = firstPattern.InfiniteLoop
                ? "// Run animation patterns — INFINITE LOOP (never exits)"
                : $"// Run animation patterns — loops {(firstPattern.DelayMs > 0 ? firstPattern.DelayMs : 10)} time(s) then stops";

            sb.AppendLine($"            {loopComment}");
            sb.AppendLine("            RunPatterns(ledPin);");

            if (!firstPattern.InfiniteLoop)
            {
                sb.AppendLine();
                sb.AppendLine("            // Keep device alive after animation ends");
                sb.AppendLine("            Thread.Sleep(Timeout.Infinite);");
            }
        }
        else
        {
            sb.AppendLine("            // Display static pattern");
            sb.AppendLine("            SendLedData(ledPin);");
            sb.AppendLine();
            sb.AppendLine("            // Keep running");
            sb.AppendLine("            Thread.Sleep(Timeout.Infinite);");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        GenerateSetInitialPattern(sb, config);

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Convert row,col to LED index (supports zigzag wiring)");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private static int GetLedIndex(int row, int col)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (row % 2 == 0)");
        sb.AppendLine("                return row * COLS + col;");
        sb.AppendLine("            else");
        sb.AppendLine("                return row * COLS + (COLS - 1 - col);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static void SetLed(int row, int col, byte r, byte g, byte b)");
        sb.AppendLine("        {");
        sb.AppendLine("            int index = GetLedIndex(row, col);");
        sb.AppendLine("            if (index >= 0 && index < TOTAL_LEDS)");
        sb.AppendLine("            {");
        sb.AppendLine("                _ledData[index * 3 + 0] = (byte)((r * BRIGHTNESS) / 255);");
        sb.AppendLine("                _ledData[index * 3 + 1] = (byte)((g * BRIGHTNESS) / 255);");
        sb.AppendLine("                _ledData[index * 3 + 2] = (byte)((b * BRIGHTNESS) / 255);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static void SetRow(int row, byte r, byte g, byte b)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (int col = 0; col < COLS; col++)");
        sb.AppendLine("                SetLed(row, col, r, g, b);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static void SetColumn(int col, byte r, byte g, byte b)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (int row = 0; row < ROWS; row++)");
        sb.AppendLine("                SetLed(row, col, r, g, b);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static void ClearAll()");
        sb.AppendLine("        {");
        sb.AppendLine("            for (int i = 0; i < TOTAL_LEDS * 3; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                _ledData[i] = 0;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static void SendLedData(GpioPin pin)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (int i = 0; i < TOTAL_LEDS; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                SendByte(pin, _ledData[i * 3 + 1]); // G");
        sb.AppendLine("                SendByte(pin, _ledData[i * 3 + 0]); // R");
        sb.AppendLine("                SendByte(pin, _ledData[i * 3 + 2]); // B");
        sb.AppendLine("            }");
        sb.AppendLine("            pin.Write(PinValue.Low);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static void SendByte(GpioPin pin, byte data)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (int bit = 7; bit >= 0; bit--)");
        sb.AppendLine("            {");
        sb.AppendLine("                if ((data & (1 << bit)) != 0)");
        sb.AppendLine("                {");
        sb.AppendLine("                    pin.Write(PinValue.High);");
        sb.AppendLine("                    Thread.SpinWait(10);");
        sb.AppendLine("                    pin.Write(PinValue.Low);");
        sb.AppendLine("                    Thread.SpinWait(5);");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    pin.Write(PinValue.High);");
        sb.AppendLine("                    Thread.SpinWait(5);");
        sb.AppendLine("                    pin.Write(PinValue.Low);");
        sb.AppendLine("                    Thread.SpinWait(10);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        if (config.Patterns.Count > 0)
        {
            GeneratePatternCode(sb, config);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private void GenerateSetInitialPattern(StringBuilder sb, LedPanelConfig config)
    {
        sb.AppendLine("        private static void SetInitialPattern()");
        sb.AppendLine("        {");
        sb.AppendLine("            ClearAll();");

        foreach (var rowColor in config.RowColors)
        {
            var (r, g, b) = ParseColor(rowColor.Value);
            sb.AppendLine($"            SetRow({rowColor.Key}, {r}, {g}, {b});");
        }

        foreach (var colColor in config.ColumnColors)
        {
            var (r, g, b) = ParseColor(colColor.Value);
            sb.AppendLine($"            SetColumn({colColor.Key}, {r}, {g}, {b});");
        }

        foreach (var led in config.LedColors)
        {
            var parts = led.Key.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out var row) && int.TryParse(parts[1], out var col))
            {
                var (r, g, b) = ParseColor(led.Value);
                sb.AppendLine($"            SetLed({row}, {col}, {r}, {g}, {b});");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void GeneratePatternCode(StringBuilder sb, LedPanelConfig config)
    {
        sb.AppendLine("        private static void RunPatterns(GpioPin pin)");
        sb.AppendLine("        {");

        for (int p = 0; p < config.Patterns.Count; p++)
        {
            var pattern = config.Patterns[p];
            sb.AppendLine($"            // Pattern: {pattern.Name}");

            if (pattern.Loop)
            {
                if (pattern.InfiniteLoop)
                {
                    sb.AppendLine("            while (true)");
                }
                else
                {
                    int loopCount = pattern.DelayMs > 0 ? pattern.DelayMs : 10;
                    sb.AppendLine($"            for (int loop = 0; loop < {loopCount}; loop++)");
                }
                sb.AppendLine("            {");
            }

            for (int f = 0; f < pattern.Frames.Count; f++)
            {
                var frame = pattern.Frames[f];
                var indent = pattern.Loop ? "                " : "            ";
                sb.AppendLine($"{indent}ClearAll();");

                foreach (var led in frame.LedColors)
                {
                    var parts = led.Key.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var row) && int.TryParse(parts[1], out var col))
                    {
                        var (r, g, b2) = ParseColor(led.Value);
                        sb.AppendLine($"{indent}SetLed({row}, {col}, {r}, {g}, {b2});");
                    }
                }

                sb.AppendLine($"{indent}SendLedData(pin);");
                sb.AppendLine($"{indent}Thread.Sleep({frame.DurationMs});");
            }

            if (pattern.Loop)
            {
                sb.AppendLine("            }");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private (byte r, byte g, byte b) ParseColor(string hexColor)
    {
        try
        {
            var color = hexColor.TrimStart('#');
            if (color.Length == 8)
                color = color.Substring(2);

            var r = Convert.ToByte(color.Substring(0, 2), 16);
            var g = Convert.ToByte(color.Substring(2, 2), 16);
            var b = Convert.ToByte(color.Substring(4, 2), 16);
            return (r, g, b);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// Save generated C# code to a project directory.
    /// All LED pattern data is embedded directly in the generated C# file —
    /// no JSON config file is written, because nanoFramework MSBuild cannot handle JSON files.
    /// NuGet packages (nanoFramework.System.Device.Gpio etc.) are managed by NanoProjectService.
    /// </summary>
    public async Task SaveToProjectAsync(LedPanelConfig config, string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        if (projectDir == null) return;

        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var ns = projectName.Replace(" ", "_").Replace("-", "_");

        var code = GenerateLedPanelCode(config, ns);
        var filePath = Path.Combine(projectDir, "LedPanelController.cs");
        await File.WriteAllTextAsync(filePath, code);

        // Note: led_panel_config.json is intentionally NOT saved here.
        // nanoFramework MSBuild fails when JSON files are present in the project.
        // All configuration data is embedded as C# constants/arrays in LedPanelController.cs.
    }
}
