
using System.Text.Json;

namespace Agent.Core.Protocol;

/// <summary>
/// An App Server notification or server-initiated JSON-RPC request.
/// RequestId is absent for notifications and preserves the original JSON id
/// (numeric or string) for requests that the client must answer.
/// </summary>
public sealed record CodexServerMessage(
    string Method,
    JsonElement Parameters,
    JsonElement? RequestId = null)
{
    public bool RequiresResponse => RequestId.HasValue;
}

/// <summary>
/// Explicit user decision for a Codex approval request.
/// No value represents auto-approval.
/// </summary>
public enum CodexApprovalDecision
{
    Accept,
    AcceptForSession,
    Decline,
    Cancel
}

public sealed record CodexDeviceCodeLogin(
    string LoginId,
    Uri VerificationUri,
    string UserCode);

/// <summary>
/// Represents an error returned by the Codex JSON-RPC server.
/// </summary>
public sealed class CodexRpcException : Exception
{
    public CodexRpcException(
        long code,
        string message,
        JsonElement? data = null)
        : base($"Codex App Server error {code}: {message}")
    {
        Code = code;
        RpcMessage = message;
        RpcData = data;
    }

    public long Code { get; }

    public string RpcMessage { get; }

    /// <summary>
    /// Additional structured data supplied by the JSON-RPC server.
    /// </summary>
    public JsonElement? RpcData { get; }
}
