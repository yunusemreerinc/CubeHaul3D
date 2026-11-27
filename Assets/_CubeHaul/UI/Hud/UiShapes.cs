using UnityEngine;

namespace CubeHaul.UI.Hud
{
    /// <summary>
    /// The few shapes the tray and the slots are drawn with, made in code at run time (no image assets): a rounded
    /// rectangle and its outline, nine-sliced so any size keeps round corners, and diagonal "on hold" stripes that tile.
    /// White, so an Image's color tints them. Nothing here is saved (HideFlags.DontSave).
    /// </summary>
    public static class UiShapes
    {
        /// <summary>Corner radius baked into the rounded sprites, pixels (also the nine-slice border).</summary>
        public const int Radius = 32;
        const int Size = Radius * 2 + 4;

        static Sprite rounded, outline, stripes;

        /// <summary>A filled rounded rectangle (Image.Type.Sliced).</summary>
        public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded(fill: true));

        /// <summary>A rounded rectangle's 3-pixel outline (Image.Type.Sliced).</summary>
        public static Sprite Outline => outline != null ? outline : (outline = MakeRounded(fill: false));

        /// <summary>Diagonal stripes, 45°, half covered (Image.Type.Tiled).</summary>
        public static Sprite Stripes => stripes != null ? stripes : (stripes = MakeStripes());

        /// <summary>
        /// Image.pixelsPerUnitMultiplier for a corner radius of <paramref name="radius"/> canvas units (the canvas has
        /// the default 100 reference pixels per unit, the sprites 100 pixels per unit).
        /// </summary>
        public static float RadiusMultiplier(float radius) => Radius / Mathf.Max(0.5f, radius);

        static Sprite MakeRounded(bool fill)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = fill ? "Rounded" : "Rounded Outline", hideFlags = HideFlags.DontSave };
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[Size * Size];
            float half = Size / 2f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // Signed distance to a rounded square of side Size - 2 with corner radius Radius.
                    float px = Mathf.Abs(x + 0.5f - half), py = Mathf.Abs(y + 0.5f - half);
                    float inner = half - 1 - Radius;
                    float qx = Mathf.Max(px - inner, 0), qy = Mathf.Max(py - inner, 0);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - Radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (!fill) a *= Mathf.Clamp01(d + 3.5f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            int b = Radius + 1;
            Sprite s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = tex.name;
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        static Sprite MakeStripes()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Stripes", hideFlags = HideFlags.DontSave };
            tex.wrapMode = TextureWrapMode.Repeat;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Bands along x + y: 16 of 32 pixels covered, soft edges.
                    float u = ((x + y) % n + 0.5f) / n * 2 * Mathf.PI;
                    float a = Mathf.Clamp01(Mathf.Sin(u) * 3);
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            s.name = tex.name;
            s.hideFlags = HideFlags.DontSave;
            return s;
        }
    }
}
