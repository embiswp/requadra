using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Quadra.Desktop;

/// <summary>
/// A tiny 3x5 pixel font (digits and the letters this game needs), drawn with filled rectangles so
/// the client has no font or content-pipeline dependency.
/// </summary>
internal static class PixelFont
{
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['1'] = [".#.", "##.", ".#.", ".#.", "###"],
        ['2'] = ["###", "..#", "###", "#..", "###"],
        ['3'] = ["###", "..#", "###", "..#", "###"],
        ['4'] = ["#.#", "#.#", "###", "..#", "..#"],
        ['5'] = ["###", "#..", "###", "..#", "###"],
        ['6'] = ["###", "#..", "###", "#.#", "###"],
        ['7'] = ["###", "..#", "..#", "..#", "..#"],
        ['8'] = ["###", "#.#", "###", "#.#", "###"],
        ['9'] = ["###", "#.#", "###", "..#", "###"],
        ['A'] = [".#.", "#.#", "###", "#.#", "#.#"],
        ['C'] = ["###", "#..", "#..", "#..", "###"],
        ['D'] = ["##.", "#.#", "#.#", "#.#", "##."],
        ['E'] = ["###", "#..", "##.", "#..", "###"],
        ['G'] = ["###", "#..", "#.#", "#.#", "###"],
        ['I'] = ["###", ".#.", ".#.", ".#.", "###"],
        ['L'] = ["#..", "#..", "#..", "#..", "###"],
        ['M'] = ["#.#", "###", "###", "#.#", "#.#"],
        ['N'] = ["##.", "#.#", "#.#", "#.#", "#.#"],
        ['O'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['P'] = ["###", "#.#", "###", "#..", "#.."],
        ['Q'] = ["###", "#.#", "#.#", "##.", "..#"],
        ['R'] = ["###", "#.#", "##.", "#.#", "#.#"],
        ['S'] = ["###", "#..", "###", "..#", "###"],
        ['T'] = ["###", ".#.", ".#.", ".#.", ".#."],
        ['U'] = ["#.#", "#.#", "#.#", "#.#", "###"],
        ['V'] = ["#.#", "#.#", "#.#", "#.#", ".#."],
        ['X'] = ["#.#", "#.#", ".#.", "#.#", "#.#"],
    };

    public const int GlyphWidth = 3;
    public const int GlyphHeight = 5;

    /// <summary>Width in pixels of <paramref name="text"/> at a scale (one blank column between glyphs).</summary>
    public static int Measure(string text, int scale) => text.Length == 0 ? 0 : (text.Length * (GlyphWidth + 1) - 1) * scale;

    public static void Draw(SpriteBatch batch, Texture2D pixel, string text, int x, int y, int scale, Color color)
    {
        foreach (char c in text)
        {
            if (Glyphs.TryGetValue(char.ToUpperInvariant(c), out var rows))
                for (int row = 0; row < GlyphHeight; row++)
                    for (int col = 0; col < GlyphWidth; col++)
                        if (rows[row][col] == '#')
                            batch.Draw(pixel, new Rectangle(x + col * scale, y + row * scale, scale, scale), color);
            x += (GlyphWidth + 1) * scale;
        }
    }

    internal static bool Knows(char c) => c == ' ' || Glyphs.ContainsKey(char.ToUpperInvariant(c));
}
