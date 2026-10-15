namespace MeetAgent.Core;

/// <summary>Respuesta del usuario al diálogo de consentimiento. Sin un sí explícito no se escribe audio a disco.</summary>
public enum Consent
{
    /// <summary>Nada: no se captura ni se transcribe.</summary>
    None,
    /// <summary>Se transcribe en memoria; el audio nunca toca el disco.</summary>
    TranscribeOnly,
    /// <summary>Se guarda el audio (WAV por canal) y se transcribe.</summary>
    RecordAndTranscribe,
}

public static class ConsentExtensions
{
    public static string ToStoreValue(this Consent c) => c switch
    {
        Consent.RecordAndTranscribe => "grabar",
        Consent.TranscribeOnly => "transcribir",
        _ => "nada",
    };
}
