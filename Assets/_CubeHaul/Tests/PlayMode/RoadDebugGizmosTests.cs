using System.Collections;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Levels;
using CubeHaul.Presentation;
using CubeHaul.Presentation.Roads;
using CubeHaul.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 13 in the game scene (docs/ROADS.md): the road debug component plans a route for every reachable cube of the
    /// strawberry, and its world points sit on the board's XZ plane where the cubes are: a truck parks one cell from its
    /// cube, and every waypoint inside the field is over an open cell.
    /// </summary>
    public class RoadDebugGizmosTests
    {
        GameSceneComposer.Composition scene;

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            scene = null;
        }

        [UnityTest]
        public IEnumerator RoutesInTheWorldEndBesideTheirCubesAndCrossOnlyOpenCells()
        {
            scene = GameSceneComposer.Compose();
            LevelDefinition level = LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors().Get("strawberry");
            scene.Grid.Show(level.Picture);
            Board board = level.CreateBoard();
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < board.ColorCount; c++)
                {
                    int t = board.PickTarget(c);
                    if (t >= 0) board.Eat(t);
                }
            scene.Grid.Bind(board);
            scene.Roads.ShowBoard(board);
            yield return null;

            RoadDebugGizmos roads = scene.Roads;
            int reachable = Enumerable.Range(0, board.ColorCount).Sum(board.ReachableCount);
            Assert.That(roads.Plans.Count, Is.EqualTo(reachable));
            Assert.That(reachable, Is.GreaterThan(10));
            BoardCoordinateMapper map = roads.Mapper;
            float cell = (float)(map.CellSize * WorldMapper.UnitsPerDesign);
            foreach (TruckRoute t in roads.Plans)
            {
                // The cube the truck goes for is the grid's cube for that cell.
                GameObject cube = scene.Grid.CubeAt(t.Target);
                Assert.That(cube, Is.Not.Null, "cell " + t.Target + " has its cube");
                Vector3 parked = roads.WorldPoint(t.Parking, 0);
                Vector3 cubeAt = cube.transform.position;
                Assert.That(Vector2.Distance(new Vector2(parked.x, parked.z), new Vector2(cubeAt.x, cubeAt.z)), Is.EqualTo(cell).Within(1e-4f),
                    "parks one cell from cube " + t.Target);
                foreach (PointD p in t.Out)
                {
                    Vector3 w = roads.WorldPoint(p, 0);
                    int under = map.WorldToCell(w.x, w.z);
                    if (under >= 0) Assert.That(board.IsOpen(under), Is.True, $"route to {t.Target}: waypoint over cell {under}, which is not open");
                }
                Assert.That(t.WayInSide, Is.EqualTo(Side.Bottom).Or.EqualTo(Side.Left).Or.EqualTo(Side.Right).Or.EqualTo(Side.Top));
                if (level.Edges == OpenEdges.Bottom) Assert.That(t.WayInSide, Is.EqualTo(Side.Bottom));
            }
        }

        [UnityTest]
        public IEnumerator OneTargetGivesOneRoute()
        {
            scene = GameSceneComposer.Compose();
            LevelDefinition level = LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors().Get("strawberry");
            Board board = level.CreateBoard();
            int target = board.PickTarget(0);
            scene.Roads.TargetCell = target;
            scene.Roads.ShowBoard(board);
            yield return null;
            Assert.That(scene.Roads.Plans.Count, Is.EqualTo(1));
            Assert.That(scene.Roads.Plans[0].Target, Is.EqualTo(target));
            scene.Roads.TargetCell = 0; // an empty corner of the strawberry: no cube, no route
            scene.Roads.Refresh();
            Assert.That(scene.Roads.Plans.Count, Is.EqualTo(level.Picture.Cells[0] < 0 ? 0 : 1));
        }
    }
}
