using System.Text.RegularExpressions;

namespace MeetAgent.Core.Text;

/// <summary>
/// Frases que Whisper inventa en silencio o con música (aprendidas de subtítulos de YouTube),
/// y anotaciones del tipo [Música] o (risas).
/// </summary>
public static partial class HallucinationFilter
{
    private static readonly string[] Phrases =
    [
        "amaraorg",
        "wwwamaraorg",
        "subtítulos realizados por",
        "subtítulos por la comunidad",
        "gracias por ver",
        "suscríbete",
        "suscribete",
        "no olvides suscribirte",
        "dale like",
    ];

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex Annotation();

    // Restos de una anotación partida entre chunks: "…puerta]" al inicio o "[Ruido de…" al final.
    [GeneratedRegex(@"^[^\[\(]*[\]\)]|[\[\(][^\]\)]*$")]
    private static partial Regex DanglingAnnotation();

    public static string Clean(string text)
    {
        var t = Annotation().Replace(text, " ");
        t = DanglingAnnotation().Replace(t, " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    public static bool IsHallucination(string text)
    {
        var words = TextNormalizer.Words(Clean(text));
        if (words.Length == 0) return true;
        var joined = " " + string.Join(' ', words) + " ";
        return Phrases.Any(p => joined.Contains(" " + p + " ", StringComparison.Ordinal));
    }
}
