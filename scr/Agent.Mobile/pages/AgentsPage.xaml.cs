using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Agent.Mobile.Models;
using Agent.Mobile.Services;
using Microsoft.Maui.Storage;

namespace Agent.Mobile.Pages;

public partial class AgentsPage : ContentPage
{
    private readonly IAgentProfileStore _profileStore;
    private readonly List<AgentProfile> _agents = new();
    private AgentProfile? _editingAgent;
    private string? _originalAvatarPath;
    private string? _newAvatarPath;
    private bool _removeAvatarRequested;
    private bool _loadFailed;
    private bool _isMutating;

    public AgentsPage(IAgentProfileStore profileStore)
    {
        InitializeComponent();
        _profileStore = profileStore;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAgentsAsync();
        RefreshListState();
    }

    private async Task LoadAgentsAsync()
    {
        try
        {
            var profiles = await _profileStore.LoadAsync();
            _agents.Clear();
            _agents.AddRange(profiles);
            _loadFailed = false;
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            await DisplayAlert("Não foi possível carregar os agentes",
                $"O arquivo local foi preservado e não será sobrescrito por esta sessão. Detalhe: {ex.Message}", "OK");
        }
    }

    private Task SaveAgentsAsync() => _profileStore.SaveAsync(_agents);

    private async void OnCreateAiStoClicked(object sender, EventArgs e)
    {
        if (_isMutating)
            return;
        await Shell.Current.GoToAsync("aisto-builder");
    }

    private void OnNewAgentClicked(object sender, EventArgs e)
    {
        if (_isMutating)
            return;

        ResetEditor();
        _editingAgent = null;
        EditorTitle.Text = "Novo agente";
        SaveAgentButton.Text = "Criar agente";
        EditorPanel.IsVisible = true;
        AgentNameEntry.Focus();
    }

    private void OnAgentNameCompleted(object sender, EventArgs e)
    {
        AgentDescriptionEditor.Focus();
    }

    private void OnCancelEditorClicked(object sender, EventArgs e)
    {
        if (_isMutating)
            return;

        EditorPanel.IsVisible = false;
        ResetEditor();
        _editingAgent = null;
    }

    private async void OnChooseAvatarClicked(object sender, EventArgs e)
    {
        if (_isMutating)
            return;

        try
        {
            var photo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Escolher foto do agente",
                FileTypes = FilePickerFileType.Images
            });

            if (photo is null)
                return;

            await using var source = await photo.OpenReadAsync();
            var destination = await _profileStore.SaveAvatarAsync(source, photo.FileName, photo.ContentType);

