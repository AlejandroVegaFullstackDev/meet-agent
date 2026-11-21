using System.Text.Json;
using System.Text.Json.Serialization;
using MeetAgent.Core;

namespace MeetAgent.Ai;

/// <summary>Tokens de «Continuar con ChatGPT» para una cuenta. El plan solo se usa con <see cref="CanUsePlan"/>.</summary>
public sealed record ChatGptCredentials(
    string ClientId,
    string Subject,
    string? Email,
    string IdToken,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes)
{
    public const string PlanScope = "chatgpt.tokens.use.direct";
    public bool CanUsePlan => Scopes.Contains(PlanScope);
}

/// <summary>
/// Guarda la sesión de ChatGPT en <c>data/chatgpt.json</c>. El id del equipo y el client_id emitido se
/// conservan al cerrar sesión (OpenAI pide reutilizarlos); los tokens van cifrados como las API keys.
/// </summary>
public sealed class ChatGptSessionStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static ChatGptSessionStore? _default;

    /// <summary>La sesión de este equipo.</summary>
    public static ChatGptSessionStore Default => _default ??= new(Path.Combine(AppPaths.Data, "chatgpt.json"));

    private sealed record Stored(string HostId, string? ClientId = null, string? Subject = null, string? Email = null,
        string? Tokens = null);

    private sealed record Tokens(string IdToken, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
        IReadOnlyList<string> Scopes);

    /// <summary>Identificador opaco de este equipo (<c>urn:uuid:…</c>); se crea una vez y no cambia.</summary>
    public string HostId
    {
        get
        {
            var stored = Read();
            if (stored is not null) return stored.HostId;
            var created = new Stored($"urn:uuid:{Guid.NewGuid()}");
            Write(created);
            return created.HostId;
        }
    }

    /// <summary>client_id emitido en el primer registro (<c>oaiapp_…</c>), si existe.</summary>
    public string? ClientId => Read()?.ClientId;

    public bool HasSession => Load() is { CanUsePlan: true };

    public ChatGptCredentials? Load()
    {
        var stored = Read();
        if (stored?.Tokens is null || stored.ClientId is null || stored.Subject is null) return null;
        try
        {
            var json = AiSettingsStore.Unprotect(stored.Tokens);
            if (json is null) return null;
            var t = JsonSerializer.Deserialize<Tokens>(json, Json);
            return t is null ? null : new ChatGptCredentials(stored.ClientId, stored.Subject, stored.Email,
                t.IdToken, t.AccessToken, t.RefreshToken, t.ExpiresAt, t.Scopes);
        }
        catch (Exception ex) when (ex is JsonException or FormatException
                                       or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Reemplaza juntos tokens, vencimiento y permisos (así lo pide OpenAI tras cada refresco).</summary>
    public void Save(ChatGptCredentials c)
    {
        var tokens = JsonSerializer.Serialize(new Tokens(c.IdToken, c.AccessToken, c.RefreshToken, c.ExpiresAt, c.Scopes), Json);
        Write(new Stored(HostId, c.ClientId, c.Subject, c.Email, AiSettingsStore.Protect(tokens)));
    }

    /// <summary>Borra los tokens; conserva el equipo, el client_id y la cuenta para volver a entrar.</summary>
    public void SignOut()
    {
        if (Read() is { } stored) Write(stored with { Tokens = null });
    }

    private Stored? Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Escritura atómica y, fuera de Windows, solo para el usuario (600) desde que se crea.</summary>
    private void Write(Stored stored) => PrivateFile.WriteAllText(path, JsonSerializer.Serialize(stored, Json));
}
