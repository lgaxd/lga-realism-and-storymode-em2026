using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace RealismOverhaul.StoryMode;

/// Panel and button sprites drawn in code for an Era Theme (ADR 0005): nothing is shipped as an
/// image file. Sprites are white-based and 9-sliced, so the game's own tints still apply on top.
public static class EraSprites
{
    /// A panel or button sprite in the given style: "bevel" (raised 2000s look), "glossy"
    /// (late-2000s gradient with a highlight), "flat" or "glass".
    public static Sprite Make(string style, int radius, bool button)
    {
        var size = Math.Max(16, radius * 2 + 8);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.hideFlags = HideFlags.HideAndDontSave;
        var px = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var a = Coverage(x, y, size, radius);
            var v = style switch
            {
                "bevel" => Bevel(x, y, size, button),
                "glossy" => Glossy(y, size, button),
                "glass" => 0.92f + 0.08f * y / (size - 1f),
                _ => 1f,
            };
            // A thin darker rim gives every style a defined edge except flat.
            if (style is "glossy" or "glass" && a > 0 && Coverage(x, y, size, radius, inset: 1) < 1) v *= 0.7f;
            var b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255);
            px[y * size + x] = new Color32(b, b, b, (byte)Mathf.RoundToInt(a * 255));
        }
        tex.SetPixels32(new Il2CppStructArray<Color32>(px));
        tex.Apply(false, true);
        var border = style == "bevel" ? 3 : Math.Max(radius, 1) + 2;
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.name = $"RealismOverhaul.Era.{style}.{(button ? "button" : "panel")}";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// Classic raised bevel: light top-left edges, dark bottom-right edges.
    static float Bevel(int x, int y, int size, bool button)
    {
        var top = size - 1 - y; // texture rows start at the bottom
        if (x == 0 || top == 0) return 1f;
        if (x == size - 1 || y == 0) return 0.35f;
        if (x == 1 || top == 1) return button ? 0.97f : 0.93f;
        if (x == size - 2 || y == 1) return 0.62f;
        return button ? 0.86f : 0.82f;
    }

    /// Late-2000s gloss: bright upper half, darker lower half.
    static float Glossy(int y, int size, bool button)
    {
        var t = y / (size - 1f); // 0 bottom .. 1 top
        var v = t > 0.5f ? 0.88f + 0.12f * (t - 0.5f) * 2f : 0.70f + 0.1f * t * 2f;
        return button ? v : 0.6f + 0.4f * v;
    }

    /// Anti-aliased coverage of a rounded rectangle (1 inside, 0 outside).
    static float Coverage(int x, int y, int size, int radius, int inset = 0)
    {
        float min = inset, max = size - 1 - inset;
        float fx = x, fy = y;
        if (fx < min || fx > max || fy < min || fy > max) return 0f;
        if (radius <= 0) return 1f;
        var r = Math.Max(1f, radius - inset);
        var cx = Mathf.Clamp(fx, min + r, max - r);
        var cy = Mathf.Clamp(fy, min + r, max - r);
        var d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
        return Mathf.Clamp01(r - d + 0.5f);
    }
}
