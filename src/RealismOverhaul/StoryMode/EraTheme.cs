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
/// and a handful of coloured button sprites, over a 3D scene (UI census). The theme:
///  * puts an Era backdrop behind each main canvas, so nothing sits on an unknown background;
///  * recolours the neutral overlays and panel sprites and swaps the coloured button sprites for
///    drawn ones; saturated colours (status, team colours, icons, photos, flags) are never touched;
///  * picks every text's colour by contrast with what is actually behind it (the nearest opaque
///    panel, or the backdrop), so text is never dark on dark or light on light;
///  * rebuilds fonts from the Windows font files the Era names, with the game's font as fallback.
/// Screens are themed as they appear: a sweep every half second handles new objects and objects
/// whose colour the game has just reset.
public class EraThemeBehaviour : MonoBehaviour
{
    public EraThemeBehaviour(IntPtr ptr) : base(ptr) { }

    float _nextSweep, _nextSlow;
    int _sweeps;

    void Start() => EraPainter.Init();

    void Update()
    {
        if (!EraPainter.Active || Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + 0.5f;
        var sw = Stopwatch.StartNew();
        int changed;
        try
        {
            var slow = Time.unscaledTime >= _nextSlow;
            if (slow) _nextSlow = Time.unscaledTime + 2f;
            changed = EraPainter.Sweep(slow);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[EraTheme] {e}");
            _nextSweep = Time.unscaledTime + 10f;
            return;
        }
        if (++_sweeps <= 5 || (changed > 200 && _sweeps % 20 == 0))
            Plugin.Log.LogInfo($"[EraTheme] sweep {_sweeps}: {changed} themed in {sw.ElapsedMilliseconds} ms");
    }
}

/// The theme logic (kept out of the injected MonoBehaviour, whose methods Il2Cpp must see).
static class EraPainter
{
    internal static Era? Era;
    internal static bool Active => Era?.Theme.Palette != null;

    // Sprites the game uses for panels and backgrounds (recoloured; plain ones get the Era panel).
    static readonly HashSet<string> PlainPanels = new()
    {
        "", "white", "UISprite", "Window001_White2", "EventPanel", "rounded-square", "SPR_HUD_MilitaryCombat_Shadow_Octagon_01",
    };
    static readonly HashSet<string> ButtonSprites = new() { "Btn08Blue_n", "Btn01Cyan_p", "Btn01Blue_p", "BgTitle_SkillButton02Orange" };
    static readonly HashSet<string> BodyFonts = new() { "mainFont" };
    static readonly HashSet<string> TitleFonts = new() { "coolvetica condensed rg SDF", "Play-Regular SDF", "coolveticaHardBlack" };
    const string BackdropName = "RealismOverhaul.EraBackdrop";

    static readonly Dictionary<int, Color> Applied = new();
    static TMP_FontAsset? _body, _title;
    static Sprite? _panel, _button;
    static Color _background, _text, _textDim, _darkText, _darkDim, _lightText, _lightDim;
    static float _backdropAlpha;

    internal static void Init()
    {
        if (!Active) return;
        var t = Era!.Theme;
        _background = t.Colour("background", new Color(0.1f, 0.1f, 0.1f));
        _text = t.Colour("text", Color.white);
        _textDim = t.Colour("text_dim", Color.gray);
        // Light and dark text pairs, so any background can get a readable colour.
        var textIsDark = Luminance(_text) < 0.5f;
        _darkText = textIsDark ? _text : new Color(0.05f, 0.05f, 0.05f);
        _darkDim = textIsDark ? _textDim : new Color(0.27f, 0.27f, 0.27f);
        _lightText = textIsDark ? new Color(0.95f, 0.95f, 0.95f) : _text;
        _lightDim = textIsDark ? new Color(0.72f, 0.74f, 0.78f) : _textDim;
        _backdropAlpha = t.Style switch { "bevel" => 1f, "glossy" => 0.93f, "flat" => 0.8f, _ => 0f };
        _panel = EraSprites.Make(t.Style, t.CornerRadius, button: false);
        _button = EraSprites.Make(t.Style, t.CornerRadius, button: true);
        _body = LoadFont(t.Font);
        _title = LoadFont(t.FontTitle) ?? _body;
        Plugin.Log.LogInfo($"[EraTheme] {Era.Name}: style {t.Style}, font {(_body != null ? t.Font : "game")}, backdrop {_backdropAlpha:0.##}");
    }

