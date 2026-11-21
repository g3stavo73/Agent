#!/usr/bin/env python3
"""Static Agent project checks; intentionally not a substitute for SDK/runtime tests."""
from __future__ import annotations

import json
import pathlib
import re
import shutil
import subprocess
import tomllib
import xml.etree.ElementTree as ET

try:
    import yaml
except ImportError as exc:
    raise SystemExit("PyYAML is required for compose-file validation.") from exc

ROOT = pathlib.Path(__file__).resolve().parents[1]
checks: list[str] = []


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"FAIL: {message}")
    checks.append(message)


# Project and configuration structure.
api_project_path = ROOT / "src/Agent.Api/Agent.Api.csproj"
mobile_project_path = ROOT / "src/Agent.Mobile/Agent.Mobile.csproj"
core_project_path = ROOT / "src/Agent.Core/Agent.Core.csproj"
for project in [api_project_path, mobile_project_path, core_project_path]:
    ET.parse(project)
require(True, "All API, MAUI and core C# project files are well-formed XML")

mobile_project = ET.parse(mobile_project_path).getroot()
require(mobile_project.findtext("./PropertyGroup/RootNamespace") == "Agent.Mobile",
        "MAUI root namespace is Agent.Mobile")
require(mobile_project.findtext("./PropertyGroup/ApplicationTitle") == "Agent",
        "MAUI application display title is Agent")
require(mobile_project.findtext("./PropertyGroup/ApplicationDisplayVersion") == "1.0.0",
        "MAUI display version is 1.0.0")
require(mobile_project.findtext("./PropertyGroup/ApplicationVersion") == "10",
        "MAUI Android version code is 10")
require("Agent.Core\\Agent.Core.csproj" in mobile_project_path.read_text(),
        "MAUI references the renamed Agent.Core project")

old_names = [
    p.relative_to(ROOT).as_posix()
    for p in (ROOT / "src").rglob("*")
    if p.is_file() and p.suffix.lower() in {".cs", ".xaml", ".csproj"}
    and ("CodexMobile" in p.name or "CodexMobile" in p.read_text(encoding="utf-8", errors="ignore"))
]
require(not old_names, "Mobile/core source no longer contains stale CodexMobile project identifiers")

# Parse all XAML/XML and JSON/TOML configuration files in the source tree.
xml_files = list((ROOT / "src").rglob("*.xaml")) + list((ROOT / "src").rglob("*.csproj"))
xml_files += list((ROOT / "src").rglob("*.xml")) + list((ROOT / "src").rglob("*.svg"))
for path in xml_files:
    ET.parse(path)
require(True, f"All {len(xml_files)} MAUI XAML/XML/SVG files parse successfully")

xaml_ns = "{http://schemas.microsoft.com/winfx/2009/xaml}Class"
xaml_files = list((ROOT / "src/Agent.Mobile").rglob("*.xaml"))
for xaml_path in xaml_files:
    xaml_root = ET.parse(xaml_path).getroot()
    class_name = xaml_root.attrib.get(xaml_ns)
    require(bool(class_name), f"{xaml_path.relative_to(ROOT)} declares x:Class")
    code_path = xaml_path.with_suffix(".xaml.cs")
    require(code_path.exists(), f"{xaml_path.relative_to(ROOT)} has a code-behind file")
    code = code_path.read_text(encoding="utf-8")
    namespace, short_name = class_name.rsplit(".", 1)
    require(f"namespace {namespace};" in code and f"class {short_name}" in code,
            f"{xaml_path.relative_to(ROOT)} matches its code-behind namespace/class")
    event_names = {"Clicked", "TextChanged", "SelectedIndexChanged", "Completed", "Toggled",
                   "ValueChanged", "Refreshing", "ItemTapped", "Scrolled", "SearchButtonPressed",
                   "Focused", "Unfocused"}
    for element in xaml_root.iter():
        for attribute, handler in element.attrib.items():
            attribute_name = attribute.rsplit("}", 1)[-1]
            if attribute_name in event_names:
                require(re.search(r"\b" + re.escape(handler) + r"\s*\(", code) is not None,
                        f"{xaml_path.relative_to(ROOT)} event {handler} has a code-behind handler")

appsettings = json.loads((ROOT / "src/Agent.Api/appsettings.json").read_text())
require(appsettings["AiStoWorker"]["TimeoutSeconds"] == 15,
        "AiSTo worker default timeout is configured as 15 seconds")
require(1 <= appsettings["AiStoWorker"]["WorkerCount"] <= 8,
        "AiSTo worker process pool has a bounded default size")
compose = yaml.safe_load((ROOT / "deploy/compose.yaml").read_text())
require(set(compose["services"]) == {"postgres", "api"}, "Compose declares PostgreSQL and Agent API")
require(compose["services"]["api"]["depends_on"]["postgres"]["condition"] == "service_healthy",
        "API startup waits for database readiness")
