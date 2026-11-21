#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

python3 scripts/validate-v1.0.py
python3 scripts/validate-v0.9.py
python3 scripts/validate-backend-static.py
python3 database/tests/validate_schema.py

cargo fmt --manifest-path workers/aisto-worker/Cargo.toml -- --check
cargo test --manifest-path workers/aisto-worker/Cargo.toml
cargo clippy --manifest-path workers/aisto-worker/Cargo.toml --all-targets -- -D warnings

dotnet build src/Agent.Core/Agent.Core.csproj --configuration Release
dotnet build src/Agent.Mobile/Agent.Mobile.csproj --configuration Release --framework net11.0-android36.0
(
  cd src/Agent.Api
  dotnet build Agent.Api.csproj --configuration Release
)
