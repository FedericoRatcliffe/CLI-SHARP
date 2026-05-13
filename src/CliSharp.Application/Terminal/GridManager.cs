using System.Text;
using CliSharp.Application.Parser;
using CliSharp.Domain.ValueObjects;

namespace CliSharp.Application.Terminal;

/// <summary>
/// Implements IParserHandler: translates ANSI parser actions
/// into Grid operations. Supports 16/256/truecolor, DECSET
/// completo, OSC 0/2/8/52, mouse reporting modes.
/// </summary>
public sealed class GridManager : IParserHandler
{
    private readonly Grid _grid;

    public GridManager(Grid grid) => _grid = grid;

    public void Print(char c) => _grid.PrintChar(c);

    public void Execute(byte controlCode)
    {
        switch (controlCode)
        {
            case 0x08: _grid.Backspace(); break;
            case 0x09: _grid.Tab(); break;
            case 0x0A: case 0x0B: case 0x0C: _grid.LineFeed(); break;
            case 0x0D: _grid.CarriageReturn(); break;
        }
    }

    public void CsiDispatch(ReadOnlySpan<int> parameters, ReadOnlySpan<byte> intermediates, byte finalByte)
    {
        if (intermediates.Length > 0 && intermediates[0] == (byte)'?')
        {
            HandlePrivateMode(parameters, finalByte);
            return;
        }

        switch ((char)finalByte)
        {
            case 'A': _grid.CursorUp(AtLeast1(Param(parameters, 0))); break;
            case 'B': _grid.CursorDown(AtLeast1(Param(parameters, 0))); break;
            case 'C': _grid.CursorForward(AtLeast1(Param(parameters, 0))); break;
            case 'D': _grid.CursorBackward(AtLeast1(Param(parameters, 0))); break;
            case 'E':
                _grid.CursorDown(AtLeast1(Param(parameters, 0)));
                _grid.CarriageReturn(); break;
            case 'F':
                _grid.CursorUp(AtLeast1(Param(parameters, 0)));
                _grid.CarriageReturn(); break;
            case 'G': _grid.SetCursor(_grid.CursorRow, Param(parameters, 0, 1) - 1); break;
            case 'H': case 'f':
                _grid.SetCursor(Param(parameters, 0, 1) - 1, Param(parameters, 1, 1) - 1); break;
            case 'J': _grid.EraseDisplay(Param(parameters, 0)); break;
            case 'K': _grid.EraseLine(Param(parameters, 0)); break;
            case 'd': _grid.SetCursor(Param(parameters, 0, 1) - 1, _grid.CursorColumn); break;
            case 'm': HandleSgr(parameters); break;
        }
    }

    public void EscDispatch(ReadOnlySpan<byte> intermediates, byte finalByte)
    {
        switch ((char)finalByte) { case 'M': _grid.ReverseIndex(); break; }
    }

    public void OscDispatch(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return;
        int sep = data.IndexOf((byte)';');
        if (sep < 1) return;

        int cmd = 0;
        for (int i = 0; i < sep; i++)
        {
            if (data[i] < '0' || data[i] > '9') return;
            cmd = cmd * 10 + (data[i] - '0');
        }

        var payload = data[(sep + 1)..];
        switch (cmd)
        {
            case 0: case 2:
                _grid.SetTitle(Encoding.UTF8.GetString(payload));
                break;
            case 7: HandleOsc7(payload); break;
            case 8: HandleOsc8(payload); break;
            case 52: HandleOsc52(payload); break;
            case 133: HandleOsc133(payload); break;
            case 1337: HandleOsc1337(payload); break;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static int Param(ReadOnlySpan<int> p, int index, int defaultValue = 0)
        => index < p.Length ? p[index] : defaultValue;
    private static int AtLeast1(int v) => v < 1 ? 1 : v;

    // ── SGR: 16 + 256 + Truecolor ──────────────────────────────────

    private void HandleSgr(ReadOnlySpan<int> parameters)
    {
        if (parameters.IsEmpty) { _grid.CurrentAttributes = CellAttributes.Default; return; }
        var attrs = _grid.CurrentAttributes;

        for (int i = 0; i < parameters.Length; i++)
        {
            int p = parameters[i];
            switch (p)
            {
                case 0: attrs = CellAttributes.Default; break;
                case 1: attrs.Flags |= CellFlags.Bold; break;
                case 3: attrs.Flags |= CellFlags.Italic; break;
                case 4: attrs.Flags |= CellFlags.Underline; break;
                case 7: attrs.Flags |= CellFlags.Inverse; break;
                case 8: attrs.Flags |= CellFlags.Hidden; break;
                case 9: attrs.Flags |= CellFlags.Strikethrough; break;
                case 22: attrs.Flags &= ~CellFlags.Bold; break;
                case 23: attrs.Flags &= ~CellFlags.Italic; break;
                case 24: attrs.Flags &= ~CellFlags.Underline; break;
                case 27: attrs.Flags &= ~CellFlags.Inverse; break;
                case 28: attrs.Flags &= ~CellFlags.Hidden; break;
                case 29: attrs.Flags &= ~CellFlags.Strikethrough; break;
                case >= 30 and <= 37: attrs.Foreground = TerminalColor.Indexed((byte)(p - 30)); break;
                case 39: attrs.Foreground = TerminalColor.Default; break;
                case >= 90 and <= 97: attrs.Foreground = TerminalColor.Indexed((byte)(p - 90 + 8)); break;
                case >= 40 and <= 47: attrs.Background = TerminalColor.Indexed((byte)(p - 40)); break;
                case 49: attrs.Background = TerminalColor.Default; break;
                case >= 100 and <= 107: attrs.Background = TerminalColor.Indexed((byte)(p - 100 + 8)); break;
                case 38: attrs.Foreground = ParseExtendedColor(parameters, ref i); break;
                case 48: attrs.Background = ParseExtendedColor(parameters, ref i); break;
            }
        }
        _grid.CurrentAttributes = attrs;
    }

    private static TerminalColor ParseExtendedColor(ReadOnlySpan<int> p, ref int i)
    {
        if (i + 1 >= p.Length) return TerminalColor.Default;
        if (p[i + 1] == 5 && i + 2 < p.Length)
            { i += 2; return TerminalColor.Indexed((byte)Math.Clamp(p[i], 0, 255)); }
        if (p[i + 1] == 2 && i + 4 < p.Length)
            { i += 4; return TerminalColor.Rgb((byte)Math.Clamp(p[i - 2], 0, 255), (byte)Math.Clamp(p[i - 1], 0, 255), (byte)Math.Clamp(p[i], 0, 255)); }
        return TerminalColor.Default;
    }

    // ── DECSET / DECRST ─────────────────────────────────────────────

    private void HandlePrivateMode(ReadOnlySpan<int> parameters, byte finalByte)
    {
        bool set = (char)finalByte == 'h';
        for (int i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i])
            {
                case 1: _grid.ApplicationCursorKeys = set; break;
                case 25: _grid.CursorVisible = set; break;
                case 47: case 1047: case 1049:
                    if (set) _grid.EnterAltScreen(); else _grid.ExitAltScreen(); break;
                case 1000: case 1002: case 1003:
                    _grid.MouseTrackingMode = set ? parameters[i] : 0; break;
                case 1006: _grid.SgrMouseMode = set; break;
                case 2004: _grid.BracketedPasteMode = set; break;
            }
        }
    }

