using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Cli;

public static class Store
{
    public static string DbPath => MeetAgent.Core.AppPaths.Database;
    public static SqliteTranscriptStore Open() => new($"Data Source={DbPath}");
}
