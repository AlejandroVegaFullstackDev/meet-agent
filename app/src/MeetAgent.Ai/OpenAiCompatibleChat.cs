using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

public class AiException(string message) : Exception(message);

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
        if ((int)status >= 400) throw new AiException(Explain(status, ErrorMessage(text)));

        string? content;
        try { content = JsonNode.Parse(text)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Un proxy o un servidor mal configurado puede responder 200 con HTML.
            throw new AiException($"{label} devolvió una respuesta que no se entiende. Revisa la dirección del servidor.");
        }
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

    /// <summary>Qué hacer según el código; el detalle del proveedor va al final por si hace falta.</summary>
    private string Explain(HttpStatusCode status, string detail)
    {
        var code = (int)status;
        var keyProblem = code is 401 or 403
                         || (code == 400 && detail.Contains("api key", StringComparison.OrdinalIgnoreCase));
        var lead = code switch
        {
            _ when keyProblem => $"{label} rechazó la API key. Revisa que esté bien copiada y activa.",
            402 => $"Tu cuenta de {label} no tiene saldo. Recarga créditos y vuelve a intentar.",
            404 => $"{label} no encontró el modelo «{model}». Prueba con otro modelo.",
            429 => $"{label} rechazó la petición por límite o cuota. Revisa tu saldo o espera un momento.",
            _ => $"{label} respondió {code}.",
        };
        return $"{lead} ({code}: {Trim(detail)})";
    }

    internal static string ErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            // Gemini envuelve el error en un arreglo.
            if (node is JsonArray { Count: > 0 } array) node = array[0];
            return node?["error"]?["message"]?.GetValue<string>() ?? body;
        }
        catch (JsonException) { return body; }
        catch (InvalidOperationException) { return body; }
    }

    internal static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
