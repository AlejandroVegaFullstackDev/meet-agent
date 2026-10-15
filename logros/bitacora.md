# Bitácora

## 2026-10-15
- **Logrado:** H0. Entorno anotado, .NET 10 verificado, dos modelos Whisper descargados, solución hexagonal creada en `app/`.
- **Logrado:** Core con 38 pruebas en verde (troceo, WER, alineación, RMS, VAD por energía, WAV, conversión de PCM). Adaptadores de audio para Linux (probado) y Windows (compila). `capture` funciona; el loopback queda pendiente de prueba con sonido.
- **Sigue:** H2 `live` y Desktop con transcriptor falso.
