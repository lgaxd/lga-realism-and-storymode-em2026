using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Double Match Day: a team playing its second match of the day (possible with overlapping
/// tournaments, see TournamentRegistration) plays it tired (see GLOSSARY.md: Double Match Day).
///
/// The game has no numeric fatigue (PhysicalStatus is categorical), so the second match's players
/// get a power penalty of DoubleMatchdayFatigue through MatchPower.
public static class DoubleMatchday
{
    public static void Apply(Harmony harmony)
    {
        MatchPower.Apply(harmony);
        MatchPower.Register("DoubleMatchday", Contribute);
        Plugin.Log.LogInfo("[DoubleMatchday] registered");
    }

    static IReadOnlyDictionary<string, float>? Contribute(DataMatch match, DataTeam team)
    {
        if (!PlayedEarlierToday(team.Id, match)) return null;
        var squad = team.MainSquad;
        if (squad == null) return null;
        var factor = 1f - Plugin.Flags.DoubleMatchdayFatigue.Value;
        var result = new Dictionary<string, float>();
        for (var i = 0; i < squad.Count; i++) result[squad[i]] = factor;
        Plugin.Log.LogInfo($"[DoubleMatchday] second match today for {team.Id} ({match.Team1Id} vs {match.Team2Id}): power -{Plugin.Flags.DoubleMatchdayFatigue.Value:P0}");
        return result;
    }

    /// True if `teamId` already finished another match today.
    static bool PlayedEarlierToday(string teamId, DataMatch current)
    {
        var today = GlobalValues.TodayMatches;
        if (today == null) return false;
        var key = MatchPower.Key(current);
        for (var i = 0; i < today.Count; i++)
        {
            var m = today[i];
            if (m == null || !m.IsMatchOver || MatchPower.Key(m) == key) continue;
            if (m.Team1Id == teamId || m.Team2Id == teamId) return true;
        }
        return false;
    }
}
