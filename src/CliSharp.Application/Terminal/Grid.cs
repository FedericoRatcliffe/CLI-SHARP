using CliSharp.Domain.Entities;
using CliSharp.Domain.ValueObjects;

namespace CliSharp.Application.Terminal;

/// <summary>
/// Terminal grid con scrollback, alt screen and DECSET modes,
/// Unicode double-width, hyperlinks and command blocks (OSC 133).
/// </summary>
public sealed class Grid
{
    private Cell[][] _rows;
    private byte[] _rowFlags; // 0=normal, 1=prompt start (OSC 133;A)
    private readonly List<Cell[]> _scrollback = new();
    private readonly List<byte> _scrollbackFlags = new();
    public const int ScrollbackMax = 1000;

    private Cell[][]? _savedMainRows;
    private byte[]? _savedRowFlags;
    private int _savedCursorRow, _savedCursorCol;
    private CellAttributes _savedAttributes;

    // Hyperlinks (OSC 8)
    private readonly List<string> _hyperlinks = [""]; // index 0 = no link
    private ushort _currentHyperlinkId;

    // Command blocks (OSC 133)
    private int _lastPromptRow = -1;
    private readonly List<BlockExitInfo> _blockExits = new();

    public readonly record struct BlockExitInfo(int ExitCode, DateTime Timestamp);

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorRow { get; private set; }
    public int CursorColumn { get; private set; }
    public CellAttributes CurrentAttributes { get; set; } = CellAttributes.Default;
    public bool CursorVisible { get; set; } = true;
    public int ScrollbackCount => _scrollback.Count;

    // Modos DECSET
    public bool IsAltScreen { get; private set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool BracketedPasteMode { get; set; }
    public int MouseTrackingMode { get; set; }
    public bool SgrMouseMode { get; set; }

    // Shell integration
    public string? CurrentDirectory { get; private set; }

    // Inline images (iTerm2 OSC 1337)
    public record InlineImageData(byte[] ImageBytes, int Row, int Col, int WidthCells, int HeightCells);
    private readonly List<InlineImageData> _inlineImages = new();
    public IReadOnlyList<InlineImageData> InlineImages => _inlineImages;

    // Dirty tracking (render generation)
    public long RenderGeneration { get; private set; }

    // Eventos
    public event Action<string>? TitleChanged;
    public event Action<string>? ClipboardSetRequested;
    public event Action<string>? DirectoryChanged;

    private int _viewportOffset;
    public int ViewportOffset
    {
        get => _viewportOffset;
        set => _viewportOffset = Math.Clamp(value, 0, _scrollback.Count);
    }

    public Grid(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        _rows = CreateRows(columns, rows);
        _rowFlags = new byte[rows];
    }

    private static Cell[][] CreateRows(int columns, int rows)
    {
        var r = new Cell[rows][];
        for (int i = 0; i < rows; i++)
        {
            r[i] = new Cell[columns];
            Array.Fill(r[i], Cell.Empty);
        }
        return r;
    }

    // ── Accessors ───────────────────────────────────────────────────

    public Cell this[int row, int column] => _rows[row][column];
    public ReadOnlySpan<Cell> GetRow(int row) => _rows[row];

    public ReadOnlySpan<Cell> GetVisibleRow(int viewRow)
    {
        if (_viewportOffset == 0) return _rows[viewRow];
        int idx = _scrollback.Count - _viewportOffset + viewRow;
        if (idx >= 0 && idx < _scrollback.Count) return _scrollback[idx];
        int activeRow = idx - _scrollback.Count;
        return (activeRow >= 0 && activeRow < Rows) ? _rows[activeRow] : _rows[0];
    }

    /// <summary>Returns 1 if the visible row is a prompt start (OSC 133;A).</summary>
    public byte GetVisibleRowFlag(int viewRow)
    {
        if (_viewportOffset == 0) return _rowFlags[viewRow];
        int idx = _scrollback.Count - _viewportOffset + viewRow;
        if (idx >= 0 && idx < _scrollbackFlags.Count) return _scrollbackFlags[idx];
        int activeRow = idx - _scrollback.Count;
        return (activeRow >= 0 && activeRow < Rows) ? _rowFlags[activeRow] : (byte)0;
    }

    /// <summary>Exit code associated with the prompt row (buscando hacia atrás).</summary>
    public int? GetPromptExitCode(int activeRow)
    {
        // Buscar el exit code más reciente que corresponda a este prompt
        for (int i = _blockExits.Count - 1; i >= 0; i--)
        {
            // Heurística simple: el último exit code corresponde al prompt más reciente
            if (_rowFlags[activeRow] == 1)
                return i < _blockExits.Count ? _blockExits[i].ExitCode : null;
        }
        return null;
    }

    public void ScrollViewport(int delta) => ViewportOffset += delta;
    internal void SetTitle(string title) => TitleChanged?.Invoke(title);
    internal void RequestClipboardSet(string text) => ClipboardSetRequested?.Invoke(text);
    private void Touch() => RenderGeneration++;

    internal void AddInlineImage(byte[] data, int widthCells, int heightCells)
    {
        _inlineImages.Add(new InlineImageData(data, CursorRow, CursorColumn, widthCells, heightCells));
        CursorRow = Math.Min(CursorRow + heightCells, Rows - 1);
        Touch();
    }

    internal void SetCurrentDirectory(string path)
    {
        CurrentDirectory = path;
        DirectoryChanged?.Invoke(path);
    }

    // ── Command blocks (OSC 133) ────────────────────────────────────

    internal void MarkPromptStart()
    {
        _rowFlags[CursorRow] = 1;
        _lastPromptRow = CursorRow;
    }

    internal void MarkCommandEnd(int exitCode)
    {
        _blockExits.Add(new BlockExitInfo(exitCode, DateTime.Now));
        // Associate exit code con el prompt row (para el renderer)
        if (_lastPromptRow >= 0 && _lastPromptRow < Rows)
            _rowFlags[_lastPromptRow] = (byte)(exitCode == 0 ? 2 : 3); // 2=ok, 3=error
    }

    // ── Hyperlinks (OSC 8) ──────────────────────────────────────────

    public void SetHyperlink(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) { _currentHyperlinkId = 0; return; }
        if (_hyperlinks.Count > 10_000) { _hyperlinks.Clear(); _hyperlinks.Add(""); }
        _hyperlinks.Add(uri);
        _currentHyperlinkId = (ushort)(_hyperlinks.Count - 1);
    }

