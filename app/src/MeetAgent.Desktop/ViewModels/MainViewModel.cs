using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Core;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Text;
using MeetAgent.Meet.Vexa;

namespace MeetAgent.Desktop.ViewModels;

/// <summary>Resultado de un inicio pedido por un agente: lo que eligió el usuario en el diálogo.</summary>
public sealed record AgentStartResult(Consent Consent, long? MeetingId, bool AlreadyRunning);

/// <summary>Páginas del shell (barra lateral + contenido).</summary>
public enum ShellPage
{
    Record,
    History,
    Connect,
    Settings,
}

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
    private readonly Func<BotMeetingSession>? _botFactory;
    private BotMeetingSession? _bot;
    private Timer? _clock;
    private DateTimeOffset _startedAt;

    /// <param name="botFactory">Crea la sesión del bot de Meet (Vexa); null si Vexa no está configurado.</param>
    public MainViewModel(MeetingSession session, string transcriberName, Func<Task<Consent>> askConsent,
        Action<Action>? ui = null, Func<DateTimeOffset>? now = null, Func<BotMeetingSession>? botFactory = null)
    {
        _session = session;
        _botFactory = botFactory;
        _askConsent = askConsent;
        _ui = ui ?? (a => a());
        _now = now ?? (() => DateTimeOffset.Now);
        _transcriberName = transcriberName;
        _session.SegmentReceived += OnSegment;
        _session.Log += msg => _ui(() => LastLog = msg);
    }

    private void OnSegment(TranscriptSegment seg) => _ui(() =>
    {
        Lines.Add(new TranscriptLine(TimeFormat.Hms(seg.Start), seg.Speaker, seg.Text, seg.SpeakerName));
        OnPropertyChanged(nameof(SegmentCount));
    });

    public ObservableCollection<TranscriptLine> Lines { get; } = new();

    /// <summary>Historial de todas las reuniones.</summary>
    public HistoryViewModel? History { get; init; }

    /// <summary>Onboarding «Conecta tu IA».</summary>
    public ConnectViewModel? Connect { get; init; }

    /// <summary>Ajustes de la app.</summary>
    public SettingsViewModel? Settings { get; init; }

    /// <summary>Quién transcribe (cambia al guardar los ajustes).</summary>
    [ObservableProperty] private string _transcriberName;

    /// <summary>Si devuelve texto, no se puede empezar y se muestra ese motivo (p. ej. falta configurar la IA).</summary>
    public Func<string?>? CannotStartReason { get; init; }

    /// <summary>Una reunión de la app terminó y quedó guardada (id en la base).</summary>
    public event Action<long>? MeetingEnded;
    public int SegmentCount => Lines.Count;

    /// <summary>Página visible en el shell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Page), nameof(IsRecordPage), nameof(IsHistoryPage),
        nameof(IsConnectPage), nameof(IsSettingsPage))]
    private ShellPage _currentPage;

    /// <summary>Contenido de la derecha: un view model, resuelto por los DataTemplates de App.axaml.</summary>
    public object? Page => CurrentPage switch
    {
        ShellPage.History => History,
        ShellPage.Connect => Connect,
        ShellPage.Settings => Settings,
        _ => this,
    };

    public bool IsRecordPage => CurrentPage == ShellPage.Record;
    public bool IsHistoryPage => CurrentPage == ShellPage.History;
    public bool IsConnectPage => CurrentPage == ShellPage.Connect;
    public bool IsSettingsPage => CurrentPage == ShellPage.Settings;

    /// <summary>Barra lateral estrecha (48 px) cuando la ventana no llega a 680 px.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isRail;

    public double SidebarWidth => IsRail ? 48 : 200;

    /// <summary>Botón de la barra lateral: el mismo que inicia o termina la reunión.</summary>
    public string SidebarActionText => IsBusy ? (IsRunning ? "Terminando…" : "Preparando…")
        : IsRunning ? "Terminar" : "Grabar";

    /// <summary>Estado de la IA en la barra lateral («Sin IA conectada» o «Claude ✓»).</summary>
    public bool HasUsableAi => Connect?.HasUsableAi == true;
    public string IaStatusText => Connect?.StatusText ?? "Sin IA conectada";

    /// <summary>
    /// Primer arranque: si no hay una IA usable abre «Conecta tu IA»; si ya la hay, Grabar.
    /// También engancha el aviso de cambios de la página de conexión.
    /// </summary>
    public void Start()
    {
        if (Connect is { } connect)
        {
            connect.Changed += RefreshAiState;
            connect.StartRequested += () => CurrentPage = ShellPage.Record;
        }
        RefreshAiState();
        CurrentPage = HasUsableAi ? ShellPage.Record : ShellPage.Connect;
    }

    private void RefreshAiState()
    {
        OnPropertyChanged(nameof(HasUsableAi));
        OnPropertyChanged(nameof(IaStatusText));
    }

    partial void OnCurrentPageChanged(ShellPage value)
    {
        // La extensión y el bot escriben desde otros procesos: se relee al abrir cada página.
        if (value == ShellPage.History) History?.Refresh();
        if (value == ShellPage.Settings) Settings?.Refresh();
    }

    [RelayCommand] private void ShowRecord() => CurrentPage = ShellPage.Record;
    [RelayCommand] private void ShowHistory() => CurrentPage = ShellPage.History;
    [RelayCommand] private void ShowConnect() => CurrentPage = ShellPage.Connect;
    [RelayCommand] private void ShowSettings() => CurrentPage = ShellPage.Settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(StatusText), nameof(SidebarActionText))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(StatusText), nameof(SidebarActionText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand), nameof(JoinBotCommand))]
    private bool _isBusy;

    /// <summary>Bot de Meet en la reunión (excluye la captura local y viceversa).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DiskText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand), nameof(JoinBotCommand), nameof(LeaveBotCommand))]
    private bool _isBotRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinBotCommand))]
    private string _meetLink = "";

    public bool CanUseBot => _botFactory is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DiskText))]
    private Consent _consent = Consent.None;

    [ObservableProperty] private string _elapsed = "00:00:00";
    [ObservableProperty] private string? _lastLog;

    public string ButtonText => IsBusy ? (IsRunning ? "Terminando…" : "Preparando…") : IsRunning ? "Terminar" : "Empezar";

    public string StatusText => IsBotRunning ? "En vivo · bot en el Meet"
        : IsRunning
        ? Consent == Consent.RecordAndTranscribe ? "En vivo · grabando y transcribiendo" : "En vivo · solo transcribiendo"
        : "Detenido";

    public string DiskText => IsBotRunning ? "Audio: no se guarda (el bot solo transcribe)"
        : Consent == Consent.RecordAndTranscribe ? "Audio: se guarda en data/" : "Audio: no se guarda";

    private bool CanToggle() => !IsBusy && !IsBotRunning;

    private bool CanJoinBot() => CanUseBot && !IsBusy && !IsRunning && !IsBotRunning && MeetLink.Trim().Length > 0;

    /// <summary>
    /// Envía el bot al Meet. Pegar el link y pulsar «Unir bot» es el consentimiento explícito;
    /// además el bot es visible para todos en la reunión y no graba audio (solo transcribe).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanJoinBot))]
    private async Task JoinBotAsync()
    {
        if (!MeetLink_IsValid(out var code))
        {
            LastLog = "Eso no parece un link de Google Meet (meet.google.com/abc-defg-hij).";
            return;
        }
        IsBusy = true;
        try
        {
            var bot = _botFactory!();
            bot.SegmentReceived += OnSegment;
            bot.Log += msg => _ui(() => LastLog = msg);
            Lines.Clear();
            OnPropertyChanged(nameof(SegmentCount));
            var id = await bot.StartAsync(code, $"Meet {_now():yyyy-MM-dd HH:mm}", CancellationToken.None);
            _bot = bot;
            IsBotRunning = true;
            _startedAt = _now();
            _clock = new Timer(_ => _ui(() => Elapsed = TimeFormat.Hms(_now() - _startedAt)), null, 0, 1000);
            LastLog = $"Reunión #{id}: bot pedido. Admítelo en el Meet.";
            _ = bot.Completion.ContinueWith(_ => _ui(OnBotEnded), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            LastLog = $"No se pudo enviar el bot: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool MeetLink_IsValid(out string code) => MeetAgent.Meet.Vexa.MeetLink.TryParse(MeetLink, out code);

    [RelayCommand(CanExecute = nameof(IsBotRunning))]
    private async Task LeaveBotAsync()
    {
        if (_bot is null) return;
        LastLog = "Sacando al bot…";
        await _bot.StopAsync();
        OnBotEnded();
    }

    private void OnBotEnded()
    {
        if (!IsBotRunning) return;
        _clock?.Dispose();
        _clock = null;
        IsBotRunning = false;
        _bot = null;
        LastLog = $"Reunión del bot guardada · {SegmentCount} segmentos";
    }

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
        if (CannotStartReason?.Invoke() is { } why)
        {
            LastLog = $"No se puede empezar: {why}.";
            return Consent.None;
        }
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
        if (_session.MeetingId is { } id) MeetingEnded?.Invoke(id);
    }
}
