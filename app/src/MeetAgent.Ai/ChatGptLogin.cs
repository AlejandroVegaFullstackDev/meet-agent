using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace MeetAgent.Ai;

/// <summary>
/// «Continuar con ChatGPT» (flujo oficial para apps de código abierto): el usuario inicia sesión en el
/// navegador, autoriza a MeetAgent y la app usa su plan de ChatGPT, sin API key ni programas extra.
/// Registro dinámico con <c>dynamic_agent_client</c>, PKCE, callback a <c>127.0.0.1</c> y validación
/// del ID token con las claves publicadas de OpenAI.
/// Docs: https://developers.openai.com/siwc/token-sharing-open-source
/// </summary>
public static class ChatGptLogin
{
    public const string Issuer = "https://auth.openai.com";
    public const string Resource = "https://api.openai.com/v1";
    public const string AppName = "MeetAgent";
    private const string AuthorizeUrl = Issuer + "/api/accounts/authorize";
    private const string TokenUrl = Issuer + "/api/accounts/oauth/token";
    private const string JwksUrl = Issuer + "/.well-known/jwks.json";
    private const string RevokeDiscovery = Issuer + "/.well-known/openid-configuration";
    private const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";

    /// <summary>Puerto sugerido por OpenAI; si está ocupado se usa otro libre (solo el puerto puede cambiar).</summary>
    public const int PreferredPort = 1455;

