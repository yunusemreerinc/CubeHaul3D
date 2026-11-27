using System;
using CubeHaul.Gameplay;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Hud;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Gameplay.Session;
using CubeHaul.Presentation;
using CubeHaul.Presentation.Session;
using UnityEngine;
using UnityEngine.UI;

namespace CubeHaul.UI.Hud
{
    /// <summary>
    /// The touchable tray and the slots (Task 16, docs/TRAY_UI.md), drawn with uGUI over the world, where main.ts draws
    /// them on its page: the slot dock under the ramp, the tray's shelf below it (<see cref="HudLayout"/>). It shows
    /// what <see cref="HudModel"/> says and nothing else: every column's stack (front crate full size, the ones behind
    /// smaller and fainter, rising a row after a tap), every slot's crate with its count, progress bar and "blocked"
    /// state, crates flying into their slots, emptied crates leaving.
    ///
    /// Input: each column's front crate has an invisible tap target (<see cref="ColumnTapTarget"/>). The EventSystem's
    /// Input System module turns a mouse click and a finger touch into the same pointer-down, which calls
    /// <see cref="GameSessionDriver.Tap"/> → GameSession.Tap → GameEngine.Tap. Nothing else changes the game. When every
    /// slot is full the tap is refused before it reaches the rules; the slots shake and the dock's rim flashes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PlayAreaView : MonoBehaviour
    {
        [SerializeField] GameSessionDriver driver;
        [SerializeField] GameCameraRig rig;

        static readonly PaletteColor Pill = PaletteColor.Parse("#6e4a2e");
        static readonly Color DockColor = new Color32(0x58, 0x5e, 0x6a, 0xff);
        static readonly Color WellColor = new Color32(0x36, 0x3b, 0x45, 0xff);
        static readonly Color ShelfColor = new Color32(0xf8, 0xf1, 0xe6, 0xff);
        static readonly Color LaneColor = new Color(140 / 255f, 95 / 255f, 50 / 255f, 0.1f);
        static readonly Color WarningColor = new Color32(0xff, 0x5a, 0x4f, 0xff);

        HudModel model;
        PaletteColor[] palette = { new PaletteColor(200, 200, 200) };
        GameSession bound;
        Font font;
        RectTransform area, shelfLayer, trayLayer, dockLayer, slotLayer, flyerLayer, targetLayer;
        Image shelf, shelfEdge, dock, dockEdge;
        Image[] lanes = new Image[0], wells = new Image[0], wellEdges = new Image[0], rings = new Image[0];
        CrateWidget[,] tray = new CrateWidget[0, 0];
        CrateWidget[] slotCrates = new CrateWidget[0], pills = new CrateWidget[0], flyers = new CrateWidget[0];
        ColumnTapTarget[] targets = new ColumnTapTarget[0];

        /// <summary>A column was tapped: column, the slot it went to (or -1), and what pressed it (MouseOrPen, Touch).</summary>
        public event Action<int, int, string> ColumnTapped;

        public HudModel Model => model;
        public GameSessionDriver Driver => driver;
        public RectTransform Area => area;
        public int TapTargetCount => targets.Length;

        public void Configure(GameSessionDriver gameDriver, GameCameraRig cameraRig)
        {
            driver = gameDriver;
            rig = cameraRig;
        }

        /// <summary>The tap target over column <paramref name="col"/>'s front crate (tests, tutorials).</summary>
        public ColumnTapTarget TargetOf(int col) => col >= 0 && col < targets.Length ? targets[col] : null;

        /// <summary>The slot widget (tests).</summary>
        public RectTransform SlotCrate(int slot) => slot >= 0 && slot < slotCrates.Length ? slotCrates[slot].Root : null;
        public RectTransform BlockedPill(int slot) => slot >= 0 && slot < pills.Length ? pills[slot].Root : null;
        public RectTransform TrayCrate(int col, int row) => col >= 0 && col < tray.GetLength(0) && row >= 0 && row < tray.GetLength(1) ? tray[col, row].Root : null;
        public RectTransform Dock => dock != null ? dock.rectTransform : null;
        public Image DockEdge => dockEdge;

        /// <summary>A pointer pressed column <paramref name="col"/>'s front crate (ColumnTapTarget).</summary>
        public int TapColumn(int col, string pointer)
        {
            if (driver == null || driver.Session == null) return -1;
            int slot = driver.Tap(col);
            ColumnTapped?.Invoke(col, slot, pointer);
            return slot;
        }

        void Awake() => area = (RectTransform)transform;

        void OnDestroy() => Unbind();

        void Unbind()
        {
            model?.Detach();
            model = null;
            bound = null;
        }

        void Bind(GameSession session)
        {
            Unbind();
            bound = session;
            if (session == null) return;
            model = new HudModel(session);
            string[] colors = driver != null && driver.Level != null ? driver.Level.Picture.Palette : null;
            if (colors != null && colors.Length > 0)
            {
                palette = new PaletteColor[colors.Length];
                for (int k = 0; k < colors.Length; k++) palette[k] = PaletteColor.TryParse(colors[k], out PaletteColor c) ? c : new PaletteColor(200, 200, 200);
            }
            Build(session.Game.State.ColumnCount);
        }

        void Build(int columns)
        {
            if (area == null) area = (RectTransform)transform;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int k = area.childCount - 1; k >= 0; k--) DestroyNow(area.GetChild(k).gameObject);

            shelfLayer = Layer("Shelf");
            trayLayer = Layer("Tray");
            dockLayer = Layer("Dock");
            slotLayer = Layer("Slots");
            flyerLayer = Layer("Flying");
            targetLayer = Layer("Tap Targets");

            shelf = CrateWidget.ImageOf(shelfLayer, "Shelf", UiShapes.Rounded, out _);
            shelf.color = ShelfColor;
            shelfEdge = CrateWidget.ImageOf(shelfLayer, "Shelf Edge", UiShapes.Outline, out _);
            shelfEdge.color = new Color(150 / 255f, 105 / 255f, 60 / 255f, 0.25f);
            lanes = new Image[columns];
            for (int c = 0; c < columns; c++)
            {
                lanes[c] = CrateWidget.ImageOf(shelfLayer, "Lane " + c, UiShapes.Rounded, out _);
                lanes[c].color = LaneColor;
            }
            tray = new CrateWidget[columns, DesignLayout.TrayRows];
            for (int row = DesignLayout.TrayRows - 1; row >= 0; row--) // back rows first, under the front ones
                for (int c = 0; c < columns; c++)
                    tray[c, row] = CrateWidget.Create(trayLayer, $"Crate {c},{row}", false, font);

            dock = CrateWidget.ImageOf(dockLayer, "Dock", UiShapes.Rounded, out _);
            dock.color = DockColor;
            dockEdge = CrateWidget.ImageOf(dockLayer, "Dock Rim", UiShapes.Outline, out _);
            int slots = SimConstants.MaxSlots;
            wells = new Image[slots];
            wellEdges = new Image[slots];
            rings = new Image[slots];
            slotCrates = new CrateWidget[slots];
            pills = new CrateWidget[slots];
            flyers = new CrateWidget[slots];
            for (int i = 0; i < slots; i++)
            {
                wells[i] = CrateWidget.ImageOf(dockLayer, "Well " + i, UiShapes.Rounded, out _);
                wells[i].color = WellColor;
                wellEdges[i] = CrateWidget.ImageOf(dockLayer, "Well Edge " + i, UiShapes.Outline, out _);
                wellEdges[i].color = new Color(1, 1, 1, 0.16f);
                rings[i] = CrateWidget.ImageOf(slotLayer, "Ring " + i, UiShapes.Outline, out _);
            }
            for (int i = 0; i < slots; i++) slotCrates[i] = CrateWidget.Create(slotLayer, "Slot " + i, true, font);
            for (int i = 0; i < slots; i++) pills[i] = CrateWidget.Create(slotLayer, "Blocked " + i, false, font);
            for (int i = 0; i < slots; i++) flyers[i] = CrateWidget.Create(flyerLayer, "Flying " + i, false, font);

            targets = new ColumnTapTarget[columns];
            for (int c = 0; c < columns; c++)
            {
                RectTransform rt = CrateWidget.Rect("Column " + c, targetLayer);
                Image hit = rt.gameObject.AddComponent<Image>();
                hit.color = new Color(1, 1, 1, 0); // invisible, still hit by the raycaster
                hit.raycastTarget = true;
                ColumnTapTarget t = rt.gameObject.AddComponent<ColumnTapTarget>();
                t.Configure(this, c);
                targets[c] = t;
            }
        }

