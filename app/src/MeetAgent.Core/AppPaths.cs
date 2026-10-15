namespace MeetAgent.Core;

/// <summary>
/// Raíz de MeetAgent (la carpeta que contiene <c>models/</c>), compartida por CLI, Desktop y MCP:
/// <c>MEETAGENT_HOME</c>, o subiendo desde el directorio actual o el del ejecutable;
/// si no aparece, <c>~/.local/share/MeetAgent</c> (Linux) o <c>%LOCALAPPDATA%\MeetAgent</c> (Windows).
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = FindRoot();
    public static string Data => Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
    public static string Models => Path.Combine(Root, "models");
    public static string Database => Path.Combine(Data, "meetagent.db");

    /// <summary>Acepta una ruta existente, un nombre de archivo o un nombre corto como <c>small-q5_1</c>.</summary>
    public static string Model(string name) =>
        File.Exists(name) ? name : Path.Combine(Models, name.EndsWith(".bin") ? name : $"ggml-{name}.bin");

    private static string FindRoot()
    {
        if (Environment.GetEnvironmentVariable("MEETAGENT_HOME") is { Length: > 0 } env) return env;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "models"))) return d.FullName;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetAgent");
    }
}
