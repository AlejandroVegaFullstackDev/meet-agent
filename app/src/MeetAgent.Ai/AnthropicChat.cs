using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

/// <summary>
/// Claude con el SDK oficial. Pide la respuesta con salida estructurada (JSON que cumple el esquema) y,
/// en los modelos que lo admiten, activa el fallback del servidor: si un clasificador de seguridad
/// rechaza la petición, otro modelo la atiende en la misma llamada.
/// </summary>
public sealed class AnthropicChat(string apiKey, string model) : IChatModel
{
    private static readonly HashSet<string> FallbackModels = ["claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];

    private readonly AnthropicClient _client = new() { ApiKey = apiKey };

    public string Name => $"Claude · {model}";

    public async Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct)
    {
        try
        {
            return await SendAsync(system, user, jsonSchema, ct).ConfigureAwait(false);
        }
        catch (AnthropicBadRequestException) when (jsonSchema is not null)
        {
            // Si el esquema no se acepta tal cual, se pide igual el JSON por instrucciones.
            return await SendAsync($"{system}\n\nEsquema JSON de la respuesta:\n{jsonSchema}", user, null, ct).ConfigureAwait(false);
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new AiException("Claude rechazó la API key. Revísala en la pestaña IA.");
        }
        catch (AnthropicApiException ex)
        {
            throw new AiException($"Claude: {ex.Message}");
        }
    }

    private async Task<string> SendAsync(string system, string user, string? jsonSchema, CancellationToken ct)
    {
        var useFallback = FallbackModels.Contains(model);
        var p = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = system,
            Messages = [new BetaUserMessageParam { Content = user }],
            Betas = useFallback ? ["server-side-fallback-2026-07-01"] : null,
            Fallbacks = useFallback ? new BetaFallbacksParam(new Default()) : null,
            OutputConfig = jsonSchema is null ? null : new BetaOutputConfig
            {
                Format = new BetaJsonOutputFormat { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonSchema)! },
            },
        };

        var msg = await _client.Beta.Messages.Create(p, ct).ConfigureAwait(false);
        if (msg.StopReason == "refusal") throw new AiException("Claude no quiso responder esta petición (rechazo de seguridad).");
        var text = string.Concat(msg.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        if (msg.StopReason == "max_tokens") throw new AiException("La respuesta de Claude se cortó por largo.");
        return text;
    }
}
