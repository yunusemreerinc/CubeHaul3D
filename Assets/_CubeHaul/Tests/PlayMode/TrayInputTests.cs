using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Session;
using CubeHaul.Levels;
using CubeHaul.Presentation.Session;
using CubeHaul.UI;
using CubeHaul.UI.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 16 acceptance (docs/TRAY_UI.md): taps on the tray through the real Input System, the EventSystem and the
    /// UI module, with a virtual mouse and a virtual touchscreen. The same taps make the same moves; a tap on a full
    /// dock is refused, changes nothing in the game and shows the warning.
    /// </summary>
    public class TrayInputTests
    {
        GameSceneComposer.Composition scene;
        Mouse mouse;
        Touchscreen touchscreen;
        InputSettings.EditorInputBehaviorInPlayMode playModeInput;
        InputSettings.BackgroundBehavior background;

        [SetUp]
        public void SetUp()
        {
            // Test devices only, and let them reach the game view even when the editor window has no focus.
            playModeInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("Test Mouse");
            touchscreen = InputSystem.AddDevice<Touchscreen>("Test Touchscreen");
        }

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            scene = null;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (touchscreen != null) InputSystem.RemoveDevice(touchscreen);
            InputSystem.settings.editorInputBehaviorInPlayMode = playModeInput;
            InputSystem.settings.backgroundBehavior = background;
        }

        static LevelDefinition Strawberry() => LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors().Get("strawberry");

        /// <summary>The scene with the level loaded and the clock held: only taps change the game.</summary>
        IEnumerator Compose()
        {
            scene = GameSceneComposer.Compose();
            GameSessionDriver driver = scene.Session;
            driver.PlayOnStart = false;
            driver.AutoPlay = false;
            driver.enabled = false;
            driver.Play(Strawberry());
            yield return null;
            yield return null;
            Assert.That(scene.PlayArea.TapTargetCount, Is.EqualTo(5), "one tap target per tray column");
        }

        Vector2 ScreenCenter(int col)
        {
            var rt = (RectTransform)scene.PlayArea.TargetOf(col).transform;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        IEnumerator Click(Vector2 at)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
            yield return null;
            yield return null; // let the UI module finish with this pointer before the next gesture
        }

        int touchId = 1;

        IEnumerator Touch(Vector2 at)
        {
            int id = touchId++;
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Began, position = at });
            yield return null;
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = at });
            yield return null;
            yield return null; // an ended touch is dropped by the UI module a frame later
        }

        static string Snapshot(GameSession s)
        {
            GameState g = s.Game.State;
            var cells = new int[g.Board.CellCount];
            g.Board.CopyCells(cells);
            var cols = Enumerable.Range(0, g.ColumnCount).Select(c => string.Join(",", Enumerable.Range(0, g.ColumnLength(c)).Select(r => $"{g.CrateAt(c, r).Id}:{g.CrateAt(c, r).Count}")));
            var slots = Enumerable.Range(0, g.SlotCount).Select(i => g.SlotAt(i) == null ? "-" : $"{g.SlotAt(i).Id}:{g.SlotAt(i).Count}");
            return $"{g.Status}|{g.Board.Pending}|{string.Join("/", cols)}|{string.Join("/", slots)}|{string.Join(",", cells)}";
        }

        /// <summary>Plays the same seven taps (five fill the dock, two are refused) with one kind of pointer.</summary>
        IEnumerator PlaySequence(bool touch, List<string> moves, List<string> pointers, List<string> states)
        {
            GameSession s = scene.Session.Session;
            s.CrateTapped += (col, slot) => moves.Add($"tap {col} -> {slot}");
            s.TapRefused += col => moves.Add($"refused {col}");
            // The view outlives a restart: listen only while this sequence plays.
            System.Action<int, int, string> onTap = (col, slot, pointer) => pointers.Add(pointer);
            scene.PlayArea.ColumnTapped += onTap;
            foreach (int col in new[] { 0, 2, 4, 1, 3, 0, 2 })
            {
                Vector2 at = ScreenCenter(col);
                yield return touch ? Touch(at) : Click(at);
                states.Add(Snapshot(s));
            }
            scene.PlayArea.ColumnTapped -= onTap;
        }

        [UnityTest]
        public IEnumerator AMouseClickAndATouchMakeTheSameMove()
        {
            yield return Compose();
            var mouseMoves = new List<string>();
            var mousePointers = new List<string>();
            var mouseStates = new List<string>();
            yield return PlaySequence(false, mouseMoves, mousePointers, mouseStates);

            scene.Session.Restart();
            yield return null;
            yield return null;
            var touchMoves = new List<string>();
            var touchPointers = new List<string>();
            var touchStates = new List<string>();
            yield return PlaySequence(true, touchMoves, touchPointers, touchStates);

            CollectionAssert.AreEqual(new[] { "tap 0 -> 0", "tap 2 -> 1", "tap 4 -> 2", "tap 1 -> 3", "tap 3 -> 4", "refused 0", "refused 2" }, mouseMoves,
                "five crates to the first free slots, then the dock is full");
            CollectionAssert.AreEqual(mouseMoves, touchMoves, "a touch makes the same moves as a click");
            CollectionAssert.AreEqual(mouseStates, touchStates, "and leaves the game in the same state after every tap");
            Assert.That(mousePointers, Has.Count.EqualTo(7).And.All.EqualTo("MouseOrPen"), "the clicks came through the mouse");
            Assert.That(touchPointers, Has.Count.EqualTo(7).And.All.EqualTo("Touch"), "the touches through the touchscreen");
        }

        [UnityTest]
        public IEnumerator ATapOnAFullDockChangesNothingAndShowsAWarning()
        {
            yield return Compose();
            GameSession s = scene.Session.Session;
            for (int col = 0; col < 5; col++) yield return Click(ScreenCenter(col));
            Assert.That(s.Game.State.HasFreeSlot(), Is.False);
            string before = Snapshot(s);
            int refused = 0;
            s.TapRefused += _ => refused++;
            for (int col = 0; col < 5; col++)
            {
                yield return Click(ScreenCenter(col));
                yield return Touch(ScreenCenter(col));
            }
            Assert.That(refused, Is.EqualTo(10), "every tap was refused");
            Assert.That(Snapshot(s), Is.EqualTo(before), "no crate, count or slot changed");
            Assert.That(s.Game.State.TrayCount(), Is.EqualTo(Strawberry().Tray.Columns.Sum(c => c.Length) - 5));
            // The warning: the dock's rim turns red and the crates in it shake.
            yield return Click(ScreenCenter(1));
            Color rim = scene.PlayArea.DockEdge.color;
            Assert.That(rim.r, Is.GreaterThan(0.85f));
            Assert.That(rim.g, Is.LessThan(0.6f), "red, not the usual white rim");
            Assert.That(scene.PlayArea.Model.Shake, Is.GreaterThan(0), "the slots are shaking");
            for (int k = 0; k < 40; k++) yield return null;
            Assert.That(scene.PlayArea.DockEdge.color.g, Is.GreaterThan(0.9f), "the warning fades");
            Assert.That(Snapshot(s), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator OnlyTheFrontCrateTakesATapAndTheTrayShowsTheRules()
        {
            yield return Compose();
            GameSession s = scene.Session.Session;
            GameState g = s.Game.State;
            // A tap on the second row hits nothing; a tap between columns hits nothing.
            var second = scene.PlayArea.TrayCrate(0, 1);
            Vector2 back = RectTransformUtility.WorldToScreenPoint(null, second.TransformPoint(second.rect.center));
            yield return Click(back);
            Vector2 between = (ScreenCenter(0) + ScreenCenter(1)) / 2;
            yield return Touch(between);
            Assert.That(g.HasFreeSlot() && g.SlotAt(0) == null, Is.True, "nothing moved");
            // The front crate does; it flies to the first slot and the slot shows the rules' count.
            Crate front = g.Front(0);
            yield return Touch(ScreenCenter(0));
            Assert.That(g.SlotAt(0), Is.SameAs(front));
            // The front row's numbers are the rules' counts.
            yield return null;
            for (int col = 0; col < g.ColumnCount; col++)
            {
                Crate c = g.Front(col);
                var label = scene.PlayArea.TrayCrate(col, 0).GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (col == 0) continue; // still rising into place
                Assert.That(label.text, Is.EqualTo(c.Count.ToString()), $"column {col}");
            }
            // The tap target follows the front box.
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenCenter(3) }, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponent<ColumnTapTarget>()?.Column, Is.EqualTo(3), "the topmost hit is column 3's target");
        }
    }
}
