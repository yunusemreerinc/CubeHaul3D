using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay;
using CubeHaul.Gameplay.Flow;
using CubeHaul.Gameplay.Session;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Task 17 (docs/GAME_FLOW.md): the level flow. The end panel opens only once every truck is home (and no crate is
    /// still flying), 0.4 s later, as in main.ts; a stuck game shows Out of moves, +1 Slot un-sticks it (once per level,
    /// free, not after a win), Retry starts over with the booster back; the first three levels play one after another.
    /// </summary>
    public class GameFlowTests
    {
        const double Dt = 1.0 / 60;

        static LevelRepository repo;
        static LevelRepository Repo => repo ?? (repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors());

        /// <summary>Two cubes in a column, bottom edge open; the slot's crate wants the top one: stuck at once.</summary>
        static GameSession Jam()
        {
            var board = new Board(1, 2, new[] { 0, 1 }, 2, OpenEdges.Bottom);
            var columns = new[] { new[] { new CrateSpec(0, 0, 1) }, new[] { new CrateSpec(1, 1, 1) } };
            return new GameSession(new GameEngine(board, columns, 1));
        }

        static void Frame(GameSession s, GameFlow flow, bool autoPlay = false)
        {
            if (autoPlay) AutoPlayer.Act(s);
            s.Advance(Dt);
            flow.Update(s, Dt);
        }

        [Test]
        public void TheWonPanelOpensOnlyOnceEveryTruckIsHome()
        {
            var s = GameSession.For(Repo.Get("strawberry"));
            var flow = new GameFlow();
            flow.Begin(false);
            int frame = 0, wonAt = -1, lastPending = -1, panelAt = -1, mainTsPanelAt = -1;
            while (panelAt < 0 && frame < 20_000)
            {
                Frame(s, flow, autoPlay: true);
                frame++;
                if (wonAt < 0 && s.Game.Status == GameStatus.Won) wonAt = frame;
                if (GameFlow.VisualPending(s) > 0) lastPending = frame;
                if (mainTsPanelAt < 0 && s.ShowsEndPanel) mainTsPanelAt = frame;
                if (flow.Screen == FlowScreen.Won) panelAt = frame;
                else Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing), $"frame {frame}");
            }
            Assert.That(wonAt, Is.GreaterThan(0), "won");
            Assert.That(lastPending, Is.GreaterThan(wonAt), "trucks were still driving home after the last pickup");
            Assert.That(panelAt, Is.GreaterThan(lastPending), "the panel waited for them");
            Assert.That((panelAt - lastPending) * Dt, Is.GreaterThanOrEqualTo(GameFlow.PanelDelay - 1e-9).And.LessThan(GameFlow.PanelDelay + 2 * Dt), "then 0.4 s");
            Assert.That(panelAt, Is.EqualTo(mainTsPanelAt), "the same frame as main.ts's overlayDelay");
            Assert.That(s.TrucksOut, Is.Zero);
        }

        [Test]
        public void AStuckGameWaitsForItsCrateToLandThenOffersTheBoosterAndRetry()
        {
            GameSession s = Jam();
            var flow = new GameFlow();
            flow.Begin(false);
            Assert.That(s.Tap(0), Is.EqualTo(0));
            Assert.That(s.Game.Status, Is.EqualTo(GameStatus.Stuck), "the rules say stuck at once");
            int frame = 0, landedAt = -1, panelAt = -1;
            while (panelAt < 0 && frame < 600)
            {
                Frame(s, flow);
                frame++;
                if (landedAt < 0 && s.Scheduler.FlightCount == 0) landedAt = frame;
                if (flow.Screen == FlowScreen.Stuck) panelAt = frame;
            }
            Assert.That(landedAt, Is.EqualTo(16), "the crate flies 0.26 s");
            Assert.That(panelAt, Is.GreaterThan(landedAt), "Out of moves only after the crate has landed");
            Assert.That((panelAt - landedAt + 1) * Dt, Is.GreaterThanOrEqualTo(GameFlow.PanelDelay - 1e-9));

            // +1 Slot (free): GameEngine.AddSlot un-sticks the game and the panel closes.
            Assert.That(s.CanAddSlot, Is.True);
            Assert.That(s.AddSlot(), Is.True);
            Assert.That(s.Game.State.SlotCount, Is.EqualTo(2));
            Assert.That(s.Game.Status, Is.EqualTo(GameStatus.Playing));
            Frame(s, flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(s.CanAddSlot, Is.False, "once per level");
            Assert.That(s.AddSlot(), Is.False);

            // The second crate opens the way: won, and the panel after the trucks.
            Assert.That(s.Tap(1), Is.EqualTo(1));
            for (int k = 0; k < 2000 && flow.Screen != FlowScreen.Won; k++) Frame(s, flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Won));
            Assert.That(s.TrucksOut, Is.Zero);
        }

        [Test]
        public void RetryStartsOverWithTheBoosterBack()
        {
            GameSession s = Jam();
            s.Tap(0);
            s.AddSlot();
            Assert.That(s.SlotBoostUsed, Is.True);
            // Retry: a new game of the same level (GameSessionDriver.Retry does this).
            GameSession again = Jam();
            var flow = new GameFlow();
            flow.Begin(false);
            Assert.That((again.SlotBoostUsed, again.CanAddSlot, again.Game.State.SlotCount, again.Game.Status),
                Is.EqualTo((false, true, 1, GameStatus.Playing)));
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        [Test]
        public void TheBoosterIsGoneOnceTheLevelIsWonOrTheSlotsAreSix()
        {
            var s = GameSession.For(Repo.Get("strawberry"));
            Assert.That(AutoPlayer.Play(s, Dt), Is.EqualTo(AutoPlayOutcome.Won));
            Assert.That(s.SlotBoostUsed, Is.False);
            Assert.That(s.CanAddSlot, Is.False, "not after a win (main.ts canSlot)");
            Assert.That(s.AddSlot(), Is.False);

            var six = new GameSession(new GameEngine(new Board(1, 1, new[] { 0 }, 1), new[] { new[] { new CrateSpec(0, 0, 1) } }, SimConstants.MaxSlots));
            Assert.That(six.CanAddSlot, Is.False, "six slots already");
        }

        [Test]
        public void TheStartScreenHoldsTheLevelUntilPlay()
        {
            GameSession s = Jam();
            var flow = new GameFlow();
            var seen = new List<FlowScreen>();
            flow.ScreenChanged += seen.Add;
            flow.Begin(true);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Start));
            s.Tap(0); // even a stuck game stays on the start screen (the driver does not advance it there)
            flow.Update(s, 5);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Start));
            flow.Play();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Playing));
            flow.Play();
            CollectionAssert.AreEqual(new[] { FlowScreen.Start, FlowScreen.Playing }, seen);
        }

        [Test]
        public void TheFirstThreeLevelsPlayOneAfterAnother()
        {
            int index = 0;
            var flow = new GameFlow();
            var played = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                LevelDefinition level = Repo.GetAt(index);
                var s = GameSession.For(level);
                s.SetSpeed(3);
                flow.Begin(k == 0);
                if (flow.Screen == FlowScreen.Start) flow.Play();
                int frames = 0;
                while (flow.Screen == FlowScreen.Playing && frames++ < 50_000) Frame(s, flow, autoPlay: true);
                Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Won), level.Name);
                Assert.That((s.Game.Board.Remaining, s.TrucksOut), Is.EqualTo((0, 0)), level.Name);
                played.Add(level.LevelId);
                Assert.That(GameFlow.IsLast(index, Repo.Count), Is.False);
                index = GameFlow.NextIndex(index, Repo.Count); // "Next level"
            }
            CollectionAssert.AreEqual(new[] { "strawberry", "mushroom", "sunset" }, played);
            Assert.That(index, Is.EqualTo(3));
            Assert.That(GameFlow.IsLast(9, 10), Is.True, "Back to level 1 after the last");
            Assert.That(GameFlow.NextIndex(9, 10), Is.Zero);
            Assert.That(GameFlow.PreviousIndex(0, 10), Is.EqualTo(9));
        }
    }
}
