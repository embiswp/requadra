## Downloads

| | |
|---|---|
| **Desktop game** | `Quadra-<system>-<version>` for your system. Unpack it and run `Quadra`. It brings its own .NET runtime. |
| **Game list server** | `QuadraServer-${VERSION}.zip` (needs the .NET 8 runtime or newer), or the Docker image below. |
| **Game host without a player** | `QuadraHost-${VERSION}.zip`, or the Docker image below. |

```
docker run -d -p 8080:8080 -v quadra-scores:/data ghcr.io/${OWNER}/requadra-server:${VERSION}
docker run -d -p 3456:3456 -p 3457:3457/udp ghcr.io/${OWNER}/requadra-host:${VERSION}
```

A ready-made `docker-compose.yml` is in [`deploy/`](https://github.com/${OWNER}/requadra/tree/v${VERSION}/deploy). `SHA256SUMS.txt` lists the checksums.

The programs are not signed. Windows may show a SmartScreen warning ("More info", then "Run anyway"). On macOS,
clear the quarantine mark first: `xattr -dr com.apple.quarantine Quadra-macos-*` and `chmod +x Quadra`.

## What changed

