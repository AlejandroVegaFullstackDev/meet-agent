namespace MeetAgent.Meet.Vexa;

/// <summary>
/// Dónde está Vexa y con qué key: <c>VEXA_URL</c>/<c>VEXA_API_KEY</c> o, si no,
/// <c>deploy/vexa/.env.local</c> (lo genera deploy/vexa/up.sh). URL por defecto: http://127.0.0.1:8056.
/// </summary>
public sealed record VexaConfig(Uri BaseUrl, string ApiKey)
{
    public static VexaConfig? Load(string root)
    {
        var url = Environment.GetEnvironmentVariable("VEXA_URL");
        var key = Environment.GetEnvironmentVariable("VEXA_API_KEY");
        var envFile = Path.Combine(root, "deploy", "vexa", ".env.local");
        if (string.IsNullOrEmpty(key) && File.Exists(envFile))
            key = File.ReadLines(envFile).Select(l => l.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "VEXA_API_KEY")?[1].Trim();
        return string.IsNullOrEmpty(key) ? null : new VexaConfig(new Uri(string.IsNullOrEmpty(url) ? "http://127.0.0.1:8056" : url), key);
    }

    public VexaClient CreateClient() => new(new HttpClient { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(30) }, ApiKey);
}
