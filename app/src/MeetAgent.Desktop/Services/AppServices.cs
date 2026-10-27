using MeetAgent.Ai;
using MeetAgent.Audio.Linux;
using MeetAgent.Audio.Windows;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;
using MeetAgent.Meet.Vexa;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Desktop.Services;

/// <summary>
/// Composición de la app: captura por SO, SQLite y la IA que configure el usuario (pestaña IA).
/// No hay modelos locales: transcribe y analiza el proveedor elegido (o el servidor propio del usuario).
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    private readonly AiSettingsStore _settingsStore;
    private readonly bool _fake;

    private AppServices(SwitchableTranscriber transcriber, SqliteTranscriptStore store, MeetingSession session,
        Func<BotMeetingSession>? botFactory, AiSettingsStore settingsStore, bool fake)
    {
        BotFactory = botFactory;
        Transcriber = transcriber;
        Store = store;
        Insights = store;
        Session = session;
        _settingsStore = settingsStore;
        _fake = fake;
        ApplySettings(settingsStore.Load());
    }

    public SwitchableTranscriber Transcriber { get; }
    public ITranscriptStore Store { get; }
    public IInsightsStore Insights { get; }
    public MeetingSession Session { get; }
    public AiSettings Settings { get; private set; } = new();

    /// <summary>Sesión del bot de Meet si Vexa está levantado (deploy/vexa/up.sh); null si no.</summary>
    public Func<BotMeetingSession>? BotFactory { get; }

    public static string Root => AppPaths.Root;
    public static string DataDir => AppPaths.Data;

    /// <summary><c>--fake</c>: transcriptor de ejemplo, para desarrollar la UI sin gastar en una API.</summary>
    public static AppServices Create(string[] args)
    {
        IAudioSourceFactory sources = OperatingSystem.IsWindows() ? new WindowsAudioSourceFactory()
            : OperatingSystem.IsLinux() ? new LinuxAudioSourceFactory()
            : throw new PlatformNotSupportedException("Solo Windows y Linux.");

        var transcriber = new SwitchableTranscriber();
        var store = new SqliteTranscriptStore($"Data Source={AppPaths.Database}");
        var recordings = new WavRecordingSinkFactory(Path.Combine(DataDir, "recordings"));
        var session = new MeetingSession(sources, transcriber, new EnergyVad(), store, recordings);
        var vexa = VexaConfig.Load(AppPaths.Root);
        Func<BotMeetingSession>? bot = vexa is null ? null : () => new BotMeetingSession(vexa.CreateClient(), store);
        return new AppServices(transcriber, store, session, bot, new AiSettingsStore(Path.Combine(DataDir, "ia.json")), args.Contains("--fake"));
    }

    /// <summary>Guarda la configuración de la pestaña IA y cambia el transcriptor (solo si no hay reunión en curso).</summary>
    public void SaveSettings(AiSettings settings)
    {
        _settingsStore.Save(settings);
        ApplySettings(settings);
    }

    private void ApplySettings(AiSettings s)
    {
        Settings = s;
        if (Session.IsRunning) return;
        if (_fake) { Transcriber.Set(new FakeTranscriber()); return; }
        if (s.Transcription is null) { Transcriber.Set(null, "conecta un transcriptor en Ajustes (Groq es gratis)"); return; }
        try { Transcriber.Set(AiFactory.CreateTranscriber(s.Transcription)); }
        catch (AiException ex) { Transcriber.Set(null, ex.Message.TrimEnd('.')); }
    }

    /// <summary>La IA de análisis configurada, o null si falta configurarla.</summary>
    public IChatModel? CreateChat() => Settings.Chat is { IsUsable: true } c ? AiFactory.CreateChat(c) : null;

    public async ValueTask DisposeAsync()
    {
        if (Session.IsRunning) await Session.StopAsync();
        await Transcriber.DisposeAsync();
        Store.Dispose();
    }
}
