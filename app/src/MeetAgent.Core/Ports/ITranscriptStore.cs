namespace MeetAgent.Core.Ports;

/// <summary>Segmento con su id en el store (sirve de cursor para leer en vivo).</summary>
public sealed record StoredSegment(long Id, TranscriptSegment Segment);

public sealed record SearchHit(long MeetingId, string MeetingTitle, TranscriptSegment Segment, string Snippet);

public interface ITranscriptStore : IDisposable
{
    long StartMeeting(string title, DateTimeOffset startedAt, string consent);
    void EndMeeting(long meetingId, DateTimeOffset endedAt);
    void AddSegment(long meetingId, TranscriptSegment segment);
    Meeting? GetMeeting(long meetingId);
    IReadOnlyList<Meeting> ListMeetings();
    IReadOnlyList<TranscriptSegment> GetSegments(long meetingId);
    IReadOnlyList<SearchHit> Search(string query, int limit = 20);

    /// <summary>Segmentos con id mayor que <paramref name="afterId"/>, en orden de llegada.</summary>
    IReadOnlyList<StoredSegment> GetSegmentsAfter(long meetingId, long afterId, int limit = 500);

    /// <summary>La reunión más reciente que no ha terminado, o null.</summary>
    Meeting? GetLatestOpenMeeting();
}
