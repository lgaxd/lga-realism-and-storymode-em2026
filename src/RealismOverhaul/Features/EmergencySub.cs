using HarmonyLib;

namespace RealismOverhaul.Features;

/// Emergency Sub: a coach who used to be a player fills in as the fifth player when the team
/// cannot field five (see GLOSSARY.md: Emergency Sub).
///
/// MatchesEngine.PrepareSquadsForMatch already calls up an Academy player when a team is short; if
/// the team still cannot field five, the game forfeits the match. Right after that call-up, for
/// the user's teams, the club's coach steps in if the game links him to a former player
/// (RetirementRoll.TryGetFormerPlayerId). For the length of the match the former player is put
/// back on the team: Retired cleared, added to the squad, mechanical skills decayed by years away.
/// Everything is restored when the match ends (or at the day change, as a safety net).
///
/// The choice is automatic: the alternative is a forfeit, which is never better. A notification
/// tells the user who stepped in.
public static class EmergencySub
{
    sealed class Sub
    {
        public DataPlayer Player = null!;
        public DataTeam Team = null!;
        public string MatchKey = "";
        public bool WasRetired;
        public string? OldTeamId;
        public bool AddedToFull;
        public Dictionary<AttributeType, float> OldAttributes = new();
    }

    static readonly List<Sub> _active = new();

    static readonly AttributeType[] Decaying =
    {
        AttributeType.Skill, AttributeType.Awp, AttributeType.Rifle, AttributeType.Pistol,
        AttributeType.Grenades, AttributeType.Clutch, AttributeType.Reaction,
    };

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(EmergencySub));
        Plugin.Log.LogInfo("[EmergencySub] patches applied");
    }

    static DataPlayer? FormerPlayerCoachOf(DataTeam team)
    {
        var coach = DataStaffs.GetByRoleAndTeam(StaffRole.Coach, team.Id);
        if (coach == null) return null;
        var formerId = RetirementRoll.TryGetFormerPlayerId(coach.Id);
        return string.IsNullOrEmpty(formerId) ? null : DataPlayers.Get(formerId);
    }

    // High priority: runs before MatchPower opens the match window, so the sub is in the lineup.
    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.PrepareSquadsForMatch))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.High)]
    static void PrepareSquadsPostfix(DataMatch match)
    {
        if (match == null) return;
        foreach (var teamId in new[] { match.Team1Id, match.Team2Id })
        {
            var team = string.IsNullOrEmpty(teamId) ? null : DataTeams.Get(teamId);
            if (team == null || !DataTeam.IsOwnedByUser(team) || !MatchesEngine.CannotFieldFive(team)) continue;
            try { StepIn(team, match); }
            catch (Exception e) { Plugin.Log.LogError($"[EmergencySub] could not field the coach for {team.Id}: {e}"); }
        }
    }

    static void StepIn(DataTeam team, DataMatch match)
    {
        var p = FormerPlayerCoachOf(team);
        if (p == null)
        {
            Plugin.Log.LogInfo($"[EmergencySub] {team.Id} cannot field five and has no former-player coach: the game will forfeit");
            return;
        }

        var years = Comeback.YearsRetired(p);
        var factor = 1f - (float)Math.Min(Plugin.Flags.ComebackMaxDecay.Value, Plugin.Flags.ComebackDecayPerYear.Value * years);
        var old = new Dictionary<AttributeType, float>();
        var attrs = p.Attributes;
        if (attrs != null)
            foreach (var t in Decaying)
            {
                old[t] = attrs.GetValue(t);
                attrs.SetValue(t, old[t] * factor, false, false);
            }

        var full = team.FullSquad;
        var addedToFull = full != null && !full.Contains(p.Id);
        var sub = new Sub
        {
            Player = p, Team = team, MatchKey = Infrastructure.MatchPower.Key(match),
            WasRetired = p.Retired, OldTeamId = p.TeamId, AddedToFull = addedToFull, OldAttributes = old,
        };
        p.Retired = false;
        p.TeamId = team.Id;
        if (addedToFull) full!.Add(p.Id);
        if (!team.MainSquad.Contains(p.Id)) team.MainSquad.Add(p.Id);
        _active.Add(sub);

        var stillShort = MatchesEngine.CannotFieldFive(team);
        Plugin.Log.LogInfo($"[EmergencySub] coach {p.Id} steps in for {team.Id} ({years:F1} years retired, mechanical skills x{factor:F2}); " +
                           $"can field five now: {!stillShort}");
        Notify(p, team);
    }

    static void Notify(DataPlayer p, DataTeam team)
    {
        try
        {
            var pt = (UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en").StartsWith("pt");
            var title = pt ? "Substituto de emergência" : "Emergency sub";
            var text = pt
                ? $"{team.Name} não tinha cinco jogadores disponíveis. O treinador {p.Id}, ex-jogador, vai jogar esta partida."
                : $"{team.Name} could not field five players. Coach {p.Id}, a former player, plays this match.";
            PopupCreator.Instance.ShowNotification(text, title);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[EmergencySub] notification failed: {e.Message}"); }
    }

    static void Restore(Sub s, string why)
    {
        s.Team.MainSquad.Remove(s.Player.Id);
        if (s.AddedToFull) s.Team.FullSquad?.Remove(s.Player.Id);
        s.Player.TeamId = s.OldTeamId;
        s.Player.Retired = s.WasRetired;
        var attrs = s.Player.Attributes;
        if (attrs != null)
            foreach (var (t, v) in s.OldAttributes) attrs.SetValue(t, v, false, false);
        Plugin.Log.LogInfo($"[EmergencySub] {s.Player.Id} back to coaching {s.Team.Id} ({why})");
    }

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    static void EndMatchPostfix(DataMatch match)
    {
        if (match == null || _active.Count == 0) return;
        var key = Infrastructure.MatchPower.Key(match);
        foreach (var s in _active.Where(s => s.MatchKey == key).ToList())
        {
            Restore(s, "match over");
            _active.Remove(s);
        }
    }

    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.NextDayDate))]
    [HarmonyPrefix]
    static void NextDayDatePrefix()
    {
        foreach (var s in _active) Restore(s, "day change safety net");
        _active.Clear();
    }
}
