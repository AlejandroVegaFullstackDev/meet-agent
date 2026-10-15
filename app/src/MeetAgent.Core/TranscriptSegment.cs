namespace MeetAgent.Core;

/// <summary>Texto transcrito con tiempos absolutos desde el inicio de la reunión.</summary>
public sealed record TranscriptSegment(Speaker Speaker, TimeSpan Start, TimeSpan End, string Text);

public sealed record Meeting(long Id, string Title, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Consent);
