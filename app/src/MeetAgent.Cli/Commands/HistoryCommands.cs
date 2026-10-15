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
        using var store = Store.Open();
        var meeting = args.Get("meeting") is { } id ? store.GetMeeting(long.Parse(id)) : store.ListMeetings().FirstOrDefault();
        if (meeting is null)
        {
            Console.Error.WriteLine("No hay reuniones guardadas.");
            return 1;
        }
        var md = MarkdownExporter.Export(meeting, store.GetSegments(meeting.Id));
        var outPath = args.Get("out") ?? Path.Combine(Paths.Data, $"reunion-{meeting.Id}.md");
        await File.WriteAllTextAsync(outPath, md);
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
