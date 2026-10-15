using MeetAgent.Core;

namespace MeetAgent.Meet.Vexa;

/// <summary>Un segmento tal como lo devuelve <c>GET /transcripts/google_meet/{code}</c>.</summary>
public sealed record VexaSegment(string SegmentId, string? Speaker, string Text, double Start, double End, bool Completed);

/// <summary>
/// Convierte consultas sucesivas de la transcripción de Vexa en segmentos nuevos para MeetAgent:
/// ignora los borradores (<c>completed: false</c>), entrega cada segmento confirmado una sola vez
/// y marca como «Yo» al participante cuyo nombre coincide con el del usuario.
/// </summary>
public sealed class TranscriptSync(string? selfName = null, DateTimeOffset? meetingStart = null)
{
    /// <summary>Por encima de esto un tiempo es Unix (segundos desde 1970), no relativo a la reunión.</summary>
    private const double EpochThreshold = 1_000_000_000;
    private readonly HashSet<string> _seen = new();
    private double? _origin = meetingStart?.ToUnixTimeMilliseconds() / 1000.0;

    public IReadOnlyList<TranscriptSegment> Take(IEnumerable<VexaSegment> segments)
    {
        var fresh = new List<TranscriptSegment>();
        foreach (var s in segments.Where(s => s.Completed).OrderBy(s => s.Start))
        {
            if (string.IsNullOrWhiteSpace(s.Text) || !_seen.Add(s.SegmentId)) continue;
            var name = string.IsNullOrWhiteSpace(s.Speaker) ? null : s.Speaker.Trim();
            var isMe = name is not null && selfName is not null && string.Equals(name, selfName.Trim(), StringComparison.OrdinalIgnoreCase);
            var (start, end) = Relative(s.Start, s.End);
            fresh.Add(new TranscriptSegment(isMe ? Speaker.Yo : Speaker.Otros,
                TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), s.Text.Trim(), name));
        }
        return fresh;
    }

    // Vexa v0.12 da tiempos Unix; si no sabemos cuándo empezó, el primer segmento marca el cero.
    private (double, double) Relative(double start, double end)
    {
        if (start < EpochThreshold) return (start, end);
        _origin ??= start;
        return (Math.Max(0, start - _origin.Value), Math.Max(0, end - _origin.Value));
    }
}
