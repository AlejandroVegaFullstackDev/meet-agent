# ADR-0007: App de escritorio con Avalonia sobre el mismo Core
**Estado:** aceptada · 2026-10-15
**Decisión:** `MeetAgent.Desktop` con Avalonia 12.1.4 + CommunityToolkit.Mvvm 8.4 (net10.0; el pedido decía .NET 8, pero el SDK presente es el 10 y Avalonia 12 lo soporta). La lógica de una reunión vive en el Core (`MeetingSession`), no en la UI: Desktop y CLI comparten consentimiento, pipeline, guardado y grabación.
- Bandeja (`TrayIcon` + `NativeMenu`): Empezar/Terminar, Mostrar ventana, Salir. Cerrar la ventana la esconde; la reunión sigue.
- Consentimiento antes de cada reunión: **Grabar y transcribir / Solo transcribir / Nada**. Esc o cerrar el diálogo equivalen a «Nada». Sin «Grabar» no se abre ningún `IRecordingSink`: el audio solo vive en memoria. Lo verifican pruebas en Core y en Desktop.
- Vista en vivo `[hh:mm:ss] [Yo|Otros] texto` alimentada por `ITranscriber`: Whisper small si el modelo existe; `--fake` (o la falta del modelo) usa `FakeTranscriber`.
**Pruebas:** Avalonia.Headless.XUnit (xUnit v3) para el ViewModel, el diálogo y capturas renderizadas con Skia (`docs/img/`).
**Riesgo:** la bandeja en GNOME necesita la extensión AppIndicator (viene en Ubuntu). En Windows falta probarlo.
