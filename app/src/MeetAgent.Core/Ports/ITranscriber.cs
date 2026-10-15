namespace MeetAgent.Core.Ports;

/// <summary>Un fragmento reconocido, con tiempos relativos al inicio del audio entregado.</summary>
/// <param name="Probability">Probabilidad media de los tokens (0-1), si el motor la da.</param>
public sealed record RecognizedText(TimeSpan Start, TimeSpan End, string Text, double? Probability = null);

public interface ITranscriber : IAsyncDisposable
{
    string Name { get; }

    /// <summary>Transcribe audio float normalizado a 16 kHz mono. No es reentrante: el llamador serializa.</summary>
    Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct);
}
