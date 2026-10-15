namespace MeetAgent.Core.Ports;

/// <summary>Un fragmento reconocido, con tiempos relativos al inicio del audio entregado.</summary>
public sealed record RecognizedText(TimeSpan Start, TimeSpan End, string Text);

public interface ITranscriber : IAsyncDisposable
{
    string Name { get; }

    /// <summary>Transcribe audio float normalizado a 16 kHz mono. No es reentrante: el llamador serializa.</summary>
    Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct);
}
