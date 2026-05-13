namespace SharpTerminal.Application.Parser;

/// <summary>
/// ANSI state machine states (Paul Williams).
/// Each state determines how incoming bytes are interpreted.
/// </summary>
public enum ParserState : byte
{
    Ground = 0,
    Escape = 1,
    EscapeIntermediate = 2,
    CsiEntry = 3,
    CsiParam = 4,
    CsiIntermediate = 5,
    CsiIgnore = 6,
    DcsEntry = 7,
    DcsParam = 8,
    DcsIntermediate = 9,
    DcsPassthrough = 10,
    DcsIgnore = 11,
    OscString = 12,
    SosPmApcString = 13,
}
