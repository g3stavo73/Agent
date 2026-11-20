using System.ComponentModel;
using System.Runtime.CompilerServices;
using Agent.Mobile.Models;
using Agent.Mobile.Services;

namespace Agent.Mobile.ViewModels;

/// <summary>
/// Owns AiSTo form state, validation and profile persistence.
/// The page is limited to platform UI operations such as FilePicker and navigation.
/// </summary>
public sealed class AiStoBuilderViewModel : INotifyPropertyChanged
{
    private readonly IAgentProfileStore _profileStore;
    private readonly IAgentSettingsStore _settingsStore;
    private List<AgentProfile> _profiles = new();
    private AgentProfile? _editingProfile;
    private string? _originalAvatarPath;
    private string? _pendingAvatarPath;
    private bool _removeAvatar;

    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _domain = "Grandes projetos e sistemas complexos";
    private string _customDomain = string.Empty;
    private string _objective = string.Empty;
    private string _instructions = string.Empty;
    private string _projectContext = string.Empty;
    private string _documentationSourcesText = string.Empty;
    private string _responseStyle = "Passo a passo";
    private string? _avatarPath;
    private string _errorMessage = string.Empty;
    private string _previewName = "Seu especialista";
    private string _previewDomain = "Escolha uma especialidade";
    private string _previewObjective = "O objetivo aparecerá aqui enquanto você configura o perfil.";
    private string _previewDetails = string.Empty;
    private string _pageTitle = "Criar AiSTo";
    private string _saveButtonText = "Salvar especialista";
    private bool _useSemanticNavigation = true;
    private bool _preferUpdatedDocumentation = true;
    private bool _applyArchitectureRules = true;
    private bool _canReadRepositories = true;
    private bool _canWriteRepositories;
    private bool _canBrowseWeb = true;
    private bool _isBusy;
    private bool _loadFailed;
    private bool _canRemoveAiSto;

    private static readonly string[] Domains =
    {
        "Grandes projetos e sistemas complexos",
        "Arquitetura e revisão de código",
        "Rust e sistemas nativos",
        ".NET MAUI e aplicativos móveis",
        "Frontend e experiência de usuário",
        "Documentação técnica e APIs",
        "Testes e qualidade de software",
        "Especialidade personalizada"
    };

    private static readonly string[] ResponseStyles =
    {
        "Passo a passo", "Objetivo e conciso", "Detalhado e explicativo", "Revisão crítica"
    };

