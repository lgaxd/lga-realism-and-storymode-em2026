using System.Text.Json.Nodes;
using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Map Familiarity, Side Bias and CT Positions (see GLOSSARY.md).
///
/// How the game decides a map: MatchOddsCalculator.BuildPlan computes each team's power once per
/// series, then adjusts each map with MapAdjustment(..., team1, team2, map, mastery1, mastery2),
/// where "mastery" is the team's own map strength (MapTrainingEngine). The map winner is decided
/// there, before any round (and so any CT/T side) is generated. Both effects therefore add points in
/// MapAdjustment, on the game's own scale (MapMasteryPoints):
///   - Map Familiarity: the lineups' average familiarity on that map, mapped onto the game's
///     map-strength range and compared like team mastery, at FamiliarityStrength of its weight;
///   - Side Bias: a lineup leaning CT does better on CT-sided maps, one leaning T on T-sided maps.
///
/// Map Familiarity is per player per map (0-100), saved in the mod's sidecar file. A player seen for
/// the first time starts from his team's strength on each map (picks high, bans low), with a small
/// personal spread. It grows with every map played and with the team's weekly training maps, and
/// fades slowly on maps he neither plays nor trains.
public static class MapFamiliarity
{
    static readonly MapType[] Maps = Enum.GetValues<MapType>();
    static readonly Dictionary<string, float[]> _fam = new();

    // Real CS2 side balance per map: positive = CT-sided, negative = T-sided.
    static readonly Dictionary<MapType, float> Sidedness = new()
    {
        [MapType.Nuke] = 0.6f, [MapType.Ancient] = 0.4f, [MapType.Train] = 0.4f, [MapType.Overpass] = 0.3f,
        [MapType.Inferno] = 0.2f, [MapType.Mirage] = 0f, [MapType.Cache] = 0f,
        [MapType.Dust] = -0.3f, [MapType.Anubis] = -0.5f,
    };

    // CT Positions per map, in the order they are filled by Role (see AssignCtPositions).
    static readonly Dictionary<MapType, string[]> CtPositions = new()
    {
        [MapType.Mirage] = new[] { "AWP", "A-site", "B-site", "Connector", "Mid" },
        [MapType.Inferno] = new[] { "AWP", "B-site", "A-site", "Apartments", "Arch/Mid" },
        [MapType.Nuke] = new[] { "AWP", "B-site (Ramp)", "A-site", "Outside", "Secret" },
        [MapType.Ancient] = new[] { "AWP", "B-site", "A-site", "Donut", "Mid" },
        [MapType.Anubis] = new[] { "AWP", "B-site", "A-site", "Connector", "Mid" },
        [MapType.Dust] = new[] { "AWP", "B-site", "A-site", "Short", "Mid" },
        [MapType.Overpass] = new[] { "AWP", "B-site", "A-site", "Short", "Connector" },
        [MapType.Train] = new[] { "AWP", "B-site", "A-site", "Ivy", "Popdog" },
        [MapType.Cache] = new[] { "AWP", "B-site", "A-site", "Highway", "Mid" },
    };

    public static void Apply(Harmony harmony)
    {
        Sidecar.Apply(harmony);
        Sidecar.Register(new Section());
        harmony.PatchAll(typeof(MapFamiliarity));
        Plugin.Log.LogInfo("[MapFamiliarity] patches applied");
    }

    // --- Data ---

    static int Idx(MapType m) => Array.IndexOf(Maps, m);

