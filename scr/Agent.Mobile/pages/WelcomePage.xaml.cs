using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class WelcomePage : ContentPage
{
    public WelcomePage() => InitializeComponent();

    private void OnPreviewClicked(object sender, EventArgs e)
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null)
            return;

        window.Page = new Agent.Mobile.AppShell();
    }
}
