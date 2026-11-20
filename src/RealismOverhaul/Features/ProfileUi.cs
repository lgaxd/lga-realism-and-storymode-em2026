using HarmonyLib;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealismOverhaul.Features;

/// Player profile UI (Attributes tab):
///   - a Role selector under "Player roles", cloned from the game's role dropdown: "Auto (X)" keeps
///     the derived Role, or pick one of the six;
///   - a "Maps / Radar" toggle on the radar panel: Maps shows one bar per map (Map Familiarity,
///     coloured by level) and the stronger side, in the radar's place.
/// Layout paths come from the UI dump of PlayerOverview.
public static class ProfileUi
{
    const string RoleDropdownName = "RealismOverhaul.RoleDropdown";
    const string MapsPanelName = "RealismOverhaul.MapsPanel";
    const string ToggleName = "RealismOverhaul.MapsToggle";
    const float RoleRowHeight = 40f;

    static readonly Role[] RoleOrder = { Role.Entry, Role.Trader, Role.Support, Role.Lurker, Role.AWPer, Role.Anchor };
    static readonly MapType[] Maps = Enum.GetValues<MapType>();

    static bool _showMaps;
    static DataPlayer? _player;
    static PlayerOverview? _window;

    /// True once the Maps panel has been built, so the biography no longer needs the bars.
    public static bool MapsPanelReady { get; private set; }

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(ProfileUi));
        Plugin.Log.LogInfo("[ProfileUi] patches applied");
    }

    [HarmonyPatch(typeof(PlayerOverview), nameof(PlayerOverview.Renew))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)] // after Roles/MapFamiliarity have rendered their text
    static void RenewPostfix(PlayerOverview __instance, DataUser user)
    {
        _window = __instance;
        _player = user?.TryCast<DataPlayer>();
        try { BuildRoleSelector(__instance); }
        catch (Exception e) { Plugin.Log.LogError($"[ProfileUi] role selector failed: {e}"); }
        try { BuildMapsPanel(__instance); }
        catch (Exception e) { Plugin.Log.LogError($"[ProfileUi] maps panel failed: {e}"); }
    }

    // --- Role selector ---

    static void BuildRoleSelector(PlayerOverview w)
    {
        var roles = w.transform.Find("Container/Other/Main/Info/Roles");
        var template = roles?.Find("MainRole")?.GetComponent<TMP_Dropdown>();
        if (roles == null || template == null) return;

        var existing = roles.Find(RoleDropdownName);
        var p = _player;
        if (p == null || p.Retired)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }

        var derived = Roles.DerivedRolesOf(p).Primary;
        var options = new List<string> { $"Auto ({derived})" };
        options.AddRange(RoleOrder.Select(r => r.ToString()));
        var value = ManualChoices.TryGetRoles(p.Id, out var chosen) ? Array.IndexOf(RoleOrder, chosen.Primary) + 1 : 0;

        if (existing == null)
        {
            // One more row in the Roles block; the Info column's layout moves the rest down.
            var rt = roles.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + RoleRowHeight);
            var dd = UiKit.Dropdown(template, roles, RoleDropdownName, options, value,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(15f, -12f - RoleRowHeight / 2f), new Vector2(200f, 34f), OnRolePicked);
            var main = template.GetComponent<RectTransform>();
            main.anchoredPosition = new Vector2(main.anchoredPosition.x, main.anchoredPosition.y + RoleRowHeight / 2f);
            var secondary = roles.Find("SecondaryRole")?.GetComponent<RectTransform>();
            if (secondary != null) secondary.anchoredPosition = new Vector2(secondary.anchoredPosition.x, secondary.anchoredPosition.y + RoleRowHeight / 2f);
            var info = roles.parent.GetComponent<RectTransform>();
            if (info != null) LayoutRebuilder.ForceRebuildLayoutImmediate(info);
            Plugin.Log.LogInfo("[ProfileUi] role selector created");
            existing = dd.transform;
        }
        else
        {
            existing.gameObject.SetActive(true);
            UiKit.SetOptions(existing.GetComponent<TMP_Dropdown>(), options, value);
        }
    }

    static void OnRolePicked(int index)
    {
        var p = _player;
        if (p == null) return;
        if (index <= 0) ManualChoices.ClearRoles(p.Id);
        else
        {
            var primary = RoleOrder[index - 1];
            var derived = Roles.DerivedRolesOf(p);
            var secondary = derived.Primary != primary ? derived.Primary : derived.Secondary;
            ManualChoices.SetRoles(p.Id, primary, secondary);
        }
        if (_window != null) Roles.RenderRoleText(_window, p);
        if (_window != null) BuildMapsPanel(_window); // the stronger side follows the Role
    }

    // --- Maps panel ---

    static void BuildMapsPanel(PlayerOverview w)
    {
        var cont = w.transform.Find("Container/Other/Main/RadarCont");
        var radar = cont?.Find("Radar");
        var style = radar?.Find("ATK")?.GetComponent<TMP_Text>();
        if (cont == null || radar == null || style == null) return;

        var p = _player;
        var toggle = cont.Find(ToggleName);
        var panel = cont.Find(MapsPanelName);
        if (p == null || p.Retired || !Plugin.Flags.MapFamiliarity.Value)
        {
            if (toggle != null) toggle.gameObject.SetActive(false);
            if (panel != null) panel.gameObject.SetActive(false);
            radar.gameObject.SetActive(true);
            return;
        }

        var pt = UiKit.IsPortuguese();
        if (toggle == null)
            toggle = UiKit.TextButton(cont, ToggleName, style, "", 15f, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-6f, -2f), new Vector2(84f, 26f), () =>
                {
                    _showMaps = !_showMaps;
                    if (_window != null) BuildMapsPanel(_window);
                }).transform;
        toggle.gameObject.SetActive(true);
        toggle.GetComponentInChildren<TMP_Text>().text = _showMaps ? "Radar" : (pt ? "Mapas" : "Maps");

        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        radar.gameObject.SetActive(!_showMaps);
        if (!_showMaps) return;

        var root = UiKit.Rect(cont, MapsPanelName, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var side = MapFamiliarity.SideBias(p);
        var sideLabel = side > 0.1f ? "CT" : side < -0.1f ? "T" : "CT/T";
        UiKit.Text(root, "Side", style, $"{(pt ? "Lado forte" : "Stronger side")}: <b>{sideLabel}</b>", 15f, Color.white,
            TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -4f), new Vector2(240f, 22f));

        const float top = -34f, rowH = 28f;
        var rows = Maps.Select(m => (Map: m, Fam: MapFamiliarity.Get(p, m))).OrderByDescending(x => x.Fam).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var (map, fam) = rows[i];
            var y = top - i * rowH;
            var color = fam >= 70 ? new Color(0.30f, 0.85f, 0.39f) : fam >= 45 ? new Color(1f, 0.8f, 0f) : new Color(1f, 0.35f, 0.31f);
            UiKit.Text(root, $"Name{i}", style, map == MapType.Dust ? "Dust2" : map.ToString(), 15f, Color.white, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, y), new Vector2(86f, rowH));
            UiKit.Image(root, $"BarBg{i}", new Color(1f, 1f, 1f, 0.12f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(102f, y - rowH / 2f), new Vector2(190f, 12f));
            UiKit.Image(root, $"Bar{i}", color, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(102f, y - rowH / 2f), new Vector2(190f * Math.Clamp(fam / 100f, 0f, 1f), 12f));
            UiKit.Text(root, $"Value{i}", style, $"{fam:F0}", 15f, Color.white, TextAlignmentOptions.Right,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(298f, y), new Vector2(40f, rowH));
        }
        MapsPanelReady = true;
    }
}
