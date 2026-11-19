# Dev container

Everything needed to build, test and run the port, plus the Claude CLI. Open the folder in VS Code and
choose **Reopen in Container** (needs Docker and the Dev Containers extension).

| What | How |
|---|---|
| .NET 11 SDK (tests) and the .NET 8 runtime (the libraries and apps target net8.0) | `dotnet test`, `dotnet build` |
| Game window (MonoGame DesktopGL) | Software OpenGL and a virtual desktop: open the forwarded port 6080 (noVNC, password `vscode`), then `dotnet run --project src/Quadra.Desktop` in a terminal of the container. The display is `:1`. |
| Window without a screen | `xvfb-run dotnet run --project src/Quadra.Desktop -- --smoke 120 --data-dir /tmp/q` |
| Game list server | `dotnet run --project src/Quadra.Server -- --urls http://0.0.0.0:5000` (port 5000 is forwarded) |
| Game host | `dotnet run --project src/Quadra.Host -- --name "My game"` (TCP port 3456 is forwarded) |
| Claude CLI | `claude` in a terminal; the first run asks you to log in. Its settings live in the Docker volume `quadra-claude-config`, so they survive a rebuild. |
| GitHub CLI | `gh auth login` |

Limits of a container:

- **Sound:** there is none. OpenAL is set to its null device (`ALSOFT_DRIVERS=null`) so the game starts without a sound card.
- **LAN search:** it uses UDP broadcast, which does not cross Docker's network. Join by address instead
  (forward TCP 3456 from the container, or use the host's address).
- **Speed:** the window is drawn by software, so it is slower than on a real machine.

## Android

A second container, **Quadra (Android, x86-64)**, has what the Android app needs: JDK 17, the Android SDK
(platform 37, build tools, `adb`) and the .NET Android workload. Choose it when VS Code asks which
configuration to open, or run **Dev Containers: Reopen in Container** again to switch.

```
dotnet build src/Quadra.Android          # builds bin/Debug/net11.0-android/org.quadra.game-Signed.apk
```

- **Why a separate container:** Google's resource compiler (AAPT2) only exists for x86-64 Linux. On an ARM
  machine such as an Apple-silicon Mac the container therefore runs emulated, which is slower, so the
  main container stays native for everything else.
- **No game window, no emulator:** it builds the app. Install the APK on a phone with `adb` (the phone's USB
  debugging, or wireless debugging with `adb pair` / `adb connect`), or copy the file over and sideload it.
- **Not in `Quadra.slnx`:** the Android project needs the workload, so a plain `dotnet build` of the solution
  skips it.
