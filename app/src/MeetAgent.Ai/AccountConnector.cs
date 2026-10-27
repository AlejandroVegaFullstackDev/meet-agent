using System.Diagnostics;

namespace MeetAgent.Ai;

public enum AccountConnectStatus
{
    Connected,
    Cancelled,
    NeedsKey,
    Failed,
}

/// <param name="Message">Texto para mostrar en la tarjeta. Null cuando la conexión salió bien.</param>
public sealed record AccountConnectResult(AccountConnectStatus Status, string? Message = null)
{
    public static readonly AccountConnectResult Connected = new(AccountConnectStatus.Connected);
    public static readonly AccountConnectResult Cancelled = new(AccountConnectStatus.Cancelled);
    public static AccountConnectResult NeedsKey(string message) => new(AccountConnectStatus.NeedsKey, message);
    public static AccountConnectResult Failed(string message) => new(AccountConnectStatus.Failed, message);
    public bool OffersApiKey => Status is AccountConnectStatus.NeedsKey or AccountConnectStatus.Failed;
}

/// <summary>
/// Conecta una cuenta con un clic: comprueba si el componente oficial ya está instalado, pide permiso
/// para instalarlo si falta, abre el inicio de sesión en el navegador y espera sin mostrar una consola.
/// </summary>
public sealed class AccountConnector(
    Func<string, string?>? find = null,
    ProcessRunner? run = null,
    AccountInstaller? installer = null,
    Func<AiProvider, Task<bool>>? confirmInstall = null,
    string? home = null,
    TimeSpan? pollInterval = null,
    TimeSpan? loginTimeout = null)
{
    private readonly ProcessRunner _run = run ?? ProcessRunnerDefault.RunAsync;
    private readonly AccountDetector _detector = new(find, run, home);
    private readonly AccountInstaller _installer = installer ?? new AccountInstaller(run: run, find: find);
    private readonly Func<AiProvider, Task<bool>> _confirmInstall = confirmInstall ?? (_ => Task.FromResult(false));
    private readonly TimeSpan _poll = pollInterval ?? TimeSpan.FromSeconds(2);
    private readonly TimeSpan _timeout = loginTimeout ?? TimeSpan.FromMinutes(5);

    public Task<AccountState> DetectAsync(AiProvider provider, CancellationToken ct = default) =>
        _detector.DetectAsync(provider, ct);

    public async Task<AccountConnectResult> ConnectAsync(AiProvider provider, IProgress<string>? progress,
        CancellationToken ct = default)
    {
        if (AccountDetector.CommandOf(provider) is null)
            return AccountConnectResult.Failed("Ese proveedor no usa una cuenta.");

        progress?.Report("Comprobando…");
        var state = await _detector.DetectAsync(provider, ct).ConfigureAwait(false);
        if (!state.Installed)
        {
            if (!await _confirmInstall(provider).ConfigureAwait(false)) return AccountConnectResult.Cancelled;

            progress?.Report("Instalando…");
            var installed = await _installer.InstallAsync(provider, progress, ct).ConfigureAwait(false);
            if (installed.Status == AccountInstallStatus.NeedsKey)
                return AccountConnectResult.NeedsKey(installed.Message ?? "Puedes conectar Google con una API key.");
            if (installed.Status == AccountInstallStatus.Failed)
                return AccountConnectResult.Failed(installed.Message ?? "No pudimos instalar el componente.");

            state = await _detector.DetectAsync(provider, ct).ConfigureAwait(false);
            if (!state.Installed)
                return AccountConnectResult.Failed("No encontramos el componente después de instalarlo. Puedes usar una API key.");
        }

        if (!state.LoggedIn)
        {
            var login = await LoginArgumentsAsync(provider, ct).ConfigureAwait(false);
            if (login is null)
                return AccountConnectResult.Failed("Tu versión del componente no permite iniciar sesión. Puedes usar una API key.");

            progress?.Report("Abriendo el navegador…");
            if (!await LoginAsync(provider, login, progress, ct).ConfigureAwait(false))
                return AccountConnectResult.Failed("No se completó el inicio de sesión. Inténtalo de nuevo o usa una API key.");
        }

        progress?.Report("Conectado ✓");
        return AccountConnectResult.Connected;
    }

    /// <summary>
    /// Comprueba con <c>--help</c> que el inicio de sesión existe en esta versión. Si Claude cambió
    /// <c>auth login</c>, prueba el otro nombre conocido antes de rendirse.
    /// </summary>
    private async Task<string[]?> LoginArgumentsAsync(AiProvider provider, CancellationToken ct)
    {
        var exe = _detector.Executable(provider);
        if (exe is null) return null;

        string[][] candidates = provider switch
        {
            AiProvider.ClaudeCuenta => [["auth", "login"], ["login"]],
            AiProvider.ChatGptCuenta => [["login"]],
            _ => [[]],
        };
        foreach (var candidate in candidates)
        {
            var info = Start(exe, candidate.Append("--help"));
            var (exit, _) = await _run(info, "", ct).ConfigureAwait(false);
            if (exit == 0) return candidate;
        }
        return null;
    }

    private async Task<bool> LoginAsync(AiProvider provider, string[] arguments, IProgress<string>? progress,
        CancellationToken ct)
    {
        var exe = _detector.Executable(provider);
        if (exe is null) return false;

        using var loginCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var info = Start(exe, arguments);
        info.CreateNoWindow = true;
        info.UseShellExecute = false;
        var login = _run(info, "", loginCts.Token);
        progress?.Report("Esperando que inicies sesión…");
        try
        {
            var until = DateTimeOffset.UtcNow + _timeout;
            while (DateTimeOffset.UtcNow < until)
            {
                if ((await _detector.DetectAsync(provider, ct).ConfigureAwait(false)).LoggedIn) return true;
                if (login.IsFaulted) return false;
                if (_poll > TimeSpan.Zero) await Task.Delay(_poll, ct).ConfigureAwait(false);
            }
            return false;
        }
        finally
        {
            loginCts.Cancel();
            try { await login.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or AiException
                                           or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // El navegador ya está abierto; el proceso de login se cierra al terminar o al cancelar.
            }
        }
    }

    private static ProcessStartInfo Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}
