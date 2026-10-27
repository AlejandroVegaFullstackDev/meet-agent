using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Beta.Messages;
using MeetAgent.Ai;

namespace MeetAgent.Ai.Tests;

/// <summary>Servidor HTTP falso: guarda cada petición y responde en orden.</summary>
internal sealed class FakeHttp(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    private int _i;
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        var (status, body) = responses[Math.Min(_i++, responses.Length - 1)];
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

public class OpenAiCompatibleChatTests
{
    private const string Ok = """{"choices":[{"message":{"role":"assistant","content":"{\"resumen\":\"ok\"}"}}]}""";

    [Fact]
    public async Task Envia_modelo_mensajes_key_y_pide_json()
    {
        var http = new FakeHttp((HttpStatusCode.OK, Ok));
        var chat = new OpenAiCompatibleChat(new HttpClient(http), "https://api.deepseek.com/v1/", "sk-1", "deepseek-chat", "DeepSeek");

        var text = await chat.CompleteAsync("reglas", "contenido", "{\"type\":\"object\"}", CancellationToken.None);

        Assert.Equal("{\"resumen\":\"ok\"}", text);
        var (req, body) = Assert.Single(http.Requests);
        Assert.Equal("https://api.deepseek.com/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer sk-1", req.Headers.Authorization!.ToString());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("deepseek-chat", json["model"]!.GetValue<string>());
        Assert.Equal("json_object", json["response_format"]!["type"]!.GetValue<string>());
        Assert.Contains("Esquema JSON", json["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal("contenido", json["messages"]![1]!["content"]!.GetValue<string>());
        Assert.Equal("DeepSeek · deepseek-chat", chat.Name);
    }

    [Fact]
    public async Task Servidor_que_no_acepta_json_object_se_reintenta_sin_el()
    {
        var http = new FakeHttp((HttpStatusCode.BadRequest, """{"error":{"message":"response_format no soportado"}}"""), (HttpStatusCode.OK, Ok));
        var chat = new OpenAiCompatibleChat(new HttpClient(http), "http://localhost:1234/v1", null, "qwen3", "LM Studio");

        await chat.CompleteAsync("r", "u", "{}", CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
        Assert.Null(JsonNode.Parse(http.Requests[1].Body)!["response_format"]);
        Assert.Null(http.Requests[0].Request.Headers.Authorization);
    }

    [Fact]
    public async Task Error_de_la_api_se_muestra_legible()
    {
        var http = new FakeHttp((HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API key"}}"""));
        var chat = new OpenAiCompatibleChat(new HttpClient(http), "https://api.openai.com/v1", "mala", "gpt-5-mini", "OpenAI");

        var ex = await Assert.ThrowsAsync<AiException>(() => chat.CompleteAsync("r", "u", null, CancellationToken.None));
        Assert.Equal("OpenAI respondió 401: Invalid API key", ex.Message);
    }
}

public class CloudTranscriberTests
{
    [Fact]
    public async Task Pide_verbose_json_y_devuelve_segmentos_con_tiempos()
    {
        var http = new FakeHttp((HttpStatusCode.OK, """
            {"text":"hola mundo","segments":[{"start":0.0,"end":1.5,"text":" hola","avg_logprob":-0.1},{"start":1.5,"end":3,"text":" mundo"}]}
            """));
        var t = new CloudTranscriber(new HttpClient(http), "https://api.groq.com/openai/v1", "gsk", "whisper-large-v3-turbo", "Groq");

        var r = await t.TranscribeAsync(new float[16000], CancellationToken.None);

        Assert.Equal(["hola", "mundo"], r.Select(x => x.Text));
        Assert.Equal(TimeSpan.FromSeconds(1.5), r[0].End);
        Assert.Equal(Math.Exp(-0.1), r[0].Probability!.Value, 6);
        var (req, body) = Assert.Single(http.Requests);
        Assert.Equal("https://api.groq.com/openai/v1/audio/transcriptions", req.RequestUri!.ToString());
        Assert.Contains("verbose_json", body);
        Assert.Contains("whisper-large-v3-turbo", body);
    }

    [Fact]
    public async Task Si_el_modelo_no_da_verbose_json_usa_json_y_un_tramo()
    {
        var http = new FakeHttp((HttpStatusCode.BadRequest, "{}"), (HttpStatusCode.OK, """{"text":"todo junto"}"""));
        var t = new CloudTranscriber(new HttpClient(http), "https://api.openai.com/v1", "sk", "gpt-4o-transcribe", "OpenAI");

        var r = await t.TranscribeAsync(new float[32000], CancellationToken.None);

        var only = Assert.Single(r);
        Assert.Equal("todo junto", only.Text);
        Assert.Equal(TimeSpan.FromSeconds(2), only.End);
        // El siguiente trozo ya va directo con json.
        await t.TranscribeAsync(new float[16000], CancellationToken.None);
        Assert.Equal(3, http.Requests.Count);
        Assert.DoesNotContain("verbose_json", http.Requests[2].Body);
    }

    [Fact]
    public void Wav_tiene_encabezado_y_16_bits()
    {
        var wav = CloudTranscriber.ToWav([0f, 1f, -1f, 2f]);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + 8, wav.Length);
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 46));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 50)); // 2f se recorta a 1
    }
}

