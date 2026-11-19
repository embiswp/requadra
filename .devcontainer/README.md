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
