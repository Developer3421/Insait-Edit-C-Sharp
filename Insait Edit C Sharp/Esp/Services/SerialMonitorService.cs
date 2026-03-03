using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Esp.Services;

/// <summary>
/// Serial monitor service for communicating with ESP devices via System.IO.Ports
/// </summary>
public class SerialMonitorService : IDisposable
{
    public event EventHandler<string>? DataReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler<bool>? ConnectionChanged;

    private SerialPort? _serialPort;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isConnected;
    private string? _currentPort;
    private int _baudRate;

    public bool IsConnected => _isConnected;
    public string? CurrentPort => _currentPort;
    public int BaudRate => _baudRate;

    /// <summary>
    /// Common baud rates for ESP devices
    /// </summary>
    public static readonly int[] CommonBaudRates = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

    /// <summary>
    /// Default baud rate for nanoFramework debug output
    /// </summary>
    public const int DefaultBaudRate = 115200;

    /// <summary>
    /// Get available COM port names on the system
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    /// <summary>
    /// Open serial monitor connection
    /// </summary>
    public Task<bool> ConnectAsync(string comPort, int baudRate = DefaultBaudRate)
    {
        if (_isConnected)
        {
            DisconnectAsync().Wait();
        }

        _currentPort = comPort;
        _baudRate = baudRate;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            OnDataReceived($"Connecting to {comPort} at {baudRate} baud...\n");

            _serialPort = new SerialPort(comPort, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                Encoding = System.Text.Encoding.UTF8,
                DtrEnable = true,
                RtsEnable = true
            };

            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.ErrorReceived += SerialPort_ErrorReceived;

            _serialPort.Open();

            _isConnected = true;
            ConnectionChanged?.Invoke(this, true);
            OnDataReceived($"Connected to {comPort} at {baudRate} baud\n");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            OnErrorReceived($"Connection failed: {ex.Message}\n");
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Send data to the serial port
    /// </summary>
    public Task SendAsync(string data)
    {
        if (!_isConnected || _serialPort == null || !_serialPort.IsOpen) return Task.CompletedTask;

        try
        {
            _serialPort.Write(data);
            OnDataReceived($"[TX] {data}\n");
        }
        catch (Exception ex)
        {
            OnErrorReceived($"Send failed: {ex.Message}\n");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Send data with newline
    /// </summary>
    public Task SendLineAsync(string data)
    {
        if (!_isConnected || _serialPort == null || !_serialPort.IsOpen) return Task.CompletedTask;

        try
        {
            _serialPort.WriteLine(data);
            OnDataReceived($"[TX] {data}\n");
        }
        catch (Exception ex)
        {
            OnErrorReceived($"Send failed: {ex.Message}\n");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disconnect from serial port
    /// </summary>
    public Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.ErrorReceived -= SerialPort_ErrorReceived;

                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                _serialPort.Dispose();
                _serialPort = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error disconnecting: {ex.Message}");
        }
        finally
        {
            _isConnected = false;
            _currentPort = null;
            ConnectionChanged?.Invoke(this, false);
            OnDataReceived("Disconnected\n");
        }

        return Task.CompletedTask;
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort == null || !_serialPort.IsOpen) return;

        try
        {
            var data = _serialPort.ReadExisting();
            if (!string.IsNullOrEmpty(data))
            {
                OnDataReceived(data);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Serial read error: {ex.Message}");
        }
    }

    private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        OnErrorReceived($"Serial error: {e.EventType}\n");
    }

    private void OnDataReceived(string data)
    {
        DataReceived?.Invoke(this, data);
    }

    private void OnErrorReceived(string error)
    {
        ErrorReceived?.Invoke(this, error);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        if (_serialPort != null)
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
            if (_serialPort.IsOpen)
            {
                try { _serialPort.Close(); } catch { }
            }
            _serialPort.Dispose();
            _serialPort = null;
        }
    }
}

