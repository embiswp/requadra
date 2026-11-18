# Spike: `Quadra.Core` demo loader and packet decoder (C#)

Part of the [modernisation plan](modernisation-options.md). Mobile ports are
not part of this spike.

## What was built

The .NET code lives at the repository root (`src/`, `tests/`, `Quadra.slnx`); the C++ game is in `legacy/`:

| Project | Target | Purpose |
|---|---|---|
| `src/Quadra.Core` | net8.0 | Demo (`.rec`) reader, big-endian packet reader, typed packet decoder, packet-level analysis. No UI or platform dependencies. |
| `src/Quadra.DemoInfo` | net11.0 | Small CLI that prints a demo's rules, roster, per-player tallies and packet counts. |
| `tests/Quadra.Tests` | net11.0 | xUnit tests that run against the four bundled `legacy/demos/*.rec` files. |

Core library files:

- `Net/NetReader.cs` – port of `Net_buf` reads. Multi-byte values are big-endian; reading
  past the end returns zero and sets `Overrun`, exactly as the zero-filled C++ buffer behaves.
- `Net/Packets.cs`, `Net/PacketDecoder.cs` – port of the `Packet_*::read` functions in
  `legacy/source/packets.cc` for the packet types that occur in the demos (chat, game server,
  player, pause, stamp block, dead, respawn, first frag, gone, lines, moves, state,
  end game, server random). Other ids come back as `RawPacket`.
- `Demos/DemoFile.cs` – zlib decompression and hunk parsing (see the format table in
  [modernisation-options.md](modernisation-options.md)).
- `Demos/DemoAnalysis.cs` – roster, deaths, stamped blocks and respawns per player derived from
  the packet stream.

Build and run (the SDK was installed with `brew install dotnet`, giving .NET 11):

```
dotnet test
dotnet run --project src/Quadra.DemoInfo -- legacy/demos/demo00.rec
```

## Result

`dotnet test`: **36 tests passed**, none skipped.

The tests check, for all four demos:

- the zlib stream decompresses to exactly the size in the header;
- every packet in the file decodes to a typed packet with no unconsumed bytes;
- packet frames never go backwards, and the last frame is not after the recorded duration;
- the rules in the game packet (name, version, level start, min combo, end type and value,
  level-up) equal the summary stored in the same file;
- the roster from the player packets (names, teams) equals the summary, and the number of
  `Dead` packets per player equals the summary's `deaths`.

Example output (`demo00.rec`): 1637 packets, 4 players, deaths 1/3/2/1 and
510 stamped blocks. Those deaths match the stored summary.

## Findings

1. **The demos are all in net protocol version 20 (Quadra 1.1).** Several packets were
   shorter then than in the current layout: `Player`, `StampBlock`, `Lines`, `Gone`,
   `EndGame`, and the `GameServer` packet itself. The C++ client reads zeros for the missing
   trailing fields, and the C# decoder does the same. A port must keep this behaviour or the
   old demos stop working. The tests state which packet types may be short.
2. **Packet-level replay works.** The data the server already decided (roster, deaths, game
   rules) can be reconstructed and verified against the summary.
3. **Scores and lines are not in the packets.** The summary's per-player `score`, `lines`,
   and `frags` come from the client-side simulation. In the C++ code this is the `Canvas` and
   its executors (`give_line`, stamping, combo and clean-bonus scoring, level-up, garbage
   lines). The packet-level pass alone cannot check score and lines, which is the real
   regression test for the game rules. The later steps below add that rule replay, and it
   now matches all four demos.
4. **The simulation is not trivial to isolate.** For example, `Canvas::give_line` mixes
   scoring with logging, network sending, chat messages and on-screen text, so it must be
   split into pure rules before it can be ported.
5. **The packets give enough to drive a simulation.** Stamp packets carry the final x, y and
   rotation. The piece type is not in them and must come from the per-player random sequence
   seeded from `GameSeed` (`Random` in `legacy/source/random.cc`, a linear congruential generator
   that is simple to port). Moves packets carry one key-state byte per frame.

## Not verified

- The semantics of the `Moves` key bits and the `State` values are not decoded yet.
- Packet types that do not appear in the demos (for example `Download`, `Stat`) are not
  ported.
- Only .NET 11 is installed on this machine. `Quadra.Core` targets net8.0 and compiles
  against it, but it has only been run through the net11.0 test project.

