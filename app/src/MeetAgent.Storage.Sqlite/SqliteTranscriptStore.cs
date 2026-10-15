using System.Globalization;
using MeetAgent.Core;
using MeetAgent.Core.Ports;
using Microsoft.Data.Sqlite;

namespace MeetAgent.Storage.Sqlite;

/// <summary>
/// Reuniones y segmentos en SQLite con índice FTS5 (unicode61, sin tildes) sincronizado por triggers.
/// </summary>
public sealed class SqliteTranscriptStore : ITranscriptStore
{
    private readonly SqliteConnection _db;

    public SqliteTranscriptStore(string connectionString)
    {
        _db = new SqliteConnection(connectionString);
        _db.Open();
        Exec("""
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS meetings (
                id         INTEGER PRIMARY KEY,
                title      TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at   TEXT,
                consent    TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS segments (
                id         INTEGER PRIMARY KEY,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                speaker    TEXT NOT NULL,
                start_ms   INTEGER NOT NULL,
                end_ms     INTEGER NOT NULL,
                text       TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_segments_meeting ON segments(meeting_id, start_ms);
            CREATE VIRTUAL TABLE IF NOT EXISTS segments_fts USING fts5(
                text, content='segments', content_rowid='id', tokenize='unicode61 remove_diacritics 2');
            CREATE TRIGGER IF NOT EXISTS segments_ai AFTER INSERT ON segments BEGIN
                INSERT INTO segments_fts(rowid, text) VALUES (new.id, new.text);
            END;
            CREATE TRIGGER IF NOT EXISTS segments_ad AFTER DELETE ON segments BEGIN
                INSERT INTO segments_fts(segments_fts, rowid, text) VALUES ('delete', old.id, old.text);
            END;
            CREATE TRIGGER IF NOT EXISTS segments_au AFTER UPDATE ON segments BEGIN
                INSERT INTO segments_fts(segments_fts, rowid, text) VALUES ('delete', old.id, old.text);
                INSERT INTO segments_fts(rowid, text) VALUES (new.id, new.text);
            END;
            """);
        Migrate();
    }

    /// <summary>Migraciones aditivas sobre bases existentes.</summary>
    private void Migrate()
    {
        using var cmd = Cmd("SELECT COUNT(*) FROM pragma_table_info('segments') WHERE name = 'speaker_name'");
        if ((long)cmd.ExecuteScalar()! == 0) Exec("ALTER TABLE segments ADD COLUMN speaker_name TEXT");
    }

    public long StartMeeting(string title, DateTimeOffset startedAt, string consent)
    {
        using var cmd = Cmd("INSERT INTO meetings(title, started_at, consent) VALUES ($t, $s, $c) RETURNING id",
            ("$t", title), ("$s", startedAt.ToString("o")), ("$c", consent));
        return (long)cmd.ExecuteScalar()!;
    }

    public void EndMeeting(long meetingId, DateTimeOffset endedAt)
    {
        using var cmd = Cmd("UPDATE meetings SET ended_at = $e WHERE id = $id", ("$e", endedAt.ToString("o")), ("$id", meetingId));
        cmd.ExecuteNonQuery();
    }

    public void AddSegment(long meetingId, TranscriptSegment s)
    {
        using var cmd = Cmd("INSERT INTO segments(meeting_id, speaker, start_ms, end_ms, text, speaker_name) VALUES ($m, $sp, $st, $en, $tx, $name)",
            ("$m", meetingId), ("$sp", s.Speaker.ToString()), ("$st", (long)s.Start.TotalMilliseconds),
            ("$en", (long)s.End.TotalMilliseconds), ("$tx", s.Text), ("$name", (object?)s.SpeakerName ?? DBNull.Value));
        cmd.ExecuteNonQuery();
    }

