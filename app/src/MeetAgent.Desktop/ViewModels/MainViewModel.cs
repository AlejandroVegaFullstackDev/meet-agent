using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Core;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Text;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>Resultado de un inicio pedido por un agente: lo que eligió el usuario en el diálogo.</summary>
public sealed record AgentStartResult(Consent Consent, long? MeetingId, bool AlreadyRunning);

public sealed record TranscriptLine(string Time, Speaker Speaker, string Text, string? SpeakerName = null)
{
    public string SpeakerLabel => string.IsNullOrWhiteSpace(SpeakerName) ? Speaker.ToString() : SpeakerName;
    public bool IsMe => Speaker == Speaker.Yo;
}

/// <summary>
/// Estado de la ventana principal y de la bandeja: Empezar → consentimiento → sesión en vivo → Terminar.
/// El diálogo y el hilo de UI se inyectan para poder probarlo sin ventanas.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly MeetingSession _session;
    private readonly Func<Task<Consent>> _askConsent;
    private readonly Action<Action> _ui;
    private readonly Func<DateTimeOffset> _now;
    private Timer? _clock;
    private DateTimeOffset _startedAt;

    public MainViewModel(MeetingSession session, string transcriberName, Func<Task<Consent>> askConsent,
        Action<Action>? ui = null, Func<DateTimeOffset>? now = null)
    {
        _session = session;
        _askConsent = askConsent;
        _ui = ui ?? (a => a());
        _now = now ?? (() => DateTimeOffset.Now);
        TranscriberName = transcriberName;
        _session.SegmentReceived += seg => _ui(() =>
        {
            Lines.Add(new TranscriptLine(TimeFormat.Hms(seg.Start), seg.Speaker, seg.Text, seg.SpeakerName));
            OnPropertyChanged(nameof(SegmentCount));
        });
        _session.Log += msg => _ui(() => LastLog = msg);
    }

    public ObservableCollection<TranscriptLine> Lines { get; } = new();
    public string TranscriberName { get; }
    public int SegmentCount => Lines.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(StatusText))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DiskText))]
    private Consent _consent = Consent.None;

    [ObservableProperty] private string _elapsed = "00:00:00";
    [ObservableProperty] private string? _lastLog;

    public string ButtonText => IsBusy ? (IsRunning ? "Terminando…" : "Preparando…") : IsRunning ? "Terminar" : "Empezar";

    public string StatusText => IsRunning
        ? Consent == Consent.RecordAndTranscribe ? "En vivo · grabando y transcribiendo" : "En vivo · solo transcribiendo"
        : "Detenido";

    public string DiskText => Consent == Consent.RecordAndTranscribe ? "Audio: se guarda en data/" : "Audio: no se guarda";

    private bool CanToggle() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        if (IsRunning) await StopAsync();
        else await StartAsync(null);
    }

    /// <summary>
    /// Inicio pedido por un agente (MCP). Muestra el mismo diálogo de consentimiento que el botón;
    /// el agente solo se entera de lo que el usuario eligió.
    /// </summary>
    public async Task<AgentStartResult> StartFromAgentAsync(string? title)
    {
        if (IsRunning || IsBusy) return new AgentStartResult(Consent, _session.MeetingId, AlreadyRunning: true);
        var consent = await StartAsync(title);
        return new AgentStartResult(consent, IsRunning ? _session.MeetingId : null, AlreadyRunning: false);
    }

    /// <returns>false si no había reunión en curso.</returns>
    public async Task<bool> StopFromAgentAsync()
    {
        if (!IsRunning) return false;
        await StopAsync();
        return true;
    }

    private async Task<Consent> StartAsync(string? requestedTitle)
    {
        var consent = await _askConsent();
        if (consent == Consent.None)
        {
            LastLog = "No se inició: elegiste «Nada».";
            return Consent.None;
        }
        IsBusy = true;
        try
        {
            Lines.Clear();
            OnPropertyChanged(nameof(SegmentCount));
            Consent = consent;
            var title = string.IsNullOrWhiteSpace(requestedTitle) ? $"Reunión {_now():yyyy-MM-dd HH:mm}" : requestedTitle.Trim();
            if (!await _session.StartAsync(consent, title)) return Consent.None;
            _startedAt = _now();
            IsRunning = true;
            LastLog = $"Reunión #{_session.MeetingId} iniciada";
            _clock = new Timer(_ => _ui(() => Elapsed = TimeFormat.Hms(_now() - _startedAt)), null, 0, 1000);
            _ = _session.Completion.ContinueWith(_ => _ui(OnSessionEnded), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            LastLog = $"No se pudo iniciar: {ex.Message}";
            Consent = Consent.None;
            return Consent.None;
        }
        finally
        {
            IsBusy = false;
        }
        return consent;
    }

    private async Task StopAsync()
    {
        IsBusy = true;
        try { await _session.StopAsync(); }
        finally
        {
            OnSessionEnded();
            IsBusy = false;
        }
    }

    private void OnSessionEnded()
    {
        if (!IsRunning) return;
        _clock?.Dispose();
        _clock = null;
        IsRunning = false;
        var p50 = _session.Stats is { Latencies.Count: > 0 } st
            ? st.Latencies.Order().ElementAt(st.Latencies.Count / 2).TotalSeconds
            : 0;
        LastLog = $"Reunión #{_session.MeetingId} guardada · {SegmentCount} segmentos · latencia p50 {p50:F1} s";
    }
}
