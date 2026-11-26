using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Levels;
using CubeHaul.Tests.EditMode.Parity;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The cube field the 3D view draws (docs/GRID_VIEW.md): one cube per non-empty cell, the same cell index as the
    /// board, the palette color, cube-hunt's cell centers; and it follows the game: claimed cubes stay, collected ones go.
    /// </summary>
    public class CubeFieldTests
    {
        static LevelRepository repo;
        static LevelRepository Repo => repo ?? (repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors());

        static IEnumerable<string> LevelIds() => LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().LevelIds;

        [Test]
        public void TheStrawberryHasACubeForEveryColoredCellInItsColor()
        {
            PictureDefinition p = Repo.Get("strawberry").Picture;
            var field = new CubeField(p);
            Assert.That((field.Width, field.Height, field.CellCount), Is.EqualTo((29, 29, 841)));
            Assert.That((field.VisibleCount, field.PresentCount, field.ClaimedCount), Is.EqualTo((527, 527, 0)));
            CollectionAssert.AreEqual(new[] { "#ec4a5a", "#48c870", "#165030", "#ffe27a" }, field.Palette.Select(c => c.ToString()));
            for (int i = 0; i < field.CellCount; i++)
            {
                Assert.That(field.ColorOf(i), Is.EqualTo(p.Cells[i]), "cell " + i);
                Assert.That(field.IsVisible(i), Is.EqualTo(p.Cells[i] != Cells.Empty), "cell " + i);
            }
            for (int c = 0; c < p.Palette.Length; c++)
                Assert.That(Enumerable.Range(0, field.CellCount).Count(i => field.ColorOf(i) == c), Is.EqualTo(p.CountOf(c)));
        }

        [TestCaseSource(nameof(LevelIds))]
        public void CellIndicesAndPlacesMatchTheBoardAndCubeHunt(string levelId)
        {
            LevelDefinition level = Repo.Get(levelId);
            var field = new CubeField(level.Picture);
            Board board = level.CreateBoard();
            Assert.That(field.CellCount, Is.EqualTo(board.CellCount));
            Assert.That(field.VisibleCount, Is.EqualTo(board.Remaining));
            double cs = DesignLayout.CellSize(field.Width, field.Height);
            Assert.That(field.CellSize, Is.EqualTo(cs).Within(1e-9));
            for (int i = 0; i < field.CellCount; i++)
            {
                Assert.That(field.ColorOf(i), Is.EqualTo(board.Cell(i)), "the same cell index addresses the same cube");
                (double x, double y) = field.DesignCenter(i);
                (double ex, double ey) = DesignLayout.CellCenter(i, field.Width, field.Height);
                Assert.That(x, Is.EqualTo(ex).Within(1e-9));
                Assert.That(y, Is.EqualTo(ey).Within(1e-9));
                Assert.That(field.CellAt(x, y), Is.EqualTo(i), "center -> cell");
                Assert.That(field.CellAt(x - cs * 0.49, y + cs * 0.49), Is.EqualTo(i), "anywhere in the cell -> cell");
                Assert.That((field.Row(i), field.Column(i)), Is.EqualTo((i / field.Width, i % field.Width)));
            }
            // Row 0 is the top (smallest design y, farthest from the ramp); column 0 is the left.
            Assert.That(field.DesignCenter(field.Width).y, Is.GreaterThan(field.DesignCenter(0).y));
            Assert.That(field.DesignCenter(1).x, Is.GreaterThan(field.DesignCenter(0).x));
            Assert.That(DesignLayout.Board.Contains(field.Field), Is.True);
            Assert.That(field.CellAt(field.Field.XMin - 0.01, field.Field.YMin + 1), Is.EqualTo(-1));
            Assert.That(field.CellAt(field.Field.XMax + 0.01, field.Field.YMin + 1), Is.EqualTo(-1));
            Assert.That(field.CellAt(field.Field.XMin + 1, field.Field.YMax + 0.01), Is.EqualTo(-1));
        }

        [Test]
        public void AClaimedCubeStaysAndACollectedCubeGoes()
        {
            LevelDefinition level = Repo.Get("strawberry");
            GameEngine game = level.CreateGame();
            var field = new CubeField(level.Picture);
            var changed = new List<int>();
            Assert.That(field.Sync(game.Board, changed), Is.EqualTo(0));

            for (int c = 0; c < game.State.ColumnCount && game.State.HasFreeSlot(); c++) game.Tap(c);
            List<Dispatch> sent = game.Tick();
            Assert.That(sent, Is.Not.Empty);
            Assert.That(field.Sync(game.Board, changed), Is.EqualTo(sent.Count));
            CollectionAssert.AreEquivalent(sent.Select(d => d.Cell), changed);
            foreach (Dispatch d in sent)
                Assert.That((field.StateOf(d.Cell), field.IsVisible(d.Cell)), Is.EqualTo((CubeState.Claimed, true)), "claimed: still shown");
            Assert.That((field.VisibleCount, field.ClaimedCount), Is.EqualTo((527, sent.Count)));

            changed.Clear();
            game.Collect(sent[0].Cell);
            Assert.That(field.Sync(game.Board, changed), Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(new[] { sent[0].Cell }));
            Assert.That((field.StateOf(sent[0].Cell), field.IsVisible(sent[0].Cell)), Is.EqualTo((CubeState.Empty, false)), "collected: gone");
            Assert.That((field.VisibleCount, field.ClaimedCount), Is.EqualTo((526, sent.Count - 1)));
        }

        [Test]
        public void MarkingByHandFollowsTheSameRules()
        {
            var field = new CubeField(Repo.Get("strawberry").Picture);
            int cell = Enumerable.Range(0, field.CellCount).First(field.IsVisible);
            int empty = Enumerable.Range(0, field.CellCount).First(i => !field.IsVisible(i));
            Assert.That(field.MarkClaimed(empty), Is.False);
            Assert.That(field.MarkClaimed(cell), Is.True);
            Assert.That(field.MarkClaimed(cell), Is.False, "already claimed");
            Assert.That(field.MarkCollected(cell), Is.True);
            Assert.That(field.MarkCollected(cell), Is.False, "already gone");
            Assert.That(field.MarkClaimed(cell), Is.False, "a collected cube does not come back");
            Assert.That(field.VisibleCount, Is.EqualTo(526));
        }

        /// <summary>
        /// The view follows the real game through cell indices only: replaying parity scenarios (TypeScript-recorded
        /// games) and syncing after every call, the shown cubes are exactly the board's cubes, the claimed ones exactly
        /// the board's claimed cells, and the cubes that go are exactly the cells TypeScript reported emptied.
        /// </summary>
        [TestCase("p01-strawberry-greedy-step.json")]
        [TestCase("p02-strawberry-generated-delayed.json")]
        [TestCase("p05-mushroom-abuse.json")]
        [TestCase("p12-cat-jam.json")]
        [TestCase("p21-icecream-sides-delayed.json")]
        public void TheFieldFollowsARecordedGameStepByStep(string file)
        {
            ParityScenario s = ParityScenario.Load(file);
            GameEngine game = ParityRunner.Setup(s, out string _);
            LevelDefinition level = LevelDefinitionReader.Read(System.IO.File.ReadAllText(ProjectPaths.Of(s.LevelFile))).Level;
            var field = new CubeField(level.Picture);
            var changed = new List<int>();
            var sent = new List<Dispatch>();
            for (int k = 1; k < s.Steps.Count; k++)
            {
                ParityStep step = s.Steps[k];
                switch (step.Op)
                {
                    case "tap": game.Tap(step.Col.Value); break;
                    case "tick": game.Tick(step.Ready == null ? (Func<int, bool>)null : slot => slot < step.Ready.Length && step.Ready[slot], sent); break;
                    case "step": game.Step(sent); break;
                    case "collect": game.Collect(step.Cell.Value); break;
                    case "addSlot": game.AddSlot(); break;
                }
                changed.Clear();
                field.Sync(game.Board, changed);
                Board b = game.Board;
                string where = $"{file} step {k} ({step.Describe()})";
                Assert.That(field.VisibleCount, Is.EqualTo(b.Remaining), where + ": cubes shown = cubes on the board");
                Assert.That(field.ClaimedCount, Is.EqualTo(b.Pending), where + ": claimed cubes shown");
                int[] gone = changed.Where(i => field.StateOf(i) == CubeState.Empty).OrderBy(i => i).ToArray();
                Assert.That("[" + string.Join(",", gone) + "]", Is.EqualTo(step.Expected["emptied"]), where + ": the cubes that go are the cells TypeScript emptied");
            }
        }

        [Test]
        public void PaletteColorsParseAndRejectBadText()
        {
            Assert.That(PaletteColor.Parse("#EC4a5A"), Is.EqualTo(new PaletteColor(0xec, 0x4a, 0x5a)));
            Assert.That(PaletteColor.Parse("#000000").ToString(), Is.EqualTo("#000000"));
            foreach (string bad in new[] { null, "", "ec4a5a", "#ec4a5", "#ec4a5a0", "#gg0000", " #ec4a5a" })
                Assert.That(PaletteColor.TryParse(bad, out _), Is.False, bad ?? "null");
            Assert.Throws<FormatException>(() => PaletteColor.Parse("red"));
        }

        [Test]
        public void BadPicturesAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new CubeField(null));
            Assert.Throws<ArgumentException>(() => new CubeField(new PictureDefinition(2, 2, new[] { 0, 0, 0 }, new[] { "#000000" })));
            Assert.Throws<ArgumentException>(() => new CubeField(new PictureDefinition(1, 1, new[] { 3 }, new[] { "#000000" })));
            var field = new CubeField(new PictureDefinition(2, 1, new[] { 0, 0 }, new[] { "#000000" }));
            Assert.Throws<ArgumentException>(() => field.Sync(new Board(3, 1, new[] { 0, 0, 0 }, 1)));
        }
    }
}
