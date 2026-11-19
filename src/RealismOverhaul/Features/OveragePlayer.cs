using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Overage Player: an Academy may hold at most one player older than 23 (see GLOSSARY.md).
/// The game blocks this in several places (offer UI, player refusal, squad card button, room
/// check, move); each patch lifts the block only while the target Academy has no Overage Player.
public static class OveragePlayer
{
    public const int AcademyAgeLimit = 23;

    public static void Apply(Harmony harmony)
    {
        Plugin.Log.LogInfo(
            $"[Overage] game constants: AcademyHardRefusalAge={WillingnessCalculator.AcademyHardRefusalAge}, " +
            $"AiMarketRules.AcademyMaxSigningAge={AiMarketRules.AcademyMaxSigningAge}");
        _ageClamp = new ScopedPatch(harmony,
            AccessTools.PropertyGetter(typeof(DataUser), nameof(DataUser.Age)),
            AccessTools.Method(typeof(OveragePlayer), nameof(ClampAgePostfix)));
        harmony.PatchAll(typeof(OveragePlayer));
        Plugin.Log.LogInfo("[Overage] patches applied");
    }

    public static bool IsOverage(DataPlayer player) => player.Age > AcademyAgeLimit;

    public static int CountOverage(DataTeam academy)
    {
        var count = 0;
        var squad = academy.FullSquad;
        if (squad == null) return 0;
        for (var i = 0; i < squad.Count; i++)
        {
            var p = DataPlayers.Get(squad[i]);
            if (p != null && IsOverage(p)) count++;
        }
        return count;
    }

    /// True when `player` may join `academy` under the Overage rule.
    public static bool HasFreeOverageSlot(DataTeam? academy, DataPlayer player)
    {
        if (academy == null) return false;
        var squad = academy.FullSquad;
        var alreadyInside = squad != null && squad.Contains(player.Id);
        var others = CountOverage(academy) - (alreadyInside && IsOverage(player) ? 1 : 0);
        return others == 0;
    }

    static readonly HashSet<string> LoggedButtonFor = new();

    static DataTeam? UserAcademy() => GlobalValues.DataTeam?.GetAcademyTeam();

    // 1. Transfer offer UI: "too old for the Academy" block.
    [HarmonyPatch(typeof(TransferOfferPopup), nameof(TransferOfferPopup.ResolveSendBlock))]
    [HarmonyPostfix]
    static void ResolveSendBlockPostfix(DataUser user, bool toAcademy, ref TransferOfferPopup.SendBlock __result)
    {
        if (__result != TransferOfferPopup.SendBlock.AcademyAge || !toAcademy) return;
        var player = user?.TryCast<DataPlayer>();
        if (player == null) return;
        if (HasFreeOverageSlot(UserAcademy(), player))
        {
            Plugin.Log.LogInfo($"[Overage] allowing offer for {player.Id} (age {player.Age}) to the Academy");
            __result = TransferOfferPopup.SendBlock.None;
        }
        else
            Plugin.Log.LogInfo($"[Overage] keeping offer block for {player.Id} (age {player.Age}): Academy already has an Overage Player");
    }

    // 2. Player refuses to sign for an Academy because of age.
    [HarmonyPatch(typeof(WillingnessCalculator), nameof(WillingnessCalculator.RefusesAcademyOutright))]
    [HarmonyPostfix]
    static void RefusesAcademyOutrightPostfix(DataPlayer player, ref bool __result)
    {
        if (!__result || player == null) return;
        if (HasFreeOverageSlot(UserAcademy(), player))
        {
            Plugin.Log.LogInfo($"[Overage] {player.Id} (age {player.Age}) no longer refuses the Academy outright");
            __result = false;
        }
        else
            Plugin.Log.LogInfo($"[Overage] {player.Id} (age {player.Age}) refuses: Academy already has an Overage Player");
    }

    // 3b. The squad card decides the "send to Academy" button inside Init. IL2CPP may inline
    // CanSendToAcademy there, so patch 3 alone does not reach the button.
    [HarmonyPatch(typeof(PlayerSquadCard), nameof(PlayerSquadCard.Init))]
    [HarmonyPostfix]
    static void SquadCardInitPostfix(PlayerSquadCard __instance, DataPlayer player, bool isOurTeam)
    {
        if (!isOurTeam || player == null || !IsOverage(player)) return;
        var button = __instance._addToAcademy;
        if (button == null || button.activeSelf) return;

        var club = GlobalValues.DataTeam;
        if (club == null || player.TeamId != club.Id) return; // only Main Roster players move down
        var academy = club.GetAcademyTeam();
        if (HasFreeOverageSlot(academy, player))
        {
            if (LoggedButtonFor.Add(player.Id))
                Plugin.Log.LogInfo($"[Overage] showing 'send to Academy' for {player.Id} (age {player.Age})");
            button.SetActive(true);
        }
    }

    // 3c. Sending a player down runs two game methods that each have the age limit compiled in
    // (`if (player.Age >= 24) fail`), so no constant can be changed:
    //   - HasAcademyRoomNow (the room check, which also shows the "squad full" notification), and
    //   - MovePlayerToAcademy (the move itself).
    // While either runs for an eligible Overage Player, report that player's age as 23, so the
    // game's own capacity/tournament/move logic runs unchanged. The get_Age patch exists only
    // inside that window, because the getter is hot.
    static readonly HashSet<string> _clampedIds = new();
    static ScopedPatch _ageClamp = null!;

    static void ClampAgePostfix(DataUser __instance, ref int __result)
    {
        if (__result > AcademyAgeLimit && _clampedIds.Count > 0 && _clampedIds.Contains(__instance.Id))
            __result = AcademyAgeLimit;
    }

