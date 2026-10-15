namespace MeetAgent.Core.Ports;

/// <summary>Crea las dos fuentes de la plataforma: micrófono por defecto y loopback de la salida por defecto.</summary>
public interface IAudioSourceFactory
{
    IAudioSource CreateMicrophone();
    IAudioSource CreateLoopback();
}