    // ── OSC 7: Current working directory ───────────────────────────

    private void HandleOsc7(ReadOnlySpan<byte> payload)
    {
        try
        {
            var uriStr = Encoding.UTF8.GetString(payload);
            if (uriStr.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(uriStr);
                _grid.SetCurrentDirectory(uri.LocalPath);
            }
            else if (Directory.Exists(uriStr))
            {
                _grid.SetCurrentDirectory(uriStr);
            }
        }
        catch { }
    }

    // ── OSC 133: Command blocks (FinalTerm shell integration) ──────

    private void HandleOsc133(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return;
        switch ((char)payload[0])
        {
            case 'A': _grid.MarkPromptStart(); break;  // Prompt start
            case 'B': break;                            // End of prompt / inicio de input
            case 'C': break;                            // End of input / inicio de output
            case 'D':                                    // End of output + exit code
                int code = 0;
                if (payload.Length > 2 && payload[1] == ';')
                    for (int i = 2; i < payload.Length; i++)
                        if (payload[i] >= '0' && payload[i] <= '9')
                            code = code * 10 + (payload[i] - '0');
                _grid.MarkCommandEnd(code);
                break;
        }
    }

    // ── OSC 8: Hyperlinks ───────────────────────────────────────────

    private void HandleOsc8(ReadOnlySpan<byte> payload)
    {
        int sep = payload.IndexOf((byte)';');
        if (sep < 0) return;
        var uri = payload[(sep + 1)..];
        _grid.SetHyperlink(uri.IsEmpty ? null : Encoding.UTF8.GetString(uri));
    }

    // ── OSC 52: Clipboard ───────────────────────────────────────────

    private void HandleOsc52(ReadOnlySpan<byte> payload)
    {
        int sep = payload.IndexOf((byte)';');
        if (sep < 0 || payload.Length > 100_000) return;

        var base64 = payload[(sep + 1)..];
        if (base64.Length == 1 && base64[0] == '?') return; // Query disabled (security)

        try
        {
            var decoded = Convert.FromBase64String(Encoding.ASCII.GetString(base64));
            _grid.RequestClipboardSet(Encoding.UTF8.GetString(decoded));
        }
        catch (FormatException) { }
    }

    // ── OSC 1337: iTerm2 inline images ──────────────────────────────

    private void HandleOsc1337(ReadOnlySpan<byte> payload)
    {
        // Format: File=[params]:<base64data>
        // Convert to string for simple parsing (params are short ASCII))
        var str = Encoding.ASCII.GetString(payload);
        if (!str.StartsWith("File=")) return;

        int colon = str.IndexOf(':');
        if (colon < 0 || colon >= str.Length - 1) return;

        var paramStr = str[5..colon];
        var base64Str = str[(colon + 1)..];

        bool inline = false;
        int wCells = 20, hCells = 10;

        foreach (var param in paramStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (param == "inline=1") inline = true;
            else if (param.StartsWith("width=") && int.TryParse(param[6..], out var w) && w > 0) wCells = w;
            else if (param.StartsWith("height=") && int.TryParse(param[7..], out var h) && h > 0) hCells = h;
        }

        if (!inline) return;

        try
        {
            var data = Convert.FromBase64String(base64Str);
            _grid.AddInlineImage(data, wCells, hCells);
        }
        catch { }
    }
}
