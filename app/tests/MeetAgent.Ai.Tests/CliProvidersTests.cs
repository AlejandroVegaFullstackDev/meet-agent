using System.Diagnostics;
using System.Net;
using System.Text;
using MeetAgent.Ai;

namespace MeetAgent.Ai.Tests;

/// <summary>Ejecutor de procesos falso: nada sale de la prueba.</summary>
internal sealed class FakeRunner(Func<ProcessStartInfo, string, (int Exit, string Output)> answer) : IDisposable
{
    public List<(ProcessStartInfo Info, string Stdin)> Calls { get; } = new();

    public Task<(int ExitCode, string Output)> RunAsync(ProcessStartInfo info, string stdin, CancellationToken ct)
    {
        Calls.Add((info, stdin));
        var (exit, output) = answer(info, stdin);
        return Task.FromResult((exit, output));
    }

    public void Dispose() { }
}

public class CliChatTests
{
    private static Connection Cuenta(AiProvider p) => new(p, Providers.Get(p).ChatModel);

    [Fact]
    public async Task Claude_usa_las_banderas_de_aislamiento_y_lee_result()
    {
        using var runner = new FakeRunner((info, stdin) =>
        {
            Assert.Contains("contenido de la reunión", stdin);
            var args = info.ArgumentList.ToList();
            Assert.StartsWith("sistema", args[args.IndexOf("--system-prompt") + 1]);
            Assert.Equal("", args[args.IndexOf("--tools") + 1]);
            Assert.Equal("", args[args.IndexOf("--setting-sources") + 1]);
            Assert.Contains("--strict-mcp-config", args);
            Assert.Contains("--disable-slash-commands", args);
            Assert.Contains("--no-session-persistence", args);
            Assert.Equal("json", args[args.IndexOf("--output-format") + 1]);
            return (0, """{"type":"result","is_error":false,"result":"{\"resumen\":\"ok\"}"}""");
        });
        var chat = new CliChat(Cuenta(AiProvider.ClaudeCuenta), runner.RunAsync, executable: _ => "claude");

        var text = await chat.CompleteAsync("sistema", "contenido de la reunión", "{\"type\":\"object\"}", default);

        Assert.Equal("{\"resumen\":\"ok\"}", text);
        Assert.Contains("Esquema JSON", runner.Calls[0].Info.ArgumentList[2]);
    }

    [Fact]
    public async Task Claude_con_is_error_se_convierte_en_AiException()
    {
        using var runner = new FakeRunner((_, _) => (0, """{"is_error":true,"result":"límite de uso"}"""));
        var chat = new CliChat(Cuenta(AiProvider.ClaudeCuenta), runner.RunAsync, executable: _ => "claude");

        var ex = await Assert.ThrowsAsync<AiException>(() => chat.CompleteAsync("s", "u", null, default));

        Assert.Contains("límite de uso", ex.Message);
    }

    [Fact]
    public async Task Codex_lee_el_archivo_de_salida_y_pasa_el_prompt_por_stdin()
    {
        using var runner = new FakeRunner((info, stdin) =>
        {
            var args = info.ArgumentList.ToList();
            Assert.Equal("exec", args[0]);
            Assert.Contains("--skip-git-repo-check", args);
            Assert.Equal("read-only", args[args.IndexOf("--sandbox") + 1]);
            Assert.Equal("-", args[^1]);
            Assert.Contains("sistema", stdin);
            Assert.Contains("contenido", stdin);
            File.WriteAllText(args[args.IndexOf("-o") + 1], "respuesta de Codex");
            return (0, "");
        });
        var chat = new CliChat(Cuenta(AiProvider.ChatGptCuenta), runner.RunAsync, executable: _ => "codex");

        var text = await chat.CompleteAsync("sistema", "contenido", null, default);

        Assert.Equal("respuesta de Codex", text);
    }

