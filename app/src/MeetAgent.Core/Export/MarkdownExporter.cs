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
        sb.Append("# ").Append(Escape(meeting.Title)).Append('\n').Append('\n');
        sb.Append("- Fecha: ").Append(meeting.StartedAt.ToString("yyyy-MM-dd HH:mm")).Append('\n');
        if (meeting.EndedAt is { } end)
            sb.Append("- Duración: ").Append(TimeFormat.Hms(end - meeting.StartedAt)).Append('\n');
        sb.Append("- Consentimiento: ").Append(meeting.Consent).Append('\n');
        sb.Append('\n').Append("## Transcripción").Append('\n').Append('\n');

        string? speaker = null;
        TimeSpan lastEnd = TimeSpan.Zero;
        var paragraph = new StringBuilder();
        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            sb.Append(paragraph).Append('\n').Append('\n');
            paragraph.Clear();
        }

        foreach (var s in segments.OrderBy(s => s.Start).ThenBy(s => s.Speaker))
        {
            if (s.Label != speaker || s.Start - lastEnd > MaxGap)
            {
                FlushParagraph();
                paragraph.Append($"**[{TimeFormat.Hms(s.Start)}] {EscapeName(s.Label)}:** ");
                speaker = s.Label;
            }
            else paragraph.Append(' ');
            paragraph.Append(Escape(s.Text.Trim()));
            lastEnd = s.End;
        }
        FlushParagraph();
        return sb.ToString();
    }

    private static string EscapeName(string name) => Escape(name);

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
