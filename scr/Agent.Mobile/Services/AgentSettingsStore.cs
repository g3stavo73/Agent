using Agent.Mobile.Models;
using Microsoft.Maui.Storage;

namespace Agent.Mobile.Services;

/// <summary>
/// Stores non-secret app preferences with the platform Preferences API.
/// Authentication credentials belong in SecureStorage, not here.
/// </summary>
public sealed class AgentSettingsStore : IAgentSettingsStore
{
    private const string ThemeKey = "agent.settings.theme.v1";
    private const string ResponseStyleKey = "agent.settings.response-style.v1";
    private const string ApiBaseUrlKey = "agent.settings.api-base-url.v1";

    private static readonly HashSet<string> ThemeModes = new(StringComparer.Ordinal)
    {
        "Sistema", "Escuro", "Claro"
    };

    private static readonly HashSet<string> ResponseStyles = new(StringComparer.Ordinal)
    {
        "Passo a passo", "Objetivo e conciso", "Detalhado e explicativo", "Revisão crítica"
    };

    public AgentSettings Load()
    {
        var defaults = AgentSettings.Defaults;
        var theme = Preferences.Default.Get(ThemeKey, defaults.ThemeMode);
        var responseStyle = Preferences.Default.Get(ResponseStyleKey, defaults.DefaultResponseStyle);
        var apiBaseUrl = Preferences.Default.Get(ApiBaseUrlKey, defaults.ApiBaseUrl);

        string normalizedApiBaseUrl;
        try
        {
            normalizedApiBaseUrl = NormalizeApiBaseUrl(apiBaseUrl);
        }
        catch (ArgumentException)
        {
            // A corrupt or legacy preference must not prevent the app from starting.
            normalizedApiBaseUrl = string.Empty;
        }

        return new AgentSettings(
            ThemeModes.Contains(theme) ? theme : defaults.ThemeMode,
            ResponseStyles.Contains(responseStyle) ? responseStyle : defaults.DefaultResponseStyle,
            normalizedApiBaseUrl);
    }

    public void Save(AgentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var theme = ThemeModes.Contains(settings.ThemeMode)
            ? settings.ThemeMode
            : AgentSettings.Defaults.ThemeMode;
        var responseStyle = ResponseStyles.Contains(settings.DefaultResponseStyle)
            ? settings.DefaultResponseStyle
            : AgentSettings.Defaults.DefaultResponseStyle;
        var apiBaseUrl = NormalizeApiBaseUrl(settings.ApiBaseUrl);

        Preferences.Default.Set(ThemeKey, theme);
        Preferences.Default.Set(ResponseStyleKey, responseStyle);
        Preferences.Default.Set(ApiBaseUrlKey, apiBaseUrl);
    }

    public void Reset()
    {
        Preferences.Default.Remove(ThemeKey);
        Preferences.Default.Remove(ResponseStyleKey);
        Preferences.Default.Remove(ApiBaseUrlKey);
    }

    public static string NormalizeApiBaseUrl(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0)
            return string.Empty;

        if (candidate.Length > 512 || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            throw new ArgumentException("Informe uma URL absoluta válida para a API.", nameof(value));

        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "A URL da API precisa usar HTTPS e não pode conter credenciais, parâmetros ou fragmentos.",
                nameof(value));
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }
}
