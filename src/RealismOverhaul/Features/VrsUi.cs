using HarmonyLib;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealismOverhaul.Features;

/// Statistics screen: a "VRS" tab switches the game's own teams table (logos, rows, paging,
/// search, team details all intact) from the game's ERS ranking to the Valve Regional Standings:
///   - "All": the invite order (Regions interleaved by quota), the order tournaments are filled in;
///   - "Europe" / "Americas" / "Asia": that Region's standings;
/// the "# ERS" / "ERS" columns become "# VRS" / "VRS", and a line shows the next Major's
/// projection (slots, split by Region, and whether the user's team is in). ERS stays the game's
/// internal rating (match odds, prestige, the AI market); VRS only decides invites.
public static class VrsUi
{
    const string ButtonName = "RealismOverhaul.VrsButton";
    const string RegionBarName = "RealismOverhaul.VrsRegions";
    const string MajorLineName = "RealismOverhaul.VrsMajor";

    static bool _vrsMode;
    static Region? _region; // null = all Regions (invite order)
    static bool _reordering;
    static StatisticWindow? _window;
    static readonly Dictionary<TMP_Text, string> _originalHeaders = new();

    // Snapshot of the standings used while rendering rows.
    static Dictionary<string, int> _rank = new();
    static Dictionary<string, float> _points = new();

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(VrsUi));
        Plugin.Log.LogInfo("[VrsUi] patches applied");
    }

    // --- The tab ---

    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.ShowTeams))]
    [HarmonyPostfix]
    static void ShowTeamsPostfix(StatisticWindow __instance) => AddButton(__instance);

    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.ShowPlayers))]
    [HarmonyPostfix]
    static void ShowPlayersPostfix(StatisticWindow __instance)
    {
        AddButton(__instance);
        if (_vrsMode) SetMode(false); // the VRS view belongs to the teams table
    }

    [HarmonyPatch(typeof(StatisticWindow), nameof(StatisticWindow.OnWindowSwitched))]
    [HarmonyPostfix]
    static void OnWindowSwitchedPostfix(StatisticWindow __instance) => AddButton(__instance);

    static readonly string[] PlayersTabTexts = { "Jogadores", "Players", "Jugadores" };

    static Transform? FindTab(StatisticWindow w)
    {
        foreach (var t in w.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t == null || !PlayersTabTexts.Contains((t.text ?? "").Trim())) continue;
            var tr = t.transform;
            return tr.parent != null && tr.parent.GetComponent<Button>() != null ? tr.parent : tr;
        }
        return null;
    }

    static Transform? FindByName(StatisticWindow w, string name)
    {
        foreach (var t in w.GetComponentsInChildren<Transform>(true))
            if (t != null && t.name == name) return t;
        return null;
    }

    static void AddButton(StatisticWindow w)
    {
        try
        {
            _window = w;
            if (FindByName(w, ButtonName) != null) return;
            var tab = FindTab(w);
            var style = tab?.GetComponentInChildren<TMP_Text>(true) ?? w.GetComponentInChildren<TMP_Text>(true);
            if (tab == null || style == null) return;
            var tabRt = tab.GetComponent<RectTransform>();
            var right = tabRt.anchoredPosition.x + tabRt.rect.width * (1f - tabRt.pivot.x);
            var btn = UiKit.TextButton(tab.parent, ButtonName, style, "<b>VRS</b>", 18f, tabRt.anchorMin, tabRt.anchorMax, new Vector2(0f, tabRt.pivot.y),
                new Vector2(right + 30f, tabRt.anchoredPosition.y), new Vector2(80f, tabRt.rect.height), () => SetMode(!_vrsMode));
            btn.transform.SetSiblingIndex(tab.GetSiblingIndex() + 1);

            // Region filter and the Major line, shown only in VRS mode.
            var bar = UiKit.Rect(tab.parent, RegionBarName, tabRt.anchorMin, tabRt.anchorMax, new Vector2(0f, tabRt.pivot.y),
                new Vector2(right + 130f, tabRt.anchoredPosition.y), new Vector2(560f, tabRt.rect.height));
            var pt = UiKit.IsPortuguese();
            var options = new (string Label, Region? Region)[]
            {
                (pt ? "Todas" : "All", null), (pt ? "Europa" : "Europe", Region.Europe),
                (pt ? "Américas" : "Americas", Region.Americas), (pt ? "Ásia" : "Asia", Region.Asia),
            };
            for (var i = 0; i < options.Length; i++)
            {
                var (label, region) = options[i];
                UiKit.TextButton(bar, $"Region{i}", style, label, 13f, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(i * 74f, 0f), new Vector2(70f, tabRt.rect.height * 0.8f), () => { _region = region; Rerender(); });
            }
            // Two short lines between the Region buttons and the search box.
            var major = UiKit.Text(bar, MajorLineName, style, "", 12f, Color.white, TextAlignmentOptions.Left,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(305f, 0f), new Vector2(225f, tabRt.rect.height + 6f));
            major.enableWordWrapping = true;
            bar.gameObject.SetActive(_vrsMode);
            Plugin.Log.LogInfo("[VrsUi] VRS tab added to the statistics screen");
        }
        catch (Exception e) { Plugin.Log.LogError($"[VrsUi] could not add the VRS tab: {e}"); }
    }

    static void SetMode(bool on)
    {
        _vrsMode = on;
        var w = _window;
        if (w == null) return;
        FindByName(w, RegionBarName)?.gameObject.SetActive(on);
        var label = FindByName(w, ButtonName)?.GetComponentInChildren<TMP_Text>();
        if (label != null) label.color = on ? new Color(1f, 0.8f, 0.3f) : Color.white;
        SetHeaders(w, on);
        var count = w._teams?._teamsCount;
        if (count != null) count.gameObject.SetActive(!on); // room for the Major line
        Rerender();
    }

    static void Rerender()
    {
        var teams = _window?._teams;
        if (teams == null) return;
        try
        {
            if (_vrsMode) Reorder(teams);
            else teams.ApplyFilter(); // the game's own filter and sort
            UpdateMajorLine();
        }
        catch (Exception e) { Plugin.Log.LogError($"[VrsUi] could not refresh the table: {e}"); }
    }

    // --- Ordering the game's table by VRS ---

    [HarmonyPatch(typeof(StatisticTeams), nameof(StatisticTeams.ApplyFilter))]
    [HarmonyPostfix]
    static void ApplyFilterPostfix(StatisticTeams __instance) { if (_vrsMode && !_reordering) Reorder(__instance); }

    [HarmonyPatch(typeof(StatisticTeams), nameof(StatisticTeams.Sort))]
    [HarmonyPostfix]
    static void SortPostfix(StatisticTeams __instance) { if (_vrsMode && !_reordering) Reorder(__instance); }

    static void Reorder(StatisticTeams view)
    {
        if (_reordering) return;
        _reordering = true;
        try
        {
            var s = Vrs.Current();
            List<DataTeam> ordered;
            if (_region == null) ordered = s.InviteOrder.ToList();
            else ordered = s.ByRegion[_region.Value].Select(x => x.Team).ToList();

            _rank = new Dictionary<string, int>();
            for (var i = 0; i < ordered.Count; i++) _rank[ordered[i].Id] = i + 1;
            _points = s.ByRegion.Values.SelectMany(x => x).ToDictionary(x => x.Team.Id, x => x.Points);

            // Keep the game's context objects (they carry the stats columns), in VRS order, and
            // respect the search box.
            var byId = new Dictionary<string, TeamStatContext>();
            var all = view._all;
            for (var i = 0; all != null && i < all.Count; i++)
                if (all[i]?.Team != null) byId[all[i].Team.Id] = all[i];
            var visible = new Il2CppSystem.Collections.Generic.List<TeamStatContext>();
            foreach (var team in ordered)
                if (byId.TryGetValue(team.Id, out var ctx) && view.MatchesSearch(team)) visible.Add(ctx);
            view._visible = visible;
            view.UpdatePagination();
            view.Refresh();
        }
        finally { _reordering = false; }
    }

    [HarmonyPatch(typeof(StatisticTeamRow), nameof(StatisticTeamRow.Initialize))]
    [HarmonyPostfix]
    static void RowPostfix(StatisticTeamRow __instance, DataTeam team)
    {
        if (!_vrsMode || team == null) return;
        if (__instance._ersPos != null && _rank.TryGetValue(team.Id, out var rank)) __instance._ersPos.text = rank.ToString();
        if (__instance._ers != null && _points.TryGetValue(team.Id, out var pts)) __instance._ers.text = pts.ToString("F0");
    }

    static void SetHeaders(StatisticWindow w, bool vrs)
    {
        var headers = w._teams?._sortHeaders;
        if (headers == null) return;
        foreach (var t in headers.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t == null) continue;
            if (vrs)
            {
                var text = (t.text ?? "").Trim();
                if (text == "# ERS" || text == "ERS")
                {
                    _originalHeaders[t] = t.text!;
                    t.text = text.Replace("ERS", "VRS");
                }
            }
            else if (_originalHeaders.TryGetValue(t, out var original)) t.text = original;
        }
        if (!vrs) _originalHeaders.Clear();
    }

    // --- The next Major ---

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

    static void UpdateMajorLine()
    {
        var w = _window;
        var line = w == null ? null : FindByName(w, MajorLineName)?.GetComponent<TMP_Text>();
        if (line == null) return;
        var pt = UiKit.IsPortuguese();
        var major = NextMajor();
        if (major == null) { line.text = pt ? "Nenhum Major agendado" : "No Major scheduled"; return; }
        var s = Vrs.Current();
        var slots = Math.Max(1, major.ParticipantCountTarget);
        var invited = s.InviteOrder.Take(slots).ToList();
        var split = string.Join(" · ", Enum.GetValues<Region>().Select(r => $"{RegionLabel(r, pt)} {invited.Count(x => Vrs.RegionOf(x) == r)}"));
        var club = GlobalValues.DataTeam;
        var place = club == null ? 0 : s.InviteOrder.ToList().FindIndex(x => x.Id == club.Id) + 1;
        var status = place <= 0 ? "" : place <= slots
            ? $"<color=#5CFF73>{(pt ? "dentro" : "in")} ({place}/{slots})</color>"
            : $"<color=#FF6B5C>{(pt ? "fora" : "out")} ({place}/{slots})</color>";
        line.text = $"<b>{major.Name}</b>: {status}\n{split}";
    }
}
