using System.Diagnostics;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace RealismOverhaul.StoryMode;

/// Applies an Era Theme to the game's UI (ADR 0005, GLOSSARY: Era Theme).
///
/// The game draws its UI with white text, translucent black overlays, a few neutral panel sprites
/// and a handful of coloured button sprites (UI census). The theme recolours exactly those neutral
/// colours and swaps those sprites for drawn ones; saturated colours (status greens and reds, team
/// colours, icons, photos, flags) are never touched, so they keep their meaning. Fonts are rebuilt
/// from the Windows font files the Era names, with the game's font as fallback for missing glyphs.
/// Screens are themed as they appear: a sweep every half second handles new objects and objects
/// whose colour the game has just reset.
public class EraThemeBehaviour : MonoBehaviour
{
    public EraThemeBehaviour(IntPtr ptr) : base(ptr) { }

    internal static Era? Era;

    // Sprites the game uses for panels and backgrounds (recoloured; plain ones get the Era panel).
    static readonly HashSet<string> PanelSprites = new()
    {
        "", "white", "UISprite", "Window001_White2", "EventPanel", "rounded-square",
        "SPR_HUD_MilitaryCombat_Shadow_Octagon_01",
    };
    // Coloured button sprites replaced by the Era's button.
    static readonly HashSet<string> ButtonSprites = new() { "Btn08Blue_n", "Btn01Cyan_p", "Btn01Blue_p", "BgTitle_SkillButton02Orange" };
    // Fonts replaced: the body font and the game's display fonts.
    static readonly HashSet<string> BodyFonts = new() { "mainFont" };
    static readonly HashSet<string> TitleFonts = new() { "coolvetica condensed rg SDF", "Play-Regular SDF", "coolveticaHardBlack" };

    readonly Dictionary<int, Color> _applied = new();
    TMP_FontAsset? _body, _title;
    Sprite? _panel, _button;
    bool _light;
    float _nextSweep, _nextTints;
    int _sweeps;

    void Start()
    {
        if (Era?.Theme.Palette == null) return;
        var t = Era.Theme;
        _light = Luminance(t.Colour("text", Color.white)) < 0.5f;
        _panel = EraSprites.Make(t.Style, t.CornerRadius, button: false);
        _button = EraSprites.Make(t.Style, t.CornerRadius, button: true);
        _body = LoadFont(t.Font);
        _title = LoadFont(t.FontTitle) ?? _body;
        Plugin.Log.LogInfo($"[EraTheme] {Era.Name}: style {t.Style}, font {(_body != null ? t.Font : "game")}, {(_light ? "light" : "dark")} palette");
    }

