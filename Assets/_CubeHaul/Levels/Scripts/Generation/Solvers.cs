using System;
using System.Collections.Generic;
using System.Threading;
using CubeHaul.Domain;

namespace CubeHaul.Levels.Generation
{
    /// <summary>cube-hunt SolveResult.</summary>
    public readonly struct SolveResult : IEquatable<SolveResult>
    {
        public bool Solved { get; }
        public int Ticks { get; }
        public int Taps { get; }

        public SolveResult(bool solved, int ticks, int taps)
        {
            Solved = solved;
            Ticks = ticks;
            Taps = taps;
        }

        public bool Equals(SolveResult other) => Solved == other.Solved && Ticks == other.Ticks && Taps == other.Taps;
        public override bool Equals(object obj) => obj is SolveResult o && Equals(o);
        public override int GetHashCode() => (Solved ? 1 : 0) ^ (Ticks * 397) ^ (Taps * 7919);
        public override string ToString() => $"solved {Solved}, {Ticks} ticks, {Taps} taps";
    }

    /// <summary>
    /// cube-hunt solveGreedy (src/levels/solver.ts): plays a level with a simple greedy strategy. A win shows the level
    /// is winnable without boosters; a loss proves nothing (the generator just tries again).
    /// </summary>
    public static class GreedySolver
    {
        public const int DefaultMaxTicks = 200_000;

        public static SolveResult Solve(LevelDefinition level, int maxTicks = DefaultMaxTicks) => Solve(level.CreateGame(), maxTicks);

        public static SolveResult Solve(PictureDefinition picture, OpenEdges edges, int slotCount, TrayDefinition tray, int maxTicks = DefaultMaxTicks) =>
            Solve(GameFactory.CreateGame(picture, edges, slotCount, tray), maxTicks);

        /// <summary>Plays this fresh game to the end (or <paramref name="maxTicks"/> steps).</summary>
        public static SolveResult Solve(GameEngine g, int maxTicks = DefaultMaxTicks)
        {
            int ticks = 0, taps = 0;
            while (g.Status == GameStatus.Playing && ticks < maxTicks)
            {
                // Fill free slots with crates that can eat right away.
                while (g.State.HasFreeSlot())
                {
                    int col = ChooseTap(g.State, false);
                    if (col < 0) break;
                    g.Tap(col);
                    taps++;
                }
                if (g.Status != GameStatus.Playing) break;
                int sent = g.StepQuietly();
                ticks++;
                if (sent == 0 && g.Status == GameStatus.Playing)
                {
                    // Nothing is eating: something has to be tapped, even a blocked color.
                    int col = ChooseTap(g.State, true);
                    if (col < 0) break;
                    g.Tap(col);
                    taps++;
                }
            }
            return new SolveResult(g.Status == GameStatus.Won, ticks, taps);
        }

        /// <summary>
        /// The column to tap: the most exposed cubes per crate of that color already in a slot; when forced, plus a
        /// little for the cubes left (likely to open soon). Ties go to the first column (strict &gt;, as TypeScript).
        /// </summary>
        public static int ChooseTap(GameState g, bool force)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            Board board = g.Board;
            for (int c = 0; c < g.ColumnCount; c++)
            {
                Crate b = g.Front(c);
                if (b == null) continue;
                int reach = board.ReachableCount(b.Color);
                if (!force && reach == 0) continue;
                int sameColor = 0;
                for (int s = 0; s < g.SlotCount; s++)
                    if (g.SlotAt(s)?.Color == b.Color) sameColor++;
                double score = reach / (1.0 + sameColor) + (force ? board.RemainingOf(b.Color) * 1e-3 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// cube-hunt randomWinRate: the share of games won by a careless player who taps a random crate whenever a slot is
    /// free. 1 = cannot lose, 0 = needs real planning. Game k is seeded with <c>seed + 101k</c>.
    /// </summary>
    public static class RandomPlayer
    {
        public const int DefaultGames = 24;

        public static double WinRate(LevelDefinition level, int games = DefaultGames, long seed = 1, CancellationToken cancel = default) =>
            WinRate(level.Picture, level.Edges, level.SlotCount, level.Tray, games, seed, null, cancel);

        /// <param name="results">If given (length &gt;= games), receives each game's outcome.</param>
        public static double WinRate(PictureDefinition picture, OpenEdges edges, int slotCount, TrayDefinition tray, int games = DefaultGames, long seed = 1,
            bool[] results = null, CancellationToken cancel = default)
        {
            if (games <= 0) throw new ArgumentOutOfRangeException(nameof(games));
            List<IReadOnlyList<CrateSpec>> specs = GameFactory.Specs(tray);
            int wins = 0;
            for (int k = 0; k < games; k++)
            {
                cancel.ThrowIfCancellationRequested();
                var game = new GameEngine(picture.CreateBoard(edges), specs, slotCount);
                bool won = Play(game, new Mulberry32(seed + k * 101L));
                if (results != null) results[k] = won;
                if (won) wins++;
            }
            return (double)wins / games;
        }

        /// <summary>One careless game. True if won.</summary>
        public static bool Play(GameEngine g, Mulberry32 rng)
        {
            GameState s = g.State;
            var columns = new int[s.ColumnCount];
            while (g.Status == GameStatus.Playing)
            {
                int taps = 0;
                while (g.Status == GameStatus.Playing && s.HasFreeSlot())
                {
                    int n = 0;
                    for (int c = 0; c < s.ColumnCount; c++)
                        if (s.ColumnLength(c) > 0) columns[n++] = c;
                    if (n == 0) break;
                    g.Tap(columns[rng.NextIndex(n)]);
                    taps++;
                }
                if (g.Status != GameStatus.Playing) break;
                // TypeScript would loop forever here: no tap, nothing sent, so the next round is the same. Only a tray
                // whose truck counts do not match the picture can get here (I-01); the generator never makes one.
                if (g.StepQuietly() == 0 && taps == 0 && g.Status == GameStatus.Playing) break;
            }
            return g.Status == GameStatus.Won;
        }
    }
}
