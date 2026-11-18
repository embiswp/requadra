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
   lines). The demos therefore cannot yet be used to check score and lines, which is the real
   regression test for the game rules. This was not attempted in the spike.
4. **The simulation is not trivial to isolate.** For example, `Canvas::give_line` mixes
   scoring with logging, network sending, chat messages and on-screen text, so it must be
   split into pure rules before it can be ported.
5. **The packets give enough to drive a simulation.** Stamp packets carry the final x, y and
   rotation. The piece type is not in them and must come from the per-player random sequence
   seeded from `GameSeed` (`Random` in `legacy/source/random.cc`, a linear congruential generator
   that is simple to port). Moves packets carry one key-state byte per frame.

## Not verified

- Frag counts. The summary's `frags` need the attacker bookkeeping from the simulation.
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

Prototype results (summary score and lines vs. replay):

| Demo | Result |
|---|---|
| `demo02` (single player, no garbage) | **Exact**: score 54826 and 18 lines, all 49 stamps valid |
| all four demos, frags | **Exact for all 13 players** |
| `demo00` Jeps, `demo01` Dada | Exact or valid stamps with garbage-first ordering |
| other multiplayer players | Diverge at some point (about 2 of 13 players match fully) |

The single-player match shows the piece generator, piece table, gravity and scoring are correct.
All 49 stamps being physically valid is only possible if the sequence is reproduced exactly.
Multiplayer divergence is limited to garbage handling. Pending garbage on death was tried both
ways and was not the cause. Remaining suspects: timing of when a `Lines` packet takes effect
relative to the recorded frame, the exact bookkeeping in `Player_add_bonus` (side-bit edits near
the hole), and respawn handling. This is not yet resolved, so exact multiplayer replay is
unproven.

Cost estimate: the rules are about 400 lines of pure logic once separated from the UI, so the
port itself is moderate. The open risk is the multiplayer garbage details above.

## Ported rules (second step)

`src/Quadra.Core/Rules` now holds the parts of the prototype that matched:

| File | Ports | C++ source |
|---|---|---|
| `LegacyRandom.cs` | piece generator (net versions below 23) | `random.cc` |
| `Pieces.cs` | the 7 pieces in 4 rotations (generated from the C++ table) | `bloc.cc` |
| `Board.cs` | playfield, placement checks, row clearing, support/gravity pass | `canvas.cc`, `player.cc` (`check_nb_line`, `Player_check_link`) |
| `LegacyScoring.cs` | line-clear points, clean-board bonus | `canvas.cc` (`give_line`), `player.cc` (`check_clean`) |
| `PlayerSim.cs` | one player: piece queue, stamping, score, lines | `player.cc` |
| `Demos/DemoReplay.cs` | replays a demo through the above | `recording.cc` |

Test result: **`demo02` (single player) reproduces the recorded score (54826) and lines (18) exactly**,
with all 49 stamps valid. The replay marks each player as `ExpectedExact` only if no garbage
rows were sent to them. Multiplayer players who were hit by garbage are not expected to match yet.
It throws `NotSupportedException` for net versions from 23 and for games with level-up, because
those rules are not ported.

Not ported: garbage rows (`Lines` packets), the frag bookkeeping, level-up, net version 23+ rules.

## Suggested next steps

1. Resolve the multiplayer garbage divergence (see the feasibility section), then port garbage
   handling (`Canvas::add_packet`, `Player_add_bonus`) so the three multiplayer demos can be
   verified too.
2. Port the frag bookkeeping (`attacks`, `last_attacker`); the prototype already matched all
   frag counts.
3. Compare `score`, `lines` and `frags` with each demo's summary. If all four demos match, the
   rules port is verified.
4. Only then start the MonoGame client on top of `Quadra.Core`.
5. Android/iOS planning remains a separate task.
