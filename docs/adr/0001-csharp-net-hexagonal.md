# ADR-0001: C# .NET con arquitectura hexagonal
**Estado:** aceptada · 2026-10-15
**Contexto:** debe correr en Windows y Linux, y la PoC es el núcleo de la app real.
**Decisión:** C# sobre .NET (net10.0, el SDK presente; cumple ".NET 8+"). Core sin dependencias de plataforma; adaptadores por SO.
**Consecuencias:** un binario por SO con el mismo Core; los adaptadores se prueban a mano, la lógica pura con xUnit.
