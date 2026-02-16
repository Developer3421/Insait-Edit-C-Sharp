using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Minimal Windows ConPTY (pseudo console) host for running interactive console apps.
/// Uses CreatePseudoConsole + CreateProcess with EXTENDED_STARTUPINFO_PRESENT.
/// </summary>
internal sealed class ConPtyHost : IDisposable
{
    private readonly IntPtr _hPc;
    private readonly SafeFileHandle _hInputWrite;
    private readonly SafeFileHandle _hOutputRead;

    private readonly Process _process;
    private volatile bool _exited;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readTask;

    public event EventHandler<string>? Output;

    public bool IsRunning => !_exited;

    public ConPtyHost(string fileName, string arguments, string workingDirectory, short cols = 120, short rows = 30)
    {
        // Create pipes for pseudo console
        CreatePipe(out var hInputReadRaw, out var hInputWriteRaw, IntPtr.Zero, 0);
        CreatePipe(out var hOutputReadRaw, out var hOutputWriteRaw, IntPtr.Zero, 0);

        _hInputWrite = new SafeFileHandle(hInputWriteRaw, ownsHandle: true);
        _hOutputRead = new SafeFileHandle(hOutputReadRaw, ownsHandle: true);

        var size = new COORD { X = cols, Y = rows };
        var hr = CreatePseudoConsole(size, hInputReadRaw, hOutputWriteRaw, 0, out _hPc);

        // Close ends not needed after pseudo console is created
        CloseHandle(hInputReadRaw);
        CloseHandle(hOutputWriteRaw);

        if (hr != 0)
            throw new Win32Exception(hr);

        // Prepare attribute list with the pseudo console
        IntPtr lpSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
        var attrList = Marshal.AllocHGlobal(lpSize);
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref lpSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // For PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, lpValue is the pseudo console handle itself
            if (!UpdateProcThreadAttribute(attrList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hPc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var siex = new STARTUPINFOEX();
            siex.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            siex.lpAttributeList = attrList;

            var cmdLine = BuildCommandLine(fileName, arguments);

            var pi = new PROCESS_INFORMATION();
            var flags = EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT;

            if (!CreateProcessW(
                    lpApplicationName: null,
                    lpCommandLine: cmdLine,
                    lpProcessAttributes: IntPtr.Zero,
                    lpThreadAttributes: IntPtr.Zero,
                    bInheritHandles: false,
                    dwCreationFlags: flags,
                    lpEnvironment: IntPtr.Zero,
                    lpCurrentDirectory: workingDirectory,
                    lpStartupInfo: ref siex,
                    lpProcessInformation: out pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            // Close raw handles returned by CreateProcess; Process.GetProcessById owns nothing here.
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);

            _process = Process.GetProcessById((int)pi.dwProcessId);

            // Track exit
            _ = Task.Run(async () =>
            {
                try { await _process.WaitForExitAsync().ConfigureAwait(false); }
                catch { /* ignore */ }
                _exited = true;
            });

            // Start reading output
            _readTask = Task.Run(() => PumpOutputAsync(_cts.Token));
        }
        finally
        {
            try { DeleteProcThreadAttributeList(attrList); } catch { /* ignore */ }
            Marshal.FreeHGlobal(attrList);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => _process.WaitForExitAsync(cancellationToken);

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        // ConPTY pipes don't support async operations, use synchronous FileStream
        using var fs = new FileStream(_hInputWrite, FileAccess.Write, 4096, isAsync: false);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush();
    }

    public void WriteLine(string text) => Write(text + "\r\n");

    public void Resize(short cols, short rows)
    {
        var size = new COORD { X = cols, Y = rows };
        ResizePseudoConsole(_hPc, size);
    }

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }

    private async Task PumpOutputAsync(CancellationToken ct)
    {
        try
        {
            // ConPTY pipes don't support async operations, use synchronous FileStream with Task.Run for reading
            using var fs = new FileStream(_hOutputRead, FileAccess.Read, 4096, isAsync: false);
            var buffer = new byte[8192];
            while (!ct.IsCancellationRequested)
            {
                int read;
                try
                {
                    // Run synchronous read on thread pool to avoid blocking
                    read = await Task.Run(() => fs.Read(buffer, 0, buffer.Length), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }

                if (read <= 0) break;

                var text = Encoding.UTF8.GetString(buffer, 0, read);
                Output?.Invoke(this, text);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string BuildCommandLine(string fileName, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return QuoteIfNeeded(fileName);
        return QuoteIfNeeded(fileName) + " " + arguments;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(' ') || value.Contains('\t') || value.Contains('"'))
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        return value;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _readTask.Wait(200); } catch { /* ignore */ }

        _cts.Dispose();

        try { _hInputWrite.Dispose(); } catch { /* ignore */ }
        try { _hOutputRead.Dispose(); } catch { /* ignore */ }

        try { ClosePseudoConsole(_hPc); } catch { /* ignore */ }

        try { _process.Dispose(); } catch { /* ignore */ }
    }

    // -----------------
    // Win32 interop
    // -----------------

    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern void ClosePseudoConsole(IntPtr hPc);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern int ResizePseudoConsole(IntPtr hPc, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);


    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
}
