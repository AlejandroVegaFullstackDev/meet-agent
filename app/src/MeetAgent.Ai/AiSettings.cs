using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetAgent.Ai;

/// <param name="BaseUrl">Solo si se cambia la del proveedor (obligatoria en «Personalizado»).</param>
public sealed record Connection(AiProvider Provider, string Model, string? ApiKey = null, string? BaseUrl = null)
{
    public ProviderInfo Info => Providers.Get(Provider);
    public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? Info.BaseUrl ?? "" : BaseUrl.Trim().TrimEnd('/');

    /// <summary>Para las cuentas por CLI, usable = el ejecutable está instalado; el resto pide modelo, key y URL.</summary>
    public bool IsUsable => Info.IsAccount
        ? AccountDetector.IsInstalled(Provider)
        : !string.IsNullOrWhiteSpace(Model) && (!Info.NeedsKey || !string.IsNullOrWhiteSpace(ApiKey))
          && (Provider == AiProvider.Anthropic || EffectiveBaseUrl.Length > 0);
}

/// <param name="Chat">La IA que analiza las reuniones.</param>
/// <param name="Transcription">Quién transcribe el audio de la app (OpenAI, Groq o un servidor propio). Null: la app no transcribe.</param>
/// <param name="AnalyzeWhenMeetingEnds">Analizar sola cada reunión de la app al terminar.</param>
public sealed record AiSettings(Connection? Chat = null, Connection? Transcription = null, bool AnalyzeWhenMeetingEnds = true);

/// <summary>
/// Guarda <see cref="AiSettings"/> en <c>data/ia.json</c>. Las API keys van cifradas con DPAPI en Windows
/// (solo tu usuario de Windows puede leerlas); en Linux y macOS el archivo queda con permisos 600.
/// </summary>
public sealed class AiSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path => path;

    public AiSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new AiSettings();
            var s = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), Json) ?? new AiSettings();
            return s with { Chat = Reveal(s.Chat), Transcription = Reveal(s.Transcription) };
        }
        catch (Exception ex) when (ex is JsonException or IOException or CryptographicException)
        {
            return new AiSettings();
        }
    }

    public void Save(AiSettings s)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var stored = s with { Chat = Hide(s.Chat), Transcription = Hide(s.Transcription) };
        File.WriteAllText(path, JsonSerializer.Serialize(stored, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static Connection? Hide(Connection? c) => c is null ? null : c with { ApiKey = Protect(c.ApiKey) };
    private static Connection? Reveal(Connection? c) => c is null ? null : c with { ApiKey = Unprotect(c.ApiKey) };

    internal static string? Protect(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!OperatingSystem.IsWindows()) return "plain:" + key;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(bytes);
    }

    internal static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        if (stored.StartsWith("plain:")) return stored["plain:".Length..];
        if (stored.StartsWith("dpapi:") && OperatingSystem.IsWindows())
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored["dpapi:".Length..]), null, DataProtectionScope.CurrentUser));
        return null;
    }
}
