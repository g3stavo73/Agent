#!/usr/bin/env python3
"""Static checks for the AiSTo worker patch; does not replace cargo test."""
from __future__ import annotations
import sys
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
checks = 0
errors: list[str] = []

def require(condition: bool, message: str) -> None:
    global checks
    checks += 1
    if not condition:
        errors.append(message)

manifest_text = (ROOT / "workers/aisto-workers/Cargo.toml").read_text(encoding="utf-8")
source = (ROOT / "workers/aisto-workers/scr/main.rs").read_text(encoding="utf-8")
tests = (ROOT / "add/workers/aisto-workers/tests/worker_protocol.rs").read_text(encoding="utf-8")
workflow = (ROOT / "add/.github/workflows/aisto-worker-tests.yml").read_text(encoding="utf-8")

def manifest_checks() -> None:
    manifest = tomllib.loads(manifest_text)
    require(manifest.get("package", {}).get("name") == "aisto-worker", "Cargo package name must remain aisto-worker")
    require(manifest.get("bin", [{}])[0].get("path") == "scr/main.rs", "Cargo bin path must point to the actual source file")
    require(manifest.get("bin", [{}])[0].get("name") == "aisto-worker", "Cargo bin name must match API executable path")

try:
    manifest_checks()
except (tomllib.TOMLDecodeError, IndexError, AttributeError) as exc:
    require(False, f"Cargo manifest is invalid: {exc}")

require("fn process_line(line: &str)" in source, "worker must isolate JSONL line parsing")
require("Invalid request payload" in source and "request_id" in source, "schema errors must preserve the request correlation id")
require("fn log_event(" in source and "io::stderr()" in source, "structured logs must use stderr")
require("write_response(&mut output" in source, "responses must go through the JSONL stdout writer")
require("input.read_line(&mut line)" in source, "worker must keep line-oriented stdin protocol")
require("thread::spawn" in source, "unit tests must exercise concurrent independent compilations")
require("worker_correlates_schema_errors" in tests, "process integration test must verify request-id correlation")
require("independent_worker_processes_handle_requests_concurrently" in tests, "process integration test must exercise parallel worker processes")
require("cargo fmt" in workflow and "cargo test" in workflow and "cargo clippy" in workflow, "CI must run format, tests, and Clippy")

# Explicitly assert that this patch did not introduce a generic memory/frame cap.
for forbidden in ("MAX_REQUEST_BYTES", "MAX_MEMORY_BYTES", "MAX_LINE_BYTES", ".take(MAX_"):
    require(forbidden not in source, f"worker must not introduce generic resource cap marker {forbidden}")

if errors:
    print(f"FAILED: {len(errors)} of {checks} static checks")
    for error in errors:
        print(f" - {error}")
    sys.exit(1)

print(f"OK: {checks} static patch checks passed; Cargo compilation remains required")
