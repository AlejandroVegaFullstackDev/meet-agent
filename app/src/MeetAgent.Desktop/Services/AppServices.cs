using MeetAgent.Audio.Linux;
using MeetAgent.Audio.Windows;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;
using MeetAgent.Meet.Vexa;
using MeetAgent.Storage.Sqlite;
using MeetAgent.Stt.Whisper;

namespace MeetAgent.Desktop.Services;

/// <summary>Composición de la app: elige adaptadores por SO y transcriptor (Whisper o falso).</summary>
public sealed class AppServices : IAsyncDisposable
{
    private AppServices(ITranscriber transcriber, ITranscriptStore store, MeetingSession session, Func<BotMeetingSession>? botFactory)
    {
        BotFactory = botFactory;
        Transcriber = transcriber;
        Store = store;
        Session = session;
    }

    public ITranscriber Transcriber { get; }
    public ITranscriptStore Store { get; }
    public MeetingSession Session { get; }

    /// <summary>Sesión del bot de Meet si Vexa está levantado (deploy/vexa/up.sh); null si no.</summary>
    public Func<BotMeetingSession>? BotFactory { get; }

    public static string Root => AppPaths.Root;
    public static string DataDir => AppPaths.Data;

    /// <summary>
    /// <c>--fake</c> fuerza el transcriptor falso; <c>--model medium-q5_0</c> cambia de modelo.
    /// Si el modelo no existe, cae al falso y lo avisa en el nombre.
    /// </summary>
    public static AppServices Create(string[] args)
    {
        IAudioSourceFactory sources = OperatingSystem.IsWindows() ? new WindowsAudioSourceFactory()
            : OperatingSystem.IsLinux() ? new LinuxAudioSourceFactory()
            : throw new PlatformNotSupportedException("Solo Windows y Linux.");

        var modelName = ArgValue(args, "--model") ?? "small-q5_1";
        var modelPath = AppPaths.Model(modelName);
        ITranscriber transcriber;
        if (args.Contains("--fake")) transcriber = new FakeTranscriber();
        else if (!File.Exists(modelPath)) transcriber = new FakeTranscriber(reason: $"no está {modelPath}");
        else
        {
            // Whisper se carga recién al empezar una reunión local (no al abrir la app).
            transcriber = new LazyTranscriber(modelName,
                () => new WhisperTranscriber(modelPath, WhisperTranscriber.DefaultThreads, audioContextSeconds: 6));
        }

        var store = new SqliteTranscriptStore($"Data Source={AppPaths.Database}");
        var recordings = new WavRecordingSinkFactory(Path.Combine(DataDir, "recordings"));
        var session = new MeetingSession(sources, transcriber, new EnergyVad(), store, recordings);
        var vexa = VexaConfig.Load(AppPaths.Root);
        Func<BotMeetingSession>? bot = vexa is null ? null : () => new BotMeetingSession(vexa.CreateClient(), store);
        return new AppServices(transcriber, store, session, bot);
    }

    private static string? ArgValue(string[] args, string key)
    {
        var i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }


    public async ValueTask DisposeAsync()
    {
        if (Session.IsRunning) await Session.StopAsync();
        await Transcriber.DisposeAsync();
        Store.Dispose();
    }
}
