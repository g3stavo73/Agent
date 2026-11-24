# The repository pins .NET SDK 11 in global.json. The API itself targets .NET 10.
FROM rust:1.100.0-slim-bookworm AS worker-build
WORKDIR /worker
COPY workers/aisto-workers/Cargo.toml ./Cargo.toml
COPY workers/aisto-workers/scr ./scr
RUN cargo test --release && cargo build --release

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS api-build
WORKDIR /src
COPY . .
WORKDIR /src/scr/Agent.Api
RUN dotnet restore Agent.Api.csproj
RUN dotnet publish Agent.Api.csproj --configuration Release --output /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=api-build /out .
COPY --from=worker-build /worker/target/release/aisto-worker /app/aisto-worker
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Agent.Api.dll"]
