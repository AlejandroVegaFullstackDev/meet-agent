using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Cli;

public static class Store
{
    public static string DbPath => Path.Combine(Paths.Data, "meetagent.db");
    public static SqliteTranscriptStore Open() => new($"Data Source={DbPath}");
}
