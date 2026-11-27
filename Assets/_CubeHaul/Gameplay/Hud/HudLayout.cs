using System;
using CubeHaul.Gameplay.Layout;

namespace CubeHaul.Gameplay.Hud
{
    /// <summary>
    /// Where the tray and the slots go on screen (Task 16, docs/TRAY_UI.md). They are UI, but they sit in the world's
    /// layout: main.ts draws them on the same 420 x 900 page as the board and the ramp, so here a design point goes where
    /// the camera shows that point of the ground (<see cref="ScreenLayout.DesignToScreen(double, double, double)"/>).
    /// The ground is seen at an angle, so design lengths across the screen and down it have their own scale
    /// (<see cref="PixelsAcross"/>, <see cref="PixelsDown"/>). Screen pixels, origin bottom-left, as Unity's.
    /// </summary>
    public readonly struct HudLayout
    {
        readonly double originX, originY;

        public HudLayout(ScreenLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            (originX, originY) = layout.DesignToScreen(0, 0);
            (double ax, _) = layout.DesignToScreen(1, 0);
            (_, double dy) = layout.DesignToScreen(0, 1);
            PixelsAcross = ax - originX;
            PixelsDown = originY - dy;
            CanvasScale = layout.CanvasScale;
            Screen = layout;
        }

        public ScreenLayout Screen { get; }
        /// <summary>Screen pixels per design unit across (x) and down (y, foreshortened by the camera's pitch).</summary>
        public double PixelsAcross { get; }
        public double PixelsDown { get; }
        /// <summary>Screen pixels per canvas unit (CanvasScaler: 420 x 900, Expand).</summary>
        public double CanvasScale { get; }

        /// <summary>A design point (y down) on screen.</summary>
        public (double x, double y) Point(double designX, double designY) => (originX + designX * PixelsAcross, originY - designY * PixelsDown);

        /// <summary>A design rectangle on screen.</summary>
        public RectD Rect(RectD design)
        {
            (double x0, double y1) = Point(design.XMin, design.YMin);
            (double x1, double y0) = Point(design.XMax, design.YMax);
            return RectD.FromMinMax(x0, y0, x1, y1);
        }

        /// <summary>Design lengths in canvas units, across and down.</summary>
        public double CanvasAcross(double design) => design * PixelsAcross / CanvasScale;
        public double CanvasDown(double design) => design * PixelsDown / CanvasScale;
    }
}
