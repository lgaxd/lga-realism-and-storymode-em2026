using HarmonyLib;
using ERegion = NationalityBarrier.ERegion;

namespace RealismOverhaul.Features;

/// One of the smaller areas Regional Tournaments are restricted to (see GLOSSARY.md: Subregion).
public enum Subregion { Europe, CIS, NorthAmerica, SouthAmerica, Asia, Oceania, MENA }

/// One of Valve's three competitive Regions, used by VRS (see GLOSSARY.md: Region).
public enum Region { Europe, Americas, Asia }

/// Subregions and Regional Tournaments (see GLOSSARY.md: Subregion, Region, Regional Tournament).
///
/// The game already maps nationalities to regions (NationalityBarrier.GetRegion: CN, CIS, UA, EU,
/// NA, BR, SA, OCE, ASIA, MENA) for transfer resistance; Subregions group those. A team's
/// Subregion is the majority nationality of its five starters (the org's country breaks ties),
/// as in Valve's rules.
///
/// The game has no regional tournaments, so they are classified by rule: a tournament whose name
/// names a Subregion is restricted to it; an Online T3/T4 tournament is restricted to its host
/// country's Subregion; LANs and T1/T2/Majors stay open. Restriction applies where the AI fills a
/// tournament (TournamentEngine.GetAvailableTeams, inside FillRegisteredList*), and to invites and
/// registration for the user's teams (IsTeamInviteEligible, CanParticipateInTournament). If a
/// Subregion cannot fill the field, teams from the same Region complete it.
public static class Regions
{
    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(Regions));
        Plugin.Log.LogInfo("[Regions] patches applied");
    }

    // --- Mapping ---

    public static Subregion? SubregionOfCountry(string? country)
    {
        if (string.IsNullOrEmpty(country)) return null;
        ERegion r;
        try { r = NationalityBarrier.GetRegion(country); }
        catch { return null; }
        return r switch
        {
            ERegion.EU => Subregion.Europe,
            ERegion.CIS or ERegion.UA => Subregion.CIS,
            ERegion.NA => Subregion.NorthAmerica,
            ERegion.SA or ERegion.BR => Subregion.SouthAmerica,
            ERegion.ASIA or ERegion.CN => Subregion.Asia,
            ERegion.OCE => Subregion.Oceania,
            ERegion.MENA => Subregion.MENA,
            _ => null,
        };
    }

    public static Region RegionOf(Subregion s) => s switch
    {
        Subregion.NorthAmerica or Subregion.SouthAmerica => Region.Americas,
        Subregion.Asia or Subregion.Oceania => Region.Asia,
        _ => Region.Europe, // Europe, CIS, MENA
    };

    /// Majority nationality of the five starters; the org's country breaks ties.
    public static Subregion? SubregionOf(DataTeam team)
    {
        var counts = new Dictionary<Subregion, int>();
        var squad = team.MainSquad;
        if (squad != null)
            for (var i = 0; i < squad.Count && i < 5; i++)
            {
                var s = SubregionOfCountry(DataPlayers.Get(squad[i])?.Country);
                if (s != null) counts[s.Value] = counts.TryGetValue(s.Value, out var n) ? n + 1 : 1;
            }
        var org = SubregionOfCountry(team.Country);
        if (counts.Count == 0) return org;
        var top = counts.Max(kv => kv.Value);
        var leaders = counts.Where(kv => kv.Value == top).Select(kv => kv.Key).ToList();
        return leaders.Count == 1 || org == null || !leaders.Contains(org.Value) ? leaders[0] : org;
    }

    // --- Classifying tournaments ---

    static readonly (string Keyword, Subregion Sub)[] NameKeywords =
    {
        ("South America", Subregion.SouthAmerica), ("Brazil", Subregion.SouthAmerica), ("Brasil", Subregion.SouthAmerica),
        ("LATAM", Subregion.SouthAmerica), ("North America", Subregion.NorthAmerica), ("NA", Subregion.NorthAmerica),
        ("Oceania", Subregion.Oceania), ("Australia", Subregion.Oceania), ("MENA", Subregion.MENA),
        ("Middle East", Subregion.MENA), ("CIS", Subregion.CIS), ("Asia", Subregion.Asia), ("China", Subregion.Asia),
        ("Europe", Subregion.Europe), ("EU", Subregion.Europe),
    };

    static readonly Dictionary<string, Subregion?> _classified = new();

    // Share of lower-tier online events per Subregion, roughly as in the real scene.
    static readonly (Subregion Sub, int Weight)[] OnlineWeights =
    {
        (Subregion.Europe, 45), (Subregion.CIS, 15), (Subregion.SouthAmerica, 14), (Subregion.NorthAmerica, 9),
        (Subregion.Asia, 9), (Subregion.Oceania, 4), (Subregion.MENA, 4),
    };

    static Subregion WeightedSubregion(string tournamentId)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in tournamentId) h = (h ^ c) * 16777619u;
            var roll = (int)(h % 100);
            foreach (var (sub, weight) in OnlineWeights)
            {
                if (roll < weight) return sub;
                roll -= weight;
            }
            return Subregion.Europe;
        }
    }

    /// The Subregion a tournament is restricted to, or null when it is open to everyone.
    public static Subregion? RestrictionOf(DataTournament t)
    {
        if (_classified.TryGetValue(t.Id, out var cached)) return cached;
        Subregion? result = null;
        string why = "open";
        var name = t.Name ?? "";
        foreach (var (keyword, sub) in NameKeywords)
            // Whole words only: "CIS" must not match "Precision", nor "NA" "Nations".
            if (System.Text.RegularExpressions.Regex.IsMatch(name, $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b",
                    keyword.Length <= 4 ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                result = sub;
                why = $"name mentions '{keyword}'";
                break;
            }
        if (result == null && !t.IsLan && (t.Tier == TournamentType.T3 || t.Tier == TournamentType.T4))
        {
            result = SubregionOfCountry(t.Country);
            if (result != null) why = $"Online {t.Tier} hosted in {t.Country}";
            else
            {
                // Online events have no host country in the game. Real lower-tier online events are
                // almost all regional, so give each one a Subregion, stable per tournament, drawn
                // with the real scene's weights.
                result = WeightedSubregion(t.Id);
                why = $"Online {t.Tier} without a host country, assigned";
            }
        }
        _classified[t.Id] = result;
        Plugin.Log.LogInfo($"[Regions] '{t.Name}' ({t.Tier}, {(t.IsLan ? "LAN" : "Online")}, {t.Country}): " +
                           (result == null ? "open to all" : $"Regional Tournament for {result} ({why})"));
        return result;
    }

    // --- Restricting who plays ---

    static DataTournament? _filling;

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.FillRegisteredList))]
    [HarmonyPrefix]
    static void FillPrefix(DataTournament tournament, out bool __state) => __state = BeginFill(tournament);

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.FillRegisteredList))]
    [HarmonyPostfix]
    static void FillPostfix(bool __state) { if (__state) _filling = null; }

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.FillRegisteredListWithQualifications))]
    [HarmonyPrefix]
    static void FillQualPrefix(DataTournament tournament, out bool __state) => __state = BeginFill(tournament);

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.FillRegisteredListWithQualifications))]
    [HarmonyPostfix]
    static void FillQualPostfix(bool __state) { if (__state) _filling = null; }

    static bool BeginFill(DataTournament? t)
    {
        if (_filling != null || t == null) return false;
        _filling = t;
        return true;
    }

    /// While a Regional Tournament is being filled, keep only its Subregion's teams, completed by
    /// same-Region teams if the Subregion alone cannot fill the field. Order is preserved, so the
    /// game's own ranking-based picking still applies within the allowed teams.
    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.GetAvailableTeams))]
    [HarmonyPostfix]
    static void GetAvailableTeamsPostfix(ref Il2CppSystem.Collections.Generic.List<DataTeam> __result)
    {
        var t = _filling;
        if (t == null || __result == null) return;
        var sub = RestrictionOf(t);
        if (sub == null) return;
        try
        {
            var inSub = new List<DataTeam>();
            var inRegion = new List<DataTeam>();
            for (var i = 0; i < __result.Count; i++)
            {
                var team = __result[i];
                var ts = team == null ? null : SubregionOf(team);
                if (ts == sub) inSub.Add(team!);
                else if (ts != null && RegionOf(ts.Value) == RegionOf(sub.Value)) inRegion.Add(team!);
            }
            var target = t.ParticipantCountTarget;
            var filtered = new Il2CppSystem.Collections.Generic.List<DataTeam>();
            foreach (var team in inSub) filtered.Add(team);
            var borrowed = 0;
            foreach (var team in inRegion)
            {
                if (filtered.Count >= target) break;
                filtered.Add(team);
                borrowed++;
            }
            _originalAvailable = __result.Count;
            Plugin.Log.LogInfo($"[Regions] filling '{t.Name}' ({sub}): {inSub.Count} {sub} teams" +
                               (borrowed > 0 ? $" + {borrowed} from the {RegionOf(sub.Value)} region" : "") +
                               $" of {__result.Count} available (target {target})");
            __result = filtered;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[Regions] could not filter teams for '{t.Name}': {e}");
        }
    }

    static int _originalAvailable;

    /// The game picks a tournament's teams by skipping the top `skip` of the ranked list, so a T3
    /// gets teams ranked below the elite. That skip is sized for the whole world; on a Subregion's
    /// much shorter list it would skip everyone and leave the tournament empty. Scale it to the
    /// filtered list, keeping room for the target count.
    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.PickTeamsWithoutOrgDuplicates))]
    [HarmonyPrefix]
    static void PickTeamsPrefix(Il2CppSystem.Collections.Generic.List<DataTeam> availableTeams, ref int skip, int targetCount)
    {
        var t = _filling;
        if (t == null || availableTeams == null || RestrictionOf(t) == null || _originalAvailable <= 0) return;
        var count = availableTeams.Count;
        if (count >= _originalAvailable) return;
        var scaled = (int)Math.Round(skip * (double)count / _originalAvailable);
        scaled = Math.Max(0, Math.Min(scaled, count - targetCount));
        if (scaled != skip)
            Plugin.Log.LogInfo($"[Regions] '{t.Name}': skipping the top {scaled} of {count} regional teams (was {skip} of {_originalAvailable} worldwide)");
        skip = scaled;
    }

    // --- UI: mark Regional Tournaments in the tournament window ---

    static string SubregionLabel(Subregion s, bool pt) => s switch
    {
        Subregion.NorthAmerica => pt ? "América do Norte" : "North America",
        Subregion.SouthAmerica => pt ? "América do Sul" : "South America",
        Subregion.Europe => pt ? "Europa" : "Europe",
        Subregion.Asia => pt ? "Ásia" : "Asia",
        Subregion.Oceania => pt ? "Oceania" : "Oceania",
        _ => s.ToString(),
    };

    [HarmonyPatch(typeof(TournamentOverview), nameof(TournamentOverview.Renew))]
    [HarmonyPostfix]
    static void TournamentOverviewRenewPostfix(TournamentOverview __instance)
    {
        try
        {
            var t = __instance._tournament;
            var sub = t == null ? null : RestrictionOf(t);
            if (sub == null) return;
            var pt = Infrastructure.UiKit.IsPortuguese();
            var label = SubregionLabel(sub.Value, pt);
            var lan = __instance._lan;
            if (lan != null && !(lan.text ?? "").Contains("Regional"))
                lan.text = $"{lan.text} · Regional: {label}";
            var place = __instance._place;
            if (place != null && !t!.IsLan && (string.IsNullOrWhiteSpace(place.text) || place.text.Trim() == "-"))
                place.text = label;
        }
        catch (Exception e) { Plugin.Log.LogError($"[Regions] could not label the tournament window: {e}"); }
    }

    /// The user's teams: invited to (and may register for) a Regional Tournament only if they are
    /// from its Subregion.
    static bool Allowed(DataTournament? t, DataTeam? team)
    {
        if (t == null || team == null) return true;
        var sub = RestrictionOf(t);
        return sub == null || SubregionOf(team) == sub;
    }

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.IsTeamInviteEligible))]
    [HarmonyPostfix]
    static void IsTeamInviteEligiblePostfix(DataTournament tournament, DataTeam team, ref bool __result)
    {
        if (__result && !Allowed(tournament, team)) __result = false;
    }

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.CanParticipateInTournament))]
    [HarmonyPostfix]
    static void CanParticipatePostfix(DataTournament tournament, DataTeam team, ref bool __result)
    {
        if (__result && !Allowed(tournament, team)) __result = false;
    }
}
