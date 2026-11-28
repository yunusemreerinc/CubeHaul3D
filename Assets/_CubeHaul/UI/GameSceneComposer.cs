using System.Collections.Generic;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Levels;
using CubeHaul.Presentation;
using CubeHaul.Presentation.Grid;
using CubeHaul.Presentation.Roads;
using CubeHaul.Presentation.Session;
using CubeHaul.UI.Hud;
using CubeHaul.Presentation.Trucks;
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
            public GridPresenter Grid;
            /// <summary>The game loop: a GameSession of the library's first level, auto-played until the tray takes touches.</summary>
            public GameSessionDriver Session;
            public SessionControls SessionControls;
            /// <summary>The touchable tray and the slots (Task 16), under the bars.</summary>
            public PlayAreaView PlayArea;
            /// <summary>The start, won and stuck screens over everything (Task 17).</summary>
            public GameFlowView Flow;
            public RoadDebugGizmos Roads;
            public TruckFleetView Trucks;
            public Canvas Canvas;
            public CanvasScaler Scaler;
            public SafeAreaFitter SafeArea;
            public RectTransform Header;
            public RectTransform Footer;
            /// <summary>Every button and label in the bars.</summary>
            public List<RectTransform> Controls = new List<RectTransform>();
        }

        public static Composition Compose(LevelLibrary library = null, Material cubeMaterial = null, TruckVisual truckPrefab = null)
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
            c.Preview.ShowField = false; // the GridPresenter draws the cubes
            c.Preview.ShowDockAndTray = false; // the UI draws the slots and the tray
            previewGo.SetActive(true);

            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(world.transform, false);
            gridGo.SetActive(false);
            c.Grid = gridGo.AddComponent<GridPresenter>();
            c.Grid.Library = library;
            c.Grid.CubeMaterial = cubeMaterial;
            var trucksGo = new GameObject("Trucks");
            trucksGo.transform.SetParent(world.transform, false);
            c.Trucks = trucksGo.AddComponent<TruckFleetView>();
            c.Trucks.Prefab = truckPrefab;

            gridGo.SetActive(true);

            var sessionGo = new GameObject("Game Loop");
            sessionGo.transform.SetParent(c.Root.transform, false);
            c.Session = sessionGo.AddComponent<GameSessionDriver>();
            c.Session.Configure(c.Grid, c.Trucks, library);

            var roadsGo = new GameObject("Road Debug");
            roadsGo.transform.SetParent(world.transform, false);
            c.Roads = roadsGo.AddComponent<RoadDebugGizmos>();
            c.Roads.Configure(c.Grid, library);

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

            // The tray and the slots first, so the bars draw over them. Laid out where the world shows them, not in the safe area.
            RectTransform play = Rect("Play Area", canvasGo.transform);
            c.PlayArea = play.gameObject.AddComponent<PlayAreaView>();
            c.PlayArea.Configure(c.Session, c.Rig);

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
            RectTransform speed = Button(c.Header, "Speed", "1x", new Vector2(1, 1), new Vector2(-53, -33), new Vector2(82, 38), ButtonBlue, 17);
            c.Controls.Add(speed);
            string title = "1/10 · Level";
            DifficultyLabel label = DifficultyLabel.Easy;
            if (!ReferenceEquals(library, null) && library == null)
                Debug.LogWarning("CubeHaul: the level library given to the scene composer has been unloaded (a destroyed object); "
                    + "the top bar keeps its placeholder title and the preview shows a checkerboard. Load the library after opening the scene.");
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
            RectTransform titleRect = Label(c.Header, "Title", title, new Vector2(0.5f, 1), new Vector2(-25, -24), new Vector2(190, 24), 18, Ink);
            c.Controls.Add(titleRect);
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

            c.SessionControls = c.Canvas.gameObject.AddComponent<SessionControls>();
            c.SessionControls.Configure(c.Session, speed.GetComponent<Button>(), speed.Find("Label").GetComponent<Text>(),
                c.Header.Find("Restart").GetComponent<Button>(), slot.GetComponent<Button>());
            c.SessionControls.ConfigureLevels(c.Header.Find("Previous").GetComponent<Button>(), c.Header.Find("Next").GetComponent<Button>(),
                titleRect.GetComponent<Text>(), chip.GetComponent<Image>(), chip.Find("Label").GetComponent<Text>());

            BuildOverlay(c, canvasGo.transform);
        }

        static readonly Color PanelCard = new Color32(0xfb, 0xf4, 0xe8, 0xff);
        static readonly Color PanelBody = new Color32(0x8a, 0x64, 0x40, 0xff);
        static readonly Color Purple = new Color32(0x8f, 0x7c, 0xf0, 0xff);
        static readonly Color Red = new Color32(0xe8, 0x42, 0x4f, 0xff);

        /// <summary>
        /// main.ts drawOverlay / panel, in design units: the dim layer over the whole screen (it takes every touch), and a
        /// 320-wide card 60 above the middle with its title, a line of text and its buttons (54 high, 74 from the bottom).
        /// </summary>
        static void BuildOverlay(Composition c, Transform canvas)
        {
            RectTransform overlay = Rect("Overlay", canvas);
            Stretch(overlay);
            RectTransform dim = Rect("Blocker", overlay);
            Stretch(dim);
            var dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(40 / 255f, 25 / 255f, 10 / 255f, 0.45f);
            dimImage.raycastTarget = true;

            RectTransform start = Panel(overlay, "Start Panel", "1/10 · Level", "Tap a crate at the front of the tray: its trucks haul away the cubes of its color.", Purple, out Text startTitle);
            RectTransform play = PanelButton(start, "Play", "Play", ButtonGreen, 0, 200);
            RectTransform won = Panel(overlay, "Won Panel", "Picture complete!", "Every cube was hauled away.", ButtonGreen, out _);
            RectTransform next = PanelButton(won, "Next Level", "Next level", ButtonGreen, 0, 200);
            RectTransform stuck = Panel(overlay, "Stuck Panel", "Out of moves", "Every slot is waiting for a color that can't be reached.", Red, out _);
            RectTransform addSlot = PanelButton(stuck, "Add Slot", "+1 Slot", ButtonGreen, -73, 130);
            RectTransform retry = PanelButton(stuck, "Retry", "Retry", Purple, 73, 130);

            c.Flow = overlay.gameObject.AddComponent<GameFlowView>();
            c.Flow.Configure(c.Session, dim.gameObject, start.gameObject, won.gameObject, stuck.gameObject, startTitle,
                play.GetComponent<Button>(), next.GetComponent<Button>(), next.Find("Label").GetComponent<Text>(),
                addSlot.GetComponent<Button>(), retry.GetComponent<Button>());
            // Hidden until the game asks for them (GameFlowView shows the one for the current screen).
            foreach (RectTransform panel in new[] { start, won, stuck }) panel.gameObject.SetActive(false);
            dim.gameObject.SetActive(false);
        }

        static RectTransform Panel(RectTransform parent, string name, string title, string body, Color tone, out Text titleText)
        {
            const float w = 320, h = 220;
            RectTransform card = Rect(name, parent);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(w, h);
            card.anchoredPosition = new Vector2(0, 60);
            var image = card.gameObject.AddComponent<Image>();
            image.color = PanelCard;
            RectTransform t = Label(card, "Title", title, new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(w - 36, 36), 27, tone);
            titleText = t.GetComponent<Text>();
            RectTransform b = Label(card, "Body", body, new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(w - 40, 44), 15, PanelBody);
            b.GetComponent<Text>().fontStyle = FontStyle.Normal;
            return card;
        }

        static RectTransform PanelButton(RectTransform card, string name, string text, Color color, float x, float width) =>
            Button(card, name, text, new Vector2(0.5f, 0), new Vector2(x, 74 - 27), new Vector2(width, 54), color, 20);

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
