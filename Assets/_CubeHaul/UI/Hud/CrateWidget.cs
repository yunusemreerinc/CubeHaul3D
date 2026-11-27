using CubeHaul.Gameplay.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CubeHaul.UI.Hud
{
    /// <summary>
    /// One crate as uGUI images (Task 16): cube-hunt's "pod" (view/draw.ts) — a darker base under the face for depth, the
    /// face in the crate's color with a gloss band, a thin edge — and its count ("countLabel": white lettering with an
    /// outline in a deep shade of the color and a drop under it; dark lettering on light colors). Slot crates also have
    /// the progress bar and the "on hold" stripes. Placed by its bottom middle in canvas units, so it can stretch, dip
    /// and wobble from its base like main.ts's crates. Nothing in it takes touches.
    /// </summary>
    public sealed class CrateWidget
    {
        public RectTransform Root { get; private set; }
        public RectTransform Body { get; private set; }
        public Text Label { get; private set; }

        CanvasGroup bodyGroup;
        Image baseImage, face, gloss, edge, flash;
        RectTransform faceRect, baseRect, glossRect, edgeRect, flashRect, labelRect;
        Outline labelOutline;
        Shadow labelDrop;
        RectTransform stripesClip;
        Image stripes, barTrack, barFill;
        int shownCount = int.MinValue;
        string shownText;

        public bool Visible => Root.gameObject.activeSelf;

        public static CrateWidget Create(Transform parent, string name, bool slotParts, Font font)
        {
            var w = new CrateWidget();
            w.Root = Rect(name, parent);
            w.Root.pivot = new Vector2(0.5f, 0);
            w.Body = Rect("Body", w.Root);
            Stretch(w.Body);
            w.bodyGroup = w.Body.gameObject.AddComponent<CanvasGroup>();
            w.bodyGroup.blocksRaycasts = false;
            w.bodyGroup.interactable = false;
            w.baseImage = ImageOf(w.Body, "Base", UiShapes.Rounded, out w.baseRect);
            w.face = ImageOf(w.Body, "Face", UiShapes.Rounded, out w.faceRect);
            w.gloss = ImageOf(w.Body, "Gloss", UiShapes.Rounded, out w.glossRect);
            w.edge = ImageOf(w.Body, "Edge", UiShapes.Outline, out w.edgeRect);
            if (slotParts)
            {
                w.stripesClip = Rect("Stripes", w.Body);
                Image clip = w.stripesClip.gameObject.AddComponent<Image>();
                clip.sprite = UiShapes.Rounded;
                clip.type = Image.Type.Sliced;
                clip.raycastTarget = false;
                w.stripesClip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                w.stripes = ImageOf(w.stripesClip, "Lines", UiShapes.Stripes, out RectTransform lines);
                w.stripes.type = Image.Type.Tiled;
                Stretch(lines);
                w.barTrack = ImageOf(w.Root, "Bar", UiShapes.Rounded, out _);
                w.barFill = ImageOf(w.barTrack.rectTransform, "Fill", UiShapes.Rounded, out _);
            }
            w.flash = ImageOf(w.Body, "Flash", UiShapes.Rounded, out w.flashRect);
            w.labelRect = Rect("Count", w.Root);
            w.Label = w.labelRect.gameObject.AddComponent<Text>();
            w.Label.font = font;
            w.Label.fontStyle = FontStyle.Bold;
            w.Label.alignment = TextAnchor.MiddleCenter;
            w.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            w.Label.verticalOverflow = VerticalWrapMode.Overflow;
            w.Label.raycastTarget = false;
            w.labelDrop = w.labelRect.gameObject.AddComponent<Shadow>();
            w.labelOutline = w.labelRect.gameObject.AddComponent<Outline>();
            return w;
        }

        public void Show(bool visible)
        {
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Puts the crate with its bottom middle at <paramref name="bottomCenter"/> (canvas units), <paramref name="width"/>
        /// by <paramref name="height"/>; <paramref name="depth"/> of it is the darker base (main.ts: max(3, h · 0.1)).
        /// </summary>
        public void Place(Vector2 bottomCenter, float width, float height, float depth, float radius, float pivotY = 0)
        {
            // Scaled and turned around (0.5, pivotY) of the box: its base, or its middle for a crate spinning away.
            Root.pivot = new Vector2(0.5f, pivotY);
            Root.anchoredPosition = bottomCenter + new Vector2(0, pivotY * height);
            Root.sizeDelta = new Vector2(width, height);
            float faceH = Mathf.Max(1, height - depth);
            float mult = UiShapes.RadiusMultiplier(radius);
            Set(baseRect, 0, 0, width, faceH);
            Set(faceRect, 0, depth, width, faceH);
            Set(edgeRect, 0, depth, width, faceH);
            Set(flashRect, 0, depth, width, faceH);
            // The gloss band: 84% wide, the top 46% of the face, a little below its edge.
            Set(glossRect, width * 0.08f, depth + faceH * 0.54f - faceH * 0.03f, width * 0.84f, faceH * 0.46f);
            baseImage.pixelsPerUnitMultiplier = face.pixelsPerUnitMultiplier = edge.pixelsPerUnitMultiplier = flash.pixelsPerUnitMultiplier = mult;
            gloss.pixelsPerUnitMultiplier = mult / 0.75f;
            if (stripesClip != null)
            {
                Set(stripesClip, 0, depth, width, faceH);
                stripesClip.GetComponent<Image>().pixelsPerUnitMultiplier = mult;
            }
        }

        /// <summary>Colors the crate; <paramref name="alpha"/> fades its body.</summary>
        public void Paint(PaletteColor color, float alpha)
        {
            baseImage.color = ToColor(color.Shade(-0.32));
            face.color = ToColor(color);
            gloss.color = new Color(1, 1, 1, 0.3f);
            edge.color = ToColor(color.Shade(-0.45), 0.35f);
            bodyGroup.alpha = alpha;
        }

        /// <summary>The press flash over the face (main.ts pressGlow: white at 0.22 · dip).</summary>
        public void Flash(float amount) => flash.color = new Color(1, 1, 1, 0.22f * Mathf.Clamp01(amount));

        /// <summary>
        /// The count, centered <paramref name="fromBottom"/> canvas units above the crate's bottom, <paramref name="size"/>
        /// canvas units high. <paramref name="flat"/>: no drop and a thinner outline (small or faded crates).
        /// </summary>
        public void Count(int count, PaletteColor color, float size, float fromBottom, float alpha, bool flat, float pop = 1)
        {
            if (count != shownCount)
            {
                shownCount = count;
                Label.text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            StyleLabel(color, size, fromBottom, alpha, flat, pop);
        }

        /// <summary>A text label instead of a count (the "0" of a leaving crate, or a pill's word).</summary>
        public void Text(string text, PaletteColor color, float size, float fromBottom, float alpha, bool flat)
        {
            if (!ReferenceEquals(text, shownText))
            {
                shownText = text;
                shownCount = int.MinValue;
                Label.text = text;
            }
            StyleLabel(color, size, fromBottom, alpha, flat, 1);
        }

        void StyleLabel(PaletteColor color, float size, float fromBottom, float alpha, bool flat, float pop)
        {
            bool light = color.IsLight;
            Label.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            Label.color = light ? ToColor(color.Shade(-0.6), alpha) : new Color(1, 1, 1, alpha);
            float outline = size * (flat ? 0.1f : 0.16f) * 0.5f;
            labelOutline.effectColor = light ? new Color(1, 1, 1, alpha) : ToColor(color.Shade(-0.62), alpha);
            labelOutline.effectDistance = new Vector2(outline, -outline);
            labelDrop.enabled = !flat;
            labelDrop.effectColor = ToColor(light ? color.Shade(-0.2) : color.Shade(-0.75), alpha * 0.9f);
            labelDrop.effectDistance = new Vector2(0, -Mathf.Max(1.5f, size * 0.08f) - outline);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0, fromBottom);
            labelRect.sizeDelta = new Vector2(Root.sizeDelta.x * 1.5f + size, size * 1.4f);
            labelRect.localScale = new Vector3(pop, pop, 1);
        }

        /// <summary>The slot's progress bar: <paramref name="progress"/> of the trucks still to go (draw: a recessed track, a bright fill).</summary>
        public void Bar(bool shown, PaletteColor color, float progress, float x, float bottom, float width, float height, float alpha)
        {
            if (barTrack == null) return;
            if (barTrack.gameObject.activeSelf != shown) barTrack.gameObject.SetActive(shown);
            if (!shown) return;
            var track = barTrack.rectTransform;
            track.anchorMin = track.anchorMax = Vector2.zero;
            track.pivot = Vector2.zero;
            track.anchoredPosition = new Vector2(x, bottom);
            track.sizeDelta = new Vector2(width, height);
            float mult = UiShapes.RadiusMultiplier(height / 2);
            barTrack.pixelsPerUnitMultiplier = barFill.pixelsPerUnitMultiplier = mult;
            barTrack.color = new Color(0, 0, 0, 0.22f * alpha);
            var fill = barFill.rectTransform;
            fill.anchorMin = fill.anchorMax = Vector2.zero;
            fill.pivot = Vector2.zero;
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(Mathf.Max(height, width * Mathf.Clamp01(progress)), height);
            barFill.color = color.IsLight ? ToColor(color.Shade(-0.55), alpha) : new Color(1, 1, 1, 0.9f * alpha);
        }

        /// <summary>The "on hold" stripes over the face of a blocked crate (draw.ts holdStripes).</summary>
        public void Stripes(bool shown, PaletteColor color, float period)
        {
            if (stripesClip == null) return;
            if (stripesClip.gameObject.activeSelf != shown) stripesClip.gameObject.SetActive(shown);
            if (!shown) return;
            stripes.color = color.Luminance < 0.35 ? new Color(1, 1, 1, 0.16f) : new Color(40 / 255f, 20 / 255f, 5 / 255f, 0.16f);
            stripes.pixelsPerUnitMultiplier = 32f / Mathf.Max(1, period);
        }

        /// <summary>Stretch, wobble and spin around the base.</summary>
        public void Transform(float scaleX, float scaleY, float turnDegrees)
        {
            Root.localScale = new Vector3(scaleX, scaleY, 1);
            Root.localRotation = Quaternion.Euler(0, 0, turnDegrees);
        }

        public static Color ToColor(PaletteColor c, float alpha = 1) => new Color(c.R / 255f, c.G / 255f, c.B / 255f, alpha);

        static void Set(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5, hideFlags = HideFlags.DontSave };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal static Image ImageOf(Transform parent, string name, Sprite sprite, out RectTransform rt)
        {
            rt = Rect(name, parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }
    }
}
