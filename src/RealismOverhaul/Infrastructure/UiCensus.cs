using System.Text;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealismOverhaul.Infrastructure;

/// Diagnostics: a census of the colours, sprites and fonts the game's UI actually uses (ADR 0005).
///
/// Era Themes recolour the game's own palette and nothing else, so we first need that palette.
/// Every two seconds this sweeps the visible UI and counts each distinct (kind, colour, sprite or
/// font) with the screens it appears on and an example path; button tint blocks are recorded too.
/// The report is rewritten to BepInEx/RealismOverhaul-ui-census.txt whenever something new shows
/// up, so browsing the game's screens for a few minutes is enough.
public class UiCensusBehaviour : MonoBehaviour
{
    public UiCensusBehaviour(IntPtr ptr) : base(ptr) { }

    sealed class Entry
    {
        public readonly HashSet<int> Objects = new();
        public readonly HashSet<string> Screens = new();
        public string Example = "";
    }

    readonly Dictionary<string, Entry> _entries = new();
    float _nextSweep;
    bool _dirty;
    float _nextWrite;

    void Update()
    {
        if (Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + 2f;
        try
        {
            Sweep();
            if (_dirty && Time.unscaledTime >= _nextWrite)
            {
                Write();
                _dirty = false;
                _nextWrite = Time.unscaledTime + 10f;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[UiCensus] {e.Message}");
            _nextSweep = Time.unscaledTime + 30f;
        }
    }

    void Sweep()
    {
        foreach (var g in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if (g == null || !g.isActiveAndEnabled) continue;
            string kind, detail;
            var text = g.TryCast<TMP_Text>();
            if (text != null)
            {
                kind = "text";
                detail = text.font != null ? text.font.name : "?";
            }
            else if (g.TryCast<Image>() is { } image)
            {
                kind = "image";
                detail = image.sprite != null ? $"{image.sprite.name} ({image.type})" : "(no sprite)";
            }
            else if (g.TryCast<RawImage>() is { } raw)
            {
                kind = "raw";
                detail = raw.texture != null ? raw.texture.name : "(no texture)";
            }
            else continue;
            Note($"{kind}\t{Hex(g.color)}\t{detail}", g.transform);
        }
        foreach (var s in Resources.FindObjectsOfTypeAll<Selectable>())
        {
            if (s == null || !s.isActiveAndEnabled || s.transition != Selectable.Transition.ColorTint) continue;
            var c = s.colors;
            Note($"tint\t{Hex(c.normalColor)} {Hex(c.highlightedColor)} {Hex(c.pressedColor)} {Hex(c.selectedColor)} {Hex(c.disabledColor)}\tx{c.colorMultiplier:0.##}", s.transform);
        }
    }

    void Note(string key, Transform t)
    {
        if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry();
        if (e.Objects.Add(t.gameObject.GetInstanceID()))
        {
            _dirty = true;
            if (e.Example.Length == 0) e.Example = PathOf(t);
        }
        if (e.Screens.Add(ScreenOf(t))) _dirty = true;
    }

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    static string ScreenOf(Transform t)
    {
        // The child of the root canvas: the screen or window this object belongs to.
        Transform last = t;
        while (t.parent != null)
        {
            last = t;
            t = t.parent;
        }
        return $"{t.name}/{last.name}";
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var i = 0; t != null && i < 8; i++, t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    void Write()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Realism Overhaul UI census {DateTime.Now:yyyy-MM-dd HH:mm}  (kind, colour RGBA, sprite/font, objects, screens, example)");
        foreach (var (key, e) in _entries.OrderByDescending(x => x.Value.Objects.Count))
            sb.AppendLine($"{key}\t{e.Objects.Count}\t{e.Screens.Count}\t{e.Example}\t[{string.Join(", ", e.Screens.Take(6))}]");
        File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "RealismOverhaul-ui-census.txt"), sb.ToString());
    }
}

public static class UiCensus
{
    public static void Start()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<UiCensusBehaviour>();
            var go = new GameObject("RealismOverhaul.UiCensus");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<UiCensusBehaviour>();
            Plugin.Log.LogInfo("[UiCensus] recording UI colours to BepInEx/RealismOverhaul-ui-census.txt");
        }
        catch (Exception e) { Plugin.Log.LogError($"[UiCensus] could not start: {e}"); }
    }
}
