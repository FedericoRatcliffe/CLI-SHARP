using FluentAssertions;
using SharpTerminal.Application.Parser;
using Xunit;

namespace SharpTerminal.Tests.Unit.Parser;

public class AnsiParserTests
{
    private readonly RecordingHandler _handler = new();
    private readonly AnsiParser _parser;

    public AnsiParserTests()
    {
        _parser = new AnsiParser(_handler);
    }

    // ── Texto plano ─────────────────────────────────────────────────

    [Fact]
    public void PlainText_Ascii_AllPrinted()
    {
        _parser.Process("Hello World!");
        _handler.PrintedText.Should().Be("Hello World!");
    }

    [Fact]
    public void PlainText_Unicode_PrintedDirectly()
    {
        _parser.Process("café ñ 日本語");
        _handler.PrintedText.Should().Be("café ñ 日本語");
    }

    [Fact]
    public void PlainText_Empty_NoEvents()
    {
        _parser.Process("");
        _handler.Events.Should().BeEmpty();
    }

    [Fact]
    public void PlainText_AllPrintableAscii_Printed()
    {
        // 0x20 (space) hasta 0x7E (~)
        var printable = new string(Enumerable.Range(0x20, 0x7E - 0x20 + 1).Select(b => (char)b).ToArray());
        _parser.Process(printable);
        _handler.PrintedText.Should().Be(printable);
    }

    // ── Códigos de control ──────────────────────────────────────────

