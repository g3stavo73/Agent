using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class PluginsPage : ContentPage
{
    public PluginsPage() => InitializeComponent();
    private async void OnBackClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
