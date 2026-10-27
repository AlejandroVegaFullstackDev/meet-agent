using System.Globalization;
using System.Text;
using MeetAgent.Core.Text;

namespace MeetAgent.Core.Analysis;

/// <param name="Share">Fracción (0-1) del tiempo total hablado.</param>
/// <param name="Turns">Intervenciones: segmentos seguidos de la misma persona cuentan como una.</param>
public sealed record SpeakerStats(string Speaker, TimeSpan TalkTime, double Share, int Words, int Turns, TimeSpan LongestTurn)
{
    public double WordsPerMinute => TalkTime.TotalMinutes > 0 ? Words / TalkTime.TotalMinutes : 0;
}

/// <summary>Estadísticas de una reunión calculadas en el equipo, sin IA: no gastan tokens y no inventan.</summary>
public sealed record MeetingStats(TimeSpan Duration, TimeSpan TalkTime, int Words, int Turns, IReadOnlyList<SpeakerStats> Speakers)
{
    public static MeetingStats From(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0) return new MeetingStats(TimeSpan.Zero, TimeSpan.Zero, 0, 0, []);
        var ordered = segments.OrderBy(s => s.Start).ToList();
        var duration = ordered.Max(s => s.End) - ordered[0].Start;

        // Intervenciones: se cortan cuando cambia quien habla.
        var turns = new List<(string Speaker, TimeSpan Length)>();
        foreach (var s in ordered)
        {
            var len = Length(s);
            if (turns.Count > 0 && turns[^1].Speaker == s.Label) turns[^1] = (s.Label, turns[^1].Length + len);
            else turns.Add((s.Label, len));
        }

        var total = TimeSpan.FromTicks(ordered.Sum(s => Length(s).Ticks));
        var speakers = ordered.GroupBy(s => s.Label).Select(g =>
        {
            var talk = TimeSpan.FromTicks(g.Sum(s => Length(s).Ticks));
            var mine = turns.Where(t => t.Speaker == g.Key).ToList();
            return new SpeakerStats(g.Key, talk, total.Ticks > 0 ? (double)talk.Ticks / total.Ticks : 0,
                g.Sum(s => CountWords(s.Text)), mine.Count, mine.Max(t => t.Length));
        }).OrderByDescending(s => s.TalkTime).ToList();

        return new MeetingStats(duration, total, speakers.Sum(s => s.Words), turns.Count, speakers);
    }

    public static int CountWords(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static TimeSpan Length(TranscriptSegment s) => s.End > s.Start ? s.End - s.Start : TimeSpan.Zero;

    public string ToMarkdown()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("- Duración: ").Append(TimeFormat.Hms(Duration)).Append(" · tiempo hablado: ").Append(TimeFormat.Hms(TalkTime)).Append('\n');
        sb.Append("- Palabras: ").Append(Words).Append(" · intervenciones: ").Append(Turns).Append('\n').Append('\n');
        sb.Append("| Persona | Tiempo | % | Palabras | Intervenciones | Más larga | Palabras/min |\n");
        sb.Append("|---|---|---|---|---|---|---|\n");
        foreach (var s in Speakers)
            sb.Append("| ").Append(s.Speaker).Append(" | ").Append(TimeFormat.Hms(s.TalkTime))
              .Append(" | ").Append((s.Share * 100).ToString("F0", c)).Append(" | ").Append(s.Words)
              .Append(" | ").Append(s.Turns).Append(" | ").Append(TimeFormat.Hms(s.LongestTurn))
              .Append(" | ").Append(s.WordsPerMinute.ToString("F0", c)).Append(" |\n");
        return sb.ToString();
    }
}
