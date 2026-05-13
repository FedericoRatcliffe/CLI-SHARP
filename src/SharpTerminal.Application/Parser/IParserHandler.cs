namespace SharpTerminal.Application.Parser;

/// <summary>
/// Receives actions dispatched by the ANSI parser.
/// GridManager implements this interface to apply sequences to the grid.
/// </summary>
public interface IParserHandler
{
    /// <summary>
    /// Carácter imprimible (ASCII o Unicode).
    /// </summary>
    void Print(char c);

    /// <summary>
    /// Código de control C0: BEL(0x07), BS(0x08), HT(0x09), LF(0x0A), CR(0x0D), etc.
    /// </summary>
    void Execute(byte controlCode);

    /// <summary>
    /// Complete CSI sequence.
    /// Ejemplo: ESC[1;31m → params=[1,31], intermediates=[], finalByte='m'
    /// Para private modes: ESC[?25h → params=[25], intermediates=['?'], finalByte='h'
    /// </summary>
    void CsiDispatch(ReadOnlySpan<int> parameters, ReadOnlySpan<byte> intermediates, byte finalByte);

    /// <summary>
    /// Complete ESC sequence.
    /// Ejemplo: ESC M → intermediates=[], finalByte='M' (reverse index)
    /// Ejemplo: ESC ( B → intermediates=['('], finalByte='B' (charset)
    /// </summary>
    void EscDispatch(ReadOnlySpan<byte> intermediates, byte finalByte);

    /// <summary>
    /// Complete OSC sequence (terminada por BEL o ST).
    /// Ejemplo: ESC]0;title BEL → data = bytes de "0;title"
    /// </summary>
    void OscDispatch(ReadOnlySpan<byte> data);
}