    public string? GetHyperlinkUri(ushort id)
        => id > 0 && id < _hyperlinks.Count ? _hyperlinks[id] : null;

    // ── Printing (con soporte double-width) ─────────────────────────

    public void PrintChar(char c)
    {
        Touch();
        int width = UnicodeWidth.GetWidth(c);
        if (width == 0) return;

        var attrs = _currentHyperlinkId > 0
            ? CurrentAttributes with { HyperlinkId = _currentHyperlinkId }
            : CurrentAttributes;

        if (width == 2)
        {
            if (CursorColumn >= Columns - 1)
            {
                if (CursorColumn == Columns - 1) _rows[CursorRow][CursorColumn] = Cell.Empty;
                CursorColumn = 0; LineFeed();
            }
            else if (CursorColumn >= Columns) { CursorColumn = 0; LineFeed(); }

            ClearWideCharIfNeeded(CursorRow, CursorColumn);
            ClearWideCharIfNeeded(CursorRow, CursorColumn + 1);
            _rows[CursorRow][CursorColumn] = new Cell(c, attrs);
            _rows[CursorRow][CursorColumn + 1] = new Cell(' ', attrs with { Flags = attrs.Flags | CellFlags.WideCharCont });
            CursorColumn += 2;
        }
        else
        {
            if (CursorColumn >= Columns) { CursorColumn = 0; LineFeed(); }
            ClearWideCharIfNeeded(CursorRow, CursorColumn);
            _rows[CursorRow][CursorColumn] = new Cell(c, attrs);
            CursorColumn++;
        }
    }

    private void ClearWideCharIfNeeded(int row, int col)
    {
        if (col < 0 || col >= Columns) return;
        if (_rows[row][col].Attributes.Flags.HasFlag(CellFlags.WideCharCont) && col > 0)
            _rows[row][col - 1] = Cell.Empty;
        if (col + 1 < Columns && _rows[row][col + 1].Attributes.Flags.HasFlag(CellFlags.WideCharCont))
            _rows[row][col + 1] = Cell.Empty;
    }

    // ── Control codes ───────────────────────────────────────────────

    public void LineFeed()
    {
        if (CursorRow < Rows - 1) CursorRow++;
        else ScrollUp();
    }

    public void CarriageReturn() => CursorColumn = 0;
    public void Backspace() { if (CursorColumn > 0) CursorColumn--; }
    public void Tab() { CursorColumn = Math.Min(((CursorColumn / 8) + 1) * 8, Columns - 1); }
    public void ReverseIndex() { if (CursorRow > 0) CursorRow--; else ScrollDown(); }