require("127.0.0.1:8080:8080" in compose["services"]["api"]["ports"],
        "development API port is loopback-bound")
require("AiStoWorker__TimeoutSeconds" in compose["services"]["api"]["environment"],
        "Compose forwards the AiSTo worker timeout setting")
require("AiStoWorker__WorkerCount" in compose["services"]["api"]["environment"],
        "Compose forwards the bounded AiSTo worker pool size")

cargo = tomllib.loads((ROOT / "workers/aisto-worker/Cargo.toml").read_text())
require("url" in cargo["dependencies"], "Rust worker uses a standards-based URL parser")
require(cargo.get("profile", {}).get("release", {}).get("lto") == "thin" and
        cargo.get("profile", {}).get("release", {}).get("codegen-units") == 1,
        "Rust release profile enables thin LTO and a single codegen unit")
ci_workflow = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text())
require("jobs" in ci_workflow and set(ci_workflow["jobs"]) == {"rust-worker", "dotnet-services", "android-client", "static-and-schema"},
        "GitHub Actions CI contains Rust, API/Core, Android and static/schema jobs")
dockerignore = (ROOT / ".dockerignore").read_text()
gitignore = (ROOT / ".gitignore").read_text()
require("**/.env.*" in dockerignore and "**/*.p12" in dockerignore,
        "Docker build context excludes environment files and signing credentials")
require("!.env.example" in gitignore and "!deploy/.env.example" in gitignore,
        "Git ignore rules allow the deploy environment example while excluding local env files")

for script in [ROOT / "deploy/init-db.sh", ROOT / "scripts/apply-postgres-migrations.sh"]:
    if shutil.which("bash"):
        subprocess.run(["bash", "-n", str(script)], check=True)
require(True, "Bash deployment scripts pass bash -n")

for py in ROOT.rglob("*.py"):
    compile(py.read_text(encoding="utf-8"), str(py), "exec")
require(True, "All project Python validation scripts pass syntax compilation")

# API routing, security and endpoint contracts.
api = (ROOT / "src/Agent.Api/Program.cs").read_text()
for route in ["/health/live", "/health/ready", "/me"]:
    require(route in api, f"API route contract contains {route}")
for endpoint in ["ListAgents", "CreateAgent", "UpdateAgent", "DeleteAgent", "CompileAiStoContext"]:
    source = "\n".join(p.read_text() for p in (ROOT / "src/Agent.Api").rglob("*.cs"))
    require(endpoint in source, f"API endpoint {endpoint} is present")

middleware_order = [api.index("app.UseAuthentication();"), api.index("app.UseRateLimiter();"),
                    api.index("app.UseMiddleware<MicrosoftIdentityResolutionMiddleware>();"),
                    api.index("app.UseAuthorization();")]
require(middleware_order == sorted(middleware_order),
        "JWT authentication and rate limiting precede internal identity resolution and authorization")
require('SHA256.HashData(Encoding.UTF8.GetBytes(partitionSource))' in api,
        "Rate-limit partition keys hash the validated external identity")
identity_middleware = (ROOT / "src/Agent.Api/Middleware/MicrosoftIdentityResolutionMiddleware.cs").read_text()
require("IAllowAnonymous" in identity_middleware and "context.GetEndpoint()" in identity_middleware,
        "AllowAnonymous endpoints bypass internal account resolution")
require("issuer.Length > 500" in identity_middleware and "subject.Length > 500" in identity_middleware,
        "External identity claim lengths are checked before database resolution")

# AiSTo validation and bounded worker lifecycle.
contracts = (ROOT / "src/Agent.Api/Contracts/AgentProfileContracts.cs").read_text()
for limit in ["CharacterCount(Domain) > 80", "CharacterCount(Objective) > 1600",
              "CharacterCount(Instructions) > 8000", "CharacterCount(ProjectContext) > 5000",
              "CharacterCount(candidate) > 2048"]:
    require(limit in contracts, f"AiSTo API validation enforces {limit}")
require("uri.Scheme != Uri.UriSchemeHttps" in contracts and "uri.UserInfo" in contracts,
        "API requires HTTPS documentation sources without embedded credentials")
require("uri.AbsoluteUri" in contracts, "API stores normalized absolute documentation URLs")
profile_service = (ROOT / "src/Agent.Api/Services/AgentProfileService.cs").read_text()
endpoint_source = (ROOT / "src/Agent.Api/Endpoints/AiStoEndpoints.cs").read_text()
require("request.CanWriteRepositories && !request.CanReadRepositories" in profile_service,
        "Profile CRUD rejects repository write preference without read")
