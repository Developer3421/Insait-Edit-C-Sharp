using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Insait_Edit_C_Sharp.Esp.Models;

namespace Insait_Edit_C_Sharp.Esp.Services;

/// <summary>
/// Service for deploying firmware and applications to ESP devices using nanoff CLI tool
/// </summary>
public class NanoDeployService
{
    public event EventHandler<string>? OutputReceived;
    public event EventHandler<bool>? DeployCompleted;

    private const string NANOFF_TOOL = "nanoff";

    /// <summary>
    /// Check if nanoff tool is installed
    /// </summary>
    public async Task<bool> IsNanoffInstalledAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = NANOFF_TOOL,
                    Arguments = "--help",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Install nanoff tool globally
    /// </summary>
    public async Task<bool> InstallNanoffAsync()
    {
        OnOutput("Installing nanoff tool...\n");

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "tool install -g nanoff",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[ERROR] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput(success ? "nanoff installed successfully!\n" : "Failed to install nanoff.\n");
            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Failed to install nanoff: {ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// Flash nanoFramework firmware to an ESP device
    /// </summary>
    public async Task<bool> FlashFirmwareAsync(string comPort, string targetBoard, bool updateFw = true)
    {
        OnOutput($"========== Flash Firmware ==========\n");
        OnOutput($"Board: {targetBoard}\n");
        OnOutput($"COM Port: {comPort}\n\n");

        if (!await EnsureNanoffAsync()) return false;

        try
        {
            var args = $"--target {targetBoard} --serialport {comPort}";
            if (updateFw) args += " --update";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = NANOFF_TOOL,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[WARN] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput($"\n========== Flash {(success ? "Succeeded" : "Failed")} ==========\n");
            DeployCompleted?.Invoke(this, success);
            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Flash failed: {ex.Message}\n");
            DeployCompleted?.Invoke(this, false);
            return false;
        }
    }

    /// <summary>
    /// Deploy compiled application to ESP device
    /// </summary>
    public async Task<bool> DeployAsync(string comPort, string deployFilePath)
    {
        OnOutput($"========== Deploy Application ==========\n");
        OnOutput($"COM Port: {comPort}\n");
        OnOutput($"File: {deployFilePath}\n\n");

        if (!await EnsureNanoffAsync()) return false;

        if (!File.Exists(deployFilePath))
        {
            OnOutput($"[ERROR] Deploy file not found: {deployFilePath}\n");
            return false;
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = NANOFF_TOOL,
                    Arguments = $"--deploy --serialport {comPort} --image \"{deployFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[WARN] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput($"\n========== Deploy {(success ? "Succeeded" : "Failed")} ==========\n");
            DeployCompleted?.Invoke(this, success);
            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Deploy failed: {ex.Message}\n");
            DeployCompleted?.Invoke(this, false);
            return false;
        }
    }

    /// <summary>
    /// Deploy all PE files (main assembly + dependencies) to ESP device.
    /// nanoFramework requires all PE files to be deployed together.
    /// </summary>
    public async Task<bool> DeployAllPeFilesAsync(string comPort, List<string> peFiles)
    {
        if (peFiles == null || peFiles.Count == 0)
        {
            OnOutput("[ERROR] No PE files to deploy.\n");
            return false;
        }

        OnOutput($"========== Deploy Application (PE Format) ==========\n");
        OnOutput($"COM Port: {comPort}\n");
        OnOutput($"PE files to deploy: {peFiles.Count}\n");
        foreach (var pe in peFiles)
        {
            OnOutput($"  → {Path.GetFileName(pe)} ({new FileInfo(pe).Length:N0} bytes)\n");
        }
        OnOutput("\n");

        if (!await EnsureNanoffAsync()) return false;

        // Verify all files exist
        var missingFiles = peFiles.Where(f => !File.Exists(f)).ToList();
        if (missingFiles.Count > 0)
        {
            OnOutput("[ERROR] Missing PE files:\n");
            foreach (var f in missingFiles)
            {
                OnOutput($"  ✗ {f}\n");
            }
            return false;
        }

        try
        {
            // Build image arguments for all PE files
            var imageArgs = string.Join(" ", peFiles.Select(f => $"--image \"{f}\""));
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = NANOFF_TOOL,
                    Arguments = $"--deploy --serialport {comPort} {imageArgs}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[WARN] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput($"\n========== PE Deploy {(success ? "Succeeded" : "Failed")} ==========\n");
            if (success)
            {
                OnOutput($"Successfully deployed {peFiles.Count} PE file(s) to {comPort}\n");
            }
            DeployCompleted?.Invoke(this, success);
            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] PE deploy failed: {ex.Message}\n");
            DeployCompleted?.Invoke(this, false);
            return false;
        }
    }

    /// <summary>
    /// List available COM ports that may have ESP devices
    /// </summary>
    public async Task<List<EspDevice>> ListDevicesAsync()
    {
        var devices = new List<EspDevice>();

        try
        {
            // Use nanoff to list devices
            if (await IsNanoffInstalledAsync())
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = NANOFF_TOOL,
                        Arguments = "--listports",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse COM ports from output
                var comPortRegex = new Regex(@"(COM\d+)", RegexOptions.IgnoreCase);
                var matches = comPortRegex.Matches(output);

                foreach (Match match in matches)
                {
                    devices.Add(new EspDevice
                    {
                        ComPort = match.Value,
                        IsConnected = true,
                        LastSeen = DateTime.Now
                    });
                }
            }

            // If nanoff didn't find anything, try to enumerate COM ports directly
            if (devices.Count == 0)
            {
                devices = await EnumerateComPortsAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error listing devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Enumerate available COM ports on the system
    /// </summary>
    private async Task<List<EspDevice>> EnumerateComPortsAsync()
    {
        var devices = new List<EspDevice>();

        try
        {
            // Use PowerShell to get COM ports with descriptions
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"Get-WMIObject Win32_SerialPort | Select-Object DeviceID, Description | Format-List\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse output
            var lines = output.Split('\n');
            string? currentPort = null;
            string? currentDesc = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("DeviceID"))
                {
                    currentPort = trimmed.Split(':').LastOrDefault()?.Trim();
                }
                else if (trimmed.StartsWith("Description"))
                {
                    currentDesc = trimmed.Split(':').LastOrDefault()?.Trim();
                }

                if (currentPort != null)
                {
                    var isEsp = currentDesc?.Contains("USB", StringComparison.OrdinalIgnoreCase) == true ||
                                currentDesc?.Contains("Serial", StringComparison.OrdinalIgnoreCase) == true ||
                                currentDesc?.Contains("CP210", StringComparison.OrdinalIgnoreCase) == true ||
                                currentDesc?.Contains("CH340", StringComparison.OrdinalIgnoreCase) == true ||
                                currentDesc?.Contains("FTDI", StringComparison.OrdinalIgnoreCase) == true;

                    devices.Add(new EspDevice
                    {
                        ComPort = currentPort,
                        Description = currentDesc,
                        IsConnected = true,
                        LastSeen = DateTime.Now,
                        BoardType = isEsp ? "ESP32" : "Unknown"
                    });

                    currentPort = null;
                    currentDesc = null;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating COM ports: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Erase flash on the ESP device
    /// </summary>
    public async Task<bool> EraseFlashAsync(string comPort)
    {
        OnOutput($"========== Erase Flash on {comPort} ==========\n");

        if (!await EnsureNanoffAsync()) return false;

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = NANOFF_TOOL,
                    Arguments = $"--serialport {comPort} --masserase",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput(e.Data + "\n");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) OnOutput($"[WARN] {e.Data}\n");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            OnOutput($"\n========== Erase {(success ? "Succeeded" : "Failed")} ==========\n");
            return success;
        }
        catch (Exception ex)
        {
            OnOutput($"[ERROR] Erase failed: {ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// Ensure nanoff is installed, install if not
    /// </summary>
    private async Task<bool> EnsureNanoffAsync()
    {
        if (await IsNanoffInstalledAsync()) return true;

        OnOutput("nanoff tool not found. Installing...\n");
        return await InstallNanoffAsync();
    }

    private void OnOutput(string message)
    {
        OutputReceived?.Invoke(this, message);
    }
}

