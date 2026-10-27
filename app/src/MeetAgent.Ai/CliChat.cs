using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

/// <summary>
/// Usa la cuenta que el usuario ya tiene en Claude Code, Codex o Gemini CLI: lanza el CLI con las
/// instrucciones por parámetro y el contenido por stdin. Sin API key y sin gastar de más.
/// </summary>
/// <param name="run">Ejecutor de procesos; en pruebas, uno falso.</param>
/// <param name="workDir">Carpeta de trabajo vacía; null para crear una temporal por llamada.</param>
/// <param name="executable">Busca el CLI (en pruebas, uno falso).</param>
public sealed class CliChat(Connection connection, ProcessRunner? run = null, string? workDir = null,
    Func<AiProvider, string?>? executable = null) : IChatModel
{
    /// <summary>Un análisis largo puede tardar; nunca dejamos el proceso colgado.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private readonly ProcessRunner _run = run ?? ProcessRunnerDefault.RunAsync;
    private readonly Func<AiProvider, string?> _executable = executable ?? AccountDetector.Default.Executable;

    public string Name => connection.Info.Label;

    public async Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct)
    {
        var provider = connection.Provider;
        var exe = _executable(provider)
                  ?? throw new AiException($"No encontramos {connection.Info.Label}. Instálalo o conecta otra IA.");

        var instructions = jsonSchema is null ? system : $"{system}\n\nEsquema JSON de la respuesta:\n{jsonSchema}";
        var dir = workDir ?? Directory.CreateTempSubdirectory("meetagent-ia-").FullName;
        var ownDir = workDir is null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return provider switch
            {
                AiProvider.ClaudeCuenta => await RunClaude(exe, dir, instructions, user, timeout.Token).ConfigureAwait(false),
                AiProvider.ChatGptCuenta => await RunCodex(exe, dir, $"{instructions}\n\n{user}", timeout.Token).ConfigureAwait(false),
                AiProvider.GeminiCuenta => await RunGemini(exe, dir, instructions, user, timeout.Token).ConfigureAwait(false),
                _ => throw new AiException("Ese proveedor no usa una cuenta del sistema."),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("La IA tardó demasiado. Inténtalo de nuevo.");
        }
        finally
        {
            if (ownDir)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { }
            }
        }
    }

    private async Task<string> RunClaude(string exe, string dir, string instructions, string user, CancellationToken ct)
    {
        var info = Start(exe, dir, Arguments("claude", instructions, null));
        var (exit, output) = await _run(info, user, ct).ConfigureAwait(false);
        EnsureOk(exit, output);
        return ParseClaude(output);
    }

    private async Task<string> RunCodex(string exe, string dir, string prompt, CancellationToken ct)
    {
        var resultPath = Path.Combine(dir, $"codex-{Guid.NewGuid():N}.txt");
        var info = Start(exe, dir, Arguments("codex", null, resultPath));
        var (exit, output) = await _run(info, prompt, ct).ConfigureAwait(false);
        EnsureOk(exit, output);
        var answer = File.Exists(resultPath) ? File.ReadAllText(resultPath) : output;
        return string.IsNullOrWhiteSpace(answer) ? throw new AiException("ChatGPT no devolvió una respuesta. Inténtalo de nuevo.") : answer.Trim();
    }

    private async Task<string> RunGemini(string exe, string dir, string instructions, string user, CancellationToken ct)
    {
        var info = Start(exe, dir, Arguments("gemini", instructions, null));
        var (exit, output) = await _run(info, user, ct).ConfigureAwait(false);
        EnsureOk(exit, output);
        return ParseGemini(output);
    }

    private ProcessStartInfo Start(string exe, string dir, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = dir };
        foreach (var a in arguments) info.ArgumentList.Add(a);
        return info;
    }

    /// <summary>Argumentos exactos de cada CLI (se prueban sin lanzar nada).</summary>
    internal static List<string> Arguments(string cli, string? instructions, string? resultPath) => cli switch
    {
        // Estas banderas aíslan Claude Code: sin ellas carga ~124k tokens de contexto.
        "claude" =>
        [
            "-p",
            "--system-prompt", instructions ?? "",
            "--tools", "",
            "--setting-sources", "",
            "--strict-mcp-config",
            "--disable-slash-commands",
            "--no-session-persistence",
            "--output-format", "json",
        ],
        "codex" =>
        [
            "exec",
            "--skip-git-repo-check",
            "--sandbox", "read-only",
            "-o", resultPath ?? "",
            "-",
        ],
        "gemini" =>
        [
            "--output-format", "json",
            "-p", instructions ?? "",
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(cli)),
    };

    /// <summary>Claude devuelve un objeto JSON con la respuesta en <c>result</c>.</summary>
    internal static string ParseClaude(string output)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(output); }
        catch (JsonException) { throw new AiException("Claude devolvió una respuesta que no se entiende. Inténtalo de nuevo."); }
        if (node is null) throw new AiException("Claude no devolvió nada. Inténtalo de nuevo.");
        if (node["is_error"]?.GetValue<bool>() == true)
        {
            var why = node["result"]?.GetValue<string>();
            throw new AiException(string.IsNullOrWhiteSpace(why)
                ? "Claude no pudo analizar la reunión. Revisa tu sesión en Claude Code."
                : $"Claude no pudo analizar la reunión: {Short(why)}");
        }
        return node["result"]?.GetValue<string>()
               ?? throw new AiException("Claude no devolvió texto. Inténtalo de nuevo.");
    }

    /// <summary>Gemini con <c>--output-format json</c> pone el texto en <c>response</c>.</summary>
    internal static string ParseGemini(string output)
    {
        try
        {
            if (JsonNode.Parse(output)?["response"]?.GetValue<string>() is { Length: > 0 } response) return response;
        }
        catch (JsonException)
        {
            // Algunas versiones contestan texto plano: se usa tal cual.
        }
        return string.IsNullOrWhiteSpace(output) ? throw new AiException("Gemini no devolvió una respuesta. Inténtalo de nuevo.") : output.Trim();
    }

    private void EnsureOk(int exit, string output)
    {
        if (exit == 0) return;
        var why = string.IsNullOrWhiteSpace(output) ? "" : $": {Short(output.Trim())}";
        throw new AiException($"{connection.Info.Label} no respondió. Revisa tu sesión{why}");
    }

    private static string Short(string text) => text.Length > 200 ? text[..200] + "…" : text;
}
