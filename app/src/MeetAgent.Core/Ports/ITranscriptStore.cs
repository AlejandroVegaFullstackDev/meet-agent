namespace MeetAgent.Core.Ports;

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
}