    [Fact]
    public void Control_LineFeed_Execute()
    {
        _parser.Process("\n");
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x0A);
    }

    [Fact]
    public void Control_CarriageReturn_Execute()
    {
        _parser.Process("\r");
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x0D);
    }

    [Fact]
    public void Control_Backspace_Execute()
    {
        _parser.Process("\b");
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x08);
    }

    [Fact]
    public void Control_Tab_Execute()
    {
        _parser.Process("\t");
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x09);
    }

    [Fact]
    public void Control_Bell_Execute()
    {
        _parser.Process("\u0007");
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x07);
    }

    [Fact]
    public void Control_Del_Ignored()
    {
        _parser.Process("\u007f");
        _handler.Events.Should().BeEmpty();
    }

    [Fact]
    public void Control_MixedWithText_CorrectOrder()
    {
        _parser.Process("A\nB\rC");
        _handler.Events.Should().HaveCount(5);
        _handler.Events[0].Should().BeOfType<RecordingHandler.PrintEvent>().Which.Character.Should().Be('A');
        _handler.Events[1].Should().BeOfType<RecordingHandler.ExecuteEvent>().Which.Code.Should().Be(0x0A);
        _handler.Events[2].Should().BeOfType<RecordingHandler.PrintEvent>().Which.Character.Should().Be('B');
        _handler.Events[3].Should().BeOfType<RecordingHandler.ExecuteEvent>().Which.Code.Should().Be(0x0D);
        _handler.Events[4].Should().BeOfType<RecordingHandler.PrintEvent>().Which.Character.Should().Be('C');
    }

    // ── CSI: SGR (Select Graphic Rendition) ─────────────────────────

    [Fact]
    public void Csi_Reset_NoParams()
    {
        _parser.Process("\u001b[m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'m');
        csi.Params.Should().BeEmpty();
        csi.Intermediates.Should().BeEmpty();
    }

    [Fact]
    public void Csi_Reset_ExplicitZero()
    {
        _parser.Process("\u001b[0m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.Params.Should().Equal(0);
    }

    [Fact]
    public void Csi_ForegroundColor_Red()
    {
        _parser.Process("\u001b[31m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'m');
        csi.Params.Should().Equal(31);
    }

    [Fact]
    public void Csi_BoldAndColor_MultipleParams()
    {
        _parser.Process("\u001b[1;31m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.Params.Should().Equal(1, 31);
    }

    [Fact]
    public void Csi_ManyParams_AllCollected()
    {
        _parser.Process("\u001b[38;2;255;128;0m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.Params.Should().Equal(38, 2, 255, 128, 0);
    }

    [Fact]
    public void Csi_EmptyParamsWithSemicolon()
    {
        // ESC[;m → params=[0, 0]
        _parser.Process("\u001b[;m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.Params.Should().Equal(0, 0);
    }

    [Fact]
    public void Csi_TrailingSemicolon()
    {
        // ESC[1;m → params=[1, 0]
        _parser.Process("\u001b[1;m");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.Params.Should().Equal(1, 0);
    }

    // ── CSI: Cursor Movement ────────────────────────────────────────

    [Fact]
    public void Csi_CursorUp()
    {
        _parser.Process("\u001b[5A");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'A');
        csi.Params.Should().Equal(5);
    }

    [Fact]
    public void Csi_CursorPosition_TwoParams()
    {
        // ESC[10;20H → Cursor Position (row=10, col=20)
        _parser.Process("\u001b[10;20H");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'H');
        csi.Params.Should().Equal(10, 20);
    }

    [Fact]
    public void Csi_CursorHome_NoParams()
    {
        _parser.Process("\u001b[H");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'H');
        csi.Params.Should().BeEmpty();
    }

    // ── CSI: Erase ──────────────────────────────────────────────────

    [Fact]
    public void Csi_EraseDisplay()
    {
        _parser.Process("\u001b[2J");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'J');
        csi.Params.Should().Equal(2);
    }

    [Fact]
    public void Csi_EraseLine()
    {
        _parser.Process("\u001b[K");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'K');
        csi.Params.Should().BeEmpty();
    }

    // ── CSI: Private Modes (DECSET / DECRST) ────────────────────────

    [Fact]
    public void Csi_PrivateMode_ShowCursor()
    {
        _parser.Process("\u001b[?25h");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.FinalByte.Should().Be((byte)'h');
        csi.Params.Should().Equal(25);
        csi.Intermediates.Should().Equal((byte)'?');
    }

    [Fact]
    public void Csi_PrivateMode_AltScreen()
    {
        _parser.Process("\u001b[?1049h");
        var csi = _handler.CsiEvents.Should().ContainSingle().Subject;
        csi.Params.Should().Equal(1049);
        csi.Intermediates.Should().Equal((byte)'?');
    }

    [Fact]
    public void Csi_InvalidPrivateAfterParams_Ignored()
    {
        // '?' después de params → CsiIgnore → no dispatch
        _parser.Process("\u001b[25?h");
        _handler.CsiEvents.Should().BeEmpty();
    }

    // ── ESC sequences ───────────────────────────────────────────────

    [Fact]
    public void Esc_ReverseIndex()
    {
        _parser.Process("\u001bM");
        var esc = _handler.EscEvents.Should().ContainSingle().Subject;
        esc.FinalByte.Should().Be((byte)'M');
        esc.Intermediates.Should().BeEmpty();
    }

    [Fact]
    public void Esc_SaveCursor()
    {
        _parser.Process("\u001b7");
        var esc = _handler.EscEvents.Should().ContainSingle().Subject;
        esc.FinalByte.Should().Be((byte)'7');
    }

    [Fact]
    public void Esc_RestoreCursor()
    {
        _parser.Process("\u001b8");
        var esc = _handler.EscEvents.Should().ContainSingle().Subject;
        esc.FinalByte.Should().Be((byte)'8');
    }

    [Fact]
    public void Esc_Charset_WithIntermediate()
    {
        // ESC ( B → Designate G0 Character Set (USASCII)
        _parser.Process("\u001b(B");
        var esc = _handler.EscEvents.Should().ContainSingle().Subject;
        esc.FinalByte.Should().Be((byte)'B');
        esc.Intermediates.Should().Equal((byte)'(');
    }

    // ── OSC sequences ───────────────────────────────────────────────

    [Fact]
    public void Osc_SetTitle_BelTerminated()
    {
        _parser.Process("\u001b]0;My Terminal\u0007");
        var osc = _handler.OscEvents.Should().ContainSingle().Subject;
        var text = System.Text.Encoding.ASCII.GetString(osc.Data);
        text.Should().Be("0;My Terminal");
    }

    [Fact]
    public void Osc_SetTitle_StTerminated()
    {
        // ST = ESC\ — el OSC se despacha al salir del estado OscString (en el ESC)
        _parser.Process("\u001b]0;Title\u001b\\");
        var osc = _handler.OscEvents.Should().ContainSingle().Subject;
        var text = System.Text.Encoding.ASCII.GetString(osc.Data);
        text.Should().Be("0;Title");
    }

    // ── Secuencias mezcladas ────────────────────────────────────────

    [Fact]
    public void Mixed_TextAndCsi_InterleavedCorrectly()
    {
        _parser.Process("Hello\u001b[31m World\u001b[0m!");
        _handler.PrintedText.Should().Be("Hello World!");
        _handler.CsiEvents.Should().HaveCount(2);
        _handler.CsiEvents.First().Params.Should().Equal(31);
        _handler.CsiEvents.Last().Params.Should().Equal(0);
    }

    [Fact]
    public void Mixed_MultipleSequenceTypes()
    {
        _parser.Process("A\u001b[1mB\u001bMC\n");
        _handler.Prints.Select(p => p.Character).Should().Equal('A', 'B', 'C');
        _handler.CsiEvents.Should().ContainSingle().Which.Params.Should().Equal(1);
        _handler.EscEvents.Should().ContainSingle().Which.FinalByte.Should().Be((byte)'M');
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x0A);
    }

    // ── Buffer boundaries (secuencias partidas entre llamadas) ──────

    [Fact]
    public void Partial_CsiSplitAcrossBuffers()
    {
        _parser.Process("\u001b[");    // ESC[ → parser en CsiEntry
        _parser.Process("31m");      // 31m → CsiDispatch
        _handler.CsiEvents.Should().ContainSingle().Which.Params.Should().Equal(31);
    }

    [Fact]
    public void Partial_EscAloneAtEndOfBuffer()
    {
        _parser.Process("A\u001b");    // ESC al final → parser en Escape
        _parser.Process("[0m");      // [0m → CsiDispatch
        _handler.PrintedText.Should().Be("A");
        _handler.CsiEvents.Should().ContainSingle().Which.Params.Should().Equal(0);
    }

    [Fact]
    public void Partial_IncompleteSequence_NoDispatch()
    {
        _parser.Process("\u001b[31");  // Incompleto → sin dispatch
        _handler.CsiEvents.Should().BeEmpty();
        _parser.CurrentState.Should().Be(ParserState.CsiParam);
    }

    [Fact]
    public void Partial_ResumedCorrectly()
    {
        _parser.Process("\u001b[3");
        _parser.Process("1;4");
        _parser.Process("2m");
        _handler.CsiEvents.Should().ContainSingle().Which.Params.Should().Equal(31, 42);
    }

    // ── Anywhere transitions ────────────────────────────────────────

    [Fact]
    public void Anywhere_EscInMiddleOfCsi_Resets()
    {
        // ESC[31 seguido de ESC[0m — el primer CSI se descarta
        _parser.Process("\u001b[31\u001b[0m");
        _handler.CsiEvents.Should().ContainSingle().Which.Params.Should().Equal(0);
    }

    [Fact]
    public void Anywhere_CanInMiddleOfCsi_ExecuteAndReset()
    {
        // CAN (0x18) cancela la secuencia y ejecuta
        _parser.Process("\u001b[31\u0018A");
        _handler.CsiEvents.Should().BeEmpty();
        _handler.Executes.Should().ContainSingle().Which.Code.Should().Be(0x18);
        _handler.Prints.Should().ContainSingle().Which.Character.Should().Be('A');
    }

    [Fact]
    public void Anywhere_EscReturnsToGroundAfterOsc()
    {
        _parser.Process("\u001b]0;test\u001b[31m");
        _handler.OscEvents.Should().ContainSingle();
        _handler.CsiEvents.Should().ContainSingle().Which.Params.Should().Equal(31);
    }
}
