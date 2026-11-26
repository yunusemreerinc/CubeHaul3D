using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;
using UnityEngine;

namespace CubeHaul.Presentation.Grid
{
    /// <summary>
    /// A stand-in for the game loop until trucks exist (Task 12): with <see cref="AutoPlay"/> on, Play mode plays the
    /// level with greedy taps; each tick's trucks claim their cubes (which stay on the board) and collect them a moment
    /// later (when the cubes leave the board). Only for seeing the grid follow the game.
    /// </summary>
    public sealed class GridDemoDriver : MonoBehaviour
    {
        [SerializeField] GridPresenter grid;
        [SerializeField] LevelLibrary library;
        [SerializeField, Min(0)] int levelIndex;
        [SerializeField, Tooltip("Play the level by itself in Play mode.")]
        bool autoPlay;
        [SerializeField, Min(0.02f)] float tickInterval = 0.15f;
        [SerializeField, Min(0f), Tooltip("Seconds a claimed cube stays before it is collected.")]
        float collectDelay = 0.6f;

        readonly List<Dispatch> sent = new List<Dispatch>();
        readonly Queue<(int cell, float at)> out_ = new Queue<(int, float)>();
        GameEngine game;
        float nextTick;

        public GameEngine Game => game;

        public bool AutoPlay
        {
            get => autoPlay;
            set => autoPlay = value;
        }

        public void Configure(GridPresenter presenter, LevelLibrary levels)
        {
            grid = presenter;
            library = levels;
        }

        void Start()
        {
            if (!autoPlay || grid == null || library == null) return;
            LevelRepository repo = library.Load();
            if (repo.HasErrors || repo.Count == 0) return;
            LevelDefinition level = repo.GetAt(Mathf.Clamp(levelIndex, 0, repo.Count - 1));
            game = level.CreateGame();
            grid.Show(level.Picture);
            grid.Bind(game.Board);
            nextTick = Time.time + 0.5f;
        }

        void Update()
        {
            if (game == null) return;
            while (out_.Count > 0 && out_.Peek().at <= Time.time) game.Collect(out_.Dequeue().cell);
            if (game.Status != GameStatus.Playing || Time.time < nextTick) return;
            nextTick = Time.time + tickInterval;
            while (game.State.HasFreeSlot())
            {
                int col = GreedySolver.ChooseTap(game.State, false);
                if (col < 0 && out_.Count == 0) col = GreedySolver.ChooseTap(game.State, true);
                if (col < 0) break;
                game.Tap(col);
            }
            game.Tick(null, sent);
            foreach (Dispatch d in sent) out_.Enqueue((d.Cell, Time.time + collectDelay));
        }
    }
}
