using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Ai;
using MeetAgent.Core.Ports;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>
/// «Conecta tu IA»: un clic abre el navegador del proveedor y vuelve conectado. También admite
/// una API key o un modelo propio, sin hablar de componentes ni de comandos en la pantalla.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private readonly Action<AiSettings> _save;
    private readonly Func<Connection, IChatModel> _createChat;
    private readonly AccountConnector _connector;
    private readonly Func<CancellationToken, Task<string>> _openRouterLogin;
    private readonly Action<string> _openUrl;
    private readonly Func<CancellationToken, Task<IReadOnlyList<LocalModel>>> _detectModels;

    /// <param name="createChat">Arma la IA de una conexión (en pruebas, una falsa).</param>
    /// <param name="connector">Conecta cuentas con un clic (en pruebas, uno falso).</param>
    /// <param name="openRouterLogin">Inicia sesión en OpenRouter y devuelve la API key.</param>
    /// <param name="detectModels">Busca modelos en Ollama y LM Studio.</param>
    /// <param name="askInstall">Pide permiso antes de descargar el componente oficial.</param>
    public ConnectViewModel(AiSettings current, Action<AiSettings> save,
        Func<Connection, IChatModel>? createChat = null,
        AccountConnector? connector = null,
        Func<CancellationToken, Task<string>>? openRouterLogin = null,
        Action<string>? openUrl = null,
        Func<CancellationToken, Task<IReadOnlyList<LocalModel>>>? detectModels = null,
        Func<AiProvider, Task<bool>>? askInstall = null)
    {
        _save = save;
        _createChat = createChat ?? AiFactory.CreateChat;
        _openUrl = openUrl ?? OpenRouterLogin.OpenBrowser;
        _connector = connector ?? new AccountConnector(confirmInstall: askInstall ?? (_ => Task.FromResult(false)));
        _openRouterLogin = openRouterLogin ?? (ct => OpenRouterLogin.LoginAsync(AiFactory.Http, ct: ct));
        _detectModels = detectModels ?? (ct => new LocalModels().ListAsync(ct));

        Claude = new ProviderCardViewModel("Claude", "Usa tu cuenta de Claude", "Iniciar sesión con Claude",
            ProviderCardKind.Account, ConnectCardAsync, AiProvider.ClaudeCuenta,
            "ProviderClaudeBrush", "C", disconnect: DisconnectCardAsync, openUrl: _openUrl, useApiKey: UseApiKey);
        ChatGpt = new ProviderCardViewModel("ChatGPT", "Usa tu cuenta de ChatGPT", "Iniciar sesión con ChatGPT",
            ProviderCardKind.Account, ConnectCardAsync, AiProvider.ChatGptCuenta,
            "ProviderOpenAiBrush", "AI", disconnect: DisconnectCardAsync, openUrl: _openUrl, useApiKey: UseApiKey);
        Gemini = new ProviderCardViewModel("Google (Gemini)", "Usa tu cuenta de Google", "Iniciar sesión con Google",
            ProviderCardKind.Account, ConnectCardAsync, AiProvider.GeminiCuenta,
            "ProviderGoogleBrush", "G", keyUrl: Providers.Get(AiProvider.Gemini).KeyUrl,
            keyAction: "Conseguir mi key gratis", disconnect: DisconnectCardAsync, openUrl: _openUrl,
            useApiKey: UseApiKey);
        OpenRouter = new ProviderCardViewModel("OpenRouter", "Todos los modelos, pago por uso",
            "Iniciar sesión con OpenRouter", ProviderCardKind.OpenRouter, ConnectCardAsync,
            brandBrushKey: "ProviderOpenRouterBrush", logoText: "O", disconnect: DisconnectCardAsync,
            openUrl: _openUrl);
        DeepSeek = new ProviderCardViewModel("DeepSeek", "Usa tu API key de DeepSeek", "Conseguir mi key",
            ProviderCardKind.DeepSeek, ConnectCardAsync, AiProvider.DeepSeek,
            "ProviderDeepSeekBrush", "D", keyMode: true, keyUrl: Providers.Get(AiProvider.DeepSeek).KeyUrl,
            disconnect: DisconnectCardAsync, openUrl: _openUrl);
        ApiKey = new ProviderCardViewModel("Tengo una API key", "Claude, OpenAI, DeepSeek, Gemini o Groq",
            "Configurar", ProviderCardKind.ApiKey, ConnectCardAsync,
            disconnect: DisconnectCardAsync, openUrl: _openUrl);
        OwnModel = new ProviderCardViewModel("Tengo mi propio modelo", "Ollama, LM Studio o tu servidor",
            "Configurar", ProviderCardKind.OwnModel, ConnectCardAsync, detectModels: _detectModels,
            disconnect: DisconnectCardAsync, openUrl: _openUrl);

        Cards = [Claude, ChatGpt, Gemini, OpenRouter, DeepSeek, ApiKey, OwnModel];
        _settings = current;
        HasTranscriber = current.Transcription is { IsUsable: true };
        RefreshFromSettings();
    }

    public ProviderCardViewModel Claude { get; }
    public ProviderCardViewModel ChatGpt { get; }
    public ProviderCardViewModel Gemini { get; }
    public ProviderCardViewModel OpenRouter { get; }
    public ProviderCardViewModel DeepSeek { get; }
    public ProviderCardViewModel ApiKey { get; }
    public ProviderCardViewModel OwnModel { get; }
    public IReadOnlyList<ProviderCardViewModel> Cards { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUsableAi), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    private AiSettings _settings;

    /// <summary>La conexión cambió: la barra lateral y los ajustes se actualizan.</summary>
    public event Action? Changed;

    /// <summary>«Empezar a grabar»: el shell cambia a la página Grabar.</summary>
    public event Action? StartRequested;

    public Connection? Chat => Settings.Chat;
    public bool HasUsableAi => Settings.Chat is { IsUsable: true };

    public string StatusText => Settings.Chat is { IsUsable: true } c
        ? $"{ShortName(c.Provider)} ✓"
        : "Sin IA conectada";

    // --- Transcripción (solo la nota de Groq; el resto vive en Ajustes) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGroqNote))]
    private bool _hasTranscriber;

    public bool ShowGroqNote => !HasTranscriber;

    [ObservableProperty] private bool _isGroqFormVisible;
    [ObservableProperty] private string _groqKey = "";
    [ObservableProperty] private string? _groqStatus;

    [RelayCommand]
    private void OpenGroqKeys() => _openUrl("https://console.groq.com/keys");

    /// <summary>Groq es gratis y transcribe las llamadas; pide su key y guarda la configuración.</summary>
    [RelayCommand]
    private void ConnectGroq()
    {
        if (string.IsNullOrWhiteSpace(GroqKey))
        {
            IsGroqFormVisible = true;
            GroqStatus = "Crea tu key gratis en console.groq.com/keys.";
            return;
        }
        var transcription = new Connection(AiProvider.Groq, Providers.Get(AiProvider.Groq).TranscriptionModel!, GroqKey.Trim());
        Apply(Settings with { Transcription = transcription });
        IsGroqFormVisible = false;
        GroqStatus = "Listo: Groq transcribe tus llamadas.";
    }

    /// <summary>Guarda la configuración elegida y avisa a quien escuche.</summary>
    public void Apply(AiSettings settings)
    {
        Settings = settings;
        HasTranscriber = settings.Transcription is { IsUsable: true };
        _save(settings);
        Changed?.Invoke();
    }

    /// <summary>Actualiza la foto de la configuración sin volver a guardarla.</summary>
    public void Reload(AiSettings settings)
    {
        Settings = settings;
        HasTranscriber = settings.Transcription is { IsUsable: true };
        RefreshFromSettings();
    }

    /// <summary>Comprueba si las cuentas ya tienen sesión. No gasta tokens ni instala nada.</summary>
    public async Task StartAsync()
    {
        foreach (var card in new[] { Claude, ChatGpt, Gemini })
        {
            if (card.Provider is not { } provider || card.IsConnected || card.IsBusy || card.HasError) continue;
            var state = await _connector.DetectAsync(provider);
            card.Status = state.LoggedIn ? "Sesión detectada ✓" : null;
        }
    }

    private bool CanStartRecording() => HasUsableAi;

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private void StartRecording() => StartRequested?.Invoke();

    private Task ConnectCardAsync(ProviderCardViewModel card) => card.Kind switch
    {
        ProviderCardKind.Account => ConnectAccountAsync(card),
        ProviderCardKind.OpenRouter => LoginOpenRouterAsync(card),
        ProviderCardKind.DeepSeek => ConnectDeepSeekAsync(card),
        ProviderCardKind.ApiKey => ConnectApiKeyAsync(card),
        ProviderCardKind.OwnModel => ConnectOwnModelAsync(card),
        _ => Task.CompletedTask,
    };

    private async Task ConnectAccountAsync(ProviderCardViewModel card)
    {
        if (card.Provider is not { } provider) return;
        if (card.ShowsKeyField)
        {
            var apiProvider = provider == AiProvider.GeminiCuenta ? AiProvider.Gemini : provider;
            var keyConnection = new Connection(apiProvider, Providers.Get(apiProvider).ChatModel,
                Blank(card.KeyValue));
            if (!keyConnection.IsUsable)
            {
                card.SetError("Escribe tu API key.", alternative: false);
                return;
            }
            await TestAndSaveAsync(card, keyConnection);
            return;
        }
        card.State = ProviderCardState.Busy;
        card.Status = "Comprobando…";
        var progress = new Progress<string>(text => card.Status = text);
        try
        {
            var result = await _connector.ConnectAsync(provider, progress, CancellationToken.None);
            switch (result.Status)
            {
                case AccountConnectStatus.Connected:
                    SaveChat(new Connection(provider, Providers.Get(provider).ChatModel));
                    break;
                case AccountConnectStatus.Cancelled:
                    card.SetDisconnected();
                    card.Status = null;
                    break;
                case AccountConnectStatus.NeedsKey:
                    card.EnableKeyMode("Usa tu API key de Google (gratis)");
                    card.State = ProviderCardState.Default;
                    card.Status = result.Message;
                    break;
                default:
                    card.SetError(result.Message ?? "No pudimos conectar tu cuenta. Inténtalo de nuevo.");
                    break;
            }
        }
        catch (Exception)
        {
            card.SetError("No pudimos conectar tu cuenta. Revisa tu conexión e inténtalo de nuevo.");
        }
    }

    private async Task LoginOpenRouterAsync(ProviderCardViewModel card)
    {
        card.State = ProviderCardState.Busy;
        card.Status = "Abriendo el navegador…";
        try
        {
            var login = _openRouterLogin(CancellationToken.None);
            if (!login.IsCompleted) card.Status = "Esperando que inicies sesión…";
            var key = await login;
            SaveChat(new Connection(AiProvider.OpenRouter, Providers.Get(AiProvider.OpenRouter).ChatModel, key));
        }
        catch (Exception)
        {
            card.SetError("No pudimos conectar OpenRouter. Inténtalo de nuevo.", alternative: false);
        }
    }

    private async Task ConnectDeepSeekAsync(ProviderCardViewModel card)
    {
        var connection = new Connection(AiProvider.DeepSeek, Providers.Get(AiProvider.DeepSeek).ChatModel,
            Blank(card.KeyValue));
        if (!connection.IsUsable)
        {
            card.SetError("Escribe tu API key de DeepSeek.", alternative: false);
            return;
        }
        await TestAndSaveAsync(card, connection);
    }

    private async Task ConnectApiKeyAsync(ProviderCardViewModel card)
    {
        var connection = new Connection(card.SelectedProvider.Provider, card.Model.Trim(),
            Blank(card.ApiKey), Blank(card.ApiBaseUrl));
        if (!connection.IsUsable)
        {
            card.SetError("Escribe tu API key.", alternative: false);
            return;
        }
        await TestAndSaveAsync(card, connection);
    }

    private async Task ConnectOwnModelAsync(ProviderCardViewModel card)
    {
        var connection = new Connection(AiProvider.Personalizado, card.OwnModel.Trim(),
            Blank(card.OwnKey), Blank(card.OwnBaseUrl));
        if (!connection.IsUsable)
        {
            card.SetError("Escribe la dirección y el nombre del modelo.", alternative: false);
            return;
        }
        await TestAndSaveAsync(card, connection);
    }

    /// <summary>Una pregunta mínima: confirma que la IA responde antes de darla por conectada.</summary>
    private async Task TestAndSaveAsync(ProviderCardViewModel card, Connection connection)
    {
        card.State = ProviderCardState.Busy;
        card.Status = "Comprobando…";
        try
        {
            var model = _createChat(connection);
            await model.CompleteAsync("Responde solo con la palabra: listo", "¿Estás conectado?", null, CancellationToken.None);
            SaveChat(connection);
        }
        catch (Exception)
        {
            card.SetError("No pudimos conectar. Revisa la key e inténtalo de nuevo.", alternative: false);
        }
    }

    private void SaveChat(Connection connection)
    {
        Apply(Settings with { Chat = connection });
        RefreshFromSettings();
    }

    /// <summary>Desconecta la IA activa; el resto de la configuración (transcriptor) se conserva.</summary>
    private Task DisconnectCardAsync(ProviderCardViewModel card)
    {
        if (Settings.Chat is { } chat && Matches(card, chat.Provider))
            Apply(Settings with { Chat = null });
        RefreshFromSettings();
        return Task.CompletedTask;
    }

    /// <summary>El aviso de error ofrece seguir con una API key en la tarjeta correspondiente.</summary>
    private void UseApiKey(ProviderCardViewModel card)
    {
        var provider = card.Provider switch
        {
            AiProvider.ClaudeCuenta => AiProvider.Anthropic,
            AiProvider.ChatGptCuenta => AiProvider.OpenAI,
            _ => AiProvider.Gemini,
        };
        ApiKey.SetDisconnected();
        ApiKey.SelectedProvider = Providers.Get(provider);
        ApiKey.IsExpanded = true;
    }

    /// <summary>Pega solo una key de DeepSeek copiada; se llama al volver a la ventana.</summary>
    public bool PasteDeepSeekKeyFromClipboard(string? text)
    {
        var key = text?.Trim();
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("sk-", StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(DeepSeek.KeyValue)) return false;
        DeepSeek.KeyValue = key;
        DeepSeek.SetKeyStatus("Key pegada ✓");
        return true;
    }

    /// <summary>Pinta el estado guardado en las tarjetas (una sola IA de análisis a la vez).</summary>
    private void RefreshFromSettings()
    {
        foreach (var card in Cards) card.SetDisconnected();

        var chat = Settings.Chat;
        if (chat is not { IsUsable: true }) return;

        switch (chat.Provider)
        {
            case AiProvider.ClaudeCuenta:
                Claude.SetConnected($"Conectado con tu cuenta de {AccountName(chat.Provider)} ✓");
                break;
            case AiProvider.ChatGptCuenta:
                ChatGpt.SetConnected($"Conectado con tu cuenta de {AccountName(chat.Provider)} ✓");
                break;
            case AiProvider.GeminiCuenta:
                Gemini.EnableAccountMode("Usa tu cuenta de Google");
                Gemini.SetConnected($"Conectado con tu cuenta de {AccountName(chat.Provider)} ✓");
                break;
            case AiProvider.Gemini:
                Gemini.EnableKeyMode("Usa tu API key de Google (gratis)");
                Gemini.KeyValue = chat.ApiKey ?? "";
                Gemini.SetConnected("Conectado con Google ✓");
                break;
            case AiProvider.OpenRouter:
                OpenRouter.SetConnected("Conectado con OpenRouter ✓");
                break;
            case AiProvider.DeepSeek:
                DeepSeek.KeyValue = chat.ApiKey ?? "";
                DeepSeek.SetConnected("Conectado con DeepSeek ✓");
                break;
            case AiProvider.Personalizado:
                OwnModel.OwnModel = chat.Model;
                OwnModel.OwnBaseUrl = chat.BaseUrl ?? OwnModel.OwnBaseUrl;
                OwnModel.OwnKey = chat.ApiKey ?? "";
                OwnModel.SetConnected("Conectado con tu modelo ✓");
                break;
            default:
                ApiKey.SelectedProvider = chat.Info;
                ApiKey.Model = chat.Model;
                ApiKey.ApiKey = chat.ApiKey ?? "";
                ApiKey.ApiBaseUrl = chat.BaseUrl ?? "";
                ApiKey.IsExpanded = false;
                ApiKey.SetConnected($"Conectado con {ShortName(chat.Provider)} ✓");
                break;
        }
    }

    private static bool Matches(ProviderCardViewModel card, AiProvider provider) => card.Kind switch
    {
        ProviderCardKind.Account => card.Provider == provider
            || (card.Provider == AiProvider.GeminiCuenta && provider == AiProvider.Gemini),
        ProviderCardKind.OpenRouter => provider == AiProvider.OpenRouter,
        ProviderCardKind.DeepSeek => provider == AiProvider.DeepSeek,
        ProviderCardKind.ApiKey => provider is AiProvider.Anthropic or AiProvider.OpenAI or AiProvider.Groq,
        ProviderCardKind.OwnModel => provider == AiProvider.Personalizado,
        _ => false,
    };

    /// <summary>Nombre corto del proveedor para la barra lateral y los ajustes.</summary>
    internal static string ShortName(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic or AiProvider.ClaudeCuenta => "Claude",
        AiProvider.OpenAI => "OpenAI",
        AiProvider.ChatGptCuenta => "ChatGPT",
        AiProvider.Gemini or AiProvider.GeminiCuenta => "Google",
        AiProvider.DeepSeek => "DeepSeek",
        AiProvider.OpenRouter => "OpenRouter",
        AiProvider.Groq => "Groq",
        _ => "Tu modelo",
    };

    private static string AccountName(AiProvider provider) => provider switch
    {
        AiProvider.ClaudeCuenta => "Claude",
        AiProvider.ChatGptCuenta => "ChatGPT",
        _ => "Google",
    };

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
