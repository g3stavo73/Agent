using Microsoft.Maui.Controls;
using Agent.Mobile.Pages;

namespace Agent.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("history-detail", typeof(HistoryPage));
        Routing.RegisterRoute("settings-detail", typeof(SettingsPage));
        Routing.RegisterRoute("context", typeof(ContextPage));
        Routing.RegisterRoute("plugins", typeof(PluginsPage));
        Routing.RegisterRoute("aisto-builder", typeof(AiStoBuilderPage));
    }
}
