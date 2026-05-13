using Microsoft.Win32.SafeHandles;
using static SharpTerminal.Infrastructure.ConPty.NativeMethods;

namespace SharpTerminal.Infrastructure.ConPty;

/// <summary>
/// Managed wrapper over the ConPTY HPCON handle.
/// Manages creation, resize, and release of the pseudo-terminal.
/// </summary>
internal sealed class PseudoConsole : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    internal IntPtr Handle => _handle;

    private PseudoConsole(IntPtr handle)
    {
        _handle = handle;
    }

    internal static PseudoConsole Create(
        SafeFileHandle inputReadSide,
        SafeFileHandle outputWriteSide,
        int columns,
        int rows)
    {
        var size = new COORD { X = (short)columns, Y = (short)rows };
        int hr = CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out var handle);

        if (hr != S_OK)
            throw new InvalidOperationException(
                $"CreatePseudoConsole failed with HRESULT 0x{hr:X8}");

        return new PseudoConsole(handle);
    }

    internal void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var size = new COORD { X = (short)columns, Y = (short)rows };
        ResizePseudoConsole(_handle, size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClosePseudoConsole(_handle);
        _handle = IntPtr.Zero;
    }
}
