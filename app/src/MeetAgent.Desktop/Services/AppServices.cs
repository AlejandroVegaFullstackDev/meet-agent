using MeetAgent.Audio.Linux;
using MeetAgent.Audio.Windows;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;
using MeetAgent.Storage.Sqlite;
using MeetAgent.Stt.Whisper;

namespace MeetAgent.Desktop.Services;

/// <summary>Composición de la app: elige adaptadores por SO y transcriptor (Whisper o falso).</summary>
public sealed class AppServices : IAsyncDisposable
{
    private AppServices(ITranscriber transcriber, ITranscriptStore store, MeetingSession session)
    {
        Transcriber = transcriber;
        Store = store;
        Session = session;
    }

    public ITranscriber Transcriber { get; }
    public ITranscriptStore Store { get; }
    public MeetingSession Session { get; }

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
            // Si falta la librería nativa o el modelo está dañado, la app arranca igual y lo muestra.
            try { transcriber = new WhisperTranscriber(modelPath, WhisperTranscriber.DefaultThreads, audioContextSeconds: 6); }
            catch (Exception ex) { transcriber = new FakeTranscriber(reason: $"Whisper no cargó: {ex.Message}"); }
        }

        var store = new SqliteTranscriptStore($"Data Source={AppPaths.Database}");
        var recordings = new WavRecordingSinkFactory(Path.Combine(DataDir, "recordings"));
        var session = new MeetingSession(sources, transcriber, new EnergyVad(), store, recordings);
        return new AppServices(transcriber, store, session);
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
