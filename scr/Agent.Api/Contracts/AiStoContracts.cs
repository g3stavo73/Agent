namespace Agent.Api.Contracts;

public sealed record AiStoCompileRequest(
    string AgentName,
    AiStoSpecializationRequest Configuration,
    AgentCapabilityRequest Capabilities);

public sealed record AgentCapabilityRequest(
    bool CanReadRepositories,
    bool CanWriteRepositories,
    bool CanBrowseWeb,
    bool UseSemanticNavigation,
    bool PreferUpdatedDocumentation,
    bool ApplyArchitectureRules);

public sealed record AiStoCompileResponse(
    string RuntimeStatus,
    string SystemInstruction,
    IReadOnlyList<string> DocumentationSources,
    AgentCapabilityRequest RequestedCapabilities,
    string Notice);

internal sealed record WorkerRequest(
    string RequestId,
    string Operation,
    string AgentName,
    AiStoSpecializationRequest Configuration,
    AgentCapabilityRequest Capabilities);

internal sealed record WorkerResponse(
    string RequestId,
    bool Ok,
    string? SystemInstruction,
    IReadOnlyList<string>? DocumentationSources,
    AgentCapabilityRequest? RequestedCapabilities,
    string? Error);
