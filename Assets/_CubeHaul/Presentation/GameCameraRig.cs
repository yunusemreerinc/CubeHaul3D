using CubeHaul.Gameplay.Layout;
using UnityEngine;

namespace CubeHaul.Presentation
{
    /// <summary>
    /// The game camera: orthographic, tilted down by <see cref="Pitch"/> (top-down with a slight 3D angle, no yaw so the
    /// grid stays square to the portrait screen), sized and placed so the world content (board to tray) fills the area
    /// between the top and bottom bars inside the safe area. The numbers come from <see cref="ScreenLayout"/>
    /// (docs/SCENE_LAYOUT.md); this only applies them, again whenever the screen size or safe area changes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class GameCameraRig : MonoBehaviour
    {
        [SerializeField, Range(30f, 90f), Tooltip("Tilt from the horizontal; 90 = straight down.")]
        float pitch = 60f;
        [SerializeField, Tooltip("How tall things on the ground get, design units; their tops stay in view.")]
        float objectHeight = 40f;
        [SerializeField, Tooltip("False: the screen size and safe area come from Apply (tests, render textures).")]
        bool followScreen = true;

        Camera cam;
        int lastWidth, lastHeight;
        Rect lastSafe;
        float lastPitch;

        public Camera Camera => cam != null ? cam : (cam = GetComponent<Camera>());
        public ScreenLayout Layout { get; private set; }

        public float Pitch
        {
            get => pitch;
            set => pitch = Mathf.Clamp(value, 30f, 90f);
        }

        public bool FollowScreen
        {
            get => followScreen;
            set => followScreen = value;
        }

        void OnEnable() => Refresh(true);

        void LateUpdate() => Refresh(false);

        void Refresh(bool force)
        {
            if (!followScreen) return;
            int w = Screen.width, h = Screen.height;
            Rect safe = Screen.safeArea;
            if (!force && w == lastWidth && h == lastHeight && safe == lastSafe && pitch == lastPitch) return;
            Apply(w, h, safe);
        }

        /// <summary>Lays the camera out for a screen of this size and safe area (pixels, origin bottom-left).</summary>
        public ScreenLayout Apply(int width, int height, Rect safeArea)
        {
            lastWidth = width;
            lastHeight = height;
            lastSafe = safeArea;
            lastPitch = pitch;
            Layout = ScreenLayout.Compute(new ScreenLayoutInput
            {
                ScreenWidth = Mathf.Max(1, width),
                ScreenHeight = Mathf.Max(1, height),
                SafeArea = new RectD(safeArea.x, safeArea.y, safeArea.width, safeArea.height),
                PitchDegrees = pitch,
                UnitsPerDesign = WorldMapper.UnitsPerDesign,
                ObjectHeight = objectHeight,
            });
            Camera c = Camera;
            c.orthographic = true;
            c.orthographicSize = (float)Layout.OrthographicSize;
            c.nearClipPlane = 0.1f;
            c.farClipPlane = (float)(2 * Layout.Input.CameraDistance);
            transform.SetPositionAndRotation(
                new Vector3((float)Layout.CameraX, (float)Layout.CameraY, (float)Layout.CameraZ),
                Quaternion.Euler(pitch, 0f, 0f));
            return Layout;
        }
    }
}
