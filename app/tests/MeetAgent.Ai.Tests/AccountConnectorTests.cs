using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using MeetAgent.Ai;

namespace MeetAgent.Ai.Tests;

public class AccountConnectorTests
{
    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Values { get; } = new();
        public void Report(string value) => Values.Add(value);
    }

    private static AccountConnector Connector(FakeRunner runner, Func<string, string?> find,
        Func<AiProvider, Task<bool>>? confirm = null, AccountInstaller? installer = null) =>
        new(find, runner.RunAsync, installer, confirm,
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
    public async Task Sin_instalar_pide_permiso_instala_y_despues_conecta()
    {
        var installed = false;
        var loggedIn = false;
        var asked = false;
        using var runner = new FakeRunner((info, _) =>
        {
            var args = info.ArgumentList.ToList();
            if (info.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) || info.FileName == "bash")
            {
                installed = true;
                return (0, "");
            }
            if (args.SequenceEqual(["auth", "status", "--json"]))
                return (0, $$"""{"loggedIn":{{(loggedIn ? "true" : "false")}}}""");
            if (args.SequenceEqual(["auth", "login", "--help"])) return (0, "login");
            if (args.SequenceEqual(["auth", "login"])) { loggedIn = true; return (0, ""); }
            return (1, "no esperado");
        });
        var installer = new AccountInstaller(run: runner.RunAsync, find: _ => null);

        var result = await Connector(runner, name => installed && name == "claude" ? "claude" : null,
            _ => { asked = true; return Task.FromResult(true); }, installer)
            .ConnectAsync(AiProvider.ClaudeCuenta, null);

        Assert.True(asked);
        Assert.Equal(AccountConnectStatus.Connected, result.Status);
        Assert.Contains(runner.Calls, c => c.Info.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase)
                                           || c.Info.FileName == "bash");
    }

    [Fact]
    public async Task Sin_permiso_no_instala_nada()
    {
        using var runner = new FakeRunner((_, _) => (1, "no se debe llamar"));

        var result = await Connector(runner, _ => null, _ => Task.FromResult(false))
            .ConnectAsync(AiProvider.ChatGptCuenta, null);

        Assert.Equal(AccountConnectStatus.Cancelled, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Si_falla_la_instalacion_ofrece_la_api_key()
    {
        using var runner = new FakeRunner((_, _) => (1, "sin conexión"));
        var installer = new AccountInstaller(run: runner.RunAsync, find: _ => null);

        var result = await Connector(runner, _ => null, _ => Task.FromResult(true), installer)
            .ConnectAsync(AiProvider.ClaudeCuenta, null);

        Assert.Equal(AccountConnectStatus.Failed, result.Status);
        Assert.True(result.OffersApiKey);
        Assert.Contains("API key", result.Message);
    }

    [Fact]
    public async Task Gemini_sin_Node_pasa_a_la_key()
    {
        using var runner = new FakeRunner((info, _) =>
            info.ArgumentList.Contains("--version") ? (1, "no hay node") : (1, "no esperado"));
        var installer = new AccountInstaller(run: runner.RunAsync, find: name => name == "node" ? "node" : null);

        var result = await Connector(runner, _ => null, _ => Task.FromResult(true), installer)
            .ConnectAsync(AiProvider.GeminiCuenta, null);

        Assert.Equal(AccountConnectStatus.NeedsKey, result.Status);
        Assert.True(result.OffersApiKey);
        Assert.Contains("API key", result.Message);
    }
}

public class AccountInstallerTests
{
    private sealed class InstallerHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(answer(request));
    }

    [Fact]
    public void El_asset_de_Codex_es_el_nombre_real_de_la_release()
    {
        var asset = AccountInstaller.CodexAssetName();
        Assert.NotNull(asset);
        Assert.StartsWith("codex-", asset);
        Assert.Contains(OperatingSystem.IsWindows() ? "windows-msvc" : OperatingSystem.IsMacOS() ? "apple-darwin" : "linux-musl", asset);
    }

    [Fact]
    public async Task Codex_se_descarga_de_la_release_y_queda_en_tools()
    {
        if (AccountInstaller.CodexAssetName() is not { } asset)
        {
            Assert.Fail("Este sistema no tiene un binario de ChatGPT.");
            return;
        }

        var binary = Encoding.UTF8.GetBytes("programa de ChatGPT");
        var payload = asset.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? TarGz(binary) : binary;
        var json = $$"""{"assets":[{"name":"{{asset}}","browser_download_url":"https://example.test/{{asset}}"}]}""";
        using var http = new HttpClient(new InstallerHttp(request =>
            request.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
        var tools = Directory.CreateTempSubdirectory("meetagent-codex-").FullName;
        try
        {
            var installer = new AccountInstaller(http: http, toolsDir: tools);

            var result = await installer.InstallAsync(AiProvider.ChatGptCuenta, null);

            Assert.Equal(AccountInstallStatus.Installed, result.Status);
            var file = Path.Combine(tools, OperatingSystem.IsWindows() ? "codex.exe" : "codex");
            Assert.True(File.Exists(file));
            Assert.False(File.Exists(Path.Combine(tools, "codex.download")));
            Assert.Equal(binary, await File.ReadAllBytesAsync(file));
        }
        finally { Directory.Delete(tools, true); }
    }

    [Fact]
    public async Task Sin_Node_Gemini_pide_una_key()
    {
        using var runner = new FakeRunner((_, _) => (1, "no hay node"));
        var installer = new AccountInstaller(run: runner.RunAsync, find: name => name == "node" ? "node" : null);

        var result = await installer.InstallAsync(AiProvider.GeminiCuenta, null);

        Assert.Equal(AccountInstallStatus.NeedsKey, result.Status);
        Assert.Contains("API key", result.Message);
    }

    [Fact]
    public async Task Claude_usa_su_instalador_oficial_sin_ventana()
    {
        ProcessStartInfo? install = null;
        using var runner = new FakeRunner((info, _) => { install = info; return (0, ""); });
        var installer = new AccountInstaller(run: runner.RunAsync, find: _ => null);

        var result = await installer.InstallAsync(AiProvider.ClaudeCuenta, null);

        Assert.Equal(AccountInstallStatus.Installed, result.Status);
        Assert.NotNull(install);
        Assert.True(install!.CreateNoWindow);
        Assert.Contains(install.ArgumentList, a => a.Contains("claude.ai/install", StringComparison.Ordinal));
    }

    private static byte[] TarGz(byte[] content)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "codex")
            {
                DataStream = new MemoryStream(content),
            });
        }
        return memory.ToArray();
    }
}
