using Agent.Mobile.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Agent.Mobile.Pages;

/// <summary>
/// Thin MAUI view: platform pickers, confirmation dialogs and Shell navigation only.
/// AiSTo state, validation and profile persistence live in AiStoBuilderViewModel.
/// </summary>
public partial class AiStoBuilderPage : ContentPage, IQueryAttributable
{
    private readonly AiStoBuilderViewModel _viewModel;
    private string? _requestedAgentId;
    private bool _initialized;
    private bool _isSaving;
    private bool _isPickingAvatar;

    public AiStoBuilderPage(AiStoBuilderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("agentId", out var value))
            _requestedAgentId = value?.ToString();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;

        _initialized = true;
        try
        {
            await _viewModel.LoadAsync(_requestedAgentId);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Não foi possível abrir o AiSTo", ex.Message, "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_isSaving && !_isPickingAvatar)
            _viewModel.CancelPendingChanges();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_isSaving || _isPickingAvatar || _viewModel.IsBusy)
            return true;

        MainThread.BeginInvokeOnMainThread(async () => await NavigateBackAsync());
        return true;
    }

    private async void OnBackClicked(object sender, EventArgs e) => await NavigateBackAsync();

    private async void OnCancelClicked(object sender, EventArgs e) => await NavigateBackAsync();

    private async Task NavigateBackAsync()
    {
        if (_isSaving || _isPickingAvatar)
            return;

        _viewModel.CancelPendingChanges();
        await Shell.Current.GoToAsync("..");
    }

    private async void OnChooseAvatarClicked(object sender, EventArgs e)
    {
        if (_viewModel.IsBusy || _isPickingAvatar)
            return;

        _isPickingAvatar = true;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Escolher foto do especialista",
                FileTypes = FilePickerFileType.Images
            });

            if (file is null)
                return;

            await using var stream = await file.OpenReadAsync();
            await _viewModel.SaveAvatarAsync(stream, file.FileName, file.ContentType);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Não foi possível usar esta imagem", ex.Message, "OK");
        }
        finally
        {
            _isPickingAvatar = false;
        }
    }

    private void OnRemoveAvatarClicked(object sender, EventArgs e)
    {
        if (_viewModel.IsBusy)
            return;
        _viewModel.RemoveAvatar();
    }

    private async void OnLoadTemplateClicked(object sender, EventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        if (_viewModel.HasTemplateText)
        {
            var replace = await DisplayAlert(
                "Substituir conteúdo atual?",
                "O modelo da área substituirá o objetivo e as instruções preenchidos até agora.",
                "Aplicar modelo", "Manter conteúdo");
            if (!replace)
                return;
        }

        _viewModel.ApplyDomainTemplate();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (_isSaving || _viewModel.IsBusy)
            return;

        _isSaving = true;
        try
        {
            if (await _viewModel.SaveAsync())
            {
                await DisplayAlert("AiSTo salvo", "A configuração foi salva neste protótipo. A execução especializada será conectada pelo backend.", "OK");
                await Shell.Current.GoToAsync("..");
            }
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async void OnRemoveAiStoClicked(object sender, EventArgs e)
    {
        if (_isSaving || _viewModel.IsBusy || !_viewModel.CanRemoveAiSto)
            return;

        var confirmed = await DisplayAlert(
            "Remover especialização?",
            $"O agente \"{_viewModel.Name}\" continuará existindo e passará a usar o Codex padrão, sem a configuração AiSTo.",
            "Remover AiSTo", "Cancelar");
        if (!confirmed)
            return;

        _isSaving = true;
        try
        {
            if (await _viewModel.RemoveSpecializationAsync())
            {
                await DisplayAlert("AiSTo removido", "O perfil foi mantido e voltou ao modo Codex padrão.", "OK");
                await Shell.Current.GoToAsync("..");
            }
        }
        finally
        {
            _isSaving = false;
        }
    }
}
