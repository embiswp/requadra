using Quadra.Client;

// quadra [--smoke [updates]] [--screenshot file.png] [--level n] [--pause] [--fade]
//        [--screen main|setup|options|scores|play] [--focus item] [--lang 0|1] [--data-dir folder]
//        [--script "move 200 110;click;key Down;type Hero;wait 5"]
//   --smoke plays by itself for a moment and exits (for checking a build);
//   --screenshot saves the last frame of a smoke run;
//   --level starts a game at a level (each level has its own background, colours and sounds);
//   --pause shows the pause sign in the last frame of a smoke run;
//   --fade starts a level change early in a smoke run, to look at the fade;
//   --screen opens a screen first, --focus focuses an item on it, --lang picks the language;
//   --data-dir keeps settings and high scores in another folder;
//   --script feeds scripted mouse and keyboard input, one step per update, then exits.
var options = StartupOptions.Parse(args);
using var game = new QuadraGame(options);
game.Window.TextInput += (_, e) => game.TypeCharacter(e.Character);
game.Run();

if (options.SmokeUpdates > 0 || options.Script is not null)
    Console.WriteLine(game.SmokeSummary);
