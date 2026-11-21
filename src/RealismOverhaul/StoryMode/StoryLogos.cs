using System.Text.Json;
using HarmonyLib;
using UnityEngine;

namespace RealismOverhaul.StoryMode;

/// Team logos and player photos of a start pack (story/yyyy-MM-dd/logos/<team name>.png and
/// photos/<player nick>.png, made locally by tools/storydata/logos.py and never shipped).
///
/// The game looks images up in CustomAssets by team name or player nick. A Story Mode world answers
/// those lookups from its own pack instead, nothing is copied into CustomAssets: the same name needs
/// another logo in another year ("Counter Logic Gaming" in 2016 is not the team of that name today),
/// and the player's own CustomAssets stay exactly as he left them.
///   * Team logos: the world's logo first (it is the one of that year), CustomAssets otherwise.
///   * Player photos: the player's own photo first, the world's otherwise.
[HarmonyPatch]
internal static class StoryLogos
{
    static string StoryDir => Path.Combine(BepInEx.Paths.PluginPath, "RealismOverhaul", "story");

    /// The Start Date whose pack answers lookups: the career being played, or the world just loaded.
    static string? Active => StoryStart.CareerStart ?? StoryStart.PendingStart;

    [HarmonyPatch(typeof(CustomAssetOverride), nameof(CustomAssetOverride.TryLoadOverride)), HarmonyPrefix]
    static bool TeamFromPack(ImageLoaderType type, string key, ref Sprite sprite, ref bool __result)
    {
        if (type != ImageLoaderType.TEAM || Active is not { } start || string.IsNullOrEmpty(key)) return true;
        if (!FromPack(start, "logos", key, ref sprite)) return true;
        __result = true;
        return false;
    }

    [HarmonyPatch(typeof(CustomAssetOverride), nameof(CustomAssetOverride.TryLoadOverride)), HarmonyPostfix]
    static void PhotoFromPack(ImageLoaderType type, string key, ref Sprite sprite, ref bool __result)
    {
        if (__result || type != ImageLoaderType.PLAYER || Active is not { } start || string.IsNullOrEmpty(key)) return;
        if (FromPack(start, "photos", key, ref sprite)) __result = true;
    }

    static bool FromPack(string start, string folder, string key, ref Sprite sprite)
    {
        try
        {
            var dir = Path.Combine(StoryDir, start, folder);
            var file = Safe(key) + ".png";
            if (!File.Exists(Path.Combine(dir, file)))
            {
                // A career keeps the names of the pack it began from; a rebuilt pack may spell a team
                // a little differently ("K1ck eSports Club" -> "K1CK"). Players are matched exactly.
                if (folder != "logos" || !Loose(dir).TryGetValue(Norm(key), out var near)) return false;
                file = near;
            }
            // TryLoadFile appends each supported extension itself: give it the bare name.
            if (!CustomAssetOverride.TryLoadFile(dir, Path.GetFileNameWithoutExtension(file), out var found) || found == null)
            {
                Plugin.Log.LogWarning($"[StoryLogos] {folder}/{file} is in the pack but the game could not load it");
                return false;
            }
            sprite = found;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[StoryLogos] {folder}/{key}: {e.Message}");
            return false;
        }
    }

    static readonly Dictionary<string, Dictionary<string, string>> LooseIndex = new();

    static Dictionary<string, string> Loose(string dir)
    {
        if (LooseIndex.TryGetValue(dir, out var index)) return index;
        index = new Dictionary<string, string>();
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.png"))
                index.TryAdd(Norm(Path.GetFileNameWithoutExtension(f)), Path.GetFileName(f));
        return LooseIndex[dir] = index;
    }

    static readonly System.Text.RegularExpressions.Regex Affixes =
        new(@"^(team|the)\s+|\s+(e-?sports?(\s+club)?|gaming|clan|club|gg|team)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static string Norm(string name)
    {
        name = Affixes.Replace(Affixes.Replace(name.Trim(), ""), "");
        return new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    static string Safe(string name)
    {
        foreach (var c in "<>:\"/\\|?*") name = name.Replace(c, '_');
        return name.Trim();
    }

    /// A world was loaded or a career opened: images already shown belong to another world.
    internal static void Install(string start)
    {
        try
        {
            CustomAssetOverride.ClearCache();
            ImageLoader.RecordedSprites?.Clear();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[StoryLogos] could not clear the image caches: {e.Message}"); }
        Plugin.Log.LogInfo($"[StoryLogos] team logos and player photos of {start} in use");
    }

    /// Earlier versions copied pack images into CustomAssets: take back exactly those files.
    internal static void RemoveCopies()
    {
        foreach (var (manifest, folder) in new[] { ("logos-installed.json", "Teams"), ("photos-installed.json", "Players") })
        {
            var path = Path.Combine(StoryDir, manifest);
            if (!File.Exists(path)) continue;
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "CustomAssets", folder);
                var removed = 0;
                foreach (var name in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new())
                {
                    var file = Path.Combine(dir, Path.GetFileName(name));
                    if (!File.Exists(file)) continue;
                    File.Delete(file);
                    removed++;
                }
                File.Delete(path);
                Plugin.Log.LogInfo($"[StoryLogos] removed {removed} images earlier versions had copied into CustomAssets/{folder}");
            }
            catch (Exception e) { Plugin.Log.LogError($"[StoryLogos] cleaning CustomAssets/{folder}: {e.Message}"); }
        }
    }
}
