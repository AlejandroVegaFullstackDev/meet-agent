namespace MeetAgent.Cli;

/// <summary>Ubica la raíz del proyecto (la carpeta que contiene <c>models/</c>) para resolver data/ y models/.</summary>
public static class Paths
{
    public static string Root { get; } = FindRoot();
    public static string Data => Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
    public static string Models => Path.Combine(Root, "models");

    public static string Model(string name) =>
        File.Exists(name) ? name : Path.Combine(Models, name.EndsWith(".bin") ? name : $"ggml-{name}.bin");

    private static string FindRoot()
    {
        if (Environment.GetEnvironmentVariable("MEETAGENT_HOME") is { Length: > 0 } env) return env;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "models"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
