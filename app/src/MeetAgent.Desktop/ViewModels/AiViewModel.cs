using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Ai;
using MeetAgent.Core.Ports;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>
/// Pestaña «IA»: qué IA analiza las reuniones y quién transcribe el audio. Cada uno pone su propia
/// cuenta (API key, login de OpenRouter) o la URL de su servidor (Ollama, LM Studio…).
/// </summary>
public sealed partial class AiViewModel : ObservableObject
{
    private readonly Action<AiSettings> _save;
    private readonly Func<Connection, IChatModel> _createChat;
    private readonly Func<CancellationToken, Task<string>> _openRouterLogin;
    private readonly Action<string> _openUrl;

    /// <param name="createChat">Arma la IA a partir de una conexión (en pruebas, una falsa).</param>
    /// <param name="openRouterLogin">Inicia sesión en OpenRouter y devuelve la API key.</param>
    public AiViewModel(AiSettings current, Action<AiSettings> save, Func<Connection, IChatModel>? createChat = null,
        Func<CancellationToken, Task<string>>? openRouterLogin = null, Action<string>? openUrl = null)
    {
        _save = save;
        _createChat = createChat ?? AiFactory.CreateChat;
        _openRouterLogin = openRouterLogin ?? (ct => OpenRouterLogin.LoginAsync(AiFactory.Http, ct: ct));
        _openUrl = openUrl ?? OpenRouterLogin.OpenBrowser;

        var chat = current.Chat ?? new Connection(AiProvider.Anthropic, Providers.Get(AiProvider.Anthropic).ChatModel);
        _chatProvider = chat.Info;
        _chatModel = chat.Model;
        _chatKey = chat.ApiKey ?? "";
        _chatBaseUrl = chat.BaseUrl ?? "";

        _transcribe = current.Transcription is not null;
        var tx = current.Transcription ?? new Connection(AiProvider.OpenAI, Providers.Get(AiProvider.OpenAI).TranscriptionModel!);
        _txProvider = tx.Info;
        _txModel = tx.Model;
        _txKey = tx.ApiKey ?? "";
        _txBaseUrl = tx.BaseUrl ?? "";
        _analyzeWhenMeetingEnds = current.AnalyzeWhenMeetingEnds;
    }

    public IReadOnlyList<ProviderInfo> ChatProviders => Providers.All;
    public IReadOnlyList<ProviderInfo> TranscriptionProviders { get; } = Providers.All.Where(p => p.CanTranscribe).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChatBaseUrlHint), nameof(ChatKeyHint), nameof(IsOpenRouter))]
    private ProviderInfo _chatProvider;

    [ObservableProperty] private string _chatModel;
    [ObservableProperty] private string _chatKey;
    [ObservableProperty] private string _chatBaseUrl;

    [ObservableProperty] private bool _transcribe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxBaseUrlHint))]
    private ProviderInfo _txProvider;

    [ObservableProperty] private string _txModel;
    [ObservableProperty] private string _txKey;
    [ObservableProperty] private string _txBaseUrl;

    [ObservableProperty] private bool _analyzeWhenMeetingEnds;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand), nameof(LoginOpenRouterCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _status;

    public bool IsOpenRouter => ChatProvider.Provider == AiProvider.OpenRouter;
    public string ChatBaseUrlHint => ChatProvider.BaseUrl is { } u ? $"URL (vacío = {u})" : "URL (no hace falta para Claude)";
    public string ChatKeyHint => ChatProvider.NeedsKey ? "API key" : "API key (si tu servidor la pide)";
    public string TxBaseUrlHint => $"URL (vacío = {TxProvider.BaseUrl})";

    // Al cambiar de proveedor se sugiere su modelo, salvo que el usuario haya escrito uno propio.
    partial void OnChatProviderChanged(ProviderInfo? oldValue, ProviderInfo newValue)
    {
        if (string.IsNullOrWhiteSpace(ChatModel) || ChatModel == oldValue?.ChatModel) ChatModel = newValue.ChatModel;
    }

    partial void OnTxProviderChanged(ProviderInfo? oldValue, ProviderInfo newValue)
    {
        if (string.IsNullOrWhiteSpace(TxModel) || TxModel == oldValue?.TranscriptionModel) TxModel = newValue.TranscriptionModel!;
    }

    public Connection ChatConnection => new(ChatProvider.Provider, ChatModel.Trim(), Blank(ChatKey), Blank(ChatBaseUrl));

    /// <summary>Si es el mismo proveedor que la IA de análisis y no se puso key, se reutiliza esa.</summary>
    public Connection TxConnection => new(TxProvider.Provider, TxModel.Trim(),
        Blank(TxKey) ?? (TxProvider.Provider == ChatProvider.Provider ? Blank(ChatKey) : null), Blank(TxBaseUrl));

    public AiSettings ToSettings() => new(ChatConnection, Transcribe ? TxConnection : null, AnalyzeWhenMeetingEnds);

    [RelayCommand]
    private void Save()
    {
        var s = ToSettings();
        _save(s);
        var problems = new List<string>();
        if (!s.Chat!.IsUsable) problems.Add("a la IA de análisis le falta el modelo o la API key");
        if (s.Transcription is { IsUsable: false }) problems.Add("a la transcripción le falta el modelo o la API key");
        Status = problems.Count == 0 ? "Guardado." : $"Guardado, pero {string.Join(" y ", problems)}.";
    }

    private bool CanRun() => !IsBusy;

    /// <summary>Una pregunta mínima a la IA para comprobar key, modelo y URL (gasta muy pocos tokens).</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TestAsync()
    {
        IsBusy = true;
        Status = "Probando…";
        try
        {
            var model = _createChat(ChatConnection);
            var answer = await model.CompleteAsync("Responde solo con la palabra: listo", "¿Estás conectado?", null, CancellationToken.None);
            Status = $"Funciona: {model.Name} respondió «{Short(answer)}».";
        }
        catch (Exception ex)
        {
            Status = $"No funcionó: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoginOpenRouterAsync()
    {
        IsBusy = true;
        Status = "Se abrió el navegador: inicia sesión en OpenRouter y autoriza a MeetAgent…";
        try
        {
            var key = await _openRouterLogin(CancellationToken.None);
            ChatProvider = Providers.Get(AiProvider.OpenRouter);
            ChatKey = key;
            Save();
            Status = "Conectado a OpenRouter. Elige el modelo (p. ej. anthropic/…, deepseek/…, qwen/…) o deja openrouter/auto.";
        }
        catch (Exception ex)
        {
            Status = $"No se pudo conectar con OpenRouter: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenKeyPage()
    {
        if (ChatProvider.KeyUrl is { } url) _openUrl(url);
        else Status = "Tu servidor no necesita key: pon su URL (por ejemplo http://localhost:11434/v1 para Ollama) y el modelo.";
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Short(string s) => s.Trim() is var t && t.Length > 60 ? t[..60] + "…" : s.Trim();
}
