using Quadra.Core.Input;
using Quadra.Core.Settings;

namespace Quadra.Tests;

public class GameSettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"quadra-test-{Guid.NewGuid():N}", "settings.json");

    [Fact]
    public void Defaults_follow_the_original_configuration()
    {
        var s = GameSettings.CreateDefault();
        Assert.Equal(3, s.Players.Length);
        Assert.Equal(["#1", "#2", "#3"], s.Players.Select(p => p.Name));
        var p = s.Players[0];
        Assert.False(p.Shadow);
        Assert.True(p.Smooth);
        Assert.Equal(2, p.HorizontalSpeed);
        Assert.Equal(2, p.VerticalSpeed);
        Assert.True(p.ContinuousDown);
        Assert.Equal("Left", p.Keys[GameAction.Left]);
        Assert.Equal("Right", p.Keys[GameAction.Right]);
        Assert.Equal("Up", p.Keys[GameAction.RotateLeft]);
        Assert.Equal("Up", p.Keys[GameAction.RotateRight]);
        Assert.Equal("Down", p.Keys[GameAction.Down]);
        Assert.Equal("RightShift", p.Keys[GameAction.RotateTwice]);
        Assert.Equal("Space", p.Keys[GameAction.Drop]);
        Assert.Equal(0, s.SetupPlayer);
        Assert.Equal(0, s.Language);
    }

    [Fact]
    public void Settings_survive_a_save_and_load()
    {
        string path = TempFile();
        try
        {
            var s = GameSettings.CreateDefault();
            s.SetupPlayer = 2;
            s.Language = 1;
            s.Players[2].Name = "Hero";
            s.Players[2].Shadow = true;
            s.Players[2].HorizontalSpeed = 3;
            s.Players[2].Keys[GameAction.Drop] = "Enter";
            SettingsStore.Save(path, s);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(2, loaded.SetupPlayer);
            Assert.Equal(1, loaded.Language);
            Assert.Equal("Hero", loaded.CurrentPlayer.Name);
            Assert.True(loaded.CurrentPlayer.Shadow);
            Assert.Equal(3, loaded.CurrentPlayer.HorizontalSpeed);
            Assert.Equal("Enter", loaded.CurrentPlayer.Keys[GameAction.Drop]);
            Assert.Equal("Left", loaded.CurrentPlayer.Keys[GameAction.Left]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void A_missing_or_damaged_file_gives_the_defaults()
    {
        Assert.Equal("#1", SettingsStore.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-quadra.json")).Players[0].Name);

        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{ this is not json");
            Assert.Equal("#1", SettingsStore.Load(path).Players[0].Name);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Odd_values_are_repaired()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """
            { "SetupPlayer": 9, "Language": -4,
              "Players": [ { "Name": "   ", "HorizontalSpeed": 40, "VerticalSpeed": -2, "Keys": { "Left": "A", "Drop": "" } } ] }
            """);
            var s = SettingsStore.Load(path);
            Assert.Equal(3, s.Players.Length);
            Assert.Equal(2, s.SetupPlayer);
            Assert.Equal(0, s.Language);
            Assert.Equal("#1", s.Players[0].Name);
            Assert.Equal(3, s.Players[0].HorizontalSpeed);
            Assert.Equal(0, s.Players[0].VerticalSpeed);
            Assert.Equal("A", s.Players[0].Keys[GameAction.Left]);
            Assert.Equal("Space", s.Players[0].Keys[GameAction.Drop]);       // blank falls back to the default
            Assert.Equal("#3", s.Players[2].Name);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Names_are_limited_to_the_original_length()
    {
        var s = GameSettings.CreateDefault();
        s.Players[0].Name = new string('x', 100);
        s.Normalize();
        Assert.Equal(39, s.Players[0].Name.Length);
    }
}

public class HighScoreTableTests
{
    private static HighScore Score(int points, string name = "A") => new(name, points, points / 100, 1, new DateTime(2026, 1, 1));

    [Fact]
    public void Scores_are_kept_best_first_and_the_list_holds_five()
    {
        var table = new HighScoreTable();
        Assert.Equal(0, table.Add(Score(500)));
        Assert.Equal(0, table.Add(Score(900)));
        Assert.Equal(2, table.Add(Score(100)));
        Assert.Equal(1, table.Add(Score(600)));
        Assert.Equal(4, table.Add(Score(50)));
        Assert.Equal([900, 600, 500, 100, 50], table.Entries.Select(e => e.Score));

        Assert.Equal(-1, table.Add(Score(40)));          // below the fifth
        Assert.Equal(3, table.Add(Score(200)));          // pushes the 50 out
        Assert.Equal([900, 600, 500, 200, 100], table.Entries.Select(e => e.Score));
    }

    [Fact]
    public void A_tie_goes_below_the_older_score()
    {
        var table = new HighScoreTable();
        table.Add(Score(500, "first"));
        Assert.Equal(1, table.Add(Score(500, "second")));
        Assert.Equal(["first", "second"], table.Entries.Select(e => e.Name));
    }

    [Fact]
    public void A_score_of_zero_never_qualifies()
    {
        var table = new HighScoreTable();
        Assert.False(table.Qualifies(0));
        Assert.Equal(-1, table.Add(Score(0)));
        Assert.True(table.Qualifies(1));
    }

    [Fact]
    public void A_full_list_only_takes_better_scores()
    {
        var table = new HighScoreTable();
        for (int i = 1; i <= 5; i++)
            table.Add(Score(i * 100));
        Assert.False(table.Qualifies(100));       // ties the worst: does not qualify
        Assert.True(table.Qualifies(101));
    }

    [Fact]
    public void The_list_survives_a_save_and_load()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"quadra-test-{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "scores.json");
        try
        {
            var table = new HighScoreTable();
            table.Add(Score(700, "Hero"));
            table.Add(Score(300, "Zed"));
            table.Save(path);

            var loaded = HighScoreTable.Load(path);
            Assert.Equal(table.Entries, loaded.Entries);
            Assert.Empty(HighScoreTable.Load(Path.Combine(dir, "missing.json")).Entries);

            File.WriteAllText(path, "garbage");
            Assert.Empty(HighScoreTable.Load(path).Entries);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