    public AiStoBuilderViewModel(IAgentProfileStore profileStore, IAgentSettingsStore settingsStore)
    {
        _profileStore = profileStore;
        _settingsStore = settingsStore;
        _responseStyle = settingsStore.Load().DefaultResponseStyle;
        UpdatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(AgentName));
                UpdatePreview();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
    }

    public string Domain
    {
        get => _domain;
        set
        {
            if (value is null || !Domains.Contains(value, StringComparer.Ordinal))
                value = "Especialidade personalizada";
            if (SetProperty(ref _domain, value))
            {
                OnPropertyChanged(nameof(IsCustomDomain));
                UpdatePreview();
            }
        }
    }

    public string CustomDomain
    {
        get => _customDomain;
        set { if (SetProperty(ref _customDomain, value ?? string.Empty)) UpdatePreview(); }
    }

    public bool IsCustomDomain => string.Equals(Domain, "Especialidade personalizada", StringComparison.Ordinal);
    public string EffectiveDomain => IsCustomDomain ? CustomDomain.Trim() : Domain;

    public string Objective
    {
        get => _objective;
        set { if (SetProperty(ref _objective, value ?? string.Empty)) UpdatePreview(); }
    }

    public string Instructions
    {
        get => _instructions;
        set => SetProperty(ref _instructions, value ?? string.Empty);
    }

    public string ProjectContext
    {
        get => _projectContext;
        set => SetProperty(ref _projectContext, value ?? string.Empty);
    }

    public string DocumentationSourcesText
    {
        get => _documentationSourcesText;
        set => SetProperty(ref _documentationSourcesText, value ?? string.Empty);
    }

    public string ResponseStyle
    {
        get => _responseStyle;
        set { if (SetProperty(ref _responseStyle, value ?? ResponseStyles[0])) UpdatePreview(); }
    }

    public string? AvatarPath
    {
        get => _avatarPath;
        private set
        {
            if (SetProperty(ref _avatarPath, value))
            {
                OnPropertyChanged(nameof(HasAvatar));
                OnPropertyChanged(nameof(IsAvatarFallbackVisible));
            }
        }
    }

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath);
    public bool IsAvatarFallbackVisible => !HasAvatar;
    public string ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string PreviewName { get => _previewName; private set => SetProperty(ref _previewName, value); }
    public string PreviewDomain { get => _previewDomain; private set => SetProperty(ref _previewDomain, value); }
    public string PreviewObjective { get => _previewObjective; private set => SetProperty(ref _previewObjective, value); }
    public string PreviewDetails { get => _previewDetails; private set => SetProperty(ref _previewDetails, value); }
    public string PageTitle { get => _pageTitle; private set => SetProperty(ref _pageTitle, value); }
    public string SaveButtonText { get => _saveButtonText; private set => SetProperty(ref _saveButtonText, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsFormEnabled));
        }
    }
    public bool IsFormEnabled => !IsBusy && !_loadFailed;
    public bool CanRemoveAiSto { get => _canRemoveAiSto; private set => SetProperty(ref _canRemoveAiSto, value); }
    public string AgentName => Name;
    public bool HasTemplateText => !string.IsNullOrWhiteSpace(Objective) || !string.IsNullOrWhiteSpace(Instructions);

    public bool UseSemanticNavigation
    {
        get => _useSemanticNavigation;
        set { if (SetProperty(ref _useSemanticNavigation, value)) UpdatePreview(); }
    }

    public bool PreferUpdatedDocumentation
    {
        get => _preferUpdatedDocumentation;
        set { if (SetProperty(ref _preferUpdatedDocumentation, value)) UpdatePreview(); }
    }

    public bool ApplyArchitectureRules
    {
        get => _applyArchitectureRules;
        set { if (SetProperty(ref _applyArchitectureRules, value)) UpdatePreview(); }
    }

    public bool CanReadRepositories
    {
        get => _canReadRepositories;
        set { if (SetProperty(ref _canReadRepositories, value)) UpdatePreview(); }
    }

    public bool CanWriteRepositories
    {
        get => _canWriteRepositories;
        set { if (SetProperty(ref _canWriteRepositories, value)) UpdatePreview(); }
    }

    public bool CanBrowseWeb
    {
        get => _canBrowseWeb;
        set { if (SetProperty(ref _canBrowseWeb, value)) UpdatePreview(); }
    }

    public async Task LoadAsync(string? agentId, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        _loadFailed = false;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(IsFormEnabled));
        try
        {
            _profiles = await _profileStore.LoadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                _editingProfile = _profiles.FirstOrDefault(profile => profile.Id == agentId);
                if (_editingProfile is null)
                {
                    SetLoadFailure("O agente selecionado não foi encontrado. Volte à lista e abra novamente.");
                    return;
                }
                Populate(_editingProfile);
            }
            else
            {
                _editingProfile = null;
                _originalAvatarPath = null;
                Name = string.Empty;
                Description = string.Empty;
                Domain = Domains[0];
                CustomDomain = string.Empty;
                var initialTemplate = GetTemplate(Domains[0]);
                Objective = initialTemplate.Objective;
                Instructions = initialTemplate.Instructions;
                ProjectContext = string.Empty;
                DocumentationSourcesText = string.Join(Environment.NewLine, new[]
                {
                    "https://learn.microsoft.com/dotnet/maui/",
                    "https://doc.rust-lang.org/book/",
                    "https://docs.github.com/"
                });
                ResponseStyle = _settingsStore.Load().DefaultResponseStyle;
                AvatarPath = null;
                UseSemanticNavigation = true;
                PreferUpdatedDocumentation = true;
                ApplyArchitectureRules = true;
                CanReadRepositories = true;
                CanWriteRepositories = false;
                CanBrowseWeb = true;
            }

            PageTitle = _editingProfile is null
                ? "Criar AiSTo"
                : _editingProfile.AiSto is null ? "Especializar agente" : "Editar AiSTo";
            SaveButtonText = _editingProfile is null ? "Salvar especialista" : "Salvar especialização";
            CanRemoveAiSto = _editingProfile?.AiSto is not null;
            UpdatePreview();
        }
        catch (Exception ex)
        {
            SetLoadFailure($"Não foi possível carregar os perfis existentes. A gravação foi bloqueada para preservar os dados. {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsFormEnabled));
        }
    }

    public async Task SaveAvatarAsync(Stream stream, string fileName, string? contentType, CancellationToken cancellationToken = default)
    {
        var savedPath = await _profileStore.SaveAvatarAsync(stream, fileName, contentType, cancellationToken);
        if (_pendingAvatarPath is not null)
            _profileStore.DeleteAvatar(_pendingAvatarPath);
        _pendingAvatarPath = savedPath;
        _removeAvatar = false;
        AvatarPath = savedPath;
        ErrorMessage = string.Empty;
    }

    public void RemoveAvatar()
    {
        if (_pendingAvatarPath is not null)
            _profileStore.DeleteAvatar(_pendingAvatarPath);
        _pendingAvatarPath = null;
        _removeAvatar = true;
        AvatarPath = null;
    }

    public void CancelPendingChanges()
    {
        if (_pendingAvatarPath is not null)
            _profileStore.DeleteAvatar(_pendingAvatarPath);
        _pendingAvatarPath = null;
        _removeAvatar = false;
        AvatarPath = _originalAvatarPath;
    }

    public void ApplyDomainTemplate()
    {
        var (objective, instructions) = GetTemplate(EffectiveDomain);
        Objective = objective;
        Instructions = instructions;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _loadFailed)
            return false;

        ErrorMessage = string.Empty;
        var name = Name.Trim();
        var objective = Objective.Trim();
        var instructions = Instructions.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Fail("Informe um nome para o especialista.");
        var effectiveDomain = EffectiveDomain;
        if (string.IsNullOrWhiteSpace(effectiveDomain))
            return Fail("Informe o nome da especialidade personalizada.");
        if (effectiveDomain.Length > 80)
            return Fail("O nome da especialidade deve ter no máximo 80 caracteres.");
        if (objective.Length < 20)
            return Fail("Descreva um objetivo com pelo menos 20 caracteres para orientar o especialista.");
        if (instructions.Length < 30)
            return Fail("Adicione instruções específicas com pelo menos 30 caracteres.");
        if (CanWriteRepositories && !CanReadRepositories)
            return Fail("Para propor alterações, o especialista também precisa ter leitura do repositório selecionado.");

        var sources = DocumentationSourcesText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (sources.Length > 20)
            return Fail("Adicione no máximo 20 referências de documentação.");

        var normalizedSources = new List<string>();
        foreach (var source in sources)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrWhiteSpace(uri.UserInfo))
                return Fail($"A referência \"{source}\" não é uma URL HTTPS válida. Use uma URL por linha.");
            normalizedSources.Add(uri.AbsoluteUri);
        }

        var duplicate = _profiles.FirstOrDefault(profile =>
            profile.Id != _editingProfile?.Id &&
            string.Equals(profile.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            return Fail("Já existe um agente com esse nome. Escolha outro para diferenciá-los.");

        var now = DateTimeOffset.UtcNow;
        var originalProfile = _editingProfile;
        var previousAvatar = originalProfile?.AvatarPath;
        var updated = CloneProfile(originalProfile, now);
        var updatedSpecialization = new AiStoSpecialization
        {
            SchemaVersion = 1,
            Domain = effectiveDomain,
            Objective = objective,
            Instructions = instructions,
            ProjectContext = ProjectContext.Trim(),
            DocumentationSources = normalizedSources,
            ResponseStyle = ResponseStyleToCode(ResponseStyle),
            CreatedAtUtc = originalProfile?.AiSto?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        };
        updatedSpecialization.Normalize();

        updated.Name = name;
        updated.Description = string.IsNullOrWhiteSpace(Description)
            ? $"Especialista em {effectiveDomain.ToLowerInvariant()}."
            : Description.Trim();
        updated.AvatarPath = _removeAvatar ? null : _pendingAvatarPath ?? _originalAvatarPath;
        updated.Engine = "codex";
        updated.AiSto = updatedSpecialization;
        updated.CanReadRepositories = CanReadRepositories;
        updated.CanWriteRepositories = CanWriteRepositories;
        updated.CanBrowseWeb = CanBrowseWeb;
        updated.UseSemanticNavigation = UseSemanticNavigation;
        updated.PreferUpdatedDocumentation = PreferUpdatedDocumentation;
        updated.ApplyArchitectureRules = ApplyArchitectureRules;
        updated.UpdatedAtUtc = now;

        var originalIndex = originalProfile is null ? -1 : _profiles.FindIndex(item => item.Id == originalProfile.Id);
        if (originalProfile is not null && originalIndex < 0)
            return Fail("O perfil não está mais na lista. Volte e abra novamente.");

        if (originalProfile is null)
            _profiles.Add(updated);
        else
            _profiles[originalIndex] = updated;

        IsBusy = true;
        try
        {
            await _profileStore.SaveAsync(_profiles, cancellationToken);
            if (previousAvatar is not null && previousAvatar != updated.AvatarPath)
                _profileStore.DeleteAvatar(previousAvatar);

            _pendingAvatarPath = null;
            return true;
        }
        catch (Exception ex)
        {
            if (originalProfile is null)
                _profiles.Remove(updated);
            else if (originalIndex >= 0)
                _profiles[originalIndex] = originalProfile;

            return Fail($"Não foi possível salvar a especialização. {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RemoveSpecializationAsync(CancellationToken cancellationToken = default)
    {
        var original = _editingProfile;
        if (IsBusy || _loadFailed || original is null || original.AiSto is null)
            return false;

        CancelPendingChanges();
        var index = _profiles.FindIndex(profile => profile.Id == original.Id);
        if (index < 0)
            return Fail("O agente não está mais na lista. Volte e abra novamente.");

        var standard = CloneProfile(original, DateTimeOffset.UtcNow);
        standard.AiSto = null;
        _profiles[index] = standard;
        IsBusy = true;
        try
        {
            await _profileStore.SaveAsync(_profiles, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _profiles[index] = original;
            return Fail($"Não foi possível remover a especialização. {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Populate(AgentProfile profile)
    {
        var specialization = profile.AiSto ?? new AiStoSpecialization();
        _originalAvatarPath = profile.AvatarPath;
        Name = profile.Name;
        Description = profile.Description;
        var isPresetDomain = Domains.Contains(specialization.Domain, StringComparer.Ordinal);
        Domain = isPresetDomain ? specialization.Domain : "Especialidade personalizada";
        CustomDomain = isPresetDomain ? string.Empty : specialization.Domain;
        if (profile.AiSto is null)
        {
            var initialTemplate = GetTemplate(EffectiveDomain);
            Objective = initialTemplate.Objective;
            Instructions = initialTemplate.Instructions;
            DocumentationSourcesText = string.Join(Environment.NewLine, new[]
            {
                "https://learn.microsoft.com/dotnet/maui/",
                "https://doc.rust-lang.org/book/",
                "https://docs.github.com/"
            });
        }
        else
        {
            Objective = specialization.Objective;
            Instructions = specialization.Instructions;
        }
        ProjectContext = specialization.ProjectContext;
        DocumentationSourcesText = string.Join(Environment.NewLine, specialization.DocumentationSources);
        ResponseStyle = ResponseStyleFromCode(specialization.ResponseStyle);
        AvatarPath = profile.HasAvatar ? profile.AvatarPath : null;
        UseSemanticNavigation = profile.UseSemanticNavigation;
        PreferUpdatedDocumentation = profile.PreferUpdatedDocumentation;
        ApplyArchitectureRules = profile.ApplyArchitectureRules;
        CanReadRepositories = profile.CanReadRepositories;
        CanWriteRepositories = profile.CanWriteRepositories;
        CanBrowseWeb = profile.CanBrowseWeb;
    }

    private void UpdatePreview()
    {
        PreviewName = string.IsNullOrWhiteSpace(Name) ? "Seu especialista" : Name.Trim();
        PreviewDomain = string.IsNullOrWhiteSpace(EffectiveDomain)
            ? (IsCustomDomain ? "Informe sua especialidade" : "Escolha uma especialidade")
            : EffectiveDomain;
        var trimmedObjective = Objective.Trim();
        PreviewObjective = string.IsNullOrWhiteSpace(trimmedObjective)
            ? "O objetivo aparecerá aqui enquanto você configura o perfil."
            : trimmedObjective.Length > 220 ? trimmedObjective[..220] + "…" : trimmedObjective;

        var tools = new List<string>();
        if (CanReadRepositories) tools.Add("leitura de repositório");
        if (CanWriteRepositories) tools.Add("propor alterações");
        if (CanBrowseWeb) tools.Add("web");
        var context = new List<string>();
        if (UseSemanticNavigation) context.Add("semântica");
        if (PreferUpdatedDocumentation) context.Add("docs");
        if (ApplyArchitectureRules) context.Add("arquitetura");
        PreviewDetails = $"Resposta: {ResponseStyle} · Ferramentas: {string.Join(", ", tools)} · Contexto: {string.Join(", ", context)}";
    }

    private void SetLoadFailure(string message)
    {
        _loadFailed = true;
        ErrorMessage = message;
        OnPropertyChanged(nameof(IsFormEnabled));
    }

    private bool Fail(string message)
    {
        ErrorMessage = message;
        return false;
    }

    private static AgentProfile CloneProfile(AgentProfile? profile, DateTimeOffset now)
    {
        if (profile is null)
            return new AgentProfile { Id = Guid.NewGuid().ToString("N"), CreatedAtUtc = now };

        return new AgentProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Description = profile.Description,
            AvatarPath = profile.AvatarPath,
            Engine = "codex",
            CanReadRepositories = profile.CanReadRepositories,
            CanWriteRepositories = profile.CanWriteRepositories,
            CanBrowseWeb = profile.CanBrowseWeb,
            UseSemanticNavigation = profile.UseSemanticNavigation,
            PreferUpdatedDocumentation = profile.PreferUpdatedDocumentation,
            ApplyArchitectureRules = profile.ApplyArchitectureRules,
            AiSto = profile.AiSto,
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = now
        };
    }

    private static string ResponseStyleToCode(string value) => value switch
    {
        "Objetivo e conciso" => "Concise",
        "Detalhado e explicativo" => "Detailed",
        "Revisão crítica" => "CriticalReview",
        _ => "StepByStep"
    };

    private static string ResponseStyleFromCode(string value) => value switch
    {
        "Concise" => "Objetivo e conciso",
        "Detailed" => "Detalhado e explicativo",
        "CriticalReview" => "Revisão crítica",
        _ => "Passo a passo"
    };

    private static (string Objective, string Instructions) GetTemplate(string domain) => domain switch
    {
        "Grandes projetos e sistemas complexos" => (
            "Compreender, planejar, evoluir e manter sistemas grandes com coerência arquitetural, navegação semântica, documentação verificável e mudanças rastreáveis.",
            "Antes de propor mudanças, faça uma leitura orientada da estrutura do repositório: pontos de entrada, módulos, limites entre camadas, símbolos importantes, dependências e contratos. Use navegação semântica por símbolos e relações quando o índice estiver disponível; não confunda busca textual com compreensão completa do projeto. Consulte documentação oficial compatível com as versões realmente usadas e identifique quando uma referência pode estar desatualizada. Respeite as regras de arquitetura do workspace e explique qualquer conflito antes de sugerir uma exceção. Divida tarefas grandes em etapas pequenas, com critérios de aceitação e verificações. Avalie impacto, compatibilidade, migrações, segurança, desempenho, testes e riscos de regressão. Preserve decisões arquiteturais justificadas e evite reescritas amplas sem necessidade. Antes de editar, apresente plano e arquivos afetados. Para qualquer alteração em repositório, produza um diff revisável e aguarde aprovação explícita antes de aplicar ou publicar. Nunca afirme que compilou, testou, pesquisou ou atualizou documentação sem evidência observável."),
        "Arquitetura e revisão de código" => (
            "Avaliar decisões arquiteturais, limites entre módulos, acoplamento, extensibilidade e riscos de manutenção.",
            "Analise as dependências entre camadas e módulos. Procure responsabilidades duplicadas, acoplamento desnecessário, abstrações sem propósito e violações dos limites definidos pelo projeto. Diferencie problemas confirmados de hipóteses. Priorize achados por impacto e apresente evidências com caminhos de arquivos e símbolos. Proponha a menor correção coerente. Não altere arquivos sem apresentar o plano e obter aprovação."),
        "Rust e sistemas nativos" => (
            "Desenvolver e revisar componentes Rust com foco em segurança, previsibilidade, interoperabilidade e manutenção.",
            "Prefira Rust seguro e evite unsafe sem necessidade. Quando unsafe ou FFI forem indispensáveis, documente invariantes, propriedade de memória, validade de ponteiros, alinhamento, duração de strings e tratamento de erros. Mantenha contratos ABI explícitos e estáveis. Use tipos e erros claros, testes de unidade e testes de integração. Explique decisões de ownership, borrowing e concorrência quando forem relevantes. Não esconda riscos de plataforma."),
        ".NET MAUI e aplicativos móveis" => (
            "Construir interfaces móveis consistentes, acessíveis e responsivas, mantendo a lógica fora das páginas.",
            "Use C# e XAML com separação entre apresentação, ViewModels/serviços e Core. Respeite o ciclo de vida Android, o teclado, a navegação, o consumo de memória e as diferenças de tamanho de tela. Inclua estados de carregamento, vazio, erro e indisponibilidade. Considere TalkBack, escala de fonte, alvos de toque e foco. Evite regra de negócio no XAML ou code-behind e prefira injeção de dependência."),
        "Frontend e experiência de usuário" => (
            "Projetar experiências claras, previsíveis e acessíveis com hierarquia visual consistente.",
            "Priorize tarefas principais, hierarquia visual, feedback imediato e prevenção de erros. Projete para larguras móveis e conteúdo longo. Não use cor como único indicador de estado. Dê nomes acessíveis aos controles, respeite contraste e áreas de toque. Diferencie ações disponíveis, pendentes, desativadas e com falha. Valide os fluxos completos, não apenas a aparência estática."),
        "Documentação técnica e APIs" => (
            "Produzir e revisar documentação técnica precisa, rastreável e compatível com as versões usadas pelo projeto.",
            "Prefira documentação oficial e versionada. Registre a fonte, versão, data de consulta e limitações. Verifique se exemplos e assinaturas correspondem à versão usada. Separe comportamento documentado de inferência. Mantenha contratos de API explícitos, exemplos mínimos e mensagens de erro compreensíveis. Não apresente referência antiga como atual."),
        "Testes e qualidade de software" => (
            "Aumentar a confiança nas mudanças por meio de critérios de aceitação e testes relevantes.",
            "Derive os testes dos requisitos e dos riscos. Inclua casos normais, limites, falhas, cancelamento, concorrência e regressões relevantes. Prefira testes determinísticos. Explique o que cada teste comprova e o que permanece sem cobertura. Não declare testes executados sem evidência de execução. Para falhas, preserve os passos mínimos de reprodução."),
        _ => (
            "Ajudar com uma especialidade definida pelo usuário, entregando resultados verificáveis e bem explicados.",
            "Siga o objetivo e o contexto fornecidos pelo usuário. Faça perguntas apenas quando um detalhe indispensável estiver ausente. Separe fatos, hipóteses e recomendações. Use fontes confiáveis quando necessário. Explique limitações e valide as alterações antes de considerá-las concluídas. Não invente capacidades ou resultados.")
    };

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
