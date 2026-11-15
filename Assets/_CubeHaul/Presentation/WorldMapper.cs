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
        public static Vector3 ToWorld(double x, double y, float unitsPerPixel)
        {
            return new Vector3((float)(x * unitsPerPixel), 0f, (float)(-y * unitsPerPixel));
        }
    }
}
