using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RealismOverhaul.StoryMode;

/// An Era of Story Mode history (ADR 0005), read from data/eras.json next to the plugin.
public sealed class Era
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("summary_pt")] public string? SummaryPt { get; set; }
}

public static class Eras
{
    sealed class File_
    {
        [JsonPropertyName("eras")] public List<Era> Eras { get; set; } = new();
    }

    static List<Era>? _all;
    static List<(string Date, string Version)>? _versions;

    public static string DataDir => Path.Combine(BepInEx.Paths.PluginPath, "RealismOverhaul", "data");

    public static IReadOnlyList<Era> All
    {
        get
        {
            if (_all != null) return _all;
            try
            {
                _all = JsonSerializer.Deserialize<File_>(File.ReadAllText(Path.Combine(DataDir, "eras.json")))?.Eras ?? new List<Era>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Eras] could not read eras.json: {e.Message}");
                _all = new List<Era>();
            }
            return _all;
        }
    }

    public static Era? ById(string id) => All.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// The Era a date belongs to.
    public static Era? At(DateTime date)
    {
        var d = date.ToString("yyyy-MM-dd");
        return All.LastOrDefault(e => string.CompareOrdinal(e.From, d) <= 0);
    }

    /// The Era after this one, if any.
    public static Era? Next(Era era)
    {
        var i = All.ToList().FindIndex(e => e.Id == era.Id);
        return i >= 0 && i + 1 < All.Count ? All[i + 1] : null;
    }

    /// The game version the Main Circuit played on a date (Game Updates of type "version").
    public static string VersionAt(DateTime date)
    {
        _versions ??= LoadVersions();
        var d = date.ToString("yyyy-MM-dd");
        var hit = _versions.LastOrDefault(v => string.CompareOrdinal(v.Date, d) <= 0);
        return hit.Version ?? "Counter-Strike Beta";
    }

    /// The next game version change after a date, if any.
    public static (string Date, string Version)? NextVersion(DateTime date)
    {
        _versions ??= LoadVersions();
        var d = date.ToString("yyyy-MM-dd");
        foreach (var v in _versions)
            if (string.CompareOrdinal(v.Date, d) > 0) return v;
        return null;
    }

    static List<(string, string)> LoadVersions()
    {
        var list = new List<(string, string)>();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(DataDir, "game_updates.json")));
            foreach (var e in root?["entries"]?.AsArray() ?? new JsonArray())
            {
                var version = e?["effects"]?["version"]?.GetValue<string>();
                var date = e?["date"]?.GetValue<string>();
                if (version != null && date != null) list.Add((date, FullName(version)));
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Eras] could not read game_updates.json: {ex.Message}"); }
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    static string FullName(string version) => version switch
    {
        "CS 1.0" => "Counter-Strike 1.0 – 1.5",
        "CS 1.6" => "Counter-Strike 1.6",
        "CS:GO" => "Counter-Strike: Global Offensive",
        "CS2" => "Counter-Strike 2",
        _ => version,
    };
}
