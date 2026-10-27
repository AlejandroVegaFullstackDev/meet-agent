using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MeetAgent.Ai;

/// <param name="Installed">El CLI está en el PATH.</param>
/// <param name="LoggedIn">Además, tiene una sesión iniciada.</param>
public sealed record AccountState(bool Installed, bool LoggedIn)
{
    public static readonly AccountState Missing = new(false, false);
}

/// <summary>
/// Detecta si el usuario tiene Claude Code, Codex o Gemini CLI instalados y con sesión iniciada.
/// No gasta tokens: solo consulta el estado o abre el login en una terminal.
/// </summary>
/// <param name="find">Busca un ejecutable en el PATH (en pruebas, uno falso).</param>
/// <param name="run">Ejecuta un CLI (en pruebas, uno falso).</param>
/// <param name="launch">Abre la terminal visible del login (en pruebas, uno falso).</param>
/// <param name="home">Carpeta del usuario, para los credenciales de Gemini.</param>
public sealed class AccountDetector(
    Func<string, string?>? find = null,
    ProcessRunner? run = null,
    Action<AiProvider>? launch = null,
    string? home = null,
    TimeSpan? pollInterval = null,
    TimeSpan? loginTimeout = null)
{
    private readonly Func<string, string?> _find = find ?? FindOnPath;
    private readonly ProcessRunner _run = run ?? ProcessRunnerDefault.RunAsync;
    private readonly Action<AiProvider> _launch = launch ?? LaunchTerminal;
    private readonly TimeSpan _poll = pollInterval ?? TimeSpan.FromSeconds(2);
    private readonly TimeSpan _timeout = loginTimeout ?? TimeSpan.FromMinutes(5);
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

    /// <summary>Página oficial de instalación para el botón «Instalar».</summary>
    public static string InstallUrl(AiProvider provider) => provider switch
    {
        AiProvider.ClaudeCuenta => "https://docs.anthropic.com/en/docs/claude-code/setup",
        AiProvider.ChatGptCuenta => "https://github.com/openai/codex",
        AiProvider.GeminiCuenta => "https://github.com/google-gemini/gemini-cli",
        _ => "https://github.com/AlejandroVegaFullstackDev/meet-agent",
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

    /// <summary>
    /// Abre el login en una terminal visible y espera a que la sesión quede iniciada.
    /// Devuelve false si no está instalado o si se agota el tiempo.
    /// </summary>
    public async Task<bool> LoginAsync(AiProvider provider, CancellationToken ct = default)
    {
        if (Executable(provider) is null) return false;
        _launch(provider);

        var until = DateTimeOffset.UtcNow + _timeout;
        while (DateTimeOffset.UtcNow < until)
        {
            if ((await DetectAsync(provider, ct).ConfigureAwait(false)).LoggedIn) return true;
            if (_poll > TimeSpan.Zero) await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
        return false;
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

    private static void LaunchTerminal(AiProvider provider)
    {
        if (CommandOf(provider) is not { } command) return;
        string[] args = provider switch
        {
            AiProvider.ClaudeCuenta => ["auth", "login"],
            AiProvider.ChatGptCuenta => ["login"],
            _ => [],
        };
        var line = args.Length == 0 ? command : $"{command} {string.Join(' ', args)}";

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // cmd /c start abre una consola visible aunque la app no tenga ninguna.
                Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    ArgumentList = { "/c", "start", "MeetAgent", "cmd.exe", "/k", line },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return;
            }

            foreach (var (terminal, prefix) in new[]
                     {
                         ("x-terminal-emulator", "-e"), ("gnome-terminal", "--"), ("konsole", "-e"),
                         ("xfce4-terminal", "-e"), ("xterm", "-e"),
                     })
            {
                try
                {
                    var start = new ProcessStartInfo(terminal) { UseShellExecute = false };
                    start.ArgumentList.Add(prefix);
                    start.ArgumentList.Add("sh");
                    start.ArgumentList.Add("-c");
                    start.ArgumentList.Add(line);
                    Process.Start(start);
                    return;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Prueba con la siguiente terminal.
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Sin terminal disponible: el usuario puede iniciar sesión a mano y reintentar.
        }
    }

    /// <summary>Busca el ejecutable en el PATH, también con .cmd/.exe en Windows.</summary>
    internal static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions.Prepend(""))
            {
                var candidate = Path.Combine(dir.Trim('"'), command + ext.ToLowerInvariant());
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.Trim('"'), command + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
