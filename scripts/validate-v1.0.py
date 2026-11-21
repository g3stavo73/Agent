#!/usr/bin/env python3
"""Dependency-light release metadata, project and repository hygiene checks."""
from __future__ import annotations

import json
import re
import sys
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
checks: list[str] = []
failures: list[str] = []

def check(condition: bool, label: str) -> None:
    (checks if condition else failures).append(label)

# Version metadata must be consistent across source, Android and the visible settings UI.
version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
mobile = ET.parse(ROOT / "src/Agent.Mobile/Agent.Mobile.csproj").getroot()
mobile_group = mobile.find("./PropertyGroup")
check(version == "1.0.0", "VERSION declares 1.0.0")
check(mobile_group is not None and mobile_group.findtext("ApplicationDisplayVersion") == version,
      "Android display version matches VERSION")
check(mobile_group is not None and mobile_group.findtext("ApplicationVersion") == "10",
      "Android version code is 10")
settings = (ROOT / "src/Agent.Mobile/Pages/SettingsPage.xaml").read_text(encoding="utf-8")
check("versão 1.0.0" in settings and 'Text="1.0.0 (10)"' in settings,
      "Settings UI displays version 1.0.0 (10)")
core = (ROOT / "src/Agent.Core/Protocol/CodexAppServerClient.cs").read_text(encoding="utf-8")
check('clientVersion = "1.0.0"' in core, "Codex client identifies as Agent 1.0.0")

# Project graph and pinned SDK contracts.
for rel in ["Directory.Build.props", "src/Agent.Core/Agent.Core.csproj",
            "src/Agent.Mobile/Agent.Mobile.csproj", "src/Agent.Api/Agent.Api.csproj"]:
    ET.parse(ROOT / rel)
check(True, "Shared build props and all project files are valid XML")
solution = (ROOT / "Agent.sln").read_text(encoding="utf-8")
for rel in ["src/Agent.Core/Agent.Core.csproj", "src/Agent.Mobile/Agent.Mobile.csproj", "src/Agent.Api/Agent.Api.csproj"]:
    check(rel.replace("/", "\\") in solution and (ROOT / rel).is_file(),
          f"Solution contains existing project {rel}")
for rel, expected in [("global.json", "11.0.100"), ("src/Agent.Api/global.json", "10.0.100")]:
    data = json.loads((ROOT / rel).read_text(encoding="utf-8"))
    check(data.get("sdk", {}).get("version") == expected,
          f"{rel} pins SDK {expected}")

# Validate machine-readable assets and Rust package metadata.
json_files = sorted(ROOT.rglob("*.json"))
for path in json_files:
    if any(part in {"bin", "obj", "target", "__pycache__"} for part in path.parts):
        continue
    json.loads(path.read_text(encoding="utf-8"))
check(True, f"All {len(json_files)} repository JSON files parse")
xml_files = sorted({*ROOT.rglob("*.xaml"), *ROOT.rglob("*.svg"), *ROOT.rglob("*.xml")})
for path in xml_files:
    ET.parse(path)
check(True, f"All {len(xml_files)} XAML/SVG/XML files parse")
with (ROOT / "workers/aisto-worker/Cargo.toml").open("rb") as stream:
    cargo = tomllib.load(stream)
check(cargo.get("package", {}).get("name") == "aisto-worker",
      "Rust worker Cargo manifest has expected package name")

# Release packaging and repository safety checks.
required = [
    "README.md", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md", ".gitignore",
    ".gitattributes", ".github/workflows/ci.yml", ".github/dependabot.yml",
    "docs/RELEASE_STATUS_1_0.md", "docs/GITHUB_RELEASE_PREPARATION.md",
    "docs/LICENSE_DECISION.md", "docs/VERIFICATION_V1_0.md", "scripts/verify-release.sh",
    "deploy/.env.example", "database/postgres/migrations/001_initial_postgres.sql",
    "database/postgres/tests/rls_smoke.sql"
]
for rel in required:
    check((ROOT / rel).is_file(), f"Release file exists: {rel}")
check(not (ROOT / "LICENSE").exists() and "license" in (ROOT / "docs/LICENSE_DECISION.md").read_text(encoding="utf-8").lower(),
      "No license was invented; maintainer decision is documented")
ignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
for token in [".env", "!.env.example", "*.keystore", "*.pfx", "**/bin/", "**/obj/", "**/target/"]:
    check(token in ignore, f".gitignore covers {token}")

sensitive_suffixes = {".pfx", ".p12", ".jks", ".keystore", ".pem", ".key"}
sensitive_files = []
for path in ROOT.rglob("*"):
    if not path.is_file() or any(part in {".git", "__pycache__", "bin", "obj", "target"} for part in path.parts):
        continue
    if path.suffix.lower() in sensitive_suffixes or path.name == ".env":
        sensitive_files.append(path.relative_to(ROOT).as_posix())
check(not sensitive_files, "No local environment file, private key or signing container is present")

readme = (ROOT / "README.md").read_text(encoding="utf-8")
release_status = (ROOT / "docs/RELEASE_STATUS_1_0.md").read_text(encoding="utf-8")
check("source release candidate" in readme.lower(), "README honestly labels release as a source candidate")
check("not implemented" in release_status.lower(), "Release status lists unfinished integration gates")
check("native host" in readme.lower() and "not included" in readme.lower(),
      "README discloses the missing native Codex host")
check("maui-android" in (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8"),
      "CI has an Android MAUI build job")
check("validate-v1.0.py" in (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8"),
      "CI runs the release validator")

# Parse YAML when the optional validator dependency is installed in CI.
try:
    import yaml  # type: ignore
except ImportError:
    pass
else:
    for rel in [".github/workflows/ci.yml", ".github/dependabot.yml", "deploy/compose.yaml"]:
        yaml.safe_load((ROOT / rel).read_text(encoding="utf-8"))
    check(True, "GitHub Actions, Dependabot and Compose YAML parse")

# Basic staged-tree hygiene: Python bytecode caches are not release inputs.
cache_dirs = [p.relative_to(ROOT).as_posix() for p in ROOT.rglob("__pycache__") if p.is_dir()]
check(not cache_dirs, "No Python cache directories are included in source tree")

for item in checks:
    print(f"PASS: {item}")
for item in failures:
    print(f"FAIL: {item}", file=sys.stderr)
print(f"RESULT: {len(checks)} passed, {len(failures)} failed")
sys.exit(1 if failures else 0)
