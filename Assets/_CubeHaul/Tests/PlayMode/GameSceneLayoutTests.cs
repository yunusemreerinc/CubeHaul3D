using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Gameplay.Layout;
using CubeHaul.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 11 acceptance in Play Mode: the game scene, built as the Editor builds it, rendered into a texture of each
    /// screen size (16:9 and long portrait screens, with notches), with the real Unity camera and canvas. Nothing may
    /// overflow: the world placeholders (board, ramp, slots, tray) stay between the bars, the bars and their buttons stay
    /// inside the safe area, and the camera puts every point where the engine-free layout says (docs/SCENE_LAYOUT.md).
    /// </summary>
    public class GameSceneLayoutTests
    {
        public sealed class ScreenCase
        {
            public string Name;
            public int W, H;
            public Rect Safe;
            public override string ToString() => Name;
        }

        static ScreenCase S(string name, int w, int h, int bottom = 0, int top = 0) =>
            new ScreenCase { Name = name, W = w, H = h, Safe = new Rect(0, bottom, w, h - bottom - top) };

        public static readonly ScreenCase[] Screens =
        {
            S("16:9 720x1280", 720, 1280),
            S("16:9 1080x1920", 1080, 1920),
            S("19.5:9 1080x2340", 1080, 2340),
            S("19.5:9 iPhone 13 1170x2532", 1170, 2532, bottom: 102, top: 141),
            S("20:9 1080x2400 punch hole", 1080, 2400, top: 96),
            S("21:9 1080x2520", 1080, 2520, top: 110),
            S("4:3 iPad 1536x2048", 1536, 2048),
        };

        GameSceneComposer.Composition scene;
        RenderTexture target;

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
            scene = null;
            target = null;
        }

        /// <summary>The scene rendered into a w x h texture: the camera and the canvas see exactly that screen.</summary>
        IEnumerator Build(ScreenCase s)
        {
            scene = GameSceneComposer.Compose();
            target = new RenderTexture(s.W, s.H, 24);
            target.Create();
            scene.Rig.FollowScreen = false;
            scene.SafeArea.FollowScreen = false;
            scene.Camera.targetTexture = target;
            scene.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            scene.Canvas.worldCamera = scene.Camera;
            scene.Canvas.planeDistance = 1f;
            scene.Rig.Apply(s.W, s.H, s.Safe);
            scene.SafeArea.Apply(new Vector2(s.W, s.H), s.Safe);
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        Rect ScreenRect(Bounds b)
        {
            var points = new List<Vector3>();
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                points.Add(scene.Camera.WorldToScreenPoint(corner));
            }
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }

        Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3[] p = corners.Select(c => scene.Camera.WorldToScreenPoint(c)).ToArray();
            return Rect.MinMaxRect(p.Min(x => x.x), p.Min(x => x.y), p.Max(x => x.x), p.Max(x => x.y));
        }

        static Rect ToRect(RectD r) => Rect.MinMaxRect((float)r.XMin, (float)r.YMin, (float)r.XMax, (float)r.YMax);

        static bool Inside(Rect outer, Rect inner, float tolerance = 1.5f) =>
            inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance && inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;

        [UnityTest]
        public IEnumerator TheWorldStaysBetweenTheBars([ValueSource(nameof(Screens))] ScreenCase s)
        {
            yield return Build(s);
            ScreenLayout layout = scene.Rig.Layout;
            Assert.That(layout.Fits, Is.True, layout.ToString());
            Rect region = ToRect(layout.WorldRegion);
            Assert.That(scene.Preview.Renderers.Count, Is.GreaterThan(10));
            foreach (Renderer r in scene.Preview.Renderers)
            {
                Rect onScreen = ScreenRect(r.bounds);
                Assert.That(Inside(region, onScreen), Is.True, $"{s}: {r.name} at {onScreen} leaves the world region {region}");
            }
        }

        [UnityTest]
        public IEnumerator TheBarsStayInTheSafeArea([ValueSource(nameof(Screens))] ScreenCase s)
        {
            yield return Build(s);
            ScreenLayout layout = scene.Rig.Layout;
            Assert.That(scene.Canvas.scaleFactor, Is.EqualTo((float)layout.CanvasScale).Within(1e-3f), "CanvasScaler Expand on 420 x 900");
            Rect safe = s.Safe;
            Rect header = ScreenRect(scene.Header), footer = ScreenRect(scene.Footer);
            Assert.That(Inside(ToRect(layout.Header), header) && Inside(header, ToRect(layout.Header)), Is.True, $"top bar {header} vs {layout.Header}");
            Assert.That(Inside(ToRect(layout.Footer), footer) && Inside(footer, ToRect(layout.Footer)), Is.True, $"bottom bar {footer} vs {layout.Footer}");
            foreach (RectTransform control in scene.Controls)
            {
                Rect r = ScreenRect(control);
                Assert.That(Inside(safe, r), Is.True, $"{s}: {control.name} at {r} leaves the safe area {safe}");
                Rect bar = control.IsChildOf(scene.Header) ? header : footer;
                Assert.That(Inside(bar, r), Is.True, $"{s}: {control.name} at {r} leaves its bar {bar}");
            }
            for (int a = 0; a < scene.Controls.Count; a++)
                for (int b = a + 1; b < scene.Controls.Count; b++)
                    Assert.That(ScreenRect(scene.Controls[a]).Overlaps(ScreenRect(scene.Controls[b])), Is.False,
                        $"{s}: {scene.Controls[a].name} overlaps {scene.Controls[b].name}");
        }

        [UnityTest]
        public IEnumerator TheUnityCameraProjectsLikeTheLayout([ValueSource(nameof(Screens))] ScreenCase s)
        {
            yield return Build(s);
            ScreenLayout layout = scene.Rig.Layout;
            Assert.That(scene.Camera.orthographic, Is.True);
            Assert.That(scene.Camera.pixelWidth, Is.EqualTo(s.W));
            foreach (var (x, y, h) in new[] { (0.0, 64.0, 0.0), (420.0, 808.0, 0.0), (210.0, 540.0, 40.0), (16.0, 452.0, 10.0) })
            {
                Vector3 p = scene.Camera.WorldToScreenPoint(CubeHaul.Presentation.WorldMapper.ToWorld(x, y, h));
                (double ex, double ey) = layout.DesignToScreen(x, y, h);
                Assert.That(p.x, Is.EqualTo((float)ex).Within(0.5f), $"design ({x}, {y}, {h}) x");
                Assert.That(p.y, Is.EqualTo((float)ey).Within(0.5f), $"design ({x}, {y}, {h}) y");
            }
        }

        [UnityTest]
        public IEnumerator TheLayoutFollowsAScreenChange()
        {
            yield return Build(Screens[1]);
            float before = scene.Camera.orthographicSize;
            ScreenCase tall = Screens[4];
            target.Release();
            target.width = tall.W;
            target.height = tall.H;
            target.Create();
            scene.Rig.Apply(tall.W, tall.H, tall.Safe);
            scene.SafeArea.Apply(new Vector2(tall.W, tall.H), tall.Safe);
            yield return null;
            Assert.That(scene.Camera.orthographicSize, Is.Not.EqualTo(before));
            Assert.That(scene.Rig.Layout.Fits, Is.True);
            var rt = (RectTransform)scene.SafeArea.transform;
            Assert.That(rt.anchorMax.y, Is.EqualTo((tall.H - 96f) / tall.H).Within(1e-5f));
        }
    }
}
