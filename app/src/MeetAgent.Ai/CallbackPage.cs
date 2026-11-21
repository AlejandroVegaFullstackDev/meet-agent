namespace MeetAgent.Ai;

/// <summary>
/// Página que ve el usuario en el navegador al volver de iniciar sesión. Solo dice «Listo» si llegó un
/// código; con <c>error</c> (p. ej. <c>access_denied</c>) dice claramente que no se conectó.
/// </summary>
internal static class CallbackPage
{
    public static string Html(string provider, string? code, string? error)
    {
        var text = error switch
        {
            null when !string.IsNullOrWhiteSpace(code) =>
                $"Listo: vuelve a MeetAgent para terminar de conectar {provider}. Puedes cerrar esta pestaña.",
            "access_denied" =>
                $"No se conectó: cancelaste el permiso en {provider}. Vuelve a MeetAgent y pulsa Reintentar si quieres conectarlo.",
            _ =>
                $"No se conectó: {provider} no completó el inicio de sesión. Vuelve a MeetAgent e inténtalo de nuevo.",
        };
        return $"<!doctype html><meta charset=utf-8><title>MeetAgent</title><p style='font:16px sans-serif'>{text}";
    }
}
