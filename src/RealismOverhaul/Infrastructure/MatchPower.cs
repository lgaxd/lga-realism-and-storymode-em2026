using HarmonyLib;

namespace RealismOverhaul.Infrastructure;

/// Per-player power multipliers for the match being played.
///
/// The match round generator (MatchAIGenerator.BuildRoundCache / RollWeapon) reads player strength
/// through DataPlayer.CalculatePower, which the AI transfer market also calls constantly. So the
/// multipliers are applied only inside a match window: from PrepareSquadsForMatch (user matches) or
/// ProccesAIMatch (AI matches) until EndMatch. Features register contributors; each returns
/// multipliers for one team's players in that match, and they compose by multiplication.
public static class MatchPower
{
    /// Returns multipliers (playerId -> factor) for `teamId`'s players in `match`, or null.
    public delegate IReadOnlyDictionary<string, float>? Contributor(DataMatch match, DataTeam team);

    static readonly List<(string Name, Contributor Fn)> _contributors = new();
    static readonly Dictionary<string, float> _multipliers = new();
    static string? _activeKey;
    static ScopedPatch _power = null!;
    static bool _applied;

    public static void Register(string name, Contributor contributor) => _contributors.Add((name, contributor));

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _applied = true;
        _power = new ScopedPatch(harmony,
            AccessTools.Method(typeof(DataPlayer), nameof(DataPlayer.CalculatePower), new[] { typeof(float) }),
            AccessTools.Method(typeof(MatchPower), nameof(CalculatePowerPostfix)));
        harmony.PatchAll(typeof(MatchPower));
    }

    public static string Key(DataMatch m) => $"{m.Team1Id}|{m.Team2Id}|{m.TournamentId}|{m.Date.ToString("yyyyMMddHHmm")}";

    static void CalculatePowerPostfix(DataPlayer __instance, ref float __result)
    {
        if (_multipliers.Count > 0 && _multipliers.TryGetValue(__instance.Id, out var m))
            __result *= m;
    }

    static void Begin(DataMatch? match)
    {
        if (match == null || _activeKey != null || _contributors.Count == 0) return;
        foreach (var teamId in new[] { match.Team1Id, match.Team2Id })
        {
            var team = string.IsNullOrEmpty(teamId) ? null : DataTeams.Get(teamId);
            if (team == null) continue;
            foreach (var (name, fn) in _contributors)
            {
                IReadOnlyDictionary<string, float>? result;
                try { result = fn(match, team); }
                catch (Exception e) { Plugin.Log.LogError($"[MatchPower] {name} failed for {teamId}: {e}"); continue; }
                if (result == null) continue;
                foreach (var (playerId, factor) in result)
                    _multipliers[playerId] = (_multipliers.TryGetValue(playerId, out var f) ? f : 1f) * factor;
            }
        }
        _activeKey = Key(match);
        // Inside an AI batch the patch is already installed (nested scope): this costs nothing.
        if (_multipliers.Count > 0) { _power.Begin(); _matchScopeOpen = true; }
    }

    static bool _matchScopeOpen;

    static void End(DataMatch? match = null)
    {
        if (_activeKey == null || match != null && Key(match) != _activeKey) return;
        if (_matchScopeOpen) { _power.End(); _matchScopeOpen = false; }
        _multipliers.Clear();
        _activeKey = null;
    }

    // AI matches are simulated in a batch each day. Installing and removing the CalculatePower patch
    // per match would cost milliseconds each; keep it installed for the whole batch instead.
    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.ProcessAIMatches))]
    [HarmonyPrefix]
    static void ProcessAIMatchesPrefix() { if (_contributors.Count > 0) { _power.Begin(); _batchOpen = true; } }

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.ProcessAIMatches))]
    [HarmonyPostfix]
    static void ProcessAIMatchesPostfix()
    {
        End();
        if (_batchOpen) { _power.End(); _batchOpen = false; }
    }

    static bool _batchOpen;

    // User matches: squads are prepared when the match starts. Normal priority, so features that
    // change the lineup first (Emergency Sub, at higher priority) are seen here.
    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.PrepareSquadsForMatch))]
    [HarmonyPostfix]
    static void PrepareSquadsPostfix(DataMatch match) => Begin(match);

    // AI matches are simulated in one call.
    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.ProccesAIMatch))]
    [HarmonyPrefix]
    static void ProccesAIMatchPrefix(DataMatch match) => Begin(match);

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.ProccesAIMatch))]
    [HarmonyPostfix]
    static void ProccesAIMatchPostfix(DataMatch match) => End(match);

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)] // after features that read the window (e.g. to restore an Emergency Sub)
    static void EndMatchPostfix(DataMatch match) => End(match);

    // Safety net: never carry multipliers into the next day.
    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.NextDayDate))]
    [HarmonyPrefix]
    static void NextDayDatePrefix()
    {
        End();
        if (_batchOpen) { _power.End(); _batchOpen = false; }
    }
}
