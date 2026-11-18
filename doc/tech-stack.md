# Quadra – Tech Stack and Dependencies

Quadra is an old (originally late-1990s) multiplayer, networked Tetris-style
falling-block game. This repository is the "Quadra Source Release", version
**1.3.0** (see `source/version.h`, `configure.ac`). Upstream:
<https://github.com/quadra-game/quadra>.

> Repository layout note: the C++ game described here now lives in `legacy/`. All source paths below are relative to that folder. The .NET port is at the repository root.

## Overview

| Aspect | Details |
| --- | --- |
| Game type | Multiplayer falling-block puzzle (Tetris-like), with LAN and Internet play |
| Main language | C++ (~32,000 lines in `source/`, `.cc`/`.h`) |
| Auxiliary languages | Perl (game server and stats tools), shell/batch, Autotools m4 |
| Platforms | Linux/Unix, Mac OS X (app bundle), Windows (Visual C++) |
| Build systems | GNU Autotools (Unix/macOS), Visual Studio projects (Windows) |
| License | See `LICENSE` |

## Languages

- **C++** – game client (`source/*.cc`). The code is old-style C++ (custom
  containers/helpers, raw pointers, `new`-based UI zones) that has been
  modernised somewhat in 1.3.0 (standard typedefs, templates, Clang/GCC
  warnings fixed).
- **Perl** – the Qserv Internet game server (`server/qserv.pl`, using CGI) and
  statistics/demo tools (`stats/*.pl`, `stats/RecReader.pm`, `crypt.pl`).
- **Autoconf/Automake/m4** – build configuration (`configure.ac`,
  `Makefile.am`, `m4/ax_*.m4`).

## Runtime / build dependencies (client)

| Dependency | Purpose | Notes |
| --- | --- | --- |
| **SDL 2.0** | Window/video, input, audio output, timing, endian helpers, logging | Required. Found via `sdl2-config` (`AX_PATH_GENERIC`). Only core SDL2 is used (no SDL_mixer / SDL_image / SDL_net). Audio uses `SDL_OpenAudio`. |
| **Boost.System** | Support library for Boost.Filesystem | Required |
| **Boost.Filesystem** | File and path handling | Required (`boost/filesystem.hpp`) |
| **zlib** | Compression of the packed resource file (`quadra.res`) and demos | Required (`compress`) |
| **libpng** | Loading PNG images/sprites | Required (`png.h`, `png_create_read_struct`) |
| C++ standard library | `vector`, `list`, `set`, `string`, `algorithm`, `iostream`, `fstream` | |
| POSIX sockets | Networking (`sys/socket.h`, `netinet/*`, `arpa/inet.h`, `netdb.h`) | Unix/macOS |
| Winsock / Win32 API | Networking and system integration on Windows (`windows.h`, `shlobj.h`, `mmsystem.h`, ...) | Windows only |

Windows NuGet packages (`VisualC++/packages.config`): `libpng` 1.5.10.11,
`libpng.redist` 1.5.10.11, `zlib` 1.2.8.6, `zlib.redist` 1.2.8.6. The README
mentions the DirectX SDK (August 2007 or earlier) for the Windows build.

Ubuntu build packages (from the README): `make`, `g++`, `libsdl2-dev`,
`libpng12-dev`, `libboost-dev`, `libboost-filesystem-dev`.

## Build system and tooling

- **Autotools**: `autoreconf -i` → `./configure` → `make` → `make install`.
  Requires autoconf ≥ 2.68 and automake. Boost detection uses the
  `ax_boost_*.m4` macros bundled in `m4/`.
- **configure options**: `--disable-version-check` (disable new-version
  notification), `--enable-app-bundle` (macOS `.app`; default on Darwin).
- **Build targets** (`Makefile.am`):
  - `quadra` – the game executable
  - `wadder` – packs `resources.txt` assets into `quadra.res`
  - `dumpwad` – inspects/unpacks resource files
  - `test_version` – version-parsing test
- **Visual C++**: solutions/projects for VS 2003 and 2012 (`VisualC++/`);
  `res.bat` builds `quadra.res`.
- **Packaging**: RPM spec (`packages/quadra.spec.in`), NSIS Windows installer
  (`packages/quadra.nsi.in`), Windows readme template.
- Tested compilers: gcc (Linux), Visual C++ (Windows), Clang (macOS).

## Architecture notes

- **Rendering**: software-rendered, palette-based 8-bit graphics
  (`video.cc`, `palette.cc`, `bitmap.cc`, `canvas.cc`, `sprite.cc`) presented
  through SDL2. A "dumb" video driver (`video_dumb.cc`) exists for headless
  use (e.g. dedicated server).
