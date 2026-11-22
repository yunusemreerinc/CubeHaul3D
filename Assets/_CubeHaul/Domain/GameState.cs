using System;
using System.Collections.Generic;

namespace CubeHaul.Domain
{
    /// <summary>
    /// The state of a game and every question the rules ask about it (cube-hunt src/core/game.ts, the read-only half).
    /// Changed only by its <see cref="GameEngine"/>; everything here is a query.
    /// </summary>
    public sealed class GameState
    {
        internal readonly List<List<Crate>> columns;
        internal readonly List<Crate> slots;

        internal GameState(Board board, List<List<Crate>> columns, int slotCount)
        {
            Board = board;
            this.columns = columns;
            slots = new List<Crate>(Math.Max(slotCount, 6));
            for (int s = 0; s < slotCount; s++) slots.Add(null);
        }

        public Board Board { get; }
        public GameStatus Status { get; internal set; } = GameStatus.Playing;

        /// <summary>Tray columns.</summary>
        public int ColumnCount => columns.Count;

        /// <summary>Crates left in this column (0 for a column that does not exist).</summary>
        public int ColumnLength(int col) => col >= 0 && col < columns.Count ? columns[col].Count : 0;

        /// <summary>The crate at this depth of a column: row 0 is the front. Null if there is none.</summary>
        public Crate CrateAt(int col, int row) => col >= 0 && col < columns.Count && row >= 0 && row < columns[col].Count ? columns[col][row] : null;

        /// <summary>The front (tappable) crate of a column, or null (cube-hunt front()).</summary>
        public Crate Front(int col) => CrateAt(col, 0);

        public int SlotCount => slots.Count;

        /// <summary>The crate in this slot, or null for a free slot or a slot that does not exist.</summary>
        public Crate SlotAt(int slot) => slot >= 0 && slot < slots.Count ? slots[slot] : null;

        public bool HasFreeSlot() => slots.Contains(null);

        /// <summary>Lowest free slot index, or -1.</summary>
        public int FirstFreeSlot() => slots.IndexOf(null);

        public bool CanTap(int col) => Status == GameStatus.Playing && Front(col) != null && HasFreeSlot();

        /// <summary>Can the crate in this slot eat anything right now?</summary>
        public bool SlotCanEat(int slot)
        {
            Crate c = SlotAt(slot);
            return c != null && Board.ReachableCount(c.Color) > 0;
        }

        /// <summary>
        /// Is this slot's crate really blocked: nothing of its color reachable now, and nothing that the trucks already
        /// out will open up when they collect their cubes?
        /// </summary>
        public bool SlotBlocked(int slot)
        {
            Crate c = SlotAt(slot);
            return c != null && Board.ReachableCount(c.Color) == 0 && !Board.OpensUp(c.Color);
        }

        /// <summary>Stuck = every slot is taken, none of them can eat, and no truck is still on its way.</summary>
        public bool IsStuck()
        {
            if (HasFreeSlot() || Board.Pending > 0) return false;
            for (int s = 0; s < slots.Count; s++)
                if (SlotCanEat(s)) return false;
            return true;
        }

        /// <summary>Crates still in the tray.</summary>
        public int TrayCount()
        {
            int n = 0;
            foreach (List<Crate> col in columns) n += col.Count;
            return n;
        }
    }
}
