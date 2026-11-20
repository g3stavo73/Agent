using System.Text.Json;

namespace Agent.Core.AiSto;

public static class AiStoDraftParser
{
    private const int MaxResponseBytes = 48 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 12
    };

    public static AiStoDraftParseResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return Failure("The assistant returned an empty draft.");

        var json = ExtractJson(response.Trim());
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxResponseBytes)
            return Failure("The specialization draft exceeds 48 KiB.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            var profile = JsonSerializer.Deserialize<AiStoDraftProfile>(root.GetRawText(), Options);
            if (profile is null) return Failure("The specialization draft is empty.");
            var errors = Validate(profile);
            return errors.Count == 0
                ? new(true, profile with
                {
                    DocumentationSources = (profile.DocumentationSources ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).Take(20).ToArray(),
                    Assumptions = (profile.Assumptions ?? Array.Empty<string>()).Take(8).ToArray(),
                    Evidence = (profile.Evidence ?? Array.Empty<string>()).Take(12).ToArray()
                }, Array.Empty<string>())
                : new(false, null, errors);
        }
        catch (JsonException)
        {
            return Failure("The assistant response is not valid specialization JSON.");
        }
    }

    private static List<string> Validate(AiStoDraftProfile profile)
    {
        var errors = new List<string>();
        Check(profile.Name, 1, 80, "name", errors);
        Check(profile.Description, 0, 240, "description", errors);
        Check(profile.Domain, 1, 80, "domain", errors);
        Check(profile.Objective, 20, 1_600, "objective", errors);
        Check(profile.Instructions, 30, 8_000, "instructions", errors);
        Check(profile.ProjectContext, 0, 5_000, "projectContext", errors);
        if (profile.ResponseStyle is not ("StepByStep" or "Concise" or "Detailed" or "CriticalReview"))
            errors.Add("responseStyle is not supported.");
        if ((profile.DocumentationSources?.Count ?? 0) > 20) errors.Add("No more than 20 documentation sources are allowed.");
        foreach (var value in profile.DocumentationSources ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2_048 ||
                !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
                errors.Add("Documentation sources must be HTTPS URLs without embedded credentials.");
        }
        if (profile.CanWriteRepositories && !profile.CanReadRepositories)
            errors.Add("Repository write requires repository read.");
        if ((profile.Assumptions?.Count ?? 0) > 8 || (profile.Evidence?.Count ?? 0) > 12)
            errors.Add("The draft contains too many assumptions or evidence entries.");
        if ((profile.Assumptions ?? Array.Empty<string>()).Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 500) ||
            (profile.Evidence ?? Array.Empty<string>()).Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 500))
            errors.Add("Assumptions and evidence entries must contain 1–500 characters.");
        return errors;
    }

    private static void Check(string? value, int minimum, int maximum, string field, List<string> errors)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < minimum || length > maximum)
            errors.Add($"{field} must contain between {minimum} and {maximum} characters.");
    }

    private static string ExtractJson(string value)
    {
        var fenceStart = value.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            var start = fenceStart + 7;
            var end = value.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return value[start..end].Trim();
        }
        var first = value.IndexOf('{');
        var last = value.LastIndexOf('}');
        return first >= 0 && last > first ? value[first..(last + 1)] : value;
    }

    private static AiStoDraftParseResult Failure(string message) => new(false, null, new[] { message });
}
