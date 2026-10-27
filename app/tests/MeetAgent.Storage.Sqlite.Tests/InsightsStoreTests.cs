using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Storage.Sqlite.Tests;

public class InsightsStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Guarda_y_reemplaza_el_analisis_de_una_reunion()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        var id = store.StartMeeting("Daily", T0, "subtitulos");
        Assert.Null(store.GetInsights(id));

        store.SaveInsights(id, "{\"resumen\":\"uno\"}", "DeepSeek · deepseek-chat", T0);
        store.SaveInsights(id, "{\"resumen\":\"dos\"}", "Claude · claude-opus-5-5", T0.AddHours(1));

        var saved = store.GetInsights(id)!;
        Assert.Equal("{\"resumen\":\"dos\"}", saved.Json);
        Assert.Equal("Claude · claude-opus-5-5", saved.Model);
        Assert.Equal(T0.AddHours(1), saved.CreatedAt);
    }
}
