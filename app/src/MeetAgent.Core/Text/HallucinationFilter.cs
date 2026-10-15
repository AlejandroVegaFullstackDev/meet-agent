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

    public static string Clean(string text) => Regex.Replace(Annotation().Replace(text, " "), @"\s+", " ").Trim();

    public static bool IsHallucination(string text)
    {
        var words = TextNormalizer.Words(Clean(text));
        if (words.Length == 0) return true;
        var joined = " " + string.Join(' ', words) + " ";
        return Phrases.Any(p => joined.Contains(" " + p + " ", StringComparison.Ordinal));
    }
}
