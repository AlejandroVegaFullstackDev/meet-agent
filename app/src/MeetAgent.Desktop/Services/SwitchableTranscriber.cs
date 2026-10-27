using MeetAgent.Ai;
using MeetAgent.Core.Ports;

namespace MeetAgent.Desktop.Services;

/// <summary>
/// El transcriptor que usa la sesión, cambiable desde la pestaña IA sin reiniciar la app (entre reuniones).
/// Sin configurar, no transcribe: <see cref="Problem"/> dice qué falta.
/// </summary>
public sealed class SwitchableTranscriber : ITranscriber
{
    private ITranscriber? _inner;
    private string? _problem = "falta configurar la transcripción en la pestaña IA";

    public string Name => _inner?.Name ?? "sin configurar";

    /// <summary>Por qué no se puede transcribir, o null si está listo.</summary>
    public string? Problem => _inner is null ? _problem : null;

    public void Set(ITranscriber? transcriber, string? problem = null)
    {
        var old = _inner;
        _inner = transcriber;
        _problem = problem ?? "falta configurar la transcripción en la pestaña IA";
        if (old is not null && !ReferenceEquals(old, transcriber)) _ = old.DisposeAsync();
    }

    public Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct) =>
        (_inner ?? throw new AiException($"No se puede transcribir: {_problem}.")).TranscribeAsync(samples, ct);

    public ValueTask DisposeAsync() => _inner?.DisposeAsync() ?? ValueTask.CompletedTask;
}