    internal static int Sweep(bool slow)
    {
        var changed = 0;
        if (slow) EnsureBackdrops();
        var graphics = UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        // Backgrounds first, so texts are judged against their themed backgrounds.
        foreach (var g in graphics)
            if (g != null && g.TryCast<TMP_Text>() == null && ThemeImage(g)) changed++;
        foreach (var g in graphics)
            if (g != null && g.TryCast<TMP_Text>() is { } text && ThemeText(text)) changed++;
        if (slow)
            foreach (var s in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (s != null) Tint(s);
        return changed;
    }

    // ---------------------------------------------------------------- backdrop

    static readonly HashSet<int> Backdropped = new();

    static void EnsureBackdrops()
    {
        if (_backdropAlpha <= 0) return;
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (canvas == null || !canvas.isRootCanvas || !Backdropped.Add(canvas.GetInstanceID())) continue;
            var path = canvas.transform.root.name + "/" + canvas.name;
            // The main windows, popups and main menu; not loading screens, tooltips or world canvases.
            if (canvas.renderMode == RenderMode.WorldSpace || path.Contains("Persistance") || path.Contains("Tooltip")
                || canvas.transform.Find(BackdropName) != null) continue;
            var rt = canvas.GetComponent<RectTransform>();
            if (rt == null || rt.rect.width < Screen.width * 0.5f) continue;
            var go = new GameObject(BackdropName);
            var brt = go.AddComponent<RectTransform>();
            brt.SetParent(canvas.transform, false);
            brt.SetAsFirstSibling();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = go.AddComponent<Image>();
            image.color = WithAlpha(_background, _backdropAlpha);
            image.raycastTarget = false;
            Applied[image.GetInstanceID()] = image.color;
            Plugin.Log.LogInfo($"[EraTheme] backdrop behind {path}");
        }
    }

    // ---------------------------------------------------------------- panels and buttons

    static bool ThemeImage(Graphic g)
    {
        var id = g.GetInstanceID();
        var current = g.color;
        if (Applied.TryGetValue(id, out var done) && done == current) return false;
        var image = g.TryCast<Image>();
        if (image == null)
        {
            Applied[id] = current;
            return false;
        }
        var t = Era!.Theme;
        var spriteName = image.sprite != null ? image.sprite.name : "";
        if (ButtonSprites.Contains(spriteName))
        {
            image.sprite = _button;
            image.type = Image.Type.Sliced;
            image.color = IsNeutral(current) && Luminance(current) > 0.8f
                ? WithAlpha(t.Colour("button", current), current.a)
                : WithAlpha(t.Colour("panel_alt", current), current.a);
        }
        else if (PlainPanels.Contains(spriteName) || spriteName.StartsWith("SPR_HUD_MilitaryCombat_Gradient", StringComparison.Ordinal))
        {
            if (PlainPanels.Contains(spriteName) && IsPanelName(g.name) && _panel != null && current.a > 0.3f)
            {
                image.sprite = _panel;
                image.type = Image.Type.Sliced;
            }
            image.color = PanelColour(current, t);
        }
        else
        {
            Applied[id] = current; // icons, photos, flags, stars: untouched
            return false;
        }
        Applied[id] = image.color;
        return true;
    }

    static Color PanelColour(Color c, EraThemeData t)
    {
        if (IsNeutral(c))
        {
            var l = Luminance(c);
            if (l < 0.3f)
            {
                // Translucent black overlays become the Era's panels; bevelled panels are opaque.
                var minAlpha = t.Style == "bevel" && c.a > 0.15f ? 0.95f : 0f;
                return WithAlpha(t.Colour("panel", c), Math.Max(c.a, minAlpha));
            }
            if (l > 0.75f && c.a < 0.35f) return WithAlpha(t.Colour("panel_alt", c), c.a);
            return WithAlpha(t.Colour("header", c), c.a);
        }
        Color.RGBToHSV(c, out var h, out var s, out _);
        var deg = h * 360f;
        // The game's gold highlights and blue HUD accents become the Era accent; status colours stay.
        if (s > 0.4f && ((deg >= 30 && deg <= 60) || (deg >= 185 && deg <= 225)))
            return WithAlpha(t.Colour("accent", c), c.a);
        return c;
    }

    // ---------------------------------------------------------------- text

