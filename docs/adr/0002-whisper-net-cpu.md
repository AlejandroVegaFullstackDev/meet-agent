# ADR-0002: Whisper.net con runtime CPU, modelos ggml cuantizados
**Estado:** aceptada · 2026-10-15
**Decisión:** Whisper.net 1.9.1 + Whisper.net.Runtime (CPU). Modelos `small-q5_1` y `medium-q5_0` de huggingface.co/ggerganov/whisper.cpp. `language=es`, sin contexto previo (`WithNoContext`), hilos = núcleos lógicos / 2.
**Consecuencias:** todo local, sin red al transcribir. Una única instancia de modelo para los dos canales.
