using System.Text;
using MeetAgent.Core.Text;

namespace MeetAgent.Core.Export;

/// <summary>Exporta una reunión a Markdown, agrupando turnos consecutivos del mismo hablante.</summary>
public static class MarkdownExporter
{
    /// <summary>Más de este silencio entre segmentos del mismo hablante abre un párrafo nuevo.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    public static string Export(Meeting meeting, IEnumerable<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(EscapeLine(meeting.Title)).Append('\n').Append('\n');
        sb.Append("- Fecha: ").Append(meeting.StartedAt.ToString("yyyy-MM-dd HH:mm")).Append('\n');
        if (meeting.EndedAt is { } end)
            sb.Append("- Duración: ").Append(TimeFormat.Hms(end - meeting.StartedAt)).Append('\n');
        sb.Append("- Consentimiento: ").Append(EscapeLine(meeting.Consent)).Append('\n');
        sb.Append('\n').Append("## Transcripción").Append('\n').Append('\n');

        (Speaker Kind, string Label)? speaker = null;
        TimeSpan lastEnd = TimeSpan.Zero;
        var paragraph = new StringBuilder();
        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            sb.Append(paragraph).Append('\n').Append('\n');
            paragraph.Clear();
        }

        foreach (var s in segments.OrderBy(s => s.Start))
        {
            var identity = (s.Speaker, s.Label);
            if (identity != speaker || s.Start - lastEnd > MaxGap)
            {
                FlushParagraph();
                paragraph.Append($"**[{TimeFormat.Hms(s.Start)}] {s.Speaker}:** ");
                if (s.Label != s.Speaker.ToString())
                    paragraph.Append(EscapeLine(s.Label)).Append(": ");
                speaker = identity;
                lastEnd = s.End;
            }
            else paragraph.Append(' ');
            paragraph.Append(EscapeLine(s.Text.Trim()));
            if (s.End > lastEnd) lastEnd = s.End;
        }
        FlushParagraph();
        return sb.ToString();
    }

    private static string EscapeLine(string text) => Escape(text.ReplaceLineEndings(" "));

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '\\' or '`' or '*' or '_' or '#' or '[' or ']' or '|' => "\\" + c,
                _ => c.ToString(),
            });
        return sb.ToString();
    }
}
