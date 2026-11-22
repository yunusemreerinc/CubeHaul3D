using System;

namespace CubeHaul.Gameplay.Layout
{
    /// <summary>What the layout is computed for: the screen, its safe area and the camera's tilt.</summary>
    public sealed class ScreenLayoutInput
    {
        /// <summary>Screen size in pixels.</summary>
        public double ScreenWidth { get; set; }
        public double ScreenHeight { get; set; }
        /// <summary>Unity's Screen.safeArea: pixels, origin bottom-left. Null = the whole screen.</summary>
        public RectD? SafeArea { get; set; }
        /// <summary>Camera tilt from the horizontal: 90 = straight down. Default 60: top-down with a slight 3D angle.</summary>
        public double PitchDegrees { get; set; } = 60;
        /// <summary>World units per design unit (WorldMapper).</summary>
        public double UnitsPerDesign { get; set; } = 0.025;
        /// <summary>How tall things on the ground get, design units (crates, trucks); keeps their tops in view.</summary>
        public double ObjectHeight { get; set; } = 40;
        /// <summary>Distance of the camera from the plane through the content's center, world units (orthographic: only clipping).</summary>
        public double CameraDistance { get; set; } = 60;
    }

    /// <summary>
    /// The screen layout (docs/SCENE_LAYOUT.md): the UI canvas scale (CanvasScaler "Expand" on a 420 x 900 reference),
    /// the top and bottom bars inside the safe area, the region between them for the 3D world, and the orthographic
    /// camera that fits the world content (board to tray) into that region. Engine-free, so it is tested for many screens
    /// without Unity; the Presentation layer only applies it.
    /// </summary>
    public sealed class ScreenLayout
    {
        public ScreenLayoutInput Input { get; private set; }
        public double ScreenWidth { get; private set; }
        public double ScreenHeight { get; private set; }

        /// <summary>Pixels per UI unit: what CanvasScaler (Scale With Screen Size, 420 x 900, Expand) gives.</summary>
        public double CanvasScale { get; private set; }
        public double CanvasWidth => ScreenWidth / CanvasScale;
        public double CanvasHeight => ScreenHeight / CanvasScale;

        /// <summary>Screen rectangles, pixels, origin bottom-left.</summary>
        public RectD SafeArea { get; private set; }
        public RectD Header { get; private set; }
        public RectD Footer { get; private set; }
        /// <summary>The area between the bars where the world content is fitted.</summary>
        public RectD WorldRegion { get; private set; }
        /// <summary>Where the world content (DesignLayout.WorldContent, object tops included) lands on screen.</summary>
        public RectD ContentOnScreen { get; private set; }

        /// <summary>Pixels per world unit; times UnitsPerDesign = pixels per design unit across the ground.</summary>
        public double PixelsPerWorldUnit { get; private set; }
        public double PixelsPerDesignUnit => PixelsPerWorldUnit * Input.UnitsPerDesign;
        /// <summary>Unity Camera.orthographicSize: half the visible height, world units.</summary>
        public double OrthographicSize { get; private set; }
        /// <summary>Camera position, world units; its rotation is Euler(PitchDegrees, 0, 0).</summary>
        public double CameraX { get; private set; }
        public double CameraY { get; private set; }
        public double CameraZ { get; private set; }
        public double PitchDegrees => Input.PitchDegrees;

        /// <summary>True when the content fits its region, the bars fit the safe area and nothing overlaps.</summary>
        public bool Fits =>
            WorldRegion.Height > 0 && WorldRegion.Contains(ContentOnScreen, 0.5) && SafeArea.Contains(Header, 0.5) && SafeArea.Contains(Footer, 0.5)
            && !Header.Overlaps(ContentOnScreen, 0.5) && !Footer.Overlaps(ContentOnScreen, 0.5) && !Header.Overlaps(Footer, 0.5);

        double sin, cos, planeCenterX, planeCenterY;

        public static ScreenLayout Compute(ScreenLayoutInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!(input.ScreenWidth >= 1 && input.ScreenHeight >= 1)) throw new ArgumentOutOfRangeException(nameof(input), "screen size");
            if (!(input.PitchDegrees > 0 && input.PitchDegrees <= 90)) throw new ArgumentOutOfRangeException(nameof(input), "pitch must be in (0, 90]");
            if (!(input.UnitsPerDesign > 0)) throw new ArgumentOutOfRangeException(nameof(input), "units per design unit");