    void Update()
    {
        if (Era?.Theme.Palette == null || Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + 0.5f;
        var sw = Stopwatch.StartNew();
        var changed = 0;
        try
        {
            foreach (var g in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (g != null && Theme(g)) changed++;
            if (Time.unscaledTime >= _nextTints)
            {
                _nextTints = Time.unscaledTime + 2f;
                foreach (var s in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (s != null) Tint(s);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[EraTheme] {e.Message}");
            _nextSweep = Time.unscaledTime + 10f;
        }
        if (++_sweeps <= 5 || (changed > 200 && _sweeps % 20 == 0))
            Plugin.Log.LogInfo($"[EraTheme] sweep {_sweeps}: {changed} themed in {sw.ElapsedMilliseconds} ms");
    }

    bool Theme(Graphic g)
    {
        var id = g.GetInstanceID();
        var current = g.color;
        if (_applied.TryGetValue(id, out var done) && done == current) return false;
        var t = Era!.Theme;

        if (g.TryCast<TMP_Text>() is { } text)
        {
            var fontName = text.font != null ? text.font.name : "";
            if (_body != null && BodyFonts.Contains(fontName)) text.font = _body;
            else if (_title != null && TitleFonts.Contains(fontName)) text.font = _title;
            text.color = TextColour(current, t);
        }
        else if (g.TryCast<Image>() is { } image)
        {
            var spriteName = image.sprite != null ? image.sprite.name : "";
            if (ButtonSprites.Contains(spriteName))
            {
                image.sprite = _button;
                image.type = Image.Type.Sliced;
                image.color = IsNeutral(current) && Luminance(current) > 0.8f
                    ? WithAlpha(t.Colour("button", current), current.a)
                    : WithAlpha(t.Colour("panel_alt", current), current.a);
            }
            else if (PanelSprites.Contains(spriteName) || spriteName.StartsWith("SPR_HUD_MilitaryCombat_Gradient", StringComparison.Ordinal))
            {
                var plain = spriteName is "" or "white" or "UISprite" or "Window001_White2" or "EventPanel" or "SPR_HUD_MilitaryCombat_Shadow_Octagon_01";
                if (plain && IsPanelName(g.name) && _panel != null)
                {
                    image.sprite = _panel;
                    image.type = Image.Type.Sliced;
                }
                image.color = PanelColour(current, t);
            }
            else
            {
                _applied[id] = current; // icons, photos, flags, stars: untouched
                return false;
            }
        }
        else
        {
            _applied[id] = current;
            return false;
        }
        _applied[id] = g.color;
        return true;
    }

    Color TextColour(Color c, EraThemeData t)
    {
        if (IsNeutral(c, 0.25f))
        {
            var l = Luminance(c);
            if (l > 0.82f) return WithAlpha(t.Colour("text", c), c.a);
            if (l > 0.45f) return WithAlpha(t.Colour("text_dim", c), c.a);
            return c;
        }
        // Saturated text keeps its meaning; on a light palette it is darkened to stay readable.
        if (_light)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            var d = Color.HSVToRGB(h, s, Math.Min(v, 0.55f));
            return WithAlpha(d, c.a);
        }
        return c;
    }

    Color PanelColour(Color c, EraThemeData t)
    {
        var l = Luminance(c);
        if (IsNeutral(c))
        {
            if (l < 0.3f)
            {
                // Translucent black overlays become the Era's panels; light palettes need them opaque.
                var minAlpha = t.Style switch { "bevel" => 0.94f, "glossy" => 0.7f, _ => 0f };
                return WithAlpha(t.Colour("panel", c), c.a <= 0.02f ? c.a : Math.Max(c.a, minAlpha));
            }
            if (l > 0.75f && c.a < 0.35f) return WithAlpha(t.Colour(_light ? "border" : "panel_alt", c), c.a);
            return WithAlpha(t.Colour("header", c), c.a);
        }
        Color.RGBToHSV(c, out var h, out var s, out _);
        var deg = h * 360f;
        // The game's gold highlights and blue HUD accents become the Era accent; status colours stay.
        if (s > 0.4f && ((deg >= 30 && deg <= 60) || (deg >= 185 && deg <= 225)))
            return WithAlpha(t.Colour("accent", c), c.a);
        return c;
    }

    void Tint(Selectable s)
    {
        if (s.transition != Selectable.Transition.ColorTint) return;
        var t = Era!.Theme;
        var block = s.colors;
        var accent = t.Colour("accent", block.highlightedColor);
        var changed = false;
        if (!IsNeutral(block.highlightedColor)) { block.highlightedColor = accent; changed = true; }
        if (!IsNeutral(block.pressedColor)) { block.pressedColor = accent * 0.85f; changed = true; }
        if (!IsNeutral(block.selectedColor)) { block.selectedColor = accent; changed = true; }
        if (changed) s.colors = block;
    }

    static bool IsPanelName(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("bg") || n.Contains("panel") || n.Contains("background") || n.Contains("window") || n.Contains("container");
    }

    static bool IsNeutral(Color c, float tolerance = 0.12f) =>
        Math.Max(c.r, Math.Max(c.g, c.b)) - Math.Min(c.r, Math.Min(c.g, c.b)) < tolerance;

    static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    static TMP_FontAsset? LoadFont(string? family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        var file = family switch
        {
            "Tahoma" => "tahoma.ttf", "Verdana" => "verdana.ttf", "Trebuchet MS" => "trebuc.ttf",
            "Segoe UI" => "segoeui.ttf", "Segoe UI Semibold" => "seguisb.ttf", "Bahnschrift" => "bahnschrift.ttf",
            "Arial" => "arial.ttf", _ => family,
        };
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"[EraTheme] font file not found: {path}");
            return null;
        }
        try
        {
            var font = TMP_FontAsset.CreateFontAsset(path, 0, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024);
            if (font == null) throw new InvalidOperationException("CreateFontAsset returned null");
            font.name = $"RealismOverhaul.Era.{family}";
            font.hideFlags = HideFlags.HideAndDontSave;
            var fallback = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name == "mainFont");
            if (fallback != null)
            {
                font.fallbackFontAssetTable ??= new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                font.fallbackFontAssetTable.Add(fallback);
            }
            return font;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[EraTheme] could not build font {family}: {e.Message}");
            return null;
        }
    }
}

public static class EraTheme
{
    /// Applies an Era Theme for the whole session. Story Mode will choose the Era from the date;
    /// until then the ForceEra diagnostic previews one.
    public static void Start(string eraId)
    {
        var era = Eras.ById(eraId);
        if (era == null)
        {
            Plugin.Log.LogWarning($"[EraTheme] unknown era '{eraId}'; known: {string.Join(", ", Eras.All.Select(e => e.Id))}");
            return;
        }
        try
        {
            EraThemeBehaviour.Era = era;
            ClassInjector.RegisterTypeInIl2Cpp<EraThemeBehaviour>();
            var go = new GameObject("RealismOverhaul.EraTheme");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<EraThemeBehaviour>();
        }
        catch (Exception e) { Plugin.Log.LogError($"[EraTheme] could not start: {e}"); }
    }
}
