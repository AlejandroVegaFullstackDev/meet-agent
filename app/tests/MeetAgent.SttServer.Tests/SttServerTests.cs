using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace MeetAgent.SttServer.Tests;

public sealed class EchoTranscriber : ITranscriber
{
    public int LastSampleCount;
    public string Name => "eco";
    public Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        LastSampleCount = samples.Length;
        return Task.FromResult<IReadOnlyList<RecognizedText>>(
        [
            new(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.6), "Hola", 0.95),
            new(TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(1.0), "equipo.", 0.8),
        ]);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class Server : IAsyncDisposable
{
    public readonly EchoTranscriber Transcriber = new();
    private readonly WebApplication _app;
    public HttpClient Http { get; }

    public Server(string? token = null)
    {
        var b = WebApplication.CreateSlimBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        _app = b.Build();
        _app.MapStt(Transcriber, token);
        _app.StartAsync().GetAwaiter().GetResult();
        var url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Http = new HttpClient { BaseAddress = new Uri(url) };
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class SttServerTests
{
    private static byte[] Wav(int rate, int channels, int seconds)
    {
        // WavWriter es 16 kHz mono; para otros formatos armamos el encabezado a mano.
        var n = rate * channels * seconds;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1);
        w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(3000 * Math.Sin(i * 0.05)));
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Los mismos campos que manda el bot de Vexa (transcription-client.ts).</summary>
    private static MultipartFormDataContent VexaForm(byte[] wav)
    {
        var f = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        f.Add(file, "file", "audio.wav");
        f.Add(new StringContent("whisper-1"), "model");
        f.Add(new StringContent("verbose_json"), "response_format");
        f.Add(new StringContent("es"), "language");
        f.Add(new StringContent("word"), "timestamp_granularities");
        return f;
    }

    [Fact]
    public async Task Health_responde_ok()
    {
        await using var s = new Server();
        var r = await s.Http.GetStringAsync("/health");
        Assert.Contains("\"ok\"", r);
    }

    [Fact]
    public async Task Responde_verbose_json_como_lo_espera_Vexa()
    {
        await using var s = new Server();
        var r = await s.Http.PostAsync("/v1/audio/transcriptions", VexaForm(Wav(16000, 1, 2)));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Hola equipo.", root.GetProperty("text").GetString());
        Assert.Equal("es", root.GetProperty("language").GetString());
        Assert.Equal(2, root.GetProperty("duration").GetDouble(), 3);
        var seg = root.GetProperty("segments")[0];
        Assert.Equal(0.2, seg.GetProperty("start").GetDouble(), 3);
        Assert.True(seg.GetProperty("avg_logprob").GetDouble() > -1.3);
        Assert.Equal(2, seg.GetProperty("words").GetArrayLength());
        Assert.Equal("equipo.", seg.GetProperty("words")[1].GetProperty("word").GetString());
    }

    [Fact]
    public async Task Convierte_48k_estereo_a_16k_mono()
    {
        await using var s = new Server();
        var r = await s.Http.PostAsync("/v1/audio/transcriptions", VexaForm(Wav(48000, 2, 1)));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.InRange(s.Transcriber.LastSampleCount, 15990, 16000);
    }

    [Fact]
    public async Task Con_token_rechaza_sin_Bearer_y_acepta_con_el()
    {
        await using var s = new Server(token: "secreto");
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.PostAsync("/v1/audio/transcriptions", VexaForm(Wav(16000, 1, 1)))).StatusCode);
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/transcriptions") { Content = VexaForm(Wav(16000, 1, 1)) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secreto");
        Assert.Equal(HttpStatusCode.OK, (await s.Http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Sin_archivo_o_con_basura_responde_400()
    {
        await using var s = new Server();
        var noFile = new MultipartFormDataContent { { new StringContent("whisper-1"), "model" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Http.PostAsync("/v1/audio/transcriptions", noFile)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Http.PostAsync("/v1/audio/transcriptions", VexaForm("no es un wav"u8.ToArray()))).StatusCode);
    }
}
