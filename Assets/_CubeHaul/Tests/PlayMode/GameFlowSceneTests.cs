using System.Collections;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Flow;
using CubeHaul.Levels;
using CubeHaul.Presentation.Session;
using CubeHaul.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 17 acceptance (docs/GAME_FLOW.md): in the game scene, the first three levels are played one after another
    /// through the start screen and the won panels; the won panel opens only once every truck is home; a stuck level
    /// shows Out of moves with +1 Slot and Retry, and both work.
    /// </summary>
    public class GameFlowSceneTests
    {
        const float Dt = 1 / 60f;

        GameSceneComposer.Composition scene;

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            scene = null;
        }

        static LevelRepository Levels() => LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors();

        /// <summary>The scene with its loop driven frame by frame here.</summary>
        GameSessionDriver Compose()
        {
            scene = GameSceneComposer.Compose();
            GameSessionDriver driver = scene.Session;
            driver.PlayOnStart = false;
            driver.enabled = false;
            scene.Trucks.AutoStep = false;
            driver.UseLevels(Levels());
            return driver;
        }

        /// <summary>A two-cube column whose first crate wants the hidden top cube: stuck at once with one slot.</summary>
        static LevelDefinition Jam() => new LevelDefinition
        {
            LevelId = "jam",
            Name = "Jam",
            Picture = new PictureDefinition(1, 2, new[] { 0, 1 }, new[] { "#e8424f", "#5cc464" }),
            Edges = OpenEdges.Bottom,
            SlotCount = 1,
            Tray = new TrayDefinition(new[] { new[] { new BlockDefinition(0, 0, 1) }, new[] { new BlockDefinition(1, 1, 1) } }),
        };

        [UnityTest]
        public IEnumerator TheFirstThreeLevelsPlayOneAfterAnother()
        {
            GameSessionDriver driver = Compose();
            driver.AutoPlay = true;
            driver.SetSpeed(3);
            Assert.That(driver.LoadLevel(0, showStart: true), Is.True);
            yield return null;
            GameFlowView flow = scene.Flow;
            Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Start));
            Assert.That(flow.StartPanel.activeSelf && flow.Blocker.activeSelf, Is.True, "the start screen, over a dimmed game");
            Assert.That(flow.StartTitle.text, Is.EqualTo("1/10 · Strawberry"));
            Assert.That(scene.SessionControls.Title.text, Is.EqualTo("1/10 · Strawberry"));
            for (int k = 0; k < 30; k++) driver.Frame(Dt);
            Assert.That(driver.Session.Game.State.TrayCount(), Is.EqualTo(45), "nothing moves before Play");
            flow.PlayButton.onClick.Invoke();
            yield return null;
            Assert.That(flow.StartPanel.activeSelf || flow.Blocker.activeSelf, Is.False);

            string[] titles = { "1/10 · Strawberry", "2/10 · Mushroom", "3/10 · Sunset" };
            for (int level = 0; level < 3; level++)
            {
                Assert.That(scene.SessionControls.Title.text, Is.EqualTo(titles[level]));
                bool wonSeen = false, trucksWhenWon = false;
                int frames = 0;
                while (driver.Screen == FlowScreen.Playing && frames++ < 12_000)
                {
                    driver.Frame(Dt);
                    if (driver.Session.Game.Status == GameStatus.Won && !wonSeen)
                    {
                        wonSeen = true;
                        trucksWhenWon = scene.Trucks.ActiveCount > 0;
                    }
                    if (frames % 600 == 0) yield return null;
                }
                Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Won), titles[level]);
                Assert.That(trucksWhenWon, Is.True, $"{titles[level]}: trucks were still out when the last cube was collected");
                Assert.That((scene.Trucks.ActiveCount, driver.Session.TrucksOut), Is.EqualTo((0, 0)), $"{titles[level]}: every truck home before the panel");
                yield return null;
                Assert.That(flow.WonPanel.activeSelf && flow.Blocker.activeSelf, Is.True, "Picture complete!");
                Assert.That(flow.NextLabel.text, Is.EqualTo("Next level"));
                Assert.That(scene.SessionControls.AddSlotButton.interactable, Is.False, "no booster after a win");
                flow.NextButton.onClick.Invoke();
                yield return null;
                Assert.That(flow.WonPanel.activeSelf, Is.False);
                Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Playing), "the next level starts at once");
            }
            Assert.That(driver.PlayingIndex, Is.EqualTo(3));
            Assert.That(scene.SessionControls.Title.text, Is.EqualTo("4/10 · Ringed Planet"));
            Assert.That(scene.Grid.Field.Width, Is.EqualTo(driver.Level.Picture.Width), "the board shows the new level");
        }

        [UnityTest]
        public IEnumerator AStuckLevelOffersTheBoosterAndRetryAndBothWork()
        {
            GameSessionDriver driver = Compose();
            driver.Play(Jam());
            yield return null;
            GameFlowView flow = scene.Flow;
            Assert.That(scene.SessionControls.Title.text, Is.EqualTo("Jam"));

            // Stuck: the panel waits for the crate to land, then 0.4 s.
            driver.Tap(0);
            int frames = 0;
            while (driver.Screen == FlowScreen.Playing && frames++ < 600) driver.Frame(Dt);
            Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Stuck));
            // The crate lands in frame 16; the 0.4 s (24 frames) count from that frame on: the panel opens in frame 39.
            Assert.That(frames, Is.EqualTo(16 + 24 - 1), "0.26 s of flight, then 0.4 s");
            yield return null;
            Assert.That(flow.StuckPanel.activeSelf && flow.Blocker.activeSelf, Is.True, "Out of moves");
            Assert.That(flow.AddSlotButton.gameObject.activeSelf && flow.RetryButton.gameObject.activeSelf, Is.True, "+1 Slot and Retry");

            // Retry: the level again, nothing kept.
            flow.RetryButton.onClick.Invoke();
            yield return null;
            Assert.That(flow.StuckPanel.activeSelf || flow.Blocker.activeSelf, Is.False);
            Assert.That(driver.Session.Game.State.SlotCount, Is.EqualTo(1));
            Assert.That(driver.Session.Game.State.TrayCount(), Is.EqualTo(2));
            Assert.That(driver.Session.Game.Status, Is.EqualTo(GameStatus.Playing));

            // Stuck again; this time +1 Slot (free): the panel closes and the level can be won.
            driver.Tap(0);
            frames = 0;
            while (driver.Screen == FlowScreen.Playing && frames++ < 600) driver.Frame(Dt);
            yield return null;
            Assert.That(flow.StuckPanel.activeSelf, Is.True);
            flow.AddSlotButton.onClick.Invoke();
            driver.Frame(Dt);
            yield return null;
            Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(flow.StuckPanel.activeSelf, Is.False);
            Assert.That(driver.Session.Game.State.SlotCount, Is.EqualTo(2), "GameEngine.AddSlot");
            Assert.That(scene.SessionControls.AddSlotButton.interactable, Is.False, "spent for this level");
            Assert.That(scene.SessionControls.AddSlotButton.targetGraphic.color, Is.EqualTo(SessionControls.SlotSpent));
            Assert.That(driver.Tap(1), Is.EqualTo(1));
            frames = 0;
            while (driver.Screen == FlowScreen.Playing && frames++ < 3000) driver.Frame(Dt);
            yield return null;
            Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Won));
            Assert.That(flow.WonPanel.activeSelf, Is.True);
            Assert.That(scene.Trucks.ActiveCount, Is.Zero);

            // "Next level" for a level from outside the library plays it again, with the booster back.
            flow.NextButton.onClick.Invoke();
            yield return null;
            Assert.That(flow.WonPanel.activeSelf, Is.False);
            Assert.That((driver.Session.SlotBoostUsed, driver.Session.CanAddSlot, driver.Session.Game.State.SlotCount), Is.EqualTo((false, true, 1)));
            Assert.That(scene.SessionControls.AddSlotButton.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator TheTopBarGoesRoundTheLevels()
        {
            GameSessionDriver driver = Compose();
            driver.LoadLevel(0);
            yield return null;
            SessionControls bar = scene.SessionControls;
            bar.PreviousButton.onClick.Invoke();
            Assert.That(bar.Title.text, Is.EqualTo("10/10 · Watermelon"), "before the first, the last");
            Assert.That(driver.IsLastLevel, Is.True);
            bar.NextButton.onClick.Invoke();
            Assert.That(bar.Title.text, Is.EqualTo("1/10 · Strawberry"), "after the last, the first");
            Assert.That(bar.DifficultyLabel.text, Is.EqualTo(DifficultyLabels.ToJson(driver.Level.DifficultyMetadata.Label)));
            bar.NextButton.onClick.Invoke();
            Assert.That(bar.Title.text, Is.EqualTo("2/10 · Mushroom"));
            // Restart mid-level: the same level from the beginning.
            driver.AutoPlay = true;
            for (int k = 0; k < 300; k++) driver.Frame(Dt);
            Assert.That(driver.Session.Game.Board.Remaining, Is.LessThan(driver.Level.Picture.CubeCount));
            bar.RestartButton.onClick.Invoke();
            Assert.That(driver.Session.Game.Board.Remaining, Is.EqualTo(driver.Level.Picture.CubeCount));
            Assert.That(bar.Title.text, Is.EqualTo("2/10 · Mushroom"));
            Assert.That(driver.Screen, Is.EqualTo(FlowScreen.Playing));
        }
    }
}
