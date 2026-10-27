using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

/// <summary>Arma los adaptadores a partir de lo que el usuario configuró en la pestaña IA.</summary>
public static class AiFactory
{
    /// <summary>Un solo HttpClient para toda la app; los análisis largos pueden tardar varios minutos.</summary>
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static IChatModel CreateChat(Connection c)
    {
        if (!c.IsUsable) throw new AiException("Falta conectar tu IA. Elige una opción en Conectar IA.");
        return c.Provider switch
        {
            AiProvider.ClaudeCuenta or AiProvider.ChatGptCuenta or AiProvider.GeminiCuenta => new CliChat(c),
            AiProvider.Anthropic => new AnthropicChat(c.ApiKey!, c.Model.Trim()),
            _ => new OpenAiCompatibleChat(Http, c.EffectiveBaseUrl, c.ApiKey, c.Model.Trim(), c.Info.Label),
        };
    }

    public static ITranscriber CreateTranscriber(Connection c)
    {
        if (!c.Info.CanTranscribe) throw new AiException($"{c.Info.Label} no transcribe audio. Usa OpenAI, Groq o tu servidor.");
        if (!c.IsUsable) throw new AiException("Falta conectar tu transcriptor en Ajustes.");
        return new CloudTranscriber(Http, c.EffectiveBaseUrl, c.ApiKey, c.Model.Trim(), c.Info.Label);
    }
}
