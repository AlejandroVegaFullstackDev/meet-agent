namespace MeetAgent.Ai;

public enum AiProvider { OpenAI, Anthropic, Gemini, DeepSeek, OpenRouter, Groq, Personalizado }

/// <param name="BaseUrl">Endpoint compatible con OpenAI (<c>/chat/completions</c>); null para Claude, que usa su SDK.</param>
/// <param name="TranscriptionModel">Modelo de <c>/audio/transcriptions</c>; null si el proveedor no transcribe audio.</param>
/// <param name="KeyUrl">Dónde saca la API key el usuario.</param>
public sealed record ProviderInfo(AiProvider Provider, string Label, string? BaseUrl, string ChatModel,
    string? TranscriptionModel, string? KeyUrl, bool NeedsKey = true)
{
    public bool CanTranscribe => TranscriptionModel is not null;
    public override string ToString() => Label;
}

/// <summary>
/// Proveedores que se pueden conectar. Los modelos son sugerencias: el usuario puede escribir otro.
/// «Personalizado» es cualquier servidor compatible con OpenAI: Ollama, LM Studio, vLLM… (por ejemplo un Qwen propio).
/// </summary>
public static class Providers
{
    public static readonly IReadOnlyList<ProviderInfo> All =
    [
        new(AiProvider.Anthropic, "Claude (Anthropic)", null, "claude-opus-5-5", null, "https://console.anthropic.com/settings/keys"),
        new(AiProvider.OpenAI, "OpenAI", "https://api.openai.com/v1", "gpt-5-mini", "whisper-1", "https://platform.openai.com/api-keys"),
        new(AiProvider.DeepSeek, "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", null, "https://platform.deepseek.com/api_keys"),
        new(AiProvider.Gemini, "Gemini (Google)", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-flash-latest", null, "https://aistudio.google.com/apikey"),
        new(AiProvider.OpenRouter, "OpenRouter (todas con un login)", "https://openrouter.ai/api/v1", "openrouter/auto", null, "https://openrouter.ai/settings/keys"),
        new(AiProvider.Groq, "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "whisper-large-v3-turbo", "https://console.groq.com/keys"),
        new(AiProvider.Personalizado, "Mi servidor (Ollama, LM Studio, vLLM…)", "http://localhost:11434/v1", "qwen3", "whisper-1", null, NeedsKey: false),
    ];

    public static ProviderInfo Get(AiProvider p) => All.First(x => x.Provider == p);
}
