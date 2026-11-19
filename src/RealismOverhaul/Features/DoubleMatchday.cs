using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Double Match Day: a team playing its second match of the day (possible with overlapping
/// tournaments, see TournamentRegistration) plays it tired.
///
/// The game has no numeric fatigue (PhysicalStatus is categorical), and the physical multiplier is
/// inlined into DataPlayer.CalculatePower. So while the second match is being played (from
/// PrepareSquadsForMatch to EndMatch), that team's players' power is reduced by
/// DoubleMatchdayFatigue.
public static class DoubleMatchday
{
    static readonly HashSet<string> _tiredPlayers = new();
    static string? _tiredMatchKey;
    static ScopedPatch _power = null!;

    public static void Apply(Harmony harmony)
    {
        _power = new ScopedPatch(harmony,
            AccessTools.Method(typeof(DataPlayer), nameof(DataPlayer.CalculatePower), new[] { typeof(float) }),
            AccessTools.Method(typeof(DoubleMatchday), nameof(CalculatePowerPostfix)));
        harmony.PatchAll(typeof(DoubleMatchday));
        Plugin.Log.LogInfo("[DoubleMatchday] patches applied");
    }

    static string Key(DataMatch m) => $"{m.Team1Id}|{m.Team2Id}|{m.TournamentId}|{m.Date:yyyyMMddHHmm}";

    static void CalculatePowerPostfix(DataPlayer __instance, ref float __result)
    {
        if (_tiredPlayers.Count > 0 && _tiredPlayers.Contains(__instance.Id))
            __result *= 1f - Plugin.Flags.DoubleMatchdayFatigue.Value;
    }

    /// True if `teamId` already finished another match today.
    static bool PlayedEarlierToday(string teamId, DataMatch current)
    {
        var today = GlobalValues.TodayMatches;
        if (today == null) return false;
        for (var i = 0; i < today.Count; i++)
        {
            var m = today[i];
            if (m == null || !m.IsMatchOver || Key(m) == Key(current)) continue;
            if (m.Team1Id == teamId || m.Team2Id == teamId) return true;
        }
        return false;
    }

    static void AddSquad(string teamId)
    {
        var team = DataTeams.Get(teamId);
        var squad = team?.MainSquad;
        if (squad == null) return;
        for (var i = 0; i < squad.Count; i++) _tiredPlayers.Add(squad[i]);
    }

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.PrepareSquadsForMatch))]
    [HarmonyPostfix]
    static void PrepareSquadsPostfix(DataMatch match)
    {
        if (match == null || _tiredMatchKey != null) return;
        var tiredTeams = new List<string>();
        foreach (var teamId in new[] { match.Team1Id, match.Team2Id })
            if (!string.IsNullOrEmpty(teamId) && PlayedEarlierToday(teamId, match)) tiredTeams.Add(teamId);
        if (tiredTeams.Count == 0) return;

        foreach (var teamId in tiredTeams) AddSquad(teamId);
        _tiredMatchKey = Key(match);
        _power.Begin();
        Plugin.Log.LogInfo($"[DoubleMatchday] second match today for {string.Join(", ", tiredTeams)} " +
                           $"({match.Team1Id} vs {match.Team2Id}): power -{Plugin.Flags.DoubleMatchdayFatigue.Value:P0}");
    }

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    static void EndMatchPostfix(DataMatch match)
    {
        if (_tiredMatchKey == null || match == null || Key(match) != _tiredMatchKey) return;
        _power.End();
        _tiredPlayers.Clear();
        _tiredMatchKey = null;
        Plugin.Log.LogInfo($"[DoubleMatchday] fatigue cleared after {match.Team1Id} vs {match.Team2Id}");
    }

    // Safety net: never carry fatigue into the next day.
    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.NextDayDate))]
    [HarmonyPrefix]
    static void NextDayDatePrefix()
    {
        if (_tiredMatchKey == null) return;
        _power.End();
        _tiredPlayers.Clear();
        _tiredMatchKey = null;
        Plugin.Log.LogInfo("[DoubleMatchday] fatigue cleared at day change (safety net)");
    }
}
