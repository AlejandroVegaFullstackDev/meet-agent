namespace MeetAgent.Core.Text;

public sealed record WerResult(int ReferenceWords, int Substitutions, int Deletions, int Insertions)
{
    public int Errors => Substitutions + Deletions + Insertions;
    public double Rate => ReferenceWords == 0 ? (Insertions > 0 ? 1.0 : 0.0) : (double)Errors / ReferenceWords;
}

/// <summary>Word Error Rate por distancia de Levenshtein sobre palabras normalizadas.</summary>
public static class Wer
{
    public static WerResult Compute(string reference, string hypothesis) =>
        Compute(TextNormalizer.Words(reference), TextNormalizer.Words(hypothesis));

    public static WerResult Compute(IReadOnlyList<string> r, IReadOnlyList<string> h)
    {
        int n = r.Count, m = h.Count;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1));

        int s = 0, del = 0, ins = 0;
        for (int i = n, j = m; i > 0 || j > 0;)
        {
            if (i > 0 && j > 0 && d[i, j] == d[i - 1, j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1))
            {
                if (r[i - 1] != h[j - 1]) s++;
                i--; j--;
            }
            else if (i > 0 && d[i, j] == d[i - 1, j] + 1) { del++; i--; }
            else { ins++; j--; }
        }
        return new WerResult(n, s, del, ins);
    }
}
