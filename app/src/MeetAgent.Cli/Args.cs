namespace MeetAgent.Cli;

/// <summary>Parser mínimo: <c>comando --clave valor --bandera</c>.</summary>
public sealed class Args
{
    private readonly Dictionary<string, string?> _opts = new(StringComparer.OrdinalIgnoreCase);

    public Args(string[] argv)
    {
        Command = argv.Length > 0 && !argv[0].StartsWith("--") ? argv[0] : "help";
        for (int i = Command == "help" ? 0 : 1; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) continue;
            var key = argv[i][2..];
            string? value = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : null;
            _opts[key] = value;
        }
    }

    public string Command { get; }
    public bool Has(string key) => _opts.ContainsKey(key);
    public string? Get(string key) => _opts.TryGetValue(key, out var v) ? v : null;
    public string Get(string key, string fallback) => Get(key) ?? fallback;
    public double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    public int GetInt(string key, int fallback) => int.TryParse(Get(key), out var n) ? n : fallback;
}
