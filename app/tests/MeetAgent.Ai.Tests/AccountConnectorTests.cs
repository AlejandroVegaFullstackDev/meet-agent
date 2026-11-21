using MeetAgent.Ai;

namespace MeetAgent.Ai.Tests;

public class AccountConnectorTests
{
    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Values { get; } = new();
        public void Report(string value) => Values.Add(value);
    }

    private static AccountConnector Connector(FakeRunner runner, Func<string, string?> find) =>
        new(find, runner.RunAsync,
            pollInterval: TimeSpan.FromMilliseconds(1), loginTimeout: TimeSpan.FromSeconds(2));

    [Fact]
    public async Task Instalado_y_con_sesion_conecta_al_instante()
    {
        using var runner = new FakeRunner((info, _) =>
            info.ArgumentList.SequenceEqual(["auth", "status", "--json"])
                ? (0, """{"loggedIn":true}""")
                : (1, "no esperado"));

        var result = await Connector(runner, _ => "claude").ConnectAsync(AiProvider.ClaudeCuenta, null);

        Assert.Equal(AccountConnectStatus.Connected, result.Status);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Instalado_sin_sesion_abre_el_navegador_sin_ventana_y_espera()
    {
        var loggedIn = false;
        using var runner = new FakeRunner((info, _) =>
        {
            var args = info.ArgumentList.ToList();
            if (args.SequenceEqual(["auth", "status", "--json"])) return (0, $$"""{"loggedIn":{{(loggedIn ? "true" : "false")}}}""");
            if (args.SequenceEqual(["auth", "login", "--help"])) return (0, "login");
            if (args.SequenceEqual(["auth", "login"])) { loggedIn = true; return (0, ""); }
            return (1, "no esperado");
        });
        var progress = new ListProgress();

        var result = await Connector(runner, _ => "claude").ConnectAsync(AiProvider.ClaudeCuenta, progress);

        Assert.Equal(AccountConnectStatus.Connected, result.Status);
        var login = runner.Calls.Single(c => c.Info.ArgumentList.SequenceEqual(["auth", "login"])).Info;
        Assert.True(login.CreateNoWindow);
        Assert.False(login.UseShellExecute);
        Assert.Contains("Abriendo el navegador…", progress.Values);
        Assert.Contains("Esperando que inicies sesión…", progress.Values);
    }

    [Fact]
    public async Task Sin_el_programa_de_Claude_no_instala_nada_y_ofrece_openrouter_o_key()
    {
        using var runner = new FakeRunner((_, _) => (1, "no se debe llamar"));

        var result = await Connector(runner, _ => null).ConnectAsync(AiProvider.ClaudeCuenta, null);

        Assert.Equal(AccountConnectStatus.Failed, result.Status);
        Assert.True(result.OffersApiKey);
        Assert.Contains("OpenRouter", result.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Google_sin_su_programa_pasa_a_la_key_gratis()
    {
        using var runner = new FakeRunner((_, _) => (1, "no se debe llamar"));

        var result = await Connector(runner, _ => null).ConnectAsync(AiProvider.GeminiCuenta, null);

        Assert.Equal(AccountConnectStatus.NeedsKey, result.Status);
        Assert.Contains("key", result.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Si_el_login_termina_sin_sesion_no_espera_los_cinco_minutos()
    {
        using var runner = new FakeRunner((info, _) =>
        {
            var args = info.ArgumentList.ToList();
            if (args.SequenceEqual(["auth", "status", "--json"])) return (0, """{"loggedIn":false}""");
            if (args.SequenceEqual(["auth", "login", "--help"])) return (0, "login");
            return (1, "cancelado");
        });
        var connector = new AccountConnector(_ => "claude", runner.RunAsync,
            pollInterval: TimeSpan.FromMilliseconds(1), loginTimeout: TimeSpan.FromMinutes(5));

        var result = await connector.ConnectAsync(AiProvider.ClaudeCuenta, null).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(AccountConnectStatus.Failed, result.Status);
    }
}
