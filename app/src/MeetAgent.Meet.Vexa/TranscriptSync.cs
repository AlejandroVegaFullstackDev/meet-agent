using MeetAgent.Core;

namespace MeetAgent.Meet.Vexa;

/// <summary>Un segmento tal como lo devuelve <c>GET /transcripts/google_meet/{code}</c>.</summary>
public sealed record VexaSegment(string SegmentId, string? Speaker, string Text, double Start, double End, bool Completed);

/// <summary>
/// Convierte consultas sucesivas de la transcripción de Vexa en segmentos nuevos para MeetAgent:
/// ignora los borradores (<c>completed: false</c>), entrega cada segmento confirmado una sola vez
/// y marca como «Yo» al participante cuyo nombre coincide con el del usuario.
/// </summary>
public sealed class TranscriptSync(string? selfName = null)
{
    private readonly HashSet<string> _seen = new();

    public IReadOnlyList<TranscriptSegment> Take(IEnumerable<VexaSegment> segments)
    {
        var fresh = new List<TranscriptSegment>();
        foreach (var s in segments.Where(s => s.Completed).OrderBy(s => s.Start))
        {
            if (string.IsNullOrWhiteSpace(s.Text) || !_seen.Add(s.SegmentId)) continue;
            var name = string.IsNullOrWhiteSpace(s.Speaker) ? null : s.Speaker.Trim();
            var isMe = name is not null && selfName is not null && string.Equals(name, selfName.Trim(), StringComparison.OrdinalIgnoreCase);
            fresh.Add(new TranscriptSegment(isMe ? Speaker.Yo : Speaker.Otros,
                TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), s.Text.Trim(), name));
        }
        return fresh;
    }
}
