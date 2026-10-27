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

        // En Windows, los CLI instalados con npm son .cmd: hay que pasarlos por cmd.exe.
        if (OperatingSystem.IsWindows() && (info.FileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                                            || info.FileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            var original = info.FileName;
            info.FileName = "cmd.exe";
            info.ArgumentList.Insert(0, original);
            info.ArgumentList.Insert(0, "/c");
        }

        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new AiException("No se pudo abrir el programa de la IA.");
        using var kill = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });

        if (!string.IsNullOrEmpty(stdin))
        {
            await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
        }
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var errors = await stderr.ConfigureAwait(false);
        return (process.ExitCode, process.ExitCode == 0 || string.IsNullOrWhiteSpace(errors) ? output : output + errors);
    }
}
