# ADR-0005: Captura en Windows con WasapiCapture + WasapiLoopbackCapture (NAudio 3.1)
**Estado:** aceptada · 2026-10-15
**Decisión:** micrófono con `WasapiCapture` y salida con `WasapiLoopbackCapture` sobre el dispositivo por defecto (rol Console). `MMDeviceEnumerator.CreateNotificationClient` (la envoltura de NAudio 3 sobre `IMMNotificationClient`) avisa de `DefaultDeviceChanged`, y la captura se cierra y se reabre sobre el nuevo dispositivo. La conversión a 16 kHz mono 16 bits se hace en el Core (`PcmConverter`: mezcla, FIR paso bajo a 7,2 kHz y remuestreo lineal), con pruebas en Linux.
**Nota:** NAudio 3 marca esas dos clases como obsoletas a favor de `WasapiRecorder`; se usan igual porque son las del plan y su comportamiento está probado. Migrar a `WasapiRecorder` (sin copias y con loopback por proceso) queda como mejora.
**Riesgo:** compila en Linux, pero no se ha ejecutado en Windows todavía.
