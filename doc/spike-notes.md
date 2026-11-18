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
loop; wall kicks (see above); and the death check when a new piece appears on top of blocks.

## Suggested next steps

1. Decide which rule sets the new game must support. The bundled demos only cover the 1.1 rules
   (net version 20, survivor). Newer rules (net versions 23 and 24, blind and potato modes) have no
   recorded demos, so they would need new test data, for example recorded by the C++ client.
2. Decide the rules that need a design choice, not more porting: the spawn-collision death check,
   a drop-points formula, wall kicks, and whether the new game keeps the 1.1.0 key-repeat behaviour
   or the current client's blocked-move retry (both are supported).
3. Start the MonoGame client on top of `Quadra.Core`.
4. Android/iOS planning remains a separate task.
