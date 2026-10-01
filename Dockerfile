# syntax=docker/dockerfile:1
# One Dockerfile, one target per service:
#   docker build --target ingest-api -t traffic-events/ingest-api:dev .
# The build stage is shared, so building all four images compiles the solution once.

# ---- build: full SDK, never shipped ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/ src/
# The cache mount keeps NuGet packages between builds without baking them into a layer.
RUN --mount=type=cache,target=/root/.nuget/packages \
    for p in IngestApi Processor QueryApi Simulator; do \
      dotnet publish "src/TrafficEvents.$p/TrafficEvents.$p.csproj" -c Release -o "/out/$p" -p:UseAppHost=false || exit 1; \
    done

# ---- runtime base: chiseled = no shell, no package manager, non-root (UID 1654) by default ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
ARG APP_VERSION=dev
ENV APP_VERSION=${APP_VERSION} \
    ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
USER $APP_UID
EXPOSE 8080

FROM runtime AS ingest-api
COPY --from=build /out/IngestApi .
ENTRYPOINT ["dotnet", "TrafficEvents.IngestApi.dll"]

FROM runtime AS processor
COPY --from=build /out/Processor .
ENTRYPOINT ["dotnet", "TrafficEvents.Processor.dll"]

FROM runtime AS query-api
COPY --from=build /out/QueryApi .
ENTRYPOINT ["dotnet", "TrafficEvents.QueryApi.dll"]

FROM runtime AS simulator
COPY --from=build /out/Simulator .
ENTRYPOINT ["dotnet", "TrafficEvents.Simulator.dll"]
