using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Levels;
using CubeHaul.Presentation;
using CubeHaul.Presentation.Grid;
using CubeHaul.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>
    /// Task 12 acceptance in Play Mode (docs/GRID_VIEW.md): the strawberry level drawn as 3D cubes in the game scene,
    /// rendered into a 1080 x 1920 texture. Every colored cell has a cube in its palette color at its cell's place, every
    /// empty cell has none, the rendered picture shows the right color at every cell, and the cubes follow the game by
    /// cell index: a claimed cube stays, a collected one goes back to the pool.
    /// </summary>
    public class GridPresenterTests
    {
        const int W = 1080, H = 1920;

        GameSceneComposer.Composition scene;
        RenderTexture target;
        LevelRepository repo;

        [SetUp]
        public void SetUp()
        {
            repo = LevelRepository.LoadDirectory("Assets/_CubeHaul/Levels/Content").ThrowIfErrors();
        }

        [TearDown]
        public void TearDown()
        {
            if (scene != null) Object.Destroy(scene.Root);
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
            scene = null;
            target = null;
        }

        IEnumerator Build(string levelId, bool unlit = false)
        {
            scene = GameSceneComposer.Compose();
            target = new RenderTexture(W, H, 24);
            target.Create();
            scene.Rig.FollowScreen = false;
            scene.SafeArea.FollowScreen = false;
            scene.Camera.targetTexture = target;
            scene.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            scene.Canvas.worldCamera = scene.Camera;
            scene.Canvas.planeDistance = 1f;
            scene.Rig.Apply(W, H, new Rect(0, 0, W, H));
            scene.SafeArea.Apply(new Vector2(W, H), new Rect(0, 0, W, H));
            scene.Grid.Unlit = unlit;
            scene.Grid.Show(repo.Get(levelId).Picture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryCellOfTheStrawberryHasItsCubeInItsColorAtItsPlace()
        {
            yield return Build("strawberry");
            PictureDefinition p = repo.Get("strawberry").Picture;
            GridPresenter grid = scene.Grid;
            Assert.That(grid.Field.VisibleCount, Is.EqualTo(527));
            Assert.That(grid.Pool.InUseCount, Is.EqualTo(527));
            Assert.That(GridPresenter.BoardTop, Is.EqualTo(LayoutPreview.BoardHeight), "the cubes stand on the board");
            var layout = scene.Rig.Layout;
            for (int i = 0; i < p.Cells.Length; i++)
            {
                GameObject cube = grid.CubeAt(i);
                if (p.Cells[i] < 0)
                {
                    Assert.That(cube, Is.Null, "empty cell " + i + " has no cube");
                    continue;
                }
                Assert.That(cube, Is.Not.Null, "cell " + i);
                Assert.That(cube.activeInHierarchy, Is.True);
                Assert.That(cube.name, Is.EqualTo("Cube " + i));
                Color32 expected = (Color32)Parse(p.Palette[p.Cells[i]]);
                Color32 got = cube.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.That((got.r, got.g, got.b), Is.EqualTo((expected.r, expected.g, expected.b)), "cell " + i + " color");
                // Its top face is drawn where the engine-free layout puts cube-hunt's cell center.
                (double x, double y) = grid.Field.DesignCenter(i);
                (double ex, double ey) = layout.DesignToScreen(x, y, GridPresenter.BoardTop + grid.CubeSize);
                Vector3 s = scene.Camera.WorldToScreenPoint(grid.TopCenter(i));
                Assert.That(s.x, Is.EqualTo((float)ex).Within(0.5f), "cell " + i + " x");
                Assert.That(s.y, Is.EqualTo((float)ey).Within(0.5f), "cell " + i + " y");
                Vector3 c = cube.GetComponent<MeshRenderer>().bounds.center;
                Assert.That(Vector3.Distance(new Vector3(c.x, 0, c.z), new Vector3(grid.TopCenter(i).x, 0, grid.TopCenter(i).z)), Is.LessThan(1e-4f));
            }
            // Column 0 is on the left, row 0 at the back (farthest from the camera, highest on screen).
            Vector3 a = scene.Camera.WorldToScreenPoint(grid.TopCenter(0)), right = scene.Camera.WorldToScreenPoint(grid.TopCenter(1)), front = scene.Camera.WorldToScreenPoint(grid.TopCenter(29));
            Assert.That(right.x, Is.GreaterThan(a.x));
            Assert.That(front.y, Is.LessThan(a.y));
            Assert.That(grid.GetComponentsInChildren<Collider>(true), Is.Empty, "no physics");
        }

        [UnityTest]
        public IEnumerator TheRenderedPictureIsTheStrawberry()
        {
            yield return Build("strawberry", unlit: true);
            yield return new WaitForEndOfFrame();
            var image = new Texture2D(W, H, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            PictureDefinition p = repo.Get("strawberry").Picture;
            Color32[] palette = p.Palette.Select(h => (Color32)Parse(h)).ToArray();
            GridPresenter grid = scene.Grid;
            var wrong = new List<string>();
            for (int i = 0; i < p.Cells.Length; i++)
            {
                bool cube = p.Cells[i] >= 0;
                (double x, double y) = grid.Field.DesignCenter(i);
                Vector3 at = cube ? grid.TopCenter(i) : WorldMapper.FromDesign(x, y, GridPresenter.BoardTop);
                Vector3 s = scene.Camera.WorldToScreenPoint(at);
                Color32 px = image.GetPixel(Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.y));
                int nearest = Nearest(palette, px, out int distance);
                if (cube && (nearest != p.Cells[i] || distance > 12)) wrong.Add($"cell {i}: expected {p.Palette[p.Cells[i]]}, got {Hex(px)}");
                if (!cube && distance <= 40) wrong.Add($"empty cell {i}: shows {Hex(px)}, close to {p.Palette[nearest]}");
            }
            Object.Destroy(image);
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong.Take(20)));
        }

        [UnityTest]
        public IEnumerator AClaimedCubeStaysAndACollectedCubeGoesBackToThePool()
        {
            yield return Build("strawberry");
            LevelDefinition level = repo.Get("strawberry");
            GameEngine game = level.CreateGame();
            GridPresenter grid = scene.Grid;
            grid.Bind(game.Board);
            for (int c = 0; c < game.State.ColumnCount && game.State.HasFreeSlot(); c++) game.Tap(c);
            List<Dispatch> sent = game.Tick();
            Assert.That(sent, Is.Not.Empty);
            yield return null; // the presenter follows the board in LateUpdate
            foreach (Dispatch d in sent)
            {
                Assert.That(grid.Field.StateOf(d.Cell), Is.EqualTo(CubeState.Claimed));
                Assert.That(grid.CubeAt(d.Cell) != null && grid.CubeAt(d.Cell).activeSelf, Is.True, "claimed cube " + d.Cell + " is still shown");
            }
            Assert.That(grid.Pool.InUseCount, Is.EqualTo(527));

            GameObject first = grid.CubeAt(sent[0].Cell);
            game.Collect(sent[0].Cell);
            yield return null;
            Assert.That(grid.CubeAt(sent[0].Cell), Is.Null, "collected cube is gone");
            Assert.That(first.activeSelf, Is.False, "and back in the pool");
            Assert.That((grid.Pool.InUseCount, grid.Pool.FreeCount), Is.EqualTo((526, 1)));
            Assert.That(grid.Field.VisibleCount, Is.EqualTo(game.Board.Remaining));

            foreach (Dispatch d in sent.Skip(1)) game.Collect(d.Cell);
            Assert.That(grid.SyncNow(), Is.EqualTo(sent.Count - 1));
            Assert.That(grid.Pool.InUseCount, Is.EqualTo(game.Board.Remaining));
        }

        [UnityTest]
        public IEnumerator ThePoolReusesCubesBetweenLevels()
        {
            yield return Build("watermelon"); // 1610 cubes
            GridPresenter grid = scene.Grid;
            int made = grid.Pool.CreatedCount;
            Assert.That(made, Is.EqualTo(1610));
            grid.Show(repo.Get("strawberry").Picture);
            grid.Show(repo.Get("cactus").Picture); // 1720 cubes: 110 more
            grid.Show(repo.Get("strawberry").Picture);
            yield return null;
            Assert.That(grid.Pool.CreatedCount, Is.EqualTo(1720), "only the difference to the largest level is ever created");
            Assert.That((grid.Pool.InUseCount, grid.Pool.FreeCount), Is.EqualTo((527, 1720 - 527)));
        }

        static Color Parse(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

        static string Hex(Color32 c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}";

        static int Nearest(Color32[] palette, Color32 px, out int distance)
        {
            int best = -1;
            distance = int.MaxValue;
            for (int k = 0; k < palette.Length; k++)
            {
                int d = Mathf.Max(Mathf.Abs(palette[k].r - px.r), Mathf.Abs(palette[k].g - px.g), Mathf.Abs(palette[k].b - px.b));
                if (d < distance)
                {
                    distance = d;
                    best = k;
                }
            }
            return best;
        }
    }
}
