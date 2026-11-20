using Il2CppInterop.Runtime.Injection;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealismOverhaul.StoryMode;

/// The Era banner on the home dashboard of a Story Mode career (ADR 0005): which Era the calendar
/// is in, which game version the scene plays, the Era's years and the next version. It sits in the
/// top bar, which is empty on the dashboard whatever the game shows below. The game keeps its own
/// look; this banner is the only visible sign of the Era.
public class EraIndicatorBehaviour : MonoBehaviour
{
    public EraIndicatorBehaviour(IntPtr ptr) : base(ptr) { }

    const string PanelName = "RealismOverhaul.EraIndicator";
    static readonly Color Gold = new(1f, 0.7f, 0f);

    float _next;
    GameObject? _panel;
    TMP_Text? _era, _version;
    string _shownFor = "";

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        try { Refresh(); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[EraIndicator] {e}");
            _next = Time.unscaledTime + 30f;
        }
    }

    void Refresh()
    {
        var home = StoryStart.CareerStart != null ? GameObject.Find("Canvas/MainWindows/Windows/Home") : null;
        // The dashboard stays built while other screens are open: IsShown says which one is on screen.
        var window = home != null ? home.GetComponent<Windows>() : null;
        if (home == null || (window != null && !window.IsShown))
        {
            // Only on the dashboard of a Story Mode career.
            if (_panel != null) _panel.SetActive(false);
            return;
        }
        if (_panel == null)
        {
            var top = GameObject.Find("Canvas/MainWindows/TopMenu");
            if (top == null) return;
            Build(top.transform, home.transform);
        }
        if (_panel == null) return;
        _panel.SetActive(true);

        var today = new DateTime(GlobalValues.CurrentDate.Ticks);
        var key = today.ToString("yyyy-MM-dd");
        if (key == _shownFor) return;
        _shownFor = key;
        var era = Eras.At(today);
        if (era == null) return;
        var pt = UiKit.IsPortuguese();
        var years = $"{era.From[..4]}–{(era.To != null ? era.To[..4] : (pt ? "hoje" : "today"))}";
        _era!.text = $"{era.Name.ToUpperInvariant()}  <size=60%><color=#FFB300>{(pt ? "ERA ATUAL" : "CURRENT ERA")}</color></size>";
        var line = $"{Eras.VersionAt(today)}  <alpha=#99>·  {years}";
        if (Eras.NextVersion(today) is { } v)
            line += pt ? $"  ·  próxima versão: {Short(v.Version)} em {Month(v.Date, pt)}" : $"  ·  next version: {Short(v.Version)} in {Month(v.Date, pt)}";
        _version!.text = line;
    }

    static string Short(string version) => version.Replace("Counter-Strike: Global Offensive", "CS:GO").Replace("Counter-Strike 2", "CS2").Replace("Counter-Strike ", "CS ");

    void Build(Transform topBar, Transform home)
    {
        var style = home.GetComponentInChildren<TMP_Text>(true);
        if (style == null) return;
        // The top bar between the screen title and the date is empty on the dashboard.
        var panel = UiKit.Image(topBar, PanelName, new Color(1f, 1f, 1f, 0.04f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(90f, 0f), new Vector2(880f, 70f));
        panel.raycastTarget = false;
        UiKit.Image(panel.transform, "Accent", Gold, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 0f)).raycastTarget = false;

        TMP_Text Line(string name, float size, Color colour, float top, float height)
        {
            var t = UiKit.Text(panel.transform, name, style, "", size, colour, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(20f, -top), new Vector2(-30f, height));
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }
        _era = Line("Era", 32f, Color.white, 2f, 40f);
        _era.fontStyle = FontStyles.Bold;
        _version = Line("Version", 18f, new Color(1f, 1f, 1f, 0.85f), 42f, 26f);
        _panel = panel.gameObject;
        Plugin.Log.LogInfo("[EraIndicator] banner added to the top bar");
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