            DeleteManagedAvatar(_newAvatarPath);
            _newAvatarPath = destination;
            _removeAvatarRequested = false;
            ShowAvatar(destination);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Falha ao selecionar a foto", ex.Message, "OK");
        }
    }

    private void OnRemoveAvatarClicked(object sender, EventArgs e)
    {
        if (_isMutating)
            return;

        DeleteManagedAvatar(_newAvatarPath);
        _newAvatarPath = null;
        _removeAvatarRequested = true;
        AvatarPreview.Source = null;
        AvatarPreview.IsVisible = false;
        AvatarFallback.IsVisible = true;
    }

    private async void OnSaveAgentClicked(object sender, EventArgs e)
    {
        if (_isMutating)
            return;

        var name = AgentNameEntry.Text?.Trim();
        if (_loadFailed)
        {
            ShowEditorError("O arquivo de perfis não pôde ser lido. A gravação foi bloqueada para preservar os dados existentes.");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowEditorError("Informe um nome para o agente.");
            AgentNameEntry.Focus();
            return;
        }

        var duplicate = _agents.FirstOrDefault(agent =>
            !ReferenceEquals(agent, _editingAgent) &&
            string.Equals(agent.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase));
        if (duplicate is not null)
        {
            ShowEditorError("Já existe um agente com esse nome. Escolha outro nome para diferenciá-los.");
            AgentNameEntry.Focus();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var oldProfile = _editingAgent;
        var oldIndex = oldProfile is null ? -1 : _agents.IndexOf(oldProfile);
        if (oldProfile is not null && oldIndex < 0)
        {
            ShowEditorError("Este perfil não está mais na lista. Feche o editor e tente novamente.");
            return;
        }
        var previousAvatar = oldProfile?.AvatarPath;

        var updated = new AgentProfile
        {
            Id = oldProfile?.Id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            Description = AgentDescriptionEditor.Text?.Trim() ?? string.Empty,
            AvatarPath = _removeAvatarRequested ? null : _newAvatarPath ?? _originalAvatarPath,
            Engine = "codex",
            CanReadRepositories = ReadRepositorySwitch.IsToggled,
            CanWriteRepositories = WriteRepositorySwitch.IsToggled,
            CanBrowseWeb = WebAccessSwitch.IsToggled,
            UseSemanticNavigation = SemanticNavigationSwitch.IsToggled,
            PreferUpdatedDocumentation = UpdatedDocsSwitch.IsToggled,
            ApplyArchitectureRules = ArchitectureRulesSwitch.IsToggled,
            AiSto = oldProfile?.AiSto,
            CreatedAtUtc = oldProfile?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        };

        if (oldProfile is null)
            _agents.Add(updated);
        else if (oldIndex >= 0)
            _agents[oldIndex] = updated;

        _isMutating = true;
        EditorPanel.IsEnabled = false;
        SaveAgentButton.IsEnabled = false;
        try
        {
            await SaveAgentsAsync();
            if (previousAvatar is not null && previousAvatar != updated.AvatarPath)
                DeleteManagedAvatar(previousAvatar);

            EditorPanel.IsVisible = false;
            _editingAgent = null;
            // The profile now owns this copied avatar; ResetEditor must not delete it.
            _newAvatarPath = null;
            ResetEditor();
            RefreshListState();
        }
        catch (Exception ex)
        {
            if (oldProfile is null)
                _agents.Remove(updated);
            else if (oldIndex >= 0)
                _agents[oldIndex] = oldProfile;

            ShowEditorError($"Não foi possível salvar o agente. {ex.Message}");
        }
        finally
        {
            _isMutating = false;
            EditorPanel.IsEnabled = true;
            SaveAgentButton.IsEnabled = true;
        }
    }

    private async void OnEditAgentClicked(AgentProfile agent)
    {
        if (_isMutating)
            return;

        ResetEditor();
        _editingAgent = agent;
        _originalAvatarPath = agent.AvatarPath;
        _removeAvatarRequested = false;

        EditorTitle.Text = "Editar agente";
        SaveAgentButton.Text = "Salvar alterações";
        AgentNameEntry.Text = agent.Name;
        AgentDescriptionEditor.Text = agent.Description;
        ReadRepositorySwitch.IsToggled = agent.CanReadRepositories;
        WriteRepositorySwitch.IsToggled = agent.CanWriteRepositories;
        WebAccessSwitch.IsToggled = agent.CanBrowseWeb;
        SemanticNavigationSwitch.IsToggled = agent.UseSemanticNavigation;
        UpdatedDocsSwitch.IsToggled = agent.PreferUpdatedDocumentation;
        ArchitectureRulesSwitch.IsToggled = agent.ApplyArchitectureRules;

        if (agent.HasAvatar && agent.AvatarPath is not null)
            ShowAvatar(agent.AvatarPath);

        EditorPanel.IsVisible = true;
        await Task.Yield();
        AgentNameEntry.Focus();
    }

    private async void OnDeleteAgentClicked(AgentProfile agent)
    {
        if (_isMutating)
            return;

        _isMutating = true;
        try
        {
            var confirmed = await DisplayAlert("Excluir agente",
                $"Excluir o perfil \"{agent.Name}\" deste dispositivo? Esta ação não remove arquivos do GitHub.",
                "Excluir", "Cancelar");
            if (!confirmed)
                return;

            var index = _agents.IndexOf(agent);
            if (index < 0)
                return;

            if (_editingAgent?.Id == agent.Id)
            {
                EditorPanel.IsVisible = false;
                _editingAgent = null;
                ResetEditor();
            }

            _agents.RemoveAt(index);
            try
            {
                await SaveAgentsAsync();
                DeleteManagedAvatar(agent.AvatarPath);
                RefreshListState();
            }
            catch (Exception ex)
            {
                _agents.Insert(index, agent);
                RefreshListState();
                await DisplayAlert("Falha ao excluir", ex.Message, "OK");
            }
        }
        finally
        {
            _isMutating = false;
        }
    }

    private void ResetEditor()
    {
        DeleteManagedAvatar(_newAvatarPath);
        _newAvatarPath = null;
        _originalAvatarPath = null;
        _removeAvatarRequested = false;

        AgentNameEntry.Text = string.Empty;
        AgentDescriptionEditor.Text = string.Empty;
        ReadRepositorySwitch.IsToggled = true;
        WriteRepositorySwitch.IsToggled = false;
        WebAccessSwitch.IsToggled = true;
        SemanticNavigationSwitch.IsToggled = true;
        UpdatedDocsSwitch.IsToggled = true;
        ArchitectureRulesSwitch.IsToggled = true;
        AvatarPreview.Source = null;
        AvatarPreview.IsVisible = false;
        AvatarFallback.IsVisible = true;
        EditorErrorLabel.Text = string.Empty;
        EditorErrorLabel.IsVisible = false;
    }

    private void ShowAvatar(string path)
    {
        AvatarPreview.Source = ImageSource.FromFile(path);
        AvatarPreview.IsVisible = true;
        AvatarFallback.IsVisible = false;
    }

    private void ShowEditorError(string message)
    {
        EditorErrorLabel.Text = message;
        EditorErrorLabel.IsVisible = true;
    }

    private void DeleteManagedAvatar(string? path) => _profileStore.DeleteAvatar(path);

    private View CreateAgentCard(AgentProfile agent)
    {
        var avatarGrid = new Grid { WidthRequest = 48, HeightRequest = 48 };
        avatarGrid.Children.Add(new Border
        {
            BackgroundColor = (Color)Application.Current!.Resources["AccentSurface"],
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Content = new Label
            {
                Text = agent.Initials,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current.Resources["Accent"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        });

        if (agent.HasAvatar && agent.AvatarPath is not null)
        {
            var avatarImage = new Image
            {
                Source = ImageSource.FromFile(agent.AvatarPath),
                Aspect = Aspect.AspectFill
            };
            SemanticProperties.SetDescription(avatarImage, $"Foto de {agent.Name}");
            avatarGrid.Children.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
                Padding = 0,
                Content = avatarImage
            });
        }

        var details = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = agent.Name, FontSize = 14, FontAttributes = FontAttributes.Bold },
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(agent.Description) ? "Sem descrição" : agent.Description,
                    FontSize = 10,
                    TextColor = (Color)Application.Current!.Resources["TextSecondary"],
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2
                },
                new Label
                {
                    Text = $"CODEX · {agent.CapabilitiesSummary}",
                    FontSize = 9,
                    TextColor = (Color)Application.Current!.Resources["Accent"]
                },
                new Label
                {
                    Text = agent.IsAiStoSpecialized ? $"AiSTo · {agent.AiSto!.Domain}" : "Codex padrão · sem especialização AiSTo",
                    FontSize = 9,
                    TextColor = (Color)Application.Current.Resources[agent.IsAiStoSpecialized ? "Accent" : "TextMuted"],
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                },
                new Label
                {
                    Text = agent.ContextSummary,
                    FontSize = 9,
                    TextColor = (Color)Application.Current!.Resources["TextMuted"]
                }
            }
        };

        var editButton = new Button
        {
            Text = "Editar", FontSize = 9, Padding = new Thickness(9, 5),
            BackgroundColor = (Color)Application.Current!.Resources["SurfaceRaised"],
            Command = new Command(() => OnEditAgentClicked(agent))
        };
        SemanticProperties.SetDescription(editButton, $"Editar o agente {agent.Name}");

        var deleteButton = new Button
        {
            Text = "Excluir", FontSize = 9, Padding = new Thickness(9, 5),
            BackgroundColor = (Color)Application.Current!.Resources["SurfaceRaised"],
            TextColor = (Color)Application.Current.Resources["Danger"],
            Command = new Command(() => OnDeleteAgentClicked(agent))
        };
        SemanticProperties.SetDescription(deleteButton, $"Excluir o agente {agent.Name}");

        var actions = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center
        };
        actions.Children.Add(editButton);

        var specializationButton = new Button
        {
            Text = agent.IsAiStoSpecialized ? "Editar AiSTo" : "Especializar",
            FontSize = 9,
            Padding = new Thickness(9, 5),
            BackgroundColor = (Color)Application.Current!.Resources["AccentSurface"],
            TextColor = (Color)Application.Current.Resources["Accent"],
            Command = new Command(async () =>
            {
                await Shell.Current.GoToAsync("aisto-builder", new Dictionary<string, object>
                {
                    ["agentId"] = agent.Id
                });
            })
        };
        SemanticProperties.SetDescription(specializationButton,
            agent.IsAiStoSpecialized ? $"Editar especialização AiSTo de {agent.Name}" : $"Especializar {agent.Name} com AiSTo");
        actions.Children.Add(specializationButton);
        actions.Children.Add(deleteButton);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 50 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 10
        };
        grid.Add(avatarGrid, 0, 0);
        grid.Add(details, 1, 0);
        grid.Add(actions, 2, 0);

        return new Border
        {
            Padding = 12,
            BackgroundColor = (Color)Application.Current!.Resources["Surface"],
            Stroke = (Color)Application.Current.Resources["Border"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = grid
        };
    }

    private void RefreshListState()
    {
        var hasAgents = _agents.Count > 0;
        EmptyState.IsVisible = !hasAgents;
        AgentsList.IsVisible = hasAgents;
        AgentsList.Children.Clear();

        foreach (var agent in _agents.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            AgentsList.Children.Add(CreateAgentCard(agent));

        AgentCountLabel.Text = $"{_agents.Count} {(_agents.Count == 1 ? "agente" : "agentes")}";
    }
}
