# ADR-0006: Troceo 5/1 s, audio_ctx reducido y alineación por tiempo
**Estado:** aceptada (5/1 s queda pendiente de revisar con datos reales) · 2026-10-15
**Contexto:** en este equipo, small con la ventana completa de 30 s va a RTF 1,87, que no sirve para tiempo real.
**Decisión:**
- `audio_ctx` = (chunk + 1 s) × 50 tramas: RTF 0,39, con una pérdida de WER de alrededor de 2 puntos.
- Timestamps por palabra y alineación por tiempo en la mitad del solape, más una deduplicación textual tolerante.
- 5/1 s por defecto, como pide el plan. Chunk y solape son configurables.
**Evidencia:** con 10/2 s el WER se reduce a la mitad (small 25,5 → 13,1 %; medium 15,2 → 7,6 %) con el mismo RTF y unos 5 s más de retardo (H3). **Propuesta:** decidir con la referencia real si el valor por defecto pasa a 10/2 s.
**Modelo en vivo:** small. Medium no sostiene dos canales en tiempo real en un Ryzen 3 7320U; queda para el reprocesado posterior a la reunión.
