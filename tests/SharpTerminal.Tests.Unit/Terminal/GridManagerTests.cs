using FluentAssertions;
using SharpTerminal.Application.Parser;
using SharpTerminal.Application.Terminal;
using SharpTerminal.Domain.Enums;
using SharpTerminal.Domain.ValueObjects;
using Xunit;

namespace SharpTerminal.Tests.Unit.Terminal;

public class GridManagerTests
{
    private readonly Grid _grid;
    private readonly AnsiParser _parser;

    public GridManagerTests()
    {
        _grid = new Grid(20, 5);
        var manager = new GridManager(_grid);
        _parser = new AnsiParser(manager);
    }

    private string ReadRow(int row)
    {
        var chars = new char[_grid.Columns];
        for (int c = 0; c < _grid.Columns; c++)
            chars[c] = _grid[row, c].Character;
        return new string(chars).TrimEnd();
    }

    // Helper: color indexado por AnsiColor enum (para legibilidad)
    private static TerminalColor Ansi(AnsiColor c) => TerminalColor.Indexed((byte)c);

    // ── SGR: 16 colores ─────────────────────────────────────────────

    [Fact]
    public void Sgr_Reset_DefaultAttributes()
    {
        _parser.Process("\u001b[1;31m");
        _parser.Process("\u001b[0m");
        _grid.CurrentAttributes.Should().Be(CellAttributes.Default);
    }

    [Fact]
    public void Sgr_ResetWithoutParams_DefaultAttributes()
    {
        _parser.Process("\u001b[1m");
        _parser.Process("\u001b[m");
        _grid.CurrentAttributes.Should().Be(CellAttributes.Default);
    }

    [Fact]
    public void Sgr_ForegroundRed()
    {
        _parser.Process("\u001b[31m");
        _grid.CurrentAttributes.Foreground.Should().Be(Ansi(AnsiColor.Red));
    }

    [Fact]
    public void Sgr_BackgroundBlue()
    {
        _parser.Process("\u001b[44m");
        _grid.CurrentAttributes.Background.Should().Be(Ansi(AnsiColor.Blue));
    }

    [Fact]
    public void Sgr_Bold()
    {
        _parser.Process("\u001b[1m");
        _grid.CurrentAttributes.Flags.Should().HaveFlag(CellFlags.Bold);
    }

    [Fact]
    public void Sgr_BrightForeground()
    {
        _parser.Process("\u001b[91m");
        _grid.CurrentAttributes.Foreground.Should().Be(Ansi(AnsiColor.BrightRed));
    }

    [Fact]
    public void Sgr_BrightBackground()
    {
        _parser.Process("\u001b[102m");
        _grid.CurrentAttributes.Background.Should().Be(TerminalColor.Indexed(10)); // BrightGreen
    }

    [Fact]
    public void Sgr_BoldAndColor_Combined()
    {
        _parser.Process("\u001b[1;31m");
        _grid.CurrentAttributes.Foreground.Should().Be(Ansi(AnsiColor.Red));
        _grid.CurrentAttributes.Flags.Should().HaveFlag(CellFlags.Bold);
    }

    [Fact]
    public void Sgr_DisableBold()
    {
        _parser.Process("\u001b[1m");
        _parser.Process("\u001b[22m");
        _grid.CurrentAttributes.Flags.Should().NotHaveFlag(CellFlags.Bold);
    }

    [Fact]
    public void Sgr_DefaultForeground()
    {
        _parser.Process("\u001b[31m");
        _parser.Process("\u001b[39m");
        _grid.CurrentAttributes.Foreground.Should().Be(TerminalColor.Default);
    }

    // ── SGR: 256 colores ────────────────────────────────────────────

    [Fact]
    public void Sgr_256Color_Foreground()
    {
        _parser.Process("\u001b[38;5;208m"); // orange (index 208)
        _grid.CurrentAttributes.Foreground.Should().Be(TerminalColor.Indexed(208));
    }

    [Fact]
    public void Sgr_256Color_Background()
    {
        _parser.Process("\u001b[48;5;52m"); // dark red (index 52)
        _grid.CurrentAttributes.Background.Should().Be(TerminalColor.Indexed(52));
    }

    // ── SGR: Truecolor (RGB) ────────────────────────────────────────

    [Fact]
    public void Sgr_Truecolor_Foreground()
    {
        _parser.Process("\u001b[38;2;255;128;0m");
        _grid.CurrentAttributes.Foreground.Should().Be(TerminalColor.Rgb(255, 128, 0));
    }

    [Fact]
    public void Sgr_Truecolor_Background()
    {
        _parser.Process("\u001b[48;2;30;30;46m");
        _grid.CurrentAttributes.Background.Should().Be(TerminalColor.Rgb(30, 30, 46));
    }