    /// Starts an age-clamp window for the card's player if they may move down as the Overage Player.
    static bool BeginClamp(PlayerSquadCard card)
    {
        var player = card._player;
        if (player == null || !IsOverage(player)) return false;
        var club = GlobalValues.DataTeam;
        if (club == null || player.TeamId != club.Id || !HasFreeOverageSlot(club.GetAcademyTeam(), player)) return false;
        return BeginClamp(new[] { player.Id });
    }

    /// Starts an age-clamp window: while it is open, the given players' ages read as 23.
    static bool BeginClamp(IEnumerable<string> playerIds)
    {
        foreach (var id in playerIds) _clampedIds.Add(id);
        if (_clampedIds.Count == 0) return false;
        _ageClamp.Begin();
        return true;
    }

    static void EndClamp(bool began)
    {
        if (!began) return;
        _ageClamp.End();
        if (!_ageClamp.IsOpen) _clampedIds.Clear();
    }

    [HarmonyPatch(typeof(PlayerSquadCard), nameof(PlayerSquadCard.HasAcademyRoomNow))]
    [HarmonyPrefix]
    static void HasAcademyRoomNowPrefix(PlayerSquadCard __instance, out bool __state) => __state = BeginClamp(__instance);

    [HarmonyPatch(typeof(PlayerSquadCard), nameof(PlayerSquadCard.HasAcademyRoomNow))]
    [HarmonyPostfix]
    static void HasAcademyRoomNowPostfix(bool __state) => EndClamp(__state);

    [HarmonyPatch(typeof(PlayerSquadCard), nameof(PlayerSquadCard.MovePlayerToAcademy))]
    [HarmonyPrefix]
    static void MovePlayerToAcademyPrefix(PlayerSquadCard __instance, out bool __state) => __state = BeginClamp(__instance);

    [HarmonyPatch(typeof(PlayerSquadCard), nameof(PlayerSquadCard.MovePlayerToAcademy))]
    [HarmonyPostfix]
    static void MovePlayerToAcademyPostfix(PlayerSquadCard __instance, bool __state)
    {
        EndClamp(__state);
        var player = __instance._player;
        if (__state && player != null && player.TeamId != GlobalValues.DataTeam?.Id)
            Plugin.Log.LogInfo($"[Overage] moved {player.Id} (age {player.Age}) down to the Academy as its Overage Player");
    }

    // 4. AI Academies. AIAcademyOveragePass promotes or lists for sale every AI Academy player older
    // than 23 (age limit compiled in). Sometimes keep one instead: always the Academy's IGL if they
    // aged out, otherwise each aged-out player with a fixed per-player chance. At most one per Academy.
    [HarmonyPatch(typeof(AiRosterLifecyclePasses), nameof(AiRosterLifecyclePasses.AIAcademyOveragePass))]
    [HarmonyPrefix]
    static void AIAcademyOveragePassPrefix(out bool __state)
    {
        __state = false;
        if (!Plugin.Flags.AiOverageRetention.Value) return;
        var keep = new List<string>();
        var aiAcademies = 0;
        // IReadOnlyList's Count lives on the IReadOnlyCollection interface in the interop.
        var teams = DataTeams.List;
        var count = teams.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataTeam>>().Count;
        for (var i = 0; i < count; i++)
        {
            var academy = teams[i];
            if (academy == null || !academy.IsAcademy || DataTeam.IsOwnedByUser(academy)) continue;
            aiAcademies++;
            var kept = PickRetainedOverage(academy);
            if (kept != null) keep.Add(kept.Id);
        }
        if (keep.Count == 0) return;
        var summary = string.Join(", ", keep);
        if (summary != _lastRetentionSummary)
        {
            _lastRetentionSummary = summary;
            Plugin.Log.LogInfo($"[Overage] {keep.Count} of {aiAcademies} AI Academies keeping an Overage Player: {summary}");
        }
        __state = BeginClamp(keep);
    }

    [HarmonyPatch(typeof(AiRosterLifecyclePasses), nameof(AiRosterLifecyclePasses.AIAcademyOveragePass))]
    [HarmonyPostfix]
    static void AIAcademyOveragePassPostfix(bool __state) => EndClamp(__state);

    static string? _lastRetentionSummary;

    static DataPlayer? PickRetainedOverage(DataTeam academy)
    {
        var squad = academy.FullSquad;
        if (squad == null) return null;
        DataPlayer? chosen = null;
        for (var i = 0; i < squad.Count; i++)
        {
            var p = DataPlayers.Get(squad[i]);
            if (p == null || !IsOverage(p)) continue;
            if (p.IsIGL) return p;
            if (chosen == null && StableRoll(p.Id) < Plugin.Flags.AiOverageRetentionChance.Value) chosen = p;
        }
        return chosen;
    }

    /// Deterministic 0-99 roll per player, so the AI's decision is stable across weekly passes.
    static int StableRoll(string id)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in id) h = (h ^ c) * 16777619u;
            return (int)(h % 100);
        }
    }

    // 3. Moving one of our own players down to the Academy.
    [HarmonyPatch(typeof(PlayerSquadCard), nameof(PlayerSquadCard.CanSendToAcademy))]
    [HarmonyPostfix]
    static void CanSendToAcademyPostfix(DataPlayer player, DataTeam academy, ref bool __result)
    {
        if (__result || player == null || !IsOverage(player)) return;
        if (HasFreeOverageSlot(academy, player))
        {
            Plugin.Log.LogInfo($"[Overage] allowing {player.Id} (age {player.Age}) to move down to the Academy");
            __result = true;
        }
        else
            Plugin.Log.LogInfo($"[Overage] blocking {player.Id} (age {player.Age}): Academy already has an Overage Player");
    }
}
