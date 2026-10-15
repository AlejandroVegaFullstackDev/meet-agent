using MeetAgent.Core;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Storage.Sqlite.Tests;

public class SqliteTranscriptStoreTests
{
    private static SqliteTranscriptStore NewStore() => new("Data Source=:memory:");
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-5));

    private static TranscriptSegment S(Speaker sp, double start, string text) =>
        new(sp, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 3), text);

    [Fact]
    public void Guarda_y_lee_una_reunion_con_sus_segmentos_en_orden()
    {
        using var store = NewStore();
        var id = store.StartMeeting("Planeación", T0, "grabar");
        store.AddSegment(id, S(Speaker.Otros, 4, "segundo"));
        store.AddSegment(id, S(Speaker.Yo, 0, "primero"));
        store.EndMeeting(id, T0.AddMinutes(30));

        var m = store.GetMeeting(id)!;
        Assert.Equal("Planeación", m.Title);
        Assert.Equal("grabar", m.Consent);
        Assert.Equal(T0.AddMinutes(30), m.EndedAt);
        var segs = store.GetSegments(id);
        Assert.Equal(new[] { "primero", "segundo" }, segs.Select(s => s.Text));
        Assert.Equal(Speaker.Yo, segs[0].Speaker);
        Assert.Equal(TimeSpan.FromSeconds(3), segs[0].End);
    }

    [Fact]
    public void Busqueda_de_texto_completo_ignora_tildes_y_mayusculas()
    {
        using var store = NewStore();
        var a = store.StartMeeting("Lunes", T0, "transcribir");
        store.AddSegment(a, S(Speaker.Otros, 10, "Revisamos la planeación del trimestre"));
        store.AddSegment(a, S(Speaker.Yo, 20, "El presupuesto está aprobado"));
        var b = store.StartMeeting("Martes", T0.AddDays(1), "transcribir");
        store.AddSegment(b, S(Speaker.Otros, 5, "Sin novedades en planeacion"));

        var hits = store.Search("PLANEACION");
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.MeetingTitle == "Lunes" && h.Segment.Start == TimeSpan.FromSeconds(10));
        Assert.All(hits, h => Assert.Contains("[", h.Snippet));      // resalta la coincidencia
    }

    [Fact]
    public void Busqueda_con_prefijo_y_frase()
    {
        using var store = NewStore();
        var a = store.StartMeeting("x", T0, "transcribir");
        store.AddSegment(a, S(Speaker.Yo, 0, "el presupuesto está aprobado"));
        Assert.Single(store.Search("presu*"));
        Assert.Single(store.Search("\"está aprobado\""));
        Assert.Empty(store.Search("\"aprobado está\""));
    }

    [Fact]
    public void Una_consulta_con_simbolos_raros_no_revienta()
    {
        using var store = NewStore();
        store.StartMeeting("x", T0, "transcribir");
        Assert.Empty(store.Search("hola) OR (\"*"));
    }

    [Fact]
    public void Lista_reuniones_de_la_mas_reciente_a_la_mas_antigua()
    {
        using var store = NewStore();
        store.StartMeeting("vieja", T0, "transcribir");
        store.StartMeeting("nueva", T0.AddDays(2), "transcribir");
        Assert.Equal(new[] { "nueva", "vieja" }, store.ListMeetings().Select(m => m.Title));
    }

    [Fact]
    public void Persiste_en_archivo_entre_aperturas()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meetagent-test-{Guid.NewGuid():N}.db");
        try
        {
            long id;
            using (var s1 = new SqliteTranscriptStore($"Data Source={path}"))
            {
                id = s1.StartMeeting("persistente", T0, "transcribir");
                s1.AddSegment(id, S(Speaker.Yo, 1, "hola"));
            }
            using var s2 = new SqliteTranscriptStore($"Data Source={path}");
            Assert.Equal("hola", Assert.Single(s2.GetSegments(id)).Text);
            Assert.Single(s2.Search("hola"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
