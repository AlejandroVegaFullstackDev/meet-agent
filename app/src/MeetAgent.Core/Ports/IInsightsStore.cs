namespace MeetAgent.Core.Ports;

/// <summary>Análisis de una reunión hecho por la IA: el JSON de <see cref="Analysis.MeetingInsights"/> y quién lo hizo.</summary>
public sealed record StoredInsights(long MeetingId, string Json, string Model, DateTimeOffset CreatedAt);

/// <summary>Guarda el último análisis de cada reunión (aparte de <see cref="ITranscriptStore"/> para no tocar sus dobles de prueba).</summary>
public interface IInsightsStore
{
    void SaveInsights(long meetingId, string json, string model, DateTimeOffset createdAt);
    StoredInsights? GetInsights(long meetingId);
}
