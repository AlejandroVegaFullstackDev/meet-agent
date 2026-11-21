using MeetAgent.Core.Export;
using MeetAgent.Core.Text;

namespace MeetAgent.Cli.Commands;

public static class HistoryCommands
{
    /// <summary><c>meetings</c>: lista las reuniones guardadas.</summary>
    public static int List()
    {
        using var store = Store.Open();
        foreach (var m in store.ListMeetings())
        {
            var dur = m.EndedAt is { } e ? TimeFormat.Hms(e - m.StartedAt) : "en curso";
            Console.WriteLine($"#{m.Id,-4} {m.StartedAt:yyyy-MM-dd HH:mm}  {dur}  {store.GetSegments(m.Id).Count,4} segmentos  [{m.Consent}]  {m.Title}");
        }
        return 0;
    }

    /// <summary><c>export [--meeting ID] [--out archivo.md]</c>: por defecto la última reunión, a data/reunion-ID.md.</summary>
    public static async Task<int> ExportAsync(Args args)
    {
        long? meetingId = null;
        if (args.Has("meeting"))
        {
            if (!long.TryParse(args.Get("meeting"), out var id) || id <= 0)
            {
                Console.Error.WriteLine("--meeting requiere un ID de reunión positivo.");
                return 1;
            }
            meetingId = id;
        }
        using var store = Store.Open();
        var meeting = meetingId is { } requested ? store.GetMeeting(requested) : store.ListMeetings().FirstOrDefault();
        if (meeting is null)
        {
            Console.Error.WriteLine(meetingId is null ? "No hay reuniones guardadas." : $"No existe la reunión #{meetingId}.");
            return 1;
        }
        var md = MarkdownExporter.Export(meeting, store.GetSegments(meeting.Id));
        var outPath = args.Get("out") ?? Path.Combine(Paths.Data, $"reunion-{meeting.Id}.md");
        try { await File.WriteAllTextAsync(outPath, md); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"No se pudo exportar la reunión: {ex.Message}");
            return 1;
        }
        Console.WriteLine(outPath);
        return 0;
    }

    /// <summary><c>search "texto"</c>: búsqueda FTS5 en todas las reuniones.</summary>
    public static int Search(Args args, string[] argv)
    {
        var q = args.Get("q") ?? string.Join(' ', argv.Skip(1).Where(a => !a.StartsWith("--")));
        using var store = Store.Open();
        foreach (var h in store.Search(q, args.GetInt("limit", 20)))
            Console.WriteLine($"#{h.MeetingId} {h.MeetingTitle} [{TimeFormat.Hms(h.Segment.Start)}] [{h.Segment.Label}] {h.Snippet}");
        return 0;
    }
}
