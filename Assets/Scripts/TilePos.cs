/// <summary>
/// Struct representing a position on the tile board, with column being the outer index
/// and row being the inner index
/// </summary>
public struct TilePos
{
    /// <summary>
    /// Outer index on the tile board
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Inner index on the tile board
    /// </summary>
    public int Row { get; set; }

    /// <summary>
    /// Constructor to create a new TilePos
    /// </summary>
    /// <param name="col">Column, outer index</param>
    /// <param name="row">Row, inner index</param>
    public TilePos(int col, int row)
    {
        Column = col;
        Row = row;
    }

    public readonly void Deconstruct(out int col, out int row)
    {
        col = Column;
        row = Row;
    }
}