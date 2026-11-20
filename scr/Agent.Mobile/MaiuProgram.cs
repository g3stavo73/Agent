using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Agent.Core.AiSto;
using Agent.Core.Intents;
using Agent.Mobile.Pages;
using Agent.Mobile.Services;
using Agent.Mobile.ViewModels;

namespace Agent.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(_ => { });

        builder.Services.AddSingleton<IAgentProfileStore, AgentProfileJsonStore>();
        builder.Services.AddSingleton<IAgentSettingsStore, AgentSettingsStore>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
        builder.Services.AddSingleton<IAgentApiClient, AgentApiClient>();
        builder.Services.AddSingleton<AgentUiRenderer>();
        builder.Services.AddSingleton<AiStoDraftAssistant>();
        builder.Services.AddSingleton<IAgentUserIntentInterpreter, CodexUserIntentInterpreter>();
        builder.Services.AddTransient<AgentsPage>();
        builder.Services.AddTransient<AiStoBuilderPage>();
        builder.Services.AddTransient<AiStoBuilderViewModel>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<ProjectsPage>();
        builder.Services.AddTransient<MorePage>();
        builder.Services.AddTransient<ContextPage>();
        builder.Services.AddTransient<PluginsPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
