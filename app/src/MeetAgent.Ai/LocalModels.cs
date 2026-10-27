namespace MeetAgent.Ai;

/// <param name="Origin">De dónde salió: Ollama o LM Studio.</param>
/// <param name="BaseUrl">URL compatible con OpenAI del servidor que lo tiene.</param>
public sealed record LocalModel(string Name, string Origin, string BaseUrl);

/// <summary>
/// Detecta modelos que el usuario ya tiene corriendo en Ollama o LM Studio.
/// Sondeo corto (1,5 s): si el servidor no está, no se hace esperar.
/// </summary>
public sealed class LocalModels(HttpClient? http = null)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1.5);

    private readonly HttpClient _http = http ?? new HttpClient { Timeout = Timeout };

    public async Task<IReadOnlyList<LocalModel>> ListAsync(CancellationToken ct = default)
    {
        var ollama = Probe("http://localhost:11434/api/tags", ParseOllama, "Ollama", "http://localhost:11434/v1", ct);
        var lmStudio = Probe("http://localhost:1234/v1/models", ParseLmStudio, "LM Studio", "http://localhost:1234/v1", ct);
        await Task.WhenAll(ollama, lmStudio).ConfigureAwait(false);

        return (await ollama.ConfigureAwait(false)).Concat(await lmStudio.ConfigureAwait(false))
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<LocalModel>> Probe(string url, Func<string, List<string>> parse, string origin,
        string baseUrl, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var res = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return [];
            var body = await res.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return parse(body).Select(name => new LocalModel(name, origin, baseUrl)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                       or System.Text.Json.JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Ollama: <c>{"models":[{"name":"qwen3:8b",…}]}</c>.</summary>
    internal static List<string> ParseOllama(string json) =>
        Names(json, "models", "name");

    /// <summary>LM Studio: <c>{"data":[{"id":"qwen3-8b",…}]}</c>.</summary>
    internal static List<string> ParseLmStudio(string json) =>
        Names(json, "data", "id");

    private static List<string> Names(string json, string list, string property)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node?[list] is not System.Text.Json.Nodes.JsonArray items) return [];
        return items.Select(i => i?[property]?.GetValue<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();
    }
}
