namespace LiveClaude.Vt;

/// <summary>
/// The character grid a VT terminal draws on: primary and alternate buffers, a cursor, a scroll
/// region and the handful of erase/scroll primitives the escape sequences map onto.
/// </summary>
public sealed class TerminalScreen
{
    private TerminalCell[,] _primary;
    private TerminalCell[,] _alternate;
    private bool _usingAlternate;

    private int _savedRow;
    private int _savedColumn;

    public TerminalScreen(int columns, int rows)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        _primary = CreateBuffer(Columns, Rows);
        _alternate = CreateBuffer(Columns, Rows);
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public int CursorRow { get; private set; }

    public int CursorColumn { get; private set; }

    public bool CursorVisible { get; set; } = true;

    public int ScrollTop { get; private set; }

    public int ScrollBottom { get; private set; }

    public TerminalCell Pen = TerminalCell.Empty;

    /// <summary>Incremented on every mutation, so the view only repaints when something changed.</summary>
    public long Revision { get; private set; }

    private TerminalCell[,] Buffer => _usingAlternate ? _alternate : _primary;

    public TerminalCell this[int row, int column] => Buffer[
        Math.Clamp(row, 0, Rows - 1),
        Math.Clamp(column, 0, Columns - 1)];

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows)
            return;

        _primary = Resize(_primary, Columns, Rows, columns, rows);
        _alternate = Resize(_alternate, Columns, Rows, columns, rows);

        Columns = columns;
        Rows = rows;
        ScrollTop = 0;
        ScrollBottom = rows - 1;
        CursorRow = Math.Min(CursorRow, rows - 1);
        CursorColumn = Math.Min(CursorColumn, columns - 1);
        Touch();
    }

    public void UseAlternateBuffer(bool value)
    {
        if (_usingAlternate == value)
            return;

        _usingAlternate = value;
        if (value)
        {
            Clear(_alternate);
            CursorRow = 0;
            CursorColumn = 0;
        }

        Touch();
    }

    public void Write(char c)
    {
        if (CursorColumn >= Columns)
        {
            CursorColumn = 0;
            LineFeed();
        }

        var cell = Pen;
        cell.Char = c;
        Buffer[CursorRow, CursorColumn] = cell;
        CursorColumn++;
        Touch();
    }

    public void CarriageReturn()
    {
        CursorColumn = 0;
        Touch();
    }

    public void LineFeed()
    {
        if (CursorRow == ScrollBottom)
            ScrollUp(1);
        else if (CursorRow < Rows - 1)
            CursorRow++;

        Touch();
    }

    public void ReverseLineFeed()
    {
        if (CursorRow == ScrollTop)
            ScrollDown(1);
        else if (CursorRow > 0)
            CursorRow--;

        Touch();
    }

    public void Backspace()
    {
        if (CursorColumn > 0)
            CursorColumn--;
        Touch();
    }

    public void Tab()
    {
        var next = (CursorColumn / 8 + 1) * 8;
        CursorColumn = Math.Min(next, Columns - 1);
        Touch();
    }

    public void MoveCursor(int row, int column)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        Touch();
    }

    public void MoveCursorRelative(int rowDelta, int columnDelta) =>
        MoveCursor(CursorRow + rowDelta, CursorColumn + columnDelta);

    public void SaveCursor()
    {
        _savedRow = CursorRow;
        _savedColumn = CursorColumn;
    }

    public void RestoreCursor() => MoveCursor(_savedRow, _savedColumn);

    public void SetScrollRegion(int top, int bottom)
    {
        ScrollTop = Math.Clamp(top, 0, Rows - 1);
        ScrollBottom = Math.Clamp(bottom, ScrollTop, Rows - 1);
        MoveCursor(ScrollTop, 0);
    }

    /// <param name="mode">0: cursor to end, 1: start to cursor, 2/3: whole screen.</param>
    public void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (var row = CursorRow + 1; row < Rows; row++)
                    ClearRow(row);
                break;
            case 1:
                EraseInLine(1);
                for (var row = 0; row < CursorRow; row++)
                    ClearRow(row);
                break;
            default:
                for (var row = 0; row < Rows; row++)
                    ClearRow(row);
                break;
        }

        Touch();
    }

    /// <param name="mode">0: cursor to end of line, 1: start of line to cursor, 2: whole line.</param>
    public void EraseInLine(int mode)
    {
        var from = mode switch { 0 => CursorColumn, 1 => 0, _ => 0 };
        var to = mode switch { 0 => Columns - 1, 1 => CursorColumn, _ => Columns - 1 };

        for (var column = from; column <= to && column < Columns; column++)
            Buffer[CursorRow, column] = Blank();

        Touch();
    }

    public void EraseCharacters(int count)
    {
        for (var i = 0; i < count && CursorColumn + i < Columns; i++)
            Buffer[CursorRow, CursorColumn + i] = Blank();
        Touch();
    }

    public void DeleteCharacters(int count)
    {
        for (var column = CursorColumn; column < Columns; column++)
        {
            var source = column + count;
            Buffer[CursorRow, column] = source < Columns ? Buffer[CursorRow, source] : Blank();
        }

        Touch();
    }

    public void InsertCharacters(int count)
    {
        for (var column = Columns - 1; column >= CursorColumn; column--)
        {
            var source = column - count;
            Buffer[CursorRow, column] = source >= CursorColumn ? Buffer[CursorRow, source] : Blank();
        }

        Touch();
    }

    public void InsertLines(int count)
    {
        for (var i = 0; i < count; i++)
        {
            for (var row = ScrollBottom; row > CursorRow; row--)
                CopyRow(row - 1, row);
            ClearRow(CursorRow);
        }

        Touch();
    }

    public void DeleteLines(int count)
    {
        for (var i = 0; i < count; i++)
        {
            for (var row = CursorRow; row < ScrollBottom; row++)
                CopyRow(row + 1, row);
            ClearRow(ScrollBottom);
        }

        Touch();
    }

    public void ScrollUp(int count)
    {
        for (var i = 0; i < count; i++)
        {
            for (var row = ScrollTop; row < ScrollBottom; row++)
                CopyRow(row + 1, row);
            ClearRow(ScrollBottom);
        }

        Touch();
    }

    public void ScrollDown(int count)
    {
        for (var i = 0; i < count; i++)
        {
            for (var row = ScrollBottom; row > ScrollTop; row--)
                CopyRow(row - 1, row);
            ClearRow(ScrollTop);
        }

        Touch();
    }

    public void Reset()
    {
        Clear(_primary);
        Clear(_alternate);
        _usingAlternate = false;
        Pen = TerminalCell.Empty;
        CursorRow = 0;
        CursorColumn = 0;
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        CursorVisible = true;
        Touch();
    }

    /// <summary>Plain-text copy of the visible screen, for "copy all" in the UI.</summary>
    public string GetText()
    {
        var sb = new System.Text.StringBuilder(Rows * (Columns + 2));
        for (var row = 0; row < Rows; row++)
        {
            var line = new char[Columns];
            for (var column = 0; column < Columns; column++)
                line[column] = Buffer[row, column].Char;

            sb.AppendLine(new string(line).TrimEnd());
        }

        return sb.ToString().TrimEnd();
    }

    private TerminalCell Blank()
    {
        var cell = TerminalCell.Empty;
        cell.Background = Pen.Background;
        return cell;
    }

    private void ClearRow(int row)
    {
        for (var column = 0; column < Columns; column++)
            Buffer[row, column] = Blank();
    }

    private void CopyRow(int from, int to)
    {
        for (var column = 0; column < Columns; column++)
            Buffer[to, column] = Buffer[from, column];
    }

    private void Touch() => Revision++;

    private static TerminalCell[,] CreateBuffer(int columns, int rows)
    {
        var buffer = new TerminalCell[rows, columns];
        Clear(buffer);
        return buffer;
    }

    private static void Clear(TerminalCell[,] buffer)
    {
        for (var row = 0; row < buffer.GetLength(0); row++)
        for (var column = 0; column < buffer.GetLength(1); column++)
            buffer[row, column] = TerminalCell.Empty;
    }

    private static TerminalCell[,] Resize(TerminalCell[,] source, int oldColumns, int oldRows, int columns, int rows)
    {
        var target = CreateBuffer(columns, rows);

        // Keep the bottom of the buffer, which is where the live content is.
        var rowOffset = Math.Max(0, oldRows - rows);
        for (var row = 0; row < Math.Min(rows, oldRows); row++)
        for (var column = 0; column < Math.Min(columns, oldColumns); column++)
            target[row, column] = source[row + rowOffset, column];

        return target;
    }
}
