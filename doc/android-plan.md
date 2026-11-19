# Plan: Quadra on an Android phone

This plan is based on how the pieces are structured, not on a fresh read of the desktop project code.
Phase 1 starts by checking the assumptions below.

## What carries over unchanged

`Quadra.Core` (rules, protocol, host, client, LAN discovery) has no graphics or platform code and targets
net8.0, so it runs on Android as it is. Its tests keep guarding the rules. All the work is in the client
shell around it.

## Phase 1: Split the client so two heads can share it

- Move the game code out of `Quadra.Desktop` (`QuadraGame`, `AppFlow`, `MenuLayouts`, `MenuPainter`, the
  sound and asset loading) into a shared project that holds no desktop-only code.
- Keep two thin heads: `Quadra.Desktop` (DesktopGL, as now) and a new `Quadra.Android`
  (`net8.0-android`, MonoGame.Framework.Android 3.8.x).
- Share the source by linking the files into both heads, because MonoGame's DesktopGL and Android packages
  are separate assemblies.
- **Done when:** the desktop build and tests are unchanged and an empty Android head builds.
- **Needs:** the .NET Android workload (`dotnet workload install android`), a JDK and the Android SDK.
  None of these are installed on the development machine yet.

## Phase 2: Assets, storage and lifecycle

- **Assets:** the art, fonts and sounds are read from `legacy/` by file path today. Android has no such
  path, so they get packaged as Android assets and opened through `TitleContainer`.
- **Check the formats:** confirm the sound and image formats load on Android.
- **Storage:** settings and high scores go to the app's private storage, which `LocalApplicationData`
  already maps to.
- **Lifecycle:** auto-pause when the app goes to the background or the screen locks. Close sockets cleanly
  and reconnect or drop out of the game on return.

## Phase 3: Display

- The original is a fixed 640×480 layout. Render it to an off-screen target and scale it to the screen
  with letterboxing.
- Start in landscape. Phones are 19:9 or wider, so there will be bars at the sides; use them for the touch
  controls.
- Handle the notch and system bars (immersive mode).
- Keep the multi-player screen readable: several boards at 640×480 on a phone will be small. Decide later
  whether to show your own board large and the others reduced.

## Phase 4: Touch input (the biggest design piece)

- **Menus:** map touch to the existing mouse-style hit testing. Make hit areas bigger than the original's.
- **In-game controls:** two options:
  1. **Gestures:** swipe left and right to move, tap to rotate, swipe down for soft drop, flick down for
     hard drop. It is fast, but hard to tune.
  2. **On-screen buttons:** left, right, down, rotate and drop in the side bars. It is predictable and the
     first thing to build.
- Both feed the existing input layer, so the 1.1.0 key-repeat behaviour carries over. Hold-to-repeat on
  the arrow buttons needs checking.
- **Name entry:** the player name needs the Android soft keyboard.
- **Back button:** map it to Esc.
- **Haptics:** optional.

## Phase 5: Networking on Android

- **Permissions:** add INTERNET and ACCESS_NETWORK_STATE. LAN discovery may also need a multicast lock on
  some devices (CHANGE_WIFI_MULTICAST_STATE), or broadcasts won't arrive.
- **List server over plain HTTP:** Android 9 and later block cleartext HTTP by default, and the list
  server is HTTP today. Either serve it over HTTPS (recommended) or add a network-security exception.
- **Hosting from a phone:** a phone can join games. Hosting is possible but unreliable once the app is
  backgrounded, so leave it out of the first version and let the phone only join.

## Phase 6: Testing, release and polish

- **Core:** the existing tests still cover it.
- **On the device:** add a script-driven smoke run like the desktop `--smoke`, and try it on an emulator
  and at least one real phone. Check frame rate, audio latency and the touch feel.
- **Packaging:** a signed app bundle, an app icon and a minimum Android version, probably 8 or newer.
- **Continuous integration:** an Android build job.

## Decisions needed

1. **Orientation:** landscape only, or portrait too? Portrait means a new layout and is the most work.
2. **Controls:** buttons first, then gestures later?
3. **Scope:** does the first version include multiplayer, or only single-player plus joining?
4. **Distribution:** sideloading for personal use, or Google Play?

Decision 4 matters most, because the art and sound licence (Ludus Design copyright, no separate statement)
is still unconfirmed. For a Play Store release treat that as a blocker and ask the upstream authors first.

## Order and effort

- **Phases 1–3:** single-player in landscape with menus working. This is the first milestone and mostly
  mechanical.
- **Phase 4:** the bulk of the work. It needs a real phone for tuning, because touch feel cannot be judged
  from tests.
- **Phase 5:** after that, once the touch controls feel right.
- **Phase 6:** at the end.

iOS would share phases 1–4 almost entirely, so splitting the client in Phase 1 helps both.
