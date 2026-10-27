using Avalonia.Headless.XUnit;
using MeetAgent.Core;
using MeetAgent.Desktop.ViewModels;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Desktop.Tests;

public class HistoryViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-5));

    private static (HistoryViewModel vm, SqliteTranscriptStore store, string dir) Make()
    {
        var store = new SqliteTranscriptStore("Data Source=:memory:");
        var local = store.StartMeeting("Planeación", T0, "transcribir");
        store.AddSegment(local, new TranscriptSegment(Speaker.Yo, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Empecemos con el presupuesto"));
        store.EndMeeting(local, T0.AddMinutes(30));
        var bot = store.StartMeeting("Meet con bot", T0.AddDays(1), "bot-transcribir");
        store.AddSegment(bot, new TranscriptSegment(Speaker.Otros, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hola equipo", "Ana Gómez"));
        var subs = store.StartMeeting("SPRINT BRIEF", T0.AddDays(2), "subtitulos");
        store.AddSegment(subs, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(70), "La tabla de concesionarios", "Juan Pérez"));
        var dir = Directory.CreateTempSubdirectory("meetagent-hist-").FullName;
        return (new HistoryViewModel(store, dir), store, dir);
    }

    [AvaloniaFact]
    public void Lista_todas_las_reuniones_con_su_origen_de_la_mas_reciente_a_la_mas_antigua()
    {
        var (vm, store, _) = Make();
        using var _s = store;
        vm.Refresh();
        Assert.Equal(["SPRINT BRIEF", "Meet con bot", "Planeación"], vm.Meetings.Select(m => m.Title));
        Assert.Equal(["Meet", "Meet", "Micrófono"], vm.Meetings.Select(m => m.Source));
        Assert.Equal("00:30:00", vm.Meetings[2].Duration);
        Assert.Equal("en curso", vm.Meetings[0].Duration);
        Assert.Equal("15/10/2026 · 09:00", vm.Meetings[2].When);
        Assert.All(vm.Meetings.Take(2), m => Assert.True(m.IsMeet));
        Assert.False(vm.Meetings[2].IsMeet);
        Assert.All(vm.Meetings, m => Assert.False(m.HasAnalysis));
    }

    [AvaloniaFact]
    public void Una_reunion_de_hoy_se_muestra_como_HOY_con_la_hora()
    {
        var store = new SqliteTranscriptStore("Data Source=:memory:");
        using var _s = store;
        var now = new DateTimeOffset(2026, 10, 27, 18, 0, 0, TimeSpan.FromHours(-5));
        store.StartMeeting("Daily de hoy", now.AddHours(-2), "grabar");
        var vm = new HistoryViewModel(store, Directory.CreateTempSubdirectory("meetagent-hoy-").FullName, now: () => now);

        vm.Refresh();

        Assert.Equal("HOY · 16:00", Assert.Single(vm.Meetings).When);
    }

    [AvaloniaFact]
    public void Elegir_una_reunion_muestra_su_transcripcion_completa_con_nombres()
    {
        var (vm, store, _) = Make();
        using var _s = store;
        vm.Refresh();
        vm.Selected = vm.Meetings[0];
        var line = Assert.Single(vm.Lines);
        Assert.Equal("00:01:05", line.Time);
        Assert.Equal("Juan Pérez", line.SpeakerLabel);
        Assert.Equal("La tabla de concesionarios", line.Text);
    }

    [AvaloniaFact]
    public void Buscar_encuentra_en_todas_y_abrir_un_resultado_selecciona_la_reunion()
    {
        var (vm, store, _) = Make();
        using var _s = store;
        vm.Refresh();
        vm.Query = "presupuesto";
        vm.SearchCommand.Execute(null);
        var hit = Assert.Single(vm.Results);
        Assert.Equal("Planeación", hit.MeetingTitle);
        vm.OpenResultCommand.Execute(hit);
        Assert.Equal("Planeación", vm.Selected!.Title);
        Assert.Equal("Empecemos con el presupuesto", Assert.Single(vm.Lines).Text);
    }

    [AvaloniaFact]
    public void Exportar_escribe_el_markdown_de_la_reunion_elegida()
    {
        var (vm, store, dir) = Make();
        using var _s = store;
        try
        {
            vm.Refresh();
            vm.Selected = vm.Meetings[1];
            vm.ExportCommand.Execute(null);
            var file = Assert.Single(Directory.GetFiles(dir, "*.md"));
            Assert.Contains("# Meet con bot", File.ReadAllText(file));
            Assert.Contains("Ana Gómez", File.ReadAllText(file));
            Assert.Equal("Reunión exportada a Markdown.", vm.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Refrescar_ve_reuniones_nuevas_escritas_por_otro_proceso()
    {
        var (vm, store, _) = Make();
        using var _s = store;
        vm.Refresh();
        store.StartMeeting("Nueva desde la extensión", T0.AddDays(3), "subtitulos");
        vm.RefreshCommand.Execute(null);
        Assert.Equal("Nueva desde la extensión", vm.Meetings[0].Title);
    }
}