require("request.Capabilities.CanWriteRepositories && !request.Capabilities.CanReadRepositories" in endpoint_source,
        "AiSTo compile endpoint rejects write preference without read")

compiler = (ROOT / "src/Agent.Api/Services/RustAiStoCompiler.cs").read_text()
require("Math.Clamp(configuredSeconds, 1, 60)" in compiler,
        "Rust worker deadline is clamped to 1–60 seconds")
require("Channel.CreateBounded<WorkerSlot>" in compiler and "_availableSlots.Reader.ReadAsync(workerToken)" in compiler,
        "Rust compiler uses a bounded multi-process worker pool")
require("JsonSerializer.SerializeAsync" in compiler and "ReadLineAsync(workerToken)" in compiler,
        "Worker JSON input is streamed and output uses the bounded cancellation token")
require("AiStoWorker:WorkerCount" in compiler and "Math.Clamp(configuredWorkerCount, 1, 8)" in compiler,
        "Worker process count is configurable and capped at eight")
require("AiStoWorkerTimeoutException" in compiler,
        "Worker timeout is distinguished from client cancellation")
require("StatusCodes.Status504GatewayTimeout" in endpoint_source,
        "AiSTo worker timeout maps to HTTP 504")

worker = (ROOT / "workers/aisto-worker/src/main.rs").read_text()
require('"compile_aisto"' in worker, "Rust worker implements the compile_aisto protocol")
require("Capability preferences do not grant access" in worker,
        "Rust worker distinguishes requested capabilities from granted permissions")
require("#[cfg(test)]" in worker, "Rust worker includes unit tests")
require("documentation_sources.len() > 20" in worker and "parse_https_reference(trimmed)" in worker,
        "Rust independently enforces documentation URL count and HTTPS constraints")
require("String::with_capacity(16 * 1024)" in worker and "HashSet::with_capacity" in worker,
        "Rust worker reuses its JSON line buffer and preallocates bounded data structures")
require("String::with_capacity(estimated_capacity)" in worker,
        "Rust context generation reserves output capacity before concatenation")
for test_name in ["rejects_invalid_documentation_urls", "rejects_more_than_twenty_documentation_sources",
                  "accepts_https_localhost_and_ip_hosts", "rejects_write_without_read_permission_preference"]:
    require(test_name in worker, f"Rust worker tests {test_name}")

# Mobile settings persistence, app theme and accessibility surface.
settings_store = (ROOT / "src/Agent.Mobile/Services/AgentSettingsStore.cs").read_text()
settings_contract = (ROOT / "src/Agent.Mobile/Services/IAgentSettingsStore.cs").read_text()
settings_page = (ROOT / "src/Agent.Mobile/Pages/SettingsPage.xaml").read_text()
settings_code = (ROOT / "src/Agent.Mobile/Pages/SettingsPage.xaml.cs").read_text()
mobile_boot = (ROOT / "src/Agent.Mobile/MauiProgram.cs").read_text()
app_code = (ROOT / "src/Agent.Mobile/App.xaml.cs").read_text()
aisto_vm = (ROOT / "src/Agent.Mobile/ViewModels/AiStoBuilderViewModel.cs").read_text()
require("Preferences.Default" in settings_store and "SecureStorage.Default" not in settings_store,
        "Mobile settings use Preferences and do not store secrets or auth tokens")
settings_model = (ROOT / "src/Agent.Mobile/Models/AgentSettings.cs").read_text()
require("AgentSettings Load()" in settings_contract and "ThemeMode" in settings_model and
        "DefaultResponseStyle" in settings_model,
        "Settings contract persists theme and AiSTo response defaults")
require("IAgentSettingsStore, AgentSettingsStore" in mobile_boot,
        "Settings store is registered through MAUI dependency injection")
require("settingsStore.Load().ThemeMode" in app_code and "RequestedThemeChanged" in app_code,
        "Stored theme is loaded on boot and follows Android theme changes")
require("Application.Current is global::Agent.Mobile.App app" in settings_code,
        "Settings screen applies theme through the Agent application")
require("_settingsStore.Load().DefaultResponseStyle" in aisto_vm,
        "New AiSTo profiles inherit the user's saved response style")
for section in ["AccountSection", "AiPreferencesSection", "AppearanceSection",
                "ConnectionSection", "PrivacySection", "AboutSection"]:
    require(f'x:Name="{section}"' in settings_page,
            f"Settings UI declares the {section} group")
require("OnSettingsSearchChanged" in settings_page and "NoResultsLabel" in settings_page,
        "Settings screen offers working search and an empty state")
require("OnSaveApiUrlClicked" in settings_code and "Uri.UriSchemeHttps" in settings_store,
        "API endpoint setting persists validated HTTPS addresses only")
