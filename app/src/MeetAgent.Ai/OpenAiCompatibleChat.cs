using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

public sealed class AiException(string message) : Exception(message);

/// <summary>
/// <c>POST {base}/chat/completions</c>: OpenAI, DeepSeek, Gemini (endpoint OpenAI), OpenRouter, Groq,
/// y servidores propios como Ollama o LM Studio.
/// </summary>
public sealed class OpenAiCompatibleChat(HttpClient http, string baseUrl, string? apiKey, string model, string label) : IChatModel
{
    public string Name => $"{label} · {model}";

    public async Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = jsonSchema is null ? system : $"{system}\n\nEsquema JSON de la respuesta:\n{jsonSchema}" },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        if (jsonSchema is not null) body["response_format"] = new JsonObject { ["type"] = "json_object" };

        var (status, text) = await PostAsync(body, ct).ConfigureAwait(false);
        // Algunos servidores (LM Studio, modelos viejos) no aceptan json_object: se reintenta sin él;
        // el esquema ya va en las instrucciones.
        if (status == HttpStatusCode.BadRequest && jsonSchema is not null)
        {
            body.Remove("response_format");
            (status, text) = await PostAsync(body, ct).ConfigureAwait(false);
        }
        if ((int)status >= 400) throw new AiException($"{label} respondió {(int)status}: {Trim(ErrorMessage(text))}");

        var content = JsonNode.Parse(text)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        return content ?? throw new AiException($"{label} no devolvió texto.");
    }

    private async Task<(HttpStatusCode, string)> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        // OpenRouter usa estas cabeceras para mostrar la app en su panel de uso.
        req.Headers.TryAddWithoutValidation("X-Title", "MeetAgent");
        req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/AlejandroVegaFullstackDev/meet-agent");
        try
        {
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            return (res.StatusCode, await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"No se pudo conectar con {label} ({baseUrl}): {ex.Message}");
        }
    }

    internal static string ErrorMessage(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body; }
        catch (JsonException) { return body; }
        catch (InvalidOperationException) { return body; }
    }

    internal static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
