using System.Text.Json;
using Agent.Core.Protocol;

namespace Agent.Core.Intents;

public sealed record AgentUserIntentRequest(string UserText, string CurrentScreen, string? ExistingThreadId = null);

public enum AgentIntentKind
{
    Navigate,
    ExplainCurrentScreen,
    CreateAiStoDraft,
    Unsupported
}

/// <summary>
/// A proposal produced from natural language. It is data only; a separate local router
/// must validate the destination and decide whether any operation needs confirmation.
/// </summary>
public sealed record AgentUserIntent(
    AgentIntentKind Kind,
    string? Destination = null,
    string? Brief = null,
    string? Explanation = null);

public interface IAgentUserIntentInterpreter
{
    Task<AgentUserIntent> InterpretAsync(
        CodexAppServerClient codex,
        AgentUserIntentRequest request,
        CancellationToken cancellationToken = default);
}

public static class AgentIntentPolicy
{
    private static readonly HashSet<string> Destinations = new(StringComparer.Ordinal)
    {
        "chat", "agents", "projects", "history", "context", "plugins", "settings"
    };

    public static bool IsAllowedDestination(string? destination) =>
        destination is not null && Destinations.Contains(destination);

    /// <summary>Only navigation and read-only explanation/draft proposals exist in v0.9.</summary>
    public static bool RequiresExplicitConfirmation(AgentUserIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        // v0.9 exposes only navigation and read-only proposals. Mutating intents are
        // intentionally absent; when added, they must opt into confirmation by default.
        return false;
    }
}

public static class AgentUserIntentInterpreter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 8
    };

    public static AgentUserIntent Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > 16 * 1024)
            throw new InvalidDataException("The intent response is empty or exceeds 16 KiB.");

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var payload = JsonSerializer.Deserialize<IntentPayload>(document.RootElement.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("Codex returned an empty intent.");

        var intentCode = payload.Intent ?? string.Empty;
        var normalizedBrief = payload.Brief?.Trim();
        var intent = intentCode switch
        {
            "navigate" when AgentIntentPolicy.IsAllowedDestination(payload.Destination) =>
                new AgentUserIntent(AgentIntentKind.Navigate, Destination: payload.Destination),
            "explain_current_screen" =>
                new AgentUserIntent(AgentIntentKind.ExplainCurrentScreen, Explanation: Limit(payload.Explanation, 600)),
            "create_aisto_draft" when normalizedBrief is { Length: >= 12 and <= 4_000 } =>
                new AgentUserIntent(AgentIntentKind.CreateAiStoDraft, Brief: normalizedBrief),
            "unsupported" =>
                new AgentUserIntent(AgentIntentKind.Unsupported, Explanation: Limit(payload.Explanation, 600)),
            _ => throw new InvalidDataException("Codex returned an intent or destination that Agent does not allow.")
        };

        if (intentCode != "navigate" && payload.Destination is not null)
            throw new InvalidDataException("Only navigation intents may include a destination.");
        if (intentCode != "create_aisto_draft" && payload.Brief is not null)
            throw new InvalidDataException("Only AiSTo draft intents may include a brief.");
        return intent;
    }

    private static string? Limit(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maximum ? trimmed : trimmed[..maximum];
    }

    private sealed record IntentPayload(
        string Intent,
        string? Destination,
        string? Brief,
        string? Explanation);
}

/// <summary>
/// Reuses Codex's existing language understanding to classify one user utterance.
/// The interpreter has no write-capable tools and returns proposals, never executes them.
/// </summary>
public sealed class CodexUserIntentInterpreter : IAgentUserIntentInterpreter
{
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    public async Task<AgentUserIntent> InterpretAsync(
        CodexAppServerClient codex,
        AgentUserIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codex);
        ArgumentNullException.ThrowIfNull(request);
        if (!codex.IsConnected)
            throw new InvalidOperationException("Connect the user's Codex App Server session before interpreting a request.");

        var userText = request.UserText?.Trim() ?? string.Empty;
        if (userText.Length is < 1 or > 4_000)
            throw new ArgumentException("User text must contain between 1 and 4000 characters.", nameof(request));
        var screenText = request.CurrentScreen?.Trim() ?? string.Empty;
        var currentScreen = screenText.Length == 0
            ? "unknown" : screenText[..Math.Min(screenText.Length, 80)];

