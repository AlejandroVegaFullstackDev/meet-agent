using MeetAgent.Control;
using MeetAgent.Core;
using MeetAgent.Core.Ports;
using MeetAgent.Storage.Sqlite;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MeetAgent.Mcp.Tests;

/// <summary>Carpeta MEETAGENT_HOME temporal con su propia base.</summary>
public sealed class TempHome : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("meetagent-mcp-").FullName;
    public string Db => Path.Combine(Directory.CreateDirectory(Path.Combine(Dir, "data")).FullName, "meetagent.db");
    public SqliteTranscriptStore Open() => new($"Data Source={Db}");
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }
}

public sealed class FakeApp(Func<ControlRequest, ControlResponse?> answer) : IAppControl
{
    public List<ControlRequest> Requests { get; } = new();
    public Task<ControlResponse?> SendAsync(ControlRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }
}

public class MeetingToolsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-5));
    private static TranscriptSegment S(Speaker sp, double t, string text) => new(sp, TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 3), text);

    [Fact]
    public async Task En_vivo_entrega_solo_lo_nuevo_con_un_cursor()
    {
        using var home = new TempHome();
        long id;
        using (var s = home.Open())
        {
            id = s.StartMeeting("Daily", T0, "transcribir");
            s.AddSegment(id, S(Speaker.Otros, 0, "Buenos días"));
            s.AddSegment(id, S(Speaker.Yo, 4, "Hola a todos"));
        }
        var app = new FakeApp(_ => new ControlResponse(true, true, id, "transcribir", "En vivo"));
        var tools = new MeetingTools(home.Open, app);

        var first = await tools.LiveAsync(0, null, default);
        Assert.Contains("[00:00:00] [Otros] Buenos días", first);
        Assert.Contains("[00:00:04] [Yo] Hola a todos", first);
        var cursor = long.Parse(first[(first.LastIndexOf(':') + 1)..].Trim());

        using (var s = home.Open()) s.AddSegment(id, S(Speaker.Otros, 8, "Empecemos"));
        var second = await tools.LiveAsync(cursor, null, default);
        Assert.Contains("Empecemos", second);
        Assert.DoesNotContain("Buenos días", second);
        Assert.Contains("en curso", second);
    }

    [Fact]
    public async Task Sin_app_abierta_lee_la_ultima_reunion_abierta_de_la_base()
    {
        using var home = new TempHome();
        using (var s = home.Open())
        {
            var id = s.StartMeeting("Abierta", T0, "transcribir");
            s.AddSegment(id, S(Speaker.Yo, 1, "texto guardado"));
        }
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null));
        Assert.Contains("texto guardado", await tools.LiveAsync(0, null, default));
        Assert.StartsWith("App: cerrada", await tools.StatusAsync(default));
    }

    [Fact]
    public async Task Empezar_sin_app_explica_que_hay_que_abrirla()
    {
        using var home = new TempHome();
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null));
        Assert.Contains("no está abierta", await tools.StartAsync("x", default));
    }

    [Fact]
    public async Task Empezar_solo_envia_titulo_y_reporta_lo_que_eligio_el_usuario()
    {
        using var home = new TempHome();
        var app = new FakeApp(r => new ControlResponse(true, true, 7, "transcribir", "El usuario eligió solo transcribir (el audio no se guarda)."));
        var tools = new MeetingTools(home.Open, app);
        var text = await tools.StartAsync("Planeación", default);
        var req = Assert.Single(app.Requests);
        Assert.Equal(new ControlRequest(ControlCommand.Start, "Planeación"), req);
        Assert.Contains("solo transcribir", text);
        Assert.Contains("#7", text);

        var nada = new MeetingTools(home.Open, new FakeApp(_ => new ControlResponse(true, false, null, "nada", "El usuario eligió «Nada»: no se inició la reunión.")));
        Assert.Contains("«Nada»", await nada.StartAsync(null, default));
    }

    [Fact]
    public void Leer_buscar_y_listar()
    {
        using var home = new TempHome();
        using (var s = home.Open())
        {
            var id = s.StartMeeting("Presupuesto Q4", T0, "grabar");
            s.AddSegment(id, S(Speaker.Otros, 0, "El presupuesto está aprobado"));
            s.EndMeeting(id, T0.AddMinutes(10));
        }
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null));
        Assert.Contains("# Presupuesto Q4", tools.ReadTranscript(null));
        Assert.Contains("[presupuesto]", tools.Search("PRESUPUESTO", 5));
        Assert.Contains("00:10:00", tools.ListMeetings(5));
        Assert.Contains("No existe la reunión #99", tools.ReadTranscript(99));
        Assert.Contains("Sin resultados", tools.Search("inexistente", 5));
    }
}

