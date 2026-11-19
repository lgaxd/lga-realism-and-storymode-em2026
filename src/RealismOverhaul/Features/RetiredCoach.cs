using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace RealismOverhaul.Features;

/// Any Retired Player can be hired as a coach (see GLOSSARY.md: Retired Player).
///
/// The game already turns some retirees into coaches or analysts at retirement
/// (RetirementRoll.SpawnStaffFromPlan, which links the staff record to the former player). This
/// adds a "Hire as coach" button to a retired player's transfer tab: it runs the same spawn with
/// the plan set to Coach (or reuses an existing free-agent staff record), then opens the game's
/// normal contract offer for that coach.
public static class RetiredCoach
{
    const string ButtonName = "RealismOverhaul.HireAsCoach";

    public static void Apply(Harmony harmony)
    {
        harmony.PatchAll(typeof(RetiredCoach));
        Plugin.Log.LogInfo("[RetiredCoach] patches applied");
    }

    static DataStaff? StaffRecordOf(DataPlayer p) => DataStaffs.Get(RetirementRoll.RetirementStaffId(p.Id));

    /// Retired, and not already employed as staff somewhere.
    static bool CanBeHiredAsCoach(DataPlayer p)
    {
        if (!(p.Retired || Comeback.IsTemporarilyUnflagged(p))) return false;
        var staff = StaffRecordOf(p);
        return staff == null || string.IsNullOrEmpty(staff.TeamId);
    }

    static string Label()
    {
        try
        {
            var code = LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en";
            if (code.StartsWith("pt")) return "Contratar como treinador";
            if (code.StartsWith("es")) return "Contratar como entrenador";
        }
        catch { /* localization not ready: fall back to English */ }
        return "Hire as coach";
    }

    static DataPlayer? _shown;

    [HarmonyPatch(typeof(PlayerTransferWindow), nameof(PlayerTransferWindow.Renew))]
    [HarmonyPostfix]
    static void RenewPostfix(PlayerTransferWindow __instance, DataUser user)
    {
        try
        {
            var template = __instance._freeBtn;
            if (template == null) return;
            var parent = template.transform.parent;
            var existing = parent.Find(ButtonName);
            var player = user?.TryCast<DataPlayer>();

            if (player == null || !CanBeHiredAsCoach(player))
            {
                if (existing != null) existing.gameObject.SetActive(false);
                _shown = null;
                return;
            }

            _shown = player;
            LogLayoutOnce(__instance);
            var button = existing != null ? existing.gameObject : CreateButton(template, parent);
            button.SetActive(true);
            PlaceInFreeSlot(__instance, button);
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = Label();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[RetiredCoach] could not show the button: {e}");
        }
    }

    /// The transfer tab's action buttons, positioned absolutely inside the "Actions" box.
    static GameObject?[] ActionButtons(PlayerTransferWindow w) =>
        new[] { w._buyBtn, w._sellBtn, w._freeBtn, w._negolationsBtn, w._removeBtn, w._endcontractBtn, w._transfernowBtn, w._banteamBtn };

    static bool _loggedLayout;

    static void LogLayoutOnce(PlayerTransferWindow w)
    {
        if (_loggedLayout) return;
        _loggedLayout = true;
        foreach (var b in ActionButtons(w))
        {
            if (b == null) continue;
            var rt = b.GetComponent<RectTransform>();
            Plugin.Log.LogInfo($"[RetiredCoach][layout] {b.name}: active={b.activeSelf}, parent={b.transform.parent?.name}, " +
                               $"pos={rt?.anchoredPosition}, size={rt?.sizeDelta}");
        }
    }

    /// Puts the clone in the slot of an action button the game hid for this player, which is inside
    /// the Actions box; falls back to the template's own slot.
    static void PlaceInFreeSlot(PlayerTransferWindow w, GameObject clone)
    {
        var rt = clone.GetComponent<RectTransform>();
        if (rt == null) return;
        GameObject? slot = null;
        foreach (var b in ActionButtons(w))
            if (b != null && !b.activeSelf && b.transform.parent == clone.transform.parent) { slot = b; break; }
        var src = (slot ?? w._freeBtn)?.GetComponent<RectTransform>();
        if (src == null) return;
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition;
        rt.sizeDelta = src.sizeDelta;
        clone.transform.SetAsLastSibling(); // drawn on top, so it receives clicks
    }

    static GameObject CreateButton(GameObject template, Transform parent)
    {
        var clone = UnityEngine.Object.Instantiate(template, parent);
        clone.name = ButtonName;

        // The template's text is driven by a localization component; drop it so our label stays.
        foreach (var c in clone.GetComponentsInChildren<Component>(true))
            if (c != null && c.GetIl2CppType().Name.Contains("Localize"))
                UnityEngine.Object.Destroy(c);

        var button = clone.GetComponent<Button>() ?? clone.GetComponentInChildren<Button>(true);
        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent(); // drops the template's persistent listeners
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(OnClick)));
        }
        else
            Plugin.Log.LogWarning("[RetiredCoach] template has no Button component; the clone will not be clickable");
        Plugin.Log.LogInfo("[RetiredCoach] 'Hire as coach' button created");
        return clone;
    }

    static void OnClick()
    {
        var player = _shown;
        Plugin.Log.LogInfo($"[RetiredCoach] 'Hire as coach' clicked for {player?.Id}");
        if (player == null) return;
        try
        {
            var staff = StaffRecordOf(player);
            if (staff == null)
            {
                player.RetirementStaffPlan = RetirementStaffOutcome.Coach;
                RetirementRoll.SpawnStaffFromPlan(player);
                staff = StaffRecordOf(player);
                Plugin.Log.LogInfo($"[RetiredCoach] created coach record for {player.Id}: {(staff != null ? staff.Id : "<failed>")}");
            }
            if (staff == null) return;
            if (staff.Role != StaffRole.Coach)
                Plugin.Log.LogInfo($"[RetiredCoach] {player.Id} already has a {staff.Role} staff record; offering that");
            PopupCreator.Instance.ShowOffer(staff);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[RetiredCoach] hiring {player.Id} failed: {e}");
        }
    }
}
