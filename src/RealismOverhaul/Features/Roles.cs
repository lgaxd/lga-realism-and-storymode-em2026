using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// A player's tactical specialty (see GLOSSARY.md: Role). IGL stays the game's own separate duty.
public enum Role { Entry, Trader, Support, Lurker, AWPer, Anchor }

/// Realistic Roles and Role Balance (see GLOSSARY.md: Role, Role Balance).
///
/// The game only knows a main role (AWPer / Rifler) and an optional secondary (Support / Lurker /
/// Sniper). A player's Role is derived from that plus his attribute profile, so it needs no saved
/// data and follows the player as his attributes change:
///   - AWPer main role -> AWPer;
///   - a Support / Lurker secondary -> that Role;
///   - otherwise the Role whose attribute profile fits best (Entry, Trader, Anchor, Support, Lurker).
/// A second Role is the next best fit (AWPer for a Rifler with the Sniper secondary).
///
/// Role Balance: a lineup that lacks or doubles key Roles plays slightly worse, a balanced one
/// slightly better, through MatchPower, for every team including the AI's.
public static class Roles
{
    public static void Apply(Harmony harmony)
    {
        MatchPower.Apply(harmony);
        ManualChoices.Apply(harmony);
        MatchPower.Register("RoleBalance", Contribute);
        harmony.PatchAll(typeof(Roles));
        Plugin.Log.LogInfo("[Roles] patches applied");
    }

    // --- Deriving a player's Roles ---

    static float A(DataPlayer p, AttributeType t)
    {
        try { return p.GetEffectiveAttribute(t); }
        catch { return 0f; }
    }

    static Dictionary<Role, float> RiflerFit(DataPlayer p) => new()
    {
        [Role.Entry] = 1.2f * A(p, AttributeType.Reaction) + A(p, AttributeType.Rifle) + 0.6f * A(p, AttributeType.Creativity),
        [Role.Trader] = A(p, AttributeType.Rifle) + 0.8f * A(p, AttributeType.Teamwork) + 0.6f * A(p, AttributeType.Skill),
        [Role.Anchor] = 1.1f * A(p, AttributeType.Clutch) + 0.7f * A(p, AttributeType.Grenades) + 0.6f * A(p, AttributeType.Teamwork),
        [Role.Support] = 1.1f * A(p, AttributeType.Grenades) + A(p, AttributeType.Teamwork) + 0.5f * A(p, AttributeType.Tactic),
        [Role.Lurker] = A(p, AttributeType.Creativity) + 0.8f * A(p, AttributeType.Clutch) + 0.6f * A(p, AttributeType.Skill),
    };

    /// The player's Roles: the user's choice if any, otherwise derived.
    public static (Role Primary, Role? Secondary) RolesOf(DataPlayer p) =>
        ManualChoices.TryGetRoles(p.Id, out var chosen) ? chosen : DerivedRolesOf(p);

    /// The Roles derived from the game's role and the player's attributes.
    public static (Role Primary, Role? Secondary) DerivedRolesOf(DataPlayer p)
    {
        if (p.MainRole == PlayerMainRole.AWPer)
        {
            var best = RiflerFit(p).OrderByDescending(kv => kv.Value).First().Key;
            return (Role.AWPer, best);
        }

        var fit = RiflerFit(p);
        Role primary = p.SecondaryRole switch
        {
            PlayerSecondaryRole.Support => Role.Support,
            PlayerSecondaryRole.Lurker => Role.Lurker,
            _ => fit.OrderByDescending(kv => kv.Value).First().Key,
        };
        Role? secondary = p.SecondaryRole == PlayerSecondaryRole.Sniper
            ? Role.AWPer
            : fit.Where(kv => kv.Key != primary).OrderByDescending(kv => kv.Value).First().Key;
        return (primary, secondary);
    }

    // --- Role Balance ---

    /// Team-wide multiplier for a lineup's Role composition, with the reasons.
    public static (float Factor, List<string> Notes) Balance(IList<DataPlayer> five)
    {
        var roles = five.Select(RolesOf).ToList();
        int Count(Role r) => roles.Count(x => x.Primary == r);
        bool Covered(Role r) => roles.Any(x => x.Secondary == r);

        var notes = new List<string>();
        var delta = 0f;
        void Missing(Role r, float penalty, params Role[] alternatives)
        {
            if (Count(r) > 0 || alternatives.Any(a => Count(a) > 0)) return;
            var p = Covered(r) || alternatives.Any(Covered) ? penalty / 2 : penalty;
            delta -= p;
            notes.Add($"no {r} -{p:P0}");
        }
        void Extra(Role r, float perExtra)
        {
            var extra = Count(r) - 1;
            if (extra <= 0) return;
            delta -= perExtra * extra;
            notes.Add($"{Count(r)} {r}s -{perExtra * extra:P0}");
        }

        Missing(Role.AWPer, 0.03f);
        Missing(Role.Entry, 0.03f);
        Missing(Role.Support, 0.02f, Role.Anchor);
        Extra(Role.AWPer, 0.04f);
        Extra(Role.Lurker, 0.03f);

        if (delta == 0f && Count(Role.AWPer) == 1 && Count(Role.Entry) >= 1 && Count(Role.Support) + Count(Role.Anchor) >= 1)
        {
            delta = 0.02f;
            notes.Add("balanced +2%");
        }

        delta *= Plugin.Flags.RoleBalanceStrength.Value;
        delta = Math.Clamp(delta, -0.08f, 0.02f);
        return (1f + delta, notes);
    }

    static IReadOnlyDictionary<string, float>? Contribute(DataMatch match, DataTeam team)
    {
        var squad = team.MainSquad;
        if (squad == null) return null;
        var five = new List<DataPlayer>();
        for (var i = 0; i < squad.Count && five.Count < 5; i++)
        {
            var p = DataPlayers.Get(squad[i]);
            if (p != null) five.Add(p);
        }
        if (five.Count < 5) return null;

        var (factor, notes) = Balance(five);
        if (DataTeam.IsOwnedByUser(team))
        {
            var lineup = string.Join(", ", five.Select(p => $"{p.Id}={RolesOf(p).Primary}"));
            Plugin.Log.LogInfo($"[Roles] {team.Id} lineup: {lineup} -> x{factor:F2} ({(notes.Count == 0 ? "neutral" : string.Join(", ", notes))})");
        }
        if (Math.Abs(factor - 1f) < 0.0001f) return null;
        return five.ToDictionary(p => p.Id, _ => factor);
    }

    // --- UI: show the Role on the player profile, next to the game's own role ---

    [HarmonyPatch(typeof(PlayerOverview), nameof(PlayerOverview.Renew))]
    [HarmonyPostfix]
    static void PlayerOverviewRenewPostfix(PlayerOverview __instance, DataUser user) => RenderRoleText(__instance, user?.TryCast<DataPlayer>());

    /// Appends the Role to the profile header's role text ("Rifler · Entry / Trader").
    public static void RenderRoleText(PlayerOverview __instance, DataPlayer? p)
    {
        try
        {
            var text = __instance._roleTxt;
            if (p == null || text == null) return;
            var (primary, secondary) = RolesOf(p);
            var baseText = text.text ?? "";
            var sep = baseText.IndexOf(" · ", StringComparison.Ordinal);
            if (sep >= 0) baseText = baseText[..sep];
            text.text = $"{baseText} · {primary}{(secondary != null ? $" / {secondary}" : "")}";
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[Roles] could not show the Role: {e}");
        }
    }
}
