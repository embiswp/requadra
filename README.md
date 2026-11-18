# Quadra (.NET)

A work-in-progress port of the multiplayer falling-block game Quadra to C# and .NET. The original C++
game is kept untouched in [`legacy/`](legacy/).

## Layout

| Path | What |
|---|---|
| `src/Quadra.Core` | Game rules, input layer, demo reader and packet decoder. No graphics or platform code. |
| `src/Quadra.Desktop` | MonoGame (DesktopGL) client for Windows, macOS and Linux. |
| `src/Quadra.DemoInfo` | Command-line tool that replays the recorded demos and prints the results. |
| `tests/Quadra.Tests` | xUnit tests, including replays of the four demos in `legacy/demos`. |
| `legacy/` | The original C++ game, its art, sounds and the old Perl server. |
| `doc/` | Notes: the old tech stack, modernisation options and the spike findings. |

## Run

```
dotnet run --project src/Quadra.Desktop        # play
dotnet test                                    # run the tests
dotnet run --project src/Quadra.DemoInfo -- legacy/demos/demo00.rec
```

Controls: arrows move and soft-drop, Up rotates right, Z rotates left, X rotates twice, Space drops,
P pauses, Esc quits.

Needs the .NET SDK (the libraries target net8.0; the tests target net11.0).

## Status

The rules of the 1.1 game are ported and verified against the recorded demos: every piece, stamp, score,
line count, frag and death in the four demos is reproduced. Newer rules, networking, menus, art and sound
are not done yet. See [`doc/spike-notes.md`](doc/spike-notes.md) for what is verified and what is not.

The original game is licensed under the LGPL; see [`LICENSE`](LICENSE).
