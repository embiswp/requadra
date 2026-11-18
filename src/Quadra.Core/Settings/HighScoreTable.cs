using System.Text.Json;

namespace Quadra.Core.Settings;

/// <summary>One entry of the local high score list.</summary>
public sealed record HighScore(string Name, int Score, int Lines, int Level, DateTime When);

/// <summary>
/// The local high scores: the best five single-player games (<c>MAX_SCORE</c> in the original), best first.
/// The original keeps each as a recorded game that can be played back; this keeps the figures only.
/// </summary>
public sealed class HighScoreTable
{
    public const int MaxEntries = 5;

    private readonly List<HighScore> _entries = [];

    public IReadOnlyList<HighScore> Entries => _entries;

    /// <summary>True if a game with this score would make the list.</summary>
    public bool Qualifies(int score) =>
        score > 0 && (_entries.Count < MaxEntries || score > _entries[^1].Score);

    /// <summary>
    /// Adds a game. Returns its place (0 for the best), or -1 if it did not make the list. A game that
    /// ties an older one goes below it.
    /// </summary>
    public int Add(HighScore score)
    {
        if (!Qualifies(score.Score))
            return -1;
        int place = _entries.FindIndex(e => score.Score > e.Score);
        if (place < 0)
            place = _entries.Count;
        _entries.Insert(place, score);
        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);
        return place;
    }

    public static HighScoreTable Load(string path)
    {
        var table = new HighScoreTable();
        try
        {
            var loaded = JsonSerializer.Deserialize<List<HighScore>>(File.ReadAllText(path)) ?? [];
            foreach (var entry in loaded.Where(e => e is not null && e.Score > 0).OrderByDescending(e => e.Score).Take(MaxEntries))
                table._entries.Add(entry with { Name = entry.Name ?? "" });
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            table._entries.Clear();
        }
        return table;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