            double w = input.ScreenWidth, h = input.ScreenHeight;
            var l = new ScreenLayout { Input = input, ScreenWidth = w, ScreenHeight = h };
            l.CanvasScale = Math.Min(w / DesignLayout.Width, h / DesignLayout.Height);

            RectD screen = new RectD(0, 0, w, h);
            RectD safe = input.SafeArea ?? screen;
            safe = RectD.FromMinMax(Math.Max(0, safe.XMin), Math.Max(0, safe.YMin), Math.Min(w, safe.XMax), Math.Min(h, safe.YMax));
            if (safe.Width <= 0 || safe.Height <= 0) safe = screen;
            l.SafeArea = safe;
            l.Header = RectD.FromMinMax(safe.XMin, safe.YMax - DesignLayout.HeaderHeight * l.CanvasScale, safe.XMax, safe.YMax);
            l.Footer = RectD.FromMinMax(safe.XMin, safe.YMin, safe.XMax, safe.YMin + DesignLayout.FooterHeight * l.CanvasScale);
            l.WorldRegion = RectD.FromMinMax(safe.XMin, l.Footer.YMax, safe.XMax, Math.Max(l.Footer.YMax, l.Header.YMin));

            // Camera basis for rotation Euler(pitch, 0, 0): right (1, 0, 0), up (0, cos, sin), forward (0, -sin, cos).
            double pitch = input.PitchDegrees * Math.PI / 180;
            l.sin = Math.Sin(pitch);
            l.cos = Math.Cos(pitch);
            double u = input.UnitsPerDesign;
            RectD c = DesignLayout.WorldContent;
            // Content on the camera plane (x right, y up): the ground rectangle foreshortened by sin, object tops lifted by cos.
            double left = c.XMin * u, right = c.XMax * u;
            double bottom = -c.YMax * u * l.sin;
            double top = -c.YMin * u * l.sin + input.ObjectHeight * u * l.cos;
            double s = Math.Max(1e-9, Math.Min(l.WorldRegion.Width / (right - left), Math.Max(1e-9, l.WorldRegion.Height) / (top - bottom)));
            l.PixelsPerWorldUnit = s;
            l.OrthographicSize = h / (2 * s);
            // The content's center goes to the region's center.
            l.planeCenterX = (left + right) / 2 - (l.WorldRegion.CenterX - w / 2) / s;
            l.planeCenterY = (bottom + top) / 2 - (l.WorldRegion.CenterY - h / 2) / s;
            double d = input.CameraDistance;
            l.CameraX = l.planeCenterX;
            l.CameraY = l.planeCenterY * l.cos + d * l.sin;
            l.CameraZ = l.planeCenterY * l.sin - d * l.cos;
            l.ContentOnScreen = RectD.FromMinMax(l.PlaneToScreenX(left), l.PlaneToScreenY(bottom), l.PlaneToScreenX(right), l.PlaneToScreenY(top));
            return l;
        }

        double PlaneToScreenX(double x) => ScreenWidth / 2 + (x - planeCenterX) * PixelsPerWorldUnit;
        double PlaneToScreenY(double y) => ScreenHeight / 2 + (y - planeCenterY) * PixelsPerWorldUnit;

        /// <summary>Where a world point appears on screen, pixels from the bottom-left (Camera.WorldToScreenPoint).</summary>
        public (double x, double y) WorldToScreen(double x, double y, double z) => (PlaneToScreenX(x), PlaneToScreenY(y * cos + z * sin));

        /// <summary>Where a design point at <paramref name="height"/> design units above the ground appears on screen.</summary>
        public (double x, double y) DesignToScreen(double designX, double designY, double height = 0)
        {
            double u = Input.UnitsPerDesign;
            return WorldToScreen(designX * u, height * u, -designY * u);
        }

        /// <summary>A design rectangle on the ground (or raised), as the screen rectangle around it.</summary>
        public RectD DesignToScreen(RectD design, double height = 0)
        {
            (double x0, double y0) = DesignToScreen(design.XMin, design.YMax, 0);
            (double x1, double y1) = DesignToScreen(design.XMax, design.YMin, height);
            return RectD.FromMinMax(x0, y0, x1, y1);
        }

        public override string ToString() => FormattableString.Invariant(
            $"{ScreenWidth}x{ScreenHeight}: canvas x{CanvasScale:0.###}, safe {SafeArea}, header {Header}, footer {Footer}, world {WorldRegion}, content {ContentOnScreen}, {PixelsPerDesignUnit:0.###} px/design, ortho {OrthographicSize:0.###}");
    }
}