    // ── Cursor movement ─────────────────────────────────────────────

    public void SetCursor(int row, int column)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
    }

    public void CursorUp(int n) => CursorRow = Math.Max(0, CursorRow - n);
    public void CursorDown(int n) => CursorRow = Math.Min(Rows - 1, CursorRow + n);
    public void CursorForward(int n) => CursorColumn = Math.Min(Columns - 1, CursorColumn + n);
    public void CursorBackward(int n) => CursorColumn = Math.Max(0, CursorColumn - n);

    // ── Erase ───────────────────────────────────────────────────────

    public void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseRange(CursorRow, CursorColumn, Columns);
                for (int r = CursorRow + 1; r < Rows; r++) Array.Fill(_rows[r], Cell.Empty);
                break;
            case 1:
                for (int r = 0; r < CursorRow; r++) Array.Fill(_rows[r], Cell.Empty);
                EraseRange(CursorRow, 0, CursorColumn + 1);
                break;
            case 2: case 3:
                for (int r = 0; r < Rows; r++) Array.Fill(_rows[r], Cell.Empty);
                break;
        }
    }

    public void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: EraseRange(CursorRow, CursorColumn, Columns); break;
            case 1: EraseRange(CursorRow, 0, CursorColumn + 1); break;
            case 2: Array.Fill(_rows[CursorRow], Cell.Empty); break;
        }
    }

    private void EraseRange(int row, int start, int end)
    {
        int count = Math.Min(end, Columns) - start;
        if (count > 0) Array.Fill(_rows[row], Cell.Empty, start, count);
    }

    // ── Scroll ──────────────────────────────────────────────────────

    public void ScrollUp()
    {
        if (!IsAltScreen)
        {
            _scrollback.Add((Cell[])_rows[0].Clone());
            _scrollbackFlags.Add(_rowFlags[0]);
            if (_scrollback.Count > ScrollbackMax)
            {
                _scrollback.RemoveAt(0);
                _scrollbackFlags.RemoveAt(0);
            }
        }
        var first = _rows[0];
        Array.Copy(_rows, 1, _rows, 0, Rows - 1);
        Array.Copy(_rowFlags, 1, _rowFlags, 0, Rows - 1);
        _rows[Rows - 1] = first;
        _rowFlags[Rows - 1] = 0;
        Array.Fill(first, Cell.Empty);
    }

    public void ScrollDown()
    {
        var last = _rows[Rows - 1];
        Array.Copy(_rows, 0, _rows, 1, Rows - 1);
        Array.Copy(_rowFlags, 0, _rowFlags, 1, Rows - 1);
        _rows[0] = last;
        _rowFlags[0] = 0;
        Array.Fill(last, Cell.Empty);
    }

    // ── Alt Screen Buffer ───────────────────────────────────────────

    public void EnterAltScreen()
    {
        if (IsAltScreen) return;
        _savedMainRows = _rows; _savedRowFlags = _rowFlags;
        _savedCursorRow = CursorRow; _savedCursorCol = CursorColumn;
        _savedAttributes = CurrentAttributes;
        _rows = CreateRows(Columns, Rows); _rowFlags = new byte[Rows];
        CursorRow = 0; CursorColumn = 0; IsAltScreen = true;
    }

    public void ExitAltScreen()
    {
        if (!IsAltScreen || _savedMainRows is null) return;
        _rows = _savedMainRows; _rowFlags = _savedRowFlags ?? new byte[Rows];
        _savedMainRows = null; _savedRowFlags = null;
        CursorRow = _savedCursorRow; CursorColumn = _savedCursorCol;
        CurrentAttributes = _savedAttributes; IsAltScreen = false;
    }

    // ── Resize ──────────────────────────────────────────────────────

    public void Resize(int newColumns, int newRows)
    {
        if (newColumns == Columns && newRows == Rows) return;
        var newArr = CreateRows(newColumns, newRows);
        var newFlags = new byte[newRows];
        int copyRows = Math.Min(Rows, newRows);
        int copyCols = Math.Min(Columns, newColumns);
        for (int r = 0; r < copyRows; r++) Array.Copy(_rows[r], newArr[r], copyCols);
        Array.Copy(_rowFlags, newFlags, Math.Min(Rows, newRows));
        _rows = newArr; _rowFlags = newFlags; Columns = newColumns; Rows = newRows;
        CursorRow = Math.Clamp(CursorRow, 0, Rows - 1);
        CursorColumn = Math.Clamp(CursorColumn, 0, Columns - 1);
    }
}
