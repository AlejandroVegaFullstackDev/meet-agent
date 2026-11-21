using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

/// <summary>
/// ChatGPT con el plan del usuario: <c>POST /v1/responses</c> con el token de «Continuar con ChatGPT»,
/// siempre con <c>store: false</c> y <c>stream: true</c>. Solo vale como respuesta un <c>response.completed</c>.
/// </summary>
public sealed class ChatGptPlanChat(HttpClient http, ChatGptSessionStore store, string model) : IChatModel
{
    public const string UsageUrl = "https://chatgpt.com/settings/usage";

    public string Name => $"ChatGPT · {model}";

    public async Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct)
    {
        var token = await ChatGptLogin.AccessTokenAsync(http, store, ct).ConfigureAwait(false);
        var body = new JsonObject
        {
            ["model"] = model,
            // Este flujo rechaza los mensajes «system»: las instrucciones van en «instructions».
            ["instructions"] = jsonSchema is null ? system : $"{system}\n\nEsquema JSON de la respuesta:\n{jsonSchema}",
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
            ["store"] = false,
            ["stream"] = true,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, ChatGptLogin.Resource + "/responses")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new AiException("No se pudo conectar con ChatGPT. Revisa tu conexión a internet.");
        }
        using (res)
        {
            if (!res.IsSuccessStatusCode)
                throw new AiException(Explain((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)));
            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await ReadStreamAsync(stream, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Lee los eventos SSE y junta el texto; falla si no llega <c>response.completed</c>.</summary>
    internal static async Task<string> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data is "" or "[DONE]") continue;
            JsonNode? ev;
            try { ev = JsonNode.Parse(data); }
            catch (JsonException) { continue; }

            switch (ev?["type"]?.GetValue<string>())
            {
                case "response.output_text.delta":
                    text.Append(ev["delta"]?.GetValue<string>());
                    break;
                case "response.completed":
                    return text.Length > 0 ? text.ToString() : OutputText(ev["response"])
                        ?? throw new AiException("ChatGPT no devolvió texto. Inténtalo de nuevo.");
                case "response.failed":
                    var error = ev["response"]?["error"];
                    throw new AiException(ExplainCode(error?["code"]?.GetValue<string>(), error?["message"]?.GetValue<string>()));
                case "response.incomplete":
                    throw new AiException("La respuesta de ChatGPT quedó incompleta. Inténtalo de nuevo.");
                case "error":
                    throw new AiException(ExplainCode(ev["code"]?.GetValue<string>() ?? ev["error"]?["code"]?.GetValue<string>(),
                        ev["message"]?.GetValue<string>() ?? ev["error"]?["message"]?.GetValue<string>()));
            }
        }
        throw new AiException("Se cortó la conexión con ChatGPT antes de terminar. Inténtalo de nuevo.");
    }

    private static string? OutputText(JsonNode? response) =>
        response?["output"]?.AsArray()
            .SelectMany(o => o?["content"]?.AsArray() ?? [])
            .Where(c => c?["type"]?.GetValue<string>() == "output_text")
            .Select(c => c!["text"]?.GetValue<string>())
            .Aggregate((string?)null, (acc, t) => acc + t);

    /// <summary>Error antes del stream: puede venir como <c>{"error":{…}}</c> o como <c>{"detail":"…"}</c>.</summary>
    internal static string Explain(int status, string body)
    {
        string? code = null, message = null;
        try
        {
            var node = JsonNode.Parse(body);
            code = node?["error"]?["code"]?.GetValue<string>();
            message = node?["error"]?["message"]?.GetValue<string>() ?? node?["detail"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }

        if (code is not null) return ExplainCode(code, message);
        return status switch
        {
            401 => "Tu sesión de ChatGPT ya no es válida. Vuelve a iniciar sesión en Conectar IA.",
            403 => "ChatGPT no permite usar tu plan desde aquí (puede ser tu país o tu tipo de cuenta).",
            429 => $"Llegaste al límite de uso de tu plan de ChatGPT. Revísalo en {UsageUrl}",
            503 => "ChatGPT no está disponible ahora. Inténtalo en unos minutos.",
            _ => $"ChatGPT respondió {status}. Inténtalo de nuevo.",
        };
    }

    internal static string ExplainCode(string? code, string? message) => code switch
    {
        "subscription_sharing_user_not_eligible" =>
            "Tu plan de ChatGPT no se puede usar en otras apps. Hace falta ChatGPT Plus o Pro; también puedes conectar OpenRouter.",
        "subscription_sharing_usage_limit_exceeded" =>
            $"Llegaste al límite de uso de tu plan de ChatGPT para MeetAgent. Revísalo en {UsageUrl}",
        "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" =>
            "ChatGPT no pudo comprobar tu plan ahora. Inténtalo en unos minutos.",
        "subscription_sharing_invalid_user" =>
            "Tu sesión de ChatGPT ya no es válida. Vuelve a iniciar sesión en Conectar IA.",
        _ => message is { Length: > 0 }
            ? $"ChatGPT no pudo responder: {OpenAiCompatibleChat.Trim(message)}"
            : "ChatGPT no pudo responder. Inténtalo de nuevo.",
    };
}
