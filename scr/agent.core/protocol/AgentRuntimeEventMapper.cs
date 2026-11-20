using System.Text.Json;

namespace Agent.Core.Protocol;

public enum AgentRuntimeEventKind
{
    Unknown, TurnStarted, TurnCompleted, AssistantTextDelta, PlanUpdated,
    ToolStarted, ToolCompleted, CommandApprovalRequired, FileChangeApprovalRequired, Warning
}

/// <summary>Application-facing representation of stable Codex App Server events.</summary>
public sealed record AgentRuntimeEvent(
    AgentRuntimeEventKind Kind,
    string? Text = null,
    string? Status = null,
    string? ItemId = null,
    bool RequiresUserDecision = false,
    JsonElement? OriginalParameters = null);

public static class AgentRuntimeEventMapper
{
    public static AgentRuntimeEvent Map(CodexServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var parameters = message.Parameters;
        var itemId = ReadString(parameters, "itemId") ?? ReadNestedString(parameters, "item", "id");

        return message.Method switch
        {
            "turn/started" => new(AgentRuntimeEventKind.TurnStarted, Status: "running", OriginalParameters: parameters),
            "turn/completed" => new(AgentRuntimeEventKind.TurnCompleted,
                Status: ReadNestedString(parameters, "turn", "status") ?? "unknown",
                Text: ReadNestedString(parameters, "turn", "error", "message"), OriginalParameters: parameters),
            "item/agentMessage/delta" => new(AgentRuntimeEventKind.AssistantTextDelta,
                Text: ReadString(parameters, "delta"), ItemId: itemId, OriginalParameters: parameters),
            "turn/plan/updated" => new(AgentRuntimeEventKind.PlanUpdated,
                Text: ReadString(parameters, "explanation"), OriginalParameters: parameters),
            "item/started" => new(AgentRuntimeEventKind.ToolStarted,
                Text: ReadNestedString(parameters, "item", "type"), ItemId: itemId, OriginalParameters: parameters),
            "item/completed" => new(AgentRuntimeEventKind.ToolCompleted,
                Text: ReadNestedString(parameters, "item", "type"), ItemId: itemId, OriginalParameters: parameters),
            "item/commandExecution/requestApproval" => new(AgentRuntimeEventKind.CommandApprovalRequired,
                Text: "Codex requested permission to run a command.", RequiresUserDecision: true, OriginalParameters: parameters),
            "item/fileChange/requestApproval" => new(AgentRuntimeEventKind.FileChangeApprovalRequired,
                Text: "Codex requested permission to apply file changes.", RequiresUserDecision: true, OriginalParameters: parameters),
            "warning" or "configWarning" => new(AgentRuntimeEventKind.Warning,
                Text: ReadString(parameters, "message") ?? ReadString(parameters, "summary"), OriginalParameters: parameters),
            _ => new(AgentRuntimeEventKind.Unknown, ItemId: itemId, OriginalParameters: parameters)
        };
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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
