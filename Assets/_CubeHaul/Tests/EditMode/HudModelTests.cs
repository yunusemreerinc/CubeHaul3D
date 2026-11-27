using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay;
using CubeHaul.Gameplay.Hud;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Gameplay.Session;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Task 16 (docs/TRAY_UI.md): what the tray and the slots show. Counts, colors and "blocked" come straight from the
    /// rules every frame of a whole game; a tap on a full dock is refused before the rules and changes nothing; the
    /// columns close up as in main.ts; crates rise, fly, land and leave with main.ts's timing; and on every phone and
    /// tablet screen the slots and the tray stay between the bars with fingertip-sized tap targets.
    /// </summary>
    public class HudModelTests
    {
        const double Dt = 1.0 / 60;

        static LevelDefinition Level(string id) => LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().Get(id);

        static string Snapshot(GameSession s)
        {
            GameState g = s.Game.State;
            var cells = new int[g.Board.CellCount];
            g.Board.CopyCells(cells);
            var cols = Enumerable.Range(0, g.ColumnCount).Select(c => string.Join(",", Enumerable.Range(0, g.ColumnLength(c)).Select(r => $"{g.CrateAt(c, r).Id}:{g.CrateAt(c, r).Count}")));
            var slots = Enumerable.Range(0, g.SlotCount).Select(i => g.SlotAt(i) == null ? "-" : $"{g.SlotAt(i).Id}:{g.SlotAt(i).Count}");
            return $"{g.Status}|{g.Board.Pending}|{g.Board.Remaining}|{string.Join("/", cols)}|{string.Join("/", slots)}|{string.Join(",", cells)}";
        }

        [Test]
        public void ATappedCrateFliesToTheFirstFreeSlotAndCountsDownFromWhereItStarted()
        {
            var s = GameSession.For(Level("strawberry"));
            var hud = new HudModel(s);
            Crate front = s.Game.State.Front(2);
            double fromX = hud.ColumnX(2);
            Assert.That(s.Tap(2), Is.EqualTo(0), "the first free slot");
            Assert.That(hud.Slot(0).Shows, Is.EqualTo(SlotShows.Incoming), "flying: the bay stays empty");
            Assert.That(hud.Flyer(0, out FlyerLook f), Is.True);
            Assert.That(f.CenterX, Is.EqualTo(fromX + DesignLayout.BlockWidth / 2).Within(1e-9), "takes off from its column");
            Assert.That((f.Color, f.Count, f.Column), Is.EqualTo((front.Color, front.Count, 2)));
            // It flies in real time (1/3.8 s) and lands in its bay.
            int frames = 0;
            while (s.Scheduler.IsFlying(0))
            {
                s.Advance(Dt);
                hud.Advance(Dt);
                frames++;
            }
            Assert.That(frames, Is.EqualTo(16), "0.26 s at 60 fps");
            SlotLook look = hud.Slot(0);
            Assert.That(look.Shows, Is.EqualTo(SlotShows.Crate));
            Assert.That(look.Land, Is.GreaterThan(0.9), "it just landed: the bounce starts");
            int start = front.Count;
            for (int k = 0; k < 120 && s.Game.State.SlotAt(0) != null; k++)
            {
                s.Advance(Dt);
                hud.Advance(Dt);
                Crate c = s.Game.State.SlotAt(0);
                if (c == null) break;
                look = hud.Slot(0);
                Assert.That(look.Count, Is.EqualTo(c.Count), "the count is the rules' count");
                Assert.That(look.Progress, Is.EqualTo((double)c.Count / start).Within(1e-12), "trucks still to go of the crate's trucks");
            }
            Assert.That(hud.Slot(0).Progress, Is.LessThan(1), "it sent trucks");
        }

        [TestCase("strawberry")]
        [TestCase("rocket")]
        public void EverythingShownComesFromTheRulesEveryFrame(string id)
        {
            // A whole game: each slot shows the crate GameState holds, its count, and GameState.SlotBlocked; each tray
            // crate the crate at that place. Rocket gets stuck with blocked crates; strawberry is won.
            var s = GameSession.For(Level(id));
            var hud = new HudModel(s);
            int blockedSeen = 0, frames = 0;
            AutoPlayer.Play(s, Dt, 200_000, _ =>
            {
                hud.Advance(Dt);
                frames++;
                GameState g = s.Game.State;
                for (int i = 0; i < g.SlotCount; i++)
                {
                    SlotLook look = hud.Slot(i);
                    Crate c = g.SlotAt(i);
                    if (s.Scheduler.IsFlying(i))
                    {
                        Assert.That(look.Shows, Is.EqualTo(SlotShows.Incoming));
                        continue;
                    }
                    if (c == null)
                    {
                        Assert.That(look.Shows == SlotShows.Empty || look.Shows == SlotShows.Leaving, Is.True, $"frame {frames} slot {i}: {look.Shows}");
                        continue;
                    }
                    Assert.That(look.Shows, Is.EqualTo(SlotShows.Crate));
                    Assert.That((look.Color, look.Count), Is.EqualTo((c.Color, c.Count)), $"frame {frames} slot {i}");
                    Assert.That(look.Blocked, Is.EqualTo(g.SlotBlocked(i)), $"frame {frames} slot {i}: blocked is the rules' answer");
                    if (look.Blocked) blockedSeen++;
                }
                for (int col = 0; col < g.ColumnCount; col++)
                    for (int row = 0; row < DesignLayout.TrayRows; row++)
                    {
                        TrayCrateLook t = hud.TrayCrate(col, row);
                        Crate c = g.CrateAt(col, row);
                        if (c == null) Assert.That(t.Visible, Is.False);
                        else if (t.Visible) Assert.That((t.Color, t.Count), Is.EqualTo((c.Color, c.Count)), $"frame {frames} column {col} row {row}");
                    }
            });
            if (id == "rocket") Assert.That(blockedSeen, Is.GreaterThan(0), "blocked crates were shown");
        }

        [Test]
        public void ATapOnAFullDockIsRefusedAndChangesNothing()
        {
            var s = GameSession.For(Level("strawberry"));
            var hud = new HudModel(s);
            GameState g = s.Game.State;
            int col = 0;
            for (int k = 0; k < g.SlotCount; k++)
            {
                while (g.ColumnLength(col) == 0) col++;
                Assert.That(s.Tap(col), Is.EqualTo(k));
            }
            Assert.That(g.HasFreeSlot(), Is.False);
            string before = Snapshot(s);
            int refusedEvents = 0;
            s.TapRefused += _ => refusedEvents++;
            for (int c = 0; c < g.ColumnCount; c++)
                for (int k = 0; k < 3; k++)
                    Assert.That(s.Tap(c), Is.EqualTo(-1), $"column {c}");
            Assert.That(Snapshot(s), Is.EqualTo(before), "no crate moved, no count or slot changed");
            Assert.That(refusedEvents, Is.EqualTo(3 * g.ColumnCount));
            Assert.That(hud.RefusedCount, Is.EqualTo(3 * g.ColumnCount));
            // The warning: the slots shake for 0.35 s, the tapped crate dips.
            Assert.That(hud.Shake, Is.EqualTo(HudModel.ShakeTime));
            hud.Advance(0.05);
            Assert.That(Math.Abs(hud.ShakeOffset), Is.GreaterThan(0.5));
            Assert.That(hud.Slot(0).Rect.X, Is.Not.EqualTo(DesignLayout.SlotRect(0, g.SlotCount).X), "the crates in the slots shake");
            Assert.That(hud.Dock.X, Is.EqualTo(DesignLayout.SlotRect(0, g.SlotCount).X - 8), "the dock stays");
            int last = g.ColumnCount - 1;
            Assert.That(hud.TrayCrate(last, 0).Dip, Is.GreaterThan(0.4), "the refused crate dips in place");
            hud.Advance(0.4);
            Assert.That(hud.ShakeOffset, Is.Zero);
            Assert.That(hud.TrayCrate(last, 0).Dip, Is.Zero);
            Assert.That(Snapshot(s), Is.EqualTo(before));
        }

        [Test]
        public void ColumnsCloseUpAroundTheMiddleWhenOneRunsOut()
        {
            // Three columns; the middle one holds one crate.
            var board = new Board(3, 1, new[] { 0, 1, 2 }, 3, OpenEdges.Bottom);
            var cols = new[]
            {
                new[] { new CrateSpec(0, 0, 1) },
                new[] { new CrateSpec(1, 1, 1) },
                new[] { new CrateSpec(2, 2, 1) },
            };
            var s = new GameSession(new GameEngine(board, cols, 3));
            var hud = new HudModel(s);
            double step = DesignLayout.BlockWidth + DesignLayout.BlockGapX;
            for (int c = 0; c < 3; c++) Assert.That(hud.ColumnX(c), Is.EqualTo(DesignLayout.TrayLeft(3) + c * step).Within(1e-9));
            double middle = hud.ColumnX(1);
            s.Tap(1);
            Assert.That(hud.ColumnTargetX(0), Is.EqualTo(DesignLayout.TrayLeft(2)).Within(1e-9), "two columns left, centered");
            Assert.That(hud.ColumnTargetX(2), Is.EqualTo(DesignLayout.TrayLeft(2) + step).Within(1e-9));
            Assert.That(hud.ColumnTargetX(1), Is.EqualTo(middle), "an empty column keeps its place");
            Assert.That(hud.FrontBox(1).Width, Is.Zero, "nothing to tap there");
            Assert.That(hud.Lane(1).Width, Is.Zero);
            hud.Advance(Dt);
            double x0 = hud.ColumnX(0);
            Assert.That(x0, Is.GreaterThan(DesignLayout.TrayLeft(3)).And.LessThan(DesignLayout.TrayLeft(2)), "gliding");
            for (int k = 0; k < 120; k++) hud.Advance(Dt);
            Assert.That(hud.ColumnX(0), Is.EqualTo(DesignLayout.TrayLeft(2)));
            Assert.That(hud.FrontBox(0).X, Is.EqualTo(DesignLayout.TrayLeft(2)), "the tap target moved with it");
            Assert.That(hud.Shelf.Width, Is.EqualTo(3 * DesignLayout.BlockWidth + 2 * DesignLayout.BlockGapX + 16), "the shelf keeps its width");
        }

        [Test]
        public void TheCratesBehindRiseARowAfterATap()
        {
            var s = GameSession.For(Level("strawberry"));
            var hud = new HudModel(s);
            TrayCrateLook settled = hud.TrayCrate(0, 0), second = hud.TrayCrate(0, 1), third = hud.TrayCrate(0, 2);
            Assert.That((settled.BodyAlpha, settled.LabelSize, settled.Front), Is.EqualTo((1.0, 25.0, true)));
            Assert.That(second.LabelAlpha, Is.EqualTo(0.55).Within(1e-12));
            Assert.That(second.Width, Is.EqualTo(DesignLayout.BlockWidth - 10).Within(1e-12), "inset 5 a side");
            Assert.That(third.LabelAlpha, Is.EqualTo(0.32).Within(1e-12));
            s.Tap(0);
            TrayCrateLook rising = hud.TrayCrate(0, 0);
            Assert.That(rising.LabelAlpha, Is.EqualTo(0.55).Within(1e-12), "the new front crate starts where it was, a row back");
            Assert.That(rising.Front, Is.False);
            Assert.That(hud.TrayCrate(0, 2).Visible, Is.False, "the crate coming into the third row fades in from nothing");
            for (int k = 0; k < 6; k++) hud.Advance(Dt);
            double partway = hud.TrayCrate(0, 0).Bottom;
            Assert.That(partway, Is.LessThan(rising.Bottom), "moving up");
            for (int k = 0; k < 60; k++) hud.Advance(Dt);
            TrayCrateLook done = hud.TrayCrate(0, 0);
            Assert.That((done.BodyAlpha, done.Front), Is.EqualTo((1.0, true)));
            Assert.That(done.Bottom, Is.EqualTo(settled.Bottom).Within(1e-9));
        }

        [Test]
        public void AnEmptiedCrateLeavesAndGivesWayToTheNextOne()
        {
            // One crate of one truck in a one-slot game: it empties at once.
            var board = new Board(2, 1, new[] { 0, 0 }, 1, OpenEdges.Bottom);
            var s = new GameSession(new GameEngine(board, new[] { new[] { new CrateSpec(0, 0, 1), new CrateSpec(1, 0, 1) } }, 1));
            var hud = new HudModel(s);
            s.Tap(0);
            while (s.Game.State.SlotAt(0) != null)
            {
                s.Advance(Dt);
                hud.Advance(Dt);
            }
            Assert.That(hud.Slot(0).Shows, Is.EqualTo(SlotShows.Leaving));
            hud.Advance(0.2);
            Assert.That(hud.Slot(0).LeaveT, Is.EqualTo((Dt + 0.2) / HudModel.LeaveTime).Within(1e-9), "0.5 s to leave (one frame already passed)");
            var (scale, alpha, turn, _) = HudModel.Leaving(0.4);
            Assert.That(scale, Is.LessThan(1.12));
            Assert.That(alpha, Is.LessThan(1));
            Assert.That(turn, Is.GreaterThan(0));
            s.Tap(0); // the next crate flies in: the leaving one gives way at once
            hud.Advance(Dt);
            Assert.That(hud.Slot(0).Shows, Is.EqualTo(SlotShows.Incoming));
        }

        [Test]
        public void MainTsHelpers()
        {
            Assert.That(HudModel.EaseOutBack(0), Is.EqualTo(0).Within(1e-12));
            Assert.That(HudModel.EaseOutBack(1), Is.EqualTo(1));
            Assert.That(HudModel.EaseOutBack(0.8), Is.GreaterThan(1), "overshoots before settling");
            Assert.That(HudModel.AtDepth(HudModel.TrayAlpha, 0.5), Is.EqualTo(0.775).Within(1e-12));
            Assert.That(HudModel.AtDepth(HudModel.TrayAlpha, 9), Is.EqualTo(0));
            Assert.That(HudModel.AtDepth(HudModel.TrayLabel, -1), Is.EqualTo(25));
            Assert.That(HudModel.Pop(1), Is.EqualTo(1.22).Within(1e-12));
            Assert.That(HudModel.Wobble(1), Is.Zero);
            Assert.That(HudModel.Wobble(0), Is.Zero);
        }

        // ---- On screen ----

        static IEnumerable<ScreenLayoutTests.Screen> Screens() => ScreenLayoutTests.Screens;

        [TestCaseSource(nameof(Screens))]
        public void TheSlotsAndTheTrayFitEveryScreenWithFingertipTargets(ScreenLayoutTests.Screen screen)
        {
            ScreenLayout layout = ScreenLayout.Compute(new ScreenLayoutInput { ScreenWidth = screen.W, ScreenHeight = screen.H, SafeArea = screen.Safe });
            var map = new HudLayout(layout);
            foreach (int slots in new[] { 5, 6 })
            {
                var all = new List<(string name, RectD r)>();
                for (int i = 0; i < slots; i++) all.Add(($"slot {i}/{slots}", map.Rect(DesignLayout.SlotRect(i, slots))));
                // The blocked pill under a slot (main.ts: 9 below, 16 high).
                RectD lastSlot = DesignLayout.SlotRect(slots - 1, slots);
                all.Add(("pill", map.Rect(new RectD(lastSlot.CenterX - 28, lastSlot.YMax + 1, 56, 16))));
                for (int c = 0; c < 5; c++)
                    for (int row = 0; row < DesignLayout.TrayRows; row++)
                        all.Add(($"crate {c},{row}", map.Rect(DesignLayout.BlockRect(c, row, 5))));
                foreach (var (name, r) in all)
                {
                    Assert.That(layout.WorldRegion.Contains(r, 0.5), Is.True, $"{screen}: {name} {r} leaves the play region {layout.WorldRegion}");
                    Assert.That(layout.SafeArea.Contains(r, 0.5), Is.True, $"{screen}: {name} outside the safe area");
                    Assert.That(r.Overlaps(layout.Header, 0.5) || r.Overlaps(layout.Footer, 0.5), Is.False, $"{screen}: {name} under a bar");
                }
                for (int i = 0; i < slots; i++)
                    for (int j = i + 1; j < slots; j++)
                        Assert.That(all[i].r.Overlaps(all[j].r, 0.5), Is.False, $"{screen}: slots {i} and {j} overlap");
            }
            // Tap targets: every front crate at least 44 x 40 canvas units (about 44 pt on a phone), apart from each other,
            // and the whole front row below the dock.
            RectD dock = map.Rect(DesignLayout.SlotRect(0, 6));
            for (int c = 0; c < 5; c++)
            {
                RectD front = map.Rect(DesignLayout.BlockRect(c, 0, 5));
                Assert.That(front.Width / map.CanvasScale, Is.GreaterThanOrEqualTo(44), $"{screen}: column {c} width");
                Assert.That(front.Height / map.CanvasScale, Is.GreaterThanOrEqualTo(40), $"{screen}: column {c} height");
                Assert.That(front.YMax, Is.LessThan(dock.YMin), $"{screen}: the tray is under the dock");
                if (c > 0) Assert.That(front.Overlaps(map.Rect(DesignLayout.BlockRect(c - 1, 0, 5)), 0.5), Is.False);
            }
            // The camera maps the ground linearly: the HUD's points are where the world shows them.
            (double x, double y) = map.Point(210, 540);
            (double ex, double ey) = layout.DesignToScreen(210, 540);
            Assert.That(x, Is.EqualTo(ex).Within(1e-6));
            Assert.That(y, Is.EqualTo(ey).Within(1e-6));
        }
    }
}
