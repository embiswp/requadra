# Quadra (.NET)

[![CI](https://github.com/embiswp/requadra/actions/workflows/ci.yml/badge.svg)](https://github.com/embiswp/requadra/actions/workflows/ci.yml)

A work-in-progress port of the multiplayer falling-block game Quadra to C# and .NET. The original C++
game is kept untouched in [`legacy/`](legacy/).

## Layout

| Path | What |
|---|---|
| `src/Quadra.Core` | Game rules, input layer, demos, the network protocol, game host and client, game list client. No graphics or platform code. |
| `src/Quadra.Desktop` | MonoGame (DesktopGL) client for Windows, macOS and Linux. Uses the original art, fonts and sounds from `legacy/`. |
| `src/Quadra.Server` | The game list server (ASP.NET Core), replacing the old Perl `qserv.pl`. |
| `src/Quadra.Host` | A game host without a player: takes connections and relays moves; can announce itself to a game list server. |
| `src/Quadra.DemoInfo` | Command-line tool that replays the recorded demos and prints the results. |
| `tests/Quadra.Tests` | xUnit tests, including replays of the four demos in `legacy/demos`. |
| `legacy/` | The original C++ game, its art, sounds and the old Perl server. |
| `doc/` | Notes: the old tech stack, modernisation options and the spike findings. |
| `.devcontainer/` | VS Code dev container: .NET SDKs, a virtual desktop for the game window, GitHub CLI and the Claude CLI. See its [README](.devcontainer/README.md). |

## Run

```
dotnet run --project src/Quadra.Desktop        # play
dotnet test                                    # run the tests
dotnet run --project src/Quadra.DemoInfo -- legacy/demos/demo00.rec
dotnet run --project src/Quadra.Server -- --urls http://localhost:5000   # game list server
dotnet run --project src/Quadra.Host -- --name "My game" --list http://localhost:5000/cgi-bin/qserv.pl
```

The program starts on the original menu: Single-player game, Highscores, Player setup (name, keys, speeds,
shadow, smooth, continuous down, three profiles) and Options (English or French). Settings and high scores are
saved in your application data folder (on macOS `~/Library/Application Support/Quadra`); `--data-dir` uses
another folder. F11 toggles full screen.

Try other levels with `dotnet run --project src/Quadra.Desktop -- --level 7`: each of the ten levels has its
own background, block colours and sound set.

Default controls (change them in Player setup): arrows move and soft-drop, Up rotates, Right Shift rotates
twice, Space drops. In the game P pauses and Esc returns to the menu; in the menus use the mouse, or the
arrow keys with Enter and Esc.

Needs the .NET SDK (the libraries target net8.0; the tests target net11.0).

## Status

The rules of the 1.1 game are ported and verified against the recorded demos: every piece, stamp, score,
line count, frag and death in the four demos is reproduced, and so is every garbage attack. The game list
server, the network protocol, a game host and a client library are built and tested (also over sockets). The
desktop client plays a network game: the Multi-player item opens a lobby (game list from a list server, host,
join by address); `--host [port]`, `--join host[:port]` and `--name` start one from the command line. Nothing has
been tried against the original C++ client. A host sets survivor rounds, an end condition (frags, time,
points, lines) and the minimum combo on the multi-player screen, and the lobby can find games on the
local network. `Quadra.Host --record game.rec` records a hosted game as a demo.
Newer rules (hot potato, blind attacks) are not planned. See [`doc/spike-notes.md`](doc/spike-notes.md) for what is verified and what is not.

The original game is licensed under the LGPL; see [`LICENSE`](LICENSE). The art and sounds come from the
original and carry its copyright (Ludus Design); see the note in [`doc/spike-notes.md`](doc/spike-notes.md).
