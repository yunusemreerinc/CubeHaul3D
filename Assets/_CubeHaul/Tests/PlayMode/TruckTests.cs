using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;
using CubeHaul.Presentation;
using CubeHaul.Presentation.Grid;
using CubeHaul.Presentation.Trucks;
using CubeHaul.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 14 acceptance (docs/TRUCKS.md): a pooled, colored low-poly truck in the game scene drives its planned route
    /// to its cube, turns, and drives back down its ramp lane into the tunnel; then it is back in the pool.
    /// </summary>
    public class TruckTests
    {
        const float Dt = 1 / 60f;

        GameSceneComposer.Composition scene;
        readonly List<GameObject> extra = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            foreach (GameObject go in extra) Object.Destroy(go);
            extra.Clear();
            scene = null;
        }

        static LevelDefinition Strawberry() => LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors().Get("strawberry");

        static Board Peeled(LevelDefinition level, int rounds)
        {
            Board b = level.CreateBoard();
            for (int r = 0; r < rounds; r++)
                for (int c = 0; c < b.ColorCount; c++)
                {
                    int t = b.PickTarget(c);
                    if (t >= 0) b.Eat(t);
                }
            return b;
        }

        [Test]
        public void TheTruckHasABodyACabinFourWheelsAndABedAndNoColliders()
        {
            TruckVisual truck = TruckModelBuilder.Build();
            extra.Add(truck.gameObject);
            Transform t = truck.transform;
            Assert.That(t.Find("Body/Chassis"), Is.Not.Null);
            Assert.That(t.Find("Cabin/Cab"), Is.Not.Null);
            Assert.That(t.Find("Cabin/Hood"), Is.Not.Null);
            Assert.That(t.Find("Bed/Floor"), Is.Not.Null);
            Assert.That(t.Find("Bed/Cargo"), Is.Not.Null);
            Assert.That(truck.Wheels.Length, Is.EqualTo(4));
            Assert.That(t.Find("Wheels").childCount, Is.EqualTo(4));
            Assert.That(truck.GetComponentsInChildren<Collider>(true), Is.Empty, "no physics");
            Assert.That(truck.Paint.Length, Is.GreaterThanOrEqualTo(4));
            Assert.That(truck.Cargo.gameObject.activeSelf, Is.False, "empty until it loads");
            // About cube-hunt's truck: 21.4 long, 11.4 wide, facing +Z, standing on the ground.
            Bounds b = new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in truck.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);
            Assert.That(b.size.z, Is.EqualTo(21.4f).Within(0.6f));
            Assert.That(b.size.x, Is.EqualTo(11.4f).Within(1.2f));
            Assert.That(b.min.y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(t.Find("Cabin/Hood").position.z, Is.GreaterThan(t.Find("Bed/Floor").position.z), "cabin in front, bed behind");
        }

        [UnityTest]
        public IEnumerator ATruckDrivesItsRouteToItsCubeAndBackDownItsRamp()
        {
            scene = GameSceneComposer.Compose();
            LevelDefinition level = Strawberry();
            Board board = Peeled(level, 8);
            scene.Grid.Show(level.Picture);
            scene.Grid.Bind(board);
            TruckFleetView fleet = scene.Trucks;
            fleet.AutoStep = false;
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(board);
            int cell = board.PickTarget(0);
            board.Claim(cell);
            TruckRoute route = roads.PlanTruck(cell);
            PaletteColor red = PaletteColor.Parse(level.Picture.Palette[0]);
            TruckMotion motion = fleet.Send(route, roads.Center(cell), roads.CellSize, roads.Ring, red);
            TruckVisual truck = fleet.VisualOf(motion);
            yield return null;

            Color32 paint = truck.Paint[0].sharedMaterial.GetColor("_BaseColor");
            Assert.That((paint.r, paint.g, paint.b), Is.EqualTo((red.R, red.G, red.B)), "painted in the crate's color");
            Assert.That(fleet.Pool.InUseCount, Is.EqualTo(1));

            bool arrived = false, returned = false, wasOut = false;
            fleet.Arrived += m => { arrived = true; board.Eat(m.Cell); };
            fleet.Returned += m => returned = true;
            Vector3 lastSeen = Vector3.zero;
            float maxTurn = 0, previousYaw = truck.transform.eulerAngles.y;
            for (int frame = 0; frame < 6000 && !returned; frame++)
            {
                fleet.Step(Dt);
                if (!truck.gameObject.activeSelf) continue;
                wasOut = true;
                lastSeen = truck.transform.position;
                float yaw = truck.transform.eulerAngles.y;
                maxTurn = Mathf.Max(maxTurn, Mathf.Abs(Mathf.DeltaAngle(previousYaw, yaw)));
                previousYaw = yaw;
                if (motion.State == TruckState.Out || motion.State == TruckState.Home)
                {
                    // The model is where the motion says, facing where it heads.
                    PointD pose = motion.Pose();
                    Vector3 at = WorldMapper.FromDesign(pose.X, pose.Y, 0);
                    Assert.That(new Vector2(truck.transform.position.x - at.x, truck.transform.position.z - at.z).magnitude, Is.LessThan(1e-4f));
                    Vector3 heading = new Vector3(Mathf.Cos((float)motion.Angle), 0, -Mathf.Sin((float)motion.Angle));
                    Assert.That(Vector3.Dot(truck.transform.forward, heading), Is.GreaterThan(0.999f), "faces its heading");
                }
                if (motion.State == TruckState.Load && !arrived) Assert.Fail("loading before arriving");
            }
            Assert.That(wasOut && arrived && returned, Is.True, $"out {wasOut}, arrived {arrived}, back {returned}");
            Assert.That(board.Cell(cell), Is.EqualTo(Cells.Empty), "its cube was collected when it arrived");
            Assert.That(motion.State, Is.EqualTo(TruckState.Gone));
            Assert.That((float)motion.X, Is.EqualTo((float)route.Start.X), "back at its tunnel lane");
            Assert.That((float)motion.Y, Is.EqualTo((float)route.Start.Y));
            Vector3 tunnel = WorldMapper.FromDesign(route.Start.X, route.Start.Y);
            Assert.That(new Vector2(lastSeen.x - tunnel.x, lastSeen.z - tunnel.z).magnitude, Is.LessThan(0.2f), "last seen going down its ramp");
            Assert.That(maxTurn, Is.LessThan(14f * Mathf.Rad2Deg * Dt + 0.01f), "no snap turns");
            Assert.That(truck.gameObject.activeSelf, Is.False, "back in the pool");
            Assert.That((fleet.Pool.InUseCount, fleet.Pool.FreeCount, fleet.ActiveCount), Is.EqualTo((0, 1, 0)));
        }

        [Test]
        public void AHiddenTruckStillGoesBackToThePool()
        {
            // A truck sinking into the tunnel is hidden (inactive) before it is released; it must still be pooled.
            var root = new GameObject("pool test");
            extra.Add(root);
            var pool = new TruckPool(root.transform, null);
            TruckVisual a = pool.Get();
            a.gameObject.SetActive(false);
            pool.Release(a);
            pool.Release(a); // twice: still once in the pool
            Assert.That((pool.FreeCount, pool.InUseCount), Is.EqualTo((1, 0)));
            Assert.That(pool.Get(), Is.SameAs(a), "the same truck comes back");
            Assert.That(pool.CreatedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TrucksComeFromThePool()
        {
            scene = GameSceneComposer.Compose();
            LevelDefinition level = Strawberry();
            Board board = Peeled(level, 5);
            TruckFleetView fleet = scene.Trucks;
            fleet.AutoStep = false;
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(board);
            var targets = Enumerable.Range(0, board.ColorCount).Select(board.PickTarget).Where(t => t >= 0).ToArray();
            for (int round = 0; round < 3; round++)
            {
                foreach (int t in targets)
                    fleet.Send(roads.PlanTruck(t), roads.Center(t), roads.CellSize, roads.Ring, new PaletteColor(200, 80, 80));
                for (int frame = 0; frame < 6000 && fleet.ActiveCount > 0; frame++) fleet.Step(Dt);
                Assert.That(fleet.ActiveCount, Is.EqualTo(0), "every truck came home");
            }
            yield return null;
            Assert.That(fleet.Pool.CreatedCount, Is.EqualTo(targets.Length), "three rounds, one set of trucks");
            Assert.That(fleet.Pool.FreeCount, Is.EqualTo(targets.Length));
        }

        [UnityTest]
        public IEnumerator TheDemoPlaysALevelWithTrucks()
        {
            scene = GameSceneComposer.Compose();
            LevelDefinition level = Strawberry();
            var game = level.CreateGame();
            scene.Grid.Show(level.Picture);
            scene.Grid.Bind(game.Board);
            TruckFleetView fleet = scene.Trucks;
            fleet.AutoStep = false;
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(game.Board);
            fleet.Arrived += m => game.Collect(m.Cell);
            var sent = new List<Dispatch>();
            for (int frame = 0; frame < 60 * 400 && (game.Status != GameStatus.Won || fleet.ActiveCount > 0); frame++)
            {
                if (frame % 9 == 0 && game.Status == GameStatus.Playing)
                {
                    for (int c = 0; c < game.State.ColumnCount && game.State.HasFreeSlot(); c++)
                        if (game.State.ColumnLength(c) > 0 && game.Board.ReachableCount(game.State.Front(c).Color) > 0) game.Tap(c);
                    game.Tick(null, sent);
                    foreach (Dispatch d in sent)
                        fleet.Send(roads.PlanTruck(d.Cell), roads.Center(d.Cell), roads.CellSize, roads.Ring, scene.Grid.Field.Palette[d.Color]);
                    // Nothing can eat and no truck is out: tap a blocked crate, as a player would.
                    if (sent.Count == 0 && fleet.ActiveCount == 0 && game.State.HasFreeSlot())
                        for (int c = 0; c < game.State.ColumnCount; c++)
                            if (game.Tap(c) >= 0) break;
                }
                if (game.Status == GameStatus.Stuck) game.AddSlot();
                fleet.Step(Dt);
                if (frame % 600 == 0) yield return null;
            }
            yield return null;
            Assert.That(game.Status, Is.EqualTo(GameStatus.Won), "the trucks collected every cube");
            Assert.That(fleet.ActiveCount, Is.EqualTo(0), "and all came home");
            Assert.That(scene.Grid.Field.VisibleCount, Is.EqualTo(0));
            Assert.That(fleet.Pool.CreatedCount, Is.LessThan(level.Picture.CubeCount), "trucks are reused");
        }
    }
}
