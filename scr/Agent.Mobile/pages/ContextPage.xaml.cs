using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class ContextPage : ContentPage
{
    public ContextPage() => InitializeComponent();
    private async void OnBackClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
