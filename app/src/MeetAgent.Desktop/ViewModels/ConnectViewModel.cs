using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Ai;
using MeetAgent.Core.Ports;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>
/// «Conecta tu IA»: entra con la cuenta que ya usas (Claude Code, Codex, Gemini CLI), con OpenRouter,
/// con una API key o con tu propio modelo. Sin IDs técnicos: cada tarjeta explica lo que hace.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private readonly Action<AiSettings> _save;
    private readonly Func<Connection, IChatModel> _createChat;
    private readonly AccountDetector _detector;
    private readonly Func<CancellationToken, Task<string>> _openRouterLogin;
    private readonly Action<string> _openUrl;
    private readonly Func<CancellationToken, Task<IReadOnlyList<LocalModel>>> _detectModels;

    /// <param name="createChat">Arma la IA de una conexión (en pruebas, una falsa).</param>
    /// <param name="detector">Detecta los CLI y sus sesiones (en pruebas, uno falso).</param>
    /// <param name="openRouterLogin">Inicia sesión en OpenRouter y devuelve la API key.</param>
    /// <param name="detectModels">Busca modelos en Ollama y LM Studio.</param>
    public ConnectViewModel(AiSettings current, Action<AiSettings> save,
        Func<Connection, IChatModel>? createChat = null,
        AccountDetector? detector = null,
        Func<CancellationToken, Task<string>>? openRouterLogin = null,
        Action<string>? openUrl = null,
        Func<CancellationToken, Task<IReadOnlyList<LocalModel>>>? detectModels = null)
    {
        _save = save;
        _createChat = createChat ?? AiFactory.CreateChat;
        _detector = detector ?? AccountDetector.Default;
        _openRouterLogin = openRouterLogin ?? (ct => OpenRouterLogin.LoginAsync(AiFactory.Http, ct: ct));
        _openUrl = openUrl ?? OpenRouterLogin.OpenBrowser;
        _detectModels = detectModels ?? (ct => new LocalModels().ListAsync(ct));

        Claude = new ProviderCardViewModel("Usar mi cuenta de Claude", "Por Claude Code",
            "Iniciar sesión", ProviderCardKind.Account, ConnectCardAsync, AiProvider.ClaudeCuenta);
        ChatGpt = new ProviderCardViewModel("Usar mi cuenta de ChatGPT", "Por Codex",
            "Iniciar sesión", ProviderCardKind.Account, ConnectCardAsync, AiProvider.ChatGptCuenta);
        Gemini = new ProviderCardViewModel("Usar mi cuenta de Google (Gemini)", "Por Gemini CLI, gratis",
            "Iniciar sesión", ProviderCardKind.Account, ConnectCardAsync, AiProvider.GeminiCuenta);
        OpenRouter = new ProviderCardViewModel("OpenRouter", "Todos los modelos, pago por uso",
            "Iniciar sesión", ProviderCardKind.OpenRouter, ConnectCardAsync);
        ApiKey = new ProviderCardViewModel("Tengo una API key", "Anthropic, OpenAI, DeepSeek o Gemini",
            "Configurar", ProviderCardKind.ApiKey, ConnectCardAsync);
        OwnModel = new ProviderCardViewModel("Tengo mi propio modelo", "Ollama, LM Studio o tu servidor",
            "Configurar", ProviderCardKind.OwnModel, ConnectCardAsync, detectModels: _detectModels);

        Cards = [Claude, ChatGpt, Gemini, OpenRouter, ApiKey, OwnModel];
        _settings = current;
        HasTranscriber = current.Transcription is { IsUsable: true };
        RefreshFromSettings();
    }

    public ProviderCardViewModel Claude { get; }
    public ProviderCardViewModel ChatGpt { get; }
    public ProviderCardViewModel Gemini { get; }
    public ProviderCardViewModel OpenRouter { get; }
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

    /// <summary>Comprueba los CLI instalados y si ya tienen sesión. No gasta tokens.</summary>
    public async Task StartAsync()
    {
        foreach (var card in new[] { Claude, ChatGpt, Gemini })
        {
            if (card.Provider is not { } provider || card.IsConnected) continue;
            var state = await _detector.DetectAsync(provider);
            card.SetActionText(state.Installed ? "Iniciar sesión" : "Instalar");
            if (state.LoggedIn) card.Status = "Detectado ✓";
            else if (!state.Installed) card.Status = null;
        }
    }

    private bool CanStartRecording() => HasUsableAi;

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private void StartRecording() => StartRequested?.Invoke();

    private Task ConnectCardAsync(ProviderCardViewModel card) => card.Kind switch
    {
        ProviderCardKind.Account => ConnectAccountAsync(card),
        ProviderCardKind.OpenRouter => LoginOpenRouterAsync(card),
        ProviderCardKind.ApiKey => ConnectApiKeyAsync(card),
        ProviderCardKind.OwnModel => ConnectOwnModelAsync(card),
        _ => Task.CompletedTask,
    };

    private async Task ConnectAccountAsync(ProviderCardViewModel card)
    {
        if (card.Provider is not { } provider) return;
        if (_detector.Executable(provider) is null)
        {
            card.Status = $"Instala {CliName(provider)} y vuelve aquí.";
            _openUrl(AccountDetector.InstallUrl(provider));
            return;
        }

        var state = await _detector.DetectAsync(provider);
        if (!state.LoggedIn)
        {
            card.State = ProviderCardState.Busy;
            card.Status = "Esperando a que inicies sesión en la terminal…";
            bool ok;
            try { ok = await _detector.LoginAsync(provider, CancellationToken.None); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                card.State = ProviderCardState.Error;
                card.Status = "No se pudo conectar. Inténtalo de nuevo.";
                return;
            }
        }

        SaveChat(new Connection(provider, Providers.Get(provider).ChatModel));
        MarkConnected(card, $"Conectado con tu cuenta de {AccountName(provider)} ✓");
    }

    private async Task LoginOpenRouterAsync(ProviderCardViewModel card)
    {
        card.State = ProviderCardState.Busy;
        card.Status = "Se abrió el navegador: inicia sesión en OpenRouter…";
        try
        {
            var key = await _openRouterLogin(CancellationToken.None);
            SaveChat(new Connection(AiProvider.OpenRouter, Providers.Get(AiProvider.OpenRouter).ChatModel, key));
            MarkConnected(card, "Conectado con OpenRouter ✓");
        }
        catch (Exception)
        {
            card.State = ProviderCardState.Error;
            card.Status = "No se pudo conectar. Inténtalo de nuevo.";
        }
    }

    private async Task ConnectApiKeyAsync(ProviderCardViewModel card)
    {
        var connection = new Connection(card.SelectedProvider.Provider, card.Model.Trim(),
            Blank(card.ApiKey), Blank(card.ApiBaseUrl));
        if (!connection.IsUsable)
        {
            card.State = ProviderCardState.Error;
            card.Status = "Escribe tu API key.";
            return;
        }
        await TestAndSaveAsync(card, connection, $"Conectado con {ShortName(connection.Provider)} ✓");
    }

    private async Task ConnectOwnModelAsync(ProviderCardViewModel card)
    {
        var connection = new Connection(AiProvider.Personalizado, card.OwnModel.Trim(),
            Blank(card.OwnKey), Blank(card.OwnBaseUrl));
        if (!connection.IsUsable)
        {
            card.State = ProviderCardState.Error;
            card.Status = "Escribe la dirección y el nombre del modelo.";
            return;
        }
        await TestAndSaveAsync(card, connection, "Conectado con tu modelo ✓");
    }

    /// <summary>Una pregunta mínima: confirma que la IA responde antes de darla por conectada.</summary>
    private async Task TestAndSaveAsync(ProviderCardViewModel card, Connection connection, string connectedStatus)
    {
        card.State = ProviderCardState.Busy;
        card.Status = "Comprobando…";
        try
        {
            var model = _createChat(connection);
            await model.CompleteAsync("Responde solo con la palabra: listo", "¿Estás conectado?", null, CancellationToken.None);
            SaveChat(connection);
            MarkConnected(card, connectedStatus);
        }
        catch (Exception)
        {
            card.State = ProviderCardState.Error;
            card.Status = "No se pudo conectar. Inténtalo de nuevo.";
        }
    }

    private void SaveChat(Connection connection) => Apply(Settings with { Chat = connection });

    private void MarkConnected(ProviderCardViewModel card, string status)
    {
        foreach (var other in Cards)
            if (!ReferenceEquals(other, card)) other.SetDisconnected();
        card.SetConnected(status);
    }

    /// <summary>Pinta el estado guardado en las tarjetas (una sola IA de análisis a la vez).</summary>
    private void RefreshFromSettings()
    {
        var chat = Settings.Chat;
        if (chat is not { IsUsable: true })
        {
            foreach (var card in Cards) card.SetDisconnected();
            return;
        }

        var selected = chat.Provider switch
        {
            AiProvider.ClaudeCuenta => Claude,
            AiProvider.ChatGptCuenta => ChatGpt,
            AiProvider.GeminiCuenta => Gemini,
            AiProvider.OpenRouter => OpenRouter,
            AiProvider.Personalizado => OwnModel,
            _ => ApiKey,
        };

        foreach (var card in Cards)
            if (!ReferenceEquals(card, selected)) card.SetDisconnected();

        switch (selected.Kind)
        {
            case ProviderCardKind.Account:
                selected.SetConnected($"Conectado con tu cuenta de {AccountName(chat.Provider)} ✓");
                break;
            case ProviderCardKind.OpenRouter:
                selected.SetConnected("Conectado con OpenRouter ✓");
                break;
            case ProviderCardKind.ApiKey:
                selected.SelectedProvider = chat.Info;
                selected.Model = chat.Model;
                selected.ApiKey = chat.ApiKey ?? "";
                selected.ApiBaseUrl = chat.BaseUrl ?? "";
                selected.SetConnected($"Conectado con {ShortName(chat.Provider)} ✓");
                break;
            case ProviderCardKind.OwnModel:
                selected.OwnModel = chat.Model;
                selected.OwnBaseUrl = chat.BaseUrl ?? selected.OwnBaseUrl;
                selected.OwnKey = chat.ApiKey ?? "";
                selected.SetConnected("Conectado con tu modelo ✓");
                break;
        }
    }

    /// <summary>Nombre corto del proveedor para la barra lateral y los ajustes.</summary>
    internal static string ShortName(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic or AiProvider.ClaudeCuenta => "Claude",
        AiProvider.OpenAI => "OpenAI",
        AiProvider.ChatGptCuenta => "ChatGPT",
        AiProvider.Gemini or AiProvider.GeminiCuenta => "Gemini",
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

    private static string CliName(AiProvider provider) => provider switch
    {
        AiProvider.ClaudeCuenta => "Claude Code",
        AiProvider.ChatGptCuenta => "Codex",
        _ => "Gemini CLI",
    };

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
