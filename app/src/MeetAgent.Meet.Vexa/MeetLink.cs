using System.Text.RegularExpressions;

namespace MeetAgent.Meet.Vexa;

/// <summary>Código de Google Meet (<c>abc-defg-hij</c>) a partir de un link o del código suelto.</summary>
public static partial class MeetLink
{
    [GeneratedRegex(@"(?:^|/)([a-z]{3}-[a-z]{4}-[a-z]{3})(?:$|[/?#])", RegexOptions.IgnoreCase)]
    private static partial Regex Code();

    public static bool TryParse(string input, out string code)
    {
        code = "";
        var s = input.Trim();
        if (s.Contains("://") && !s.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!s.Contains('/') ) s = "/" + s;
        var m = Code().Match(s);
        if (!m.Success) return false;
        code = m.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    public static string Url(string code) => $"https://meet.google.com/{code}";
}
