using MeetAgent.Core.Ports;

namespace MeetAgent.Desktop.Services;

/// <summary>
/// Carga el modelo recién en la primera transcripción: abrir la app para ver el historial
/// (o usarla solo con la extensión de subtítulos) no gasta los ~500 MB de Whisper.
/// </summary>
public sealed class LazyTranscriber(string label, Func<ITranscriber> create) : ITranscriber
{
    private readonly Lazy<ITranscriber> _inner = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Name => _inner.IsValueCreated ? _inner.Value.Name : $"{label} (se carga al empezar)";

    public Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct) =>
        _inner.Value.TranscribeAsync(samples, ct);

    public ValueTask DisposeAsync() => _inner.IsValueCreated ? _inner.Value.DisposeAsync() : ValueTask.CompletedTask;
}
