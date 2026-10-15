using MeetAgent.Core.Ports;
using Whisper.net;

namespace MeetAgent.Stt.Whisper;

/// <summary>
/// Whisper.net (whisper.cpp, runtime CPU). Un único modelo y procesador cargados una vez;
/// el pipeline serializa las llamadas. Español fijo y sin condicionar al texto previo.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;

    /// <param name="audioContextSeconds">
    /// Ventana del encoder. Whisper siempre codifica 30 s (1500 tramas) aunque el chunk dure 5 s;
    /// reducirla a la duración del chunk (+ margen) acelera mucho el encoder. 0 = 30 s completos.
    /// </param>
    public WhisperTranscriber(string modelPath, int threads, string language = "es", double audioContextSeconds = 0)
    {
        AudioContext = audioContextSeconds > 0 ? Math.Min(1500, (int)Math.Ceiling(audioContextSeconds * 50)) : 0;
        Name = $"{Path.GetFileNameWithoutExtension(modelPath)} ({threads} hilos, audio_ctx {(AudioContext == 0 ? "1500" : AudioContext)})";
        _factory = WhisperFactory.FromPath(modelPath);
        var builder = _factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext()
            .WithThreads(threads)
            .WithNoSpeechThreshold(0.6f)
            // Un segmento por palabra (equivale a "-ml 1 -sow"): el pipeline alinea el solape por tiempo.
            .WithTokenTimestamps()
            .SplitOnWord()
            .WithMaxSegmentLength(1);
        if (AudioContext > 0) builder = builder.WithAudioContextSize(AudioContext);
        _processor = builder.Build();
    }

    public int AudioContext { get; }

    public string Name { get; }

    public static int DefaultThreads => Math.Max(1, Environment.ProcessorCount / 2);

    public async Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        var list = new List<RecognizedText>();
        await foreach (var s in _processor.ProcessAsync(samples, ct).ConfigureAwait(false))
            if (!string.IsNullOrWhiteSpace(s.Text))
                list.Add(new RecognizedText(s.Start, s.End, s.Text.Trim()));
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync().ConfigureAwait(false);
        _factory.Dispose();
    }
}
