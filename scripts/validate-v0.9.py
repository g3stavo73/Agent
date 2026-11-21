#!/usr/bin/env python3
"""Fast, dependency-free regression checks for the declarative UI and AiSTo contracts introduced in v0.9."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors = []
checks = 0

def check(condition, message):
    global checks
    checks += 1
    if not condition:
        errors.append(message)

# XML well-formedness for every XAML/XML/SVG resource.
for path in ROOT.rglob('*'):
    if path.is_file() and path.suffix.lower() in {'.xaml', '.xml', '.svg'}:
        try:
            ET.parse(path)
            check(True, f'XML parse: {path.relative_to(ROOT)}')
        except ET.ParseError as exc:
            check(False, f'XML parse failed: {path.relative_to(ROOT)}: {exc}')

ui = (ROOT / 'src/Agent.Core/Ui/AgentUiDocument.cs').read_text(encoding='utf-8')
check('MaxBlocks = 32' in ui and 'MaxPayloadBytes = 64 * 1024' in ui, 'UI payload limits are present')
check('ActionIds' in ui and 'navigate.settings' in ui and 'actionId is only valid' in ui, 'UI actions are allowlisted')
check('XAML, HTML, executable code or URLs' in ui, 'UI contract forbids executable markup')

mapper = (ROOT / 'src/Agent.Core/Protocol/AgentRuntimeEventMapper.cs').read_text(encoding='utf-8')
for method in ['turn/started', 'turn/completed', 'item/agentMessage/delta', 'item/started', 'item/completed', 'item/commandExecution/requestApproval', 'item/fileChange/requestApproval']:
    check(method in mapper, f'App Server event mapping exists: {method}')
check('RequiresUserDecision: true' in mapper, 'approval events require a user decision')
protocol = (ROOT / 'src/Agent.Core/Protocol/CodexAppServerClient.cs').read_text(encoding='utf-8')
check('sandboxPolicy' in protocol and 'outputSchema' in protocol, 'turn client forwards sandbox and structured output options')
check('SingleReader = true' in protocol and '_messageReaderActive' in protocol, 'Codex event stream prevents competing readers from silently consuming each other events')
draft_assistant = (ROOT / 'src/Agent.Core/AiSto/AiStoDraftAssistant.cs').read_text(encoding='utf-8')
check('type = "readOnly"' in draft_assistant and 'ApprovalPolicy: "never"' in draft_assistant, 'AiSTo drafting runs with read-only sandbox and no auto-approval')
check('OutputSchema: DraftOutputSchema' in draft_assistant, 'AiSTo draft uses structured output schema')
ui_schema = (ROOT / 'src/Agent.Core/Ui/AgentUiDocumentSchema.cs').read_text(encoding='utf-8')
check('maxItems' in ui_schema and 'navigate.settings' in ui_schema, 'declarative UI schema has bounded blocks and allowlisted actions')

# Embedded JSON schemas must be valid JSON before any runtime/schema validation.
for schema_name, schema_path in [
    ('Agent UI', ROOT / 'src/Agent.Core/Ui/AgentUiDocumentSchema.cs'),
    ('AiSTo draft', ROOT / 'src/Agent.Core/AiSto/AiStoDraftAssistant.cs'),
    ('user intent', ROOT / 'src/Agent.Core/Intents/AgentUserIntent.cs'),
]:
    source = schema_path.read_text(encoding='utf-8')
    match = re.search(r'JsonDocument\.Parse\(\"\"\"\s*(.*?)\s*\"\"\"\)', source, re.S)
    try:
        parsed_schema = json.loads(match.group(1)) if match else None
        check(isinstance(parsed_schema, dict) and parsed_schema.get('type') == 'object',
              f'{schema_name} structured-output schema is valid JSON')
    except json.JSONDecodeError:
        check(False, f'{schema_name} structured-output schema is valid JSON')

prompt = (ROOT / 'src/Agent.Core/AiSto/AiStoDraftContracts.cs').read_text(encoding='utf-8')
check('do not ask follow-up questions' in prompt, 'AiSTo assistant does not require field-by-field questioning')
check('Do not modify files' in prompt and 'Never invent repository facts' in prompt, 'AiSTo research is read-only and evidence-based')
check('Do not duplicate native natural-language understanding' in prompt, 'AiSTo reuses existing Codex capabilities')

api = (ROOT / 'src/Agent.Mobile/Services/AgentApiClient.cs').read_text(encoding='utf-8')
check('/health/live' in api and '/health/ready' in api, 'API liveness and readiness are both checked')
check('TimeSpan.FromSeconds(5)' in api, 'API probes have a per-request timeout')
settings = (ROOT / 'src/Agent.Mobile/Pages/SettingsPage.xaml.cs').read_text(encoding='utf-8')
check('OnTestApiConnectionClicked' in settings, 'Settings screen wires API test')

# Every MAUI binding/service type is present where expected.
program = (ROOT / 'src/Agent.Mobile/MauiProgram.cs').read_text(encoding='utf-8')
check('IAgentApiClient, AgentApiClient' in program, 'API client registered in MAUI DI')
renderer = (ROOT / 'src/Agent.Mobile/Services/AgentUiRenderer.cs').read_text(encoding='utf-8')
check('AgentUiDocumentValidator.Validate' in renderer and 'BuildAction' in renderer, 'declarative UI renderer validates and renders safe actions')
chat = (ROOT / 'src/Agent.Mobile/Pages/ChatPage.xaml.cs').read_text(encoding='utf-8')
check('TryRenderStructuredResponse' in chat and 'AgentUiDocumentParser.Parse' in chat, 'chat page renders parsed structured responses')
check('ApplicationDisplayVersion>1.0.0' in (ROOT / 'src/Agent.Mobile/Agent.Mobile.csproj').read_text(encoding='utf-8'), 'app metadata is version 1.0.0')
assistant = (ROOT / 'src/Agent.Core/AiSto/AiStoDraftAssistant.cs').read_text(encoding='utf-8')
check('CodexApprovalDecision.Decline' in assistant and 'AiStoDraftApprovalRequiredException' in assistant, 'AiSTo assistant explicitly rejects approval requests when no UI confirmer exists')

intents = (ROOT / 'src/Agent.Core/Intents/AgentUserIntent.cs').read_text(encoding='utf-8')
for intent_name in ['navigate', 'explain_current_screen', 'create_aisto_draft', 'unsupported']:
    check(intent_name in intents, f'typed intent is supported: {intent_name}')
check('IsAllowedDestination' in intents and '"settings"' in intents, 'intent destinations are locally allowlisted')
check('readOnly' in intents and 'OutputSchema: OutputSchema' in intents, 'Codex intent interpreter uses structured read-only turns')
check('ExistingThreadId' in intents and 'request.ExistingThreadId' in intents, 'intent interpreter can retain an existing Codex thread context')
check('IAgentUserIntentInterpreter, CodexUserIntentInterpreter' in program, 'natural-language intent interpreter is registered in DI')
aisto_page = (ROOT / 'src/Agent.Mobile/Pages/AiStoBuilderPage.xaml').read_text(encoding='utf-8')
check('CONSTRUIR COM AUXÍLIO DO CODEX' in aisto_page and 'Gerar rascunho com Codex' in aisto_page, 'AiSTo page exposes the one-brief assisted builder')
check('IsEnabled="False"' in aisto_page and 'A geração automática está preparada' in aisto_page, 'AiSTo action is honestly gated pending native Android host')

if errors:
    print(f'FAIL: {len(errors)} of {checks} checks failed')
    for item in errors: print(' -', item)
    sys.exit(1)
print(f'PASS: {checks} static/contract checks')
