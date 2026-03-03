using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Insait_Edit_C_Sharp.Esp.Models;
using Insait_Edit_C_Sharp.Esp.Services;

namespace Insait_Edit_C_Sharp.Esp.Controls;

public partial class DevicePanelControl : UserControl
{
    private readonly NanoDeployService _deployService;
    private readonly NanoBuildService _buildService;
    private readonly SerialMonitorService _serialMonitor;
    private string? _projectPath;
    private string? _targetBoard;
    private bool _isSerialMonitorOpen;
    private readonly StringBuilder _serialOutput = new();

    /// <summary>
    /// Event raised when build output is available
    /// </summary>
    public event EventHandler<string>? OutputReceived;

    public DevicePanelControl()
    {
        InitializeComponent();

        _deployService = new NanoDeployService();
        _buildService = new NanoBuildService();
        _serialMonitor = new SerialMonitorService();

        // Wire up events
        _deployService.OutputReceived += (s, msg) => OnOutput(msg);
        _buildService.OutputReceived += (s, e) => OnOutput(e.Output);
        _serialMonitor.DataReceived += OnSerialDataReceived;
        _serialMonitor.ErrorReceived += (s, msg) => OnOutput($"[Serial Error] {msg}");
        _serialMonitor.ConnectionChanged += OnSerialConnectionChanged;

        // Populate baud rate combo
        var baudCombo = this.FindControl<ComboBox>("BaudRateComboBox");
        if (baudCombo != null)
        {
            foreach (var rate in SerialMonitorService.CommonBaudRates)
            {
                baudCombo.Items.Add(new ComboBoxItem { Content = rate.ToString(), Tag = rate });
            }
            baudCombo.SelectedIndex = 4; // 115200
        }

        // Load COM ports
        RefreshComPorts();
    }

    /// <summary>
    /// Set the current nanoFramework project path
    /// </summary>
    public void SetProject(string projectPath, string? targetBoard = null)
    {
        _projectPath = projectPath;
        _targetBoard = targetBoard ?? "ESP32";

        var boardInfo = this.FindControl<TextBlock>("BoardInfoText");
        if (boardInfo != null)
        {
            boardInfo.Text = $"Board: {_targetBoard}\n" +
                           $"Project: {System.IO.Path.GetFileName(projectPath)}";
        }
    }

