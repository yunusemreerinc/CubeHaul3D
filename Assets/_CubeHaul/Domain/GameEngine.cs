using System;
using System.Collections.Generic;

namespace CubeHaul.Domain
{
    /// <summary>
    /// Game rules, independent of rendering and timing: a port of cube-hunt src/core/game.ts @ 559dfd9.
    ///
    /// The view calls <see cref="Tap"/> on player input and <see cref="Tick"/> on a timer. Each tick every slot sends at
    /// most one truck, which <b>claims</b> one reachable cube of its color. The cube stays on the board, still blocking
    /// the cubes behind it, until the view calls <see cref="Collect"/> when the truck actually picks it up.
    /// <see cref="Step"/> does both at once (solver, tests). Status is updated after every change: Won when the last cube
    /// is collected, Stuck when nothing can move; Stuck becomes Playing again only through <see cref="AddSlot"/>.
    ///
    /// Kept exactly as in TypeScript (docs/TS_BASELINE.md §4): a level with no cubes starts as Playing until the first
    /// change (F-2); Collect accepts a reachable cube no truck was sent for (F-3); AddSlot has no limit (the once per
    /// level, at most 6 rule belongs to the session, B-30).
    /// </summary>
    public sealed class GameEngine
    {
        static readonly Func<int, bool> AllReady = _ => true;

        readonly List<Dispatch> stepBuffer = new List<Dispatch>();

        /// <param name="board">A fresh board for the level; the engine owns it from now on.</param>
        /// <param name="columns">The tray, front crate first in each column; copied.</param>
        /// <param name="slotCount">Free slots at the start.</param>
        public GameEngine(Board board, IReadOnlyList<IReadOnlyList<CrateSpec>> columns, int slotCount)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (columns == null) throw new ArgumentNullException(nameof(columns));
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            if (board.Pending != 0) throw new ArgumentException("the board already has trucks out; pass a fresh board", nameof(board));
            var cols = new List<List<Crate>>(columns.Count);
            foreach (IReadOnlyList<CrateSpec> column in columns)
            {
                if (column == null) throw new ArgumentException("a tray column is null", nameof(columns));
                var col = new List<Crate>(column.Count);
                foreach (CrateSpec spec in column) col.Add(new Crate(spec));
                cols.Add(col);
            }
            State = new GameState(board, cols, slotCount);
        }

        public GameState State { get; }
        public Board Board => State.Board;
        public GameStatus Status => State.Status;

        /// <summary>Moves the front crate of a column into the first free slot. Returns the slot index, or -1.</summary>
        public int Tap(int col)
        {
            if (!State.CanTap(col)) return -1;
            int slot = State.FirstFreeSlot();
            List<Crate> column = State.columns[col];
            State.slots[slot] = column[0];
            column.RemoveAt(0);
            UpdateStatus();
            return slot;
        }

        /// <summary>One more slot. Un-sticks the game if it was stuck.</summary>
        public void AddSlot()
        {
            State.slots.Add(null);
            if (State.Status == GameStatus.Stuck) State.Status = GameStatus.Playing;
        }

        /// <summary>
        /// Every slot whose crate can go (and for which <paramref name="ready"/> says yes; it is asked only for filled
        /// slots, in slot order) sends one truck: the cube it will haul is claimed, not removed. Writes the trucks sent
        /// to <paramref name="output"/> (cleared first) and returns how many. Nothing is sent unless the game is Playing.
        /// Pass a cached delegate (or null for "every slot") to keep it allocation-free.
        /// </summary>
        public int Tick(Func<int, bool> ready, List<Dispatch> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (State.Status != GameStatus.Playing) return 0;
            ready = ready ?? AllReady;
            List<Crate> slots = State.slots;
            for (int s = 0; s < slots.Count; s++)
            {
                Crate crate = slots[s];
                if (crate == null || !ready(s)) continue;
                int cell = Board.PickTarget(crate.Color);
                if (cell < 0) continue;
                Board.Claim(cell);
                crate.Count--;
                output.Add(new Dispatch(s, cell, crate.Color));
                if (crate.Count == 0) slots[s] = null;
            }
            UpdateStatus();
            return output.Count;
        }

        /// <summary>Tick with every slot ready; returns a new list.</summary>
        public List<Dispatch> Tick() => Tick((Func<int, bool>)null);

        /// <summary>Tick; returns a new list.</summary>
        public List<Dispatch> Tick(Func<int, bool> ready)
        {
            var output = new List<Dispatch>();
            Tick(ready, output);
            return output;
        }

        /// <summary>A truck has picked up its claimed cube: it leaves the board and opens the space behind it.</summary>
        public void Collect(int cell)
        {
            Board.Eat(cell);
            UpdateStatus();
        }

        /// <summary>One tick with every truck picking up its cube at once. Used by the solver and tests.</summary>
        public int Step(List<Dispatch> output)
        {
            Tick(null, output);
            for (int k = 0; k < output.Count; k++) Board.Eat(output[k].Cell);
            UpdateStatus();
            return output.Count;
        }

        /// <summary>Step; returns a new list.</summary>
        public List<Dispatch> Step()
        {
            var output = new List<Dispatch>();
            Step(output);
            return output;
        }

        /// <summary>Step without keeping the trucks sent; allocation-free.</summary>
        public int StepQuietly() => Step(stepBuffer);

        void UpdateStatus()
        {
            if (Board.Remaining == 0) State.Status = GameStatus.Won;
            else if (State.IsStuck()) State.Status = GameStatus.Stuck;
        }
    }
}
