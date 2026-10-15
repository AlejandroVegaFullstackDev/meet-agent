# Arquitectura

Hexagonal: el núcleo (`MeetAgent.Core`) define puertos y lógica pura; los adaptadores viven en proyectos aparte.

```
                 ┌──────────────── MeetAgent.Core ────────────────┐
 Audio.Linux ──► │ IAudioSource ─► Chunker (5 s, 1 s solape)      │
 Audio.Windows ─►│               ─► IVad ─► cola única ─► ITranscriber ◄── Stt.Whisper
                 │ OverlapMerger ─► Segment[Yo|Otros] ─► ITranscriptStore ◄── Storage.Sqlite
                 │ Wer (bench)                                     │
                 └─────────────────────────────────────────────────┘
                 MeetingSession (consentimiento → fuentes → LiveTranscription → store [+ grabación solo con «Grabar»])
        Cli (capture/live/bench/meetings/search/export)      Desktop (Avalonia: bandeja, consentimiento, vista en vivo)
```

- Audio interno: PCM 16 kHz, mono, 16 bits (`short`); a Whisper se pasa `float` normalizado.
- Dos canales: `Yo` (micrófono) y `Otros` (loopback de la salida por defecto).
- Un solo modelo Whisper, compartido por ambos canales a través de una cola.

Decisiones: ver `docs/adr/`.
