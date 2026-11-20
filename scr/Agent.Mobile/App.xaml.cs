using Microsoft.Maui.Graphics;
using Agent.Mobile.Services;
using Microsoft.Maui.Controls;

namespace Agent.Mobile;

public partial class App : Application
{
    private string _themePreference = "Escuro";

    public App(IAgentSettingsStore settingsStore)
    {
        InitializeComponent();
        ApplyThemePreference(settingsStore.Load().ThemeMode);
        RequestedThemeChanged += OnRequestedThemeChanged;
        MainPage = new Pages.WelcomePage();
    }

    public void ApplyThemePreference(string themePreference)
    {
        _themePreference = themePreference;
        UserAppTheme = themePreference switch
        {
            "Sistema" => AppTheme.Unspecified,
            "Claro" => AppTheme.Light,
            _ => AppTheme.Dark
        };

        var resolvedTheme = UserAppTheme == AppTheme.Unspecified ? RequestedTheme : UserAppTheme;
        ApplyPalette(resolvedTheme);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        if (string.Equals(_themePreference, "Sistema", StringComparison.Ordinal))
            ApplyPalette(e.RequestedTheme);
    }

    private void ApplyPalette(AppTheme theme)
    {
        var palette = theme == AppTheme.Light ? LightPalette : DarkPalette;
        foreach (var (key, value) in palette)
            Resources[key] = Color.FromArgb(value);
    }

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#0D1016", ["Surface"] = "#151A23", ["SurfaceRaised"] = "#1B2230",
        ["SurfaceSubtle"] = "#202938", ["Border"] = "#2A3342", ["TextPrimary"] = "#F1F4F9",
        ["TextSecondary"] = "#A4AFBF", ["TextMuted"] = "#758195", ["Accent"] = "#A8A5FF",
        ["AccentStrong"] = "#7772E8", ["AccentSurface"] = "#262541", ["Success"] = "#71D6A1",
        ["Warning"] = "#E8C27A", ["Danger"] = "#F08B91", ["CodeSurface"] = "#10151D"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#F4F6FA", ["Surface"] = "#FFFFFF", ["SurfaceRaised"] = "#E9EDF4",
        ["SurfaceSubtle"] = "#DEE5EF", ["Border"] = "#D6DDE8", ["TextPrimary"] = "#18202C",
        ["TextSecondary"] = "#526176", ["TextMuted"] = "#718096", ["Accent"] = "#5E57C8",
        ["AccentStrong"] = "#5149BD", ["AccentSurface"] = "#ECEBFF", ["Success"] = "#18794E",
        ["Warning"] = "#9B6A10", ["Danger"] = "#B42332", ["CodeSurface"] = "#F0F2F7"
    };
}
