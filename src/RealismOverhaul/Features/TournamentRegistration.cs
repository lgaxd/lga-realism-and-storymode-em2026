using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Tournament registration while committed to another tournament (see GLOSSARY.md:
/// Tournament Period, Match Day, LAN / Online).
///
/// The game refuses any registration while the team is playing, or registered for, any other
/// active tournament, regardless of dates (TournamentEngine.TryRegisterPlayer:
/// HasActiveRegistrations → NeedsConfirmation, IsTeamActiveInAnyTournament → Refused).
///
/// A commitment only blocks the candidate tournament when their Tournament Periods overlap, and
/// (with OverlappingTournaments) only if that would put the team in more than 2 tournaments, or
/// 2 LANs, on the same day. Same-day matches queue through the calendar (see AcademyMatchdays);
/// the second carries a fatigue penalty (see DoubleMatchday).
///
/// Both game checks are hot and used by transfers and the AI, so they are changed only while a
/// registration method runs, and only relative to the tournament being registered for.
public static class TournamentRegistration
{
    static DataTournament? _candidate;
    static readonly HashSet<string> _logged = new();

    /// Logs once per distinct message: the game re-evaluates registration every UI refresh.
    static void LogOnce(string message)
    {
        if (_logged.Add(message)) Plugin.Log.LogInfo(message);
    }
    static ScopedPatch _activeCheck = null!;
    static ScopedPatch _registrationsCheck = null!;

    public static void Apply(Harmony harmony)
    {
        _activeCheck = new ScopedPatch(harmony,
            AccessTools.Method(typeof(DataTournaments), nameof(DataTournaments.IsTeamActiveInAnyTournament)),
            AccessTools.Method(typeof(TournamentRegistration), nameof(IsTeamActiveInAnyTournamentPostfix)));
        _registrationsCheck = new ScopedPatch(harmony,
            AccessTools.Method(typeof(TournamentEngine), nameof(TournamentEngine.HasActiveRegistrations)),
            AccessTools.Method(typeof(TournamentRegistration), nameof(HasActiveRegistrationsPostfix)));
        harmony.PatchAll(typeof(TournamentRegistration));
        Plugin.Log.LogInfo("[Registration] patches applied");
    }

    static bool PeriodsOverlap(DataTournament a, DataTournament b) =>
        a.DateStart.Date <= b.DateEnd.Date && b.DateStart.Date <= a.DateEnd.Date;

    public const int MaxSimultaneousTournaments = 2;

    /// The commitments of `teamId` (playing, or registered for one not yet started) whose
    /// Tournament Period overlaps `candidate`.
    static List<DataTournament> OverlappingCommitments(DataTournament candidate, string teamId)
    {
        var result = new List<DataTournament>();
        var today = GlobalValues.CurrentDate.Date;
        var active = DataTournaments.ActiveTournaments;
        if (active == null) return result;
        var count = active.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataTournament>>().Count;
        for (var i = 0; i < count; i++)
        {
            var t = active[i];
            if (t == null || t.Id == candidate.Id || t.Participants == null) continue;
            if (!t.Participants.TryGetValue(teamId, out var state) || state == null) continue;
            var committed = state.IsActive || t.DateStart.Date > today;
            if (committed && PeriodsOverlap(t, candidate)) result.Add(t);
        }
        return result;
    }

    /// Why `teamId` may not register for `candidate`, or null if it may.
    /// Non-overlapping commitments never block. With OverlappingTournaments on, an overlap is
    /// fine as long as, on every day of the candidate's period, the team would be in at most
    /// 2 tournaments and at most 1 LAN (a team is in one place at a time; 2 tournaments means at
    /// most 2 matches a day, played in sequence).
    public static string? FindConflict(DataTournament candidate, string teamId)
    {
        var overlapping = OverlappingCommitments(candidate, teamId);
        if (overlapping.Count == 0) return null;
        if (!Plugin.Flags.OverlappingTournaments.Value)
            return $"overlaps {Describe(overlapping[0])}";

        for (var day = candidate.DateStart.Date; day <= candidate.DateEnd.Date; day = day.AddDays(1))
        {
            int tournaments = 1, lans = candidate.IsLan ? 1 : 0;
            foreach (var t in overlapping)
            {
                if (day < t.DateStart.Date || day > t.DateEnd.Date) continue;
                tournaments++;
                if (t.IsLan) lans++;
                if (lans > 1) return $"two LANs at once on {day:yyyy-MM-dd} ({Describe(t)})";
                if (tournaments > MaxSimultaneousTournaments)
                    return $"more than {MaxSimultaneousTournaments} tournaments at once on {day:yyyy-MM-dd}";
            }
        }
        return null;
    }

    static void IsTeamActiveInAnyTournamentPostfix(string teamId, ref bool __result)
    {
        if (!__result || _candidate == null || string.IsNullOrEmpty(teamId)) return;
        var conflict = FindConflict(_candidate, teamId);
        if (conflict == null)
        {
            LogOnce($"[Registration] {teamId} is busy elsewhere, but may also play {Describe(_candidate)}: allowing");
            __result = false;
        }
        else
            LogOnce($"[Registration] {teamId} blocked from {Describe(_candidate)}: {conflict}");
    }

    static void HasActiveRegistrationsPostfix(DataTeam team, ref bool __result)
    {
        if (!__result || _candidate == null || team == null) return;
        if (FindConflict(_candidate, team.Id) == null)
            __result = false;
    }

    static string Describe(DataTournament t) =>
        $"'{t.Name}' ({t.DateStart:yyyy-MM-dd}..{t.DateEnd:yyyy-MM-dd}, {(t.IsLan ? "LAN" : "Online")})";

    static bool Begin(DataTournament? candidate)
    {
        if (!Plugin.Flags.TournamentRegistration.Value || candidate == null || _candidate != null) return false;
        _candidate = candidate;
        _activeCheck.Begin();
        _registrationsCheck.Begin();
        return true;
    }

    static void End(bool began)
    {
        if (!began) return;
        _registrationsCheck.End();
        _activeCheck.End();
        _candidate = null;
    }

    // The invite email: whether the "register" buttons are enabled, and the message shown.
    [HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CanRegisterMainTeam))]
    [HarmonyPrefix]
    static void CanRegisterMainTeamPrefix(TournamentRegistrationContent __instance, out bool __state) => __state = Begin(__instance.TournamentData);

    [HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CanRegisterMainTeam))]
    [HarmonyPostfix]
    static void CanRegisterMainTeamPostfix(bool __state) => End(__state);

    [HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CanRegisterAcademyTeam))]
    [HarmonyPrefix]
    static void CanRegisterAcademyTeamPrefix(TournamentRegistrationContent __instance, out bool __state) => __state = Begin(__instance.TournamentData);

    [HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CanRegisterAcademyTeam))]
    [HarmonyPostfix]
    static void CanRegisterAcademyTeamPostfix(bool __state) => End(__state);

    [HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CheckForMessage))]
    [HarmonyPrefix]
    static void CheckForMessagePrefix(TournamentRegistrationContent __instance, out bool __state) => __state = Begin(__instance.TournamentData);

    [HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CheckForMessage))]
    [HarmonyPostfix]
    static void CheckForMessagePostfix(bool __state) => End(__state);

    // The registration itself.
    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.TryRegisterPlayer))]
    [HarmonyPrefix]
    static void TryRegisterPlayerPrefix(DataTournament tournament, out bool __state) => __state = Begin(tournament);

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.TryRegisterPlayer))]
    [HarmonyPostfix]
    static void TryRegisterPlayerPostfix(DataTournament tournament, DataTeam team, global::TournamentRegistration __result, bool __state)
    {
        End(__state);
        if (__state)
            Plugin.Log.LogInfo($"[Registration] {team?.Id} → {Describe(tournament)}: {__result}");
    }
}
