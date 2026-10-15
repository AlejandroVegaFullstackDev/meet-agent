namespace MeetAgent.Core.Ports;

/// <summary>Fuente de audio en el formato interno (PCM 16 kHz mono 16 bits).</summary>
public interface IAudioSource : IAsyncDisposable
{
    Speaker Speaker { get; }
    string Description { get; }

    /// <summary>Se dispara con mensajes de diagnóstico (p. ej. cambio de dispositivo por defecto).</summary>
    event Action<string>? Log;

    /// <summary>Lee bloques de muestras hasta que se cancele o la fuente termine.</summary>
    IAsyncEnumerable<short[]> ReadAsync(CancellationToken ct);
}
