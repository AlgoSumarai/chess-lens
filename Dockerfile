FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json Directory.Build.props ChessLens.slnx ./
COPY src/ChessLens.Core/ChessLens.Core.csproj src/ChessLens.Core/packages.lock.json src/ChessLens.Core/
COPY src/ChessLens.Api/ChessLens.Api.csproj src/ChessLens.Api/packages.lock.json src/ChessLens.Api/
COPY src/ChessLens.Worker/ChessLens.Worker.csproj src/ChessLens.Worker/packages.lock.json src/ChessLens.Worker/
COPY tests/ChessLens.Tests/ChessLens.Tests.csproj tests/ChessLens.Tests/packages.lock.json tests/ChessLens.Tests/
RUN --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then cp /run/secrets/build_ca /usr/local/share/ca-certificates/chesslens-build.crt && update-ca-certificates; fi; \
    dotnet restore ChessLens.slnx --locked-mode
COPY src/ src/
COPY tests/ tests/
COPY samples/ samples/
RUN dotnet publish src/ChessLens.Api -c Release --no-restore -o /app/api /p:UseAppHost=false
RUN dotnet publish src/ChessLens.Worker -c Release --no-restore -o /app/worker /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS engine
ADD --checksum=sha256:9defc0d4e55d49c65a6d042f3e571a39fcea499ade6dbe741b53b8c65e03611f https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-linux-x86-64-universal.tar.gz /tmp/stockfish.tar.gz
RUN mkdir /engine && tar -xzf /tmp/stockfish.tar.gz -C /engine

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS api
WORKDIR /app
COPY --from=build /app/api .
RUN mkdir -p /app/keys && chown -R app:app /app/keys
ENV ASPNETCORE_HTTP_PORTS=8080 DataProtectionPath=/app/keys
USER app
ENTRYPOINT ["dotnet", "ChessLens.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS worker
WORKDIR /app
COPY --from=build /app/worker .
COPY --from=engine /engine/stockfish /opt/stockfish
ENV Stockfish__Path=/opt/stockfish/stockfish-linux-x86-64-universal
USER app
ENTRYPOINT ["dotnet", "ChessLens.Worker.dll"]