- **Game logic**: `bloc.cc` (pieces), `game.cc`, `player.cc`, `score.cc`,
  `attack.h`, `random.cc`.
- **UI**: custom widget system (`zone*.cc`, `menu*.cc`, `listbox.cc`,
  `inter.cc`, `fonts.cc`, `chat_text.cc`).
- **Resources**: custom archive format `quadra.res` built by `wadder` from
  `resources.txt` (PNG images, `.fnt` fonts, WAV sounds in `sons/`, demo
  recordings `demos/*.rec`, localized text in `textes/` – English and French),
  compressed with zlib (`res_compress.cc`, `resfile.cc`, `resmanager.cc`).
- **Input / audio**: `input.cc` (SDL input), `sound.cc`/`sons.cc` (SDL audio).
- **Recording**: game demo recording/playback (`recording.cc`), plus a simple
  encryption/obfuscation helper (`crypt.cc`, `crypt.pl`).
- **Configuration/persistence**: `cfgfile.cc`, `highscores.cc`.
- **i18n**: `stringtable.cc`, `unicode.cc`, text files in `textes/`.

## Networking and multiplayer

- Custom game protocol written from scratch (`net.cc`, `net_server.cc`,
  `net_stuff.cc`, `net_buf.h`, `net_call.h`, `packet.cc`, `packets.cc`,
  `net_list.cc`).
- **TCP** (default port **3456**) and **UDP** (TCP port + 1) sockets; one
  executable acts as both client and server (`net_server.cc`).
- Modes: LAN play (UDP discovery/broadcast) and Internet play through the
  central **Qserv** game list server.
- **Qserv client** (`qserv.cc`) talks to the server using hand-rolled HTTP
  (`http_request.cc`, `http_post.cc`, `url.cc`) to post/delete games, fetch
  game lists, and post/get high scores and demos.
- **Auto-update check** (`update.cc`): fetches a small file over HTTP at most
  once a day to announce new versions and obtain the default Qserv address
  (see README for the privacy discussion).
- Game logging for network games: `nglog.cc`.

## Server-side and tooling (Perl)

| File | Role |
| --- | --- |
| `server/qserv.pl` | CGI script implementing Qserv: commands `postgame`, `deletegame`, `postdemo`, `gethighscores`, `getgames`; periodic cleanup of games and scores |
| `server/plugins/HTML.pm`, `Message.pm` | Qserv plug-in modules (HTML output, messages) |
| `stats/RecReader.pm`, `parserec.pl`, `listgames.pl`, `snapshot.pl` | Parse recorded demos / game data and generate statistics (HTML output in `stats/html`) |
| `stats/decoder_ring.txt` | Notes on the recording format |

Perl modules used: `CGI`, `CGI::Carp`, `Data::Dumper`, `Socket`,
`Compress::Zlib` (`Zlib`), plus the project-local `QServ::HTML`,
`QServ::Message`, and `RecReader`. The script runs in taint mode
(`#!/usr/bin/perl -Tw`) and originally hosted on SourceForge-style paths
(`/home/groups/q/qu/quadra/perl`).

## Repository layout

```
source/      C++ sources and headers (client, wadder, dumpwad, test_version)
images/      PNG backgrounds, menus, sprites, icons (.ico / .xpm)
sons/        WAV sound effects
fonts/       Bitmap fonts (.fnt)
demos/       Demo recordings (.rec)
textes/      Localised text (English / French) and help
server/      Qserv Perl CGI server + plugins
stats/       Perl statistics tools
m4/          Autoconf macros (Boost, SDL helpers)
packages/    RPM spec, NSIS installer, Windows readme templates
VisualC++/   Visual Studio solutions and projects
resources.txt  Manifest of files packed into quadra.res
```

## Observations / modernisation notes

- Recent history (SDL2 migration, `-fullscreen` option, pause-key handling in
  `Input_SDL`) shows the project moving from DirectX/SDL1/Svgalib to SDL2.
  CD music, SOCKS, setuid and `mcheck.h` support were removed in 1.3.0.
- The `README.md` lists `libpng12-dev`, which is obsolete on current Linux
  distributions (use `libpng-dev`).
- The Windows build depends on a very old DirectX SDK and old libpng/zlib
  NuGet packages; Visual Studio 2003/2012 project files are dated.
- The Qserv server is an unmaintained Perl CGI script, and the hard-coded
  hosting paths and URLs (Google Code / SourceForge era) are likely stale.
- Networking uses unencrypted custom TCP/UDP and HTTP (not HTTPS); worth
  considering if the game were to be revived for online play.
