using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetAgent.Core;
using MeetAgent.Core.Export;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;

namespace MeetAgent.Desktop.ViewModels;

public sealed record MeetingItem(long Id, string Title, string When, string Duration, string Source, int SegmentCount);

public sealed record SearchResultItem(long MeetingId, string MeetingTitle, string Time, string Speaker, string Snippet);

/// <summary>
/// Historial de TODAS las reuniones de la base: captura local, bot de Meet y subtítulos de la extensión.
/// Permite abrir cada transcripción completa, buscar en todas y exportar a Markdown.
/// </summary>
public sealed partial class HistoryViewModel(ITranscriptStore store, string exportDir) : ObservableObject
{
    public ObservableCollection<MeetingItem> Meetings { get; } = new();
    public ObservableCollection<TranscriptLine> Lines { get; } = new();
    public ObservableCollection<SearchResultItem> Results { get; } = new();

    [ObservableProperty] private MeetingItem? _selected;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string? _message;

    partial void OnSelectedChanged(MeetingItem? value)
    {
        Lines.Clear();
        if (value is null) return;
        foreach (var s in store.GetSegments(value.Id))
            Lines.Add(new TranscriptLine(TimeFormat.Hms(s.Start), s.Speaker, s.Text, s.SpeakerName));
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Relee la base: la extensión y el bot escriben desde otros procesos.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var keep = Selected?.Id;
        Meetings.Clear();
        foreach (var m in store.ListMeetings())
            Meetings.Add(new MeetingItem(m.Id, m.Title, m.StartedAt.ToString("yyyy-MM-dd HH:mm"),
                m.EndedAt is { } e ? TimeFormat.Hms(e - m.StartedAt) : "en curso",
                SourceOf(m.Consent), store.GetSegments(m.Id).Count));
        Selected = keep is null ? null : Meetings.FirstOrDefault(m => m.Id == keep);
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
        File.WriteAllText(path, MarkdownExporter.Export(meeting, store.GetSegments(meeting.Id)));
        Message = $"Exportada a {path}";
    }

    private static string SourceOf(string consent) => consent switch
    {
        "subtitulos" => "Subtítulos de Meet",
        "bot-transcribir" => "Bot de Meet",
        "grabar" or "transcribir" => "Micrófono",
        var other => other,
    };
}
