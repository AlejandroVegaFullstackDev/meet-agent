using MeetAgent.Core;
using MeetAgent.Core.Analysis;
using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Tests;

public class MeetingAnalysisTests
{
    private static TranscriptSegment Seg(string who, double start, double end, string text) =>
        new(Speaker.Otros, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text, who);

    private static readonly Meeting Meeting = new(1, "Planificación", new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), null, "subtitulos");

    [Fact]
    public void Stats_cuentan_tiempo_palabras_e_intervenciones_por_persona()
    {
        var st = MeetingStats.From([
            Seg("Ana", 0, 10, "hola a todos"),
            Seg("Ana", 10, 20, "empecemos ya"),
            Seg("Luis", 20, 25, "de acuerdo"),
            Seg("Ana", 25, 30, "bien"),
        ]);

        Assert.Equal(TimeSpan.FromSeconds(30), st.Duration);
        Assert.Equal(8, st.Words);
        Assert.Equal(3, st.Turns);
        var ana = st.Speakers[0];
        Assert.Equal("Ana", ana.Speaker);
        Assert.Equal(TimeSpan.FromSeconds(25), ana.TalkTime);
        Assert.Equal(2, ana.Turns);
        Assert.Equal(TimeSpan.FromSeconds(20), ana.LongestTurn);
        Assert.Equal(25.0 / 30, ana.Share, 3);
        Assert.Equal("Luis", st.Speakers[1].Speaker);
        Assert.Contains("| Ana | 00:00:25 | 83 |", st.ToMarkdown());
    }

    [Fact]
    public void Stats_de_reunion_vacia_no_fallan()
    {
        var st = MeetingStats.From([]);
        Assert.Equal(0, st.Words);
        Assert.Empty(st.Speakers);
    }

    [Fact]
    public void Parse_acepta_json_envuelto_y_campos_faltantes()
    {
        var text = """
            Aquí está:
            ```json
            {"resumen": " Se aprobó el plan. ", "decisiones": ["Lanzar el lunes", ""],
             "tareas": [{"tarea": "Enviar acta", "responsable": "Ana", "fecha": null}, {"tarea": " "}]}
            ```
            """;
        var i = MeetingInsights.Parse(text);
        Assert.Equal("Se aprobó el plan.", i.Resumen);
        Assert.Equal(["Lanzar el lunes"], i.Decisiones);
        var t = Assert.Single(i.Tareas);
        Assert.Equal(new ActionItem("Enviar acta", "Ana", null), t);
        Assert.Empty(i.Temas);
        Assert.Null(i.Tono);
    }

    [Fact]
    public void Parse_sin_json_lanza_FormatException() =>
        Assert.Throws<FormatException>(() => MeetingInsights.Parse("no sé"));

    [Fact]
    public void ToSections_separa_texto_y_vinetas_y_omite_lo_vacio()
    {
        var i = new MeetingInsights("Resumen corto.", ["D1"], [new ActionItem("T1", "Luis", "viernes")],
            [], [], ["Retraso"], "Tranquilo");

        var sections = i.ToSections();

        Assert.Equal(["Resumen", "Decisiones", "Tareas", "Riesgos", "Tono"], sections.Select(s => s.Title));
        Assert.Equal("Resumen corto.", sections[0].Text);
        Assert.True(sections[0].HasText);
        Assert.False(sections[0].HasItems);
        Assert.Equal(["D1"], sections[1].Items);
        Assert.Equal(["T1 — Luis (viernes)"], sections[2].Items);
        Assert.False(sections[2].HasText);
        Assert.DoesNotContain(sections, s => s.Title is "Temas" or "Preguntas abiertas");
    }

    [Fact]
    public void Json_ida_y_vuelta_y_markdown()
    {
        var i = new MeetingInsights("Resumen corto.", ["D1"], [new ActionItem("T1", "Luis", "viernes")], ["Presupuesto"], [], ["Retraso"], "Tranquilo");
        var back = MeetingInsights.Parse(i.ToJson());
        Assert.Equal(i.Resumen, back.Resumen);
        Assert.Equal(i.Tareas, back.Tareas);
        Assert.Contains("\"preguntas_abiertas\"", i.ToJson());
        var md = i.ToMarkdown();
        Assert.Contains("- [ ] T1 — Luis (viernes)", md);
        Assert.DoesNotContain("Preguntas abiertas", md);
        Assert.Contains("DECISIONES", i.ToPlainText());
    }

    private sealed class FakeChat(params string[] answers) : IChatModel
    {
        private int _i;
        public List<(string System, string User, string? Schema)> Calls { get; } = new();
        public string Name => "falsa";

        public Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct)
        {
            Calls.Add((system, user, jsonSchema));
            return Task.FromResult(answers[Math.Min(_i++, answers.Length - 1)]);
        }
    }

    private const string Answer = """{"resumen":"ok","decisiones":["A"],"tareas":[],"temas":[],"preguntas_abiertas":[],"riesgos":[],"tono":null}""";

    [Fact]
    public async Task Analizador_manda_estadisticas_y_transcripcion_en_una_llamada()
    {
        var chat = new FakeChat(Answer);
        var r = await new MeetingAnalyzer(chat).AnalyzeAsync(Meeting, [Seg("Ana", 0, 5, "lanzamos el lunes")], CancellationToken.None);

        Assert.Equal(["A"], r.Decisiones);
        var call = Assert.Single(chat.Calls);
        Assert.Equal(MeetingAnalyzer.Schema, call.Schema);
        Assert.Contains("[00:00:00] Ana: lanzamos el lunes", call.User);
        Assert.Contains("| Ana |", call.User);
        Assert.Contains("no inventes", call.System);
    }

    [Fact]
    public async Task Analizador_divide_transcripciones_largas_en_tramos()
    {
        var segs = Enumerable.Range(0, 10).Select(i => Seg("Ana", i * 10, i * 10 + 9, new string('x', 50))).ToList();
        var chat = new FakeChat("notas", "notas", "notas", "notas", "notas", Answer);

        await new MeetingAnalyzer(chat, maxCharsPerCall: 200).AnalyzeAsync(Meeting, segs, CancellationToken.None);

        Assert.True(chat.Calls.Count > 2);
        Assert.All(chat.Calls.SkipLast(1), c => Assert.Null(c.Schema));
        Assert.Equal(MeetingAnalyzer.Schema, chat.Calls[^1].Schema);
        Assert.Contains("Notas por tramos", chat.Calls[^1].User);
    }

    [Fact]
    public async Task Analizador_sin_texto_avisa() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MeetingAnalyzer(new FakeChat(Answer)).AnalyzeAsync(Meeting, [], CancellationToken.None));

    [Fact]
    public void Split_no_corta_lineas() =>
        Assert.Equal(["aaa\nbbb\n", "ccc\n"], MeetingAnalyzer.Split(["aaa", "bbb", "ccc"], 9));
}
