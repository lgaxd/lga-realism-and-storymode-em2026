using HarmonyLib;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;

namespace RealismOverhaul.Features;

/// Map training screen: each map's column gets a "CT Positions" block for the Main Roster: one
/// dropdown per position with the five starters, plus "Auto" to go back to Role-based assignment.
/// Choices are saved (ManualChoices) and used by the CT setup and Side Bias of every match.
/// Layout from the UI dump: Content/mapsHolder holds one OneMapTrainRow per map (~178 x 500).
public static class CtPositionsUi
{
    const string BlockName = "RealismOverhaul.CtPositions";
    const float RowH = 30f;

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(CtPositionsUi));
        Plugin.Log.LogInfo("[CtPositionsUi] patches applied");
    }

    [HarmonyPatch(typeof(MapTrainingView), nameof(MapTrainingView.Renew))]
    [HarmonyPostfix]
    static void RenewPostfix(MapTrainingView __instance)
    {
        try
        {
            var holder = __instance.transform.Find("Content/mapsHolder");
            if (holder == null) return;
            for (var i = 0; i < holder.childCount; i++) BuildColumn(holder.GetChild(i));
        }
        catch (Exception e) { Plugin.Log.LogError($"[CtPositionsUi] failed: {e}"); }
    }

    static List<DataPlayer> Five(DataTeam club)
    {
        var result = new List<DataPlayer>();
        var squad = club.MainSquad;
        for (var i = 0; squad != null && i < squad.Count && result.Count < 5; i++)
        {
            var p = DataPlayers.Get(squad[i]);
            if (p != null) result.Add(p);
        }
        return result;
    }

    static void BuildColumn(Transform column)
    {
        var nameText = column.Find("mapNameTxt")?.GetComponent<TMP_Text>();
        if (nameText == null || !Enum.TryParse<MapType>(nameText.text?.Trim(), true, out var map)) return;
        var old = column.Find(BlockName);
        if (old != null) UnityEngine.Object.Destroy(old.gameObject);

        var club = GlobalValues.DataTeam;
        var template = UiKit.FindDropdownTemplate();
        if (club == null || template == null) return;
        var five = Five(club);
        if (five.Count < 5) return;

        var pt = UiKit.IsPortuguese();
        var setup = MapFamiliarity.AssignCtPositions(five, map);
        var chosen = ManualChoices.CtPositions(club.Id, map);
        var positions = MapFamiliarity.PositionsOf(map);
        var height = RowH * (positions.Length + 1) + 12f;

        var block = UiKit.Image(column, BlockName, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(-16f, height));
        block.raycastTarget = true; // keep clicks on the block from selecting the map underneath
        var t = block.transform;

        UiKit.Text(t, "Title", nameText, chosen == null ? (pt ? "CT (auto)" : "CT (auto)") : "CT", 14f, Color.white, TextAlignmentOptions.Left,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(90f, RowH - 4f));
        if (chosen != null)
            UiKit.TextButton(t, "Auto", nameText, "Auto", 13f, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-6f, -5f), new Vector2(52f, RowH - 8f), () =>
                {
                    ManualChoices.ClearCtPositions(club.Id, map);
                    BuildColumn(column);
                });

        var names = five.Select(p => p.Id).ToList();
        for (var i = 0; i < positions.Length; i++)
        {
            var position = positions[i];
            var y = -RowH * (i + 1) - 4f;
            UiKit.Text(t, $"Pos{i}", nameText, position, 12f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0.45f, 1f), new Vector2(0f, 1f), new Vector2(8f, y), new Vector2(-8f, RowH - 4f));
            var holderPlayer = setup.FirstOrDefault(x => x.Position == position).Player;
            var index = holderPlayer == null ? 0 : names.IndexOf(holderPlayer.Id);
            var pos = position;
            UiKit.Dropdown(template, t, $"Player{i}", names, Math.Max(0, index),
                new Vector2(0.45f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(-6f, RowH - 4f),
                picked =>
                {
                    if (picked < 0 || picked >= five.Count) return;
                    // Keep the rest of the current setup, then move the picked player here.
                    foreach (var (p2, pl) in MapFamiliarity.AssignCtPositions(five, map))
                        if (p2 != pos && pl.Id != five[picked].Id) ManualChoices.SetCtPosition(club.Id, map, p2, pl.Id);
                    ManualChoices.SetCtPosition(club.Id, map, pos, five[picked].Id);
                    BuildColumn(column);
                });
        }
    }
}
