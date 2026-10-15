namespace MeetAgent.Cli;

public static class Log
{
    private static readonly object Gate = new();
    public static void Info(string msg)
    {
        lock (Gate) Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {msg}");
    }
}