    // ── CSI Cursor ──────────────────────────────────────────────────

    [Fact]
    public void CsiH_CursorPosition_OneBasedToZeroBased()
    {
        _parser.Process("\u001b[5;10H");
        _grid.CursorRow.Should().Be(4);
        _grid.CursorColumn.Should().Be(9);
    }

    [Fact]
    public void CsiH_NoParams_Home()
    {
        _grid.SetCursor(3, 7);
        _parser.Process("\u001b[H");
        _grid.CursorRow.Should().Be(0);
        _grid.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void CsiA_CursorUp()
    {
        _grid.SetCursor(3, 0);
        _parser.Process("\u001b[2A");
        _grid.CursorRow.Should().Be(1);
    }

    [Fact]
    public void CsiB_CursorDown()
    {
        _parser.Process("\u001b[3B");
        _grid.CursorRow.Should().Be(3);
    }

    [Fact]
    public void CsiG_CursorCharacterAbsolute()
    {
        _parser.Process("\u001b[15G");
        _grid.CursorColumn.Should().Be(14);
    }

    // ── CSI Erase ───────────────────────────────────────────────────

    [Fact]
    public void CsiJ_EraseDisplay_Mode2()
    {
        _parser.Process("Hello");
        _parser.Process("\u001b[2J");
        ReadRow(0).Should().BeEmpty();
    }

    [Fact]
    public void CsiK_EraseLine_CursorToEnd()
    {
        _parser.Process("ABCDEFGHIJ");
        _grid.SetCursor(0, 5);
        _parser.Process("\u001b[K");
        ReadRow(0).Should().Be("ABCDE");
    }

    // ── Private modes ───────────────────────────────────────────────

    [Fact]
    public void PrivateMode_HideCursor()
    {
        _parser.Process("\u001b[?25l");
        _grid.CursorVisible.Should().BeFalse();
    }

    [Fact]
    public void PrivateMode_ShowCursor()
    {
        _grid.CursorVisible = false;
        _parser.Process("\u001b[?25h");
        _grid.CursorVisible.Should().BeTrue();
    }

    [Fact]
    public void PrivateMode_ApplicationCursorKeys()
    {
        _parser.Process("\u001b[?1h");
        _grid.ApplicationCursorKeys.Should().BeTrue();
        _parser.Process("\u001b[?1l");
        _grid.ApplicationCursorKeys.Should().BeFalse();
    }

    [Fact]
    public void PrivateMode_BracketedPaste()
    {
        _parser.Process("\u001b[?2004h");
        _grid.BracketedPasteMode.Should().BeTrue();
        _parser.Process("\u001b[?2004l");
        _grid.BracketedPasteMode.Should().BeFalse();
    }

    [Fact]
    public void PrivateMode_AltScreen()
    {
        _parser.Process("Main");
        _parser.Process("\u001b[?1049h"); // Enter alt screen
        _grid.IsAltScreen.Should().BeTrue();
        ReadRow(0).Should().BeEmpty(); // Alt screen is empty

        _parser.Process("Alt");
        _parser.Process("\u001b[?1049l"); // Exit alt screen
        _grid.IsAltScreen.Should().BeFalse();
        ReadRow(0).Should().Be("Main"); // Main screen restored
    }

    // ── End-to-end ──────────────────────────────────────────────────

    [Fact]
    public void EndToEnd_HelloWorldWithColor()
    {
        _parser.Process("Hello \u001b[31mWorld\u001b[0m!");
        ReadRow(0).Should().Be("Hello World!");
        _grid[0, 0].Attributes.Foreground.Should().Be(TerminalColor.Default);
        _grid[0, 6].Attributes.Foreground.Should().Be(Ansi(AnsiColor.Red));
        _grid[0, 11].Attributes.Foreground.Should().Be(TerminalColor.Default);
    }

    [Fact]
    public void EndToEnd_CrLf_NewLine()
    {
        _parser.Process("Line1\r\nLine2");
        ReadRow(0).Should().Be("Line1");
        ReadRow(1).Should().Be("Line2");
    }

    [Fact]
    public void EndToEnd_CursorMoveAndWrite()
    {
        _parser.Process("AAAA");
        _parser.Process("\u001b[1;1H");
        _parser.Process("BB");
        ReadRow(0).Should().Be("BBAA");
    }

    [Fact]
    public void EndToEnd_EraseAndRewrite()
    {
        _parser.Process("Old Text");
        _parser.Process("\u001b[2J");
        _parser.Process("\u001b[H");
        _parser.Process("New");
        ReadRow(0).Should().Be("New");
    }
}
