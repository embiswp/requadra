# Releasing

Every commit on `master` whose CI run passed makes a release. Work happens on branches; `master` gets a merge
only when a feature is finished, so each merge is one release.

## What a release contains

| File | What |
|---|---|
| `Quadra-win-x64-X.Y.Z.zip`, `Quadra-win-arm64-X.Y.Z.zip` | Desktop game for Windows, with the .NET runtime included |
| `Quadra-macos-arm64-X.Y.Z.tar.gz`, `Quadra-macos-x64-X.Y.Z.tar.gz` | Desktop game for macOS (Apple silicon and Intel) |
| `Quadra-linux-x64-X.Y.Z.tar.gz`, `Quadra-linux-arm64-X.Y.Z.tar.gz` | Desktop game for Linux |
| `QuadraServer-X.Y.Z.zip` | Game list server (needs the .NET 8 runtime or newer) |
| `QuadraHost-X.Y.Z.zip` | Game host without a player (same) |
| `SHA256SUMS.txt` | Checksums of the files above |
| `ghcr.io/embiswp/requadra-server:X.Y.Z` (and `:latest`) | Docker image of the game list server, for amd64 and arm64 |
| `ghcr.io/embiswp/requadra-host:X.Y.Z` (and `:latest`) | Docker image of the game host |

`deploy/docker-compose.yml` runs the server (and optionally a host) for self-hosting. The server keeps its
high scores in the volume `/data`.

## How it runs

`.github/workflows/release.yml` starts when the **CI** workflow has finished successfully on `master`:

1. **Next version:** the newest `vX.Y.Z` among the tags and the releases (drafts included) is raised by one
   minor step. A label on the merged pull request chooses another size: `release:major` or `release:patch`.
   The first release is `v0.1.0`. A commit that already has a release (a re-run) is skipped.
2. **Packages:** every program is published with that version and packed.
3. **Docker:** both images are built for amd64 and arm64 and pushed to the GitHub container registry.
4. **Release:** a GitHub release `vX.Y.Z` is created with the files and the notes.

A failed test in CI, or a failed step above, stops everything: no release.

## Release notes

GitHub generates them from the merged pull requests since the last release, and `.github/release.yml` sorts
them by label. So:

- merge with **squash**, so a feature is one line;
- give each pull request a **title** that reads well in the notes ("Add survivor rounds");
- give it one label: `gameplay`, `network`, `desktop`, `server`, `docker`, `bug`, `ci`, `documentation` or
  `refactoring` (without a label it lands under "Other changes"); `skip-changelog` leaves it out;
- add `release:major` or `release:patch` when it should not be a minor version.

The text above the list (downloads, docker commands, how to open unsigned programs) is
`.github/release-header.md`.

## Drafts

A release is a **draft** until the repository variable `PUBLISH_RELEASES` is `true`. The desktop archives
contain the original art and sounds, which are Ludus Design copyright; publish only once the licence has been
confirmed. Until then, read the draft, then publish it by hand (Releases, Edit, Publish release), which also
creates the tag. After the variable is set, releases are published at once.

The server and host files and Docker images contain none of that art. They are pushed at once, whether the
release is a draft or not. The first push of an image makes a private package; set it to public once under
the package's settings on GitHub.

## Dry run

A change to the release files (the workflow, the notes settings, a `Dockerfile` or `deploy/`) on `spike`, or
in a pull request to `master`, runs the whole pipeline without pushing images and without making a release.
A release can also be started by hand from the Actions tab (on `master` only).

## Not done yet

- Signing: Windows shows a SmartScreen warning and macOS blocks a download until its quarantine mark is
  cleared (the release text says how). Signing and notarizing need paid certificates.
- A macOS `.app` bundle and a Windows installer.
- A version number shown in the game itself (it is built into the files, but not displayed).
