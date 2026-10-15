namespace MeetAgent.Core;

/// <summary>Texto transcrito con tiempos absolutos desde el inicio de la reunión.</summary>
/// <param name="SpeakerName">Nombre del participante cuando la fuente lo conoce (bot de Meet); null en la captura local.</param>
public sealed record TranscriptSegment(Speaker Speaker, TimeSpan Start, TimeSpan End, string Text, string? SpeakerName = null)
{
    /// <summary>Lo que se muestra: el nombre si lo hay, si no Yo/Otros.</summary>
    public string Label => string.IsNullOrWhiteSpace(SpeakerName) ? Speaker.ToString() : SpeakerName;
}

public sealed record Meeting(long Id, string Title, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Consent);
