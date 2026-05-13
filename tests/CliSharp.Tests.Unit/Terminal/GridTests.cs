using FluentAssertions;
using CliSharp.Application.Terminal;
using CliSharp.Domain.Entities;
using CliSharp.Domain.Enums;
using CliSharp.Domain.ValueObjects;
using Xunit;

namespace CliSharp.Tests.Unit.Terminal;

public class GridTests
{
    private static Grid Create(int cols = 10, int rows = 5) => new(cols, rows);

    private static string ReadRow(Grid grid, int row)
    {
        var chars = new char[grid.Columns];
        for (int c = 0; c < grid.Columns; c++)
            chars[c] = grid[row, c].Character;
        return new string(chars).TrimEnd();
    }

    private static string ReadVisibleRow(Grid grid, int viewRow)
    {
        var span = grid.GetVisibleRow(viewRow);
        var chars = new char[span.Length];
        for (int c = 0; c < span.Length; c++)
            chars[c] = span[c].Character;
        return new string(chars).TrimEnd();
    }

    // ── Estado inicial ──────────────────────────────────────────────

    [Fact]
    public void Initial_AllCellsEmpty()
    {
        var grid = Create();
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Columns; c++)
                grid[r, c].Should().Be(Cell.Empty);
    }

    [Fact]
    public void Initial_CursorAtOrigin()
    {
        var grid = Create();
        grid.CursorRow.Should().Be(0);
        grid.CursorColumn.Should().Be(0);
    }

    // ── PrintChar ───────────────────────────────────────────────────

    [Fact]
    public void PrintChar_WritesCharAndAdvancesCursor()
    {
        var grid = Create();
        grid.PrintChar('A');
        grid[0, 0].Character.Should().Be('A');
        grid.CursorColumn.Should().Be(1);
    }

    [Fact]
    public void PrintChar_UsesCurrentAttributes()
    {
        var grid = Create();
        grid.CurrentAttributes = new CellAttributes(TerminalColor.Indexed(1), TerminalColor.Indexed(4), CellFlags.Bold);
        grid.PrintChar('X');
        grid[0, 0].Attributes.Foreground.Should().Be(TerminalColor.Indexed(1));
        grid[0, 0].Attributes.Background.Should().Be(TerminalColor.Indexed(4));
        grid[0, 0].Attributes.Flags.Should().HaveFlag(CellFlags.Bold);
    }

    [Fact]
    public void PrintChar_MultipleChars_WritesSequentially()
    {
        var grid = Create();
        foreach (char c in "Hello")
            grid.PrintChar(c);
        ReadRow(grid, 0).Should().Be("Hello");
        grid.CursorColumn.Should().Be(5);
    }

    [Fact]
    public void PrintChar_AtEndOfLine_WrapsToNextLine()
    {
        var grid = Create(cols: 5);
        foreach (char c in "ABCDE")
            grid.PrintChar(c);
        // Cursor at column 5 (past end), deferred wrap
        grid.CursorColumn.Should().Be(5);

        // Next char triggers wrap
        grid.PrintChar('F');
        grid.CursorRow.Should().Be(1);
        grid.CursorColumn.Should().Be(1);
        ReadRow(grid, 0).Should().Be("ABCDE");
        grid[1, 0].Character.Should().Be('F');
    }

    [Fact]
    public void PrintChar_AtBottomRight_WrapsAndScrolls()
    {
        var grid = Create(cols: 3, rows: 2);
        // Llenar fila 0
        foreach (char c in "ABC") grid.PrintChar(c);
        // Llenar fila 1
        grid.PrintChar('D'); // triggers wrap to row 1
        grid.PrintChar('E');
        grid.PrintChar('F');
        // Ahora cursor en col=3 (past end), row=1

        // Siguiente char: wrap → LF → scroll
        grid.PrintChar('G');
        ReadRow(grid, 0).Should().Be("DEF"); // fila 0 se perdió, fila 1 subió
        grid[1, 0].Character.Should().Be('G');
    }

    // ── Control codes ───────────────────────────────────────────────

    [Fact]
    public void LineFeed_MovesCursorDown()
    {
        var grid = Create();
        grid.LineFeed();
        grid.CursorRow.Should().Be(1);
        grid.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void LineFeed_AtBottom_ScrollsUp()
    {
        var grid = Create(cols: 5, rows: 3);
        foreach (char c in "AAA") grid.PrintChar(c);
        grid.SetCursor(2, 0); // Última fila
        grid.LineFeed();
        grid.CursorRow.Should().Be(2); // Se queda en la última fila
        ReadRow(grid, 0).Should().BeEmpty(); // Primera fila original se fue
    }

    [Fact]
    public void CarriageReturn_MovesToColumn0()
    {
        var grid = Create();
        grid.PrintChar('A');
        grid.PrintChar('B');
        grid.CarriageReturn();
        grid.CursorColumn.Should().Be(0);
        grid.CursorRow.Should().Be(0);
    }

    [Fact]
    public void Backspace_DecrementsColumn()
    {
        var grid = Create();
        grid.PrintChar('A');
        grid.PrintChar('B');
        grid.Backspace();
        grid.CursorColumn.Should().Be(1);
    }

    [Fact]
    public void Backspace_AtColumn0_Stays()
    {
        var grid = Create();
        grid.Backspace();
        grid.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void Tab_MovesToNextTabStop()
    {
        var grid = Create(cols: 20);
        grid.PrintChar('A'); // col = 1
        grid.Tab();
        grid.CursorColumn.Should().Be(8); // next tab stop

        grid.Tab();
        grid.CursorColumn.Should().Be(16); // next tab stop
    }

    // ── Cursor movement ─────────────────────────────────────────────

    [Fact]
    public void SetCursor_ValidPosition()
    {
        var grid = Create();
        grid.SetCursor(3, 7);
        grid.CursorRow.Should().Be(3);
        grid.CursorColumn.Should().Be(7);
    }

    [Fact]
    public void SetCursor_OutOfBounds_Clamped()
    {
        var grid = Create(cols: 10, rows: 5);
        grid.SetCursor(99, 99);
        grid.CursorRow.Should().Be(4);
        grid.CursorColumn.Should().Be(9);

        grid.SetCursor(-5, -5);
        grid.CursorRow.Should().Be(0);
        grid.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void CursorUp_StaysInBounds()
    {
        var grid = Create();
        grid.SetCursor(2, 0);
        grid.CursorUp(5);
        grid.CursorRow.Should().Be(0);
    }

    [Fact]
    public void CursorDown_StaysInBounds()
    {
        var grid = Create(rows: 5);
        grid.CursorDown(99);
        grid.CursorRow.Should().Be(4);
    }

    // ── Erase ───────────────────────────────────────────────────────

    [Fact]
    public void EraseDisplay_Mode2_ClearsAll()
    {
        var grid = Create(cols: 5, rows: 3);
        foreach (char c in "Hello") grid.PrintChar(c);
        grid.EraseDisplay(2);
        for (int r = 0; r < grid.Rows; r++)
            ReadRow(grid, r).Should().BeEmpty();
    }

    [Fact]
    public void EraseLine_Mode0_CursorToEnd()
    {
        var grid = Create(cols: 10);
        foreach (char c in "ABCDEFGHIJ") grid.PrintChar(c);
        grid.SetCursor(0, 5);
        grid.EraseLine(0);
        ReadRow(grid, 0).Should().Be("ABCDE");
    }

    [Fact]
    public void EraseLine_Mode2_ClearsEntireRow()
    {
        var grid = Create(cols: 5);
        foreach (char c in "Hello") grid.PrintChar(c);
        grid.SetCursor(0, 2);
        grid.EraseLine(2);
        ReadRow(grid, 0).Should().BeEmpty();
    }

    // ── Scroll ──────────────────────────────────────────────────────

    [Fact]
    public void ScrollUp_ShiftsRowsUp()
    {
        var grid = Create(cols: 3, rows: 3);
        // Fila 0: "AAA", Fila 1: "BBB", Fila 2: "CCC"
        foreach (char c in "AAA") grid.PrintChar(c);
        grid.SetCursor(1, 0);
        foreach (char c in "BBB") grid.PrintChar(c);
        grid.SetCursor(2, 0);
        foreach (char c in "CCC") grid.PrintChar(c);

        grid.ScrollUp();
        ReadRow(grid, 0).Should().Be("BBB");
        ReadRow(grid, 1).Should().Be("CCC");
        ReadRow(grid, 2).Should().BeEmpty();
    }

    [Fact]
    public void ScrollDown_ShiftsRowsDown()
    {
        var grid = Create(cols: 3, rows: 3);
        foreach (char c in "AAA") grid.PrintChar(c);
        grid.SetCursor(1, 0);
        foreach (char c in "BBB") grid.PrintChar(c);

        grid.ScrollDown();
        ReadRow(grid, 0).Should().BeEmpty();
        ReadRow(grid, 1).Should().Be("AAA");
        ReadRow(grid, 2).Should().Be("BBB");
    }

    // ── Scrollback ──────────────────────────────────────────────────

    [Fact]
    public void ScrollUp_AddsToScrollback()
    {
        var grid = Create(cols: 3, rows: 2);
        foreach (char c in "AAA") grid.PrintChar(c);
        grid.SetCursor(1, 0);
        foreach (char c in "BBB") grid.PrintChar(c);

        grid.ScrollUp(); // "AAA" va al scrollback
        grid.ScrollbackCount.Should().Be(1);
    }

    [Fact]
    public void ScrollbackMax_Enforced()
    {
        var grid = Create(cols: 3, rows: 2);
        for (int i = 0; i < Grid.ScrollbackMax + 50; i++)
        {
            grid.PrintChar('X');
            grid.SetCursor(1, 0);
            grid.ScrollUp();
        }
        grid.ScrollbackCount.Should().BeLessOrEqualTo(Grid.ScrollbackMax);
    }

    [Fact]
    public void ViewportOffset_ClampedToScrollbackCount()
    {
        var grid = Create(cols: 3, rows: 2);
        grid.ScrollUp(); // 1 en scrollback
        grid.ScrollUp(); // 2 en scrollback

        grid.ViewportOffset = 999;
        grid.ViewportOffset.Should().Be(2);

        grid.ViewportOffset = -5;
        grid.ViewportOffset.Should().Be(0);
    }

    [Fact]
    public void GetVisibleRow_AtBottom_ReturnsActiveRow()
    {
        var grid = Create(cols: 5, rows: 2);
        foreach (char c in "AAAAA") grid.PrintChar(c);
        grid.SetCursor(1, 0);
        foreach (char c in "BBBBB") grid.PrintChar(c);
        grid.ScrollUp();

        grid.ViewportOffset = 0;
        ReadVisibleRow(grid, 0).Should().Be("BBBBB");
    }

    [Fact]
    public void GetVisibleRow_ScrolledUp_ReturnsScrollbackRow()
    {
        var grid = Create(cols: 5, rows: 2);
        foreach (char c in "AAAAA") grid.PrintChar(c);
        grid.SetCursor(1, 0);
        foreach (char c in "BBBBB") grid.PrintChar(c);
        grid.ScrollUp();

        grid.ViewportOffset = 1;
        ReadVisibleRow(grid, 0).Should().Be("AAAAA");
    }

    // ── Resize ──────────────────────────────────────────────────────

    [Fact]
    public void Resize_Smaller_ContentPreserved()
    {
        var grid = Create(cols: 10, rows: 5);
        foreach (char c in "Hello") grid.PrintChar(c);

        grid.Resize(5, 3);
        grid.Columns.Should().Be(5);
        grid.Rows.Should().Be(3);
        ReadRow(grid, 0).Should().Be("Hello");
    }

    [Fact]
    public void Resize_Larger_NewCellsEmpty()
    {
        var grid = Create(cols: 5, rows: 2);
        foreach (char c in "Hi") grid.PrintChar(c);

        grid.Resize(10, 4);
        ReadRow(grid, 0).Should().Be("Hi");
        ReadRow(grid, 2).Should().BeEmpty();
        ReadRow(grid, 3).Should().BeEmpty();
    }

    [Fact]
    public void Resize_CursorClamped()
    {
        var grid = Create(cols: 10, rows: 10);
        grid.SetCursor(8, 8);
        grid.Resize(5, 5);
        grid.CursorRow.Should().Be(4);
        grid.CursorColumn.Should().Be(4);
    }
}
