using System.Text;
using System.Text.Json;
using Agent.Core.Protocol;

namespace Agent.Core.AiSto;

public sealed class AiStoDraftApprovalRequiredException(string method)
    : InvalidOperationException($"Codex requested an approval before completing the specialization draft: {method}")
{
    public string Method { get; } = method;
}

/// <summary>
/// Uses the user's already-connected Codex App Server session to draft AiSTo.
/// The turn is constrained to a read-only sandbox and a strict JSON output schema.
/// It does not provision credentials, start a remote hosted runtime, or auto-approve tools.
/// </summary>
public sealed class AiStoDraftAssistant
{
    private static readonly JsonElement DraftOutputSchema = CreateDraftOutputSchema();

    private static JsonElement CreateDraftOutputSchema()
    {
        using var document = JsonDocument.Parse("""
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "name": { "type": "string", "maxLength": 80 },
        "description": { "type": "string", "maxLength": 240 },
        "domain": { "type": "string", "maxLength": 80 },
        "objective": { "type": "string", "maxLength": 1600 },
        "instructions": { "type": "string", "maxLength": 8000 },
        "projectContext": { "type": "string", "maxLength": 5000 },
        "documentationSources": { "type": "array", "maxItems": 20, "items": { "type": "string", "maxLength": 2048 } },
        "responseStyle": { "type": "string", "enum": ["StepByStep", "Concise", "Detailed", "CriticalReview"] },
        "canReadRepositories": { "type": "boolean" },
        "canWriteRepositories": { "type": "boolean" },
        "canBrowseWeb": { "type": "boolean" },
        "useSemanticNavigation": { "type": "boolean" },
        "preferUpdatedDocumentation": { "type": "boolean" },
        "applyArchitectureRules": { "type": "boolean" },
        "assumptions": { "type": "array", "maxItems": 8, "items": { "type": "string", "maxLength": 500 } },
        "evidence": { "type": "array", "maxItems": 12, "items": { "type": "string", "maxLength": 500 } }
      },
      "required": ["name", "description", "domain", "objective", "instructions", "projectContext", "documentationSources", "responseStyle", "canReadRepositories", "canWriteRepositories", "canBrowseWeb", "useSemanticNavigation", "preferUpdatedDocumentation", "applyArchitectureRules", "assumptions", "evidence"]
    }
    """);
        return document.RootElement.Clone();
    }

    public async Task<AiStoDraftProfile> CreateDraftAsync(
        CodexAppServerClient codex,
        AiStoDraftRequest request,
        Func<CodexServerMessage, CancellationToken, Task>? approvalHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codex);
        ArgumentNullException.ThrowIfNull(request);
        if (!codex.IsConnected)
            throw new InvalidOperationException("Connect the user's Codex App Server session before drafting AiSTo.");

        var hasWorkspace = !string.IsNullOrWhiteSpace(request.WorkspacePath) && Directory.Exists(request.WorkspacePath);
        var workspace = hasWorkspace ? Path.GetFullPath(request.WorkspacePath!) : Environment.CurrentDirectory;
        var threadId = await codex.StartThreadAsync(workspace, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var sandboxPolicy = new
        {
            type = "readOnly",
            access = hasWorkspace
                ? (object)new { type = "restricted", readableRoots = new[] { workspace } }
                : new { type = "restricted", readableRoots = Array.Empty<string>() }
        };
        var options = new CodexTurnOptions(
            ApprovalPolicy: "never",
            SandboxPolicy: sandboxPolicy,
            OutputSchema: DraftOutputSchema);
        var turnId = await codex.StartTurnAsync(threadId, AiStoDraftPromptBuilder.Build(request), options, cancellationToken)
            .ConfigureAwait(false);

        var output = new StringBuilder(capacity: 4_096);
        await foreach (var message in codex.ReadMessagesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!BelongsToThread(message.Parameters, threadId))
                continue;

            if (message.RequiresResponse)
            {
                if (approvalHandler is not null)
                {
                    await approvalHandler(message, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // No UI approval handler is present in this path. Reject known approval
                // requests explicitly; never leave a server request waiting or auto-approve.
                if (message.Method == "item/commandExecution/requestApproval")
                    await codex.RespondToCommandApprovalAsync(message, CodexApprovalDecision.Decline, cancellationToken)
                        .ConfigureAwait(false);
                else if (message.Method == "item/fileChange/requestApproval")
                    await codex.RespondToFileChangeApprovalAsync(message, CodexApprovalDecision.Decline, cancellationToken)
                        .ConfigureAwait(false);
                else
                    await codex.RespondToServerRequestErrorAsync(message, -32601,
                        "Agent does not support this server request.", cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                throw new AiStoDraftApprovalRequiredException(message.Method);
            }

            var eventData = AgentRuntimeEventMapper.Map(message);
            switch (eventData.Kind)
            {
                case AgentRuntimeEventKind.AssistantTextDelta:
                    if (!string.IsNullOrEmpty(eventData.Text) && output.Length + eventData.Text.Length <= 48 * 1024)
                        output.Append(eventData.Text);
                    break;
                case AgentRuntimeEventKind.TurnCompleted:
                    if (!string.Equals(ReadNestedString(message.Parameters, "turn", "id"), turnId, StringComparison.Ordinal))
                        continue;
                    if (eventData.Status != "completed")
                        throw new InvalidOperationException(eventData.Text ?? $"Codex turn ended with status '{eventData.Status}'.");
                    var parsed = AiStoDraftParser.Parse(output.ToString());
                    if (!parsed.Success || parsed.Profile is null)
                        throw new InvalidDataException(string.Join(" ", parsed.Errors));
                    return parsed.Profile;
            }
        }

        throw new EndOfStreamException("Codex App Server closed before the AiSTo draft completed.");
    }

    private static bool BelongsToThread(JsonElement parameters, string expectedThreadId)
    {
        var actual = ReadNestedString(parameters, "threadId");
        return actual is null || string.Equals(actual, expectedThreadId, StringComparison.Ordinal);
    }

    private static string? ReadNestedString(JsonElement element, params string[] path)
    {
        foreach (var part in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element))
                return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
