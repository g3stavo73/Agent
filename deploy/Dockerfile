FROM rust:slim-bookworm AS worker-build
WORKDIR /worker
COPY workers/aisto-worker/Cargo.toml ./Cargo.toml
COPY workers/aisto-worker/src ./src
RUN cargo build --release

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY . .
WORKDIR /src/src/Agent.Api
RUN dotnet restore Agent.Api.csproj
RUN dotnet publish Agent.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=api-build /out .
COPY --from=worker-build /worker/target/release/aisto-worker /app/aisto-worker
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Agent.Api.dll"]
