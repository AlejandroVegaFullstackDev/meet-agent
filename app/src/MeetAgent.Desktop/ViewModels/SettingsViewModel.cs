using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Ai;
using MeetAgent.Desktop.Services;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>Quién transcribe el audio: Groq, OpenAI o un servidor propio compatible con OpenAI.</summary>
public enum TranscriptionKind
{
    Groq,
    OpenAI,
    Server,
}

/// <summary>Una opción del desplegable del transcriptor.</summary>
public sealed record TranscriptionOption(TranscriptionKind Kind, string Label);

/// <summary>
/// «Ajustes»: tu IA, el transcriptor, el análisis automático y la privacidad.
/// Guarda en el mismo <see cref="AiSettings"/> que la página de conexión.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly TranscriptionOption GroqOption = new(TranscriptionKind.Groq, "Groq (gratis)");
    private static readonly TranscriptionOption OpenAiOption = new(TranscriptionKind.OpenAI, "OpenAI");
    private static readonly TranscriptionOption ServerOption = new(TranscriptionKind.Server, "Mi servidor");

    /// <summary>La política de privacidad del repositorio, siempre disponible aunque la app esté instalada.</summary>
    public const string PrivacyUrl = "https://github.com/AlejandroVegaFullstackDev/meet-agent/blob/main/docs/privacidad.md";

    private readonly Action<AiSettings> _save;
    private readonly Action<string> _openUrl;
    private bool _applying;

    /// <param name="dataDir">Dónde están la base de datos y las grabaciones; se muestra en Privacidad.</param>
    /// <param name="openUrl">Abre el navegador; se inyecta en las pruebas.</param>
    public SettingsViewModel(AiSettings current, Action<AiSettings> save, string? dataDir = null,
        Action<string>? openUrl = null)
    {
        _save = save;
        _openUrl = openUrl ?? OpenRouterLogin.OpenBrowser;
        DataPath = dataDir ?? AppServices.DataDir;
        _settings = current;
        LoadTranscription(current.Transcription);
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiName), nameof(HasUsableAi), nameof(AiStatus))]
    private AiSettings _settings;

    [ObservableProperty] private bool _analyzeWhenMeetingEnds;

    [ObservableProperty] private bool _isTranscriptionOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TranscriptionKind), nameof(ShowTranscriptionKey),
        nameof(ShowTranscriptionServer), nameof(TranscriptionHint))]
    private TranscriptionOption _selectedTranscriptionOption = GroqOption;

    [ObservableProperty] private string _transcriptionKey = "";
    [ObservableProperty] private string _transcriptionBaseUrl = "";
    [ObservableProperty] private string _transcriptionModel = "";
    [ObservableProperty] private string? _transcriptionMessage;

    public IReadOnlyList<TranscriptionOption> TranscriptionOptions { get; } = [GroqOption, OpenAiOption, ServerOption];

    public TranscriptionKind TranscriptionKind => SelectedTranscriptionOption.Kind;

    /// <summary>Se guardó algo que también cambia otras páginas.</summary>
    public event Action? Changed;

    /// <summary>«Cambiar»: el shell abre la página «Conecta tu IA».</summary>
    public event Action? ChangeAiRequested;

    public bool HasUsableAi => Settings.Chat is { IsUsable: true };

    public string AiName => Settings.Chat is { IsUsable: true } c
        ? ConnectViewModel.ShortName(c.Provider)
        : "Sin IA conectada";

    public string AiStatus => HasUsableAi ? "Conectada ✓" : "Sin conectar";

    public bool HasTranscriber => Settings.Transcription is { IsUsable: true };
    public string TranscriptionName => Settings.Transcription is { IsUsable: true } t
        ? ConnectViewModel.ShortName(t.Provider) + " ✓"
        : "Sin transcriptor";
    public bool NeedsTranscriber => !HasTranscriber;

    public bool ShowTranscriptionKey => TranscriptionKind is TranscriptionKind.Groq or TranscriptionKind.OpenAI;
    public bool ShowTranscriptionServer => TranscriptionKind is TranscriptionKind.Server;

    public string TranscriptionHint => TranscriptionKind switch
    {
        TranscriptionKind.Groq => "Groq es gratis. Crea tu key en console.groq.com/keys.",
        TranscriptionKind.OpenAI => "Usa tu key de OpenAI.",
        _ => "Cualquier servidor compatible con OpenAI (Ollama, LM Studio, vLLM…).",
    };

    /// <summary>Dónde quedan la base de datos y las grabaciones.</summary>
    public string DataPath { get; }

    /// <summary>Relee lo que haya guardado la página de conexión.</summary>
    public void Refresh()
    {
        _applying = true;
        AnalyzeWhenMeetingEnds = Settings.AnalyzeWhenMeetingEnds;
        _applying = false;
    }

    /// <summary>Actualiza la foto de la configuración sin volver a guardarla.</summary>
    public void Reload(AiSettings settings)
    {
        Settings = settings;
        LoadTranscription(settings.Transcription);
        Refresh();
    }

    [RelayCommand]
    private void ChangeAi() => ChangeAiRequested?.Invoke();

    [RelayCommand]
    private void OpenTranscription() => IsTranscriptionOpen = true;

    [RelayCommand]
    private void OpenPrivacy() => _openUrl(PrivacyUrl);

    partial void OnAnalyzeWhenMeetingEndsChanged(bool value)
    {
        if (_applying || Settings.AnalyzeWhenMeetingEnds == value) return;
        Apply(Settings with { AnalyzeWhenMeetingEnds = value });
    }

    /// <summary>Guarda el transcriptor elegido; la app lo usa en la próxima reunión.</summary>
    [RelayCommand]
    private void SaveTranscription()
    {
        var transcription = TranscriptionKind switch
        {
            TranscriptionKind.Groq => new Connection(AiProvider.Groq,
                Providers.Get(AiProvider.Groq).TranscriptionModel!, Blank(TranscriptionKey)),
            TranscriptionKind.OpenAI => new Connection(AiProvider.OpenAI,
                Providers.Get(AiProvider.OpenAI).TranscriptionModel!, Blank(TranscriptionKey)),
            _ => new Connection(AiProvider.Personalizado, TranscriptionModel.Trim(), Blank(TranscriptionKey),
                Blank(TranscriptionBaseUrl)),
        };

        if (!transcription.IsUsable)
        {
            TranscriptionMessage = TranscriptionKind == TranscriptionKind.Server
                ? "Escribe la dirección y el nombre del modelo."
                : "Escribe tu API key.";
            return;
        }

        Apply(Settings with { Transcription = transcription });
        TranscriptionMessage = "Listo: el transcriptor quedó conectado.";
        IsTranscriptionOpen = false;
    }

    [RelayCommand]
    private void RemoveTranscription()
    {
        Apply(Settings with { Transcription = null });
        TranscriptionMessage = "La app ya no transcribe audio.";
        IsTranscriptionOpen = false;
    }

    /// <summary>Pinta los campos del transcriptor a partir de lo guardado.</summary>
    private void LoadTranscription(Connection? transcription)
    {
        _applying = true;
        SelectedTranscriptionOption = transcription?.Provider switch
        {
            AiProvider.OpenAI => OpenAiOption,
            AiProvider.Personalizado => ServerOption,
            _ => GroqOption,
        };
        TranscriptionKey = transcription?.ApiKey ?? "";
        TranscriptionBaseUrl = transcription?.BaseUrl ?? "http://localhost:8000/v1";
        TranscriptionModel = transcription?.Model ?? (TranscriptionKind == TranscriptionKind.Server
            ? "whisper-1"
            : Providers.Get(AiProvider.Groq).TranscriptionModel!);
        _applying = false;
        IsTranscriptionOpen = !HasTranscriber;
    }

    private void Apply(AiSettings settings)
    {
        Settings = settings;
        // HasTranscriber y TranscriptionName dependen de Settings, que no puede notificarse a sí mismo.
        OnPropertyChanged(nameof(HasTranscriber));
        OnPropertyChanged(nameof(TranscriptionName));
        OnPropertyChanged(nameof(NeedsTranscriber));
        OnPropertyChanged(nameof(AiStatus));
        _save(settings);
        Changed?.Invoke();
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
