namespace Agent.Mobile.Models;

/// <summary>
/// Non-secret app preferences. Authentication material must remain in SecureStorage.
/// </summary>
public sealed record AgentSettings(
    string ThemeMode,
    string DefaultResponseStyle,
    string ApiBaseUrl)
{
    public static AgentSettings Defaults { get; } = new(
        ThemeMode: "Sistema",
        DefaultResponseStyle: "Passo a passo",
        ApiBaseUrl: string.Empty);
}