    static int StableRoll(string key)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in key) h = (h ^ c) * 16777619u;
            return (int)(h % 100);
        }
    }

    /// The player's familiarity on each map, initialised from his team on first sight.
    static float[] Of(DataPlayer p)
    {
        if (_fam.TryGetValue(p.Id, out var f)) return f;
        f = new float[Maps.Length];
        // Rank the maps within the player's team: its best map starts at 85, its worst at 35. The
        // game's absolute map strength drifts above its starting range over a career, so ranking
        // (not normalising) is what keeps real differences between maps.
        var baselines = Enumerable.Repeat(40f, Maps.Length).ToArray();
        if (!string.IsNullOrEmpty(p.TeamId) && DataTeams.Get(p.TeamId) != null)
        {
            try
            {
                var strengths = Maps.Select(m => MapTrainingEngine.GetEffectiveMapStrength(p.TeamId, m)).ToArray();
                var order = Enumerable.Range(0, Maps.Length).OrderByDescending(i => strengths[i]).ToList();
                for (var rank = 0; rank < order.Count; rank++)
                    baselines[order[rank]] = 85f - rank * (50f / Math.Max(1, order.Count - 1));
            }
            catch { /* team without map data: keep the neutral baseline */ }
        }
        for (var i = 0; i < Maps.Length; i++)
        {
            var spread = StableRoll($"{p.Id}|{Maps[i]}") / 100f * 20f - 10f; // personal +-10
            f[i] = Math.Clamp(baselines[i] + spread, 5f, 95f);
        }
        _fam[p.Id] = f;
        return f;
    }

    public static float Get(DataPlayer p, MapType map) => Of(p)[Idx(map)];

    static void Grow(DataPlayer p, MapType map, float amount)
    {
        var f = Of(p);
        var i = Idx(map);
        f[i] = Math.Clamp(f[i] + amount * (1f - f[i] / 100f), 0f, 100f); // slower near the top
    }

    sealed class Section : Sidecar.ISection
    {
        public string Name => "mapFamiliarity";

        const int SectionVersion = 2; // v1 initialised from absolute map strength and saturated at 95

        public void Load(JsonNode? data)
        {
            _fam.Clear();
            if (data is not JsonObject obj) return;
            if ((obj["_v"]?.GetValue<int>() ?? 1) < SectionVersion)
            {
                Plugin.Log.LogInfo("[MapFamiliarity] stored familiarity is from an older version; rebuilding it");
                return;
            }
            foreach (var (playerId, node) in obj)
            {
                if (playerId == "_v") continue;
                if (node is not JsonObject maps) continue;
                var f = new float[Maps.Length];
                var complete = true;
                for (var i = 0; i < Maps.Length; i++)
                {
                    var v = maps[Maps[i].ToString()];
                    if (v == null) { complete = false; break; }
                    f[i] = v.GetValue<float>();
                }
                if (complete) _fam[playerId] = f; // a map added by a game update re-initialises the player
            }
        }

        public JsonNode? Save()
        {
            var obj = new JsonObject { ["_v"] = SectionVersion };
            foreach (var (playerId, f) in _fam)
            {
                var maps = new JsonObject();
                for (var i = 0; i < Maps.Length; i++) maps[Maps[i].ToString()] = MathF.Round(f[i], 1);
                obj[playerId] = maps;
            }
            return obj;
        }
    }

    // --- Side Bias: from Role (positive = CT) ---

    public static float SideBias(DataPlayer p) => Roles.RolesOf(p).Primary switch
    {
        Role.Anchor => 0.6f, Role.Support => 0.3f, Role.AWPer => 0.2f,
        Role.Lurker => -0.2f, Role.Trader => -0.3f, Role.Entry => -0.6f,
        _ => 0f,
    };

    static string SideLabel(float bias) => bias > 0.1f ? "CT" : bias < -0.1f ? "T" : "CT/T";

    // --- Effect on the map winner ---

    static List<DataPlayer> FiveOf(DataTeam team)
    {
        var result = new List<DataPlayer>();
        var squad = team.MainSquad;
        if (squad == null) return result;
        for (var i = 0; i < squad.Count && result.Count < 5; i++)
        {
            var p = DataPlayers.Get(squad[i]);
            if (p != null) result.Add(p);
        }
        return result;
    }

    static readonly HashSet<string> _logged = new();

    [HarmonyPatch(typeof(MatchOddsCalculator), nameof(MatchOddsCalculator.MapAdjustment))]
    [HarmonyPostfix]
    static void MapAdjustmentPostfix(DataTeam team1, DataTeam team2, MapType map, ref float __result)
    {
        try
        {
            if (team1 == null || team2 == null) return;
            var five1 = FiveOf(team1);
            var five2 = FiveOf(team2);
            if (five1.Count == 0 || five2.Count == 0) return;

            float min = MapTrainingEngine.StartMin, max = MapTrainingEngine.StartMax;
            float Scale(float fam) => min + fam / 100f * (max - min);
            var fam1 = five1.Average(p => Get(p, map));
            var fam2 = five2.Average(p => Get(p, map));
            var famPoints = MatchOddsCalculator.MapMasteryPoints(Scale(fam1), Scale(fam2)) * Plugin.Flags.FamiliarityStrength.Value;

            var maxPoints = MatchOddsCalculator.MapMasteryPoints(max, min);
            var lean1 = five1.Average(SideBias);
            var lean2 = five2.Average(SideBias);
            var sided = Sidedness.TryGetValue(map, out var s) ? s : 0f;
            var sidePoints = (lean1 - lean2) / 2f * sided * maxPoints * Plugin.Flags.SideBiasStrength.Value;

            var before = __result;
            __result += famPoints + sidePoints;

            var mine = DataTeam.IsOwnedByUser(team1) ? team1 : DataTeam.IsOwnedByUser(team2) ? team2 : null;
            if (mine != null && _logged.Add($"{team1.Id}|{team2.Id}|{map}|{GlobalValues.CurrentDate.ToString("yyyyMMdd")}"))
            {
                var mineFive = mine == team1 ? five1 : five2;
                Plugin.Log.LogInfo($"[MapFamiliarity] {map}: {team1.Id} fam {fam1:F0}, lean {lean1:+0.00;-0.00} vs {team2.Id} fam {fam2:F0}, lean {lean2:+0.00;-0.00}; " +
                                   $"map {(sided > 0 ? "CT" : sided < 0 ? "T" : "neutral")}-sided; adjustment {before:F3} -> {__result:F3} (familiarity {famPoints + 0f:+0.000;-0.000;0.000}, side {sidePoints + 0f:+0.000;-0.000;0.000})");
                Plugin.Log.LogInfo($"[MapFamiliarity] {mine.Id} CT setup on {map}: {string.Join(", ", AssignCtPositions(mineFive, map).Select(x => $"{x.Position}={x.Player.Id}"))}");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MapFamiliarity] map adjustment failed: {e}");
        }
    }

    /// CT Positions on a map, assigned by Role: the AWPer takes the AWP spot, Anchors and Supports
    /// hold the sites, Lurkers the mid/connector areas, Entries and Traders the remaining spots.
    public static List<(string Position, DataPlayer Player)> AssignCtPositions(IList<DataPlayer> five, MapType map)
    {
        var positions = CtPositions.TryGetValue(map, out var p) ? p : new[] { "AWP", "A-site", "B-site", "Mid", "Connector" };
        int Preference(Role r, int slot) => (r, slot) switch
        {
            (Role.AWPer, 0) => 0,
            (Role.Anchor, 1 or 2) => 0,
            (Role.Support, 1 or 2) => 1,
            (Role.Lurker, 3 or 4) => 0,
            (Role.Entry or Role.Trader, 3 or 4) => 1,
            _ => 5,
        };
        var free = Enumerable.Range(0, positions.Length).ToList();
        var result = new List<(string, DataPlayer)>();
        // Most constrained Roles first: AWPer, then Anchor, Support, Lurker, Entry/Trader.
        foreach (var player in five.OrderBy(x => Roles.RolesOf(x).Primary switch
                 { Role.AWPer => 0, Role.Anchor => 1, Role.Support => 2, Role.Lurker => 3, _ => 4 }))
        {
            if (free.Count == 0) break;
            var role = Roles.RolesOf(player).Primary;
            var slot = free.OrderBy(s => Preference(role, s)).First();
            free.Remove(slot);
            result.Add((positions[slot], player));
        }
        return result.OrderBy(x => Array.IndexOf(positions, x.Item1)).ToList();
    }

    // --- Growth and decay ---

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    static void EndMatchPostfix(DataMatch match)
    {
        try
        {
            var maps = match?.MapStats;
            if (maps == null) return;
            for (var i = 0; i < maps.Count; i++)
            {
                var rec = maps[i];
                var players = rec?._playerStats;
                if (players == null) continue;
                foreach (var key in players.Keys)
                {
                    var p = DataPlayers.Get(key);
                    if (p != null) Grow(p, rec!.Map, 3f);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MapFamiliarity] could not record maps played: {e}");
        }
    }

    /// Weekly: training maps grow, untouched maps fade (only for players who have been seen, i.e.
    /// played or were shown; the rest are initialised from their team when first needed).
    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.NextDayDate))]
    [HarmonyPostfix]
    static void NextDayDatePostfix()
    {
        if (GlobalValues.CurrentDate.DayOfWeek != Il2CppSystem.DayOfWeek.Monday || _fam.Count == 0) return;
        try
        {
            var trainedByTeam = new Dictionary<string, HashSet<MapType>>();
            foreach (var playerId in _fam.Keys.ToList())
            {
                var p = DataPlayers.Get(playerId);
                if (p == null) continue;
                HashSet<MapType>? trained = null;
                if (!string.IsNullOrEmpty(p.TeamId) && !trainedByTeam.TryGetValue(p.TeamId, out trained))
                {
                    trained = new HashSet<MapType>();
                    var team = DataTeams.Get(p.TeamId);
                    var list = team == null ? null : MapTrainingEngine.GetEffectiveTrainingMaps(team);
                    if (list != null) for (var i = 0; i < list.Count; i++) trained.Add(list[i]);
                    trainedByTeam[p.TeamId] = trained;
                }
                var f = _fam[playerId];
                for (var i = 0; i < Maps.Length; i++)
                {
                    if (trained != null && trained.Contains(Maps[i])) Grow(p, Maps[i], 1.5f);
                    else if (f[i] > 20f) f[i] = Math.Max(20f, f[i] - 0.7f);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MapFamiliarity] weekly update failed: {e}");
        }
    }

    // --- UI: Map Familiarity and Side Bias at the end of the profile biography ---

    [HarmonyPatch(typeof(PlayerOverview), nameof(PlayerOverview.Renew))]
    [HarmonyPostfix]
    static void PlayerOverviewRenewPostfix(PlayerOverview __instance, DataUser user)
    {
        try
        {
            var p = user?.TryCast<DataPlayer>();
            var bio = __instance._biography;
            if (p == null || bio == null || p.Retired) return;
            const string marker = "\n\n<b>";
            var text = bio.text ?? "";
            var cut = text.IndexOf(marker + "Mapas", StringComparison.Ordinal);
            if (cut < 0) cut = text.IndexOf(marker + "Maps", StringComparison.Ordinal);
            if (cut >= 0) text = text[..cut];

            var pt = (UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en").StartsWith("pt");
            var maps = string.Join("  ", Maps.Select(m => (Map: m, Fam: Get(p, m)))
                .OrderByDescending(x => x.Fam).Select(x => $"{x.Map} {x.Fam:F0}"));
            var side = SideLabel(SideBias(p));
            bio.text = text + $"{marker}{(pt ? "Mapas" : "Maps")}</b>: {maps}\n<b>{(pt ? "Lado forte" : "Stronger side")}</b>: {side}";
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[MapFamiliarity] could not show familiarity: {e}");
        }
    }
}
