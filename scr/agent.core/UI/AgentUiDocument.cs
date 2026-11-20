using System.Text.Json;

namespace Agent.Core.Ui;

/// <summary>
/// Safe, versioned, declarative UI payload. The model can describe only these
/// bounded presentation blocks; it cannot supply XAML, HTML, executable code or URLs.
/// </summary>
public sealed record AgentUiDocument(
    int SchemaVersion,
    string? Title,
    IReadOnlyList<AgentUiBlock>? Blocks);

public sealed record AgentUiBlock(
    string Type,
    string? Title = null,
    string? Text = null,
    string? Language = null,
    IReadOnlyList<AgentUiKeyValue>? Values = null,
    IReadOnlyList<string>? Items = null,
    string? Status = null,
    string? ActionId = null);

public sealed record AgentUiKeyValue(string Key, string Value);

public sealed record AgentUiValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static AgentUiValidationResult Valid { get; } = new(true, Array.Empty<string>());
}

public static class AgentUiDocumentValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxBlocks = 32;
    public const int MaxTextLength = 8_000;
    private static readonly HashSet<string> BlockTypes = new(StringComparer.Ordinal)
    {
        "text", "card", "code", "status", "keyValue", "list", "checklist", "progress", "action"
    };
    private static readonly HashSet<string> ActionIds = new(StringComparer.Ordinal)
    {
        "navigate.chat", "navigate.agents", "navigate.projects", "navigate.history",
        "navigate.context", "navigate.plugins", "navigate.settings"
    };
    private static readonly HashSet<string> CodeLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "json", "csharp", "rust", "typescript", "javascript", "python", "sql", "bash", "xml"
    };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "success", "warning", "error", "pending", "running", "completed", "failed"
    };

    public static AgentUiValidationResult Validate(AgentUiDocument? document)
    {
        var errors = new List<string>();
        if (document is null) return new(false, new[] { "The UI document is required." });
        if (document.SchemaVersion != CurrentSchemaVersion)
            errors.Add($"Unsupported UI schema version. Expected {CurrentSchemaVersion}.");
        if (Count(document.Title) > 120) errors.Add("The UI title cannot exceed 120 characters.");
        if (document.Blocks is null || document.Blocks.Count is < 1 or > MaxBlocks)
            errors.Add($"The UI document must contain between 1 and {MaxBlocks} blocks.");

        if (document.Blocks is not null)
        {
            for (var index = 0; index < document.Blocks.Count; index++)
            {
                var block = document.Blocks[index];
                var prefix = $"blocks[{index}]";
                if (block is null) { errors.Add($"{prefix} cannot be null."); continue; }
                if (!BlockTypes.Contains(block.Type ?? string.Empty))
                    errors.Add($"{prefix}.type is not supported.");
                if (Count(block.Title) > 120) errors.Add($"{prefix}.title is too long.");
                if (Count(block.Text) > MaxTextLength) errors.Add($"{prefix}.text is too long.");
                if (block.Type == "code")
                {
                    var language = string.IsNullOrWhiteSpace(block.Language) ? "text" : block.Language;
                    if (!CodeLanguages.Contains(language)) errors.Add($"{prefix}.language is not supported.");
                }
                if (block.Status is not null && !Statuses.Contains(block.Status))
                    errors.Add($"{prefix}.status is not supported.");
                if (block.Values is { Count: > 12 }) errors.Add($"{prefix}.values cannot contain more than 12 entries.");
                if (block.Items is { Count: > 30 }) errors.Add($"{prefix}.items cannot contain more than 30 entries.");
                if (block.Values is not null)
                {
                    foreach (var pair in block.Values)
                        if (pair is null || Count(pair.Key) > 80 || Count(pair.Value) > 1_000)
                            errors.Add($"{prefix}.values contains an invalid entry.");
                }
                if (block.Items is not null && block.Items.Any(item => item is null || Count(item) > 1_000))
                    errors.Add($"{prefix}.items contains an invalid entry.");
                if (block.Type == "action" && !ActionIds.Contains(block.ActionId ?? string.Empty))
                    errors.Add($"{prefix}.actionId is not allowlisted.");
                if (block.Type != "action" && block.ActionId is not null)
                    errors.Add($"{prefix}.actionId is only valid for action blocks.");
            }
        }

        return errors.Count == 0 ? AgentUiValidationResult.Valid : new(false, errors);
    }

    private static int Count(string? value) => value?.EnumerateRunes().Count() ?? 0;
}

public sealed record AgentUiParseResult(bool Success, AgentUiDocument? Document, IReadOnlyList<string> Errors);

public static class AgentUiDocumentParser
{
    private const int MaxPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 12
    };

    /// <summary>Accepts raw JSON or one ```agent-ui / ```json fenced payload.</summary>
    public static AgentUiParseResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new(false, null, new[] { "The assistant response is empty." });

        string candidate = ExtractPayload(response.Trim());
        if (System.Text.Encoding.UTF8.GetByteCount(candidate) > MaxPayloadBytes)
            return new(false, null, new[] { "The declarative UI payload exceeds 64 KiB." });

        try
        {
            var document = JsonSerializer.Deserialize<AgentUiDocument>(candidate, Options);
            var validation = AgentUiDocumentValidator.Validate(document);
            return validation.IsValid
                ? new(true, document, Array.Empty<string>())
                : new(false, null, validation.Errors);
        }
        catch (JsonException)
        {
            return new(false, null, new[] { "The assistant did not return valid declarative UI JSON." });
        }
    }

    private static string ExtractPayload(string value)
    {
        foreach (var marker in new[] { "```agent-ui", "```json" })
        {
            var start = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            start += marker.Length;
            var end = value.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return value[start..end].Trim();
        }
        return value;
    }
}
