using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MeetAgent.Ai;

/// <param name="Installed">El componente oficial está instalado.</param>
/// <param name="LoggedIn">Además, tiene una sesión iniciada.</param>
public sealed record AccountState(bool Installed, bool LoggedIn)
{
    public static readonly AccountState Missing = new(false, false);
}

/// <summary>
/// Detecta si el usuario tiene instalados los componentes oficiales de Claude, ChatGPT o Google
/// y si su sesión ya está iniciada. No gasta tokens ni abre una consola.
/// </summary>
/// <param name="find">Busca un ejecutable (en pruebas, uno falso).</param>
/// <param name="run">Ejecuta un componente (en pruebas, uno falso).</param>
/// <param name="home">Carpeta del usuario, para los credenciales de Gemini.</param>
public sealed class AccountDetector(
    Func<string, string?>? find = null,
    ProcessRunner? run = null,
    string? home = null)
{
    private readonly Func<string, string?> _find = find ?? FindOnPath;
    private readonly ProcessRunner _run = run ?? ProcessRunnerDefault.RunAsync;
    private readonly string _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Detección real de este equipo.</summary>
    public static AccountDetector Default { get; } = new();

    /// <summary>Solo para pruebas: sustituye la detección real de <see cref="Connection.IsUsable"/>.</summary>
    internal static Func<AiProvider, bool>? InstalledOverride;

    public static bool IsInstalled(AiProvider provider) =>
        InstalledOverride?.Invoke(provider) ?? Default.Executable(provider) is not null;

    /// <summary>Nombre del ejecutable de cada cuenta, o null si el proveedor no usa CLI.</summary>
    public static string? CommandOf(AiProvider provider) => provider switch
    {
        AiProvider.ClaudeCuenta => "claude",
        AiProvider.ChatGptCuenta => "codex",
        AiProvider.GeminiCuenta => "gemini",
        _ => null,
    };

    /// <summary>Ruta del ejecutable, o null si no está instalado.</summary>
    public string? Executable(AiProvider provider) =>
        CommandOf(provider) is { } command ? _find(command) : null;

    /// <summary>Instalado y, si se puede saber, con la sesión iniciada.</summary>
    public async Task<AccountState> DetectAsync(AiProvider provider, CancellationToken ct = default)
    {
        var exe = Executable(provider);
        if (exe is null) return AccountState.Missing;

        try
        {
            return provider switch
            {
                AiProvider.ClaudeCuenta => new AccountState(true, await ClaudeLoggedIn(exe, ct).ConfigureAwait(false)),
                AiProvider.ChatGptCuenta => new AccountState(true, await CodexLoggedIn(exe, ct).ConfigureAwait(false)),
                AiProvider.GeminiCuenta => new AccountState(true, File.Exists(GeminiCredentials)),
                _ => new AccountState(true, false),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AccountState(true, false);
        }
    }

    private async Task<bool> ClaudeLoggedIn(string exe, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe) { ArgumentList = { "auth", "status", "--json" } };
        var (exit, output) = await _run(info, "", ct).ConfigureAwait(false);
        if (exit != 0 || string.IsNullOrWhiteSpace(output)) return false;
        try { return JsonNode.Parse(output)?["loggedIn"]?.GetValue<bool>() == true; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private async Task<bool> CodexLoggedIn(string exe, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe) { ArgumentList = { "login", "status" } };
        var (exit, _) = await _run(info, "", ct).ConfigureAwait(false);
        return exit == 0;
    }

    private string GeminiCredentials => Path.Combine(_home, ".gemini", "oauth_creds.json");

    /// <summary>Carpeta donde MeetAgent guarda el componente descargado de ChatGPT.</summary>
    public static string ToolsDir => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetAgent", "tools")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "MeetAgent", "tools");

    /// <summary>
    /// Busca el ejecutable en el PATH y en las carpetas donde los instaladores oficiales lo dejan
    /// (incluida la carpeta propia de MeetAgent y la de npm en Windows).
    /// </summary>
    internal static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directories = new List<string>();
        if (!string.IsNullOrWhiteSpace(path))
            directories.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim('"')));
        directories.Add(Path.Combine(home, ".local", "bin"));
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData)) directories.Add(Path.Combine(appData, "npm"));
        }
        directories.Add(ToolsDir);

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        foreach (var dir in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var ext in extensions.Prepend(""))
            {
                var candidate = Path.Combine(dir, command + ext.ToLowerInvariant());
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
