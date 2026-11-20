using Agent.Core.Ui;
using Agent.Mobile.Services;
using Microsoft.Maui.Controls;

namespace Agent.Mobile.Pages;

public partial class ChatPage : ContentPage
{
    private readonly AgentUiRenderer _uiRenderer;

    public ChatPage(AgentUiRenderer uiRenderer)
    {
        InitializeComponent();
        _uiRenderer = uiRenderer ?? throw new ArgumentNullException(nameof(uiRenderer));
    }

    /// <summary>
    /// Called by the Codex conversation adapter when an assistant message contains
    /// an Agent declarative UI document. Plain text remains plain text.
    /// </summary>
    public bool TryRenderStructuredResponse(string assistantMessage)
    {
        var parsed = AgentUiDocumentParser.Parse(assistantMessage);
        if (!parsed.Success || parsed.Document is null)
            return false;

        AgentUiHost.Content = _uiRenderer.Render(parsed.Document, ExecuteUiActionAsync);
        AgentUiHost.IsVisible = true;
        return true;
    }

    private static Task ExecuteUiActionAsync(string actionId) => actionId switch
    {
        "navigate.chat" => Shell.Current.GoToAsync("//chat"),
        "navigate.agents" => Shell.Current.GoToAsync("//agents"),
        "navigate.projects" => Shell.Current.GoToAsync("//projects"),
        "navigate.history" => Shell.Current.GoToAsync("history-detail"),
        "navigate.context" => Shell.Current.GoToAsync("context"),
        "navigate.plugins" => Shell.Current.GoToAsync("plugins"),
        "navigate.settings" => Shell.Current.GoToAsync("//settings"),
        _ => Task.CompletedTask
    };
}
