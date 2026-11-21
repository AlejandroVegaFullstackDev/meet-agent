using System.Globalization;
using System.Text.RegularExpressions;
using MeetAgent.Core;
using MeetAgent.Core.Ports;
using Microsoft.Data.Sqlite;

namespace MeetAgent.Storage.Sqlite;

/// <summary>
/// Reuniones y segmentos en SQLite con índice FTS5 (unicode61, sin tildes) sincronizado por triggers.
/// </summary>
public sealed class SqliteTranscriptStore : ITranscriptStore, IInsightsStore
{
    private readonly SqliteConnection _db;
    // Las fechas existentes conservan su offset y los siete dígitos de precisión del formato "o".
    private const string MeetingOrder = """
        unixepoch(substr(m.started_at, 1, 19) || substr(m.started_at, 28)) DESC,
        substr(m.started_at, 21, 7) DESC, m.id DESC
        """;
    private static readonly Regex SearchTerms = new(@"""(?:[^""]|"""")*""\*?|[^\s""]+", RegexOptions.NonBacktracking);
    private static readonly Regex SearchableText = new(@"[\p{L}\p{N}\p{Co}]", RegexOptions.NonBacktracking);

    public SqliteTranscriptStore(string connectionString)
    {
        _db = new SqliteConnection(connectionString);
        try
        {
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
            CREATE TABLE IF NOT EXISTS insights (
                meeting_id INTEGER PRIMARY KEY REFERENCES meetings(id) ON DELETE CASCADE,
                json       TEXT NOT NULL,
                model      TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TRIGGER IF NOT EXISTS segments_au AFTER UPDATE ON segments BEGIN
                INSERT INTO segments_fts(segments_fts, rowid, text) VALUES ('delete', old.id, old.text);
                INSERT INTO segments_fts(rowid, text) VALUES (new.id, new.text);
            END;
            CREATE TABLE IF NOT EXISTS caption_captures (
                meeting_id INTEGER PRIMARY KEY REFERENCES meetings(id) ON DELETE CASCADE,
                capture_id TEXT NOT NULL UNIQUE,
                owner      TEXT NOT NULL,
                finished   INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS caption_events (
                meeting_id INTEGER NOT NULL REFERENCES caption_captures(meeting_id) ON DELETE CASCADE,
                event_id   TEXT NOT NULL,
                segment_id INTEGER NOT NULL REFERENCES segments(id) ON DELETE CASCADE,
                revision   INTEGER NOT NULL,
                PRIMARY KEY(meeting_id, event_id)
            );
            """);
            Migrate();
        }
        catch
        {
            _db.Dispose();
            throw;
        }
    }

    /// <summary>Migraciones aditivas sobre bases existentes.</summary>
    private void Migrate()
    {
        using var cmd = Cmd("SELECT COUNT(*) FROM pragma_table_info('segments') WHERE name = 'speaker_name'");
        if ((long)cmd.ExecuteScalar()! != 0) return;
        using var transaction = _db.BeginTransaction(deferred: false);
        cmd.Transaction = transaction;
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            cmd.CommandText = "ALTER TABLE segments ADD COLUMN speaker_name TEXT";
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public long StartMeeting(string title, DateTimeOffset startedAt, string consent)
        => InsertMeeting(title, startedAt, consent);

    private long InsertMeeting(string title, DateTimeOffset startedAt, string consent, SqliteTransaction? tx = null)
    {
        using var cmd = Cmd("INSERT INTO meetings(title, started_at, consent) VALUES ($t, $s, $c) RETURNING id",
            ("$t", title), ("$s", startedAt.ToString("o")), ("$c", consent));
        cmd.Transaction = tx;
        return (long)cmd.ExecuteScalar()!;
    }

    public void EndMeeting(long meetingId, DateTimeOffset endedAt)
    {
        using var cmd = Cmd("UPDATE meetings SET ended_at = $e WHERE id = $id", ("$e", endedAt.ToString("o")), ("$id", meetingId));
        cmd.ExecuteNonQuery();
    }

    public void AddSegment(long meetingId, TranscriptSegment s) => InsertSegment(meetingId, s);

    private long InsertSegment(long meetingId, TranscriptSegment s, SqliteTransaction? tx = null)
    {
        using var cmd = Cmd("INSERT INTO segments(meeting_id, speaker, start_ms, end_ms, text, speaker_name) VALUES ($m, $sp, $st, $en, $tx, $name) RETURNING id",
            ("$m", meetingId), ("$sp", s.Speaker.ToString()), ("$st", (long)s.Start.TotalMilliseconds),
            ("$en", (long)s.End.TotalMilliseconds), ("$tx", s.Text), ("$name", (object?)s.SpeakerName ?? DBNull.Value));
        cmd.Transaction = tx;
        return (long)cmd.ExecuteScalar()!;
    }

    public long StartCaptionCapture(string title, DateTimeOffset startedAt, string captureId, string owner)
    {
        using var tx = _db.BeginTransaction();
        var id = InsertMeeting(title, startedAt, "subtitulos", tx);
        using var cmd = Cmd("INSERT INTO caption_captures(meeting_id, capture_id, owner) VALUES ($m, $c, $o)", ("$m", id), ("$c", captureId), ("$o", owner));
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
        tx.Commit();
        return id;
    }

    public bool AddCaption(long meetingId, string captureId, string eventId, long revision, TranscriptSegment segment)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = Cmd("""
            SELECT e.segment_id, e.revision FROM caption_captures c
            LEFT JOIN caption_events e ON e.meeting_id = c.meeting_id AND e.event_id = $e
            WHERE c.meeting_id = $m AND c.capture_id = $c
            """, ("$m", meetingId), ("$c", captureId), ("$e", eventId));
        cmd.Transaction = tx;
        long? segmentId;
        long previousRevision;
        using (var result = cmd.ExecuteReader())
        {
            if (!result.Read()) return false;
            segmentId = result.IsDBNull(0) ? null : result.GetInt64(0);
            previousRevision = result.IsDBNull(1) ? -1 : result.GetInt64(1);
        }
        if (segmentId is null)
        {
            segmentId = InsertSegment(meetingId, segment, tx);
            cmd.CommandText = "INSERT INTO caption_events VALUES ($m, $e, $id, $r)";
        }
        else if (revision > previousRevision)
        {
            cmd.CommandText = """
                UPDATE caption_events SET revision = $r WHERE meeting_id = $m AND event_id = $e;
                UPDATE segments SET speaker = $sp, start_ms = $st, end_ms = $en, text = $txt, speaker_name = $name WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$sp", segment.Speaker.ToString());
            cmd.Parameters.AddWithValue("$st", (long)segment.Start.TotalMilliseconds);
            cmd.Parameters.AddWithValue("$en", (long)segment.End.TotalMilliseconds);
            cmd.Parameters.AddWithValue("$txt", segment.Text);
            cmd.Parameters.AddWithValue("$name", (object?)segment.SpeakerName ?? DBNull.Value);
        }
        else
        {
            return true;
        }
        cmd.Parameters.AddWithValue("$id", segmentId);
        cmd.Parameters.AddWithValue("$r", revision);
        cmd.Parameters.RemoveAt("$c");
        cmd.ExecuteNonQuery();
        tx.Commit();
        return true;
    }

    public bool ResumeCaptionCapture(long meetingId, string captureId, string owner)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = Cmd("UPDATE caption_captures SET owner = $o WHERE meeting_id = $m AND capture_id = $c AND finished = 0",
            ("$m", meetingId), ("$c", captureId), ("$o", owner));
        cmd.Transaction = tx;
        if (cmd.ExecuteNonQuery() == 0) return false;
        cmd.CommandText = "UPDATE meetings SET ended_at = NULL WHERE id = $m";
        cmd.ExecuteNonQuery();
        tx.Commit();
        return true;
    }

    public bool EndCaptionCapture(long meetingId, DateTimeOffset endedAt, string captureId, string? owner = null)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = Cmd("""
            UPDATE meetings SET ended_at = coalesce(ended_at, $e)
            WHERE id = $m AND EXISTS (
                SELECT 1 FROM caption_captures WHERE meeting_id = $m AND capture_id = $c AND ($o IS NULL OR owner = $o))
            """, ("$m", meetingId), ("$e", endedAt.ToString("o")), ("$c", captureId), ("$o", (object?)owner ?? DBNull.Value));
        cmd.Transaction = tx;
        if (cmd.ExecuteNonQuery() == 0) return false;
        if (owner is null)
        {
            cmd.CommandText = "UPDATE caption_captures SET finished = 1 WHERE meeting_id = $m";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    public int CountSegments(long meetingId)
    {
        using var cmd = Cmd("SELECT count(*) FROM segments WHERE meeting_id = $m", ("$m", meetingId));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void SaveInsights(long meetingId, string json, string model, DateTimeOffset createdAt)
    {
        using var cmd = Cmd("""
            INSERT INTO insights(meeting_id, json, model, created_at) VALUES ($m, $j, $mo, $c)
            ON CONFLICT(meeting_id) DO UPDATE SET json = excluded.json, model = excluded.model, created_at = excluded.created_at
            """, ("$m", meetingId), ("$j", json), ("$mo", model), ("$c", createdAt.ToString("o")));
        cmd.ExecuteNonQuery();
    }

    public StoredInsights? GetInsights(long meetingId)
    {
        using var cmd = Cmd("SELECT json, model, created_at FROM insights WHERE meeting_id = $m", ("$m", meetingId));
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new StoredInsights(meetingId, r.GetString(0), r.GetString(1), DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture))
            : null;
    }

    public Meeting? GetMeeting(long meetingId)
    {
        using var cmd = Cmd("SELECT id, title, started_at, ended_at, consent FROM meetings WHERE id = $id", ("$id", meetingId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadMeeting(r) : null;
    }

    public IReadOnlyList<Meeting> ListMeetings()
    {
        using var cmd = Cmd($"SELECT id, title, started_at, ended_at, consent FROM meetings m ORDER BY {MeetingOrder}");
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
        using var cmd = Cmd($"SELECT id, title, started_at, ended_at, consent FROM meetings m WHERE ended_at IS NULL ORDER BY {MeetingOrder} LIMIT 1");
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
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        var literal = string.Join(' ', SearchTerms.Matches(query).Select(m => m.Value)
            .Where(t => SearchableText.IsMatch(t))
            .Select(t => t.StartsWith('"') ? t : $"\"{t.TrimEnd('*')}\"{(t.EndsWith('*') ? "*" : "")}"));
        if (literal.Length == 0) return [];
        return RunSearch(literal, limit);
    }

    private List<SearchHit> RunSearch(string match, int limit)
    {
        using var cmd = Cmd($"""
            SELECT s.speaker, s.start_ms, s.end_ms, s.text, s.speaker_name, m.id, m.title,
                   snippet(segments_fts, 0, '[', ']', '…', 12)
            FROM segments_fts
            JOIN segments s ON s.id = segments_fts.rowid
            JOIN meetings m ON m.id = s.meeting_id
            WHERE segments_fts MATCH $q
            ORDER BY bm25(segments_fts), {MeetingOrder}, s.id
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
