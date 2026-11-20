using Il2CppInterop.Runtime.Injection;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealismOverhaul.StoryMode;

/// The Era panel on the home dashboard of a Story Mode career (ADR 0005): which Era the calendar
/// is in, which game version the scene plays, the Era's years and what comes next. The game keeps
/// its own look; this panel is the only visible sign of the Era.
public class EraIndicatorBehaviour : MonoBehaviour
{
    public EraIndicatorBehaviour(IntPtr ptr) : base(ptr) { }

    const string PanelName = "RealismOverhaul.EraIndicator";
    static readonly Color Gold = new(1f, 0.7f, 0f);

    float _next;
    GameObject? _panel;
    TMP_Text? _caption, _era, _version, _summary, _upcoming;
    string _shownFor = "";

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        try { Refresh(); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[EraIndicator] {e}");
            _next = Time.unscaledTime + 30f;
        }
    }

    void Refresh()
    {
        if (StoryStart.CareerStart == null)
        {
            if (_panel != null) _panel.SetActive(false);
            return;
        }
        var home = GameObject.Find("Canvas/MainWindows/Windows/Home");
        if (home == null) return; // the dashboard is not open
        if (_panel == null) Build(home.transform);
        if (_panel == null) return;
        _panel.SetActive(true);

        var today = new DateTime(GlobalValues.CurrentDate.Ticks);
        var key = today.ToString("yyyy-MM-dd");
        if (key == _shownFor) return;
        _shownFor = key;
        var era = Eras.At(today);
        if (era == null) return;
        var pt = UiKit.IsPortuguese();
        var years = $"{era.From[..4]} – {(era.To != null ? era.To[..4] : (pt ? "hoje" : "today"))}";
        _caption!.text = pt ? "ERA ATUAL  ·  MODO HISTÓRIA" : "CURRENT ERA  ·  STORY MODE";
        _era!.text = era.Name.ToUpperInvariant();
        _version!.text = $"{Eras.VersionAt(today)}   <alpha=#80>{years}";
        _summary!.text = (pt ? era.SummaryPt : null) ?? era.Summary;
        var nextVersion = Eras.NextVersion(today);
        var nextEra = Eras.Next(era);
        _upcoming!.text = nextVersion is { } v
            ? (pt ? $"Próxima versão: {v.Version} · {Month(v.Date, pt)}" : $"Next version: {v.Version} · {Month(v.Date, pt)}")
            : nextEra != null ? (pt ? $"Próxima era: {nextEra.Name} · {Month(nextEra.From, pt)}" : $"Next era: {nextEra.Name} · {Month(nextEra.From, pt)}")
            : "";
    }

    void Build(Transform home)
    {
        var style = home.GetComponentInChildren<TMP_Text>(true);
        if (style == null) return;
        // The left column (Organization) has free space at the bottom; else the dashboard itself.
        var column = home.Find("infos");
        var parent = column != null ? column : home;
        var panel = UiKit.Image(parent, PanelName, new Color(0f, 0f, 0f, 0.5f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(0f, 330f));
        panel.raycastTarget = false;
        if (column == null)
        {
            var rt = panel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(30f, 30f);
            rt.sizeDelta = new Vector2(520f, 330f);
        }
        UiKit.Image(panel.transform, "Accent", Gold, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 0f)).raycastTarget = false;

        TMP_Text Line(string name, float size, Color colour, float top, float height)
        {
            var t = UiKit.Text(panel.transform, name, style, "", size, colour, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(28f, -top), new Vector2(-48f, height));
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }
        _caption = Line("Caption", 17f, Gold, 20f, 26f);
        _era = Line("Era", 52f, Color.white, 48f, 64f);
        _era.fontStyle = FontStyles.Bold;
        _era.enableAutoSizing = true;
        _era.fontSizeMin = 30f;
        _era.fontSizeMax = 52f;
        _version = Line("Version", 26f, new Color(1f, 1f, 1f, 0.9f), 118f, 36f);
        _summary = Line("Summary", 19f, new Color(1f, 1f, 1f, 0.7f), 166f, 110f);
        _upcoming = Line("Upcoming", 16f, new Color(1f, 1f, 1f, 0.5f), 286f, 26f);
        _panel = panel.gameObject;
        var size = ((RectTransform)parent).rect.size;
        Plugin.Log.LogInfo($"[EraIndicator] panel added to {(column != null ? "the Organization column" : "the dashboard")} ({size.x:0}x{size.y:0})");
    }

    static string Month(string date, bool pt)
    {
        var d = DateTime.ParseExact(date, "yyyy-MM-dd", null);
        string[] ptm = { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };
        return pt ? $"{ptm[d.Month - 1]}/{d.Year}" : d.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public static class EraIndicator
{
    public static void Start()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<EraIndicatorBehaviour>();
            var go = new GameObject("RealismOverhaul.EraIndicator.Host");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<EraIndicatorBehaviour>();
        }
        catch (Exception e) { Plugin.Log.LogError($"[EraIndicator] could not start: {e}"); }
    }
}
