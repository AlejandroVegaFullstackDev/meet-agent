using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace MeetAgent.Ai;

/// <summary>
/// «Conectar con OpenRouter»: OAuth con PKCE. Abre el navegador, el usuario inicia sesión (con Google, GitHub…)
/// y OpenRouter devuelve a <c>http://localhost:3000/</c> un código que se cambia por una API key.
/// Con esa key se usan Claude, GPT, DeepSeek, Gemini, Qwen… pagando en OpenRouter.
/// </summary>
public static class OpenRouterLogin
{
    /// <summary>OpenRouter acepta callbacks a localhost:3000 sin registrar la app.</summary>
    public const int CallbackPort = 3000;

    public static async Task<string> LoginAsync(HttpClient http, Action<string>? openBrowser = null, CancellationToken ct = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var callback = $"http://localhost:{CallbackPort}/";

        var listener = new TcpListener(IPAddress.Loopback, CallbackPort);
        try { listener.Start(); }
        catch (SocketException)
        {
            throw new AiException($"El puerto {CallbackPort} está ocupado (¿otro servidor de desarrollo?). Ciérralo y vuelve a intentar.");
        }
        try
        {
            var url = $"https://openrouter.ai/auth?callback_url={Uri.EscapeDataString(callback)}&code_challenge={challenge}&code_challenge_method=S256";
            (openBrowser ?? OpenBrowser)(url);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var code = await WaitForCodeAsync(listener, timeout.Token).ConfigureAwait(false);

            var body = new JsonObject { ["code"] = code, ["code_verifier"] = verifier, ["code_challenge_method"] = "S256" };
            using var res = await http.PostAsync("https://openrouter.ai/api/v1/auth/keys",
                new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw new AiException($"OpenRouter no entregó la key ({(int)res.StatusCode}): {OpenAiCompatibleChat.Trim(text)}");
            return JsonNode.Parse(text)?["key"]?.GetValue<string>() ?? throw new AiException("OpenRouter no devolvió una key.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("Pasaron 5 minutos sin terminar el inicio de sesión en OpenRouter.");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Atiende peticiones a localhost hasta que llegue una con <c>?code=</c> (el navegador también pide /favicon.ico).</summary>
    private static async Task<string> WaitForCodeAsync(TcpListener listener, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? "";
            var code = CodeFrom(requestLine);
            var html = code is null ? "" : "<!doctype html><meta charset=utf-8><title>MeetAgent</title><p style='font:16px sans-serif'>Listo: MeetAgent ya está conectado a OpenRouter. Puedes cerrar esta pestaña.";
            var bytes = Encoding.UTF8.GetBytes(html);
            var head = $"HTTP/1.1 {(code is null ? "404 Not Found" : "200 OK")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            if (code is not null) return code;
        }
    }

    /// <summary>De <c>GET /?code=abc HTTP/1.1</c> saca <c>abc</c>.</summary>
    internal static string? CodeFrom(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return null;
        var q = parts[1].IndexOf('?');
        if (q < 0) return null;
        var code = HttpUtility.ParseQueryString(parts[1][(q + 1)..])["code"];
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    internal static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static void OpenBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
