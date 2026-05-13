using System.Runtime.InteropServices;

namespace SharpTerminal.Application.Parser;

/// <summary>
/// ANSI parser based on the Paul Williams state machine.
/// Processes a sequence of characters and dispatches actions to IParserHandler.
///
/// Flujo: bytes del PTY → UTF-8 decode → chars → AnsiParser → IParserHandler → Grid
///
/// The caller is responsible for UTF-8 decoding (usando System.Text.Decoder
/// to handle characters split across buffers).
/// </summary>
public sealed class AnsiParser
{
    private readonly IParserHandler _handler;
    private ParserState _state = ParserState.Ground;

    // Buffer de parámetros CSI/DCS (máx 16 parámetros es estándar)
    private readonly int[] _params = new int[16];
    private int _paramCount;
    private int _currentParam;
    private bool _hasDigits;

    // Buffer de intermediates (máx 4 es más que suficiente)
    private readonly byte[] _intermediates = new byte[4];
    private int _intermediateCount;

    // Buffer de datos OSC (dinámico para soportar imágenes inline)
    private readonly List<byte> _oscData = new(512);
    private const int OscMaxBytes = 10_000_000; // 10MB máx

    public ParserState CurrentState => _state;

    public AnsiParser(IParserHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Processes a block of characters (already decoded from UTF-8)).
    /// Can be called multiple times with partial data. The parser
    /// maintains state across calls.
    /// </summary>
    public void Process(ReadOnlySpan<char> input)
    {
        foreach (char c in input)
        {
            // UTF-8: chars ≥ 0x80 no pasan por la tabla de transiciones
            if (c >= 0x80)
            {
                if (_state == ParserState.Ground)
                    _handler.Print(c);
                // En otros estados (OSC, DCS, etc.), ignorar chars no-ASCII por ahora
                continue;
            }

            byte b = (byte)c;
            byte packed = TransitionTable.Get(_state, b);
            var action = (ParserAction)(packed >> 4);
            var nextState = (ParserState)(packed & 0x0F);

            // 1. Execute la acción del byte
            PerformAction(action, b);

            // 2. Manejar cambio de estado (con entry/exit actions)
            if (nextState != _state)
            {
                PerformStateExit(_state);
                PerformStateEntry(nextState);
                _state = nextState;
            }
        }
    }

    // ── Acciones de la tabla de transiciones ─────────────────────────

    private void PerformAction(ParserAction action, byte b)
    {
        switch (action)
        {
            case ParserAction.Print:
                _handler.Print((char)b);
                break;

            case ParserAction.Execute:
                _handler.Execute(b);
                break;

            case ParserAction.Collect:
                if (_intermediateCount < _intermediates.Length)
                    _intermediates[_intermediateCount++] = b;
                break;

            case ParserAction.Param:
                if (b >= (byte)'0' && b <= (byte)'9')
                {
                    _currentParam = _currentParam * 10 + (b - '0');
                    _hasDigits = true;
                }
                else if (b == (byte)';')
                {
                    PushParam();
                }
                break;

            case ParserAction.EscDispatch:
                _handler.EscDispatch(
                    _intermediates.AsSpan(0, _intermediateCount),
                    b);
                break;

            case ParserAction.CsiDispatch:
                FinalizeParams();
                _handler.CsiDispatch(
                    _params.AsSpan(0, _paramCount),
                    _intermediates.AsSpan(0, _intermediateCount),
                    b);
                break;

            case ParserAction.OscPut:
                if (_oscData.Count < OscMaxBytes)
                    _oscData.Add(b);
                break;

            case ParserAction.DcsPut:
                // No-op para MVP
                break;

            case ParserAction.None:
                break;
        }
    }

    // ── Acciones de entrada/salida de estado ─────────────────────────

    private void PerformStateEntry(ParserState state)
    {
        switch (state)
        {
            case ParserState.Escape:
            case ParserState.CsiEntry:
            case ParserState.DcsEntry:
                ClearBuffers();
                break;

            case ParserState.OscString:
                _oscData.Clear();
                break;
        }
    }

    private void PerformStateExit(ParserState state)
    {
        switch (state)
        {
            case ParserState.OscString:
                _handler.OscDispatch(CollectionsMarshal.AsSpan(_oscData));
                break;
        }
    }

    // ── Manejo de parámetros ─────────────────────────────────────────

    private void PushParam()
    {
        if (_paramCount < _params.Length)
            _params[_paramCount++] = _currentParam;
        _currentParam = 0;
        _hasDigits = false;
    }

    private void FinalizeParams()
    {
        // Push the last parameter if there is pending content.
        // ESC[m → no push (paramCount=0, hasDigits=false) → params=[]
        // ESC[0m → push 0 (hasDigits=true) → params=[0]
        // ESC[;m → push trailing 0 (paramCount>0) → params=[0, 0]
        if (_hasDigits || _paramCount > 0)
        {
            if (_paramCount < _params.Length)
                _params[_paramCount++] = _currentParam;
        }
    }

    private void ClearBuffers()
    {
        _paramCount = 0;
        _currentParam = 0;
        _hasDigits = false;
        _intermediateCount = 0;
    }
}
