using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Api.Data;

namespace Agent.Api.Contracts;

public sealed record AgentProfileRequest(
    string Name,
    string Description,
    string? AvatarReference,
    string Engine,
    bool CanReadRepositories,
    bool CanWriteRepositories,
    bool CanBrowseWeb,
    bool UseSemanticNavigation,
    bool PreferUpdatedDocumentation,
    bool ApplyArchitectureRules,
    AiStoSpecializationRequest? AiSto);

public sealed record AgentProfileResponse(
    Guid Id,
    string Name,
    string Description,
    string? AvatarReference,
    string Engine,
    bool CanReadRepositories,
    bool CanWriteRepositories,
    bool CanBrowseWeb,
    bool UseSemanticNavigation,
    bool PreferUpdatedDocumentation,
    bool ApplyArchitectureRules,
    AiStoSpecializationRequest? AiSto,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static AgentProfileResponse FromRecord(AgentProfileRecord record)
    {
        AiStoSpecializationRequest? config = null;
        if (!string.IsNullOrWhiteSpace(record.AiStoConfiguration))
            config = JsonSerializer.Deserialize<AiStoSpecializationRequest>(record.AiStoConfiguration, JsonOptions.Default);
        return new AgentProfileResponse(record.Id, record.Name, record.Description,
            record.AvatarReference, record.Engine, record.CanReadRepositories,
            record.CanWriteRepositories, record.CanBrowseWeb, record.UseSemanticNavigation,
            record.PreferUpdatedDocumentation, record.ApplyArchitectureRules, config,
            record.CreatedAtUtc, record.UpdatedAtUtc);
    }
}

public sealed class AiStoSpecializationRequest
{
    public int SchemaVersion { get; set; } = 1;
    public string Domain { get; set; } = "Grandes projetos e sistemas complexos";
    public string Objective { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ProjectContext { get; set; } = string.Empty;
    public List<string> DocumentationSources { get; set; } = [];
    public string ResponseStyle { get; set; } = "StepByStep";

    public IReadOnlyList<string> ValidateAndNormalize()
    {
        var errors = new List<string>();
        Domain = NormalizeText(Domain);
        Objective = NormalizeText(Objective);
        Instructions = NormalizeText(Instructions);
        ProjectContext = NormalizeText(ProjectContext);

        if (SchemaVersion != 1) errors.Add("Only AiSTo schema version 1 is supported.");
        if (string.IsNullOrWhiteSpace(Domain)) errors.Add("Domain is required.");
        if (CharacterCount(Domain) > 80) errors.Add("Domain cannot exceed 80 characters.");
        if (CharacterCount(Objective) > 1600) errors.Add("Objective cannot exceed 1600 characters.");
        if (CharacterCount(Instructions) > 8000) errors.Add("Instructions cannot exceed 8000 characters.");
        if (CharacterCount(ProjectContext) > 5000) errors.Add("ProjectContext cannot exceed 5000 characters.");
        if (!new[] { "StepByStep", "Concise", "Detailed", "CriticalReview" }.Contains(ResponseStyle, StringComparer.Ordinal))
            errors.Add("ResponseStyle is not supported.");

        var suppliedSources = DocumentationSources ?? [];
        if (suppliedSources.Count > 20)
            errors.Add("No more than 20 documentation sources may be configured.");

        var normalizedSources = new List<string>();
        foreach (var value in suppliedSources.Take(20))
        {
            var candidate = value?.Trim() ?? string.Empty;
            if (CharacterCount(candidate) > 2048 ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                errors.Add("Documentation sources must be HTTPS URLs without credentials and no longer than 2048 characters.");
                continue;
            }
            var normalized = uri.AbsoluteUri;
            if (!normalizedSources.Contains(normalized, StringComparer.Ordinal))
                normalizedSources.Add(normalized);
        }
        DocumentationSources = normalizedSources;
        return errors;
    }

    public static int CharacterCount(string? value)
    {
        var count = 0;
        foreach (var _ in (value ?? string.Empty).EnumerateRunes()) count++;
        return count;
    }

    private static string NormalizeText(string? value) => (value ?? string.Empty).Trim();
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
