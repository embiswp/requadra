namespace Quadra.Core.Rules;

/// <summary>
/// The four low bits of a board cell say which of its sides are an edge, that is, not joined to the cell next to it.
/// Cells joined together fall together; the painter bevels the edges.
/// </summary>
public static class CellEdge
{
    public const byte Left = 1;
    public const byte Top = 2;
    public const byte Right = 4;
    public const byte Bottom = 8;

    /// <summary>A cell holds the colour (piece number) above these bits.</summary>
    public const int ColorShift = 4;
}
