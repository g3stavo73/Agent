using System.Text.Json;

namespace Agent.Core.Protocol;

/// <summary>Optional documented settings for one Codex App Server turn.</summary>
public sealed record CodexTurnOptions(
    string? ApprovalPolicy = null,
    object? SandboxPolicy = null,
    JsonElement? OutputSchema = null);
