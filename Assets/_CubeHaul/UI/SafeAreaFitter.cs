using UnityEngine;

namespace CubeHaul.UI
{
    /// <summary>
    /// Stretches this RectTransform over the screen's safe area (no notch, island, rounded corners or home indicator),
    /// and again whenever it changes. Everything in the top and bottom bars lives under it.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [SerializeField, Tooltip("False: the screen size and safe area come from Apply (tests, render textures).")]
        bool followScreen = true;

        Vector2 lastSize;
        Rect lastSafe;

        public bool FollowScreen
        {
            get => followScreen;
            set => followScreen = value;
        }

        void OnEnable() => Refresh(true);

        void Update() => Refresh(false);

        void Refresh(bool force)
        {
            if (!followScreen) return;
            var size = new Vector2(Screen.width, Screen.height);
            Rect safe = Screen.safeArea;
            if (!force && size == lastSize && safe == lastSafe) return;
            Apply(size, safe);
        }

        /// <summary>Anchors this rectangle to <paramref name="safeArea"/> on a screen of <paramref name="screenSize"/> pixels.</summary>
        public void Apply(Vector2 screenSize, Rect safeArea)
        {
            lastSize = screenSize;
            lastSafe = safeArea;
            if (screenSize.x <= 0 || screenSize.y <= 0) return;
            Rect s = safeArea.width > 0 && safeArea.height > 0 ? safeArea : new Rect(Vector2.zero, screenSize);
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(Mathf.Clamp01(s.xMin / screenSize.x), Mathf.Clamp01(s.yMin / screenSize.y));
            rt.anchorMax = new Vector2(Mathf.Clamp01(s.xMax / screenSize.x), Mathf.Clamp01(s.yMax / screenSize.y));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
