namespace CliSharp.Application.Parser;

/// <summary>
/// Actions the parser executes when processing a byte, según la tabla de transiciones.
/// </summary>
internal enum ParserAction : byte
{
    None = 0,
    Print = 1,
    Execute = 2,
    Collect = 3,
    Param = 4,
    EscDispatch = 5,
    CsiDispatch = 6,
    OscPut = 7,
    DcsPut = 8,
}
