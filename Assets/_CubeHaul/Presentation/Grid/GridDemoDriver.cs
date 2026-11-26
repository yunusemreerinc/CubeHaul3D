using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;
using CubeHaul.Presentation.Trucks;
using UnityEngine;

namespace CubeHaul.Presentation.Grid
{
    /// <summary>
    /// A stand-in for the game loop until the real one exists (Tasks 12, 14): with <see cref="AutoPlay"/> on, Play mode
    /// plays the level with greedy taps. Each tick's trucks claim their cubes (which stay on the board); with a
    /// <see cref="TruckFleetView"/> every claim sends a truck along its planned route, and the cube is collected when the
    /// truck arrives, as in cube-hunt; without one, a moment later. Only for seeing the grid and the trucks follow the game.
    /// </summary>
    public sealed class GridDemoDriver : MonoBehaviour
    {
        [SerializeField] GridPresenter grid;
        [SerializeField] TruckFleetView trucks;
        [SerializeField] LevelLibrary library;
        [SerializeField, Min(0)] int levelIndex;
        [SerializeField, Tooltip("Play the level by itself in Play mode.")]
        bool autoPlay;
        [SerializeField, Min(0.02f), Tooltip("Seconds between ticks (cube-hunt sends one truck per crate every 0.15 s).")]
        float tickInterval = 0.15f;
        [SerializeField, Min(0f), Tooltip("Without trucks: seconds a claimed cube stays before it is collected.")]
        float collectDelay = 0.6f;

        readonly List<Dispatch> sent = new List<Dispatch>();
        readonly Queue<(int cell, float at)> timed = new Queue<(int, float)>();
        GameEngine game;
        LevelDefinition level;
        RoadPathPlanner roads;
        float nextTick;
        int out_;

        public GameEngine Game => game;

        public bool AutoPlay
        {
            get => autoPlay;
            set => autoPlay = value;
        }

        public void Configure(GridPresenter presenter, LevelLibrary levels, TruckFleetView fleet = null)
        {
            grid = presenter;
            library = levels;
            trucks = fleet;
        }

        void Start()
        {
            if (!autoPlay || grid == null || library == null) return;
            LevelRepository repo = library.Load();
            if (repo.HasErrors || repo.Count == 0) return;
            level = repo.GetAt(Mathf.Clamp(levelIndex, 0, repo.Count - 1));
            game = level.CreateGame();
            grid.Show(level.Picture);
            grid.Bind(game.Board);
            roads = RoadPathPlanner.ForBoard(game.Board);
            if (trucks != null)
            {
                trucks.Clear();
                trucks.Arrived += OnArrived;
            }
            nextTick = Time.time + 0.5f;
        }

        void OnDestroy()
        {
            if (trucks != null) trucks.Arrived -= OnArrived;
        }

        void OnArrived(Gameplay.Trucks.TruckMotion truck)
        {
            if (game == null) return;
            game.Collect(truck.Cell);
            out_--;
        }

        void Update()
        {
            if (game == null) return;
            while (timed.Count > 0 && timed.Peek().at <= Time.time)
            {
                game.Collect(timed.Dequeue().cell);
                out_--;
            }
            if (Time.time < nextTick) return;
            nextTick = Time.time + tickInterval;
            if (game.Status == GameStatus.Stuck && game.State.SlotCount < SimConstantsMaxSlots) game.AddSlot();
            if (game.Status != GameStatus.Playing) return;
            while (game.State.HasFreeSlot())
            {
                int col = GreedySolver.ChooseTap(game.State, false);
                if (col < 0 && out_ == 0) col = GreedySolver.ChooseTap(game.State, true);
                if (col < 0) break;
                game.Tap(col);
            }
            game.Tick(null, sent);
            foreach (Dispatch d in sent)
            {
                out_++;
                if (trucks != null)
                    trucks.Send(roads.PlanTruck(d.Cell), roads.Center(d.Cell), roads.CellSize, roads.Ring, grid.Field.Palette[d.Color]);
                else
                    timed.Enqueue((d.Cell, Time.time + collectDelay));
            }
        }

        const int SimConstantsMaxSlots = CubeHaul.Gameplay.SimConstants.MaxSlots;
    }
}
