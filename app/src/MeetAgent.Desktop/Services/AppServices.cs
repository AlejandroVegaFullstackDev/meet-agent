using MeetAgent.Audio.Linux;
using MeetAgent.Audio.Windows;
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

    public static string Root { get; } = FindRoot();
    public static string DataDir => Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;

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
        var modelPath = Path.Combine(Root, "models", $"ggml-{modelName}.bin");
        ITranscriber transcriber = args.Contains("--fake") || !File.Exists(modelPath)
            ? new FakeTranscriber()
            : new WhisperTranscriber(modelPath, WhisperTranscriber.DefaultThreads, audioContextSeconds: 6);

        var store = new SqliteTranscriptStore($"Data Source={Path.Combine(DataDir, "meetagent.db")}");
        var recordings = new WavRecordingSinkFactory(Path.Combine(DataDir, "recordings"));
        var session = new MeetingSession(sources, transcriber, new EnergyVad(), store, recordings);
        return new AppServices(transcriber, store, session);
    }

    private static string? ArgValue(string[] args, string key)
    {
        var i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string FindRoot()
    {
        if (Environment.GetEnvironmentVariable("MEETAGENT_HOME") is { Length: > 0 } env) return env;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "models"))) return d.FullName;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetAgent");
    }

    public async ValueTask DisposeAsync()
    {
        if (Session.IsRunning) await Session.StopAsync();
        await Transcriber.DisposeAsync();
        Store.Dispose();
    }
}
