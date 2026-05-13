using CliSharp.Application.Parser;

namespace CliSharp.Tests.Unit.Parser;

/// <summary>
/// Test handler that records all events dispatched by the parser.
/// </summary>
internal sealed class RecordingHandler : IParserHandler
{
    public record PrintEvent(char Character);
    public record ExecuteEvent(byte Code);
    public record CsiEvent(int[] Params, byte[] Intermediates, byte FinalByte);
    public record EscEvent(byte[] Intermediates, byte FinalByte);
    public record OscEvent(byte[] Data);

    public List<object> Events { get; } = [];

    public void Print(char c) => Events.Add(new PrintEvent(c));

    public void Execute(byte controlCode) => Events.Add(new ExecuteEvent(controlCode));

    public void CsiDispatch(ReadOnlySpan<int> parameters, ReadOnlySpan<byte> intermediates, byte finalByte)
        => Events.Add(new CsiEvent(parameters.ToArray(), intermediates.ToArray(), finalByte));

    public void EscDispatch(ReadOnlySpan<byte> intermediates, byte finalByte)
        => Events.Add(new EscEvent(intermediates.ToArray(), finalByte));

    public void OscDispatch(ReadOnlySpan<byte> data)
        => Events.Add(new OscEvent(data.ToArray()));

    // ── Assertion helpers ──────────────────────────────────────

    public IEnumerable<PrintEvent> Prints => Events.OfType<PrintEvent>();
    public IEnumerable<ExecuteEvent> Executes => Events.OfType<ExecuteEvent>();
    public IEnumerable<CsiEvent> CsiEvents => Events.OfType<CsiEvent>();
    public IEnumerable<EscEvent> EscEvents => Events.OfType<EscEvent>();
    public IEnumerable<OscEvent> OscEvents => Events.OfType<OscEvent>();

    public string PrintedText => new(Prints.Select(p => p.Character).ToArray());
}
