using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetAgent.Ai;

/// <summary>Resultado de instalar el componente oficial de una cuenta.</summary>
public enum AccountInstallStatus
{
    Installed,
    NeedsKey,
    Failed,
}

/// <param name="Message">Texto para el usuario; nunca incluye comandos ni rutas.</param>
public sealed record AccountInstallResult(AccountInstallStatus Status, string? Message = null)
{
    public static readonly AccountInstallResult Installed = new(AccountInstallStatus.Installed);
    public static AccountInstallResult NeedsKey(string message) => new(AccountInstallStatus.NeedsKey, message);
    public static AccountInstallResult Failed(string message) => new(AccountInstallStatus.Failed, message);
}

/// <summary>
/// Instala el componente oficial que permite usar la cuenta del usuario. Todo ocurre sin abrir una
/// consola: Claude usa su instalador, ChatGPT su binario oficial y Google su paquete de Node.
/// El usuario siempre confirma antes; las pruebas inyectan el ejecutor y la descarga.
/// </summary>
public sealed class AccountInstaller(
    ProcessRunner? run = null,
    HttpClient? http = null,
    string? toolsDir = null,
    Func<string, string?>? find = null)
{
    private readonly ProcessRunner _run = run ?? ProcessRunnerDefault.RunAsync;
    private readonly HttpClient _http = http ?? AiFactory.Http;
    private readonly Func<string, string?> _find = find ?? AccountDetector.FindOnPath;
    private readonly string _tools = toolsDir ?? AccountDetector.ToolsDir;

    public async Task<AccountInstallResult> InstallAsync(AiProvider provider, IProgress<string>? progress,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("Instalando…");
            return provider switch
            {
                AiProvider.ClaudeCuenta => await InstallClaudeAsync(ct).ConfigureAwait(false),
                AiProvider.ChatGptCuenta => await InstallCodexAsync(progress, ct).ConfigureAwait(false),
                AiProvider.GeminiCuenta => await InstallGeminiAsync(progress, ct).ConfigureAwait(false),
                _ => AccountInstallResult.Failed("Ese proveedor no necesita un componente."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException
                                       or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return AccountInstallResult.Failed("No pudimos instalar el componente. Revisa tu conexión e inténtalo de nuevo.");
        }
    }

    private async Task<AccountInstallResult> InstallClaudeAsync(CancellationToken ct)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(_find("powershell") ?? "powershell.exe")
            {
                ArgumentList =
                {
                    "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                    "irm https://claude.ai/install.ps1 | iex",
                },
            }
            : new ProcessStartInfo(_find("bash") ?? "bash")
            {
                ArgumentList = { "-c", "curl -fsSL https://claude.ai/install.sh | bash" },
            };
        info.UseShellExecute = false;
        info.CreateNoWindow = true;

        var (exit, _) = await _run(info, "", ct).ConfigureAwait(false);
        return exit == 0
            ? AccountInstallResult.Installed
            : AccountInstallResult.Failed("No pudimos instalar el componente de Claude. Inténtalo de nuevo o usa una API key.");
    }

    private async Task<AccountInstallResult> InstallCodexAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var assetName = CodexAssetName();
        if (assetName is null) return AccountInstallResult.Failed("No hay una versión de ChatGPT para este equipo.");

        progress?.Report("Descargando…");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/openai/codex/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MeetAgent", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var assets = JsonNode.Parse(json)?["assets"]?.AsArray();
        var asset = assets?.FirstOrDefault(a => a?["name"]?.GetValue<string>() == assetName);
        var url = asset?["browser_download_url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
            return AccountInstallResult.Failed("No encontramos la descarga oficial de ChatGPT. Inténtalo más tarde.");

        progress?.Report("Descargando…");
        Directory.CreateDirectory(_tools);
        var executable = Path.Combine(_tools, OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        var download = Path.Combine(_tools, "codex.download");
        try
        {
            await using (var source = await _http.GetStreamAsync(url, ct).ConfigureAwait(false))
            await using (var file = File.Create(download))
                await source.CopyToAsync(file, ct).ConfigureAwait(false);

            progress?.Report("Instalando…");
            if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                ExtractCodex(download, executable);
            else
                File.Move(download, executable, overwrite: true);
        }
        finally
        {
            if (File.Exists(download)) File.Delete(download);
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return AccountInstallResult.Installed;
    }

    private async Task<AccountInstallResult> InstallGeminiAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var node = _find("node") ?? "node";
        var (nodeExit, nodeOutput) = await _run(new ProcessStartInfo(node)
        {
            ArgumentList = { "--version" },
            UseShellExecute = false,
            CreateNoWindow = true,
        }, "", ct).ConfigureAwait(false);
        var major = ParseMajor(nodeOutput);
        if (nodeExit != 0 || major is null or < 20)
        {
            return AccountInstallResult.NeedsKey(
                "En este equipo, Google se conecta con una API key gratis. Consíguela y pégala aquí.");
        }

        progress?.Report("Instalando…");
        var npm = _find("npm") ?? (OperatingSystem.IsWindows() ? "npm.cmd" : "npm");
        var start = new ProcessStartInfo(npm)
        {
            ArgumentList = { "install", "-g", "@google/gemini-cli" },
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var (exit, _) = await _run(start, "", ct).ConfigureAwait(false);
        return exit == 0
            ? AccountInstallResult.Installed
            : AccountInstallResult.Failed("No pudimos instalar el componente de Google. Inténtalo de nuevo o usa una API key.");
    }

    /// <summary>Nombre real del binario en la última release de openai/codex (sin inventarlo).</summary>
    internal static string? CodexAssetName()
    {
        var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows())
            return arm ? "codex-aarch64-pc-windows-msvc.exe" : "codex-x86_64-pc-windows-msvc.exe";
        if (OperatingSystem.IsLinux())
            return arm ? "codex-aarch64-unknown-linux-musl.tar.gz" : "codex-x86_64-unknown-linux-musl.tar.gz";
        if (OperatingSystem.IsMacOS())
            return arm ? "codex-aarch64-apple-darwin.tar.gz" : "codex-x86_64-apple-darwin.tar.gz";
        return null;
    }

    internal static int? ParseMajor(string version)
    {
        var text = version.Trim().TrimStart('v', 'V');
        var dot = text.IndexOf('.');
        return int.TryParse(dot < 0 ? text : text[..dot], out var major) ? major : null;
    }

    private static void ExtractCodex(string archive, string destination)
    {
        using var archiveFile = File.OpenRead(archive);
        using var gzip = new GZipStream(archiveFile, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            var name = Path.GetFileName(entry.Name);
            if (entry.DataStream is null || !name.StartsWith("codex", StringComparison.OrdinalIgnoreCase)) continue;
            using var file = File.Create(destination);
            entry.DataStream.CopyTo(file);
            return;
        }
        throw new InvalidDataException("La descarga de ChatGPT no traía el programa.");
    }
}