    /// <summary>Un refresco a la vez: el refresh token rota y dos a la vez se invalidarían.</summary>
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    /// <summary>Abre el navegador, espera la autorización y guarda la sesión. Devuelve las credenciales.</summary>
    public static async Task<ChatGptCredentials> LoginAsync(HttpClient http, ChatGptSessionStore store,
        Action<string>? openBrowser = null, CancellationToken ct = default)
    {
        var hostId = store.HostId;
        var savedClient = store.ClientId;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));

        var listener = StartListener();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = $"http://127.0.0.1:{port}/auth/callback";
            var query = new List<(string, string)>
            {
                ("client_id", savedClient ?? "dynamic_agent_client"),
                ("ext_agent_host_id", hostId),
                ("response_type", "code"),
                ("redirect_uri", redirect),
                ("scope", Scopes),
                ("resource", Resource),
                ("state", state),
                ("nonce", nonce),
                ("code_challenge_method", "S256"),
                ("code_challenge", challenge),
            };
            if (savedClient is null) query.Add(("agent_name_hint", AppName));
            var url = AuthorizeUrl + "?" + string.Join("&", query.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
            (openBrowser ?? OpenRouterLogin.OpenBrowser)(url);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var callback = await WaitForCallbackAsync(listener, timeout.Token).ConfigureAwait(false);

            if (callback["state"] != state) throw new AiException("El inicio de sesión de ChatGPT no coincide. Inténtalo de nuevo.");
            if (callback["error"] is { } error)
            {
                throw new AiException(error == "access_denied"
                    ? "Cancelaste el permiso en ChatGPT. Vuelve a intentarlo y acepta para conectar."
                    : "ChatGPT no completó el inicio de sesión. Inténtalo de nuevo.");
            }
            var code = callback["code"] ?? throw new AiException("ChatGPT no completó el inicio de sesión. Inténtalo de nuevo.");
            var issued = callback["client_id"];
            string clientId;
            if (savedClient is null)
            {
                clientId = issued is { Length: > 0 } && issued != "dynamic_agent_client"
                    ? issued
                    : throw new AiException("ChatGPT no terminó de registrar MeetAgent. Inténtalo de nuevo.");
            }
            else
            {
                if (issued is { Length: > 0 } && issued != savedClient)
                    throw new AiException("ChatGPT devolvió otra cuenta. Inténtalo de nuevo.");
                clientId = savedClient;
            }

            var tokens = await PostTokenAsync(http, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirect,
                ["resource"] = Resource,
            }, ct).ConfigureAwait(false);

            var idToken = tokens["id_token"]?.GetValue<string>()
                          ?? throw new AiException("ChatGPT no devolvió tu identidad. Inténtalo de nuevo.");
            var claims = await ValidateIdTokenAsync(http, idToken, clientId, nonce, ct).ConfigureAwait(false);
            var credentials = ToCredentials(tokens, clientId, claims["sub"]!.GetValue<string>(),
                claims["email"]?.GetValue<string>(), idToken, previousRefresh: null);
            store.Save(credentials);

            if (!credentials.CanUsePlan)
                throw new AiException("Iniciaste sesión, pero no autorizaste a MeetAgent a usar tu plan de ChatGPT. Vuelve a intentarlo y acepta el permiso.");
            return credentials;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException("Pasaron 5 minutos sin terminar el inicio de sesión en ChatGPT.");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Access token vigente; lo refresca si vence en menos de 2 minutos.</summary>
    public static async Task<string> AccessTokenAsync(HttpClient http, ChatGptSessionStore store, CancellationToken ct)
    {
        await RefreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var c = store.Load() ?? throw new AiException("Inicia sesión con ChatGPT en Conectar IA.");
            if (c.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return c.AccessToken;

            JsonObject tokens;
            try
            {
                tokens = await PostTokenAsync(http, new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = c.ClientId,
                    ["refresh_token"] = c.RefreshToken,
                    ["resource"] = Resource,
                }, ct).ConfigureAwait(false);
            }
            catch (TokenException ex) when (ex.Code is "invalid_grant" or "invalid_refresh_token" or "token_expired"
                                                or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
            {
                store.SignOut();
                throw new AiException("Tu sesión de ChatGPT venció. Vuelve a iniciar sesión en Conectar IA.");
            }
            var renewed = ToCredentials(tokens, c.ClientId, c.Subject, c.Email,
                tokens["id_token"]?.GetValue<string>() ?? c.IdToken, c.RefreshToken, c.Scopes);
            store.Save(renewed);
            return renewed.AccessToken;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    /// <summary>Cierra la sesión: revoca el refresh token en OpenAI (si se puede) y borra los tokens locales.</summary>
    public static async Task SignOutAsync(HttpClient http, ChatGptSessionStore store, CancellationToken ct = default)
    {
        if (store.Load() is { } c)
        {
            try
            {
                var discovery = JsonNode.Parse(await http.GetStringAsync(RevokeDiscovery, ct).ConfigureAwait(false));
                if (discovery?["revocation_endpoint"]?.GetValue<string>() is { Length: > 0 } endpoint)
                {
                    using var res = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["token"] = c.RefreshToken,
                        ["token_type_hint"] = "refresh_token",
                        ["client_id"] = c.ClientId,
                    }), ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // Sin red: se borra igual en local; el usuario puede desconectar la app en ChatGPT → Ajustes.
            }
        }
        store.SignOut();
    }

    /// <summary>
    /// Reutiliza la sesión que ya está en este equipo (<c>data/chatgpt.json</c>) sin abrir el navegador:
    /// refresca el access token si venció y pide el modelo. Devuelve null si no hay sesión o si OpenAI ya
    /// no la acepta (en ese caso la borra y toca el login normal).
    /// </summary>
    public static async Task<string?> ResumeAsync(HttpClient http, ChatGptSessionStore store, CancellationToken ct = default)
    {
        if (!store.HasSession) return null;
        try
        {
            var token = await AccessTokenAsync(http, store, ct).ConfigureAwait(false);
            return await DefaultModelAsync(http, token, ct).ConfigureAwait(false);
        }
        catch (AiException) when (!store.HasSession)
        {
            return null; // el refresh token venció: AccessTokenAsync ya cerró la sesión
        }
        catch (SessionRejectedException)
        {
            store.SignOut();
            return null;
        }
    }

    private sealed class SessionRejectedException(string message) : AiException(message);

    /// <summary>Primer modelo que la cuenta muestra (<c>visibility: "list"</c>), en el orden del servidor.</summary>
    public static async Task<string> DefaultModelAsync(HttpClient http, string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Resource + "/models");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new SessionRejectedException(ChatGptPlanChat.Explain(401, text));
        if (!res.IsSuccessStatusCode) throw new AiException(ChatGptPlanChat.Explain((int)res.StatusCode, text));
        var models = JsonNode.Parse(text)?["models"]?.AsArray();
        var slug = models?.FirstOrDefault(m => m?["visibility"]?.GetValue<string>() == "list")?["slug"]?.GetValue<string>();
        return slug ?? throw new AiException("Tu cuenta de ChatGPT no tiene modelos disponibles para otras apps.");
    }

    private static ChatGptCredentials ToCredentials(JsonObject t, string clientId, string subject, string? email,
        string idToken, string? previousRefresh, IReadOnlyList<string>? previousScopes = null)
    {
        var access = t["access_token"]?.GetValue<string>() ?? throw new AiException("ChatGPT no devolvió el acceso. Inténtalo de nuevo.");
        var refresh = t["refresh_token"]?.GetValue<string>() ?? previousRefresh
                      ?? throw new AiException("ChatGPT no devolvió una sesión duradera. Inténtalo de nuevo.");
        var expiresIn = t["expires_in"]?.GetValue<int>() ?? 3600;
        // Sin «scope» en la respuesta, OAuth (RFC 6749 §5.1) da los mismos permisos de antes: al refrescar se
        // conservan; si no, la sesión perdería el permiso del plan y la app diría «Falta conectar tu IA».
        IReadOnlyList<string> scopes = t["scope"]?.GetValue<string>() is { } granted
            ? granted.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : previousScopes ?? [];
        return new ChatGptCredentials(clientId, subject, email, idToken, access, refresh,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn), scopes);
    }

    private sealed class TokenException(string? code, string message) : AiException(message)
    {
        public string? Code => code;
    }

    private static async Task<JsonObject> PostTokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new AiException("No se pudo conectar con ChatGPT. Revisa tu conexión a internet.");
        }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonNode? node = null;
            try { node = JsonNode.Parse(text); }
            catch (System.Text.Json.JsonException) { }
            if (res.IsSuccessStatusCode && node is JsonObject ok) return ok;

            // OAuth estándar: {"error":"invalid_grant"}; a veces {"error":{"code":…}}.
            var error = node?["error"];
            var code = error is JsonValue ? error.GetValue<string>() : error?["code"]?.GetValue<string>();
            throw new TokenException(code, "ChatGPT no aceptó el inicio de sesión. Inténtalo de nuevo.");
        }
    }

    /// <summary>Firma (RS256 con el JWKS de OpenAI), emisor, audiencia, vencimiento y nonce.</summary>
    internal static async Task<JsonObject> ValidateIdTokenAsync(HttpClient http, string idToken, string clientId,
        string nonce, CancellationToken ct)
    {
        var bad = new AiException("No pudimos verificar tu cuenta de ChatGPT. Inténtalo de nuevo.");
        var parts = idToken.Split('.');
        if (parts.Length != 3) throw bad;
        var header = JsonNode.Parse(FromBase64Url(parts[0]));
        var claims = JsonNode.Parse(FromBase64Url(parts[1])) as JsonObject ?? throw bad;
        if (header?["alg"]?.GetValue<string>() != "RS256") throw bad;
        var kid = header["kid"]?.GetValue<string>();

        var jwks = JsonNode.Parse(await http.GetStringAsync(JwksUrl, ct).ConfigureAwait(false))?["keys"]?.AsArray();
        var key = jwks?.FirstOrDefault(k => k?["kty"]?.GetValue<string>() == "RSA"
                                            && (kid is null || k["kid"]?.GetValue<string>() == kid)) ?? throw bad;
        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = FromBase64Url(key["n"]!.GetValue<string>()),
            Exponent = FromBase64Url(key["e"]!.GetValue<string>()),
        });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), FromBase64Url(parts[2]),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw bad;

        var aud = claims["aud"];
        var audOk = aud is JsonArray list ? list.Any(a => a?.GetValue<string>() == clientId) : aud?.GetValue<string>() == clientId;
        var exp = claims["exp"]?.GetValue<long>() ?? 0;
        if (claims["iss"]?.GetValue<string>() != Issuer || !audOk
            || DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow.AddMinutes(-1)
            || claims["nonce"]?.GetValue<string>() != nonce || claims["sub"] is null)
            throw bad;
        return claims;
    }

    private static TcpListener StartListener()
    {
        foreach (var port in new[] { PreferredPort, 0 })
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return listener;
            }
            catch (SocketException) when (port != 0)
            {
            }
        }
        throw new AiException("No se pudo esperar la respuesta de ChatGPT en este equipo.");
    }

    /// <summary>
    /// Atiende 127.0.0.1 hasta recibir <c>/auth/callback</c> (el navegador también pide /favicon.ico).
    /// Una conexión que no envía nada (Chrome abre sockets de reserva) se descarta tras <paramref name="idle"/>
    /// para no bloquear la del callback, y se leen las cabeceras completas antes de responder: cerrar con
    /// datos sin leer manda un RST y el navegador mostraría «conexión restablecida».
    /// </summary>
    internal static async Task<System.Collections.Specialized.NameValueCollection> WaitForCallbackAsync(
        TcpListener listener, CancellationToken ct, TimeSpan? idle = null)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var read = CancellationTokenSource.CreateLinkedTokenSource(ct);
            read.CancelAfter(idle ?? TimeSpan.FromSeconds(3));
            string requestLine;
            try
            {
                requestLine = await reader.ReadLineAsync(read.Token).ConfigureAwait(false) ?? "";
                while (await reader.ReadLineAsync(read.Token).ConfigureAwait(false) is { Length: > 0 }) { }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException && !ct.IsCancellationRequested)
            {
                continue; // conexión vacía o cortada: se espera la siguiente
            }
            var target = requestLine.Split(' ') is { Length: >= 2 } p ? p[1] : "";
            var q = target.IndexOf('?');
            var isCallback = (q < 0 ? target : target[..q]) == "/auth/callback";
            var query = HttpUtility.ParseQueryString(q < 0 ? "" : target[(q + 1)..]);
            var html = isCallback ? CallbackPage.Html("ChatGPT", query["code"], query["error"]) : "";
            var bytes = Encoding.UTF8.GetBytes(html);
            var head = $"HTTP/1.1 {(isCallback ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            }
            catch (IOException) when (!isCallback)
            {
                continue;
            }
            catch (IOException)
            {
                // El navegador cerró antes de leer la página; el código ya llegó.
            }
            if (isCallback) return query;
        }
    }

    internal static string Base64Url(byte[] b) => OpenRouterLogin.Base64Url(b);

    internal static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
