using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class HistoryPage : ContentPage
{
    public HistoryPage() => InitializeComponent();
    private async void OnBackClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