public class SettingsAndLoginTests
{
    [Fact]
    public void Configuracion_se_guarda_sin_la_key_en_claro_y_se_recupera()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ia-{Guid.NewGuid():N}.json");
        var store = new AiSettingsStore(path);
        var s = new AiSettings(new Connection(AiProvider.DeepSeek, "deepseek-chat", "sk-secreta"),
            new Connection(AiProvider.Personalizado, "whisper-1", null, "http://localhost:8000/v1"), AnalyzeWhenMeetingEnds: false);

        store.Save(s);
        var back = store.Load();

        Assert.Equal(s, back);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("sk-secreta", File.ReadAllText(path));
        File.Delete(path);
    }

    [Fact]
    public void Archivo_dañado_da_configuracion_vacia()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ia-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{no es json");
        Assert.Equal(new AiSettings(), new AiSettingsStore(path).Load());
        File.Delete(path);
    }

    [Theory]
    [InlineData(AiProvider.DeepSeek, "deepseek-chat", null, null, false)]
    [InlineData(AiProvider.DeepSeek, "deepseek-chat", "sk", null, true)]
    [InlineData(AiProvider.Personalizado, "qwen3", null, null, true)]
    [InlineData(AiProvider.Anthropic, "claude-opus-5-5", "sk-ant", null, true)]
    [InlineData(AiProvider.OpenAI, " ", "sk", null, false)]
    public void Conexion_usable(AiProvider p, string model, string? key, string? url, bool usable) =>
        Assert.Equal(usable, new Connection(p, model, key, url).IsUsable);

    [Fact]
    public void Proveedores_sin_audio_no_crean_transcriptor() =>
        Assert.Throws<AiException>(() => AiFactory.CreateTranscriber(new Connection(AiProvider.DeepSeek, "deepseek-chat", "sk")));

    [Fact]
    public void Fabrica_elige_el_adaptador()
    {
        Assert.IsType<AnthropicChat>(AiFactory.CreateChat(new Connection(AiProvider.Anthropic, "claude-opus-5-5", "sk-ant")));
        var local = AiFactory.CreateChat(new Connection(AiProvider.Personalizado, "qwen3"));
        Assert.Equal("Mi servidor (Ollama, LM Studio, vLLM…) · qwen3", local.Name);
    }

    [Theory]
    [InlineData("GET /?code=abc123 HTTP/1.1", "abc123")]
    [InlineData("GET /favicon.ico HTTP/1.1", null)]
    [InlineData("GET /?error=denied HTTP/1.1", null)]
    public void Codigo_de_openrouter_se_lee_del_callback(string line, string? code) =>
        Assert.Equal(code, OpenRouterLogin.CodeFrom(line));

    [Fact]
    public void Pkce_usa_base64url_sin_relleno() =>
        Assert.Equal("-_8", OpenRouterLogin.Base64Url([0xFB, 0xFF]));

    [Fact]
    public void Fallback_de_claude_se_envia_como_default() =>
        Assert.Equal("\"default\"", JsonSerializer.Serialize(new BetaFallbacksParam(new Default())));
}
