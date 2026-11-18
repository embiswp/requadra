using Quadra.Desktop;

// quadra [--smoke [updates]] [--screenshot file.png] [--level n] [--pause]
//   --smoke plays by itself for a moment and exits (for checking a build);
//   --screenshot saves the last frame of a smoke run;
//   --level starts at a level (each level has its own background, colours and sounds);
//   --pause shows the pause sign in the last frame of a smoke run.
int smokeUpdates = 0;
int smoke = Array.IndexOf(args, "--smoke");
if (smoke >= 0)
    smokeUpdates = smoke + 1 < args.Length && int.TryParse(args[smoke + 1], out int n) ? n : 120;

int shot = Array.IndexOf(args, "--screenshot");
string? screenshot = shot >= 0 && shot + 1 < args.Length ? args[shot + 1] : null;

int level = 1;
int levelArg = Array.IndexOf(args, "--level");
if (levelArg >= 0 && levelArg + 1 < args.Length && int.TryParse(args[levelArg + 1], out int parsed) && parsed >= 1)
    level = parsed;

using var game = new QuadraGame(smokeUpdates, screenshot, level, args.Contains("--pause"));
game.Run();

if (smokeUpdates > 0)
{
    var player = game.CurrentGame.Player;
    Console.WriteLine($"smoke run finished: frame {game.CurrentGame.Frame}, stamps {player.Stamps}, score {player.Score}, phase {game.CurrentGame.Phase}");
}
