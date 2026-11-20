using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class MorePage : ContentPage
{
    public MorePage() => InitializeComponent();

    private Task NavigateAsync(string route) => Shell.Current.GoToAsync(route);
    private async void OnContextClicked(object sender, EventArgs e) => await NavigateAsync("context");
    private async void OnPluginsClicked(object sender, EventArgs e) => await NavigateAsync("plugins");
    private async void OnHistoryClicked(object sender, EventArgs e) => await NavigateAsync("history-detail");
    private async void OnSettingsClicked(object sender, EventArgs e) => await NavigateAsync("settings-detail");
}
