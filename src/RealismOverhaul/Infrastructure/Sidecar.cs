using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;

namespace RealismOverhaul.Infrastructure;

/// The mod's own save data, kept next to the game's save and never inside it (ADR 0003).
///
/// One JSON file per career: Save/&lt;slotId&gt;/RealismOverhaul.json, inside the slot folder, so the
/// game's own slot delete and backup handle it too. Features own top-level sections. The file is
/// read when the game loads a slot (SlotController.LoadSlot) and written whenever the game queues a
/// save (SlotController.EnqueueSave, used by manual saves and autosaves). When a slot has no file,
/// or it is unreadable, features rebuild their data from the game's state.
public static class Sidecar
{
    public const string FileName = "RealismOverhaul.json";
    const int Version = 1;

    /// A feature's section: Load gets the stored JSON (null when absent), Save returns what to store.
    public interface ISection
    {
        string Name { get; }
        void Load(JsonNode? data);
        JsonNode? Save();
    }

    static readonly List<ISection> _sections = new();
    static bool _applied;
    static string? _loadedSlot;
    static JsonObject? _loadedRoot; // kept so sections of disabled features survive a save (ADR 0003)

    public static void Register(ISection section) => _sections.Add(section);

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _applied = true;
        harmony.PatchAll(typeof(Sidecar));
    }

    static string? SlotDir(string? slotId)
    {
        if (string.IsNullOrEmpty(slotId)) return null;
        try
        {
            // Slots live in <persistentDataPath>/Save/<slotId>/ (LocalLow/NeuronaGames/EsportsManager).
            return Path.Combine(UnityEngine.Application.persistentDataPath, "Save", slotId);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[Sidecar] could not resolve the folder of slot {slotId}: {e.Message}");
            return null;
        }
    }

    [HarmonyPatch(typeof(SlotController), nameof(SlotController.LoadSlot))]
    [HarmonyPostfix]
    static void LoadSlotPostfix(string slotId)
    {
        _loadedSlot = slotId;
        JsonObject? root = null;
        var dir = SlotDir(slotId);
        var path = dir == null ? null : Path.Combine(dir, FileName);
        try
        {
            if (path != null && File.Exists(path))
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[Sidecar] {path} is unreadable, rebuilding from the game's data: {e.Message}");
        }

        foreach (var s in _sections)
        {
            try { s.Load(root?[s.Name]); }
            catch (Exception e) { Plugin.Log.LogError($"[Sidecar] section {s.Name} failed to load: {e}"); s.Load(null); }
        }
        _loadedRoot = root;
        Plugin.Log.LogInfo($"[Sidecar] slot {slotId}: {(root == null ? "no mod data yet, rebuilding from the game" : $"loaded {path}")}");
    }

    [HarmonyPatch(typeof(SlotController), nameof(SlotController.EnqueueSave))]
    [HarmonyPrefix]
    static void EnqueueSavePrefix(string slotId)
    {
        if (_sections.Count == 0 || string.IsNullOrEmpty(slotId)) return;
        var dir = SlotDir(slotId);
        if (dir == null) return;
        try
        {
            // Start from what was loaded for this slot, so sections of features that are switched off
            // (not registered) are written back untouched.
            var root = slotId == _loadedSlot && _loadedRoot != null
                ? (JsonObject)JsonNode.Parse(_loadedRoot.ToJsonString())!
                : new JsonObject();
            root["version"] = Version;
            root["slot"] = slotId;
            foreach (var s in _sections)
            {
                try { root[s.Name] = s.Save(); }
                catch (Exception e) { Plugin.Log.LogError($"[Sidecar] section {s.Name} failed to save: {e}"); }
            }
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            File.Move(tmp, path, overwrite: true); // atomic replace: a crash mid-write never leaves half a file
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[Sidecar] could not write mod data for slot {slotId}: {e}");
        }
    }
}
