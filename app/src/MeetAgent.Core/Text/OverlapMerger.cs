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
            if (!_tail.Skip(_tail.Count - k).SequenceEqual(norm.Take(k))) continue;
            if (k == 1 && norm[0].Length < 3) break;   // una palabra corta suelta no es evidencia suficiente
            skip = k;
            break;
        }

        _tail.AddRange(norm.Skip(skip));
        if (_tail.Count > maxOverlapWords) _tail.RemoveRange(0, _tail.Count - maxOverlapWords);
        return string.Join(' ', tokens.Skip(skip));
    }

    public void Reset() => _tail.Clear();
}