require("{DynamicResource AppBackground}" in settings_page and "ApplyPalette" in app_code,
        "Settings page uses the dynamic palette for light, dark and system themes")
app_shell = (ROOT / "src/Agent.Mobile/AppShell.xaml").read_text()
require('Title="Ajustes"' in app_shell and 'Route="settings"' in app_shell,
        "Settings screen is available as a primary mobile navigation tab")
activity = (ROOT / "src/Agent.Mobile/Platforms/Android/MainActivity.cs").read_text()
require("ConfigChanges.UiMode" in activity,
        "Android activity handles system UI mode changes for theme switching")
require("1.0.0" in (ROOT / "CHANGELOG.md").read_text() and "0.9.0" in (ROOT / "CHANGELOG.md").read_text(),
        "Changelog includes both Agent v1.0.0 and the v0.9 history")
require((ROOT / "docs/ROADMAP_V0_9_AND_V1_0.md").exists() and
        (ROOT / "docs/GITHUB_REPOSITORY_PREPARATION.md").exists(),
        "v0.9 declarative UI and v1.0 GitHub/review plans are documented")
require((ROOT / ".github/workflows/ci.yml").exists() and
        (ROOT / ".github/PULL_REQUEST_TEMPLATE.md").exists() and
        (ROOT / "CONTRIBUTING.md").exists() and (ROOT / "SECURITY.md").exists(),
        "GitHub CI, PR template, contribution and security guidance are present")
for xaml_path in (ROOT / "src/Agent.Mobile").rglob("*.xaml"):
    xaml_text = xaml_path.read_text(encoding="utf-8")
    for color_key in ["AppBackground", "Surface", "SurfaceRaised", "SurfaceSubtle", "Border",
                      "TextPrimary", "TextSecondary", "TextMuted", "Accent", "AccentStrong",
                      "AccentSurface", "Success", "Warning", "Danger", "CodeSurface"]:
        require(f"{{StaticResource {color_key}}}" not in xaml_text,
                f"{xaml_path.relative_to(ROOT)} resolves {color_key} dynamically")

# PostgreSQL tenant isolation and deployment boundaries.
sql = (ROOT / "database/postgres/migrations/001_initial_postgres.sql").read_text()
owned_tables = [
    "agent_profiles", "workspaces", "github_connections", "repository_grants",
    "conversations", "conversation_messages", "approval_requests", "repository_changes",
]
for table in owned_tables:
    require(f"CREATE TABLE IF NOT EXISTS {table}" in sql, f"PostgreSQL schema includes {table}")
    require(f"'{table}'" in sql, f"RLS policy loop covers {table}")
require("FORCE ROW LEVEL SECURITY" in sql, "RLS is forced, including for table owners")
health_check = (ROOT / "src/Agent.Api/Services/AgentDatabaseHealthCheck.cs").read_text()
require("to_regclass('public.agent_profiles')" in health_check,
        "Readiness checks that the initial Agent database schema is installed")
require("request.capabilities.can_write_repositories && !request.capabilities.can_read_repositories" in worker,
        "Rust independently rejects write preference without read")
require("rejects_write_without_read_permission_preference" in worker,
        "Rust worker has a regression test for the repository capability dependency")
for test_name in ["deduplicates_normalized_documentation_sources_without_reordering",
                  "rejects_urls_with_embedded_credentials",
                  "reserves_and_returns_context_for_long_but_valid_project_details"]:
    require(test_name in worker, f"Rust worker regression test {test_name}")
require("resolve_app_user" in sql and "PRIMARY KEY (issuer, subject)" in sql,
        "schema keys Microsoft external identities by issuer and subject")
require("repeat('0', 64)" not in sql, "migration does not contain a fake checksum")

api_sdk = json.loads((ROOT / "src/Agent.Api/global.json").read_text())
root_sdk = json.loads((ROOT / "global.json").read_text())
dockerfile = (ROOT / "deploy/Dockerfile").read_text()
require("dotnet/sdk:10.0" in dockerfile and "dotnet/aspnet:10.0" in dockerfile,
        "container builds and runs the backend on .NET 10 LTS")
require(api_sdk["sdk"]["version"].startswith("10.") and root_sdk["sdk"]["version"].startswith("11."),
        "backend is pinned to .NET 10 while MAUI remains on .NET 11")
require("USER $APP_UID" in dockerfile,
        "API container runs as the built-in non-root app user")
init_script = (ROOT / "deploy/init-db.sh").read_text()
require('--file "$MIGRATION"' not in init_script,
        "PostgreSQL bootstrap does not apply the auto-run migration twice")

print(f"PASS: {len(checks)} static checks")
for item in checks:
    print(f"  - {item}")
print("NOTE: Does not compile C#, compile/test Rust, execute PostgreSQL SQL, build Docker, or run Android.")
