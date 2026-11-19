# The game list server. Build from the repository root:
#   docker build -f src/Quadra.Server/Dockerfile -t requadra-server .
# The published program does not depend on the processor, so it is built once and runs on any platform.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG VERSION=0.0.0
WORKDIR /src
COPY src/Quadra.Core src/Quadra.Core
COPY src/Quadra.Server src/Quadra.Server
RUN dotnet publish src/Quadra.Server -c Release -p:Version=$VERSION -o /app \
    && mkdir /data

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# The high scores are kept here; mount a volume to keep them when the container is replaced.
COPY --from=build --chown=app:app /data /data
ENV Qserv__ScoresFile=/data/scores.json
VOLUME /data
USER app
# The game list answers on port 8080 (the default of the .NET 8 container images).
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quadra.Server.dll"]
