# A game host without a player. Build from the repository root:
#   docker build -f src/Quadra.Host/Dockerfile -t requadra-host .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG VERSION=0.0.0
WORKDIR /src
COPY src/Quadra.Core src/Quadra.Core
COPY src/Quadra.Host src/Quadra.Host
RUN dotnet publish src/Quadra.Host -c Release -p:Version=$VERSION -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
USER app
# Players connect over TCP; searches on the local network use UDP on the next port.
EXPOSE 3456/tcp 3457/udp
ENTRYPOINT ["dotnet", "Quadra.Host.dll"]
CMD ["--name", "Quadra game"]