    public Meeting? GetMeeting(long meetingId)
    {
        using var cmd = Cmd("SELECT id, title, started_at, ended_at, consent FROM meetings WHERE id = $id", ("$id", meetingId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadMeeting(r) : null;
    }

    public IReadOnlyList<Meeting> ListMeetings()
    {
        using var cmd = Cmd("SELECT id, title, started_at, ended_at, consent FROM meetings ORDER BY started_at DESC, id DESC");
        using var r = cmd.ExecuteReader();
        var list = new List<Meeting>();
        while (r.Read()) list.Add(ReadMeeting(r));
        return list;
    }

    public IReadOnlyList<TranscriptSegment> GetSegments(long meetingId)
    {
        using var cmd = Cmd("SELECT speaker, start_ms, end_ms, text, speaker_name FROM segments WHERE meeting_id = $m ORDER BY start_ms, id", ("$m", meetingId));
        using var r = cmd.ExecuteReader();
        var list = new List<TranscriptSegment>();
        while (r.Read()) list.Add(ReadSegment(r, 0));
        return list;
    }

    public IReadOnlyList<StoredSegment> GetSegmentsAfter(long meetingId, long afterId, int limit = 500)
    {
        using var cmd = Cmd("""
            SELECT speaker, start_ms, end_ms, text, speaker_name, id FROM segments
            WHERE meeting_id = $m AND id > $after ORDER BY id LIMIT $n
            """, ("$m", meetingId), ("$after", afterId), ("$n", limit));
        using var r = cmd.ExecuteReader();
        var list = new List<StoredSegment>();
        while (r.Read()) list.Add(new StoredSegment(r.GetInt64(5), ReadSegment(r, 0)));
        return list;
    }

    public Meeting? GetLatestOpenMeeting()
    {
        using var cmd = Cmd("SELECT id, title, started_at, ended_at, consent FROM meetings WHERE ended_at IS NULL ORDER BY started_at DESC, id DESC LIMIT 1");
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadMeeting(r) : null;
    }

    /// <summary>
    /// Sintaxis FTS5 (prefijo*, "frase", AND/OR). Si la consulta no es válida, se reintenta
    /// tratando cada palabra como literal.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string query, int limit = 20)
    {
        try { return RunSearch(query, limit); }
        catch (SqliteException) { }
        var literal = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
        if (literal.Length == 0) return [];
        try { return RunSearch(literal, limit); }
        catch (SqliteException) { return []; }
    }

    private List<SearchHit> RunSearch(string match, int limit)
    {
        using var cmd = Cmd("""
            SELECT s.speaker, s.start_ms, s.end_ms, s.text, s.speaker_name, m.id, m.title,
                   snippet(segments_fts, 0, '[', ']', '…', 12)
            FROM segments_fts
            JOIN segments s ON s.id = segments_fts.rowid
            JOIN meetings m ON m.id = s.meeting_id
            WHERE segments_fts MATCH $q
            ORDER BY bm25(segments_fts), m.started_at DESC
            LIMIT $n
            """, ("$q", match), ("$n", limit));
        using var r = cmd.ExecuteReader();
        var list = new List<SearchHit>();
        while (r.Read()) list.Add(new SearchHit(r.GetInt64(5), r.GetString(6), ReadSegment(r, 0), r.GetString(7)));
        return list;
    }

    private static Meeting ReadMeeting(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1),
        DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture),
        r.IsDBNull(3) ? null : DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture),
        r.GetString(4));

    private static TranscriptSegment ReadSegment(SqliteDataReader r, int o) => new(
        Enum.Parse<Speaker>(r.GetString(o)),
        TimeSpan.FromMilliseconds(r.GetInt64(o + 1)),
        TimeSpan.FromMilliseconds(r.GetInt64(o + 2)),
        r.GetString(o + 3),
        r.IsDBNull(o + 4) ? null : r.GetString(o + 4));

    private SqliteCommand Cmd(string sql, params (string Name, object Value)[] ps)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }

    private void Exec(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
