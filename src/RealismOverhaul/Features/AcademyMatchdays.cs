using HarmonyLib;

namespace RealismOverhaul.Features;

/// Academy matches stop the calendar like Main Roster matches (see GLOSSARY.md: Academy).
///
/// The calendar stops for "today's match": CalendarController.CheckForEvents asks
/// GlobalValues.TryGetTodayMatch, which returns the first unfinished match of today involving
/// GlobalValues.TeamId (the Main Roster). Academy matches never qualify, so the AI simulates them.
///
/// EXPERIMENT (step 1): when the Main Roster has no unfinished match today, also return the
/// Academy's unfinished match, and trace how the match flow handles it. The match screen may
/// assume "our side" is the Main Roster; this step is to find out.
public static class AcademyMatchdays
{
    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(AcademyMatchdays));
        Plugin.Log.LogInfo("[AcademyMatch] EXPERIMENT patches applied");
    }

    static string? AcademyId() => GlobalValues.DataTeam?.GetAcademyTeam()?.Id;

    static bool Involves(DataMatch m, string teamId) => m.Team1Id == teamId || m.Team2Id == teamId;

    static string Describe(DataMatch? m) =>
        m == null ? "<null>" : $"{m.Team1Id} vs {m.Team2Id} (tournament {m.TournamentId}, {m.Date:yyyy-MM-dd HH:mm}, over={m.IsMatchOver})";

    static bool IsAcademyMatch(DataMatch? m)
    {
        var academyId = AcademyId();
        return m != null && academyId != null && Involves(m, academyId);
    }

    [HarmonyPatch(typeof(GlobalValues), nameof(GlobalValues.TryGetTodayMatch))]
    [HarmonyPostfix]
    static void TryGetTodayMatchPostfix(ref DataMatch match, ref bool __result)
    {
        if (__result) return;
        var academyId = AcademyId();
        var today = GlobalValues.TodayMatches;
        if (academyId == null || today == null) return;
        for (var i = 0; i < today.Count; i++)
        {
            var m = today[i];
            if (m == null || m.IsMatchOver || !Involves(m, academyId)) continue;
            Plugin.Log.LogInfo($"[AcademyMatch] today's match is the Academy's: {Describe(m)}");
            match = m;
            __result = true;
            return;
        }
    }

    // --- Tracing the match flow (step 1 only) ---

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.PrepareSquadsForMatch))]
    [HarmonyPostfix]
    static void PrepareSquadsTrace(DataMatch match)
    {
        if (IsAcademyMatch(match)) Plugin.Log.LogInfo($"[AcademyMatch][trace] PrepareSquadsForMatch {Describe(match)}");
    }

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.IsOurMatch))]
    [HarmonyPostfix]
    static void IsOurMatchTrace(DataMatch match, bool __result)
    {
        if (IsAcademyMatch(match)) Plugin.Log.LogInfo($"[AcademyMatch][trace] IsOurMatch={__result} for {Describe(match)}");
    }

    [HarmonyPatch(typeof(DayOutcome), nameof(DayOutcome.StartMatch))]
    [HarmonyPostfix]
    static void StartMatchTrace() =>
        Plugin.Log.LogInfo($"[AcademyMatch][trace] DayOutcome.StartMatch (CurrentMatch: {Describe(GlobalValues.CurrentMatch)})");

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    static void EndMatchTrace(DataMatch match, bool givenAway)
    {
        if (IsAcademyMatch(match))
            Plugin.Log.LogInfo($"[AcademyMatch][trace] EndMatch {Describe(match)} score {match.Team1Score}-{match.Team2Score} givenAway={givenAway}");
    }

    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.OnMatchEnded))]
    [HarmonyPostfix]
    static void OnMatchEndedTrace() =>
        Plugin.Log.LogInfo($"[AcademyMatch][trace] CalendarController.OnMatchEnded (CurrentMatch: {Describe(GlobalValues.CurrentMatch)})");
}
