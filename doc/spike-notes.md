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

## Suggested next steps

1. Port `Random` and the block shape table (`Bloc::bloc` in `legacy/source/bloc.cc`) to
   `Quadra.Core` and add a board model (occupied cells, collision, line detection).
2. Extract the scoring rules from `Canvas::give_line` into a pure class and add the
   garbage-line handling (`Canvas::add_packet`).
3. Replay `Moves` and `StampBlock` packets through that model and compare `score`, `lines`
   and `frags` with each demo's summary. If they match for all four demos, the rules port is
   verified.
4. Only then start the MonoGame client on top of `Quadra.Core`.
5. Android/iOS planning remains a separate task.
