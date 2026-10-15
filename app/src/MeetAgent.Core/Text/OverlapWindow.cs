using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Text;

/// <summary>
/// Alineación por tiempo de chunks solapados: el límite entre dos chunks es la mitad del solape.
/// Cada palabra se asigna al chunk que contiene su punto medio dentro de su ventana [inicio, fin),
/// así ninguna sale dos veces y se descartan las palabras mal cortadas en el borde del audio.
/// </summary>
public sealed class OverlapWindow(double chunkSeconds = 5, double overlapSeconds = 1)
{
    public IReadOnlyList<RecognizedText> Keep(IEnumerable<RecognizedText> words, bool isFirstChunk, double chunkDurationSeconds)
    {
        double from = isFirstChunk ? double.NegativeInfinity : overlapSeconds / 2;
        // Un chunk más corto que el nominal es el resto final: no hay chunk siguiente que tome su cola.
        double to = chunkDurationSeconds < chunkSeconds - 1e-6 ? double.PositiveInfinity : chunkSeconds - overlapSeconds / 2;
        return words.Where(w =>
        {
            var mid = (w.Start + w.End).TotalSeconds / 2;
            return mid >= from && mid < to;
        }).ToList();
    }
}
