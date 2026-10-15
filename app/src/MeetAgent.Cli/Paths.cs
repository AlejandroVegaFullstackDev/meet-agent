using MeetAgent.Core;

namespace MeetAgent.Cli;

public static class Paths
{
    public static string Root => AppPaths.Root;
    public static string Data => AppPaths.Data;
    public static string Models => AppPaths.Models;
    public static string Model(string name) => AppPaths.Model(name);
}