    private async void RefreshComPorts()
    {
        var comboBox = this.FindControl<ComboBox>("ComPortComboBox");
        if (comboBox == null) return;

        comboBox.Items.Clear();
        comboBox.Items.Add(new ComboBoxItem { Content = "Scanning...", IsEnabled = false });

        try
        {
            // Quick scan using System.IO.Ports
            var portNames = SerialPort.GetPortNames();
            
            // Also try nanoff for detailed info
            var devices = await _deployService.ListDevicesAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                comboBox.Items.Clear();

                if (devices.Count > 0)
                {
                    foreach (var device in devices)
                    {
                        comboBox.Items.Add(new ComboBoxItem
                        {
                            Content = device.DisplayName,
                            Tag = device.ComPort
                        });
                    }
                }
                else if (portNames.Length > 0)
                {
                    // Fallback to basic COM port names
                    foreach (var port in portNames)
                    {
                        comboBox.Items.Add(new ComboBoxItem
                        {
                            Content = port,
                            Tag = port
                        });
                    }
                }
                else
                {
                    comboBox.Items.Add(new ComboBoxItem { Content = "No devices found", IsEnabled = false });
                }

                if (comboBox.Items.Count > 0 && comboBox.Items[0] is ComboBoxItem firstItem && firstItem.IsEnabled)
                {
                    comboBox.SelectedIndex = 0;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error refreshing ports: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                comboBox.Items.Clear();
                comboBox.Items.Add(new ComboBoxItem { Content = "Error scanning", IsEnabled = false });
            });
        }
    }

    private string? GetSelectedComPort()
    {
        var comboBox = this.FindControl<ComboBox>("ComPortComboBox");
        if (comboBox?.SelectedItem is ComboBoxItem item && item.Tag is string port)
        {
            return port;
        }
        return null;
    }

    private int GetSelectedBaudRate()
    {
        var comboBox = this.FindControl<ComboBox>("BaudRateComboBox");
        if (comboBox?.SelectedItem is ComboBoxItem item && item.Tag is int rate)
        {
            return rate;
        }
        return SerialMonitorService.DefaultBaudRate;
    }

    #region Event Handlers

    private void RefreshPorts_Click(object? sender, RoutedEventArgs e)
    {
        RefreshComPorts();
    }

    private async void FlashFirmware_Click(object? sender, RoutedEventArgs e)
    {
        var comPort = GetSelectedComPort();
        if (comPort == null)
        {
            OnOutput("Please select a COM port first.\n");
            return;
        }

        var board = _targetBoard ?? "ESP32";
        SetButtonsEnabled(false);
        OnOutput($"Flashing firmware for {board} on {comPort}...\n");

        try
        {
            await _deployService.FlashFirmwareAsync(comPort, board);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void BuildProject_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectPath))
        {
            OnOutput("No nanoFramework project loaded.\n");
            return;
        }

        SetButtonsEnabled(false);
        try
        {
            await _buildService.BuildAsync(_projectPath);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void DeployApp_Click(object? sender, RoutedEventArgs e)
    {
        var comPort = GetSelectedComPort();
        if (comPort == null)
        {
            OnOutput("Please select a COM port first.\n");
            return;
        }

        if (string.IsNullOrEmpty(_projectPath))
        {
            OnOutput("No nanoFramework project loaded.\n");
            return;
        }

        SetButtonsEnabled(false);
        try
        {
            // Find built PE file
            var peFile = FindBuiltPeFile();
            if (peFile == null)
            {
                OnOutput("No built PE file found. Build the project first.\n");
                return;
            }

            await _deployService.DeployAsync(comPort, peFile);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void BuildAndDeploy_Click(object? sender, RoutedEventArgs e)
    {
        var comPort = GetSelectedComPort();
        if (comPort == null)
        {
            OnOutput("Please select a COM port first.\n");
            return;
        }

        if (string.IsNullOrEmpty(_projectPath))
        {
            OnOutput("No nanoFramework project loaded.\n");
            return;
        }

        SetButtonsEnabled(false);
        try
        {
            // Build first
            var buildResult = await _buildService.BuildAsync(_projectPath);
            if (!buildResult.Success)
            {
                OnOutput("Build failed. Deploy aborted.\n");
                return;
            }

            // Then deploy
            var peFile = buildResult.OutputPePath ?? FindBuiltPeFile();
            if (peFile == null)
            {
                OnOutput("No PE file found after build.\n");
                return;
            }

            await _deployService.DeployAsync(comPort, peFile);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void EraseFlash_Click(object? sender, RoutedEventArgs e)
    {
        var comPort = GetSelectedComPort();
        if (comPort == null)
        {
            OnOutput("Please select a COM port first.\n");
            return;
        }

        SetButtonsEnabled(false);
        try
        {
            await _deployService.EraseFlashAsync(comPort);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void ToggleSerialMonitor_Click(object? sender, RoutedEventArgs e)
    {
        var outputBorder = this.FindControl<Border>("SerialOutputBorder");
        var inputGrid = this.FindControl<Grid>("SerialInputGrid");
        var buttonText = this.FindControl<TextBlock>("SerialMonitorButtonText");

        if (_isSerialMonitorOpen)
        {
            // Close serial monitor
            await _serialMonitor.DisconnectAsync();
            _isSerialMonitorOpen = false;

            if (outputBorder != null) outputBorder.IsVisible = false;
            if (inputGrid != null) inputGrid.IsVisible = false;
            if (buttonText != null) buttonText.Text = "Open Serial Monitor";
        }
        else
        {
            var comPort = GetSelectedComPort();
            if (comPort == null)
            {
                OnOutput("Please select a COM port first.\n");
                return;
            }

            var baudRate = GetSelectedBaudRate();

            // Open serial monitor
            _serialOutput.Clear();
            if (outputBorder != null) outputBorder.IsVisible = true;
            if (inputGrid != null) inputGrid.IsVisible = true;
            if (buttonText != null) buttonText.Text = "Close Serial Monitor";

            _isSerialMonitorOpen = true;
            await _serialMonitor.ConnectAsync(comPort, baudRate);
        }
    }

    private async void SendSerialCommand_Click(object? sender, RoutedEventArgs e)
    {
        var inputBox = this.FindControl<TextBox>("SerialInputBox");
        if (inputBox == null || string.IsNullOrEmpty(inputBox.Text)) return;

        await _serialMonitor.SendAsync(inputBox.Text);
        inputBox.Text = "";
    }

    #endregion

    #region Helpers

    private void OnSerialDataReceived(object? sender, string data)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _serialOutput.Append(data);

            // Limit buffer size
            if (_serialOutput.Length > 50000)
            {
                _serialOutput.Remove(0, _serialOutput.Length - 40000);
            }

            var outputText = this.FindControl<TextBlock>("SerialOutputText");
            if (outputText != null)
            {
                outputText.Text = _serialOutput.ToString();
            }

            var scrollViewer = this.FindControl<ScrollViewer>("SerialScrollViewer");
            scrollViewer?.ScrollToEnd();
        });
    }

    private void OnSerialConnectionChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var indicator = this.FindControl<Ellipse>("StatusIndicator");
            var statusText = this.FindControl<TextBlock>("StatusText");

            if (indicator != null)
            {
                indicator.Fill = connected
                    ? new SolidColorBrush(Color.Parse("#FFA6E3A1"))
                    : new SolidColorBrush(Color.Parse("#FFF38BA8"));
            }

            if (statusText != null)
            {
                statusText.Text = connected ? "Connected" : "Disconnected";
            }
        });
    }

    private string? FindBuiltPeFile()
    {
        if (string.IsNullOrEmpty(_projectPath)) return null;

        var projectDir = System.IO.Path.GetDirectoryName(_projectPath);
        if (projectDir == null) return null;

        var binDir = System.IO.Path.Combine(projectDir, "bin", "Debug");
        if (!System.IO.Directory.Exists(binDir))
        {
            binDir = System.IO.Path.Combine(projectDir, "bin", "Release");
        }

        if (!System.IO.Directory.Exists(binDir)) return null;

        var peFiles = System.IO.Directory.GetFiles(binDir, "*.pe", System.IO.SearchOption.AllDirectories);
        return peFiles.Length > 0 ? peFiles[0] : null;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var buttons = new[] { "FlashButton", "BuildButton", "DeployButton", "BuildDeployButton", "EraseButton" };
            foreach (var name in buttons)
            {
                var btn = this.FindControl<Button>(name);
                if (btn != null) btn.IsEnabled = enabled;
            }
        });
    }

    private void OnOutput(string message)
    {
        OutputReceived?.Invoke(this, message);
    }

    #endregion
}

