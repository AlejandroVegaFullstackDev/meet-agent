# ADR-0004: Captura en Linux con parec y @DEFAULT_MONITOR@
**Estado:** aceptada · 2026-10-15
**Decisión:** procesos `parec --format=s16le --rate=16000 --channels=1 --raw`, uno con `--device=@DEFAULT_SOURCE@` (mic) y otro con `--device=@DEFAULT_MONITOR@` (loopback). PipeWire/pulse remuestrea a 16 kHz mono y sigue al dispositivo por defecto cuando cambia (parlantes ↔ audífonos). Se lee PCM por stdout.
**Alternativa:** `pw-record --target`; se descartó como predeterminado porque no acepta alias de "monitor por defecto" de forma tan directa.