    [Fact]
    public async Task Gemini_pide_json_y_lee_response()
    {
        using var runner = new FakeRunner((info, stdin) =>
        {
            Assert.Equal("json", info.ArgumentList[info.ArgumentList.IndexOf("--output-format") + 1]);
            Assert.Equal("sistema", info.ArgumentList[info.ArgumentList.IndexOf("-p") + 1]);
            Assert.Equal("contenido", stdin);
            return (0, """{"response":"{\"resumen\":\"hola\"}","stats":{}}""");
        });
        var chat = new CliChat(Cuenta(AiProvider.GeminiCuenta), runner.RunAsync, executable: _ => "gemini");

        var text = await chat.CompleteAsync("sistema", "contenido", null, default);

        Assert.Equal("{\"resumen\":\"hola\"}", text);
    }

    [Fact]
    public async Task Un_cli_que_falla_no_tapa_el_motivo()
    {
        using var runner = new FakeRunner((_, _) => (1, "no estás dentro de un repositorio"));
        var chat = new CliChat(Cuenta(AiProvider.ChatGptCuenta), runner.RunAsync, executable: _ => "codex");

        var ex = await Assert.ThrowsAsync<AiException>(() => chat.CompleteAsync("s", "u", null, default));

        Assert.Contains("no estás dentro de un repositorio", ex.Message);
    }

    [Fact]
    public async Task Sin_cli_instalado_dice_que_hacer()
    {
        var chat = new CliChat(Cuenta(AiProvider.ClaudeCuenta), executable: _ => null);
        var ex = await Assert.ThrowsAsync<AiException>(() => chat.CompleteAsync("s", "u", null, default));
        Assert.Contains("Instálalo", ex.Message);
    }

    [Fact]
    public void Los_argumentos_de_claude_llevan_el_esquema_dentro_del_sistema()
    {
        var args = CliChat.Arguments("claude", "reglas\n\nEsquema JSON: {}", null);
        Assert.Equal("reglas\n\nEsquema JSON: {}", args[args.IndexOf("--system-prompt") + 1]);
    }

    [Fact]
    public void Gemini_sin_json_devuelve_el_texto_plano()
    {
        Assert.Equal("listo", CliChat.ParseGemini("  listo  "));
    }
}

public class AccountDetectorTests
{
    private static AccountDetector Detector(FakeRunner runner, Func<string, string?>? find = null,
        string? home = null) =>
        new(find ?? (_ => "cli"), runner.RunAsync, home);

    [Fact]
    public async Task Claude_esta_conectado_si_auth_status_dice_loggedIn()
    {
        using var runner = new FakeRunner((info, _) =>
        {
            Assert.Equal(["auth", "status", "--json"], info.ArgumentList.ToList());
            return (0, """{"loggedIn":true}""");
        });
        var state = await Detector(runner).DetectAsync(AiProvider.ClaudeCuenta);
        Assert.True(state.Installed);
        Assert.True(state.LoggedIn);
    }

    [Fact]
    public async Task Claude_sin_sesion_no_esta_conectado()
    {
        using var runner = new FakeRunner((_, _) => (0, """{"loggedIn":false}"""));
        var state = await Detector(runner).DetectAsync(AiProvider.ClaudeCuenta);
        Assert.True(state.Installed);
        Assert.False(state.LoggedIn);
    }

    [Fact]
    public async Task Codex_usa_el_codigo_de_salida_de_login_status()
    {
        using var runner = new FakeRunner((_, _) => (0, "logged in"));
        Assert.True((await Detector(runner).DetectAsync(AiProvider.ChatGptCuenta)).LoggedIn);
        using var bad = new FakeRunner((_, _) => (1, "not logged"));
        Assert.False((await Detector(bad).DetectAsync(AiProvider.ChatGptCuenta)).LoggedIn);
    }

