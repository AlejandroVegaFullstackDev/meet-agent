using System.Text;

namespace MeetAgent.Core.Text;

/// <summary>Normalización para comparar texto: minúsculas, sin puntuación, conserva tildes y ñ.</summary>
public static class TextNormalizer
{
    public static string Word(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var c in token)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    public static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Word)
            .Where(w => w.Length > 0)
            .ToArray();
}
