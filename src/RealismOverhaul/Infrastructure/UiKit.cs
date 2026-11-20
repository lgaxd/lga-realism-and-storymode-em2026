using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RealismOverhaul.Infrastructure;

/// Small helpers for building UI inside the game's screens, styled by cloning the game's own
/// elements (fonts, dropdowns) so new UI looks native. Layout uses explicit RectTransforms.
public static class UiKit
{
    public static bool IsPortuguese()
    {
        try { return (UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en").StartsWith("pt"); }
        catch { return false; }
    }

    public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Image(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = Rect(parent, name, anchorMin, anchorMax, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// A text that copies font and style from an existing game text.
    public static TextMeshProUGUI Text(Transform parent, string name, TMP_Text style, string text, float size, Color color,
        TextAlignmentOptions align, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 rectSize)
    {
        var rt = Rect(parent, name, anchorMin, anchorMax, pivot, pos, rectSize);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = style.font;
        t.fontSharedMaterial = style.fontSharedMaterial;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    /// A clickable text button (text + transparent hit area), tinting on hover.
    public static Button TextButton(Transform parent, string name, TMP_Text style, string label, float size,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 rectSize, Action onClick)
    {
        var bg = Image(parent, name, new Color(1f, 1f, 1f, 0.08f), anchorMin, anchorMax, pivot, pos, rectSize);
        bg.raycastTarget = true;
        var text = Text(bg.transform, "Label", style, label, size, Color.white, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        var colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.08f);
        colors.highlightedColor = new Color(0.35f, 1f, 0.45f, 0.35f);
        colors.pressedColor = new Color(0.35f, 1f, 0.45f, 0.6f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(onClick));
        text.raycastTarget = false;
        return button;
    }

    /// A dropdown cloned from one of the game's StyledDropdowns, emptied of the game's listeners.
    public static TMP_Dropdown Dropdown(TMP_Dropdown template, Transform parent, string name, IList<string> options, int value,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Action<int> onChanged)
    {
        var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
        go.name = name;
        go.SetActive(true);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var dd = go.GetComponent<TMP_Dropdown>();
        dd.onValueChanged = new TMP_Dropdown.DropdownEvent(); // drop the game's listeners
        SetOptions(dd, options, value);
        dd.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<int>>(onChanged));
        return dd;
    }

    public static void SetOptions(TMP_Dropdown dd, IList<string> options, int value)
    {
        dd.ClearOptions();
        var list = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var o in options) list.Add(o);
        dd.AddOptions(list);
        dd.SetValueWithoutNotify(Math.Clamp(value, 0, Math.Max(0, options.Count - 1)));
        dd.RefreshShownValue();
    }

    /// Any StyledDropdown the game has loaded (active or not), to use as a template.
    public static TMP_Dropdown? FindDropdownTemplate()
    {
        var all = Resources.FindObjectsOfTypeAll<StyledDropdown>();
        foreach (var d in all)
            if (d != null && d.gameObject.scene.IsValid()) return d;
        return all.Length > 0 ? all[0] : null;
    }
}
