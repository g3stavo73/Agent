using Microsoft.Maui.Graphics;
using Agent.Mobile.Models;
using Agent.Mobile.Services;
using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly IAgentSettingsStore _settingsStore;
    private readonly IAgentApiClient _apiClient;
    private bool _isLoading;

    public SettingsPage(IAgentSettingsStore settingsStore, IAgentApiClient apiClient)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        _apiClient = apiClient;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BackButton.IsVisible = Shell.Current.CurrentState.Location.OriginalString
            .Contains("settings-detail", StringComparison.OrdinalIgnoreCase);
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            var settings = _settingsStore.Load();
            ThemePicker.SelectedItem = settings.ThemeMode;
            ResponseStylePicker.SelectedItem = settings.DefaultResponseStyle;
            ApiBaseUrlEntry.Text = settings.ApiBaseUrl;
            ApiUrlStatusLabel.IsVisible = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_isLoading || ThemePicker.SelectedItem is not string theme)
            return;

        var current = _settingsStore.Load();
        _settingsStore.Save(current with { ThemeMode = theme });
        ApplyTheme(theme);
    }

    private void OnResponseStyleChanged(object? sender, EventArgs e)
    {
        if (_isLoading || ResponseStylePicker.SelectedItem is not string style)
            return;

        var current = _settingsStore.Load();
        _settingsStore.Save(current with { DefaultResponseStyle = style });
    }

    private async void OnSaveApiUrlClicked(object? sender, EventArgs e)
    {
        try
        {
            var normalized = AgentSettingsStore.NormalizeApiBaseUrl(ApiBaseUrlEntry.Text);
            var current = _settingsStore.Load();
            _settingsStore.Save(current with { ApiBaseUrl = normalized });
            ApiBaseUrlEntry.Text = normalized;
            ApiUrlStatusLabel.Text = normalized.Length == 0
                ? "Endereço removido. A API ainda não está conectada."
                : "Endereço salvo neste dispositivo. A conexão ainda não está ativa.";
            ApiUrlStatusLabel.TextColor = normalized.Length == 0
                ? (Color)Application.Current!.Resources["TextSecondary"]
                : (Color)Application.Current!.Resources["Success"];
            ApiUrlStatusLabel.IsVisible = true;
        }
        catch (ArgumentException ex)
        {
            await DisplayAlert("Endereço inválido", ex.Message, "OK");
            ApiBaseUrlEntry.Focus();
        }
    }

    private async void OnTestApiConnectionClicked(object? sender, EventArgs e)
    {
        TestApiConnectionButton.IsEnabled = false;
        ApiUrlStatusLabel.IsVisible = true;
        ApiUrlStatusLabel.TextColor = (Color)Application.Current!.Resources["TextSecondary"];
        ApiUrlStatusLabel.Text = "Testando liveness e readiness...";
        try
        {
            var normalized = AgentSettingsStore.NormalizeApiBaseUrl(ApiBaseUrlEntry.Text);
            if (normalized.Length == 0)
                throw new ArgumentException("Informe e salve um endereço HTTPS antes do teste.");

            var current = _settingsStore.Load();
            if (!string.Equals(normalized, current.ApiBaseUrl, StringComparison.Ordinal))
            {
                _settingsStore.Save(current with { ApiBaseUrl = normalized });
                ApiBaseUrlEntry.Text = normalized;
            }

            var result = await _apiClient.CheckHealthAsync(normalized);
            ApiUrlStatusLabel.Text = result.Message;
            ApiUrlStatusLabel.TextColor = result.IsReady
                ? (Color)Application.Current!.Resources["Success"]
                : (Color)Application.Current!.Resources["Warning"];
        }
        catch (OperationCanceledException)
        {
            ApiUrlStatusLabel.Text = "Teste cancelado.";
            ApiUrlStatusLabel.TextColor = (Color)Application.Current!.Resources["Warning"];
        }
        catch (HttpRequestException ex)
        {
            ApiUrlStatusLabel.Text = $"Falha de rede/TLS: {ex.Message}";
            ApiUrlStatusLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
        }
        catch (Exception ex)
        {
            ApiUrlStatusLabel.Text = ex.Message;
            ApiUrlStatusLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
        }
        finally
        {
            TestApiConnectionButton.IsEnabled = true;
        }
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Restaurar configurações",
            "O tema, o estilo padrão de resposta e o endereço da API voltarão aos valores iniciais. Os perfis de agentes não serão apagados.",
            "Restaurar", "Cancelar");
        if (!confirmed)
            return;

        _settingsStore.Reset();
        LoadSettings();
        ApplyTheme(AgentSettings.Defaults.ThemeMode);
        await DisplayAlert("Configurações restauradas", "As preferências voltaram aos valores padrão.", "OK");
    }

    private void OnSettingsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (e.NewTextValue ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            SetAllSectionsVisible(true);
            return;
        }

        SetSection(AccountSection, Matches(query, "conta microsoft login autenticação codex conexão"));
        SetSection(AiPreferencesSection, Matches(query, "ia aisto resposta estilo especialização codex"));
        SetSection(AppearanceSection, Matches(query, "aparência tema escuro claro sistema visual"));
        SetSection(ConnectionSection, Matches(query, "servidor api endereço url backend conexão"));
        SetSection(PrivacySection, Matches(query, "privacidade dados armazenamento json token senha perfis"));
        SetSection(AboutSection, Matches(query, "sobre versão aplicativo agent android dotnet"));
        NoResultsLabel.IsVisible = !AccountSection.IsVisible && !AiPreferencesSection.IsVisible &&
            !AppearanceSection.IsVisible && !ConnectionSection.IsVisible && !PrivacySection.IsVisible &&
            !AboutSection.IsVisible;
    }

    private static bool Matches(string query, string terms) =>
        terms.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(term => terms.Contains(term, StringComparison.CurrentCultureIgnoreCase));

    private static void SetSection(VisualElement section, bool visible) => section.IsVisible = visible;

    private void SetAllSectionsVisible(bool visible)
    {
        AccountSection.IsVisible = visible;
        AiPreferencesSection.IsVisible = visible;
        AppearanceSection.IsVisible = visible;
        ConnectionSection.IsVisible = visible;
        PrivacySection.IsVisible = visible;
        AboutSection.IsVisible = visible;
        NoResultsLabel.IsVisible = false;
    }

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is global::Agent.Mobile.App app)
            app.ApplyThemePreference(theme);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        var route = Shell.Current.CurrentState.Location.OriginalString;
        if (route.Contains("settings-detail", StringComparison.OrdinalIgnoreCase))
            await Shell.Current.GoToAsync("..");
        else
            await Shell.Current.GoToAsync("//chat");
    }
}
