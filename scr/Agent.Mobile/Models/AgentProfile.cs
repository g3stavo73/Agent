using System.Text.Json.Serialization;

namespace Agent.Mobile.Models;

/// <summary>
/// Local representation of an Agent profile. The profile remains a Codex profile;
/// AiSTo is an optional configuration layer and does not represent another model.
/// </summary>
public sealed class AgentProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AvatarPath { get; set; }
    public string Engine { get; set; } = "codex";

    public bool CanReadRepositories { get; set; } = true;
    public bool CanWriteRepositories { get; set; }
    public bool CanBrowseWeb { get; set; } = true;
    public bool UseSemanticNavigation { get; set; } = true;
    public bool PreferUpdatedDocumentation { get; set; } = true;
    public bool ApplyArchitectureRules { get; set; } = true;

    public AiStoSpecialization? AiSto { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath);

    [JsonIgnore]
    public bool IsAiStoSpecialized => AiSto is not null;

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var words = (Name ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (words.Length == 0)
                return "AG";

            if (words.Length == 1)
                return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();

            return $"{words[0][0]}{words[^1][0]}".ToUpperInvariant();
        }
    }

    [JsonIgnore]
    public string CapabilitiesSummary
    {
        get
        {
            var capabilities = new List<string>();
            if (CanReadRepositories)
                capabilities.Add("leitura");
            if (CanWriteRepositories)
                capabilities.Add("alterações");
            if (CanBrowseWeb)
                capabilities.Add("web");

            return capabilities.Count == 0
                ? "sem ferramentas"
                : string.Join(" · ", capabilities);
        }
    }

    [JsonIgnore]
    public string ContextSummary
    {
        get
        {
            var features = new List<string>();
            if (UseSemanticNavigation)
                features.Add("navegação semântica");
            if (PreferUpdatedDocumentation)
                features.Add("documentação atualizada");
            if (ApplyArchitectureRules)
                features.Add("regras de arquitetura");

            return features.Count == 0
                ? "contexto padrão"
                : string.Join(" · ", features);
        }
    }
}
