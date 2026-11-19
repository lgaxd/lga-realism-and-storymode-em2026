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

    static string Lang()
    {
        try { return LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en"; }
        catch { return "en"; } // localization not ready
    }

    static string Label() => Lang() switch
    {
        var c when c.StartsWith("pt") => "Contratar como treinador",
        var c when c.StartsWith("es") => "Contratar como entrenador",
        _ => "Hire as coach",
    };

    static string ComebackLabel() => Lang() switch
    {
        var c when c.StartsWith("pt") => "Retorno: oferecer contrato como jogador",
        var c when c.StartsWith("es") => "Regreso: ofrecer contrato como jugador",
        _ => "Comeback: offer a player contract",
    };

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
            LabelComebackRow(__instance, player != null && __instance._freeBtn.activeSelf && Comeback.IsEligible(player, ignoreFlag: true));

            if (player == null || !CanBeHiredAsCoach(player))
            {
                if (existing != null) existing.gameObject.SetActive(false);
                ResetRows(__instance);
                _shown = null;
                return;
            }

            _shown = player;
            LogLayoutOnce(__instance);
            var button = existing != null ? existing.gameObject : CreateButton(template, parent);
            button.SetActive(true);
            Arrange(__instance, button);
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

    const float LineOffset = 14f;

    /// The game shows one action row at a time, every row stretched over the whole Actions box.
    /// When another row is visible (the Comeback offer), split the box: that row moves up half a
    /// line, ours down half a line. Our clone otherwise sits where the rows do.
    static void Arrange(PlayerTransferWindow w, GameObject clone)
    {
        var rt = clone.GetComponent<RectTransform>();
        var src = w._freeBtn?.GetComponent<RectTransform>();
        if (rt == null || src == null) return;
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.sizeDelta = src.sizeDelta;

        GameObject? visible = null;
        foreach (var b in ActionButtons(w))
            if (b != null && b.activeSelf) { visible = b; break; }

        rt.anchoredPosition = new Vector2(0, visible != null ? -LineOffset : 0);
        if (visible != null)
            visible.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, LineOffset);
        clone.transform.SetAsLastSibling(); // drawn on top, so it receives clicks
    }

    /// Undo Arrange's shift of the game's rows (when our button is hidden).
    static void ResetRows(PlayerTransferWindow w)
    {
        foreach (var b in ActionButtons(w))
            if (b != null) b.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
    }

    /// For a Comeback-eligible player, say plainly that the game's free-agent row is a player contract.
    static void LabelComebackRow(PlayerTransferWindow w, bool comeback)
    {
        var row = w._freeBtn;
        if (row == null) return;
        var text = row.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;
        foreach (var c in text.GetComponents<Component>())
        {
            if (c == null || !c.GetIl2CppType().Name.Contains("Localize")) continue;
            var behaviour = c.TryCast<Behaviour>();
            if (behaviour != null) behaviour.enabled = !comeback; // re-enabling restores the game's text
        }
        if (comeback) text.text = ComebackLabel();
    }

    static GameObject CreateButton(GameObject template, Transform parent)
    {
        var clone = UnityEngine.Object.Instantiate(template, parent);
        clone.name = ButtonName;

        // The template's text is driven by a localization component; drop it so our label stays.
        foreach (var c in clone.GetComponentsInChildren<Component>(true))
            if (c != null && c.GetIl2CppType().Name.Contains("Localize"))
                UnityEngine.Object.Destroy(c);

        // The game's action rows handle clicks with their own component, not a Unity Button. Log what
        // the clone carries, remove anything click-related (it would run the original action), and
        // give it a standard Button that calls us.
        var keep = new HashSet<string> { "Transform", "RectTransform", "CanvasRenderer", "Image", "RawImage",
            "TextMeshProUGUI", "TMP_SubMeshUI", "LayoutElement", "ContentSizeFitter", "HorizontalLayoutGroup",
            "VerticalLayoutGroup", "CanvasGroup", "Mask", "RectMask2D", "Outline", "Shadow" };
        foreach (var c in clone.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            var typeName = c.GetIl2CppType().Name;
            Plugin.Log.LogInfo($"[RetiredCoach][components] {c.gameObject.name}: {typeName}");
            if (!keep.Contains(typeName) && !typeName.Contains("Localize"))
            {
                Plugin.Log.LogInfo($"[RetiredCoach][components]   removing {typeName}");
                UnityEngine.Object.DestroyImmediate(c);
            }
        }

        Graphic? target = null;
        foreach (var g in clone.GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = true;
            target ??= g;
        }
        var button = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
        // Prefer the label as the tinted graphic, so hovering visibly highlights the row.
        var label = clone.GetComponentInChildren<TMP_Text>(true);
        button.targetGraphic = label != null ? label : target;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.55f, 1f, 0.55f);   // game's action green
        colors.pressedColor = new Color(0.35f, 0.8f, 0.35f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(OnClick)));
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