    static bool ThemeText(TMP_Text text)
    {
        var id = text.GetInstanceID();
        var current = text.color;
        if (Applied.TryGetValue(id, out var done) && done == current) return false;
        var fontName = text.font != null ? text.font.name : "";
        if (_body != null && BodyFonts.Contains(fontName)) text.font = _body;
        else if (_title != null && TitleFonts.Contains(fontName)) text.font = _title;
        text.color = TextColour(current, BackgroundLuminance(text.transform));
        Applied[id] = text.color;
        return true;
    }

    static Color TextColour(Color c, float background)
    {
        var onLight = background > 0.5f;
        if (c.a < 0.02f) return c; // hidden text stays hidden
        if (IsNeutral(c, 0.25f))
        {
            var l = Luminance(c);
            var secondary = c.a < 0.9f || l < 0.82f;
            // Faded or grey text is secondary: the dim colour at full strength stays readable.
            var colour = onLight ? (secondary ? _darkDim : _darkText) : (secondary ? _lightDim : _lightText);
            return WithAlpha(colour, c.a < 0.9f ? Math.Max(c.a, 0.85f) : c.a);
        }
        // Saturated text keeps its hue; on light backgrounds it is darkened to stay readable.
        if (onLight)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            return WithAlpha(Color.HSVToRGB(h, Math.Max(s, 0.6f), Math.Min(v, 0.5f)), Math.Max(c.a, 0.85f));
        }
        return c;
    }

    /// Luminance of what is drawn behind a UI element: the nearest opaque panel among its
    /// ancestors (or a background image next to one of them), else the Era backdrop.
    static float BackgroundLuminance(Transform t)
    {
        var node = t;
        for (var depth = 0; depth < 10 && node.parent != null; depth++)
        {
            var parent = node.parent;
            // Backgrounds drawn before this node at the same level ("bg" next to "Text").
            for (var i = node.GetSiblingIndex() - 1; i >= 0; i--)
                if (Opaque(parent.GetChild(i), out var lum, requireBgName: true)) return lum;
            if (Opaque(parent, out var own, requireBgName: false)) return own;
            node = parent;
        }
        return _backdropAlpha > 0.5f ? Luminance(_background) : 0.1f; // the 3D scene is dark
    }

    static bool Opaque(Transform t, out float luminance, bool requireBgName)
    {
        luminance = 0;
        if (!t.gameObject.activeInHierarchy) return false;
        if (t.name == BackdropName)
        {
            luminance = Luminance(_background);
            return _backdropAlpha > 0.5f;
        }
        if (requireBgName && !IsPanelName(t.name)) return false;
        var image = t.GetComponent<Image>();
        if (image == null || !image.enabled || image.color.a < 0.5f) return false;
        var spriteName = image.sprite != null ? image.sprite.name : "";
        // Only panels whose look we know; photos and logos say nothing reliable about contrast.
        var known = PlainPanels.Contains(spriteName) || ButtonSprites.Contains(spriteName) || spriteName.StartsWith("RealismOverhaul.Era.", StringComparison.Ordinal);
        if (!known) return false;
        var shade = spriteName.StartsWith("RealismOverhaul.Era.", StringComparison.Ordinal) ? 0.85f : 1f;
        luminance = Luminance(image.color) * shade;
        return true;
    }

    // ---------------------------------------------------------------- buttons' hover tints

    static void Tint(Selectable s)
    {
        if (s.transition != Selectable.Transition.ColorTint) return;
        var accent = Era!.Theme.Colour("accent", s.colors.highlightedColor);
        var block = s.colors;
        var changed = false;
        if (!IsNeutral(block.highlightedColor) && block.highlightedColor != accent) { block.highlightedColor = accent; changed = true; }
        if (!IsNeutral(block.pressedColor) && block.pressedColor != accent * 0.85f) { block.pressedColor = accent * 0.85f; changed = true; }
        if (!IsNeutral(block.selectedColor) && block.selectedColor != accent) { block.selectedColor = accent; changed = true; }
        if (changed) s.colors = block;
    }

    // ---------------------------------------------------------------- helpers

    static bool IsPanelName(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("bg") || n.Contains("panel") || n.Contains("background") || n.Contains("window");
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
            EraPainter.Era = era;
            ClassInjector.RegisterTypeInIl2Cpp<EraThemeBehaviour>();
            var go = new GameObject("RealismOverhaul.EraTheme");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<EraThemeBehaviour>();
        }
        catch (Exception e) { Plugin.Log.LogError($"[EraTheme] could not start: {e}"); }
    }
}