        var existingThreadId = request.ExistingThreadId?.Trim();
        if (existingThreadId?.Length > 200)
            throw new ArgumentException("The existing Codex thread id is too long.", nameof(request));
        var threadId = string.IsNullOrWhiteSpace(existingThreadId)
            ? await codex.StartThreadAsync(Environment.CurrentDirectory, cancellationToken: cancellationToken).ConfigureAwait(false)
            : existingThreadId;
        var options = new CodexTurnOptions(
            ApprovalPolicy: "never",
            SandboxPolicy: new
            {
                type = "readOnly",
                access = new { type = "restricted", readableRoots = Array.Empty<string>() }
            },
            OutputSchema: OutputSchema);
        var turnId = await codex.StartTurnAsync(threadId, BuildPrompt(userText, currentScreen), options, cancellationToken)
            .ConfigureAwait(false);

        var output = new System.Text.StringBuilder(1_024);
        await foreach (var message in codex.ReadMessagesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!BelongsToThread(message.Parameters, threadId))
                continue;

            if (message.RequiresResponse)
            {
                if (message.Method == "item/commandExecution/requestApproval")
                    await codex.RespondToCommandApprovalAsync(message, CodexApprovalDecision.Decline, cancellationToken)
                        .ConfigureAwait(false);
                else if (message.Method == "item/fileChange/requestApproval")
                    await codex.RespondToFileChangeApprovalAsync(message, CodexApprovalDecision.Decline, cancellationToken)
                        .ConfigureAwait(false);
                else
                    await codex.RespondToServerRequestErrorAsync(message, -32601,
                        "Intent interpretation does not support server tool requests.", cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                throw new InvalidOperationException("A tool approval was requested during read-only intent interpretation.");
            }

            var eventData = AgentRuntimeEventMapper.Map(message);
            if (eventData.Kind == AgentRuntimeEventKind.AssistantTextDelta && !string.IsNullOrEmpty(eventData.Text))
            {
                if (output.Length + eventData.Text.Length > 16 * 1024)
                    throw new InvalidDataException("Codex intent response exceeded 16 KiB.");
                output.Append(eventData.Text);
            }
            else if (eventData.Kind == AgentRuntimeEventKind.TurnCompleted &&
                     string.Equals(ReadNestedString(message.Parameters, "turn", "id"), turnId, StringComparison.Ordinal))
            {
                if (eventData.Status != "completed")
                    throw new InvalidOperationException(eventData.Text ?? "Codex intent interpretation did not complete.");
                return AgentUserIntentInterpreter.Parse(output.ToString());
            }
        }

        throw new EndOfStreamException("Codex App Server closed before intent interpretation completed.");
    }

    private static string BuildPrompt(string userText, string currentScreen) => $$"""
You are the natural-language intent adapter for the Agent mobile app. Reuse your built-in language understanding; do not invent tools or capabilities and do not ask the user to fill a form.

Classify the user's utterance into exactly one supported intent:
- navigate: only when the user clearly asks to open a supported Agent screen. Destination must be one of chat, agents, projects, history, context, plugins, settings.
- explain_current_screen: the user asks what the current screen or a visible feature means.
- create_aisto_draft: the user asks to create/configure a specialist; put a concise 12–4000 character AiSTo brief in brief.
- unsupported: everything else, ambiguous navigation, external actions, repository writes, deletion, publishing, purchases, sending messages, or requests that are not supported by these intents.

Never execute the request. Never claim an operation has happened. Never return a URL, code, command, arbitrary route or permission grant. A create_aisto_draft result is only a draft proposal and must be handled by the separate read-only AiSTo assistant. The local app, not you, controls confirmations.

Current Agent screen: {{currentScreen}}
User utterance: {{userText}}

Return only JSON matching the provided schema. For fields not used by the selected intent, return null. Keep explanations short and in the user's language.
""";

    private static bool BelongsToThread(JsonElement parameters, string expectedThreadId)
    {
        if (!parameters.TryGetProperty("threadId", out var threadId)) return true;
        return threadId.ValueKind == JsonValueKind.String && threadId.GetString() == expectedThreadId;
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

    private static JsonElement CreateOutputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "intent": { "type": "string", "enum": ["navigate", "explain_current_screen", "create_aisto_draft", "unsupported"] },
            "destination": { "type": ["string", "null"], "enum": ["chat", "agents", "projects", "history", "context", "plugins", "settings", null] },
            "brief": { "type": ["string", "null"], "minLength": 12, "maxLength": 4000 },
            "explanation": { "type": ["string", "null"], "maxLength": 600 }
          },
          "required": ["intent", "destination", "brief", "explanation"]
        }
        """);
        return document.RootElement.Clone();
    }
}
