using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Core;
using MeetAgent.Core.Analysis;
using MeetAgent.Core.Export;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;

namespace MeetAgent.Desktop.ViewModels;

/// <param name="Source">«Meet» o «Micrófono»: de dónde salió la transcripción.</param>
/// <param name="HasAnalysis">Si ya tiene un análisis guardado (✓ en la lista).</param>
public sealed record MeetingItem(long Id, string Title, string When, string Duration, string Source, bool HasAnalysis)
{
    public bool IsMeet => Source == "Meet";
}

/// <param name="Share">0-1, para la barra.</param>
/// <param name="BrushKey">Token del color del hablante (SpeakerYoBrush, Speaker1Brush…).</param>
public sealed record SpeakerRow(string Name, string Time, double Share, string Percent, int Words, int Turns, string BrushKey);

public sealed record SearchResultItem(long MeetingId, string MeetingTitle, string Time, string Speaker, string Snippet);

/// <summary>
/// Historial de TODAS las reuniones de la base: captura local, bot de Meet y subtítulos de la extensión.
/// Permite abrir cada transcripción completa, buscar en todas, ver estadísticas, analizarla con la IA
/// del usuario y exportar a Markdown.
/// </summary>
/// <param name="chat">La IA configurada en la pestaña IA, o null si falta configurarla.</param>
/// <param name="now">Reloj para decidir si una reunión es de hoy; se inyecta en las pruebas.</param>
public sealed partial class HistoryViewModel(ITranscriptStore store, string exportDir,
    IInsightsStore? insights = null, Func<IChatModel?>? chat = null,
    Func<DateTimeOffset>? now = null) : ObservableObject
{
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);

    public ObservableCollection<SpeakerRow> Speakers { get; } = new();
    public ObservableCollection<InsightSection> InsightBlocks { get; } = new();
    [ObservableProperty] private string? _statsSummary;
    [ObservableProperty] private string? _insightsInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoAnalysis))]
    private bool _hasInsights;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalyzeText), nameof(ShowNoAnalysis))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private bool _isAnalyzing;

    public ObservableCollection<MeetingItem> Meetings { get; } = new();
    public ObservableCollection<TranscriptLine> Lines { get; } = new();
    public ObservableCollection<SearchResultItem> Results { get; } = new();

    [ObservableProperty] private MeetingItem? _selected;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string? _message;

    /// <summary>Texto del botón de análisis, sin jerga ni el modelo conectado.</summary>
    public string AnalyzeText => IsAnalyzing ? "Analizando…" : "Analizar con IA";

    /// <summary>La reunión elegida todavía no tiene análisis y no se está analizando.</summary>
    public bool ShowNoAnalysis => Selected is not null && !HasInsights && !IsAnalyzing;

    partial void OnSelectedChanged(MeetingItem? value)
    {
        Lines.Clear();
        Speakers.Clear();
        InsightBlocks.Clear();
        StatsSummary = InsightsInfo = null;
        HasInsights = false;
        OnPropertyChanged(nameof(ShowNoAnalysis));
        ExportCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        if (value is null) return;
        var segments = store.GetSegments(value.Id);
        foreach (var s in segments)
            Lines.Add(new TranscriptLine(TimeFormat.Hms(s.Start), s.Speaker, s.Text, s.SpeakerName));
        ShowStats(MeetingStats.From(segments));
        ShowInsights(insights?.GetInsights(value.Id));
    }

    private void ShowStats(MeetingStats st)
    {
        if (st.Words == 0) return;
        StatsSummary = $"{TimeFormat.Hms(st.Duration)} · {st.Turns} intervenciones · {st.Speakers.Count} personas";
        foreach (var sp in st.Speakers)
            Speakers.Add(new SpeakerRow(sp.Speaker, TimeFormat.Hms(sp.TalkTime), sp.Share, $"{sp.Share * 100:F0} %",
                sp.Words, sp.Turns, BrushKeyFor(sp.Speaker)));
    }

    /// <summary>Color estable por persona: Yo y Otros tienen token propio; los nombres, la paleta de 6.</summary>
    private static string BrushKeyFor(string speaker)
    {
        if (speaker == nameof(Speaker.Yo)) return "SpeakerYoBrush";
        if (speaker == nameof(Speaker.Otros)) return "SpeakerOtrosBrush";
        var hash = 0;
        foreach (var c in speaker) hash = unchecked(hash * 31 + c) & 0x7FFFFFFF;
        return $"Speaker{hash % 6 + 1}Brush";
    }

    private void ShowInsights(StoredInsights? saved)
    {
        if (saved is null) return;
        try
        {
            var parsed = MeetingInsights.Parse(saved.Json);
            foreach (var section in parsed.ToSections())
                InsightBlocks.Add(new InsightSection(section.Title.ToUpperInvariant(), section.Text, section.Items));
            HasInsights = true;
            InsightsInfo = $"Analizada el {saved.CreatedAt:dd/MM/yyyy} · {saved.CreatedAt:HH:mm}";
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            InsightsInfo = "El análisis guardado no se pudo leer; vuelve a analizar.";
        }
    }

    private bool CanAnalyze() => Selected is not null && !IsAnalyzing && insights is not null;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (Selected is { } m) await AnalyzeMeetingAsync(m.Id);
    }

    /// <summary>
    /// Analiza una reunión con la IA y guarda el resultado. Lo usa el botón y también la app al terminar
    /// una reunión (si «analizar al terminar» está activo).
    /// </summary>
    public async Task AnalyzeMeetingAsync(long meetingId)
    {
        if (insights is null) return;
        var meeting = store.GetMeeting(meetingId);
        if (meeting is null) return;
        IChatModel? model;
        try { model = chat?.Invoke(); }
        catch (Exception ex) { Message = ex.Message; return; }
        if (model is null) { Message = "Conecta tu IA para analizar esta reunión."; return; }

        var segments = store.GetSegments(meetingId);
        if (segments.Count == 0) { Message = "Esta reunión no tiene texto para analizar."; return; }
        IsAnalyzing = true;
        Message = "Analizando la reunión…";
        try
        {
            var result = await new MeetingAnalyzer(model).AnalyzeAsync(meeting, segments, CancellationToken.None);
            insights.SaveInsights(meetingId, result.ToJson(), model.Name, DateTimeOffset.Now);
            MarkAnalyzed(meetingId);
            if (Selected?.Id == meetingId) ShowInsights(insights.GetInsights(meetingId));
            Message = "Análisis listo.";
        }
        catch (Exception ex)
        {
            Message = $"No se pudo analizar: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    /// <summary>Pone la ✓ en la fila sin recargar el detalle que ya está abierto.</summary>
    private void MarkAnalyzed(long meetingId)
    {
        var index = Meetings.ToList().FindIndex(m => m.Id == meetingId);
        if (index >= 0) Meetings[index] = Meetings[index] with { HasAnalysis = true };
    }

    /// <summary>Relee la base: la extensión y el bot escriben desde otros procesos.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var keep = Selected?.Id;
        Meetings.Clear();
        foreach (var m in store.ListMeetings())
            Meetings.Add(new MeetingItem(m.Id, m.Title, WhenText(m.StartedAt),
                m.EndedAt is { } e ? TimeFormat.Hms(e - m.StartedAt) : "en curso",
                SourceOf(m.Consent), insights?.GetInsights(m.Id) is not null));
        Selected = keep is null ? null : Meetings.FirstOrDefault(m => m.Id == keep);
    }

    /// <summary>«HOY · hh:mm» para lo de hoy; fecha completa para lo anterior.</summary>
    private string WhenText(DateTimeOffset startedAt)
    {
        var now = _now();
        var local = startedAt.ToOffset(now.Offset);
        return local.Date == now.Date ? $"HOY · {local:HH:mm}" : local.ToString("dd/MM/yyyy · HH:mm");
    }

    [RelayCommand]
    private void Search()
    {
        Results.Clear();
        if (string.IsNullOrWhiteSpace(Query)) return;
        foreach (var h in store.Search(Query, 50))
            Results.Add(new SearchResultItem(h.MeetingId, h.MeetingTitle, TimeFormat.Hms(h.Segment.Start), h.Segment.Label, h.Snippet));
        Message = Results.Count == 0 ? $"Sin resultados para «{Query}»." : $"{Results.Count} resultados";
    }

    [RelayCommand]
    private void OpenResult(SearchResultItem? hit)
    {
        if (hit is null) return;
        if (Meetings.Count == 0) Refresh();
        Selected = Meetings.FirstOrDefault(m => m.Id == hit.MeetingId);
    }

    private bool CanExport() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (Selected is null) return;
        var meeting = store.GetMeeting(Selected.Id);
        if (meeting is null) return;
        Directory.CreateDirectory(exportDir);
        var path = Path.Combine(exportDir, $"reunion-{meeting.Id}.md");
        var segments = store.GetSegments(meeting.Id);
        var md = MarkdownExporter.Export(meeting, segments);
        // Estadísticas y análisis (si lo hay) antes de la transcripción, que es lo más largo.
        var extra = $"## Estadísticas\n\n{MeetingStats.From(segments).ToMarkdown()}\n";
        if (insights?.GetInsights(meeting.Id) is { } saved)
        {
            try { extra += $"## Análisis ({saved.Model})\n\n" + MeetingInsights.Parse(saved.Json).ToMarkdown().Replace("## ", "### ") + "\n"; }
            catch (FormatException) { }
        }
        var at = md.IndexOf("## Transcripción", StringComparison.Ordinal);
        File.WriteAllText(path, at >= 0 ? md.Insert(at, extra) : md + "\n" + extra);
        Message = "Reunión exportada a Markdown.";
    }

    private static string SourceOf(string consent) => consent switch
    {
        "subtitulos" or "bot-transcribir" => "Meet",
        "grabar" or "transcribir" => "Micrófono",
        var other => other,
    };
}
