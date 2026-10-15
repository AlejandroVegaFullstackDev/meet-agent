namespace MeetAgent.Core.Text;

public static class TimeFormat
{
    public static string Hms(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
}
