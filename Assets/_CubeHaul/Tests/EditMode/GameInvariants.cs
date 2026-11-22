using System.Collections.Generic;
using CubeHaul.Domain;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The invariants of docs/PORTING_PLAN.md §4 that can be read off a game, as Tools/ts-reference/tests/helpers.ts
    /// violations(). Returns the violated ones; empty means all hold. One pass over the cells, so cheap enough to run
    /// after every call.
    /// </summary>
    static class GameInvariants
    {
        public static List<string> Violations(GameEngine game)
        {
            var output = new List<string>();
            GameState g = game.State;
            Board b = g.Board;
            int n = b.CellCount, colors = b.ColorCount;
            var neighbors = new int[4];

            var onBoard = new int[colors];
            var claimedOf = new int[colors];
            var reachableOf = new int[colors];
            int remaining = 0, pending = 0;
            for (int i = 0; i < n; i++)
            {
                int c = b.Cell(i);
                if (b.IsClaimed(i))
                {
                    pending++;
                    if (c == Cells.Empty) output.Add($"I-03 claimed cell {i} is empty");
                }
                if (c == Cells.Empty)
                {
                    if (b.IsReachable(i)) output.Add($"I-06 empty cell {i} is reachable");
                    continue;
                }
                remaining++;
                if (b.IsOpen(i)) output.Add($"I-04 open cell {i} holds a cube");
                if (c < 0 || c >= colors)
                {
                    output.Add($"cell {i} has color {c} outside the palette");
                    continue;
                }
                onBoard[c]++;
                if (b.IsClaimed(i)) claimedOf[c]++;
                // I-06: reachable = unclaimed cubes on an open edge or next to an open cell.
                bool exposed = b.OpenSides(i) != SideMask.None;
                int k = b.Neighbors(i, neighbors);
                for (int m = 0; m < k && !exposed; m++) exposed = b.IsOpen(neighbors[m]);
                bool expected = exposed && !b.IsClaimed(i);
                if (expected) reachableOf[c]++;
                if (b.IsReachable(i) != expected) output.Add($"I-06 reachable[{i}] is {b.IsReachable(i)}");
            }
            if (remaining != b.Remaining) output.Add("I-02 remaining");
            if (pending != b.Pending) output.Add("I-02 pending");

            // I-04: open = empty cells joined to an open edge through empty cells.
            var seen = new bool[n];
            var stack = new Stack<int>();
            for (int i = 0; i < n; i++)
            {
                if (b.Cell(i) != Cells.Empty || b.OpenSides(i) == SideMask.None) continue;
                seen[i] = true;
                stack.Push(i);
            }
            while (stack.Count > 0)
            {
                int j = stack.Pop();
                int k = b.Neighbors(j, neighbors);
                for (int m = 0; m < k; m++)
                {
                    int x = neighbors[m];
                    if (seen[x] || b.Cell(x) != Cells.Empty) continue;
                    seen[x] = true;
                    stack.Push(x);
                }
            }
            for (int i = 0; i < n; i++)
                if (seen[i] != b.IsOpen(i)) output.Add($"I-04 open[{i}]");

            var trucks = new int[colors];
            for (int c = 0; c < g.ColumnCount; c++)
                for (int r = 0; r < g.ColumnLength(c); r++)
                {
                    Crate k = g.CrateAt(c, r);
                    trucks[k.Color] += k.Count;
                }
            for (int s = 0; s < g.SlotCount; s++)
            {
                Crate k = g.SlotAt(s);
                if (k == null) continue;
                trucks[k.Color] += k.Count;
                if (k.Count < 1) output.Add($"I-11 slot {s} with count {k.Count}");
            }
            for (int c = 0; c < colors; c++)
            {
                // I-01: trucks still to go + trucks on their way = cubes of that color on the board.
                if (trucks[c] + claimedOf[c] != onBoard[c]) output.Add($"I-01 color {c}: {trucks[c]} + {claimedOf[c]} != {onBoard[c]}");
                if (b.RemainingOf(c) != onBoard[c]) output.Add($"I-02 remainingOf({c})");
                if (b.ReachableCount(c) != reachableOf[c]) output.Add($"I-06 reachableCount({c})");
                // I-07: PickTarget is -1 exactly when nothing is reachable, else a reachable cube of that color.
                int t = b.PickTarget(c);
                if ((t < 0) != (reachableOf[c] == 0) || (t >= 0 && (!b.IsReachable(t) || b.Cell(t) != c))) output.Add($"I-07 pickTarget({c})");
            }

            bool anyCanEat = false;
            for (int s = 0; s < g.SlotCount; s++) anyCanEat |= g.SlotCanEat(s);
            if ((g.Status == GameStatus.Won) != (b.Remaining == 0)) output.Add("I-14 won <=> remaining = 0");
            if (g.Status == GameStatus.Stuck && (g.HasFreeSlot() || b.Pending > 0 || anyCanEat)) output.Add("I-14 stuck definition");
            // Liveness: while playing with no truck out, a slot can eat or a crate can be tapped.
            if (g.Status == GameStatus.Playing && b.Pending == 0 && !anyCanEat && !(g.HasFreeSlot() && g.TrayCount() > 0))
                output.Add("liveness: playing but no move and no truck out");
            return output;
        }
    }
}
