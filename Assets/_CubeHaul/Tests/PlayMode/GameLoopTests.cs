using System.Collections;
using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Session;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;
using CubeHaul.Presentation.Grid;
using CubeHaul.Presentation.Session;
using CubeHaul.Presentation.Trucks;
using CubeHaul.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 15 acceptance (docs/SESSION.md): in the game scene, the first level's truck loop runs from start to end. The
    /// game loop sends a truck for every claimed cube; the cube stays on the board until its truck reaches it, then
    /// flies from its cell into the truck's bed; the trucks drive home; the level is won with every cube and truck back in
    /// its pool. The speed button switches 1x / 2x / 3x.
    /// </summary>
    public class GameLoopTests
    {
        const float Dt = 1 / 60f;

        GameSceneComposer.Composition scene;

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            scene = null;
        }

        static LevelDefinition Strawberry() => LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors().Get("strawberry");

        /// <summary>The scene with its loop driven by hand, frame by frame (its own Update off).</summary>
        GameSessionDriver Compose()
        {
            scene = GameSceneComposer.Compose();
            GameSessionDriver driver = scene.Session;
            driver.PlayOnStart = false;
            driver.enabled = false;
            scene.Trucks.AutoStep = false;
            return driver;
        }

        void Frame(GameSessionDriver driver)
        {
            driver.Frame(Dt);
            scene.Grid.SyncNow();
        }

        [UnityTest]
        public IEnumerator TheFirstLevelsTruckLoopRunsFromStartToEnd()
        {
            GameSessionDriver driver = Compose();
            LevelDefinition level = Strawberry();
            driver.AutoPlay = true;
            Assert.That(driver.Play(level), Is.True);
            GameSession session = driver.Session;
            GridPresenter grid = scene.Grid;
            TruckFleetView fleet = scene.Trucks;
            int cubes = level.Picture.CubeCount;
            Assert.That(grid.Pool.InUseCount, Is.EqualTo(cubes));

            var sent = new HashSet<int>();
            var problems = new List<string>();
            int loadsSeen = 0, flightsChecked = 0;
            session.TruckSent += t =>
            {
                if (!sent.Add(t.Cell)) problems.Add($"cell {t.Cell}: a second truck");
                if (grid.CubeAt(t.Cell) == null) problems.Add($"cell {t.Cell}: its cube is not on the board");
            };
            session.CubeCollected += t => loadsSeen++;

            int frame = 0;
            for (; frame < 60 * 120 && driver.Loop.Outcome == AutoPlayOutcome.Playing; frame++)
            {
                // The speed button: 1x for the first part, then 2x, then 3x.
                if (frame == 600 || frame == 1200) scene.SessionControls.SpeedButton.onClick.Invoke();
                Frame(driver);
                foreach (TruckMotion t in session.Fleet.OnRoad)
                {
                    if (t.State == TruckState.Out && grid.CubeAt(t.Cell) == null) problems.Add($"cell {t.Cell}: gone before its truck arrived");
                    GameObject flying = fleet.LoadingCubeOf(t);
                    if (flying == null) continue;
                    // In the air between its cell and the bed, above the board, shrinking toward the bed cube.
                    if (!flying.activeSelf) problems.Add($"cell {t.Cell}: the loading cube is hidden");
                    if (flying.transform.position.y < scene.Grid.transform.position.y) problems.Add($"cell {t.Cell}: the loading cube is under the board");
                    if (t.LoadProgress >= TruckMotion.LandAt) problems.Add($"cell {t.Cell}: still flying after landing in the bed");
                    flightsChecked++;
                }
                if (problems.Count > 0) break;
                if (frame % 300 == 0) yield return null;
            }
            yield return null;

            Assert.That(problems, Is.Empty);
            Assert.That(driver.Loop.Outcome, Is.EqualTo(AutoPlayOutcome.Won), $"after {frame} frames");
            Assert.That(session.Game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(session.Speed, Is.EqualTo(3));
            Assert.That(scene.SessionControls.SpeedLabel.text, Is.EqualTo("3x"));
            Assert.That(sent.Count, Is.EqualTo(cubes), "one truck per cube");
            Assert.That(loadsSeen, Is.EqualTo(cubes));
            Assert.That(flightsChecked, Is.GreaterThan(cubes), "every cube was seen flying into its truck");
            Assert.That(grid.Field.VisibleCount, Is.Zero);
            Assert.That(grid.Pool.InUseCount, Is.Zero, "every cube back in the pool");
            Assert.That(fleet.ActiveCount, Is.Zero, "every truck home");
            Assert.That(fleet.LoadingCount, Is.Zero);
            Assert.That(fleet.Pool.InUseCount, Is.Zero);
            Assert.That(fleet.Pool.CreatedCount, Is.LessThan(cubes / 2), "trucks are reused");
        }

        [UnityTest]
        public IEnumerator ACubeFliesFromItsCellIntoTheBedAndTheCargoTakesOver()
        {
            GameSessionDriver driver = Compose();
            driver.AutoPlay = true;
            driver.Play(Strawberry());
            GameSession session = driver.Session;
            TruckMotion watched = null;
            session.CubeCollected += t => watched = watched ?? t;
            for (int f = 0; f < 3000 && watched == null; f++) Frame(driver);
            Assert.That(watched, Is.Not.Null, "a truck arrived");
            Assert.That(scene.Grid.CubeAt(watched.Cell), Is.Null, "the board no longer shows it");
            GameObject cube = scene.Trucks.LoadingCubeOf(watched);
            Assert.That(cube, Is.Not.Null, "the truck took it from the grid");
            TruckVisual truck = scene.Trucks.VisualOf(watched);
            float startDistance = Vector3.Distance(cube.transform.position, truck.Cargo.transform.position);
            float highest = cube.transform.position.y;
            Assert.That(truck.Cargo.gameObject.activeSelf, Is.False, "not in the bed yet");
            while (watched.State == TruckState.Load && watched.LoadProgress < TruckMotion.LandAt)
            {
                Frame(driver);
                if (scene.Trucks.LoadingCubeOf(watched) == null) break;
                highest = Mathf.Max(highest, cube.transform.position.y);
            }
            Assert.That(highest, Is.GreaterThan(truck.Cargo.transform.position.y), "it arcs up over the bed");
            Assert.That(startDistance, Is.GreaterThan(0.05f), "it started away from the bed");
            Frame(driver);
            Assert.That(scene.Trucks.LoadingCubeOf(watched), Is.Null, "landed: back in the grid's pool");
            Assert.That(cube.activeSelf, Is.False);
            Assert.That(truck.Cargo.gameObject.activeSelf, Is.True, "the truck's cargo shows instead");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSpeedButtonCyclesOneTwoThreeAndRestartStartsOver()
        {
            GameSessionDriver driver = Compose();
            driver.AutoPlay = true;
            LevelDefinition level = Strawberry();
            driver.Play(level);
            SessionControls controls = scene.SessionControls;
            Assert.That(controls.SpeedLabel.text, Is.EqualTo("1x"));
            var labels = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                controls.SpeedButton.onClick.Invoke();
                labels.Add(controls.SpeedLabel.text + "/" + driver.Session.Speed);
            }
            CollectionAssert.AreEqual(new[] { "2x/2", "3x/3", "1x/1" }, labels);

            // Mid-game: trucks out, cubes in the air; Restart puts everything back.
            for (int f = 0; f < 240; f++) Frame(driver);
            Assert.That(scene.Trucks.ActiveCount, Is.GreaterThan(0));
            GameSession before = driver.Session;
            controls.RestartButton.onClick.Invoke();
            scene.Grid.SyncNow();
            Assert.That(driver.Session, Is.Not.SameAs(before), "a new session");
            Assert.That(driver.Session.Speed, Is.EqualTo(1), "keeps the speed");
            Assert.That(scene.Trucks.ActiveCount, Is.Zero);
            Assert.That(scene.Trucks.LoadingCount, Is.Zero);
            Assert.That(scene.Trucks.Pool.InUseCount, Is.Zero);
            Assert.That(scene.Grid.Field.VisibleCount, Is.EqualTo(level.Picture.CubeCount), "every cube back on the board");
            Assert.That(scene.Grid.Pool.InUseCount, Is.EqualTo(level.Picture.CubeCount));

            // +1 Slot: once.
            Assert.That(controls.AddSlotButton.interactable, Is.True);
            controls.AddSlotButton.onClick.Invoke();
            yield return null; // SessionControls.Update greys it out
            Assert.That(driver.Session.SlotBoostUsed, Is.True);
            Assert.That(controls.AddSlotButton.interactable, Is.False);
        }
    }
}
