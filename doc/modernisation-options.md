# Quadra – Modernisation Options (C# / .NET)

Goal: make Quadra platform independent (Windows, macOS, Linux first;
Android and iOS later) and preferably based on .NET and C#.

See [tech-stack.md](tech-stack.md) for an analysis of the current C++ code base,
which now lives in `legacy/`.

> Scope note: Android and iOS ports are **deliberately out of scope for now**.
> They need separate planning (touch UI, store requirements, mobile network
> behaviour). The architecture below keeps them possible.

## Options considered

| Setup | Windows / Mac / Linux | Android / iOS | Fit for Quadra |
|---|---|---|---|
| **MonoGame** (.NET 8+) | Yes (DesktopGL) | Yes, both | **Best fit.** A 2D framebuffer game maps well to it. |
| **Godot 4 with C#** | Yes | Android works; iOS C# support has been experimental | Good if an editor, scenes and a built-in UI system are wanted. |
| **Unity** | Yes | Yes, both; most mature | Most reliable for mobile/store publishing. Its C# runtime is not modern .NET and it is heavy for a small 2D game. |
| **Silk.NET or SDL2-CS/SDL3-CS** | Yes | Possible but awkward | Closest to a line-by-line port of the current SDL code. Mobile packaging is DIY. |
| **FNA** (XNA reimplementation) | Yes | Mostly community efforts | Good on desktop, weak on mobile. |
| **Avalonia / .NET MAUI + SkiaSharp** | Yes (MAUI has no Linux) | Yes | Fine for menus, awkward for a real-time game loop. |

Platform claims are from general knowledge and must be re-checked against
current releases before committing.

A non-C# alternative for mobile: SDL2 already supports Android and iOS, so a
CMake build of the existing C++ could reach mobile sooner. This is only
relevant if the C# preference is relaxed.

## Recommendation: MonoGame

Quadra is a small 2D game with 8-bit palette-based software rendering. The
existing blitting code can be ported to a `Color[]` buffer that is uploaded as
one texture per frame. MonoGame provides input, audio and touch, and builds
for all target platforms.

## Proposed solution layout

```
Quadra.Core     net8.0 class library: pieces, rules, scoring, RNG, recordings
                (no graphics or platform dependencies)
Quadra.Net      protocol and sockets (System.Net.Sockets, or LiteNetLib)
Quadra.Client   shared MonoGame code: rendering, UI, audio, menus
Quadra.Desktop  DesktopGL head (Windows / macOS / Linux)
Quadra.Server   ASP.NET Core minimal API replacing server/qserv.pl
Quadra.Tests    xUnit
(later)  Quadra.Android, Quadra.iOS
```

## Porting approach

1. **Core first, with the demos as tests.** `legacy/demos/*.rec` are recorded
   multiplayer games (see below). Replaying them in C# and comparing the
   result to the summary stored inside each file gives strong regression tests.
2. **Replace the resource packer.** Instead of porting `wadder` and
   `quadra.res`, load PNG/WAV/font files directly or from a plain zip.
   `System.IO.Compression` replaces zlib.
3. **Port the protocol exactly at first**, so C# and C++ clients can play each
   other during the port. Redesign later.
4. **Replace the UI system last.** The custom widget system (`zone*.cc`) is not
   worth porting literally.
5. **Replace Qserv** with a small ASP.NET Core service over HTTPS.

## Demo (`.rec`) file format

Determined by reading `legacy/source/recording.cc` and verified by decoding the
four bundled demos.

- Bytes 0–3: uncompressed size, little-endian `uint32`.
- Bytes 4–end: a zlib stream (the `78 9c` header is visible in the files).
- The decompressed data is a sequence of *hunks*, each starting with a type
  byte:

| Hunk | Meaning | Payload |
|---|---|---|
| 0 | seed + repeat speeds (old single-player format) | `int32` seed, 3 × `int32` |
| 1 | single-player number | `uint8` |
| 2 | key data (old single-player format) | bytes until `0xFF` |
| 3 | final info | 40-byte name, `int32` score, lines, level |
| 11 | network packet | `uint32` frame (LE), `uint16` size (LE), packet bytes |
| 13 | summary | `uint32` size, text lines (`key value`) |

The bundled demos contain hunk 11 packets (the "new" format). The first
packet is `P_GAMESERVER` (id 2), which carries the game rules and random
seed. Later packets (stamp block, lines, etc.) are applied at their recorded
frame. Inside packets, multi-byte values are **big-endian** (`Net_buf`),
unlike the hunk headers, which are little-endian.

## Spike

See `src/` and `tests/` for the first spike (`Quadra.Core` + tests). Its status and
findings are documented in [spike-notes.md](spike-notes.md).

## Open questions

- Is the game simulation fully deterministic from the recorded packets? The
  stored summary in each demo is used to check this.
- How much of the simulation (`Canvas`, `Game`, `Overmind`) depends on timing
  or UI state and must be separated before it can be ported?
- Should LAN discovery be kept? The UDP broadcast path looks only partly
  implemented.
- Mobile: input scheme, aspect ratios, store distribution (separate plan).
