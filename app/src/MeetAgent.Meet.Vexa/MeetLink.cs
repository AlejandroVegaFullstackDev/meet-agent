using System.Text.RegularExpressions;

namespace MeetAgent.Meet.Vexa;

/// <summary>Código de Google Meet (<c>abc-defg-hij</c>) a partir de un link o del código suelto.</summary>
public static partial class MeetLink
{
    [GeneratedRegex(@"(?:^|/)([a-z]{3}-[a-z]{4}-[a-z]{3})(?:$|/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    public static bool TryParse(string input, out string code)
    {
        code = "";
        var s = input.Trim();
        if (s.StartsWith("meet.google.com/", StringComparison.OrdinalIgnoreCase)) s = "https://" + s;
        if (s.Contains("://"))
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http") ||
                !uri.Host.Equals("meet.google.com", StringComparison.OrdinalIgnoreCase))
                return false;
            s = uri.AbsolutePath;
        }
        else
        {
            s = s.Split(['?', '#'], 2)[0];
            if (s.Contains('/')) return false;
        }
        var m = Code().Match(s);
        if (!m.Success) return false;
        code = m.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    public static string Url(string code) => $"https://meet.google.com/{code}";
}
