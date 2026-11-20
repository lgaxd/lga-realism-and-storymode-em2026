using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RealismOverhaul.Infrastructure;

/// Diagnostics: writes the object hierarchy of key screens (player profile, squad, map training)
/// to BepInEx/RealismOverhaul-ui-dump.txt, once per screen per session, so new UI can be placed
/// against the game's real layout instead of by trial and error.
public static class UiDump
{
    static readonly HashSet<string> _dumped = new();
    static string FilePath => Path.Combine(BepInEx.Paths.BepInExRootPath, "RealismOverhaul-ui-dump.txt");

    public static void Apply(Harmony harmony)
    {
        File.WriteAllText(FilePath, $"Realism Overhaul UI dump {DateTime.Now:yyyy-MM-dd HH:mm}\n");
        harmony.PatchAll(typeof(UiDump));
        Plugin.Log.LogInfo($"[UiDump] will write screen layouts to {FilePath}");
    }

    static void Dump(string screen, Component root)
    {
        if (root == null || !_dumped.Add(screen)) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\n===== {screen} (root '{root.gameObject.name}') =====");
            Walk(root.transform, 0, sb);
            File.AppendAllText(FilePath, sb.ToString());
            Plugin.Log.LogInfo($"[UiDump] dumped {screen}");
        }
        catch (Exception e) { Plugin.Log.LogError($"[UiDump] {screen} failed: {e}"); }
    }

    static void Walk(Transform t, int depth, StringBuilder sb)
    {
        if (depth > 14) return;
        var go = t.gameObject;
        var rt = t.TryCast<RectTransform>();
        var comps = new List<string>();
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            var n = c.GetIl2CppType().Name;
            if (n is "Transform" or "RectTransform" or "CanvasRenderer") continue;
            if (n.Contains("TextMeshPro") || n == "TMP_Text")
            {
                var text = c.TryCast<TMPro.TMP_Text>()?.text ?? "";
                if (text.Length > 30) text = text[..30] + "…";
                n += $"(\"{text.Replace("\n", " ")}\")";
            }
            comps.Add(n);
        }
        var rect = rt == null ? "" :
            $" pos={rt.anchoredPosition} size={rt.rect.size} anchors={rt.anchorMin}-{rt.anchorMax} pivot={rt.pivot}";
        sb.AppendLine($"{new string(' ', depth * 2)}{go.name}{(go.activeSelf ? "" : " [inactive]")}{rect} [{string.Join(", ", comps)}]");
        for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
    }

    [HarmonyPatch(typeof(PlayerOverview), nameof(PlayerOverview.Renew))]
    [HarmonyPostfix]
    static void PlayerOverviewPostfix(PlayerOverview __instance) => Dump("PlayerOverview (profile, Attributes tab)", __instance);

    [HarmonyPatch(typeof(SquadWindow), nameof(SquadWindow.Renew))]
    [HarmonyPostfix]
    static void SquadWindowPostfix(SquadWindow __instance) => Dump("SquadWindow", __instance);

    [HarmonyPatch(typeof(MapTrainingView), nameof(MapTrainingView.Renew))]
    [HarmonyPostfix]
    static void MapTrainingViewPostfix(MapTrainingView __instance) => Dump("MapTrainingView", __instance);
}
