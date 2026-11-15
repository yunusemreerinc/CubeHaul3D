using System;

namespace CubeHaul.Levels
{
    /// <summary>Board edges trucks can drive in from (cube-hunt OpenEdges). JSON: "bottom", "sides", "all".</summary>
    public enum EdgeMode
    {
        Bottom,
        Sides,
        All,
    }

    public enum DifficultyLabel
    {
        Easy,
        Medium,
        Hard,
        Expert,
    }

    /// <summary>
    /// A level in the CubeHaul3D LevelDefinition format, schema version 1 (docs/LEVEL_FORMAT.md).
    /// Built only from validated JSON by <see cref="LevelDefinitionReader"/>.
    /// </summary>
    public sealed class LevelDefinition
    {
        public const int CurrentSchemaVersion = 1;
        /// <summary>Cell value for an empty (transparent) cell.</summary>
        public const int Empty = -1;

        public int SchemaVersion { get; internal set; }
        public string LevelId { get; internal set; }
        public string ThemeId { get; internal set; }
        public string Name { get; internal set; }
        public PictureDefinition Picture { get; internal set; }
        public EdgeMode Edges { get; internal set; }
        public int SlotCount { get; internal set; }
        /// <summary>The exact tray to play, or null for a generation-only level.</summary>
        public TrayDefinition Tray { get; internal set; }
        /// <summary>How to generate a tray, or null for a static-only level.</summary>
        public GenerationSettings Generation { get; internal set; }
        /// <summary>Difficulty of the static tray, or null.</summary>
        public DifficultyMetadata Difficulty { get; internal set; }

        public bool HasStaticTray => Tray != null;
    }

    public sealed class PictureDefinition
    {
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        /// <summary>Row-major, Width * Height entries, row 0 at the top. <see cref="LevelDefinition.Empty"/> or a palette index.</summary>
        public int[] Cells { get; internal set; }
        /// <summary>"#rrggbb" colors (sRGB), indexed by cell values and crate colors.</summary>
        public string[] Palette { get; internal set; }

        public int CellAt(int x, int y) => Cells[y * Width + x];
    }

    public sealed class CrateDefinition
    {
        public int Id { get; internal set; }
        public int Color { get; internal set; }
        public int Count { get; internal set; }
    }

    public sealed class TrayDefinition
    {
        /// <summary>Columns[c][0] is the front (tappable) crate of column c.</summary>
        public CrateDefinition[][] Columns { get; internal set; }
    }

    /// <summary>cube-hunt BuildOptions. Optional fields left out of the JSON hold these defaults.</summary>
    public sealed class GenerationSettings
    {
        public const double DefaultTargetWinRate = 1;
        public const int DefaultColumns = 5;
        public const double DefaultBigChance = 0.15;
        public const int DefaultCandidates = 16;

        public int Seed { get; internal set; }
        public double Shuffle { get; internal set; }
        public double TargetWinRate { get; internal set; } = DefaultTargetWinRate;
        public int Columns { get; internal set; } = DefaultColumns;
        public double BigChance { get; internal set; } = DefaultBigChance;
        /// <summary>Null = derived from the picture size.</summary>
        public int? MinBlock { get; internal set; }
        /// <summary>Null = derived from the picture size.</summary>
        public int? MaxBlock { get; internal set; }
        public int Candidates { get; internal set; } = DefaultCandidates;
    }

    public sealed class GreedyResult
    {
        public bool Solved { get; internal set; }
        public int Ticks { get; internal set; }
        public int Taps { get; internal set; }
    }

    public sealed class DifficultyMetadata
    {
        public double WinRate { get; internal set; }
        public DifficultyLabel Label { get; internal set; }
        public int? RandomGames { get; internal set; }
        public GreedyResult Greedy { get; internal set; }
    }

    public static class DifficultyLabels
    {
        /// <summary>cube-hunt's difficulty chip: >= 0.9 Easy, >= 0.65 Medium, >= 0.45 Hard, else Expert.</summary>
        public static DifficultyLabel FromWinRate(double winRate)
        {
            if (winRate >= 0.9) return DifficultyLabel.Easy;
            if (winRate >= 0.65) return DifficultyLabel.Medium;
            if (winRate >= 0.45) return DifficultyLabel.Hard;
            return DifficultyLabel.Expert;
        }

        public static string ToJson(DifficultyLabel label) => label.ToString();

        /// <summary>Exact, case-sensitive and culture-independent.</summary>
        public static bool TryParse(string text, out DifficultyLabel label)
        {
            switch (text)
            {
                case "Easy": label = DifficultyLabel.Easy; return true;
                case "Medium": label = DifficultyLabel.Medium; return true;
                case "Hard": label = DifficultyLabel.Hard; return true;
                case "Expert": label = DifficultyLabel.Expert; return true;
                default: label = default; return false;
            }
        }
    }

    public static class EdgeModes
    {
        /// <summary>Exact, case-sensitive and culture-independent (no ToLower: "SIDES" is rejected, not lowered).</summary>
        public static bool TryParse(string text, out EdgeMode edges)
        {
            switch (text)
            {
                case "bottom": edges = EdgeMode.Bottom; return true;
                case "sides": edges = EdgeMode.Sides; return true;
                case "all": edges = EdgeMode.All; return true;
                default: edges = default; return false;
            }
        }

        public static string ToJson(EdgeMode edges)
        {
            switch (edges)
            {
                case EdgeMode.Bottom: return "bottom";
                case EdgeMode.Sides: return "sides";
                case EdgeMode.All: return "all";
                default: throw new ArgumentOutOfRangeException(nameof(edges));
            }
        }
    }
}
