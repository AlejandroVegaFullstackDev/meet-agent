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

/// <summary>Qué hay dentro de cada tarjeta: una cuenta, OpenRouter, DeepSeek, una API key o un modelo propio.</summary>
public enum ProviderCardKind
{
    Account,
    OpenRouter,
    DeepSeek,
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
    private readonly Func<ProviderCardViewModel, Task>? _disconnect;
    private readonly Action<string>? _openUrl;
    private readonly Action<ProviderCardViewModel>? _useApiKey;
    private readonly Func<CancellationToken, Task<IReadOnlyList<LocalModel>>>? _detectModels;
    private string _actionText;

    public ProviderCardViewModel(string title, string subtitle, string actionText, ProviderCardKind kind,
        Func<ProviderCardViewModel, Task>? connect = null, AiProvider? provider = null,
        string? brandBrushKey = null, string logoText = "", bool keyMode = false,
        string? keyUrl = null, string? keyAction = null,
        Func<ProviderCardViewModel, Task>? disconnect = null,
        Action<string>? openUrl = null,
        Action<ProviderCardViewModel>? useApiKey = null,
        Func<CancellationToken, Task<IReadOnlyList<LocalModel>>>? detectModels = null)
    {
        Title = title;
        Subtitle = subtitle;
        _actionText = actionText;
        Kind = kind;
        _connect = connect;
        _disconnect = disconnect;
        _openUrl = openUrl;
        _useApiKey = useApiKey;
        _detectModels = detectModels;
        Provider = provider;
        BrandBrushKey = brandBrushKey;
        LogoText = logoText;
        KeyMode = keyMode;
        KeyUrl = keyUrl;
        KeyAction = keyAction ?? "Conseguir mi key";
    }

    public string Title { get; }
    public string Subtitle { get; private set; }
    public ProviderCardKind Kind { get; }
    public AiProvider? Provider { get; }
    public string? BrandBrushKey { get; }
    public string LogoInkBrushKey => BrandBrushKey is { } key
        ? key.Replace("Brush", "InkBrush", StringComparison.Ordinal)
        : "BrandBrush";
    public string LogoText { get; }
    public string? KeyUrl { get; }
    public string KeyAction { get; }

