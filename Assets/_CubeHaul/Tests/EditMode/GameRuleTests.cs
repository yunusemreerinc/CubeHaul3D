using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// cube-hunt's game tests (tests/core.test.ts, Tools/ts-reference/tests/game.edge.test.ts, timing.test.ts and the
    /// FINDING tests of docs/TS_BASELINE.md §4) on the C# GameEngine, with the same pictures and expected values.
    /// </summary>
    public class GameRuleTests
    {
        // ---- helpers ----

        /// <summary>Rows of characters: '.' empty, a digit is that color; row 0 is the top.</summary>
        static Board Pic(OpenEdges edges, params string[] rows)
        {
            int[] cells = rows.SelectMany(r => r.Select(ch => ch == '.' ? Cells.Empty : ch - '0')).ToArray();
            int colors = Math.Max(1, cells.Max() + 1);
            return new Board(rows[0].Length, rows.Length, cells, colors, edges);
        }

        static Board Solid(int w, int h, OpenEdges edges = OpenEdges.Bottom) => Pic(edges, Enumerable.Repeat(new string('0', w), h).ToArray());

        /// <summary>
        /// A game on this board. Tray as "color:count" crates, front first, columns separated by '|' (an empty string
        /// between two bars is an empty column). Ids are given in reading order, as tests/helpers.ts level().
        /// </summary>
        static GameEngine Game(Board board, string tray, int slotCount = 5)
        {
            int id = 0;
            var columns = new List<IReadOnlyList<CrateSpec>>();
            foreach (string col in tray.Split('|'))
            {
                var specs = new List<CrateSpec>();
                foreach (string crate in col.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = crate.Split(':');
                    specs.Add(new CrateSpec(id++, int.Parse(parts[0]), int.Parse(parts[1])));
                }
                columns.Add(specs);
            }
            return new GameEngine(board, columns, slotCount);
        }

        static GameEngine Ring(string tray, int slotCount = 5) => Game(Pic(OpenEdges.All, "000", "010", "000"), tray, slotCount);

        static int[] Sent(IEnumerable<Dispatch> sent) => sent.Select(d => d.Cell).ToArray();

        /// <summary>Everything tap / tick / collect / addSlot may change, for "nothing changed" checks.</summary>
        internal static string Snapshot(GameEngine g)
        {
            GameState s = g.State;
            string columns = string.Join("|", Enumerable.Range(0, s.ColumnCount).Select(c =>
                string.Join(",", Enumerable.Range(0, s.ColumnLength(c)).Select(r => s.CrateAt(c, r)).Select(k => $"{k.Id}:{k.Color}:{k.Count}"))));
            string slots = string.Join(",", Enumerable.Range(0, s.SlotCount).Select(s.SlotAt).Select(k => k == null ? "-" : $"{k.Id}:{k.Color}:{k.Count}"));
            return $"{s.Status} [{columns}] [{slots}] {string.Join(",", GoldenScenarios.BoardValues(g.Board))} p{g.Board.Pending} r{g.Board.Remaining}";
        }

        // ---- core.test.ts: Game ----

        [Test]
        public void WinsWhenThePictureIsEaten()
        {
            GameEngine g = Ring("0:8|1:1");
            g.Tap(0);
            g.Tap(1);
            for (int guard = 0; g.Status == GameStatus.Playing && guard < 100; guard++) g.Step();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void IsStuckWhenEverySlotHoldsAColorThatCannotEat()
        {
            GameEngine g = Ring("1:1,0:8", 1);
            g.Tap(0);
            Assert.That(g.Status, Is.EqualTo(GameStatus.Stuck));
            g.AddSlot();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing));
            g.Tap(0);
            for (int guard = 0; g.Status == GameStatus.Playing && guard < 100; guard++) g.Step();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        // ---- the timing contract: tick claims, collect removes (core.test.ts "claimed cubes") ----

        [Test]
        public void AClaimedCubeStaysOnTheBoardBlockingWhatIsBehindItUntilCollected()
        {
            GameEngine g = Game(Solid(1, 3), "0:3", 1);
            g.Tap(0);
            CollectionAssert.AreEqual(new[] { 2 }, Sent(g.Tick()));
            Assert.That((g.Board.Cell(2), g.Board.IsClaimed(2), g.Board.IsOpen(2)), Is.EqualTo((0, true, false)), "claimed, still there, nothing opened");
            CollectionAssert.IsEmpty(g.Tick(), "cube 1 isn't reachable yet: cube 2 is claimed but still there");
            Assert.That((g.Board.Remaining, g.Board.Pending), Is.EqualTo((3, 1)));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing), "not stuck: a truck is still on its way");
            g.Collect(2);
            Assert.That((g.Board.Cell(2), g.Board.Remaining, g.Board.Pending), Is.EqualTo((Cells.Empty, 2, 0)));
            CollectionAssert.AreEqual(new[] { 1 }, Sent(g.Tick()));
        }

        [Test]
        public void IsNotDeclaredStuckWhileAPickupCouldStillOpenNewCubes()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "1", "0"), "0:1|1:1", 2);
            g.Tap(0);
            g.Tap(1);
            g.Tick();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing));
            g.Collect(1);
            CollectionAssert.AreEqual(new[] { 0 }, Sent(g.Tick()));
            g.Collect(0);
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void WonIsDeclaredWhenTheLastCubeIsCollectedNotWhenItsTruckIsSent()
        {
            GameEngine g = Game(Solid(1, 1), "0:1", 1);
            g.Tap(0);
            CollectionAssert.AreEqual(new[] { 0 }, Sent(g.Tick()));
            Assert.That((g.Board.Remaining, g.Status), Is.EqualTo((1, GameStatus.Playing)));
            g.Collect(0);
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void StepIsATickWithEveryTruckCollectingAtOnce()
        {
            GameEngine ticked = Game(Solid(5, 2), "0:10", 1), stepped = Game(Solid(5, 2), "0:10", 1);
            ticked.Tap(0);
            stepped.Tap(0);
            while (stepped.Status == GameStatus.Playing)
            {
                List<Dispatch> a = stepped.Step();
                List<Dispatch> b = ticked.Tick();
                foreach (Dispatch d in b) ticked.Collect(d.Cell);
                CollectionAssert.AreEqual(b, a);
                Assert.That(Snapshot(stepped), Is.EqualTo(Snapshot(ticked)));
                Assert.That(stepped.Board.Pending, Is.EqualTo(0));
            }
            Assert.That(stepped.Status, Is.EqualTo(GameStatus.Won));
        }

        // ---- core.test.ts: cubes next to the road first ----

        [Test]
        public void OpenOnEverySideRowsNearTheRampGoBeforeCubesOnTheFarEdges()
        {
            GameEngine g = Game(Pic(OpenEdges.All, "00000", "00000", ".000.", "..0.."), "0:4", 1);
            g.Tap(0);
            var claims = new List<int>();
            for (List<Dispatch> sent = g.Tick(); sent.Count > 0; sent = g.Tick()) claims.AddRange(Sent(sent));
            CollectionAssert.AreEqual(new[] { 17, 11, 13, 5 }, claims);
        }

        [Test]
        public void ACrateIsNotBlockedWhileATruckAlreadyOutWillUncoverItsColor()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "2", "1", ".", "0"), "1:1|0:1|2:1", 3);
            int red = g.Tap(0), green = g.Tap(2);
            Assert.That((g.State.SlotBlocked(red), g.State.SlotBlocked(green)), Is.EqualTo((true, true)), "no trucks out yet");
            int blue = g.Tap(1);
            CollectionAssert.AreEqual(new[] { 3 }, Sent(g.Tick()));
            Assert.That(g.State.SlotAt(blue), Is.Null);
            Assert.That((g.State.SlotBlocked(red), g.State.SlotBlocked(green)), Is.EqualTo((false, true)), "red opens once blue is collected; green stays behind red");
            g.Collect(3);
            Assert.That((g.State.SlotCanEat(red), g.State.SlotBlocked(red)), Is.EqualTo((true, false)));
        }

        [Test]
        public void LetsTheViewHoldASlotBackToSpaceOutItsTrucks()
        {
            GameEngine g = Game(Solid(3, 1), "0:3", 1);
            g.Tap(0);
            CollectionAssert.IsEmpty(g.Tick(_ => false));
            Assert.That(g.State.SlotAt(0).Count, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { 1 }, Sent(g.Tick(s => s == 0)));
            Assert.That(g.State.SlotAt(0).Count, Is.EqualTo(2));
        }

        [Test]
        public void TakesARowFromTheMiddleOutAndNeverMakesTrucksWait()
        {
            GameEngine g = Game(Solid(5, 2), "0:10", 1);
            g.Tap(0);
            var claims = new List<int>();
            for (List<Dispatch> sent = g.Tick(); sent.Count > 0; sent = g.Tick()) claims.AddRange(Sent(sent));
            CollectionAssert.AreEqual(new[] { 7, 6, 8, 5, 9 }, claims);
            g.Collect(7);
            CollectionAssert.AreEqual(new[] { 2 }, Sent(g.Tick()), "picking up the middle opens the cube above it at once");
            CollectionAssert.IsEmpty(g.Tick());
        }

        [Test]
        public void SendsOneTruckToEachReachableCube()
        {
            GameEngine g = Game(Solid(15, 2), "0:20", 1);
            g.Tap(0);
            var sent = new SortedSet<int>();
            for (int t = 0; t < 30; t++)
                foreach (Dispatch d in g.Tick()) Assert.That(sent.Add(d.Cell), Is.True, "cell " + d.Cell + " sent twice");
            CollectionAssert.AreEqual(Enumerable.Range(15, 15), sent);
            Assert.That(g.State.SlotAt(0).Count, Is.EqualTo(5));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing), "trucks are still on their way, so not stuck");
            g.Collect(15 + 7);
            CollectionAssert.AreEqual(new[] { 7 }, Sent(g.Tick()));
            CollectionAssert.IsEmpty(g.Tick());
        }

        // ---- game.edge.test.ts: full slots (B-10, I-12) ----

        [Test]
        public void ATapWithEverySlotTakenReturnsMinusOneAndChangesNothing()
        {
            GameEngine g = Game(Solid(3, 1), "0:1|0:1|0:1", 2);
            Assert.That((g.Tap(0), g.Tap(1)), Is.EqualTo((0, 1)));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing), "both crates can eat, so not stuck");
            Assert.That((g.State.HasFreeSlot(), g.State.CanTap(2)), Is.EqualTo((false, false)));
            string before = Snapshot(g);
            Assert.That(g.Tap(2), Is.EqualTo(-1));
            Assert.That(Snapshot(g), Is.EqualTo(before));
            Assert.That(g.State.ColumnLength(2), Is.EqualTo(1));
        }

        [Test]
        public void ASlotFreesOnTheTickThatSendsItsLastTruckAndTheNextTapTakesTheLowestFreeSlot()
        {
            GameEngine g = Game(Solid(4, 1), "0:1|0:2|0:1", 2);
            g.Tap(0);
            g.Tap(1);
            CollectionAssert.AreEqual(new[] { 0, 1 }, g.Tick().Select(d => d.Slot));
            Assert.That(g.State.SlotAt(0), Is.Null, "its only truck left");
            Assert.That(g.State.SlotAt(1).Count, Is.EqualTo(1));
            Assert.That(g.Tap(2), Is.EqualTo(0));
        }

        [Test]
        public void OnlyTheFrontCrateLeavesAColumn()
        {
            GameEngine g = Game(Solid(6, 1), "0:1,0:2,0:3", 3);
            Assert.That(g.State.Front(0).Id, Is.EqualTo(0));
            g.Tap(0);
            Assert.That(g.State.SlotAt(0).Id, Is.EqualTo(0));
            CollectionAssert.AreEqual(new[] { 1, 2 }, new[] { g.State.CrateAt(0, 0).Id, g.State.CrateAt(0, 1).Id });
            g.Tap(0);
            CollectionAssert.AreEqual(new int?[] { 0, 1, null }, Enumerable.Range(0, 3).Select(s => g.State.SlotAt(s)?.Id));
            Assert.That((g.State.ColumnLength(0), g.State.Front(0).Id), Is.EqualTo((1, 2)));
        }

        // ---- game.edge.test.ts: columns that cannot be tapped (B-10, B-13) ----

        static GameEngine TwoColumns() => Game(Solid(2, 1), "0:2|", 2);

        [Test]
        public void AnEmptyColumnReturnsMinusOne()
        {
            GameEngine g = TwoColumns();
            Assert.That(g.State.ColumnCount, Is.EqualTo(2));
            Assert.That((g.State.CanTap(1), g.Tap(1)), Is.EqualTo((false, -1)));
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(99)]
        [TestCase(int.MinValue)]
        public void AColumnIndexThatDoesNotExistReturnsMinusOneAndChangesNothing(int col)
        {
            GameEngine g = TwoColumns();
            string before = Snapshot(g);
            Assert.That(g.State.Front(col), Is.Null);
            Assert.That(g.Tap(col), Is.EqualTo(-1));
            Assert.That(Snapshot(g), Is.EqualTo(before));
        }

        [Test]
        public void AColumnIsEmptyOnceItsLastCrateIsTapped()
        {
            GameEngine g = TwoColumns();
            Assert.That(g.Tap(0), Is.EqualTo(0));
            Assert.That(g.State.Front(0), Is.Null);
            Assert.That(g.Tap(0), Is.EqualTo(-1));
        }

        [Test]
        public void NoTapIsAcceptedOnceTheGameIsOver()
        {
            GameEngine g = Game(Solid(1, 1), "0:1,0:5", 3);
            g.Tap(0);
            g.Step();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
            Assert.That((g.State.HasFreeSlot(), g.State.ColumnLength(0)), Is.EqualTo((true, 1)));
            Assert.That((g.State.CanTap(0), g.Tap(0)), Is.EqualTo((false, -1)));
        }

        // ---- game.edge.test.ts: won (B-13, I-14) ----

        [Test]
        public void WinningLeavesTheTrayAndTheSlotsEmpty()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "1100", "0011", "0101"), "0:3|1:3,0:3|1:3", 3);
            for (int guard = 0; g.Status != GameStatus.Won && guard < 100; guard++)
            {
                if (g.Status == GameStatus.Stuck) g.AddSlot();
                while (g.State.HasFreeSlot())
                {
                    int col = Enumerable.Range(0, g.State.ColumnCount).Where(c => g.State.ColumnLength(c) > 0).DefaultIfEmpty(-1).First();
                    if (g.Tap(col) < 0) break;
                }
                g.Step();
                CollectionAssert.IsEmpty(GameInvariants.Violations(g));
            }
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(g.State.TrayCount(), Is.EqualTo(0));
            Assert.That(Enumerable.Range(0, g.State.SlotCount).All(s => g.State.SlotAt(s) == null), Is.True);
        }

        [Test]
        public void WonIsFinalTickSendsNothingAndAddSlotDoesNotChangeIt()
        {
            GameEngine g = Game(Solid(1, 1), "0:1", 1);
            g.Tap(0);
            g.Step();
            CollectionAssert.IsEmpty(g.Tick());
            CollectionAssert.IsEmpty(g.Step());
            g.AddSlot();
            Assert.That((g.Status, g.State.SlotCount), Is.EqualTo((GameStatus.Won, 2)));
        }

        [Test]
        public void Finding_F2_ALevelWithNoCubesStartsPlayingAndIsWonOnlyOnTheFirstTick()
        {
            // TS_BASELINE F-2, kept as is: the constructor does not update the status.
            GameEngine g = Game(Pic(OpenEdges.Bottom, "..", ".."), "", 5);
            Assert.That((g.Board.Remaining, g.Status), Is.EqualTo((0, GameStatus.Playing)));
            CollectionAssert.IsEmpty(g.Tick());
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        // ---- game.edge.test.ts: stuck (B-13, I-14, I-15) ----

        [Test]
        public void StuckIsNotDeclaredWhileASlotIsFree()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "1", "0"), "1:1|0:1", 2);
            g.Tap(0);
            Assert.That((g.State.SlotCanEat(0), g.Status), Is.EqualTo((false, GameStatus.Playing)));
        }

        [Test]
        public void StuckIsDeclaredAsSoonAsTheLastFreeSlotIsFilledWithACrateThatCannotEat()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "1", "0"), "1:1,0:1", 1);
            g.Tap(0);
            Assert.That(g.Status, Is.EqualTo(GameStatus.Stuck));
            string before = Snapshot(g);
            CollectionAssert.IsEmpty(g.Tick());
            CollectionAssert.IsEmpty(g.Step());
            Assert.That(g.Tap(0), Is.EqualTo(-1));
            Assert.That(Snapshot(g), Is.EqualTo(before));
        }

        [Test]
        public void StuckIsNotDeclaredWhileATruckIsStillOut()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "2", "1", "0"), "0:1|2:1|1:1", 2);
            g.Tap(0);
            g.Tap(1);
            g.Tick();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing));
            g.Tap(2);
            Assert.That(g.Board.Pending, Is.EqualTo(1));
            Assert.That((g.State.IsStuck(), g.Status), Is.EqualTo((false, GameStatus.Playing)), "I-15");
            g.Collect(2);
            Assert.That((g.State.SlotCanEat(0), g.Status), Is.EqualTo((true, GameStatus.Playing)));
        }

        [Test]
        public void StuckCanBeReachedByACollect()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "0", "1", "0"), "0:2,1:1", 1);
            g.Tap(0);
            CollectionAssert.AreEqual(new[] { 2 }, Sent(g.Tick()));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing), "a truck is out");
            g.Collect(2);
            Assert.That((g.Board.ReachableCount(1), g.State.SlotAt(0).Count, g.Status), Is.EqualTo((1, 1, GameStatus.Stuck)));
        }

        // ---- game.edge.test.ts: +1 Slot (B-15, B-30) ----

        [Test]
        public void StuckThenAddSlotThenPlayingAndTheLevelCanStillBeWon()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "1", "0"), "1:1,0:1", 1);
            g.Tap(0);
            Assert.That(g.Status, Is.EqualTo(GameStatus.Stuck));
            Assert.That(g.State.TrayCount(), Is.GreaterThan(0), "liveness: +1 Slot always gives a move");
            g.AddSlot();
            Assert.That((g.Status, g.State.SlotCount), Is.EqualTo((GameStatus.Playing, 2)));
            Assert.That(g.Tap(0), Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 1 }, Sent(g.Step()));
            CollectionAssert.AreEqual(new[] { 0 }, Sent(g.Step()));
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
        }

        [Test]
        public void AddingASlotWhilePlayingOnlyAddsAFreeSlotAtTheEnd()
        {
            GameEngine g = Game(Solid(3, 1), "0:1|0:2", 1);
            g.Tap(0);
            g.AddSlot();
            Assert.That(g.Status, Is.EqualTo(GameStatus.Playing));
            CollectionAssert.AreEqual(new int?[] { 0, null }, Enumerable.Range(0, 2).Select(s => g.State.SlotAt(s)?.Id));
            Assert.That(g.Tap(1), Is.EqualTo(1));
        }

        [Test]
        public void TheCoreHasNoLimitOnSlots()
        {
            GameEngine g = Game(Solid(1, 1), "0:1", 5);
            for (int k = 0; k < 4; k++) g.AddSlot();
            Assert.That(g.State.SlotCount, Is.EqualTo(9));
        }

        // ---- the level passed in is never changed (B-16) ----

        [Test]
        public void PlayingAGameLeavesTheLevelUntouched()
        {
            LevelRepository repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors();
            LevelDefinition level = repo.Get("strawberry");
            LevelDefinition before = level.DeepCopy();
            GameEngine g = level.CreateGame();
            for (int guard = 0; g.Status == GameStatus.Playing && guard < 10_000; guard++)
            {
                for (int c = 0; c < g.State.ColumnCount && g.State.HasFreeSlot(); c++) g.Tap(c);
                g.Step();
            }
            Assert.That(g.Board.Remaining, Is.LessThan(level.Picture.CubeCount));
            Assert.That(level.ContentEquals(before), Is.True, "the level was changed by playing it");
            GameEngine again = level.CreateGame();
            Assert.That((again.State.Front(0).Count, again.Board.Remaining, again.State.TrayCount()),
                Is.EqualTo((level.Tray.Columns[0][0].Count, level.Picture.CubeCount, level.Tray.BlockCount)));
        }

        // ---- timing.test.ts: the rules have no clock ----

        [Test]
        public void TicksWithEverySlotHeldBackChangeNothing()
        {
            GameEngine g = Game(Solid(3, 2), "0:6", 1);
            g.Tap(0);
            string before = Snapshot(g);
            var buffer = new List<Dispatch>();
            Func<int, bool> none = _ => false;
            for (int k = 0; k < 1000; k++) Assert.That(g.Tick(none, buffer), Is.EqualTo(0));
            Assert.That(Snapshot(g), Is.EqualTo(before));
        }

        [Test]
        public void IdleTicksBetweenCallsDoNotChangeWhatTheNextCallDoes()
        {
            List<int> Run(int idle)
            {
                GameEngine g = Game(Solid(4, 2), "0:8", 1);
                g.Tap(0);
                var claims = new List<int>();
                for (int guard = 0; g.Status == GameStatus.Playing && guard < 100; guard++)
                {
                    for (int k = 0; k < idle; k++) g.Tick(_ => false);
                    List<Dispatch> sent = g.Tick();
                    for (int k = 0; k < idle; k++) g.Tick(_ => false);
                    foreach (Dispatch d in sent)
                    {
                        claims.Add(d.Cell);
                        g.Collect(d.Cell);
                    }
                }
                Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
                return claims;
            }
            CollectionAssert.AreEqual(Run(0), Run(7));
        }

        // ---- FINDING F-3 (board.edge.test.ts), kept as is ----

        [Test]
        public void Finding_F3_CollectAlsoAcceptsAReachableCubeNoTruckWasSentFor()
        {
            GameEngine g = Game(Solid(2, 1), "0:2", 1);
            g.Collect(0);
            Assert.That((g.Board.Remaining, g.Board.Pending, g.State.Front(0).Count), Is.EqualTo((1, 0, 2)), "the tray did not pay for it");
        }

        // ---- C# specifics ----

        [Test]
        public void ReadyIsAskedOnlyForTakenSlotsInSlotOrder()
        {
            GameEngine g = Game(Solid(6, 1), "0:1|0:1|0:1", 5);
            g.Tap(0);
            g.Tap(1);
            g.Tap(2);
            g.Tick(s => s == 1); // slot 1's crate goes; its slot frees
            var asked = new List<int>();
            List<Dispatch> sent = g.Tick(s =>
            {
                asked.Add(s);
                return true;
            });
            CollectionAssert.AreEqual(new[] { 0, 2 }, asked);
            CollectionAssert.AreEqual(new[] { 0, 2 }, sent.Select(d => d.Slot));
        }

        [Test]
        public void ACrateWhoseColorHasNothingReachableSendsNothingAndKeepsItsTrucks()
        {
            GameEngine g = Game(Pic(OpenEdges.Bottom, "1", "0"), "1:1|0:1", 2);
            g.Tap(0);
            g.Tap(1);
            List<Dispatch> sent = g.Tick();
            Assert.That(sent.Select(d => (d.Slot, d.Cell, d.Color)), Is.EqualTo(new[] { (1, 1, 0) }));
            Assert.That(g.State.SlotAt(0).Count, Is.EqualTo(1));
        }

        [Test]
        public void TheOutputListIsClearedFirst()
        {
            GameEngine g = Game(Solid(3, 1), "0:3", 1);
            g.Tap(0);
            var buffer = new List<Dispatch> { new Dispatch(9, 9, 9) };
            Assert.That(g.Tick(null, buffer), Is.EqualTo(1));
            Assert.That(buffer.Single().Cell, Is.EqualTo(1));
            Assert.That(g.Tick(_ => false, buffer), Is.EqualTo(0));
            CollectionAssert.IsEmpty(buffer);
            Assert.That(g.StepQuietly(), Is.EqualTo(1));
        }

        [Test]
        public void CollectingSomethingUnreachableThrowsAndChangesNothing()
        {
            GameEngine g = Game(Solid(1, 3), "0:3", 1);
            g.Tap(0);
            g.Tick();
            string before = Snapshot(g);
            Assert.Throws<InvalidOperationException>(() => g.Collect(1), "behind the claimed cube");
            Assert.Throws<InvalidOperationException>(() => g.Collect(0), "cell 0, not reachable");
            Assert.Throws<IndexOutOfRangeException>(() => g.Collect(7));
            Assert.That(Snapshot(g), Is.EqualTo(before));
            g.Collect(2);
            Assert.Throws<InvalidOperationException>(() => g.Collect(2), "already empty");
        }

        [Test]
        public void TheEngineCopiesTheTrayAndRejectsBadInput()
        {
            var column = new List<CrateSpec> { new CrateSpec(0, 0, 2) };
            var g = new GameEngine(Solid(2, 1), new[] { column }, 1);
            column.Add(new CrateSpec(1, 0, 1));
            Assert.That(g.State.ColumnLength(0), Is.EqualTo(1));

            Assert.Throws<ArgumentNullException>(() => new GameEngine(null, new List<IReadOnlyList<CrateSpec>>(), 1));
            Assert.Throws<ArgumentNullException>(() => new GameEngine(Solid(1, 1), null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameEngine(Solid(1, 1), new List<IReadOnlyList<CrateSpec>>(), -1));
            Assert.Throws<ArgumentException>(() => new GameEngine(Solid(1, 1), new IReadOnlyList<CrateSpec>[] { null }, 1));
            Board used = Solid(1, 1);
            used.Claim(0);
            Assert.Throws<ArgumentException>(() => new GameEngine(used, new List<IReadOnlyList<CrateSpec>>(), 1));
            Assert.Throws<ArgumentNullException>(() => g.Tick(null, null));
        }

        [Test]
        public void QueriesOutsideTheSlotsAndColumnsAnswerNothing()
        {
            GameEngine g = Game(Solid(2, 1), "0:2", 1);
            GameState s = g.State;
            Assert.That((s.SlotAt(-1), s.SlotAt(1), s.CrateAt(0, 1), s.CrateAt(1, 0), s.ColumnLength(-1)), Is.EqualTo(((Crate)null, (Crate)null, (Crate)null, (Crate)null, 0)));
            Assert.That((s.SlotCanEat(-1), s.SlotCanEat(3), s.SlotBlocked(5), s.FirstFreeSlot()), Is.EqualTo((false, false, false, 0)));
        }

        [Test]
        public void ALevelWithoutAStaticTrayCannotBePlayedYet()
        {
            LevelRepository repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors();
            LevelDefinition level = repo.Get("cat");
            level.Tray = null;
            Assert.Throws<InvalidOperationException>(() => level.CreateGame());
        }
    }
}
