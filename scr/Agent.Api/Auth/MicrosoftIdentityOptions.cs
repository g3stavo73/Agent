namespace Agent.Api.Auth;

public sealed class MicrosoftIdentityOptions
{
    public const string SectionName = "MicrosoftIdentity";
    public string ApiAudience { get; init; } = string.Empty;
    public string RequiredScope { get; init; } = "Agent.Access";
}
