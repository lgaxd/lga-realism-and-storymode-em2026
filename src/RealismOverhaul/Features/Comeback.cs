using HarmonyLib;

namespace RealismOverhaul.Features;

/// Comeback: a Retired Player retired for at most ComebackMaxYears can be signed again (see
/// GLOSSARY.md: Retired Player, Comeback).
///
/// The game blocks offers to retired players by reading the DataPlayer.Retired field directly
/// (offer popup, offer creation, offer blocking, profile buttons, transfer send block). A field
/// read cannot be patched, so while those methods run for an eligible player, Retired is cleared
/// and then restored. The Comeback becomes permanent when the player is actually added to a team
/// (DataTeam.AddUser): Retired is cleared for good, his mechanical skills decay with time away,
/// and any free-agent staff record created at his retirement is removed.
///
/// Willingness: a Comeback candidate is less willing the older he is and the longer he has been
/// away. The game turns low willingness into refusals and into a higher wage demand
/// (WillingnessCalculator.GetCounterPremium), which is the Comeback premium.
public static class Comeback
{
    static readonly AttributeType[] DecayingAttributes =
    {
        AttributeType.Skill, AttributeType.Awp, AttributeType.Rifle, AttributeType.Pistol,
        AttributeType.Grenades, AttributeType.Clutch, AttributeType.Reaction,
    };

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(Comeback));
        Plugin.Log.LogInfo("[Comeback] patches applied");
    }

    public static double YearsRetired(DataPlayer p) =>
        Math.Max(0, (GlobalValues.CurrentDate - p.RetirementLeaveDate).TotalDays / 365.25);

    /// The free-agent or employed staff record the game created for this player at retirement.
    static DataStaff? StaffRecordOf(DataPlayer p) => DataStaffs.Get(RetirementRoll.RetirementStaffId(p.Id));

    /// True if `p` is retired, within the Comeback window, and not employed as staff.
    public static bool IsEligible(DataPlayer? p)
    {
        if (p == null || !p.Retired) return false;
        if (YearsRetired(p) > Plugin.Flags.ComebackMaxYears.Value) return false;
        var staff = StaffRecordOf(p);
        return staff == null || string.IsNullOrEmpty(staff.TeamId);
    }

    // --- Temporarily clearing Retired while the game's offer checks run ---

    static readonly Dictionary<IntPtr, int> _unflagged = new();

    /// True while the game is running one of the wrapped offer checks for `p` with Retired cleared.
    public static bool IsTemporarilyUnflagged(DataPlayer p) => _unflagged.ContainsKey(p.Pointer);

    static DataPlayer? Unflag(DataUser? user)
    {
        var p = user?.TryCast<DataPlayer>();
        if (!IsEligible(p)) return null;
        var key = p!.Pointer;
        _unflagged[key] = _unflagged.TryGetValue(key, out var n) ? n + 1 : 1;
        p.Retired = false;
        return p;
    }

    static void Restore(DataPlayer? p)
    {
        if (p == null) return;
        var key = p.Pointer;
        if (!_unflagged.TryGetValue(key, out var n)) return;
        if (n > 1) { _unflagged[key] = n - 1; return; }
        _unflagged.Remove(key);
        if (!_signed.Contains(p.Id)) p.Retired = true;
    }

    static readonly HashSet<string> _signed = new();

    [HarmonyPatch(typeof(PopupCreator), nameof(PopupCreator.ShowOffer))]
    [HarmonyPrefix]
    static void ShowOfferPrefix(DataUser player, out DataPlayer? __state)
    {
        __state = Unflag(player);
        if (__state != null)
            Plugin.Log.LogInfo($"[Comeback] offer screen opened for retired {__state.Id} (age {__state.Age}, retired {YearsRetired(__state):F1} years)");
    }

    [HarmonyPatch(typeof(PopupCreator), nameof(PopupCreator.ShowOffer))]
    [HarmonyPostfix]
    static void ShowOfferPostfix(DataPlayer? __state) => Restore(__state);

    [HarmonyPatch(typeof(ContractEngine), nameof(ContractEngine.IsContractOfferBlocked))]
    [HarmonyPrefix]
    static void IsContractOfferBlockedPrefix(DataUser player, out DataPlayer? __state) => __state = Unflag(player);

    [HarmonyPatch(typeof(ContractEngine), nameof(ContractEngine.IsContractOfferBlocked))]
    [HarmonyPostfix]
    static void IsContractOfferBlockedPostfix(DataPlayer? __state) => Restore(__state);

    [HarmonyPatch(typeof(ContractEngine), nameof(ContractEngine.CreateContractOffer))]
    [HarmonyPrefix]
    static void CreateContractOfferPrefix(DataUser player, out DataPlayer? __state)
    {
        __state = Unflag(player);
        if (__state != null) Plugin.Log.LogInfo($"[Comeback] contract offer sent to retired {__state.Id}");
    }

    [HarmonyPatch(typeof(ContractEngine), nameof(ContractEngine.CreateContractOffer))]
    [HarmonyPostfix]
    static void CreateContractOfferPostfix(DataPlayer? __state) => Restore(__state);

    [HarmonyPatch(typeof(TransferOfferPopup), nameof(TransferOfferPopup.ResolveSendBlock))]
    [HarmonyPrefix]
    static void ResolveSendBlockPrefix(DataUser user, out DataPlayer? __state) => __state = Unflag(user);

    [HarmonyPatch(typeof(TransferOfferPopup), nameof(TransferOfferPopup.ResolveSendBlock))]
    [HarmonyPostfix]
    static void ResolveSendBlockPostfix(DataPlayer? __state) => Restore(__state);

    // Profile buttons ("offer contract"): PlayerOverview.Renew reads Retired for the shown player.
    [HarmonyPatch(typeof(PlayerOverview), nameof(PlayerOverview.Renew))]
    [HarmonyPrefix]
    static void PlayerOverviewRenewPrefix(DataUser user, out DataPlayer? __state) => __state = Unflag(user);

    [HarmonyPatch(typeof(PlayerOverview), nameof(PlayerOverview.Renew))]
    [HarmonyPostfix]
    static void PlayerOverviewRenewPostfix(DataPlayer? __state) => Restore(__state);

    // Transfer tab buttons ("offer contract" to a free agent).
    [HarmonyPatch(typeof(PlayerTransferWindow), nameof(PlayerTransferWindow.Renew))]
    [HarmonyPrefix]
    static void PlayerTransferRenewPrefix(DataUser user, out DataPlayer? __state) => __state = Unflag(user);

    [HarmonyPatch(typeof(PlayerTransferWindow), nameof(PlayerTransferWindow.Renew))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)] // after RetiredCoach's postfix, which still needs to see the player as retired
    static void PlayerTransferRenewPostfix(DataPlayer? __state) => Restore(__state);

    // --- Willingness: older and longer away means less willing (and a higher wage demand) ---

    [HarmonyPatch(typeof(WillingnessCalculator), nameof(WillingnessCalculator.Calculate))]
    [HarmonyPostfix]
    static void WillingnessPostfix(DataPlayer player, ref float __result)
    {
        if (player == null || !_unflagged.ContainsKey(player.Pointer)) return;
        var years = YearsRetired(player);
        var penalty = Math.Min(0.6, 0.06 * years + 0.02 * Math.Max(0, player.Age - 28));
        var before = __result;
        __result = (float)(before - Math.Abs(before) * penalty - penalty);
        Plugin.Log.LogInfo($"[Comeback] willingness of {player.Id}: {before:F2} -> {__result:F2} (age {player.Age}, {years:F1} years away)");
    }

    // --- The Comeback itself: a retired player joins a team ---

    [HarmonyPatch(typeof(DataTeam), nameof(DataTeam.AddUser))]
    [HarmonyPrefix]
    static void AddUserPrefix(DataTeam __instance, DataUser user)
    {
        // Only the user's own clubs: the AI never signs retired players (MayAiSignFreeAgent).
        if (__instance == null || !DataTeam.IsOwnedByUser(__instance)) return;
        var p = user?.TryCast<DataPlayer>();
        if (p == null || !_unflagged.ContainsKey(p.Pointer) && !(p.Retired && IsEligible(p))) return;
        CompleteComeback(p);
    }

    static void CompleteComeback(DataPlayer p)
    {
        var years = YearsRetired(p);
        _signed.Add(p.Id);
        p.Retired = false;
        p.WillRetire = false;
        p.IsEarlyRetirement = false;

        var factor = 1f - (float)Math.Min(Plugin.Flags.ComebackMaxDecay.Value, Plugin.Flags.ComebackDecayPerYear.Value * years);
        var attrs = p.Attributes;
        if (attrs != null)
            foreach (var type in DecayingAttributes)
                attrs.SetValue(type, attrs.GetValue(type) * factor, false, false);

        var staff = StaffRecordOf(p);
        if (staff != null && string.IsNullOrEmpty(staff.TeamId))
        {
            DataStaffs.Remove(staff);
            Plugin.Log.LogInfo($"[Comeback] removed {p.Id}'s free-agent staff record");
        }
        Plugin.Log.LogInfo($"[Comeback] {p.Id} is back from retirement after {years:F1} years; mechanical skills x{factor:F2}");
    }
}
