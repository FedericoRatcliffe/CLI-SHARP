namespace CliSharp.Application.Parser;

/// <summary>
/// Static transition table for the ANSI state machine.
/// Each entry encodes (acción, nuevo_estado) en un solo byte:
///   bits 7-4 = ParserAction, bits 3-0 = ParserState
/// Referencia: https://vt100.net/emu/dec_ansi_parser
/// </summary>
internal static class TransitionTable
{
    private const int StateCount = 14;
    private const int ByteCount = 128; // Solo ASCII (0x00-0x7F); UTF-8 se maneja aparte

    private static readonly byte[,] _table = new byte[StateCount, ByteCount];

    static TransitionTable()
    {
        BuildTable();
    }

    internal static byte Get(ParserState state, byte b)
    {
        return b < ByteCount
            ? _table[(int)state, b]
            : Pack(ParserAction.None, state);
    }

    private static byte Pack(ParserAction action, ParserState state)
        => (byte)(((byte)action << 4) | (byte)state);

    // ── Helpers de construcción ──────────────────────────────────────

    private static void Set(ParserState state, int b, ParserAction action, ParserState next)
        => _table[(int)state, b] = Pack(action, next);

    private static void SetRange(ParserState state, int start, int end, ParserAction action, ParserState next)
    {
        byte packed = Pack(action, next);
        for (int b = start; b <= end; b++)
            _table[(int)state, b] = packed;
    }

    // ── Construcción de la tabla ─────────────────────────────────────

    private static void BuildTable()
    {
        // Default: ignore and stay
        for (int s = 0; s < StateCount; s++)
        {
            byte stay = Pack(ParserAction.None, (ParserState)s);
            for (int b = 0; b < ByteCount; b++)
                _table[s, b] = stay;
        }

        // ── Anywhere (aplican a TODOS los estados) ──────────────────
        for (int s = 0; s < StateCount; s++)
        {
            var st = (ParserState)s;
            Set(st, 0x18, ParserAction.Execute, ParserState.Ground);  // CAN
            Set(st, 0x1A, ParserAction.Execute, ParserState.Ground);  // SUB
            Set(st, 0x1B, ParserAction.None, ParserState.Escape);     // ESC
        }

        BuildGround();
        BuildEscape();
        BuildEscapeIntermediate();
        BuildCsiEntry();
        BuildCsiParam();
        BuildCsiIntermediate();
        BuildCsiIgnore();
        BuildOscString();
        BuildDcsEntry();
        BuildDcsParam();
        BuildDcsIntermediate();
        BuildDcsPassthrough();
        BuildDcsIgnore();
        // SosPmApcString: solo las "anywhere" — todo lo demás se ignora
    }

    // ── Ground ──────────────────────────────────────────────────────