    public bool IsAccount => Kind == ProviderCardKind.Account;
    public bool IsOpenRouter => Kind == ProviderCardKind.OpenRouter;
    public bool IsDeepSeek => Kind == ProviderCardKind.DeepSeek;
    public bool IsApiKey => Kind == ProviderCardKind.ApiKey;
    public bool IsOwnModel => Kind == ProviderCardKind.OwnModel;
    public bool IsExpandable => IsApiKey || IsOwnModel;
    public bool ShowsBrandLogo => IsAccount || IsOpenRouter || IsDeepSeek;
    public bool ShowsKeyField => KeyMode;
    public bool UsesAccountIcon => IsAccount || IsOwnModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefault), nameof(IsConnected), nameof(IsBusy), nameof(HasError),
        nameof(ActionLabel), nameof(ShowConnectedStatus), nameof(ShowErrorStatus), nameof(ShowProgressStatus))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    private ProviderCardState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(ShowConnectedStatus), nameof(ShowErrorStatus),
        nameof(ShowProgressStatus))]
    private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(ShowApiKeyForm), nameof(ShowOwnModelForm),
        nameof(ShowAdvanced), nameof(ShowsKeyField))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel), nameof(ShowsKeyField), nameof(ShowConnectedStatus))]
    private bool _keyMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private string _keyValue = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyStatus))]
    private string? _keyStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdvanced))]
    private bool _isAdvanced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyAlternative))]
    private bool _showAlternative;

    public bool ShowApiKeyForm => IsApiKey && IsExpanded;
    public bool ShowOwnModelForm => IsOwnModel && IsExpanded;
    public bool ShowAdvanced => IsApiKey && IsExpanded && IsAdvanced;
    public bool IsDefault => State == ProviderCardState.Default;
    public bool IsConnected => State == ProviderCardState.Connected;
    public bool IsBusy => State == ProviderCardState.Busy;
    public bool HasError => State == ProviderCardState.Error;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasKeyStatus => !string.IsNullOrWhiteSpace(KeyStatus);
    public bool ShowConnectedStatus => HasStatus && IsConnected;
    public bool ShowErrorStatus => HasStatus && HasError;
    public bool ShowProgressStatus => HasStatus && !IsConnected && !HasError;
    public bool ShowKeyAlternative => HasError && ShowAlternative;

    /// <summary>Texto del botón según el estado (y si el formulario está plegado).</summary>
    public string ActionLabel => State switch
    {
        ProviderCardState.Busy => "Conectando…",
        ProviderCardState.Connected => "Conectado",
        ProviderCardState.Error => "Reintentar",
        _ when ShowsKeyField => string.IsNullOrWhiteSpace(KeyValue) ? KeyAction : "Conectar",
        _ when IsExpandable && !IsExpanded => _actionText,
        _ when IsApiKey => "Probar",
        _ when IsExpandable => "Conectar",
        _ => _actionText,
    };

    /// <summary>Cambia el texto de reposo, p. ej. al detectar que la cuenta ya tiene sesión.</summary>
    public void SetActionText(string text)
    {
        _actionText = text;
        OnPropertyChanged(nameof(ActionLabel));
    }

    public void SetConnected(string status)
    {
        Status = status;
        ShowAlternative = false;
        State = ProviderCardState.Connected;
    }

    public void SetError(string status, bool alternative = true)
    {
        Status = status;
        ShowAlternative = alternative;
        State = ProviderCardState.Error;
    }

    public void SetDisconnected()
    {
        Status = null;
        ShowAlternative = false;
        State = ProviderCardState.Default;
        IsExpanded = false;
        IsAdvanced = false;
    }

    /// <summary>Google sin Node pasa a conectar con una API key, sin hablar de instaladores.</summary>
    public void EnableKeyMode(string subtitle)
    {
        Subtitle = subtitle;
        KeyMode = true;
        OnPropertyChanged(nameof(Subtitle));
    }

    public void EnableAccountMode(string subtitle)
    {
        Subtitle = subtitle;
        KeyMode = false;
        OnPropertyChanged(nameof(Subtitle));
    }

    public void SetKeyStatus(string? status) => KeyStatus = status;

    private bool CanConnect() => State is not (ProviderCardState.Busy or ProviderCardState.Connected);

    private bool CanDisconnect() => IsConnected;

    /// <summary>Primero despliega el formulario; ya abierto, conecta. Sin key, abre la página oficial.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync()
    {
        if (ShowsKeyField && string.IsNullOrWhiteSpace(KeyValue))
        {
            OpenKeyPage();
            return Task.CompletedTask;
        }
        if (IsExpandable && !IsExpanded)
        {
            IsExpanded = true;
            return Task.CompletedTask;
        }
        return _connect?.Invoke(this) ?? Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() => _disconnect?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private void OpenKeyPage()
    {
        if (KeyUrl is { Length: > 0 }) _openUrl?.Invoke(KeyUrl);
    }

    [RelayCommand]
    private void UseApiKey() => _useApiKey?.Invoke(this);

    // --- «Tengo una API key» ---
    public IReadOnlyList<ProviderInfo> ApiProviders { get; } =
        Providers.All.Where(p => p.Provider is AiProvider.Anthropic or AiProvider.OpenAI
            or AiProvider.DeepSeek or AiProvider.Gemini or AiProvider.Groq).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyHint), nameof(BaseUrlHint), nameof(SelectedProviderKeyUrl))]
    private ProviderInfo _selectedProvider = Providers.Get(AiProvider.Anthropic);

    [ObservableProperty] private string _model = Providers.Get(AiProvider.Anthropic).ChatModel;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _apiBaseUrl = "";
    [ObservableProperty] private string? _apiStatus;

    public string ApiKeyHint => SelectedProvider.NeedsKey ? "API key" : "API key (si tu servidor la pide)";
    public string BaseUrlHint => SelectedProvider.BaseUrl is { } u ? $"URL (vacío = {u})" : "URL (no hace falta)";
    public string? SelectedProviderKeyUrl => SelectedProvider.KeyUrl;

    partial void OnSelectedProviderChanged(ProviderInfo? oldValue, ProviderInfo newValue)
    {
        if (string.IsNullOrWhiteSpace(Model) || Model == oldValue?.ChatModel) Model = newValue.ChatModel;
    }

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvanced = !IsAdvanced;

    [RelayCommand]
    private void OpenSelectedKeyPage()
    {
        if (SelectedProvider.KeyUrl is { Length: > 0 } url) _openUrl?.Invoke(url);
    }

    // --- «Tengo mi propio modelo» ---
    [ObservableProperty] private string _ownBaseUrl = "http://localhost:11434/v1";
    [ObservableProperty] private string _ownModel = "qwen3";
    [ObservableProperty] private string _ownKey = "";
    [ObservableProperty] private string? _modelsStatus;
    public ObservableCollection<string> LocalModels { get; } = new();

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
