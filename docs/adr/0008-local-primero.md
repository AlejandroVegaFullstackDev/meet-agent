# ADR-0008: Local primero, sin servidor ni repositorio remoto
**Estado:** aceptada · 2026-10-15
**Contexto:** no hay servidor de Roda disponible ni repositorio remoto. El proyecto arranca como trabajo propio en el portátil de Alejandro, y solo si gusta se evalúa llevarlo a la nube (Cloud Run, Lambda, S3, Workspace).
**Decisión:** todo corre y se guarda en la máquina local: modelos en `models/`, datos en `data/` (SQLite + WAV) y git solo local. Nada sale del equipo.
**Para no cerrar la puerta a la nube:** los puertos del Core ya separan lo que cambiaría:
- `ITranscriptStore` → Postgres/Firestore;
- `IRecordingSink` → S3/GCS (solo con consentimiento);
- `ITranscriber` → Whisper en un contenedor de Cloud Run con GPU o CPU.

La captura (`IAudioSource`) se queda siempre en el cliente.
**Consecuencia:** las cifras de rendimiento son las de este portátil (Ryzen 3 7320U). En la nube, medium sería viable en vivo.
