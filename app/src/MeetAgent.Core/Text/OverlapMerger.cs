namespace MeetAgent.Core.Text;

/// <summary>
/// Alinea el texto de chunks solapados: quita del inicio del chunk nuevo las palabras
/// que repiten el final de lo ya emitido (sufijo == prefijo, comparando normalizado).
/// </summary>
public sealed class OverlapMerger(int maxOverlapWords = 8)
{
    private readonly List<string> _tail = new();

    public string Add(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                         .Where(t => TextNormalizer.Word(t).Length > 0)
                         .ToArray();
        var norm = tokens.Select(TextNormalizer.Word).ToArray();

        int skip = 0;
        for (int k = Math.Min(maxOverlapWords, Math.Min(_tail.Count, norm.Length)); k >= 1; k--)
        {
            if (!_tail.Skip(_tail.Count - k).Zip(norm.Take(k)).All(p => SameWord(p.First, p.Second))) continue;
            if (k == 1 && norm[0].Length < 3) break;   // una palabra corta suelta no es evidencia suficiente
            skip = k;
            break;
        }

        _tail.AddRange(norm.Skip(skip));
        if (_tail.Count > maxOverlapWords) _tail.RemoveRange(0, _tail.Count - maxOverlapWords);
        return string.Join(' ', tokens.Skip(skip));
    }

    public void Reset() => _tail.Clear();

    /// <summary>
    /// Igualdad tolerante para la frontera, donde Whisper suele cortar o deformar la palabra:
    /// sin tildes, una es prefijo de la otra (≥ 3 letras), o ambas tienen ≥ 5 letras y difieren en ≤ 30 %.
    /// </summary>
    internal static bool SameWord(string a, string b)
    {
        if (a == b) return true;
        a = StripAccents(a);
        b = StripAccents(b);
        if (a == b) return true;
        if (Math.Min(a.Length, b.Length) >= 3 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)))
            return true;
        if (Math.Min(a.Length, b.Length) < 5) return false;
        return Levenshtein(a, b) <= Math.Max(1, (int)Math.Round(0.3 * Math.Max(a.Length, b.Length)));
    }

    private static string StripAccents(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
