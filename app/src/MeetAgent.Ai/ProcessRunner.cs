using System.Diagnostics;

namespace MeetAgent.Ai;

/// <summary>
/// Ejecuta un proceso externo y devuelve su código de salida y su salida. Se inyecta en las pruebas
/// para no lanzar CLIs reales.
/// </summary>
public delegate Task<(int ExitCode, string Output)> ProcessRunner(ProcessStartInfo startInfo, string stdin, CancellationToken ct);

/// <summary>Ejecutor real: sin ventana, con la entrada por stdin y cancelable.</summary>
public static class ProcessRunnerDefault
{
    public static async Task<(int ExitCode, string Output)> RunAsync(ProcessStartInfo info, string stdin, CancellationToken ct)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;

        // En Windows, los CLI instalados con npm son .cmd. cmd.exe corta los argumentos en el primer salto
        // de línea (y las instrucciones tienen varios), así que se lanza directo lo que el .cmd envuelve.
        if (OperatingSystem.IsWindows() && (info.FileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                                            || info.FileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            var original = info.FileName;
            var (program, script) = ResolveNpmShim(original);
            if (program is not null)
            {
                info.FileName = program;
                if (script is not null) info.ArgumentList.Insert(0, script);
            }
            else
            {
                info.FileName = "cmd.exe";
                info.ArgumentList.Insert(0, original);
                info.ArgumentList.Insert(0, "/c");
            }
        }

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new AiException("No se pudo abrir el programa de la IA.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new AiException("No se pudo abrir el programa de la IA. Revisa que siga instalado.");
        }
        using var kill = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });

        // Se lee la salida antes de escribir la entrada: si el programa responde mientras aún recibe una
        // transcripción larga, ambos esperarían al otro con las tuberías llenas.
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(stdin))
                await process.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // El programa terminó sin leer todo (p. ej. sesión vencida): su salida dice por qué.
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var errors = await stderr.ConfigureAwait(false);
        return (process.ExitCode, process.ExitCode == 0 || string.IsNullOrWhiteSpace(errors) ? output : output + errors);
    }

    /// <summary>
    /// Lee el .cmd que genera npm (<c>"%dp0%\node_modules\…\cli.js" %*</c>) y devuelve el programa real:
    /// el .exe, o node con el script. (null, null) si el .cmd no tiene esa forma.
    /// </summary>
    internal static (string? Program, string? Script) ResolveNpmShim(string cmdPath)
    {
        string text;
        try { text = File.ReadAllText(cmdPath); }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }

        var match = System.Text.RegularExpressions.Regex.Match(text, "\"%dp0%\\\\([^\"]+)\"");
        if (!match.Success) return (null, null);
        var target = Path.Combine(Path.GetDirectoryName(cmdPath)!, match.Groups[1].Value);
        if (!File.Exists(target)) return (null, null);
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return (target, null);

        var node = Path.Combine(Path.GetDirectoryName(cmdPath)!, "node.exe");
        if (!File.Exists(node)) node = AccountDetector.FindOnPath("node") ?? "node.exe";
        return (node, target);
    }
}