    [Fact]
    public async Task Gemini_mira_sus_credenciales()
    {
        var home = Directory.CreateTempSubdirectory("meetagent-gemini-").FullName;
        try
        {
            using var runner = new FakeRunner((_, _) => (0, ""));
            Assert.False((await Detector(runner, home: home).DetectAsync(AiProvider.GeminiCuenta)).LoggedIn);
            Directory.CreateDirectory(Path.Combine(home, ".gemini"));
            File.WriteAllText(Path.Combine(home, ".gemini", "oauth_creds.json"), "{}");
            Assert.True((await Detector(runner, home: home).DetectAsync(AiProvider.GeminiCuenta)).LoggedIn);
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public async Task Sin_instalar_el_estado_esta_vacio()
    {
        using var runner = new FakeRunner((_, _) => (0, """{"loggedIn":true}"""));
        var state = await Detector(runner, find: _ => null).DetectAsync(AiProvider.ClaudeCuenta);
        Assert.False(state.Installed);
        Assert.False(state.LoggedIn);
    }

    [Fact]
    public void Las_cuentas_no_piden_key()
    {
        Assert.False(Providers.Get(AiProvider.ClaudeCuenta).NeedsKey);
        Assert.False(Providers.Get(AiProvider.ClaudeCuenta).CanTranscribe);
        Assert.True(Providers.Get(AiProvider.GeminiCuenta).IsAccount);
    }

    [Fact]
    public void Una_cuenta_es_usable_solo_si_el_cli_existe()
    {
        var c = new Connection(AiProvider.ClaudeCuenta, "Claude");
        try
        {
            AccountDetector.InstalledOverride = _ => false;
            Assert.False(c.IsUsable);
            AccountDetector.InstalledOverride = _ => true;
            Assert.True(c.IsUsable);
        }
        finally { AccountDetector.InstalledOverride = null; }
    }

    [Fact]
    public void La_fabrica_rutea_las_cuentas_a_cli_chat()
    {
        try
        {
            AccountDetector.InstalledOverride = _ => true;
            Assert.IsType<CliChat>(AiFactory.CreateChat(new Connection(AiProvider.ChatGptCuenta, "ChatGPT")));
        }
        finally { AccountDetector.InstalledOverride = null; }
    }
}

/// <summary>Responde según la URL: sirve para sondear dos servidores a la vez.</summary>
internal sealed class UrlHttp(Func<string, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var (status, body) = answer(request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

public class LocalModelsTests
{
    [Fact]
    public async Task Lista_ollama_y_lm_studio_a_la_vez()
    {
        var http = new HttpClient(new UrlHttp(url => url.Contains("11434")
            ? (HttpStatusCode.OK, """{"models":[{"name":"qwen3:8b"}]}""")
            : (HttpStatusCode.OK, """{"data":[{"id":"deepseek-r1"}]}""")))
        {
            Timeout = LocalModels.Timeout,
        };

        var models = await new LocalModels(http).ListAsync();

        Assert.Equal(["qwen3:8b", "deepseek-r1"], models.Select(m => m.Name));
        Assert.Equal("Ollama", models[0].Origin);
        Assert.Equal("http://localhost:1234/v1", models[1].BaseUrl);
    }

    [Fact]
    public async Task Si_un_servidor_no_esta_el_otro_sigue()
    {
        var http = new HttpClient(new UrlHttp(url => url.Contains("11434")
            ? (HttpStatusCode.InternalServerError, "{}")
            : (HttpStatusCode.OK, """{"data":[{"id":"qwen3-4b"}]}""")))
        {
            Timeout = LocalModels.Timeout,
        };

        var models = await new LocalModels(http).ListAsync();

        Assert.Equal("qwen3-4b", Assert.Single(models).Name);
    }

    [Fact]
    public async Task Sin_servidores_devuelve_lista_vacia()
    {
        var http = new HttpClient(new UrlHttp(_ => (HttpStatusCode.NotFound, "{}"))) { Timeout = LocalModels.Timeout };
        Assert.Empty(await new LocalModels(http).ListAsync());
    }

    [Fact]
    public void Los_parseos_toleran_respuestas_raras()
    {
        Assert.Empty(LocalModels.ParseOllama("""{"models":[]}"""));
        Assert.Empty(LocalModels.ParseLmStudio("{}"));
        Assert.Equal(["a"], LocalModels.ParseLmStudio("""{"data":[{"id":"a"},{"id":null}]}"""));
    }
}
