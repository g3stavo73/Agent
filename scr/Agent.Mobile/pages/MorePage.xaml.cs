using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class MorePage : ContentPage
{
    private bool _navigationPending;

    public MorePage() => InitializeComponent();

    private async Task NavigateAsync(string route)
    {
        if (_navigationPending)
            return;

        _navigationPending = true;
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Não foi possível abrir a seção", ex.Message, "OK");
        }
        finally
        {
            _navigationPending = false;
        }
    }

    private async void OnContextTapped(object? sender, TappedEventArgs e) =>
        await NavigateAsync("context");

    private async void OnPluginsTapped(object? sender, TappedEventArgs e) =>
        await NavigateAsync("plugins");

    private async void OnHistoryTapped(object? sender, TappedEventArgs e) =>
        await NavigateAsync("history-detail");

    private async void OnSettingsTapped(object? sender, TappedEventArgs e) =>
        await NavigateAsync("settings-detail");
}
