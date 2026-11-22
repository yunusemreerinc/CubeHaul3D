using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Gameplay;
using CubeHaul.Gameplay.Layout;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The screen layout (docs/SCENE_LAYOUT.md) on phones and tablets in portrait: 16:9 and long screens, notches and
    /// home indicators. Nothing may overflow: the bars stay in the safe area, the world content (board to tray) stays
    /// between them, in the same top-to-bottom order as cube-hunt.
    /// </summary>
    public class ScreenLayoutTests
    {
        public sealed class Screen
        {
            public string Name;
            public double W, H;
            public RectD? Safe;
            public override string ToString() => Name;
        }

        static Screen S(string name, double w, double h, double safeBottom = 0, double safeTop = 0, double safeLeft = 0, double safeRight = 0) =>
            new Screen { Name = name, W = w, H = h, Safe = safeBottom + safeTop + safeLeft + safeRight == 0 ? (RectD?)null : RectD.FromMinMax(safeLeft, safeBottom, w - safeRight, h - safeTop) };

        /// <summary>Portrait screens: 16:9 and taller, with and without notches, and two tablets.</summary>
        public static readonly Screen[] Screens =
        {
            S("16:9 720x1280", 720, 1280),
            S("16:9 1080x1920", 1080, 1920),
            S("16:9 1440x2560", 1440, 2560),
            S("19.5:9 1080x2340", 1080, 2340),
            S("19.5:9 iPhone 13 1170x2532 (notch, home bar)", 1170, 2532, safeBottom: 102, safeTop: 141),
            S("19.5:9 iPhone 15 Pro Max 1290x2796 (island, home bar)", 1290, 2796, safeBottom: 102, safeTop: 177),
            S("19.5:9 iPhone 11 828x1792 (notch)", 828, 1792, safeBottom: 68, safeTop: 88),
            S("20:9 1080x2400 (punch hole)", 1080, 2400, safeTop: 96),
            S("20:9 1440x3200", 1440, 3200, safeTop: 120, safeBottom: 48),
            S("21:9 1080x2520", 1080, 2520, safeTop: 110),
            S("22.5:9 Galaxy Z Fold cover 904x2316", 904, 2316, safeTop: 80),
            S("4:3 iPad 1536x2048", 1536, 2048),
            S("iPad Pro 11 1668x2388 (home bar)", 1668, 2388, safeBottom: 40, safeTop: 48),
            S("5:3 small tablet 1200x2000", 1200, 2000),
        };

        static ScreenLayout Layout(Screen s, double pitch = 60) =>
            ScreenLayout.Compute(new ScreenLayoutInput { ScreenWidth = s.W, ScreenHeight = s.H, SafeArea = s.Safe, PitchDegrees = pitch });

        [TestCaseSource(nameof(Screens))]
        public void NothingOverflows(Screen screen)
        {
            ScreenLayout l = Layout(screen);
            TestContext.WriteLine(l);
            RectD full = new RectD(0, 0, screen.W, screen.H);
            Assert.That(l.Fits, Is.True, l.ToString());
            Assert.That(full.Contains(l.SafeArea), Is.True);
            Assert.That(l.SafeArea.Contains(l.Header, 0.5) && l.SafeArea.Contains(l.Footer, 0.5) && l.SafeArea.Contains(l.WorldRegion, 0.5), Is.True, "everything in the safe area");
            Assert.That(l.WorldRegion.Contains(l.ContentOnScreen, 0.5), Is.True, "board, ramp, slots and tray between the bars");
            Assert.That(l.Header.YMax, Is.EqualTo(l.SafeArea.YMax).Within(1e-9), "top bar at the top of the safe area");
            Assert.That(l.Footer.YMin, Is.EqualTo(l.SafeArea.YMin).Within(1e-9), "bottom bar at the bottom of the safe area");
        }

        [TestCaseSource(nameof(Screens))]
        public void TheCompositionIsCubeHuntsTopToBottom(Screen screen)
        {
            ScreenLayout l = Layout(screen);
            RectD board = l.DesignToScreen(DesignLayout.Board);
            RectD tunnel = l.DesignToScreen(DesignLayout.Tunnel);
            RectD slots = l.DesignToScreen(RectD.FromMinMax(DesignLayout.SlotRect(0, 6).XMin, DesignLayout.SlotY, DesignLayout.SlotRect(5, 6).XMax, DesignLayout.SlotY + DesignLayout.SlotHeight));
            RectD tray = l.DesignToScreen(RectD.FromMinMax(DesignLayout.TrayLeft(5), DesignLayout.TrayY, DesignLayout.Width - DesignLayout.TrayLeft(5), DesignLayout.TrayBottom));
            var order = new[] { ("header", l.Header), ("board", board), ("ramp", tunnel), ("slots", slots), ("tray", tray), ("footer", l.Footer) };
            for (int k = 1; k < order.Length; k++)
                Assert.That(order[k - 1].Item2.YMin, Is.GreaterThanOrEqualTo(order[k].Item2.YMax - 0.5), $"{order[k - 1].Item1} is above {order[k].Item1}");
            foreach (var (name, r) in order.Skip(1).Take(4))
                Assert.That(r.CenterX, Is.EqualTo(l.WorldRegion.CenterX).Within(0.5), name + " is centered");
        }

        [TestCaseSource(nameof(Screens))]
        public void TheContentUsesTheRegionAndTheGridStaysVisible(Screen screen)
        {
            ScreenLayout l = Layout(screen);
            bool widthBound = Math.Abs(l.ContentOnScreen.Width - l.WorldRegion.Width) < 0.5;
            bool heightBound = Math.Abs(l.ContentOnScreen.Height - l.WorldRegion.Height) < 0.5;
            Assert.That(widthBound || heightBound, Is.True, "the content is as large as the region allows");
            Assert.That(l.ContentOnScreen.CenterY, Is.EqualTo(l.WorldRegion.CenterY).Within(0.5));
            // The smallest cell of any level is a 46 x 46 picture's: it must stay a visible square, not a speck.
            double cell = DesignLayout.CellSize(46, 46);
            double across = cell * l.PixelsPerDesignUnit;
            double deep = across * Math.Sin(l.PitchDegrees * Math.PI / 180);
            TestContext.WriteLine($"{screen}: a 46 x 46 cell is {across:0.0} x {deep:0.0} px");
            Assert.That(deep, Is.GreaterThanOrEqualTo(0.8 * across), "tilted, a cell is still at least 80% as deep as it is wide");
            Assert.That(across, Is.GreaterThanOrEqualTo(10), "a cell is at least 10 px across, even on a 720 x 1280 phone");
        }

        [Test]
        public void SixteenByNineIsLimitedByHeightAndLongScreensByWidth()
        {
            ScreenLayout sixteenNine = Layout(Screens.Single(s => s.Name == "16:9 1080x1920"));
            ScreenLayout tall = Layout(Screens.Single(s => s.Name == "20:9 1080x2400 (punch hole)"));
            Assert.That(sixteenNine.ContentOnScreen.Height, Is.EqualTo(sixteenNine.WorldRegion.Height).Within(0.5));
            Assert.That(sixteenNine.ContentOnScreen.Width, Is.LessThan(sixteenNine.WorldRegion.Width));
            Assert.That(tall.ContentOnScreen.Width, Is.EqualTo(tall.WorldRegion.Width).Within(0.5));
            Assert.That(tall.ContentOnScreen.Height, Is.LessThan(tall.WorldRegion.Height));
        }

        [Test]
        public void CanvasScaleIsCanvasScalerExpand()
        {
            Assert.That(Layout(S("a", 1080, 1920)).CanvasScale, Is.EqualTo(1920 / 900.0));
            Assert.That(Layout(S("b", 1080, 2400)).CanvasScale, Is.EqualTo(1080 / 420.0));
            ScreenLayout l = Layout(S("c", 1080, 1920));
            Assert.That(l.CanvasWidth, Is.GreaterThanOrEqualTo(DesignLayout.Width - 1e-9));
            Assert.That(l.CanvasHeight, Is.GreaterThanOrEqualTo(DesignLayout.Height - 1e-9));
            Assert.That(l.Header.Height, Is.EqualTo(64 * l.CanvasScale).Within(1e-9));
            Assert.That(l.Footer.Height, Is.EqualTo(74 * l.CanvasScale).Within(1e-9));
        }

        [TestCase(90)]
        [TestCase(75)]
        [TestCase(60)]
        [TestCase(45)]
        public void AnyReasonablePitchFits(double pitch)
        {
            foreach (Screen s in Screens) Assert.That(Layout(s, pitch).Fits, Is.True, s + " at " + pitch);
        }

        [Test]
        public void ProjectionMatchesTheCameraBasis()
        {
            ScreenLayout l = Layout(S("x", 1080, 1920));
            // A point straight ahead of the camera, at any depth, is at the screen center.
            double th = 60 * Math.PI / 180;
            for (double t = 1; t < 100; t *= 3)
            {
                (double x, double y) = l.WorldToScreen(l.CameraX, l.CameraY - t * Math.Sin(th), l.CameraZ + t * Math.Cos(th));
                Assert.That((x, y), Is.EqualTo((540.0, 960.0)).Within(1e-6));
            }
            // Raising a point moves it up the screen by height * cos(pitch).
            (double _, double y0) = l.DesignToScreen(100, 300, 0);
            (double __, double y1) = l.DesignToScreen(100, 300, 20);
            Assert.That(y1 - y0, Is.EqualTo(20 * l.PixelsPerDesignUnit * Math.Cos(th)).Within(1e-9));
            Assert.That(l.OrthographicSize, Is.EqualTo(1920 / (2 * l.PixelsPerWorldUnit)).Within(1e-12));
        }

        [Test]
        public void BadInputIsRejectedAndABadSafeAreaIgnored()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScreenLayout.Compute(new ScreenLayoutInput { ScreenWidth = 0, ScreenHeight = 100 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScreenLayout.Compute(new ScreenLayoutInput { ScreenWidth = 100, ScreenHeight = 100, PitchDegrees = 0 }));
            Assert.Throws<ArgumentNullException>(() => ScreenLayout.Compute(null));
            ScreenLayout l = ScreenLayout.Compute(new ScreenLayoutInput { ScreenWidth = 1080, ScreenHeight = 1920, SafeArea = new RectD(0, 0, 0, 0) });
            Assert.That(l.SafeArea, Is.EqualTo(new RectD(0, 0, 1080, 1920)));
            ScreenLayout clipped = ScreenLayout.Compute(new ScreenLayoutInput { ScreenWidth = 1080, ScreenHeight = 1920, SafeArea = new RectD(-10, -10, 2000, 2000) });
            Assert.That(clipped.SafeArea, Is.EqualTo(new RectD(0, 0, 1080, 1920)));
        }

        // ---- DesignLayout against main.ts ----

        [Test]
        public void SlotsAreCubeHuntsSlotRects()
        {
            Assert.That(DesignLayout.SlotRect(0, 5), Is.EqualTo(new RectD(20, 540, 68, 66)));
            Assert.That(DesignLayout.SlotRect(4, 5), Is.EqualTo(new RectD(332, 540, 68, 66)));
            RectD six = DesignLayout.SlotRect(0, 6);
            Assert.That((six.X, six.Width), Is.EqualTo((16.0, 338.0 / 6)).Within(1e-12));
            Assert.That(DesignLayout.SlotRect(5, 6).XMax, Is.EqualTo(404).Within(1e-9));
            for (int n = 1; n <= 7; n++)
                for (int i = 0; i < n; i++)
                {
                    RectD r = DesignLayout.SlotRect(i, n);
                    Assert.That(r.XMin >= 15.99 && r.XMax <= 404.01, Is.True, $"{n} slots stay inside the 16-unit margins");
                    if (i > 0) Assert.That(r.XMin - DesignLayout.SlotRect(i - 1, n).XMax, Is.EqualTo(10).Within(1e-9));
                }
        }

        [Test]
        public void TheTrayLinesUpUnderFiveSlots()
        {
            for (int c = 0; c < 5; c++)
                Assert.That(DesignLayout.BlockRect(c, 0, 5).X, Is.EqualTo(DesignLayout.SlotRect(c, 5).X));
            Assert.That(DesignLayout.BlockRect(1, 2, 5), Is.EqualTo(new RectD(98, 756, 68, 52)));
            Assert.That(DesignLayout.TrayBottom, Is.EqualTo(808));
            Assert.That(DesignLayout.TrayLeft(3), Is.EqualTo(98), "(420 - (3 * 68 + 2 * 10)) / 2");
        }

        [Test]
        public void TheFieldIsCubeHuntsCellGrid()
        {
            Assert.That(DesignLayout.CellSize(29, 29), Is.EqualTo(312.0 / 29));
            Assert.That(DesignLayout.CellSize(36, 46), Is.EqualTo(312.0 / 46));
            RectD f = DesignLayout.Field(36, 46);
            Assert.That((f.CenterX, f.CenterY), Is.EqualTo((210.0, 258.0)).Within(1e-9));
            (double x, double y) = DesignLayout.CellCenter(0, 29, 29);
            Assert.That((x, y), Is.EqualTo((16 + 38 + 312.0 / 58, 64 + 38 + 312.0 / 58)).Within(1e-9));
            Assert.That(DesignLayout.Board.Contains(DesignLayout.Field(46, 35)), Is.True);
            Assert.That(DesignLayout.WorldContent.Contains(DesignLayout.Board) && DesignLayout.WorldContent.Contains(DesignLayout.Tunnel), Is.True);
            Assert.That((DesignLayout.HeaderHeight, DesignLayout.FooterHeight), Is.EqualTo((64.0, 74.0)));
            Assert.That(DesignLayout.WorldContent.YMin, Is.GreaterThanOrEqualTo(0) .And.LessThan(DesignLayout.Board.Y));
            Assert.That(SimConstants.BoardY - DesignLayout.WorldPadding, Is.GreaterThanOrEqualTo(DesignLayout.HeaderHeight - DesignLayout.WorldPadding));
        }
    }
}
