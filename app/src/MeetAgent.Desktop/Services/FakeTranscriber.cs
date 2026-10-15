using MeetAgent.Core.Ports;

namespace MeetAgent.Desktop.Services;

/// <summary>
/// Transcriptor falso para desarrollar la UI sin cargar Whisper: devuelve frases de ejemplo
/// en orden, con una demora parecida a la de small en este equipo.
/// </summary>
public sealed class FakeTranscriber(TimeSpan? delay = null) : ITranscriber
{
    private static readonly string[] Lines =
    [
        "Buenos días a todos, gracias por conectarse.",
        "Hoy revisamos el avance de la aplicación de escritorio.",
        "La captura de audio ya funciona en Linux.",
        "Falta probarla en un equipo con Windows.",
        "¿Cuánto tarda en aparecer el texto en pantalla?",
        "Alrededor de un segundo y medio después de cada fragmento.",
        "Perfecto, sigamos con el siguiente punto.",
    ];

    private int _next;

    public string Name => "falso (sin Whisper)";

    public async Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        await Task.Delay(delay ?? TimeSpan.FromMilliseconds(600), ct);
        var text = Lines[_next++ % Lines.Length];
        return [new RecognizedText(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), text)];
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