## Feasibility of a full replay (read of `legacy/source` plus a throwaway Python prototype)

Question: can the demos verify the game rules (score, lines, frags) and not only the packets?
Answer: **yes, feasible**. A throwaway Python replay (not in the repository) was checked against the
stored summaries of the four demos.

How the C++ code produces the numbers (all net version 20 rules):

- **Piece sequence.** Each player has its own generator seeded with the game seed from the game
  packet (`Random::crap_rnd` in `random.cc`: `tmp = seed*0x41c64e6d + 0x3039` in 32-bit
  arithmetic, `seed = tmp >> 10`, result `tmp & 0xFFFF`, piece `% 7`). Four pieces are drawn when
  a player starts or respawns, then one per stamp. No re-seed packets occur in the demos.
- **Stamp packets** give the final position and rotation, and the per-drop score. The piece
  type comes from the sequence above.
- **Line clears** are not a simple row removal. `Player_check_line` clears full rows, then
  `Player_check_link` drops every cell group that is no longer supported (connectivity comes from
  the side bits of each cell). Repeating this is what the `complexity` (combo) counter measures.
- **Scoring** is in `Canvas::give_line` (base points per line count, plus
  `1000 * (combo - 1)`, plus 10 % per level) and `check_clean` (5000 for an empty board).
- **Garbage lines** come from `Lines` packets (`Canvas::add_packet`) and are applied at the next
  stamp by `Player_add_bonus`. The executor is a stack, so garbage is added *before* the line check.
- **Frags** come from the `attacks`/`last_attacker` bookkeeping on `Lines` packets and are counted
  on `Dead` packets.

Prototype results (summary score, lines and frags vs. replay):

| Stage | Result |
|---|---|
| `demo02` (single player, no garbage) | **Exact** from the first run: score 54826, 18 lines, all 49 stamps valid |
| first multiplayer attempt | frags exact for all 13 players; scores and lines diverged for most |
| after the two fixes below | **Exact for all 13 players in all four demos** (score, lines, frags, zero invalid stamps) |

The two fixes that resolved the multiplayer divergence:

1. **Three modules, not two, run after a stamp that has garbage pending.** `Player_stamp::init`
   calls line check, garbage, line check. The executor is a stack, so they run in the order
   *line check, garbage, line check* (the second check can clear rows the garbage completed).
2. **Garbage that arrives while a player is dead is applied to the dead board**, which the respawn
   then wipes. It must not carry over onto the fresh board. This was found by tracing the first
   invalid stamp of one player: garbage received between its death and respawn was being applied
   to its new board.

## Ported rules (second step)

`src/Quadra.Core/Rules` now holds the parts of the prototype that matched:

| File | Ports | C++ source |
|---|---|---|
| `LegacyRandom.cs` | piece generator (net versions below 23) | `random.cc` |
| `Pieces.cs` | the 7 pieces in 4 rotations (generated from the C++ table) | `bloc.cc` |
| `Board.cs` | playfield, placement checks, row clearing, support/gravity pass, garbage rows | `canvas.cc`, `player.cc` (`check_nb_line`, `Player_check_link`, `Player_add_bonus`) |
| `LegacyScoring.cs` | line-clear points, clean-board bonus | `canvas.cc` (`give_line`), `player.cc` (`check_clean`) |
| `FallingPiece.cs` | one piece falling frame by frame from `Moves` bytes, speeds | `player.cc` (`Player_process_key::step`, `move_down`, `drop_down`, rotate and move functions), `canvas.cc` (`calc_speed`) |
| `Input/KeyStates.cs`, `KeyBindings.cs`, `PieceController.cs` | keys to piece actions per frame, key repeat | `player.cc` (`Player_process_key::keyboard_control`), `canvas.cc` (`check_key`, `clear_key`, `unrelease_key`), `input.cc` |
| `Game/SinglePlayerGame.cs` | the loop around a player: play the piece, stamp it, pause, next piece or game over | `player.cc` executors |
| `PlayerSim.cs` | one player: piece queue, stamping, garbage queue, attacker bookkeeping, score, lines | `player.cc`, `canvas.cc` (`add_packet`) |
| `Demos/DemoReplay.cs` | replays a demo through the above | `recording.cc` |

