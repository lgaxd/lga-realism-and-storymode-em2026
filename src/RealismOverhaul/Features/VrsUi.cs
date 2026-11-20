using HarmonyLib;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;

namespace RealismOverhaul.Features;

/// Statistics screen: a "VRS" button opens the Valve Regional Standings over the screen: each
/// Region's top 20 (the user's team highlighted, or appended with its rank), and what the standings
/// mean for the next Major: its slots, how they split between Regions, and whether the user's team
/// is in. The panel is its own full-size overlay, independent of the screen's internal layout.
public static class VrsUi
{
    const string ButtonName = "RealismOverhaul.VrsButton";
    const string PanelName = "RealismOverhaul.VrsPanel";
    const int TopN = 20;
    const float RowH = 22f;

    static StatisticWindow? _window;

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(VrsUi));
        Plugin.Log.LogInfo("[VrsUi] patches applied");
    }

    static TMP_Text? Style(Component root) => root.GetComponentInChildren<TMP_Text>(true);

    // The statistics screen opens through ShowTeams / ShowPlayers / OnWindowSwitched, not Renew.
    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.ShowTeams))]
    [HarmonyPostfix]
    static void ShowTeamsPostfix(StatisticWindow __instance) => AddButton(__instance);

    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.ShowPlayers))]
    [HarmonyPostfix]
    static void ShowPlayersPostfix(StatisticWindow __instance) => AddButton(__instance);

    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.OnWindowSwitched))]
    [HarmonyPostfix]
    static void OnWindowSwitchedPostfix(StatisticWindow __instance) => AddButton(__instance);

    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.Renew))]
    [HarmonyPostfix]
    static void RenewPostfix(StatisticWindow __instance) => AddButton(__instance);

    static void AddButton(StatisticWindow __instance)
    {
        try
        {
            _window = __instance;
            var root = __instance.transform;
            var style = Style(__instance);
            if (style == null || FindButton(__instance) != null) return;
            // A third tab right after "Teams | Players": find the Players tab by its text.
            var playersTab = FindTab(__instance);
            if (playersTab == null)
            {
                Plugin.Log.LogWarning("[VrsUi] could not find the Players tab; placing the VRS button at the top-left");
                UiKit.TextButton(root, ButtonName, style, "<b>VRS</b>", 18f, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(230f, 40f), new Vector2(80f, 34f), Toggle);
            }
            else
            {
                var tabRt = playersTab.GetComponent<RectTransform>();
                var parent = playersTab.parent;
                var btn = UiKit.TextButton(parent, ButtonName, style, "<b>VRS</b>", 18f, tabRt.anchorMin, tabRt.anchorMax, tabRt.pivot,
                    tabRt.anchoredPosition + new Vector2(tabRt.rect.width * (1f - tabRt.pivot.x) + 30f + 80f * tabRt.pivot.x, 0f), new Vector2(80f, tabRt.rect.height), Toggle);
                if (parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                {
                    var le = btn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    le.preferredWidth = 80f;
                    le.preferredHeight = tabRt.rect.height;
                }
                btn.transform.SetSiblingIndex(playersTab.GetSiblingIndex() + 1);
            }
            Plugin.Log.LogInfo("[VrsUi] VRS button added to the statistics screen");
        }
        catch (Exception e) { Plugin.Log.LogError($"[VrsUi] could not add the VRS button: {e}"); }
    }

    static readonly string[] PlayersTabTexts = { "Jogadores", "Players", "Jugadores" };

    /// The Players tab (the tab object itself if its text sits inside a button).
    static Transform? FindTab(StatisticWindow w)
    {
        foreach (var t in w.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t == null || !PlayersTabTexts.Contains((t.text ?? "").Trim())) continue;
            var tr = t.transform;
            return tr.parent != null && tr.parent.GetComponent<UnityEngine.UI.Button>() != null ? tr.parent : tr;
        }
        return null;
    }

    static Transform? FindButton(StatisticWindow w)
    {
        foreach (var t in w.GetComponentsInChildren<Transform>(true))
            if (t != null && t.name == ButtonName) return t;
        return null;
    }

    static void Toggle()
    {
        var w = _window;
        if (w == null) return;
        var existing = w.transform.Find(PanelName);
        if (existing != null) { UnityEngine.Object.Destroy(existing.gameObject); return; }
        try { Build(w); }
        catch (Exception e) { Plugin.Log.LogError($"[VrsUi] could not build the standings: {e}"); }
    }

    static string RegionLabel(Region r, bool pt) => r switch
    {
        Region.Europe => pt ? "Europa" : "Europe",
        Region.Americas => pt ? "Américas" : "Americas",
        _ => pt ? "Ásia" : "Asia",
    };

    static DataTournament? NextMajor()
    {
        var today = GlobalValues.CurrentDate;
        var active = DataTournaments.ActiveTournaments;
        if (active == null) return null;
        var n = active.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataTournament>>().Count;
        DataTournament? best = null;
        for (var i = 0; i < n; i++)
        {
            var t = active[i];
            if (t == null || t.Tier != TournamentType.Major || t.DateEnd < today) continue;
            if (best == null || t.DateStart < best.DateStart) best = t;
        }
        return best;
    }

    static void Build(StatisticWindow w)
    {
        var style = Style(w)!;
        var pt = UiKit.IsPortuguese();
        var s = Vrs.Current();
        var club = GlobalValues.DataTeam;

        var panel = UiKit.Image(w.transform, PanelName, new Color(0.055f, 0.065f, 0.09f, 1f), Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, -60f));
        panel.raycastTarget = true; // block clicks to the screen underneath
        var t = panel.transform;
        t.SetAsLastSibling();
        // Keep the toggle clickable above the panel.
        var button = FindButton(w);
        if (button != null && button.parent == w.transform) button.SetAsLastSibling();

        UiKit.Text(t, "Title", style, "<b>Valve Regional Standings (VRS)</b>", 22f, Color.white, TextAlignmentOptions.Left,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(-140f, 30f));
        var basis = pt
            ? $"Histórico do mod: {s.HistoryDays} dias · peso VRS {s.VrsWeight:P0}, ERS do jogo {1 - s.VrsWeight:P0} (o VRS assume aos poucos até 6 meses)"
            : $"Mod history: {s.HistoryDays} days · VRS weight {s.VrsWeight:P0}, game ERS {1 - s.VrsWeight:P0} (VRS takes over gradually up to 6 months)";
        UiKit.Text(t, "Basis", style, basis, 14f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Left,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -44f), new Vector2(-48f, 20f));

        // Three Region columns.
        var regions = Enum.GetValues<Region>();
        for (var c = 0; c < regions.Length; c++)
        {
            var region = regions[c];
            var x0 = c / 3f;
            var col = UiKit.Rect(t, $"Col{c}", new Vector2(x0, 1f), new Vector2(x0 + 1f / 3f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -78f), new Vector2(-36f, RowH * (TopN + 4)));
            UiKit.Text(col, "Header", style, $"<b>{RegionLabel(region, pt)}</b>", 18f, new Color(1f, 0.8f, 0.3f), TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 26f));
            var list = s.ByRegion[region];
            var mineIndex = club == null ? -1 : list.FindIndex(x => x.Team.Id == club.Id);
            var rows = list.Take(TopN).Select((x, i) => (Rank: i + 1, x.Team, x.Points)).ToList();
            if (mineIndex >= TopN) rows.Add((mineIndex + 1, list[mineIndex].Team, list[mineIndex].Points));
            for (var i = 0; i < rows.Count; i++)
            {
                var (rank, team, points) = rows[i];
                var y = -30f - (i + (i == TopN ? 0.5f : 0f)) * RowH;
                var isMine = club != null && team.Id == club.Id;
                var color = isMine ? new Color(0.35f, 1f, 0.45f) : Color.white;
                var name = isMine ? $"<b>{team.Name}</b>" : team.Name;
                UiKit.Text(col, $"Rank{i}", style, $"#{rank}", 14f, new Color(color.r, color.g, color.b, 0.7f), TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(40f, RowH));
                UiKit.Text(col, $"Team{i}", style, name, 14f, color, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(42f, y), new Vector2(-110f, RowH));
                UiKit.Text(col, $"Pts{i}", style, $"{points:F0}", 14f, color, TextAlignmentOptions.Right,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, y), new Vector2(60f, RowH));
            }
        }

        // The next Major.
        var major = NextMajor();
        string majorText;
        if (major == null)
            majorText = pt ? "<b>Próximo Major:</b> nenhum agendado no momento." : "<b>Next Major:</b> none scheduled right now.";
        else
        {
            var slots = Math.Max(1, major.ParticipantCountTarget);
            var invited = s.InviteOrder.Take(slots).ToList();
            var split = string.Join(", ", Enum.GetValues<Region>().Select(r => $"{RegionLabel(r, pt)} {invited.Count(x => Vrs.RegionOf(x) == r)}"));
            var place = club == null ? -1 : s.InviteOrder.ToList().FindIndex(x => x.Id == club.Id) + 1;
            string status;
            if (place <= 0) status = "";
            else if (place <= slots)
                status = pt ? $"<color=#5CFF73>{club!.Name}: dentro (vaga {place} de {slots})</color>" : $"<color=#5CFF73>{club!.Name}: in (slot {place} of {slots})</color>";
            else
                status = pt ? $"<color=#FF6B5C>{club!.Name}: fora ({place - slots} posições abaixo do corte)</color>" : $"<color=#FF6B5C>{club!.Name}: out ({place - slots} places below the cut)</color>";
            majorText = pt
                ? $"<b>Próximo Major:</b> {major.Name} ({major.DateStart.ToString("dd/MM/yyyy")}) · {slots} vagas por VRS, divididas por Região: {split}\n{status}"
                : $"<b>Next Major:</b> {major.Name} ({major.DateStart.ToString("yyyy-MM-dd")}) · {slots} slots by VRS, split by Region: {split}\n{status}";
        }
        UiKit.Text(t, "Major", style, majorText, 15f, Color.white, TextAlignmentOptions.Left,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(-48f, 50f))
            .enableWordWrapping = true;
    }
}
