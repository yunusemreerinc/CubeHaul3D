using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Levels;
using CubeHaul.Presentation.Grid;
using UnityEngine;

namespace CubeHaul.Presentation.Roads
{
    /// <summary>
    /// Draws truck routes as gizmos in the Scene view (and the Game view with Gizmos on), for debugging
    /// (docs/ROADS.md §5): the road ring, the three ramp lanes down to the tunnel, the open cells, and for the target
    /// cell (or every reachable cell) the route out, its waypoints, where it parks and the way home. The board is the
    /// game's (the GridPresenter's bound board) when there is one, else the library level peeled by
    /// <see cref="peelRounds"/>. Nothing here takes part in the game.
    /// </summary>
    [ExecuteAlways]
    public sealed class RoadDebugGizmos : MonoBehaviour
    {
        [SerializeField, Tooltip("Uses its bound board (the running game) when it has one.")]
        GridPresenter grid;
        [SerializeField] LevelLibrary library;
        [SerializeField, Min(0)] int levelIndex;
        [SerializeField, Tooltip("Without a running game: the edges to open (the level's own when Level).")]
        EdgeChoice edges = EdgeChoice.Level;
        [SerializeField, Min(0), Tooltip("Without a running game: eat every color's next cube this many times first.")]
        int peelRounds;
        [SerializeField, Tooltip("-1: every reachable cell.")]
        int targetCell = -1;
        [SerializeField] bool showHome = true;
        [SerializeField] bool showOpenCells = true;
        [SerializeField, Tooltip("Height of the drawn routes above the board, design units.")]
        float height = 3f;

        public enum EdgeChoice { Level, Bottom, Sides, All }

        readonly List<TruckRoute> plans = new List<TruckRoute>();
        Board board, ownBoard;
        RoadPathPlanner planner;
        BoardCoordinateMapper mapper;
        bool dirty = true;

        public IReadOnlyList<TruckRoute> Plans => plans;
        public RoadPathPlanner Planner => planner;
        public BoardCoordinateMapper Mapper => mapper;

        public int TargetCell
        {
            get => targetCell;
            set
            {
                targetCell = value;
                dirty = true;
            }
        }

        public void Configure(GridPresenter presenter, LevelLibrary levels)
        {
            grid = presenter;
            library = levels;
            dirty = true;
        }

        /// <summary>Routes on this board from now on (tests, tools), instead of the game's or the library's.</summary>
        public void ShowBoard(Board b)
        {
            ownBoard = b;
            dirty = true;
            Refresh();
        }

        void OnValidate() => dirty = true;

        /// <summary>Plans the routes for the current board and target(s).</summary>
        public void Refresh()
        {
            Board b = ownBoard ?? (grid != null ? grid.BoundBoard : null) ?? LibraryBoard();
            if (b == null)
            {
                plans.Clear();
                board = null;
                return;
            }
            if (b != board)
            {
                board = b;
                planner = RoadPathPlanner.ForBoard(b);
                mapper = new BoardCoordinateMapper(b.Width, b.Height, WorldMapper.UnitsPerDesign);
            }
            plans.Clear();
            if (targetCell >= 0)
            {
                if (targetCell < b.CellCount && b.Cell(targetCell) != Cells.Empty) plans.Add(planner.PlanTruck(targetCell));
            }
            else
            {
                for (int c = 0; c < b.ColorCount; c++)
                    foreach (int cell in b.ReachableCells(c)) plans.Add(planner.PlanTruck(cell));
            }
            dirty = false;
        }

        Board libraryBoard;
        int libraryKey = -1;

        Board LibraryBoard()
        {
            if (library == null) return null;
            int key = levelIndex * 10007 + peelRounds * 7 + (int)edges;
            if (libraryBoard != null && key == libraryKey && !dirty) return libraryBoard;
            LevelRepository repo = library.Load();
            if (repo.HasErrors || repo.Count == 0) return null;
            LevelDefinition level = repo.GetAt(Mathf.Clamp(levelIndex, 0, repo.Count - 1));
            OpenEdges e = edges == EdgeChoice.Level ? level.Edges : edges == EdgeChoice.Bottom ? OpenEdges.Bottom : edges == EdgeChoice.Sides ? OpenEdges.Sides : OpenEdges.All;
            PictureDefinition p = level.Picture;
            var b = new Board(p.Width, p.Height, p.Cells, p.Palette.Length, e);
            for (int r = 0; r < peelRounds; r++)
                for (int c = 0; c < b.ColorCount; c++)
                {
                    int t = b.PickTarget(c);
                    if (t >= 0) b.Eat(t);
                }
            libraryBoard = b;
            libraryKey = key;
            return b;
        }

        /// <summary>A design point at the gizmo height, in this object's world space.</summary>
        public Vector3 WorldPoint(PointD p, float designHeight) => transform.TransformPoint(WorldMapper.FromDesign(p.X, p.Y, designHeight));

        void OnDrawGizmos()
        {
            if (dirty || (grid != null && grid.BoundBoard != null && grid.BoundBoard != board) || board == null) Refresh();
            else if (targetCell < 0) Refresh(); // the game moves on: replan every repaint
            if (board == null) return;
            float h = height;
            RoadRing r = planner.Ring;

            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            var c0 = new PointD(r.X0, r.Y0);
            var c1 = new PointD(r.X1, r.Y0);
            var c2 = new PointD(r.X1, r.Y1);
            var c3 = new PointD(r.X0, r.Y1);
            Line(c0, c1, h);
            Line(c1, c2, h);
            Line(c2, c3, h);
            Line(c3, c0, h);

            Gizmos.color = Color.cyan;
            for (int k = 0; k < RoadLayout.LaneCount; k++)
            {
                Line(RoadLayout.TunnelStart(k), RoadLayout.LaneEntry(r, k), h);
                Gizmos.DrawSphere(WorldPoint(RoadLayout.TunnelStart(k), h), 0.06f);
            }

            float cell = (float)(mapper.CellSize * WorldMapper.UnitsPerDesign);
            if (showOpenCells)
            {
                Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                for (int i = 0; i < board.CellCount; i++)
                    if (board.IsOpen(i)) Gizmos.DrawWireCube(WorldPoint(mapper.CellCenter(i), 0.5f), new Vector3(cell * 0.8f, 0.01f, cell * 0.8f));
            }

            foreach (TruckRoute t in plans)
            {
                Gizmos.color = Color.green;
                Line(t.Start, t.Out[0], h);
                for (int k = 1; k < t.Out.Count; k++) Line(t.Out[k - 1], t.Out[k], h);
                foreach (PointD p in t.Out) Gizmos.DrawSphere(WorldPoint(p, h), cell * 0.12f);
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(WorldPoint(t.Parking, h), cell * 0.22f);
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireCube(WorldPoint(mapper.CellCenter(t.Target), (float)mapper.CellSize / 2), Vector3.one * cell);
                if (!showHome) continue;
                Gizmos.color = new Color(0.3f, 0.5f, 1f);
                float hh = h + 1.5f;
                for (int k = 1; k < t.Home.Count; k++) Line(t.Home[k - 1], t.Home[k], hh);
            }
        }

        void Line(PointD a, PointD b, float h) => Gizmos.DrawLine(WorldPoint(a, h), WorldPoint(b, h));
    }
}