    private static void BuildGround()
    {
        const ParserState S = ParserState.Ground;
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);
        SetRange(S, 0x20, 0x7E, ParserAction.Print, S);
        // 0x7F (DEL) = ignore → ya está por default
    }

    // ── Escape ──────────────────────────────────────────────────────

    private static void BuildEscape()
    {
        const ParserState S = ParserState.Escape;

        // C0 controls executed inline
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);

        // Intermediates → EscapeIntermediate
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, ParserState.EscapeIntermediate);

        // Final bytes → EscDispatch
        SetRange(S, 0x30, 0x4F, ParserAction.EscDispatch, ParserState.Ground);
        SetRange(S, 0x51, 0x57, ParserAction.EscDispatch, ParserState.Ground);
        Set(S, 0x59, ParserAction.EscDispatch, ParserState.Ground);
        Set(S, 0x5A, ParserAction.EscDispatch, ParserState.Ground);
        Set(S, 0x5C, ParserAction.EscDispatch, ParserState.Ground); // ST
        SetRange(S, 0x60, 0x7E, ParserAction.EscDispatch, ParserState.Ground);

        // Sequences that open new states
        Set(S, 0x50, ParserAction.None, ParserState.DcsEntry);         // 'P' → DCS
        Set(S, 0x58, ParserAction.None, ParserState.SosPmApcString);   // 'X' → SOS
        Set(S, 0x5B, ParserAction.None, ParserState.CsiEntry);         // '[' → CSI
        Set(S, 0x5D, ParserAction.None, ParserState.OscString);        // ']' → OSC
        Set(S, 0x5E, ParserAction.None, ParserState.SosPmApcString);   // '^' → PM
        Set(S, 0x5F, ParserAction.None, ParserState.SosPmApcString);   // '_' → APC
    }

    // ── EscapeIntermediate ──────────────────────────────────────────

    private static void BuildEscapeIntermediate()
    {
        const ParserState S = ParserState.EscapeIntermediate;
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, S);
        SetRange(S, 0x30, 0x7E, ParserAction.EscDispatch, ParserState.Ground);
    }

    // ── CsiEntry ────────────────────────────────────────────────────

    private static void BuildCsiEntry()
    {
        const ParserState S = ParserState.CsiEntry;
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, ParserState.CsiIntermediate);

        // Digits and semicolons: parameters
        SetRange(S, 0x30, 0x39, ParserAction.Param, ParserState.CsiParam);
        Set(S, 0x3B, ParserAction.Param, ParserState.CsiParam);

        // ':' → sub-parámetros (no soportado) → CsiIgnore
        Set(S, 0x3A, ParserAction.None, ParserState.CsiIgnore);

        // Private markers (?, >, <, =) → Collect como intermediate
        SetRange(S, 0x3C, 0x3F, ParserAction.Collect, ParserState.CsiParam);

        // Final byte → dispatch inmediato (sin parámetros)
        SetRange(S, 0x40, 0x7E, ParserAction.CsiDispatch, ParserState.Ground);
    }

    // ── CsiParam ────────────────────────────────────────────────────

    private static void BuildCsiParam()
    {
        const ParserState S = ParserState.CsiParam;
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, ParserState.CsiIntermediate);

        // Digits and semicolons: parameters
        SetRange(S, 0x30, 0x39, ParserAction.Param, S);
        Set(S, 0x3B, ParserAction.Param, S);
        Set(S, 0x3A, ParserAction.None, ParserState.CsiIgnore);

        // Private marker after params = invalid, ignore rest
        SetRange(S, 0x3C, 0x3F, ParserAction.None, ParserState.CsiIgnore);

        // Final byte → dispatch
        SetRange(S, 0x40, 0x7E, ParserAction.CsiDispatch, ParserState.Ground);
    }

    // ── CsiIntermediate ─────────────────────────────────────────────

    private static void BuildCsiIntermediate()
    {
        const ParserState S = ParserState.CsiIntermediate;
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, S);
        SetRange(S, 0x30, 0x3F, ParserAction.None, ParserState.CsiIgnore);
        SetRange(S, 0x40, 0x7E, ParserAction.CsiDispatch, ParserState.Ground);
    }

    // ── CsiIgnore ───────────────────────────────────────────────────

    private static void BuildCsiIgnore()
    {
        const ParserState S = ParserState.CsiIgnore;
        SetRange(S, 0x00, 0x17, ParserAction.Execute, S);
        Set(S, 0x19, ParserAction.Execute, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.Execute, S);
        SetRange(S, 0x20, 0x3F, ParserAction.None, S);
        SetRange(S, 0x40, 0x7E, ParserAction.None, ParserState.Ground);
    }

    // ── OscString ───────────────────────────────────────────────────

    private static void BuildOscString()
    {
        const ParserState S = ParserState.OscString;
        // BEL terminates OSC (xterm extension, universalmente soportada)
        Set(S, 0x07, ParserAction.None, ParserState.Ground);
        // Printable characters are collected
        SetRange(S, 0x20, 0x7E, ParserAction.OscPut, S);
        // C0 controls (excepto BEL, CAN, SUB, ESC) se ignoran → default
    }

    // ── DCS states (implementación mínima para no crashear) ─────────

    private static void BuildDcsEntry()
    {
        const ParserState S = ParserState.DcsEntry;
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, ParserState.DcsIntermediate);
        SetRange(S, 0x30, 0x39, ParserAction.Param, ParserState.DcsParam);
        Set(S, 0x3A, ParserAction.None, ParserState.DcsIgnore);
        Set(S, 0x3B, ParserAction.Param, ParserState.DcsParam);
        SetRange(S, 0x3C, 0x3F, ParserAction.Collect, ParserState.DcsParam);
        SetRange(S, 0x40, 0x7E, ParserAction.None, ParserState.DcsPassthrough);
    }

    private static void BuildDcsParam()
    {
        const ParserState S = ParserState.DcsParam;
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, ParserState.DcsIntermediate);
        SetRange(S, 0x30, 0x39, ParserAction.Param, S);
        Set(S, 0x3A, ParserAction.None, ParserState.DcsIgnore);
        Set(S, 0x3B, ParserAction.Param, S);
        SetRange(S, 0x3C, 0x3F, ParserAction.None, ParserState.DcsIgnore);
        SetRange(S, 0x40, 0x7E, ParserAction.None, ParserState.DcsPassthrough);
    }

    private static void BuildDcsIntermediate()
    {
        const ParserState S = ParserState.DcsIntermediate;
        SetRange(S, 0x20, 0x2F, ParserAction.Collect, S);
        SetRange(S, 0x30, 0x3F, ParserAction.None, ParserState.DcsIgnore);
        SetRange(S, 0x40, 0x7E, ParserAction.None, ParserState.DcsPassthrough);
    }

    private static void BuildDcsPassthrough()
    {
        const ParserState S = ParserState.DcsPassthrough;
        SetRange(S, 0x00, 0x17, ParserAction.DcsPut, S);
        Set(S, 0x19, ParserAction.DcsPut, S);
        SetRange(S, 0x1C, 0x1F, ParserAction.DcsPut, S);
        SetRange(S, 0x20, 0x7E, ParserAction.DcsPut, S);
    }

    private static void BuildDcsIgnore()
    {
        // Everything ignored until "anywhere"; default covers this
    }
}
