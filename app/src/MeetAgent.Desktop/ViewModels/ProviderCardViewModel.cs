using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Ai;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>Estado visual de una tarjeta de proveedor.</summary>
public enum ProviderCardState
{
    Default,
    Busy,
    Connected,
    Error,
}

/// <summary>Qué hay dentro de cada tarjeta: una cuenta, OpenRouter, una API key o un modelo propio.</summary>
public enum ProviderCardKind
{
    Account,
    OpenRouter,
    ApiKey,
    OwnModel,
}

/// <summary>
/// Tarjeta de «Conecta tu IA». Guarda su estado y los campos de los formularios plegables;
/// la acción concreta la ejecuta el view model de la página.
/// </summary>
public sealed partial class ProviderCardViewModel : ObservableObject
{
    private readonly Func<ProviderCardViewModel, Task>? _connect;
    private string _actionText;

    public ProviderCardViewModel(string title, string subtitle, string actionText, ProviderCardKind kind,
        Func<ProviderCardViewModel, Task>? connect = null, AiProvider? provider = null,
        Func<CancellationToken, Task<IReadOnlyList<LocalModel>>>? detectModels = null)
    {
        Title = title;
        Subtitle = subtitle;
        _actionText = actionText;
        Kind = kind;
        _connect = connect;
        Provider = provider;
        _detectModels = detectModels;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public ProviderCardKind Kind { get; }
    public AiProvider? Provider { get; }

    public bool IsAccount => Kind == ProviderCardKind.Account;
    public bool IsOpenRouter => Kind == ProviderCardKind.OpenRouter;
    public bool IsApiKey => Kind == ProviderCardKind.ApiKey;
    public bool IsOwnModel => Kind == ProviderCardKind.OwnModel;
    public bool IsExpandable => IsApiKey || IsOwnModel;
    public bool UsesAccountIcon => IsAccount || IsOpenRouter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefault), nameof(IsConnected), nameof(IsBusy), nameof(HasError),
        nameof(ActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ProviderCardState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(ShowApiKeyForm), nameof(ShowOwnModelForm))]
    private bool _isExpanded;

    public bool ShowApiKeyForm => IsApiKey && IsExpanded;
    public bool ShowOwnModelForm => IsOwnModel && IsExpanded;
    public bool IsDefault => State == ProviderCardState.Default;
    public bool IsConnected => State == ProviderCardState.Connected;
    public bool IsBusy => State == ProviderCardState.Busy;
    public bool HasError => State == ProviderCardState.Error;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    /// <summary>Texto del botón según el estado (y si el formulario está plegado).</summary>
    public string ActionLabel => State switch
    {
        ProviderCardState.Busy => "Conectando…",
        ProviderCardState.Connected => "Conectado",
        ProviderCardState.Error => "Reintentar",
        _ when IsExpandable && !IsExpanded => _actionText,
        _ when IsExpandable => "Conectar",
        _ => _actionText,
    };

    /// <summary>Cambia el texto de reposo (p. ej. «Instalar» si el CLI no está).</summary>
    public void SetActionText(string text)
    {
        _actionText = text;
        OnPropertyChanged(nameof(ActionLabel));
    }

    public void SetConnected(string status)
    {
        Status = status;
        State = ProviderCardState.Connected;
    }

    public void SetDisconnected()
    {
        if (State == ProviderCardState.Connected) Status = null;
        State = ProviderCardState.Default;
        IsExpanded = false;
    }

    private bool CanConnect() => State is not (ProviderCardState.Busy or ProviderCardState.Connected);

    /// <summary>Primero despliega el formulario; ya abierto, conecta.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync()
    {
        if (IsExpandable && !IsExpanded)
        {
            IsExpanded = true;
            return Task.CompletedTask;
        }
        return _connect?.Invoke(this) ?? Task.CompletedTask;
    }

    // --- «Tengo una API key» ---
    public IReadOnlyList<ProviderInfo> ApiProviders { get; } =
        Providers.All.Where(p => !p.IsAccount && p.Provider is
            AiProvider.Anthropic or AiProvider.OpenAI or AiProvider.DeepSeek or AiProvider.Gemini).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyHint), nameof(BaseUrlHint))]
    private ProviderInfo _selectedProvider = Providers.Get(AiProvider.Anthropic);

    [ObservableProperty] private string _model = Providers.Get(AiProvider.Anthropic).ChatModel;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _apiBaseUrl = "";

    public string ApiKeyHint => SelectedProvider.NeedsKey ? "API key" : "API key (si tu servidor la pide)";
    public string BaseUrlHint => SelectedProvider.BaseUrl is { } u ? $"URL (vacío = {u})" : "URL (no hace falta)";

    partial void OnSelectedProviderChanged(ProviderInfo? oldValue, ProviderInfo newValue)
    {
        if (string.IsNullOrWhiteSpace(Model) || Model == oldValue?.ChatModel) Model = newValue.ChatModel;
    }

    // --- «Tengo mi propio modelo» ---
    [ObservableProperty] private string _ownBaseUrl = "http://localhost:11434/v1";
    [ObservableProperty] private string _ownModel = "qwen3";
    [ObservableProperty] private string _ownKey = "";
    [ObservableProperty] private string? _modelsStatus;
    public ObservableCollection<string> LocalModels { get; } = new();

    private readonly Func<CancellationToken, Task<IReadOnlyList<LocalModel>>>? _detectModels;

    [RelayCommand]
    private async Task DetectModelsAsync()
    {
        if (_detectModels is null) return;
        ModelsStatus = "Buscando modelos…";
        try
        {
            var models = await _detectModels(CancellationToken.None);
            LocalModels.Clear();
            foreach (var m in models) LocalModels.Add($"{m.Name} · {m.Origin}");
            if (models.Count > 0)
            {
                OwnModel = models[0].Name;
                OwnBaseUrl = models[0].BaseUrl;
                ModelsStatus = $"Encontré {models.Count} modelo(s).";
            }
            else
            {
                ModelsStatus = "No encontré modelos. ¿Está abierto Ollama o LM Studio?";
            }
        }
        catch (Exception)
        {
            ModelsStatus = "No pude buscar modelos. Inténtalo de nuevo.";
        }
    }

    partial void OnModelsStatusChanged(string? value) => OnPropertyChanged(nameof(HasModelsStatus));
    public bool HasModelsStatus => !string.IsNullOrWhiteSpace(ModelsStatus);
}