Test result: **all four demos replay exactly in C#.** For every player the replayed score,
lines, frags and deaths equal the stored summary and no stamp is invalid (13 players, about
1500 stamps). `DemoReplay` throws `NotSupportedException` for rule sets that are not ported, instead of
replaying them wrongly: net versions from 23, games with level-up, and games without survivor rules.

Not ported: level-up, non-survivor frag rules, net version 23+ rules (new complexity and clean
bonus, blind attacks), hot potato, handicap effects on sent lines. None of these are exercised by
the bundled demos, so they cannot be verified here.

## Falling pieces from `Moves` packets (third step)

The `Moves` packets hold one byte per game frame in which a piece was falling. Bits: 1 down, 2 left,
4 right, 8 rotate left, 16 rotate right, 32 rotate twice, 64 hard drop. `FallingPiece` applies them
in the game's fixed order and then applies gravity; the piece lands when the next gravity step would
collide. `DemoReplay.Run(demo, ReplayInput.Moves)` feeds these bytes into `PlayerSim.Move` and compares
each landing with the following stamp packet.

Result: **all 2409 stamps in the four demos land exactly where the stamp packet says, in position,
rotation and on the exact frame** (the last move byte of a piece is the landing frame; no bytes follow
a landing). Score, lines, frags and deaths still match the summaries.

Things the demos taught us:

- **No wall kick in the recording clients.** The current C++ client tries to shift a piece sideways
  when a rotation is blocked by a wall. A prototype with that kick broke one recorded stamp
  (`demo01`, player Rem, stamp 21), without it all 2409 match. The `Moves` bytes do not record the
  sideways shift, so kicks cannot be replayed from them anyway. `FallingPiece` has no kick. If the new
  game wants wall kicks, that is a rules option, not something the demos can verify.
- **Drop points come from the old client.** In the current C++ code the points for a placed piece are
  `max(0, 100 - max(frames - 50, 0)) >> 1`. With a small constant frame offset this matches 87% of the
  recorded stamp scores; the rest are larger than the formula allows (up to 54 instead of 50), so the
  1.1.0 clients used a different formula. The replay therefore keeps taking the drop points from the
  stamp packet, and the formula is not ported.
- **Stamp timing relative to other packets.** A stamp takes effect when the stamp packet arrives, not
  when the last `Moves` byte is played, because garbage rows from other players can arrive in between
  and the server order decides which stamp they belong to.

Not covered: the key-to-action layer (key repeat, held keys), the death check when a new piece
collides at spawn (the replay follows the `Dead` packets), and speed changes by level (levels do not
change in these games).

## Input layer (fourth step)

`Quadra.Core/Input` turns key states into the per-frame piece actions, so a keyboard, gamepad or touch
front end only has to report "key N went down / up" (keys are plain integers) and call
`PlayerSim.Frame()` once per frame in which the piece is in play. It returns the action byte (what a
client would record or send as a `Moves` packet) and the landing, if any.

Behaviour, ported from `keyboard_control`:

- Key state is `Pressed` on key-down and `Released` on key-up; the game clears what it has handled.
  A tap that ends before the next frame is seen as `Released` only.
- **Rotations happen when the key is released**, not pressed. If two rotations share a key (the
  default setup uses Up for both) the first one in order wins and clears the key.
- **Down** acts every frame while held, or once for a tap. **Drop** acts once on key-down.
- **Left / Right** act at once, repeat after `delay + 10` frames, then every `delay` frames
  (`delay` = 11, 6, 3 or 1 for repeat setting 0 to 3). Pressing both cancels both. The repeat starts
  over for every new piece.
- A stale drop key is discarded when a new piece appears; a held down key is too, unless "continuous
  down" is on.

How it was checked. The demos contain the resulting `Moves` bytes, not the keys. The test oracle
(`tests/Quadra.Tests/KeyScheduleOracle.cs`) works backwards: for each recorded byte it invents the
key presses that should produce it, drives the real controller, and compares every frame and every
landing. Taps always work, so the oracle holds a direction key whenever the controller's own repeat
predicts the next recorded attempt; the share of attempts explained that way is what tests the repeat
constants.

