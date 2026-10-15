using MeetAgent.Core.Ports;
using Whisper.net;

namespace MeetAgent.Stt.Whisper;

/// <summary>
/// Whisper.net (whisper.cpp, runtime CPU). El modelo se carga una vez (<see cref="WhisperFactory"/>);
/// los procesadores se crean por tamaño de ventana del encoder y comparten esos pesos.
/// Español fijo, sin condicionar al texto previo, un segmento por palabra con su probabilidad.
/// No es reentrante: el llamador serializa (el pipeline en vivo y el servidor STT lo hacen).
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly WhisperFactory _factory;
    private readonly int _threads;
    private readonly string _language;
    private readonly bool _adaptive;
    private readonly Dictionary<int, WhisperProcessor> _processors = new();

    /// <param name="audioContextSeconds">
    /// Ventana del encoder. Whisper siempre codifica 30 s (1500 tramas) aunque el audio dure 5 s;
    /// reducirla a la duración del audio (+ margen) acelera mucho el encoder. 0 = 30 s completos.
    /// </param>
    /// <param name="adaptiveContext">
    /// Si es true, la ventana se elige por llamada según la duración del audio (en tramos de 5 s),
    /// para entradas de largo variable como las del servidor STT.
    /// </param>
    public WhisperTranscriber(string modelPath, int threads, string language = "es", double audioContextSeconds = 0,
        bool adaptiveContext = false)
    {
        _threads = threads;
        _language = language;
        _adaptive = adaptiveContext;
        // Modo fijo: ventana exacta (6 s → 300 tramas, lo medido en H2). El adaptativo usa tramos de 5 s.
        AudioContext = audioContextSeconds > 0 ? Math.Min(1500, (int)Math.Ceiling(audioContextSeconds * 50)) : 0;
        var ctxLabel = adaptiveContext ? "adaptativo" : AudioContext == 0 ? "1500" : AudioContext.ToString();
        Name = $"{Path.GetFileNameWithoutExtension(modelPath)} ({threads} hilos, audio_ctx {ctxLabel})";
        _factory = WhisperFactory.FromPath(modelPath);
        Processor(AudioContext);    // falla aquí (y no en la primera reunión) si el modelo o la nativa no cargan
    }

    public string Name { get; }
    public int AudioContext { get; }

    public static int DefaultThreads => Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Tramas del encoder para cubrir <paramref name="seconds"/> + 1 s, redondeado a tramos de 5 s.</summary>
    public static int ContextFor(double seconds)
    {
        var s = Math.Ceiling((seconds + 1) / 5) * 5;
        return s >= 30 ? 0 : (int)(s * 50);
    }

    private WhisperProcessor Processor(int audioCtx)
    {
        if (_processors.TryGetValue(audioCtx, out var p)) return p;
        var builder = _factory.CreateBuilder()
            .WithLanguage(_language)
            .WithNoContext()
            .WithThreads(_threads)
            .WithNoSpeechThreshold(0.6f)
            .WithProbabilities()   // sin esto Probability llega en 0 (el bot de Vexa descartaría todo)
            // Un segmento por palabra (equivale a "-ml 1 -sow"): el pipeline alinea el solape por tiempo.
            .WithTokenTimestamps()
            .SplitOnWord()
            .WithMaxSegmentLength(1);
        if (audioCtx > 0) builder = builder.WithAudioContextSize(audioCtx);
        return _processors[audioCtx] = builder.Build();
    }

    public async Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        var ctx = _adaptive ? ContextFor(samples.Length / 16000.0) : AudioContext;
        var list = new List<RecognizedText>();
        await foreach (var s in Processor(ctx).ProcessAsync(samples, ct).ConfigureAwait(false))
            if (!string.IsNullOrWhiteSpace(s.Text))
                list.Add(new RecognizedText(s.Start, s.End, s.Text.Trim(), s.Probability));
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _processors.Values) await p.DisposeAsync().ConfigureAwait(false);
        _factory.Dispose();
    }
}
