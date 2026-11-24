# Rust worker and ASP.NET API are built with their own pinned toolchains.
FROM rust:1.100.0-slim-bookworm AS worker-build
WORKDIR /scr
COPY workers/aisto-workers ./workers/aisto-workers
COPY tests/worker_protocol.rs ./tests/worker_protocol.rs
RUN cargo test --release --manifest-path workers/aisto-workers/Cargo.toml
RUN cargo build --release --manifest-path workers/aisto-workers/Cargo.toml

# Agent.Api has its own global.json and targets .NET 10.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY . .
WORKDIR /src/srcc/Agent.Api
RUN dotnet --info
RUN dotnet restore Agent.Api.csproj
RUN dotnet publish Agent.Api.csproj --configuration Release --output /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=api-build /out .
COPY --from=worker-build /src/workers/aisto-workers/target/release/aisto-worker /app/aisto-worker
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Agent.Api.dll"]