        RectTransform Layer(string name)
        {
            RectTransform rt = CrateWidget.Rect(name, area);
            return rt;
        }

        void LateUpdate()
        {
            GameSession session = driver != null ? driver.Session : null;
            if (session != bound) Bind(session);
            if (model == null) return;
            model.Advance(Mathf.Min(Time.deltaTime, 0.05f));
            ScreenLayout layout = rig != null && rig.Layout != null ? rig.Layout : ScreenLayout.Compute(new ScreenLayoutInput
            {
                ScreenWidth = Mathf.Max(1, Screen.width),
                ScreenHeight = Mathf.Max(1, Screen.height),
                UnitsPerDesign = WorldMapper.UnitsPerDesign,
            });
            Render(new HudLayout(layout));
        }

        /// <summary>Draws the tray and the slots for this screen layout now.</summary>
        public void Render(HudLayout map)
        {
            if (model == null) return;
            area.anchorMin = area.anchorMax = Vector2.zero;
            area.pivot = Vector2.zero;
            area.anchoredPosition = Vector2.zero;
            area.sizeDelta = Vector2.zero;

            // Shelf, lanes and stacks.
            SetRect(shelf, map, model.Shelf, 20);
            SetRect(shelfEdge, map, model.Shelf, 20);
            int cols = tray.GetLength(0);
            for (int c = 0; c < cols; c++)
            {
                RectD lane = model.Lane(c);
                Show(lanes[c], lane.Width > 0);
                if (lane.Width > 0) SetRect(lanes[c], map, lane, 16);
                for (int row = 0; row < DesignLayout.TrayRows; row++)
                {
                    TrayCrateLook look = model.TrayCrate(c, row);
                    CrateWidget w = tray[c, row];
                    w.Show(look.Visible);
                    if (!look.Visible) continue;
                    PaletteColor color = palette[Mathf.Clamp(look.Color, 0, palette.Length - 1)];
                    float h = Down(map, look.Height);
                    w.Place(Point(map, look.CenterX, look.Bottom + 3 * look.Dip), Across(map, look.Width), h, Down(map, Math.Max(3, look.Height * 0.1)), Across(map, 14));
                    w.Transform(1 - 0.06f * (float)look.Dip, 1 - 0.12f * (float)look.Dip, 0);
                    w.Paint(color, (float)look.BodyAlpha);
                    w.Flash((float)look.Dip);
                    w.Count(look.Count, color, Across(map, look.LabelSize), h * 0.58f, (float)look.LabelAlpha, !look.Front);
                }
                RectD front = model.FrontBox(c);
                bool live = front.Width > 0;
                if (targets[c].gameObject.activeSelf != live) targets[c].gameObject.SetActive(live);
                if (live) SetRect((RectTransform)targets[c].transform, map, front);
            }

            // The dock and its bays; its rim flashes while a refused tap shakes the slots.
            int n = model.SlotCount;
            SetRect(dock, map, model.Dock, 20);
            SetRect(dockEdge, map, model.Dock, 20);
            float warn = (float)(model.Shake / HudModel.ShakeTime);
            dockEdge.color = Color.Lerp(new Color(1, 1, 1, 0.22f), WarningColor, warn);
            for (int i = 0; i < wells.Length; i++)
            {
                bool used = i < n;
                Show(wells[i], used);
                Show(wellEdges[i], used);
                if (!used) continue;
                RectD r = DesignLayout.SlotRect(i, n);
                SetRect(wells[i], map, r, 14);
                SetRect(wellEdges[i], map, new RectD(r.X + 4.5, r.Y + 4.5, r.Width - 9, r.Height - 9), 10);
            }

            for (int i = 0; i < slotCrates.Length; i++)
            {
                SlotLook look = i < n ? model.Slot(i) : default;
                CrateWidget w = slotCrates[i];
                bool crate = i < n && look.Shows == SlotShows.Crate;
                bool leaving = i < n && look.Shows == SlotShows.Leaving;
                w.Show(crate || leaving);
                Show(rings[i], false);
                pills[i].Show(false);
                RectD r = look.Rect;
                if (crate)
                {
                    PaletteColor color = palette[Mathf.Clamp(look.Color, 0, palette.Length - 1)];
                    float h = Down(map, r.Height);
                    float wob = (float)HudModel.Wobble(look.Land);
                    w.Place(Point(map, r.CenterX, r.YMax), Across(map, r.Width), h, Down(map, Math.Max(3, r.Height * 0.1)), Across(map, 14));
                    w.Transform(1 + wob, 1 - wob, 0);
                    w.Paint(color, 1);
                    w.Flash(0);
                    w.Stripes(look.Blocked, color, Across(map, 11));
                    float alpha = look.Blocked ? 0.6f : 1f;
                    w.Count(look.Count, color, Across(map, 25), h * 0.6f, alpha, false, (float)HudModel.Pop(look.Bump));
                    w.Bar(true, color, (float)look.Progress, Across(map, 11), Down(map, r.Height * 0.29 - 5), Across(map, r.Width - 22), Down(map, 5), alpha);
                    if (look.Land > 0)
                    {
                        double grow = (1 - look.Land) * 12;
                        Show(rings[i], true);
                        SetRect(rings[i], map, new RectD(r.X - grow, r.Y - grow, r.Width + 2 * grow, r.Height + 2 * grow), 14 + grow);
                        rings[i].color = CrateWidget.ToColor(color.Shade(0.45), (float)(look.Land * 0.9));
                    }
                    if (look.Blocked)
                    {
                        // The "blocked" pill under the slot.
                        CrateWidget p = pills[i];
                        p.Show(true);
                        float ph = Down(map, 16);
                        p.Place(Point(map, r.CenterX, r.YMax + 9 + 8), Across(map, 56), ph, Down(map, 3), Across(map, 8));
                        p.Transform(1, 1, 0);
                        p.Paint(Pill, 1);
                        p.Flash(0);
                        p.Text("blocked", Pill, Across(map, 10), ph * 0.55f, 1, true);
                    }
                }
                else if (leaving)
                {
                    PaletteColor color = palette[Mathf.Clamp(look.Color, 0, palette.Length - 1)];
                    (double scale, double alpha, double turn, double ring) = HudModel.Leaving(look.LeaveT);
                    float h = Down(map, r.Height);
                    w.Place(Point(map, r.CenterX, r.YMax), Across(map, r.Width), h, Down(map, Math.Max(3, r.Height * 0.1)), Across(map, 14), 0.5f);
                    w.Transform((float)scale, (float)scale, (float)(-turn * Mathf.Rad2Deg));
                    w.Paint(color, (float)alpha);
                    w.Flash(0);
                    w.Stripes(false, color, 1);
                    w.Bar(false, color, 0, 0, 0, 0, 0, 0);
                    w.Text("0", color, Across(map, 25), h * 0.6f, (float)alpha, false);
                    Show(rings[i], true);
                    SetRect(rings[i], map, new RectD(r.X - ring, r.Y - ring, r.Width + 2 * ring, r.Height + 2 * ring), 14 + ring);
                    rings[i].color = new Color(1, 1, 1, (float)((1 - look.LeaveT) * 0.85));
                }
            }

            for (int i = 0; i < flyers.Length; i++)
            {
                CrateWidget w = flyers[i];
                bool flying = model.Flyer(i, out FlyerLook f);
                w.Show(flying);
                if (!flying) continue;
                PaletteColor color = palette[Mathf.Clamp(f.Color, 0, palette.Length - 1)];
                float h = Down(map, f.Height);
                w.Place(Point(map, f.CenterX, f.CenterY + f.Height / 2), Across(map, f.Width), h, Down(map, Math.Max(3, f.Height * 0.1)), Across(map, 14));
                w.Transform(1, 1, 0);
                w.Paint(color, 1);
                w.Flash(0);
                w.Count(f.Count, color, Across(map, f.LabelSize), h * 0.58f, 1, false);
            }
        }

        static void Show(Graphic g, bool visible)
        {
            if (g.gameObject.activeSelf != visible) g.gameObject.SetActive(visible);
        }

        Vector2 Point(HudLayout map, double x, double y)
        {
            (double sx, double sy) = map.Point(x, y);
            return new Vector2((float)(sx / map.CanvasScale), (float)(sy / map.CanvasScale));
        }

        static float Across(HudLayout map, double design) => (float)map.CanvasAcross(design);
        static float Down(HudLayout map, double design) => (float)map.CanvasDown(design);

        void SetRect(Image image, HudLayout map, RectD design, double radius)
        {
            SetRect(image.rectTransform, map, design);
            image.pixelsPerUnitMultiplier = UiShapes.RadiusMultiplier(Across(map, radius));
        }

        void SetRect(RectTransform rt, HudLayout map, RectD design)
        {
            RectD screen = map.Rect(design);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2((float)(screen.XMin / map.CanvasScale), (float)(screen.YMin / map.CanvasScale));
            rt.sizeDelta = new Vector2((float)(screen.Width / map.CanvasScale), (float)(screen.Height / map.CanvasScale));
        }

        static void DestroyNow(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
