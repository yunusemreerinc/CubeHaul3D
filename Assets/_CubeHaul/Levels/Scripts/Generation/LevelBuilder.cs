using System;
using System.Collections.Generic;
using System.Threading;
using CubeHaul.Domain;

namespace CubeHaul.Levels.Generation
{
    /// <summary>Which branch of buildLevel produced the tray.</summary>
    public enum BuildPath
    {
        /// <summary>One of the candidate seeds (seed + 7919k) that greedy play wins; the closest to the target win rate.</summary>
        Candidate,
        /// <summary>No candidate won: the first of seed + k, shuffle * (1 - k/8), k = 1..8, that greedy play wins.</summary>
        Fallback,
        /// <summary>
        /// Nothing won: the unshuffled tray, <b>not</b> verified as solvable (TypeScript only logs a console warning).
        /// </summary>
        Unverified,
    }

    /// <summary>One tray buildLevel generated, and how the solvers did on it.</summary>
    public sealed class BuildAttempt
    {
        public BuildPath Phase { get; internal set; }
        /// <summary>k of the candidate (0..) or fallback (1..8) loop; 0 for the unverified tray.</summary>
        public int K { get; internal set; }
        public long Seed { get; internal set; }
        public double Shuffle { get; internal set; }
        public TrayDefinition Tray { get; internal set; }
        public SolveResult Greedy { get; internal set; }
        /// <summary>Random-player game results, only for candidates the solver won (null otherwise).</summary>
        public bool[] Games { get; internal set; }
        /// <summary>Random-player win rate, only for candidates the solver won (null otherwise).</summary>
        public double? WinRate { get; internal set; }
    }

    /// <summary>What buildLevel returns, plus how it got there.</summary>
    public sealed class BuildResult
    {
        public TrayDefinition Tray { get; internal set; }
        /// <summary>Measured random-player win rate of <see cref="Tray"/>.</summary>
        public double WinRate { get; internal set; }
        public BuildPath Path { get; internal set; }
        /// <summary>Every tray generated, in order (the chosen one is <see cref="Chosen"/>).</summary>
        public IReadOnlyList<BuildAttempt> Attempts { get; internal set; }
        public int Chosen { get; internal set; }
        /// <summary>False only for <see cref="BuildPath.Unverified"/>.</summary>
        public bool Verified => Path != BuildPath.Unverified;
    }

    /// <summary>
    /// cube-hunt buildLevel: generates candidate trays, keeps the ones the greedy solver wins (so the level is winnable
    /// without boosters), and returns the one whose random-player win rate is closest to the target. Same options, same
    /// tray and win rate as TypeScript.
    ///
    /// It plays thousands of games (16 candidates x 25 games on a ~2000-cube picture), so do it ahead of time with the
    /// Editor's bake menu, or off the main thread (<see cref="LevelBaker.BakeAsync"/>); it can be cancelled.
    /// </summary>
    public static class LevelBuilder
    {
        public const int CandidateSeedStep = 7919;
        public const double TargetTolerance = 0.04;
        public const int FallbackSteps = 8;

        public static BuildResult Build(PictureDefinition picture, GenerationOptions options, CancellationToken cancel = default)
        {
            if (picture == null) throw new ArgumentNullException(nameof(picture));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Candidates < 0) throw new ArgumentOutOfRangeException(nameof(options.Candidates));
            double target = options.TargetWinRate;
            var attempts = new List<BuildAttempt>();
            int best = -1;
            double bestRate = 0;

            BuildAttempt Attempt(BuildPath phase, int k, long seed, double shuffle)
            {
                cancel.ThrowIfCancellationRequested();
                TrayDefinition tray = LevelGenerator.Generate(picture, options.With(seed, shuffle));
                var a = new BuildAttempt
                {
                    Phase = phase, K = k, Seed = seed, Shuffle = shuffle, Tray = tray,
                    Greedy = GreedySolver.Solve(picture, options.Edges, options.SlotCount, tray),
                };
                attempts.Add(a);
                return a;
            }

            double Rate(TrayDefinition tray, bool[] games) =>
                RandomPlayer.WinRate(picture, options.Edges, options.SlotCount, tray, RandomPlayer.DefaultGames, options.Seed, games, cancel);

            for (int k = 0; k < options.Candidates; k++)
            {
                BuildAttempt a = Attempt(BuildPath.Candidate, k, options.Seed + (long)k * CandidateSeedStep, options.Shuffle);
                if (!a.Greedy.Solved) continue;
                var games = new bool[RandomPlayer.DefaultGames];
                double rate = Rate(a.Tray, games);
                a.Games = games;
                a.WinRate = rate;
                if (best < 0 || Math.Abs(rate - target) < Math.Abs(bestRate - target))
                {
                    best = attempts.Count - 1;
                    bestRate = rate;
                }
                if (Math.Abs(rate - target) < TargetTolerance) break;
            }
            if (best >= 0) return Result(BuildPath.Candidate);

            // Nothing passed: back off the shuffle until the solver wins.
            for (int k = 1; k <= FallbackSteps; k++)
            {
                BuildAttempt a = Attempt(BuildPath.Fallback, k, options.Seed + k, options.Shuffle * (1 - k / (double)FallbackSteps));
                if (!a.Greedy.Solved) continue;
                best = attempts.Count - 1;
                bestRate = Rate(a.Tray, null);
                return Result(BuildPath.Fallback);
            }

            BuildAttempt last = Attempt(BuildPath.Unverified, 0, options.Seed, 0);
            best = attempts.Count - 1;
            bestRate = Rate(last.Tray, null);
            return Result(BuildPath.Unverified);

            BuildResult Result(BuildPath path) => new BuildResult
            {
                Tray = attempts[best].Tray, WinRate = bestRate, Path = path, Attempts = attempts, Chosen = best,
            };
        }
    }
}