| Measure (all four demos) | Result |
|---|---|
| pieces reproduced frame by frame | **2409 of 2409** (183,265 frames) |
| direction moves produced by key repeat itself | 9814 of 13632 (72 %) |
| score, lines, frags | still equal the summaries |
| gap between repeats (delay 3) | 4814 at 3 frames, 1055 at 13 (`delay + 10`); 28 at 12 and 47 at 14 |
| gap between repeats (delay 1) | 3879 at 1 frame, 236 at 11; 5 at 12 and 8 at 10 |
| mutation: `delay + 9` instead of `+ 10` | caught by 7 tests, including 3 demos |

What the demos taught us:

- **The 1.1.0 clients did not retry a blocked move.** The current C++ code retries a blocked left or
  right move on the next frame. With that rule, 2 of 2409 pieces could not be reproduced (a first move
  against a wall followed by the next repeat exactly 11 frames later). Without it, all 2409 are. The
  rule is a `retryBlockedMoves` option, default on (current client), off for the 1.1.0 demos. The gap
  histogram agrees: a player holding a key against a wall with delay 3 would show constant 1-frame gaps
  otherwise, and there are only 64 of those.
- **A round can end while a piece is falling.** Its `Moves` bytes are never followed by a stamp and the
  client discards the piece on respawn. Replays must discard them too (the oracle does).

Not covered: the throttle in the C++ client that skips key handling when the game runs ahead of the
display (it depends on video frame counters, and a fixed-timestep game does not need it); frames outside
the piece's life (the pause before it appears, the line-clear animation), which belong to the game
loop (see the next section); and wall kicks (see above).

## Decisions taken

| Question | Decision |
|---|---|
| Rule sets | **1.1 rules only** (net version 20). Newer rules have no recordings to verify them against. |
| Wall kick | **None**, as in the 1.1.0 games in the demos. |
| Key repeat against a wall | **1.1.0 behaviour** (steady rhythm). The current client's blocked-move retry stays available as an option. |

## Death check, level-up and a playable client (fifth step)

- **Death check** (`PlayerSim.SpawnBlocked`): a player dies when the piece that just appeared overlaps
  blocks (`Player_normal::step`). Verified: all 33 recorded deaths in the four demos coincide with such a
  spawn collision, and there is no spawn collision without a recorded death
  (`DemoReplay` now reports `DeathMismatches`, zero for every player).
- **Level-up** (`PlayerSim` with `levelUp: true`): the level rises when the lines cleared since the last
  (re)start reach 15 times the level, and gravity follows the level. This is a port of
  `Canvas::give_line`, but no bundled demo has level-up, so it is covered by unit tests built from the C++
  rules only, not by recordings.
- **Drop points** use the current C++ formula (`LegacyScoring.DropPoints`). It matches 87 % of the
  recorded stamp scores; the 1.1.0 clients used a slightly different, unknown formula.
- **`SinglePlayerGame`** wraps a `PlayerSim`: it steps the piece each frame, stamps it on landing, waits
  2 frames (plus a fixed 20 while lines clear), then brings in the next piece or ends the game. The
  original steps its logic every 10 ms, so a front end calls `Update()` 100 times a second. The line-clear
  animation timing is an approximation (the original flashes about 17 frames and then lets cells fall one
  row every other frame); it is not verified.
- **`src/Quadra.Desktop`** is a playable MonoGame (DesktopGL) client: fixed 100 Hz logic step, keyboard
  to key-state events, pause and game over. It first drew with plain rectangles; the original art and sound
  came next (see the sixth step). It targets net8.0 with roll-forward, so it also runs on a newer installed
  .NET.

Checked here: the game logic by unit tests (including a bot that plays to game over), and the client by
`dotnet run --project src/Quadra.Desktop -- --smoke 150`, which opens the window, plays by itself and
exits; `--screenshot file.png` saves the last frame, and the output looked right. Nobody has played it
by hand yet, so feel (key timing, speed, animation) is unchecked.

Controls: Left / Right arrows, Down arrow (soft drop), Up arrow (rotate right), Z (rotate left), X (rotate
twice), Space (drop), P (pause), Enter (new game after game over), Esc (quit).

## Original art and sound (sixth step)

The client now uses the original files from `legacy/`, linked into the build output (nothing is copied
into the repository): ten level backgrounds, the level-up and pause signs, the two bitmap fonts and the
sound effects.

How the original draws, and how that was ported (all in `Quadra.Core`, so it is testable without a window):

