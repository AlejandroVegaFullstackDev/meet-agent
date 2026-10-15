using MeetAgent.Core;
using MeetAgent.Core.Ports;

namespace MeetAgent.Meet.Vexa;

/// <summary>
/// Una reunión de Google Meet escuchada por el bot de Vexa: lo invita, consulta la transcripción
/// cada pocos segundos, guarda en el store cada segmento confirmado (con el nombre del participante)
/// y cierra la reunión cuando Vexa la da por terminada o al llamar <see cref="StopAsync"/>.
/// No depende del micrófono ni de los parlantes del equipo.
/// </summary>
public sealed class BotMeetingSession(IVexaApi api, ITranscriptStore store, string botName = "MeetAgent",
    string language = "es", string? selfName = null, TimeSpan? pollInterval = null)
{
    private readonly TimeSpan _poll = pollInterval ?? TimeSpan.FromSeconds(3);
    private CancellationTokenSource? _cts;
    private Task _completion = Task.CompletedTask;
    private string? _code;
    private long _meetingId;

    public event Action<TranscriptSegment>? SegmentReceived;
    public event Action<string>? Log;

    public bool IsRunning => !_completion.IsCompleted;
    public Task Completion => _completion;

    /// <returns>Id de la reunión en el store.</returns>
    public async Task<long> StartAsync(string meetLinkOrCode, string title, CancellationToken ct)
    {
        if (IsRunning) throw new InvalidOperationException("Ya hay un bot en una reunión.");
        if (!MeetLink.TryParse(meetLinkOrCode, out var code))
            throw new ArgumentException($"«{meetLinkOrCode}» no es un link de Google Meet (meet.google.com/abc-defg-hij).");

        await api.StartBotAsync(code, botName, language, ct);
        _code = code;
        var startedAt = DateTimeOffset.Now;
        _meetingId = store.StartMeeting(title, startedAt, "bot-transcribir");
        Log?.Invoke($"Bot «{botName}» pedido para {MeetLink.Url(code)}");
        _cts = new CancellationTokenSource();
        var sync = new TranscriptSync(selfName, startedAt);
        _completion = Task.Run(() => LoopAsync(code, sync, _cts.Token), CancellationToken.None);
        return _meetingId;
    }

    private async Task LoopAsync(string code, TranscriptSync sync, CancellationToken ct)
    {
        string? lastStatus = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                VexaTranscript t;
                try { t = await api.GetTranscriptAsync(code, ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log?.Invoke($"No se pudo leer la transcripción: {ex.Message}"); await Delay(ct); continue; }

                Save(sync.Take(t.Segments));
                if (t.Status != lastStatus && t.Status is not null)
                {
                    lastStatus = t.Status;
                    Log?.Invoke(Describe(t.Status));
                }
                if (t.Status is "completed" or "failed") break;
                await Delay(ct);
            }
            // Al parar a mano: recoger lo que el bot alcanzó a confirmar.
            if (ct.IsCancellationRequested)
                try { Save(sync.Take((await api.GetTranscriptAsync(code, CancellationToken.None)).Segments)); } catch { }
        }
        finally
        {
            store.EndMeeting(_meetingId, DateTimeOffset.Now);
        }
    }

    private void Save(IReadOnlyList<TranscriptSegment> segments)
    {
        foreach (var s in segments)
        {
            store.AddSegment(_meetingId, s);
            SegmentReceived?.Invoke(s);
        }
    }

    private async Task Delay(CancellationToken ct)
    {
        try { await Task.Delay(_poll, ct); } catch (OperationCanceledException) { }
    }

    /// <summary>Saca al bot del Meet y espera a que se guarde lo último.</summary>
    public async Task StopAsync()
    {
        if (_code is null || !IsRunning) return;
        try { await api.StopBotAsync(_code, CancellationToken.None); }
        catch (Exception ex) { Log?.Invoke($"No se pudo sacar al bot: {ex.Message}"); }
        _cts?.Cancel();
        await _completion;
    }

    private string Describe(string status) => status switch
    {
        "requested" or "joining" => $"El bot «{botName}» está entrando al Meet…",
        "awaiting_admission" => $"Esperando a que el anfitrión lo admita: acepta la solicitud de «{botName}» en el Meet.",
        "active" => $"«{botName}» está en la reunión escuchando.",
        "stopping" => "Sacando al bot de la reunión…",
        "completed" => "La reunión terminó: el bot salió y la transcripción quedó guardada.",
        "failed" => "El bot no pudo entrar o se cayó (revisa el link y que lo hayan admitido).",
        _ => $"Estado del bot: {status}",
    };
}
