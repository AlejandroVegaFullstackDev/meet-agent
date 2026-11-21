using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using MeetAgent.Ai;

namespace MeetAgent.Ai.Tests;

public class ChatGptTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("meetagent-chatgpt-").FullName;
    private readonly RSA _key = RSA.Create(2048);

    public void Dispose()
    {
        _key.Dispose();
        Directory.Delete(_dir, true);
    }

    private ChatGptSessionStore Store() => new(Path.Combine(_dir, "chatgpt.json"));

    /// <summary>Hace de auth.openai.com: JWKS, token y revocación. Guarda los formularios recibidos.</summary>
    private sealed class FakeOpenAi(Func<HttpRequestMessage, string, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.ToString(), body));
            var (status, text) = answer(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private string Jwks()
    {
        var p = _key.ExportParameters(false);
        return new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "RSA", ["kid"] = "k1", ["alg"] = "RS256",
                ["n"] = ChatGptLogin.Base64Url(p.Modulus!), ["e"] = ChatGptLogin.Base64Url(p.Exponent!),
            }),
        }.ToJsonString();
    }

    private string IdToken(string clientId, string nonce, string iss = ChatGptLogin.Issuer)
    {
        var header = ChatGptLogin.Base64Url(Encoding.UTF8.GetBytes("""{"alg":"RS256","kid":"k1","typ":"JWT"}"""));
        var claims = ChatGptLogin.Base64Url(Encoding.UTF8.GetBytes(new JsonObject
        {
            ["iss"] = iss, ["aud"] = clientId, ["sub"] = "user-1", ["email"] = "ana@example.com", ["nonce"] = nonce,
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }.ToJsonString()));
        var signature = _key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{ChatGptLogin.Base64Url(signature)}";
    }

    /// <summary>Simula el navegador: lee la URL de autorización y llama al callback en 127.0.0.1.</summary>
    private static Action<string> Browser(Action<Dictionary<string, string>> seen, string? clientId = "oaiapp_123",
        string? error = null) => url =>
    {
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        var values = q.AllKeys.ToDictionary(k => k!, k => q[k]!);
        seen(values);
        var callback = $"{values["redirect_uri"]}?state={values["state"]}"
                       + (error is null ? "&code=abc" : $"&error={error}")
                       + (clientId is null ? "" : $"&client_id={clientId}");
        _ = Task.Run(async () =>
        {
            using var browser = new HttpClient();
            await browser.GetAsync(callback);
        });
    };

    [Fact]
    public async Task Primer_login_registra_la_app_valida_el_token_y_guarda_la_sesion()
    {
        Dictionary<string, string> authorize = new();
        var http = new FakeOpenAi((req, body) => req.RequestUri!.AbsolutePath switch
        {
            "/.well-known/jwks.json" => (HttpStatusCode.OK, Jwks()),
            "/api/accounts/oauth/token" => (HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "at-1", ["refresh_token"] = "rt-1", ["expires_in"] = 3600,
                ["id_token"] = IdToken("oaiapp_123", authorize["nonce"]),
                ["scope"] = "chatgpt.tokens.use.direct email offline_access openid profile resource.invoke",
            }.ToJsonString()),
            _ => (HttpStatusCode.NotFound, "{}"),
        });
        var store = Store();

        var c = await ChatGptLogin.LoginAsync(new HttpClient(http), store, Browser(v => authorize = v));

        Assert.Equal("dynamic_agent_client", authorize["client_id"]);
        Assert.Equal("MeetAgent", authorize["agent_name_hint"]);
        Assert.StartsWith("urn:uuid:", authorize["ext_agent_host_id"]);
        Assert.StartsWith("http://127.0.0.1:", authorize["redirect_uri"]);
        Assert.EndsWith("/auth/callback", authorize["redirect_uri"]);
        Assert.Equal("https://api.openai.com/v1", authorize["resource"]);
        Assert.Contains("chatgpt.tokens.use.direct", authorize["scope"]);
        Assert.Equal("S256", authorize["code_challenge_method"]);

        var exchange = HttpUtility.ParseQueryString(http.Requests.Single(r => r.Url.EndsWith("/oauth/token")).Body);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("oaiapp_123", exchange["client_id"]);
        Assert.Equal(authorize["redirect_uri"], exchange["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(exchange["code_verifier"]));

        Assert.True(c.CanUsePlan);
        Assert.Equal("oaiapp_123", store.ClientId);
        Assert.Equal("at-1", store.Load()!.AccessToken);
        Assert.True(store.HasSession);
        Assert.DoesNotContain("at-1", await File.ReadAllTextAsync(Path.Combine(_dir, "chatgpt.json")));
    }

    [Fact]
    public async Task Un_id_token_de_otro_emisor_se_rechaza_y_no_guarda_nada()
    {
        Dictionary<string, string> authorize = new();
        var http = new FakeOpenAi((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/.well-known/jwks.json" => (HttpStatusCode.OK, Jwks()),
            _ => (HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "at", ["refresh_token"] = "rt", ["scope"] = "chatgpt.tokens.use.direct",
                ["id_token"] = IdToken("oaiapp_123", authorize["nonce"], iss: "https://malo.example"),
            }.ToJsonString()),
        });
        var store = Store();

        await Assert.ThrowsAsync<AiException>(() =>
            ChatGptLogin.LoginAsync(new HttpClient(http), store, Browser(v => authorize = v)));

        Assert.False(store.HasSession);
    }

    [Fact]
    public async Task Si_el_usuario_cancela_no_se_cambia_ningun_codigo()
    {
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.OK, "{}"));

        var ex = await Assert.ThrowsAsync<AiException>(() =>
            ChatGptLogin.LoginAsync(new HttpClient(http), Store(), Browser(_ => { }, error: "access_denied")));

        Assert.Contains("Cancelaste", ex.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Volver_a_entrar_reutiliza_el_client_id_y_el_equipo()
    {
        var store = Store();
        var host = store.HostId;
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", "ana@example.com", "id", "at", "rt",
            DateTimeOffset.UtcNow.AddHours(1), ["chatgpt.tokens.use.direct"]));
        store.SignOut();
        Assert.False(store.HasSession);
        Assert.Equal("oaiapp_123", store.ClientId);
        Assert.Equal(host, store.HostId);

        Dictionary<string, string> authorize = new();
        var http = new FakeOpenAi((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/.well-known/jwks.json" => (HttpStatusCode.OK, Jwks()),
            _ => (HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = "at-2", ["refresh_token"] = "rt-2", ["scope"] = "chatgpt.tokens.use.direct",
                ["id_token"] = IdToken("oaiapp_123", authorize["nonce"]),
            }.ToJsonString()),
        });

        await ChatGptLogin.LoginAsync(new HttpClient(http), store, Browser(v => authorize = v, clientId: null));

        Assert.Equal("oaiapp_123", authorize["client_id"]);
        Assert.False(authorize.ContainsKey("agent_name_hint"));
        Assert.Equal(host, authorize["ext_agent_host_id"]);
        Assert.Equal("at-2", store.Load()!.AccessToken);
    }

    [Fact]
    public async Task El_token_vencido_se_refresca_y_guarda_el_refresh_nuevo()
    {
        var store = Store();
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", null, "id", "viejo", "rt-1",
            DateTimeOffset.UtcNow.AddMinutes(-1), ["chatgpt.tokens.use.direct"]));
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.OK,
            """{"access_token":"nuevo","refresh_token":"rt-2","expires_in":3600,"scope":"chatgpt.tokens.use.direct"}"""));

        var token = await ChatGptLogin.AccessTokenAsync(new HttpClient(http), store, default);

        Assert.Equal("nuevo", token);
        Assert.Equal("rt-2", store.Load()!.RefreshToken);
        var form = HttpUtility.ParseQueryString(http.Requests.Single().Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("oaiapp_123", form["client_id"]);
        Assert.Equal("rt-1", form["refresh_token"]);
    }

    [Fact]
    public async Task Un_refresh_invalido_cierra_la_sesion_y_pide_entrar_de_nuevo()
    {
        var store = Store();
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", null, "id", "viejo", "rt-1",
            DateTimeOffset.UtcNow.AddMinutes(-1), ["chatgpt.tokens.use.direct"]));
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() => ChatGptLogin.AccessTokenAsync(new HttpClient(http), store, default));

        Assert.Contains("vuelve a iniciar sesión", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(store.HasSession);
    }

    [Fact]
    public async Task La_respuesta_junta_el_stream_y_pide_store_false_y_stream_true()
    {
        var store = Store();
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", null, "id", "at", "rt",
            DateTimeOffset.UtcNow.AddHours(1), ["chatgpt.tokens.use.direct"]));
        const string sse = """
            event: response.output_text.delta
            data: {"type":"response.output_text.delta","delta":"lis"}

            data: {"type":"response.output_text.delta","delta":"to"}

            data: {"type":"response.completed","response":{}}

            """;
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.OK, sse));
        var chat = new ChatGptPlanChat(new HttpClient(http), store, "gpt-6.1-sol");

        var text = await chat.CompleteAsync("sistema", "hola", null, default);

        Assert.Equal("listo", text);
        var body = JsonNode.Parse(http.Requests.Single().Body)!;
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.Equal("sistema", body["instructions"]!.GetValue<string>());
        Assert.Equal("user", body["input"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sin_response_completed_no_se_da_por_buena()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data: {\"type\":\"response.output_text.delta\",\"delta\":\"a medias\"}\n\n"));
        await Assert.ThrowsAsync<AiException>(() => ChatGptPlanChat.ReadStreamAsync(stream, default));
    }

    [Fact]
    public async Task El_limite_del_plan_a_mitad_del_stream_dice_donde_revisarlo()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n"));
        var ex = await Assert.ThrowsAsync<AiException>(() => ChatGptPlanChat.ReadStreamAsync(stream, default));
        Assert.Contains("chatgpt.com/settings/usage", ex.Message);
    }

    [Fact]
    public void Un_plan_gratis_explica_que_hace_falta_Plus_y_ofrece_openrouter()
    {
        var text = ChatGptPlanChat.Explain(403, """{"error":{"code":"subscription_sharing_user_not_eligible","message":"x"}}""");
        Assert.Contains("Plus o Pro", text);
        Assert.Contains("OpenRouter", text);
        Assert.Contains("límite", ChatGptPlanChat.Explain(429, """{"detail":"slow down"}"""));
    }

    [Fact]
    public async Task Una_conexion_vacia_del_navegador_no_bloquea_el_callback()
    {
        // Chrome abre sockets de reserva que no envían nada; el callback llega por otro.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var idle = new System.Net.Sockets.TcpClient();
            await idle.ConnectAsync(IPAddress.Loopback, port);
            var wait = ChatGptLogin.WaitForCallbackAsync(listener, CancellationToken.None, TimeSpan.FromMilliseconds(300));

            using var browser = new HttpClient();
            var page = await browser.GetAsync($"http://127.0.0.1:{port}/auth/callback?code=abc&state=s1")
                .WaitAsync(TimeSpan.FromSeconds(10));
            var query = await wait.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("vuelve a MeetAgent", await page.Content.ReadAsStringAsync());
            Assert.Equal("abc", query["code"]);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Un_refresco_sin_scope_conserva_el_permiso_del_plan()
    {
        var store = Store();
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", null, "id", "viejo", "rt-1",
            DateTimeOffset.UtcNow.AddMinutes(-1), ["openid", "chatgpt.tokens.use.direct"]));
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.OK,
            """{"access_token":"nuevo","refresh_token":"rt-2","expires_in":3600}"""));

        await ChatGptLogin.AccessTokenAsync(new HttpClient(http), store, default);

        Assert.True(store.HasSession);
        Assert.True(store.Load()!.CanUsePlan);
    }

    [Fact]
    public async Task Con_access_denied_la_pagina_dice_que_no_se_conecto()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var wait = ChatGptLogin.WaitForCallbackAsync(listener, CancellationToken.None);
            using var browser = new HttpClient();
            var page = await (await browser.GetAsync($"http://127.0.0.1:{port}/auth/callback?error=access_denied&state=s"))
                .Content.ReadAsStringAsync();
            await wait.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("No se conectó", page);
            Assert.DoesNotContain("Listo", page);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task OpenRouter_con_error_lo_dice_en_la_pagina_y_deja_de_esperar()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var wait = OpenRouterLogin.WaitForCodeAsync(listener, CancellationToken.None);
            using var browser = new HttpClient();
            var page = await (await browser.GetAsync($"http://127.0.0.1:{port}/?error=access_denied")).Content.ReadAsStringAsync();

            var ex = await Assert.ThrowsAsync<AiException>(() => wait.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains("Cancelaste", ex.Message);
            Assert.Contains("No se conectó", page);
            Assert.DoesNotContain("Listo", page);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Cancelar_el_login_libera_el_puerto_del_callback()
    {
        using var cts = new CancellationTokenSource();
        var port = 0;
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.OK, "{}"));
        var login = ChatGptLogin.LoginAsync(new HttpClient(http), Store(), url =>
        {
            port = new Uri(HttpUtility.ParseQueryString(new Uri(url).Query)["redirect_uri"]!).Port;
            cts.CancelAfter(100);
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login.WaitAsync(TimeSpan.FromSeconds(10)));

        var again = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        again.Start(); // no lanza: el puerto quedó libre
        again.Stop();
    }

    [Fact]
    public async Task Una_sesion_guardada_se_reutiliza_refrescando_sin_abrir_el_navegador()
    {
        var store = Store();
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", null, "id", "viejo", "rt-1",
            DateTimeOffset.UtcNow.AddMinutes(-1), ["chatgpt.tokens.use.direct"]));
        var http = new FakeOpenAi((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/accounts/oauth/token" => (HttpStatusCode.OK,
                """{"access_token":"nuevo","refresh_token":"rt-2","expires_in":3600,"scope":"chatgpt.tokens.use.direct"}"""),
            "/v1/models" => (HttpStatusCode.OK, """{"models":[{"slug":"oculto","visibility":"hide"},{"slug":"gpt-6.1-sol","visibility":"list"}]}"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        });

        var model = await ChatGptLogin.ResumeAsync(new HttpClient(http), store);

        Assert.Equal("gpt-6.1-sol", model);
        Assert.Equal("nuevo", store.Load()!.AccessToken);
        Assert.DoesNotContain(http.Requests, r => r.Url.Contains("/authorize"));
    }

    [Fact]
    public async Task Una_sesion_que_OpenAI_ya_no_acepta_pasa_al_login_normal()
    {
        var store = Store();
        store.Save(new ChatGptCredentials("oaiapp_123", "user-1", null, "id", "at", "rt",
            DateTimeOffset.UtcNow.AddHours(1), ["chatgpt.tokens.use.direct"]));
        var http = new FakeOpenAi((_, _) => (HttpStatusCode.Unauthorized, """{"detail":"token revoked"}"""));

        Assert.Null(await ChatGptLogin.ResumeAsync(new HttpClient(http), store));
        Assert.False(store.HasSession);
        Assert.Equal("oaiapp_123", store.ClientId);

        Assert.Null(await ChatGptLogin.ResumeAsync(new HttpClient(http), Store()));
    }
}