- **Blocks are not sprites.** The original paints every 18 x 18 cell with shade lines (a bevel) on the sides
  that are not joined to a neighbouring cell, plus inner corners where two neighbours meet
  (`raw_draw_bloc`, `raw_draw_bloc_corner`, `raw_small_draw_bloc`). `BlockPainter` is a port of that
  drawing, pixel for pixel.
- **The colours live in the background's palette.** Each of the 9 block colours has 8 shades at palette
  entries 184 and up, and every level's background has its own ramps, so the blocks change look with the
  level. `PngPalette` reads the palette chunk of the PNG and `BlockColors` builds the ramps (including the
  original's swap of colours 7 and 8, which makes the landing shadow use the entries after the pieces).
- **Layout.** The single-player screen uses the middle of the three frames in the background: board at
  pixel (223, 37), score, lines and level at y = 403, 422 and 460, the next pieces in the strip above the
  board (one full size, two small). `SinglePlayerView` paints all of it into a 640 x 480 overlay; the client
  draws the background, the overlay and the pause sign into a 640 x 480 target and scales that to the
  window, keeping the proportions.
- **Fonts.** The `.fnt` files hold up to 109 glyphs of 8-bit pixels where 0 is transparent and 1 to 7 is a
  brightness (dark outline to bright fill). `LegacyFont` parses them and draws them in any colour.
- **Landing shadow and smooth movement.** The piece is drawn at its pixel position (1/16 pixel units, so it
  falls smoothly) and slides sideways towards its column at the key-repeat speed; the shadow uses the
  special colour 8. `FallingPiece` gained the display position and `RestingRow`.
- **Line clears look like the original.** The sim now records what a clear looked like (rows removed, board
  after removal, board after each fall step). `SinglePlayerGame` turns that into a frame-accurate timeline:
  16 frames of flashing rows (white, white, red, red, ...), 2 frames, then 2 frames per row that loose cells
  fall, 2 more and one line-check frame. The level-up banner rises for 220 frames.
- **Sounds.** Each level has one of ten sets of five samples; the original varies volume, pan and pitch at
  random on every play, and the pitch drops with every step of a combo. `SoundCues` reproduces those
  formulas (volume -4096..0 and pan -4096..4096 become 0..1 and -1..1, a playback rate in Hz becomes a
  pitch in octaves relative to the sample's own rate, read from the WAV header). Quirk kept: samples
  recorded at 22050 Hz are played at about 11000 Hz, half speed, as in the original. Events (rotate,
  stamp, line flash, cells fell, level up, game over, pause) come from `SinglePlayerGame.Events`.

Checked here: unit tests on the real files (palette entries, bevel pixels, corner pixels, clipping, glyph
brightness, WAV rates, that every sound file exists, the sound set per level, the cue formulas, and a game
driven until a row flashes, with its timeline length and the flash colours in the painted overlay), and
smoke runs with screenshots (level 1 and level 6, pause sign). Nobody has listened to it or played it by
hand, so the sound balance and animation feel are unchecked.

Differences from the original, on purpose or for now:

- Panning follows the piece's side (right piece, right speaker). In the original code the sign looks
  inverted; this is a guess about what was intended.
- Master volume is 0.6; the original mixes quietly (samples are scaled to a quarter of full scale).
- Death uses the flash sound once instead of the original's animated board colouring.
- No music; the original has none either, only effects.

Side frames and level fade (seventh step):

- **Number fields.** The score, lines and level (and all counts) are drawn as the original's framed fields
  (`Zone_text_field`): a one pixel border in palette entry 210, a near-black inside (entry 0), the number
  right-aligned in the number font. The labels now come from the original text table ("Score:", "Lines:",
  "Level:") through `LegacyText`, which finds strings by the original numbers (`ST_BASE`); French is linked
  too and ready for a language setting.
- **Left frame** (`Pane_blockinfo`): the seven pieces with how many of each have been placed, and a total.
  **Right frame** (`Pane_comboinfo`): Single, Double, Triple, Quad, 5-lines ... 14-lines and More!, with how
  many moves cleared that many lines at once, and the total lines. `PlayerSim` now keeps `PieceCounts` and
  `ClearCounts` for this. Deliberately not copied: the original counts a 15-line move twice.
- **Level fade** (`Player_level_up`, `Player_change_level`): when the level rises, the whole screen fades to
  black over 16 frames while play goes on; at black the background, block colours and sounds switch; then the
  game freezes for 30 frames (16 of fade-in and a 14 frame wait) while the new level fades in. The banner
  keeps rising throughout. The original fades the palette; here the finished picture is darkened, which
  looks the same. `SinglePlayerGame.Brightness` and `IsFrozen` expose it; the client draws a black layer.
- Checked: tests (text numbers in both languages, the counts, a four-line clear counted as a Quad, the
  painted frames and boxes, the exact fade and freeze timeline) and screenshots of a normal frame and of a
  frame in the middle of a fade (`--fade` runs one in a smoke test).

Menus and settings (eighth step):

- **Screens.** The program now starts on the original main menu (`debuto.png`) and leads to Single-player
  game, Highscores, Player setup, Options and Quit. Multi-player, Demo central and the old "Register" item
  show a short "not available yet" notice. Every screen uses the original pictures at the original
  coordinates; hover pictures are drawn over the baked-in text. English and French both work (their own
  pictures, positions and strings; accented letters and the arrow symbol in "Back" use the font's own glyphs).
- **Structure.** `Quadra.Core/Menus` holds all of the logic and none of the drawing: `MenuScreen` (items with
  hit areas, pointer and keyboard focus), `MenuLayouts` (the original coordinates, from `menu.cc`),
  `SetupModel` (cycling values, typing, key capture), and `AppFlow` (which screen is shown and what each
  choice does, including the game-over to high-scores step). `MenuPainter` paints the text and panels
  (framed value boxes in the picture's own palette colours, as `Zone_state_text` does). The window only
  forwards mouse, keyboard and text input and draws the result.
- **Player setup.** Three profiles, each with name, password (kept for the later Internet game), shadow,
  smooth movement, horizontal and vertical speed, continuous down and the seven keys. Clicking a key waits
  for a key press (Escape cancels, Enter is refused as in the original); "Set all keys" asks for them in
  the original order with the original prompts. The settings really take effect in the game: bindings,
  repeat speeds, the landing shadow, smooth movement and continuous down.
- **High scores.** The best five single-player games (the original's `MAX_SCORE`). A death goes on to the
  table after a moment (Enter skips it), as in the original; the new entry is highlighted and the level-up
  sound plays. The original keeps each score as a recording that can be played back; this keeps the figures.
- **Storage.** `settings.json` and `highscores.json` in the user's application data folder (on macOS
  `~/Library/Application Support/Quadra`); a missing or damaged file gives the defaults, values are
  repaired when read, and files are written through a temporary file. `--data-dir` uses another folder.
- **Menu sounds.** Hover, click and value-change use the original samples and parameters.
- Checked: tests for the screen model, the layouts (hit areas from the real picture sizes, no value panel
  covering another label), setup typing and key capture, the whole flow, settings and high score files,
  French fonts and strings, and the painter's pixels; screenshots of every screen in both languages; and
  **scripted runs of the real window** (`--script`): clicking through to Player setup, toggling Shadow,
  typing a name, rebinding Drop and leaving gave exactly the saved settings; starting a game from the menu
  used the rebound key and a finished game appeared in the saved high score table.
- Found while testing: the French pictures must be copied opaquely over the background (treating them as
  see-through left ghosts of the English text), and the left key panels were wide enough to cover the
  start of the "Rotate counterclockwise" label.

Not done from the original's menus: the intro, the "Single-player game type" choice (normal or 5-minute
sprint), the options for sound mode, mouse speed, Internet port, game server and proxy (they belong to the
network game), the demo and help screens, and playing back a high score. Nobody has used the menus by hand
yet: hover feel, sounds and the key capture are checked only through the scripted runs.

Open question: the repository's licence (LGPL) covers the whole source release including these files, but
there is no separate statement for the art and sound samples, and the game's copyright notice names Ludus
Design. Worth confirming with the upstream project before distributing builds that include them.

## Suggested next steps

1. Play the client and tune the feel: sound balance, speed per level, key timing, menu use.
2. The network game: the packet decoder, the rules and the key layer are already in `Quadra.Core`; the
   menus have the profiles (name, password) and a place for the options (port, server). The Perl game
   list server (Qserv) would be rebuilt in ASP.NET Core.
3. Smaller gaps: the 5-minute sprint game, high score playback, the demo screen.
4. Android and iOS planning remains a separate task. Touch controls map naturally onto the key-state layer
   (a button is a key id) and the menu model (items with hit areas).
