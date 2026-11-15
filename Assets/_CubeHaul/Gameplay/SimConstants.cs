using System.Collections.Generic;

namespace CubeHaul.Gameplay
{
    /// <summary>
    /// Timing and layout constants from cube-hunt src/main.ts @ 559dfd9, in its design units (pixels of a 420 x 900
    /// portrait screen, y down). They decide when trucks arrive and so which cubes open when (PORTING_PLAN §3.4, R-07).
    /// </summary>
    public static class SimConstants
    {
        /// <summary>Truck speed, design px per second at 1x.</summary>
        public const double CarSpeed = 200;
        /// <summary>Spacing of trucks leaving the same ramp lane, design px (scaled by carScale).</summary>
        public const double ConvoyGap = 30;
        /// <summary>Sim seconds between one crate's trucks (B-33).</summary>
        public const double PopInterval = ConvoyGap / CarSpeed;
        /// <summary>Sim seconds a truck stops to load its cube (B-35).</summary>
        public const double LoadTime = 0.42;
        /// <summary>A tapped crate's flight into its slot, 1 / seconds; it runs in real time, not sim time (B-32).</summary>
        public const double FlySpeed = 3.8;
        public const double CrateFlightTime = 1 / FlySpeed;
        /// <summary>Real seconds after the last truck is home before the end panel opens (B-36).</summary>
        public const double EndPanelDelay = 0.4;
        /// <summary>Longest frame step main.ts allows, seconds.</summary>
        public const double MaxFrameTime = 0.05;
        /// <summary>Slots after the +1 Slot booster, at most (B-30).</summary>
        public const int MaxSlots = 6;

        public const double ScreenWidth = 420;
        public const double ScreenHeight = 900;
        public const double BoardX = 16, BoardY = 64, BoardWidth = 388, BoardHeight = 388;
        public const double FieldInset = 38;
        public const double RoadGap = 22;
        public const double TunnelX = ScreenWidth / 2, TunnelY = 496, TunnelWidth = 112, TunnelHeight = 36;
        /// <summary>The three ramp lanes, as offsets from the tunnel's middle (B-34).</summary>
        public static readonly IReadOnlyList<double> RampLanes = new[] { -34.0, 0.0, 34.0 };
    }
}
