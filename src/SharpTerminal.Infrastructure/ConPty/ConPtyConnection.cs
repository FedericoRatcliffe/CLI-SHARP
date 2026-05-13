using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SharpTerminal.Application.Abstractions;
using static SharpTerminal.Infrastructure.ConPty.NativeMethods;

namespace SharpTerminal.Infrastructure.ConPty;

/// <summary>
/// IPtyConnection implementation using ConPTY (Windows 10 1809+)+).
/// Creates a pseudo-terminal, launches a shell process, and exposes I/O streams.
/// </summary>
public sealed class ConPtyConnection : IPtyConnection
{
    private PseudoConsole? _pseudoConsole;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private IntPtr _attributeList;
    private FileStream? _inputStream;
    private FileStream? _outputStream;
    private bool _disposed;

    public Stream InputStream => _inputStream ?? throw new ObjectDisposedException(nameof(ConPtyConnection));
    public Stream OutputStream => _outputStream ?? throw new ObjectDisposedException(nameof(ConPtyConnection));
    public event Action? ProcessExited;

    private ConPtyConnection() { }

    /// <summary>
    /// Creates a ConPTY connection and launches the given process.
    /// </summary>
    public static ConPtyConnection Start(string command, int columns = 120, int rows = 30)
    {
        var connection = new ConPtyConnection();
        connection.Initialize(command, columns, rows);
        return connection;
    }

    private void Initialize(string command, int columns, int rows)
    {
        // ── 1. Crear pipes ──────────────────────────────────────────
        // inputPipe:  we write to writeSide, ConPTY reads from readSide
        // outputPipe: ConPTY writes to writeSide, we read from readSide
        var pipeSec = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>()
        };

        if (!CreatePipe(out var inputReadSide, out var inputWriteSide, ref pipeSec, 0))
            throw new InvalidOperationException(
                $"CreatePipe (input) failed: error {Marshal.GetLastWin32Error()}");

        if (!CreatePipe(out var outputReadSide, out var outputWriteSide, ref pipeSec, 0))
        {
            inputReadSide.Dispose();
            inputWriteSide.Dispose();
            throw new InvalidOperationException(
                $"CreatePipe (output) failed: error {Marshal.GetLastWin32Error()}");
        }

        // ── 2. Crear pseudo-console ─────────────────────────────────
        _pseudoConsole = PseudoConsole.Create(inputReadSide, outputWriteSide, columns, rows);

        // ConPTY duplica estos handles internamente, podemos Close nuestras copias
        inputReadSide.Dispose();
        outputWriteSide.Dispose();

        // ── 3. Create child process ───────────────────────────────────
        var startupInfo = CreateStartupInfo();

        if (!CreateProcessW(
            null,
            command,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            EXTENDED_STARTUPINFO_PRESENT,
            IntPtr.Zero,
            null,
            ref startupInfo,
            out var processInfo))
        {
            throw new InvalidOperationException(
                $"CreateProcess failed: error {Marshal.GetLastWin32Error()}");
        }

        _processHandle = processInfo.hProcess;
        _threadHandle = processInfo.hThread;

        // ── 4. Expose I/O streams ───────────────────────────────
        _inputStream = new FileStream(inputWriteSide, FileAccess.Write);
        _outputStream = new FileStream(outputReadSide, FileAccess.Read);

        // ── 5. Monitor process exit ───────────────────────────
        _ = Task.Run(() =>
        {
            WaitForSingleObject(_processHandle, INFINITE);
            ProcessExited?.Invoke();
        });
    }

    private STARTUPINFOEX CreateStartupInfo()
    {
        // Calculate required size for attribute list (1 atributo)
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);

        _attributeList = Marshal.AllocHGlobal(size.ToInt32());

        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref size))
            throw new InvalidOperationException(
                $"InitializeProcThreadAttributeList failed: error {Marshal.GetLastWin32Error()}");

        // Associate HPCON with attribute list
        if (!UpdateProcThreadAttribute(
            _attributeList,
            0,
            (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _pseudoConsole!.Handle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"UpdateProcThreadAttribute failed: error {Marshal.GetLastWin32Error()}");
        }

        var startupInfo = new STARTUPINFOEX { lpAttributeList = _attributeList };
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        return startupInfo;
    }

    public void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pseudoConsole?.Resize(columns, rows);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 1. Close pseudo-console (termina el proceso hijo y rompe los pipes)
        _pseudoConsole?.Dispose();

        // 2. Close streams
        if (_inputStream is not null) await _inputStream.DisposeAsync();
        if (_outputStream is not null) await _outputStream.DisposeAsync();

        // 3. Release process handles
        if (_processHandle != IntPtr.Zero) { CloseHandle(_processHandle); _processHandle = IntPtr.Zero; }
        if (_threadHandle != IntPtr.Zero) { CloseHandle(_threadHandle); _threadHandle = IntPtr.Zero; }

        // 4. Release attribute list
        if (_attributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }
    }
}
