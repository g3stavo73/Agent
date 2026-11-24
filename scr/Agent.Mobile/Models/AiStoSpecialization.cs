using System.Text;

namespace Agent.Mobile.Models;

/// <summary>
/// Versioned AiSTo configuration stored with a Codex agent profile.
/// Property names and response-style values intentionally match the API contract.
/// </summary>
public sealed class AiStoSpecialization
{
    private static readonly HashSet<string> SupportedResponseStyles = new(StringComparer.Ordinal)
    {
        "StepByStep", "Concise", "Detailed", "CriticalReview"
    };

    public int SchemaVersion { get; set; } = 1;
    public string Domain { get; set; } = "Grandes projetos e sistemas complexos";
    public string Objective { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ProjectContext { get; set; } = string.Empty;
    public List<string> DocumentationSources { get; set; } = new();
    public string ResponseStyle { get; set; } = "StepByStep";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Normalizes data loaded from local JSON so legacy or malformed values do not
    /// prevent the profile list from opening. Limits mirror the API contract.
    /// </summary>
    public void Normalize()
    {
        SchemaVersion = 1;
        Domain = Limit(Domain, 80);
        if (Domain.Length == 0)
            Domain = "Grandes projetos e sistemas complexos";

        Objective = Limit(Objective, 1600);
        Instructions = Limit(Instructions, 8000);
        ProjectContext = Limit(ProjectContext, 5000);

        if (!SupportedResponseStyles.Contains(ResponseStyle ?? string.Empty))
            ResponseStyle = "StepByStep";

        var normalizedSources = new List<string>();
        foreach (var source in DocumentationSources ?? new List<string>())
        {
            var candidate = Limit(source, 2048);
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                continue;
            }

            var normalized = uri.AbsoluteUri;
            if (!normalizedSources.Contains(normalized, StringComparer.Ordinal))
                normalizedSources.Add(normalized);

            if (normalizedSources.Count == 20)
                break;
        }

        DocumentationSources = normalizedSources;

        if (CreatedAtUtc == default)
            CreatedAtUtc = DateTimeOffset.UtcNow;
        if (UpdatedAtUtc == default)
            UpdatedAtUtc = CreatedAtUtc;
    }

    private static string Limit(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (count++ >= maximumLength)
                break;
            builder.Append(rune.ToString());
        }
        return count <= maximumLength ? normalized : builder.ToString();
    }
}
