namespace Agent.Core.AiSto;

/// <summary>The one-piece brief supplied by the user. Other profile fields are inferred.</summary>
public sealed record AiStoDraftRequest(string Brief, string? WorkspacePath = null, string? ExistingContext = null);

public sealed record AiStoDraftProfile(
    string Name,
    string Description,
    string Domain,
    string Objective,
    string Instructions,
    string ProjectContext,
    IReadOnlyList<string> DocumentationSources,
    string ResponseStyle,
    bool CanReadRepositories,
    bool CanWriteRepositories,
    bool CanBrowseWeb,
    bool UseSemanticNavigation,
    bool PreferUpdatedDocumentation,
    bool ApplyArchitectureRules,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Evidence);

public sealed record AiStoDraftParseResult(bool Success, AiStoDraftProfile? Profile, IReadOnlyList<string> Errors);

public static class AiStoDraftPromptBuilder
{
    public static string Build(AiStoDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brief = (request.Brief ?? string.Empty).Trim();
        if (brief.Length is < 12 or > 4_000)
            throw new ArgumentException("Describe the desired specialist in 12–4000 characters.", nameof(request));

        var workspace = Limit(request.WorkspacePath, 1_000);
        var context = Limit(request.ExistingContext, 4_000);
        return $$"""
You are the AiSTo specialization designer inside Agent. AiSTo configures a Codex-based specialist; it is not a separate model.

GOAL
Construct a useful first draft from the user's short brief. Do not make the user fill every field and do not ask follow-up questions. Infer reasonable defaults, mark assumptions, and leave optional details empty when evidence is unavailable.

RESEARCH AND REUSE RULES
- First inspect available workspace context and relevant files if Codex tools and a workspace are actually available. Do not claim to have inspected a repository if no repository tool/context exists.
- Consult current, official documentation that is directly relevant to the requested stack or domain when web/documentation tools are available. Prefer existing configured runtime tools and sources.
- Check what Codex already provides. Do not duplicate native natural-language understanding, semantic navigation, web research, or documentation tools when the connected runtime already provides the capability. Record only complementary, verified project-specific rules in the AiSTo profile.
- If a capability is unavailable, describe it as unavailable or requested, never as granted.
- Do not modify files, execute repository writes, publish changes, or save the profile. This task only drafts configuration.
- Never invent repository facts, versions, documentation references, benchmark results, or permissions. Use HTTPS documentation URLs only.
- Avoid asking the user questions. Make a conservative proposal and list assumptions/evidence.

USER BRIEF
{{brief}}

AVAILABLE WORKSPACE PATH (may be empty)
{{workspace}}

EXISTING CONTEXT (may be empty)
{{context}}

OUTPUT CONTRACT
Return exactly one JSON object, with no markdown fence and no extra prose, matching this shape:
{
  "name": "short profile name",
  "description": "one-sentence description",
  "domain": "up to 80 characters",
  "objective": "clear measurable goal, up to 1600 characters",
  "instructions": "useful operating instructions, up to 8000 characters",
  "projectContext": "verified context only, up to 5000 characters",
  "documentationSources": ["https://..."],
  "responseStyle": "StepByStep|Concise|Detailed|CriticalReview",
  "canReadRepositories": true,
  "canWriteRepositories": false,
  "canBrowseWeb": false,
  "useSemanticNavigation": false,
  "preferUpdatedDocumentation": true,
  "applyArchitectureRules": true,
  "assumptions": ["..."],
  "evidence": ["..." ]
}

Use no more than 20 documentationSources, 8 assumptions, and 12 evidence entries. Set capability booleans to true only where the connected runtime exposes the capability; they remain preferences, not permission grants. canWriteRepositories requires canReadRepositories. Keep the default write preference false. Return valid JSON.
""";
    }

    private static string Limit(string? value, int length)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= length ? normalized : normalized[..length];
    }
}
