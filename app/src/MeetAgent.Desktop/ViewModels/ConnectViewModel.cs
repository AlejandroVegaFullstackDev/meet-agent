using CommunityToolkit.Mvvm.ComponentModel;
using MeetAgent.Ai;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>
/// «Conecta tu IA»: elige con qué cuenta o servicio analiza MeetAgent. De momento guarda el estado
/// de la conexión y avisa a la barra lateral; en la siguiente fase absorbe el formulario técnico.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private readonly Action<AiSettings> _save;

    public ConnectViewModel(AiSettings current, Action<AiSettings> save)
    {
        _save = save;
        _settings = current;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUsableAi), nameof(StatusText))]
    private AiSettings _settings;

    /// <summary>La conexión cambió: la barra lateral y los ajustes se actualizan.</summary>
    public event Action? Changed;

    public Connection? Chat => Settings.Chat;
    public bool HasUsableAi => Settings.Chat is { IsUsable: true };

    public string StatusText => Settings.Chat is { IsUsable: true } c
        ? $"{ShortName(c.Provider)} ✓"
        : "Sin IA conectada";

    /// <summary>Guarda la configuración elegida y avisa a quien escuche.</summary>
    public void Apply(AiSettings settings)
    {
        Settings = settings;
        _save(settings);
        Changed?.Invoke();
    }

    /// <summary>Actualiza la foto de la configuración sin volver a guardarla.</summary>
    public void Reload(AiSettings settings) => Settings = settings;

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
}
