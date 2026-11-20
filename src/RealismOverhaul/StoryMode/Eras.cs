using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace RealismOverhaul.StoryMode;

/// The Eras of Story Mode (ADR 0005), read from data/eras.json next to the plugin.
public sealed class Era
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("theme")] public EraThemeData Theme { get; set; } = new();
    /// Game maps that existed in this Era (lower case: "dust", "nuke"...); null = all of them.
    [JsonPropertyName("maps")] public List<string>? Maps { get; set; }
}

/// The look of an Era. A null palette means "the game's own look" (the present day).
public sealed class EraThemeData
{
    [JsonPropertyName("style")] public string Style { get; set; } = "flat";
    [JsonPropertyName("font")] public string? Font { get; set; }
    [JsonPropertyName("font_title")] public string? FontTitle { get; set; }
    [JsonPropertyName("corner_radius")] public int CornerRadius { get; set; }
    [JsonPropertyName("texture")] public string? Texture { get; set; }
    [JsonPropertyName("palette")] public Dictionary<string, string>? Palette { get; set; }

    public Color Colour(string role, Color fallback) =>
        Palette != null && Palette.TryGetValue(role, out var hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
}

public static class Eras
{
    sealed class File_
    {
        [JsonPropertyName("eras")] public List<Era> Eras { get; set; } = new();
    }

    static List<Era>? _all;

    public static string DataDir => Path.Combine(BepInEx.Paths.PluginPath, "RealismOverhaul", "data");

    public static IReadOnlyList<Era> All
    {
        get
        {
            if (_all != null) return _all;
            try
            {
                var json = File.ReadAllText(Path.Combine(DataDir, "eras.json"));
                _all = JsonSerializer.Deserialize<File_>(json)?.Eras ?? new List<Era>();
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

    /// The Era a date belongs to (dates are "yyyy-MM-dd").
    public static Era? At(DateTime date)
    {
        var d = date.ToString("yyyy-MM-dd");
        return All.LastOrDefault(e => string.CompareOrdinal(e.From, d) <= 0);
    }
}
