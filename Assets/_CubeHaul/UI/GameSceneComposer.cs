using System.Collections.Generic;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Levels;
using CubeHaul.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CubeHaul.UI
{
    /// <summary>
    /// Builds the game scene's objects (docs/SCENE_LAYOUT.md): the tilted orthographic camera, a light, the world with
    /// its layout preview, and the UI canvas (CanvasScaler 420 x 900 Expand, a safe-area root, the top bar and the bottom
    /// bar), plus an EventSystem for the Input System. The Editor's Create Game Scene menu saves the result as
    /// GameScene.unity; PlayMode tests build it directly.
    /// </summary>
    public static class GameSceneComposer
    {
        public static readonly Color Background = new Color32(0xf6, 0xef, 0xe4, 0xff);
        static readonly Color ButtonBlue = new Color32(0x3f, 0xb6, 0xf2, 0xff);
        static readonly Color ButtonGreen = new Color32(0x5c, 0xc4, 0x64, 0xff);
        static readonly Color Ink = new Color32(0xb5, 0x76, 0x3c, 0xff);

        /// <summary>What was built, for the Editor and tests.</summary>
        public sealed class Composition
        {
            public GameObject Root;
            public Camera Camera;
            public GameCameraRig Rig;
            public LayoutPreview Preview;
            public Canvas Canvas;
            public CanvasScaler Scaler;
            public SafeAreaFitter SafeArea;
            public RectTransform Header;
            public RectTransform Footer;
            /// <summary>Every button and label in the bars.</summary>
            public List<RectTransform> Controls = new List<RectTransform>();
        }

        public static Composition Compose(LevelLibrary library = null)
        {
            var c = new Composition { Root = new GameObject("Game") };

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            camGo.transform.SetParent(c.Root.transform, false);
            c.Camera = camGo.GetComponent<Camera>();
            c.Camera.clearFlags = CameraClearFlags.SolidColor;
            c.Camera.backgroundColor = Background;
            c.Rig = camGo.AddComponent<GameCameraRig>();

            var light = new GameObject("Directional Light", typeof(Light));
            light.transform.SetParent(c.Root.transform, false);
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            Light l = light.GetComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.shadows = LightShadows.Soft;

            var world = new GameObject("World");
            world.transform.SetParent(c.Root.transform, false);
            var previewGo = new GameObject("Layout Preview");
            previewGo.transform.SetParent(world.transform, false);
            previewGo.SetActive(false);
            c.Preview = previewGo.AddComponent<LayoutPreview>();
            c.Preview.Library = library;
            previewGo.SetActive(true);

            BuildUi(c, library);

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(c.Root.transform, false);
            }
            return c;
        }

        static void BuildUi(Composition c, LevelLibrary library)
        {
            var canvasGo = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(c.Root.transform, false);
            c.Canvas = canvasGo.GetComponent<Canvas>();
            c.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            c.Scaler = canvasGo.GetComponent<CanvasScaler>();
            c.Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            c.Scaler.referenceResolution = new Vector2((float)DesignLayout.Width, (float)DesignLayout.Height);
            c.Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform safe = Rect("Safe Area", canvasGo.transform);
            Stretch(safe);
            c.SafeArea = safe.gameObject.AddComponent<SafeAreaFitter>();

            // Top bar: main.ts drawTopBar, in design units. Left buttons keep their distance from the left edge, right ones
            // from the right edge, the title stays centered between them.
            c.Header = Rect("Top Bar", safe);
            c.Header.anchorMin = new Vector2(0, 1);
            c.Header.anchorMax = new Vector2(1, 1);
            c.Header.pivot = new Vector2(0.5f, 1);
            c.Header.sizeDelta = new Vector2(0, (float)DesignLayout.HeaderHeight);
            c.Header.anchoredPosition = Vector2.zero;
            c.Controls.Add(Button(c.Header, "Restart", "R", new Vector2(0, 1), new Vector2(30, -32), new Vector2(34, 34), ButtonBlue, 18));
            c.Controls.Add(Button(c.Header, "Previous", "<", new Vector2(0, 1), new Vector2(70, -32), new Vector2(30, 30), ButtonBlue, 18));
            c.Controls.Add(Button(c.Header, "Next", ">", new Vector2(1, 1), new Vector2(-120, -32), new Vector2(30, 30), ButtonBlue, 18));
            c.Controls.Add(Button(c.Header, "Speed", "1x", new Vector2(1, 1), new Vector2(-53, -33), new Vector2(82, 38), ButtonBlue, 17));
            string title = "1/10 · Level";
            DifficultyLabel label = DifficultyLabel.Easy;
            if (library != null)
            {
                LevelRepository repo = library.Load();
                if (repo.HasErrors || repo.Count == 0)
                    Debug.LogWarning($"CubeHaul: the level library \"{library.name}\" gave {repo.Count} levels; the top bar keeps its placeholder title."
                        + (repo.HasErrors ? "\n" + new LevelLoadException(repo.Errors).Message : ""));
                else
                {
                    LevelDefinition first = repo.GetAt(0);
                    title = $"1/{repo.Count} · {first.Name}";
                    if (first.DifficultyMetadata != null) label = first.DifficultyMetadata.Label;
                }
            }
            c.Controls.Add(Label(c.Header, "Title", title, new Vector2(0.5f, 1), new Vector2(-25, -24), new Vector2(190, 24), 18, Ink));
            RectTransform chip = Button(c.Header, "Difficulty", DifficultyLabels.ToJson(label), new Vector2(0.5f, 1), new Vector2(-25, -46.5f), new Vector2(64, 17),
                DifficultyChipColors.For(label), 11);
            Object.DestroyImmediate(chip.GetComponent<Button>());
            c.Controls.Add(chip);

            // Bottom bar: "+1 Slot" (main.ts drawBottomBar). Without "Your image" (not in v1) it is centered.
            c.Footer = Rect("Bottom Bar", safe);
            c.Footer.anchorMin = new Vector2(0, 0);
            c.Footer.anchorMax = new Vector2(1, 0);
            c.Footer.pivot = new Vector2(0.5f, 0);
            c.Footer.sizeDelta = new Vector2(0, (float)DesignLayout.FooterHeight);
            c.Footer.anchoredPosition = Vector2.zero;
            RectTransform slot = Button(c.Footer, "Add Slot", "+1 Slot", new Vector2(0.5f, 0), new Vector2(0, 6 + 27), new Vector2(176, 54), ButtonGreen, 20);
            c.Controls.Add(slot);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A button centered at <paramref name="center"/> from its anchor, in design units.</summary>
        static RectTransform Button(RectTransform parent, string name, string text, Vector2 anchor, Vector2 center, Vector2 size, Color color, int fontSize)
        {
            RectTransform rt = Rect(name, parent);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            rt.gameObject.AddComponent<Button>().targetGraphic = image;
            RectTransform caption = Label(rt, "Label", text, new Vector2(0.5f, 0.5f), Vector2.zero, size, fontSize, Color.white);
            caption.anchorMin = Vector2.zero;
            caption.anchorMax = Vector2.one;
            caption.offsetMin = Vector2.zero;
            caption.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform Label(RectTransform parent, string name, string text, Vector2 anchor, Vector2 center, Vector2 size, int fontSize, Color color)
        {
            RectTransform rt = Rect(name, parent);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 8;
            t.resizeTextMaxSize = fontSize;
            t.raycastTarget = false;
            return rt;
        }
    }
}
