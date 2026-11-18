namespace Quadra.Core.Game;

/// <summary>The most recent chat lines of a network game, oldest first.</summary>
public sealed class ChatLog(int capacity = 50)
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public void Add(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return;
        _lines.Add(line);
        if (_lines.Count > capacity)
            _lines.RemoveAt(0);
    }

    public IEnumerable<string> Last(int count) => _lines.Skip(Math.Max(0, _lines.Count - count));
}
