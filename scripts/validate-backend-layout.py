#!/usr/bin/env python3
"""Static layout and deployment checks for the Agent backend patch."""
from __future__ import annotations

import sys
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
checks = 0


def require(condition: bool, message: str) -> None:
    global checks
    checks += 1
    if not condition:
        errors.append(message)


def read(relative: str) -> str:
    path = ROOT / relative
    require(path.is_file(), f"required file missing: {relative}")
    return path.read_text(encoding="utf-8") if path.is_file() else ""


dockerfile = read("deploy/Dockerfile")
compose = read("deploy/compose.yaml")
api_project = read("scr/Agent.Api/Agent.Api.csproj")
compiler = read("scr/Agent.Api/Services/RustAiStoCompiler.cs")
appsettings = read("scr/Agent.Api/appsettings.json")
worker_manifest_text = read("workers/aisto-workers/Cargo.toml")
migration = read("database/postgres/migrations/001_initial_postgres.sql")
init_script = read("deploy/init-db.sh")
toolchain = read("rust-toolchain.toml")
workflow = read(".github/workflows/backend-validation.yml")
dockerignore = read(".dockerignore")

try:
    manifest = tomllib.loads(worker_manifest_text)
    require(manifest.get("package", {}).get("name") == "aisto-worker", "Cargo package name must be aisto-worker")
    require(manifest.get("bin", [{}])[0].get("path") == "scr/main.rs", "Cargo binary must point to scr/main.rs")
except (tomllib.TOMLDecodeError, IndexError, AttributeError) as exc:
    require(False, f"Cargo.toml is invalid: {exc}")

try:
    ET.parse(ROOT / "scr/Agent.Api/Agent.Api.csproj")
    require("../../database/postgres/migrations/*.sql" in api_project, "API project must copy migration SQL")
except ET.ParseError as exc:
    require(False, f"Agent.Api.csproj is invalid XML: {exc}")

require("workers/aisto-workers/Cargo.toml" in dockerfile, "Dockerfile must use workers/aisto-workers")
require("workers/aisto-workers/scr" in dockerfile, "Dockerfile must copy actual Rust source directory")
require("/src/scr/Agent.Api" in dockerfile, "Dockerfile must build from scr/Agent.Api")
require("dotnet/sdk:11.0" in dockerfile and "aspnet:10.0" in dockerfile, "Docker SDK/runtime must match SDK pin and API TFM")
require("cargo test --release" in dockerfile, "Docker build must execute Rust unit tests")
require("aisto-workers" in compiler and "aisto-worker" in compiler, "local AiSTo worker default path must match repository layout")
require("workers/aisto-workers/target/release/aisto-worker" in appsettings, "appsettings worker path must match repository layout")
require("../database/postgres/migrations/001_initial_postgres.sql" in compose, "Compose migration mount must resolve")
require("image: agent-api:local" in compose, "Compose must provide a stable local image name")
require("CREATE TABLE IF NOT EXISTS public.app_users" in migration, "migration must define identity mapping")
require("CREATE TABLE IF NOT EXISTS public.agent_profiles" in migration, "migration must define agent_profiles")
require("resolve_app_user" in migration, "migration must define resolve_app_user")
require("ENABLE ROW LEVEL SECURITY" in migration and "WITH CHECK" in migration, "agent_profiles must enforce RLS reads and writes")
require("FORCE ROW LEVEL SECURITY" in migration, "agent_profiles must force RLS")
require("SET search_path = pg_catalog, pg_temp" in migration, "SECURITY DEFINER function must use a safe search_path")
require("001_initial_postgres.sql" in init_script and "schema_migrations" in init_script, "init script must record migration checksum")
require("NOSUPERUSER" in init_script and "NOBYPASSRLS" in init_script, "application login role must be kept non-privileged")
require('channel = "1.100.0"' in toolchain and "rust:1.100.0-slim-bookworm" in dockerfile, "Rust toolchain must be pinned consistently")
require("deploy/.env" in dockerignore, "Docker context must exclude deployment secrets")
require("/health/ready" in workflow and 'test "$status" = "401"' in workflow, "CI must test readiness and anonymous authorization")
require("Verify PostgreSQL row-level isolation" in workflow and "resolve_app_user" in workflow, "CI must test tenant row isolation")
require("/app/aisto-worker" in workflow and '"ok":true' in workflow, "CI must smoke-test the Rust worker")
require("src/Agent.Api" not in dockerfile and "workers/aisto-worker/" not in dockerfile, "stale Docker paths must be removed")

if errors:
    print(f"FAILED: {len(errors)} of {checks} checks")
    for error in errors:
        print(f" - {error}")
    sys.exit(1)

print(f"OK: {checks} backend layout/deployment checks passed")
