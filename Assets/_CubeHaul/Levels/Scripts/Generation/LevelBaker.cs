using System;
using System.Threading;
using System.Threading.Tasks;

namespace CubeHaul.Levels.Generation
{
    /// <summary>A level with its generated tray, and how the tray was built.</summary>
    public sealed class BakeResult
    {
        /// <summary>A copy of the input level with <c>tray</c> and <c>difficultyMetadata</c> filled in.</summary>
        public LevelDefinition Level { get; internal set; }
        public BuildResult Build { get; internal set; }
        /// <summary>False if no tray could be verified as solvable (<see cref="BuildPath.Unverified"/>).</summary>
        public bool Verified => Build.Verified;
    }

    /// <summary>What baking one level file would do.</summary>
    public sealed class BakeFileResult
    {
        /// <summary>The file could not be read; see <see cref="Read"/>.Errors.</summary>
        public bool ReadFailed => !Read.Ok;
        public LevelReadResult Read { get; internal set; }
        /// <summary>False if the level has no generation settings (nothing to bake; the file is left as it is).</summary>
        public bool HasGeneration { get; internal set; }
        /// <summary>The baked file text (canonical format), or null when there is nothing to bake.</summary>
        public string Text { get; internal set; }
        /// <summary>True if <see cref="Text"/> differs from the file: its tray or difficulty is not what its settings give.</summary>
        public bool Changed { get; internal set; }
        public BakeResult Bake { get; internal set; }
    }

    /// <summary>
    /// Turns a level's generation settings into a static tray and difficulty: exactly what the TypeScript export wrote
    /// into the ten level files (Tools/ts-reference/scripts/level-examples.ts builtInDefinition). Used by the Editor's
    /// bake menu (ahead of time), and at runtime for a level that ships without a tray.
    /// </summary>
    public static class LevelBaker
    {
        public static BakeResult Bake(LevelDefinition level, CancellationToken cancel = default)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            GenerationOptions options = GenerationOptions.For(level);
            BuildResult build = LevelBuilder.Build(level.Picture, options, cancel);
            SolveResult greedy = GreedySolver.Solve(level.Picture, level.Edges, level.SlotCount, build.Tray);
            LevelDefinition baked = level.DeepCopy();
            baked.Tray = build.Tray.DeepCopy();
            baked.DifficultyMetadata = new DifficultyMetadata
            {
                WinRate = build.WinRate,
                Label = DifficultyLabels.FromWinRate(build.WinRate),
                RandomGames = RandomPlayer.DefaultGames,
                Greedy = new GreedyResult { Solved = greedy.Solved, Ticks = greedy.Ticks, Taps = greedy.Taps },
            };
            return new BakeResult { Level = baked, Build = build };
        }

        /// <summary>
        /// Bakes a level file's text: the Editor's bake and verify menus. The result is the canonical file text
        /// (<see cref="LevelDefinitionWriter.ToJson"/>), so an up-to-date file is left byte for byte unchanged.
        /// </summary>
        public static BakeFileResult BakeFile(string text, CancellationToken cancel = default)
        {
            var result = new BakeFileResult { Read = LevelDefinitionReader.Read(text) };
            if (!result.Read.Ok) return result;
            LevelDefinition level = result.Read.Level;
            result.HasGeneration = level.Generation != null;
            if (!result.HasGeneration) return result;
            result.Bake = Bake(level, cancel);
            result.Text = LevelDefinitionWriter.ToJson(result.Bake.Level);
            result.Changed = result.Text != text;
            return result;
        }

        /// <summary>
        /// The level as given if it has a static tray; otherwise baked on a worker thread (cube-hunt builds levels in a
        /// Web Worker for the same reason: about a second on a phone).
        /// </summary>
        public static async Task<LevelDefinition> EnsureTrayAsync(LevelDefinition level, CancellationToken cancel = default)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (level.Tray != null) return level;
            BakeResult baked = await BakeAsync(level, cancel).ConfigureAwait(false);
            return baked.Level;
        }

        /// <summary><see cref="Bake"/> on a worker thread.</summary>
        public static Task<BakeResult> BakeAsync(LevelDefinition level, CancellationToken cancel = default)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            LevelDefinition copy = level.DeepCopy(); // the caller may change its level meanwhile
            return Task.Run(() => Bake(copy, cancel), cancel);
        }
    }
}