/// <summary>Arranca el servidor MCP real por stdio y le habla con el cliente oficial del SDK.</summary>
public class StdioIntegrationTests
{
    private static string ServerDll() =>
        Path.Combine(AppContext.BaseDirectory, "meetagent-mcp.dll");

    [Fact]
    public async Task Expone_las_nueve_herramientas_y_el_inicio_pasa_por_la_app()
    {
        using var home = new TempHome();
        using (var s = home.Open()) s.StartMeeting("previa", DateTimeOffset.Now.AddHours(-1), "transcribir");
        var pipe = $"meetagent-test-{Guid.NewGuid():N}";
        ControlRequest? seen = null;
        await using var fakeApp = ControlServer.Start(pipe, req =>
        {
            seen = req;
            return Task.FromResult(new ControlResponse(true, true, 3, "transcribir", "El usuario eligió solo transcribir (el audio no se guarda)."));
        });

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "meetagent",
            Command = "dotnet",
            Arguments = [ServerDll()],
            EnvironmentVariables = new Dictionary<string, string?> { ["MEETAGENT_HOME"] = home.Dir, ["MEETAGENT_CONTROL"] = pipe },
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        Assert.Equal(
            ["buscar", "decir", "empezar_reunion", "esperar_novedades", "estado", "leer_transcripcion", "listar_reuniones", "terminar_reunion", "transcripcion_en_vivo"],
            tools.Select(t => t.Name).Order());
        // Ninguna herramienta acepta un parámetro para grabar o fijar el consentimiento.
        Assert.All(tools, t => Assert.DoesNotMatch("(?i)grabar|record|consent", t.JsonSchema.GetRawText()));

        var start = await client.CallToolAsync("empezar_reunion", new Dictionary<string, object?> { ["titulo"] = "Daily" });
        var text = string.Join("", start.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("solo transcribir", text);
        Assert.Equal(new ControlRequest(ControlCommand.Start, "Daily"), seen);

        var list = await client.CallToolAsync("listar_reuniones", new Dictionary<string, object?>());
        Assert.Contains("previa", string.Join("", list.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }
}

public class VoiceAndWaitToolsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public async Task Decir_manda_el_texto_al_Meet_y_avisa_si_no_hay_Meet_abierto()
    {
        using var home = new TempHome();
        string? said = null;
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null),
            say: (t, _) => { said = t; return Task.FromResult<System.Text.Json.JsonElement?>(System.Text.Json.JsonDocument.Parse("""{"ok":true,"seconds":2.5}""").RootElement); });
        var r = await tools.SayAsync("Decidimos el presupuesto", default);
        Assert.Equal("Decidimos el presupuesto", said);
        Assert.Contains("2.5", r.Replace(',', '.'));

        var none = new MeetingTools(home.Open, new FakeApp(_ => null), say: (_, _) => Task.FromResult<System.Text.Json.JsonElement?>(null));
        Assert.Contains("Transcribir", await none.SayAsync("hola", default));
    }

    [Fact]
    public async Task Esperar_novedades_vuelve_en_cuanto_llega_texto_nuevo()
    {
        using var home = new TempHome();
        long id;
        using (var s = home.Open()) id = s.StartMeeting("Meet", T0, "subtitulos");
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null));
        var wait = tools.WaitAsync(0, 10, null, default);
        await Task.Delay(300);
        using (var s = home.Open()) s.AddSegment(id, new TranscriptSegment(Speaker.Otros, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hola a todos", "Ana"));
        var r = await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("[Ana] Hola a todos", r);
        Assert.Contains("siguiente desde_id", r);
    }

    [Fact]
    public async Task Con_palabra_clave_solo_vuelve_cuando_nombran_al_asistente()
    {
        using var home = new TempHome();
        long id;
        using (var s = home.Open()) id = s.StartMeeting("Meet", T0, "subtitulos");
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null));
        var wait = tools.WaitAsync(0, 10, "meetagent", default);
        using (var s = home.Open()) s.AddSegment(id, new TranscriptSegment(Speaker.Otros, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Sigamos con el sprint", "Ana"));
        await Task.Delay(1500);
        Assert.False(wait.IsCompleted);
        using (var s = home.Open()) s.AddSegment(id, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "Meet Agent, ¿qué decidimos?", "Ana"));
        var r = await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("te nombraron", r);
        Assert.Contains("¿qué decidimos?", r);
        Assert.Contains("Sigamos con el sprint", r);       // incluye el contexto previo
    }

    [Fact]
    public async Task Esperar_sin_novedades_vuelve_al_cumplirse_el_tiempo()
    {
        using var home = new TempHome();
        using (var s = home.Open()) s.StartMeeting("Meet", T0, "subtitulos");
        var tools = new MeetingTools(home.Open, new FakeApp(_ => null));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await tools.WaitAsync(0, 2, null, default);
        Assert.InRange(sw.Elapsed.TotalSeconds, 1.5, 6);
        Assert.Contains("sin novedades", r);
    }
}
