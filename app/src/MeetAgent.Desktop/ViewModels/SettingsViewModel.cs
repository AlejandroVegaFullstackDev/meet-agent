using CommunityToolkit.Mvvm.ComponentModel;
using MeetAgent.Ai;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>
/// «Ajustes»: tu IA, el transcriptor, el análisis automático y la privacidad.
/// La base queda aquí; el formulario completo llega en la fase de ajustes.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Action<AiSettings> _save;
    private bool _applying;

    public SettingsViewModel(AiSettings current, Action<AiSettings> save)
    {
        _save = save;
        _settings = current;
        _analyzeWhenMeetingEnds = current.AnalyzeWhenMeetingEnds;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiName), nameof(HasUsableAi))]
    private AiSettings _settings;

    [ObservableProperty] private bool _analyzeWhenMeetingEnds;

    /// <summary>Se guardó algo que también cambia otras páginas.</summary>
    public event Action? Changed;

    public bool HasUsableAi => Settings.Chat is { IsUsable: true };

    public string AiName => Settings.Chat is { IsUsable: true } c
        ? ConnectViewModel.ShortName(c.Provider)
        : "Sin IA conectada";

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
        Refresh();
    }

    partial void OnAnalyzeWhenMeetingEndsChanged(bool value)
    {
        if (_applying || Settings.AnalyzeWhenMeetingEnds == value) return;
        Apply(Settings with { AnalyzeWhenMeetingEnds = value });
    }

    private void Apply(AiSettings settings)
    {
        Settings = settings;
        _save(settings);
        Changed?.Invoke();
    }
}
