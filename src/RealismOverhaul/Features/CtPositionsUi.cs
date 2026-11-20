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
    const float RowH = 23f;
    const float TitleArea = 108f; // map name and pick/ban badges at the top of the column

    // Short labels: the columns are ~150 px wide.
    static string Short(string position) => position switch
    {
        "A-site" => "A", "B-site" => "B", "B-site (Ramp)" => "Ramp", "Connector" => "Con", "Apartments" => "Apps",
        "Arch/Mid" => "Mid", "Outside" => "Out", "Secret" => "Sec", "Highway" => "Hwy", "Popdog" => "Pop",
        _ => position,
    };

    /// The column's regular body font (the info texts at the bottom), not the condensed title font.
    static TMP_Text? BodyStyle(Transform column)
    {
        var info = column.Find("infoGo");
        var t = info?.GetComponentInChildren<TMP_Text>(true);
        return t ?? column.Find("mapNameTxt")?.GetComponent<TMP_Text>();
    }

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
        var mapName = column.Find("mapNameTxt")?.GetComponent<TMP_Text>();
        if (mapName == null || !Enum.TryParse<MapType>(mapName.text?.Trim(), true, out var map)) return;
        var nameText = BodyStyle(column)!;
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
        var height = RowH * (positions.Length + 1) + 6f;

        // Anchored under the map name, ending above the training gauge.
        var block = UiKit.Image(column, BlockName, new Color(0f, 0f, 0f, 0.6f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -TitleArea), new Vector2(-16f, height));
        block.raycastTarget = true; // keep clicks on the block from selecting the map underneath
        var t = block.transform;

        UiKit.Text(t, "Title", nameText, chosen == null ? "<b>CT</b> (auto)" : "<b>CT</b>", 13f, Color.white, TextAlignmentOptions.Left,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -2f), new Vector2(90f, RowH));
        if (chosen != null)
            UiKit.TextButton(t, "Auto", nameText, "Auto", 13f, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-5f, -3f), new Vector2(46f, RowH - 5f), () =>
                {
                    ManualChoices.ClearCtPositions(club.Id, map);
                    BuildColumn(column);
                });

        var names = five.Select(p => p.Id).ToList();
        for (var i = 0; i < positions.Length; i++)
        {
            var position = positions[i];
            var y = -RowH * (i + 1) - 2f;
            UiKit.Text(t, $"Pos{i}", nameText, Short(position), 13f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0.3f, 1f), new Vector2(0f, 1f), new Vector2(8f, y), new Vector2(-8f, RowH));
            var holderPlayer = setup.FirstOrDefault(x => x.Position == position).Player;
            var index = holderPlayer == null ? 0 : names.IndexOf(holderPlayer.Id);
            var pos = position;
            var dd = UiKit.Dropdown(template, t, $"Player{i}", names, Math.Max(0, index),
                new Vector2(0.3f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(-5f, RowH - 2f),
                picked =>
                {
                    if (picked < 0 || picked >= five.Count) return;
                    // Keep the rest of the current setup, then move the picked player here.
                    foreach (var (p2, pl) in MapFamiliarity.AssignCtPositions(five, map))
                        if (p2 != pos && pl.Id != five[picked].Id) ManualChoices.SetCtPosition(club.Id, map, p2, pl.Id);
                    ManualChoices.SetCtPosition(club.Id, map, pos, five[picked].Id);
                    BuildColumn(column);
                });
            // One line, smaller: "s1mple" must not wrap in a ~100 px field.
            // Ellipsis mode hides the whole text when it does not fit vertically, which the
            // dropdown's padding causes at this row height; overflow keeps it visible.
            if (dd.captionText != null)
            {
                dd.captionText.enableWordWrapping = false;
                dd.captionText.overflowMode = TextOverflowModes.Overflow;
                dd.captionText.enableAutoSizing = false;
                dd.captionText.fontSize = 13f;
                dd.captionText.verticalAlignment = VerticalAlignmentOptions.Middle;
                dd.captionText.margin = Vector4.zero;
            }
        }
    }
}
