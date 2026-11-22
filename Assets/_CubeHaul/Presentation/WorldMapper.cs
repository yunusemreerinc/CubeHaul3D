using UnityEngine;

namespace CubeHaul.Presentation
{
    /// <summary>
    /// The one place design space meets the 3D world (PORTING_PLAN §3.5, R-09). Design space is cube-hunt's 420 x 900
    /// screen with y pointing down; the world lays it flat on the XZ plane so the ramp side (large y) is nearest the
    /// orthographic camera (negative z) and the picture is not mirrored.
    /// </summary>
    public static class WorldMapper
    {
        /// <summary>World units per design unit: the 420-unit-wide design screen is 10.5 world units wide.</summary>
        public const float UnitsPerDesign = 0.025f;

        public static Vector3 ToWorld(double x, double y, float unitsPerPixel)
        {
            return new Vector3((float)(x * unitsPerPixel), 0f, (float)(-y * unitsPerPixel));
        }

        /// <summary>A design point at <paramref name="height"/> design units above the ground.</summary>
        public static Vector3 ToWorld(double x, double y, double height = 0)
        {
            return new Vector3((float)(x * UnitsPerDesign), (float)(height * UnitsPerDesign), (float)(-y * UnitsPerDesign));
        }
    }
}
