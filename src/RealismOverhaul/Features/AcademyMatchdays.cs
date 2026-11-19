using HarmonyLib;

namespace RealismOverhaul.Features;

/// Academy matches stop the calendar like Main Roster matches (see GLOSSARY.md: Academy).
///
/// The calendar stops for "today's match": CalendarController.CheckForEvents asks
/// GlobalValues.TryGetTodayMatch, which returns the first unfinished match of today involving
/// GlobalValues.TeamId (the Main Roster). Academy matches never qualify, so the AI simulates them.
///
/// When the Main Roster has no unfinished match today, also return the Academy's unfinished
/// match. Because finished matches are skipped, a day with both a Main Roster and an Academy
/// match queues them: Main Roster first, then the Academy, each on its own Continue.
public static class AcademyMatchdays
{
    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(AcademyMatchdays));
        Plugin.Log.LogInfo("[AcademyMatch] patches applied");
    }

    static string? _lastLoggedMatch;

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
            var key = $"{m.Team1Id}|{m.Team2Id}|{m.Date:yyyyMMddHHmm}";
            if (key != _lastLoggedMatch)
            {
                _lastLoggedMatch = key;
                Plugin.Log.LogInfo($"[AcademyMatch] today's match is the Academy's: {Describe(m)}");
            }
            match = m;
            __result = true;
            return;
        }
    }

    // --- Result popup: win/loss animation and sound ---
    // WinLossPopup.Init decides "our win" by comparing the winner with GlobalValues.TeamId (IsOurWin
    // is inlined there), so Academy wins played the loss animation. While the popup initialises for
    // an Academy match, make the Academy the active team (SetTeam only sets two fields), then
    // restore the club. Restored by postfix, by finalizer if Init throws, and as a last resort
    // on the next Continue click.
    static DataTeam? _swappedOutClub;

    static void RestoreClub(string where)
    {
        if (_swappedOutClub == null) return;
        GlobalValues.SetTeam(_swappedOutClub);
        Plugin.Log.LogInfo($"[AcademyMatch] active team restored to {_swappedOutClub.Id} ({where})");
        _swappedOutClub = null;
    }

    [HarmonyPatch(typeof(WinLossPopup), nameof(WinLossPopup.Init))]
    [HarmonyPrefix]
    static void WinLossInitPrefix(DataMatch match)
    {
        RestoreClub("before result popup");
        if (!IsAcademyMatch(match)) return;
        var club = GlobalValues.DataTeam;
        var academy = club?.GetAcademyTeam();
        if (club == null || academy == null) return;
        _swappedOutClub = club;
        GlobalValues.SetTeam(academy);
    }

    [HarmonyPatch(typeof(WinLossPopup), nameof(WinLossPopup.Init))]
    [HarmonyPostfix]
    static void WinLossInitPostfix(DataMatch match)
    {
        if (_swappedOutClub != null)
        {
            var academyId = GlobalValues.TeamId;
            var won = (match.IsTeamOneWon ? match.Team1Id : match.Team2Id) == academyId;
            RestoreClub("after result popup");
            Plugin.Log.LogInfo($"[AcademyMatch] result popup shown as {(won ? "WIN" : "LOSS")} for the Academy: {Describe(match)} {match.Team1Score}-{match.Team2Score}");
        }
    }

    [HarmonyPatch(typeof(WinLossPopup), nameof(WinLossPopup.Init))]
    [HarmonyFinalizer]
    static void WinLossInitFinalizer() => RestoreClub("result popup finalizer");

    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.ClickContinue))]
    [HarmonyPrefix]
    static void ClickContinueSafetyNet() => RestoreClub("safety net on Continue");
}
