using System;
using CubeHaul.Domain;
using CubeHaul.Levels.Json;

namespace CubeHaul.Levels
{
    // C# counterparts of cube-hunt src/core/types.ts, in the LevelDefinition v1 format (docs/LEVEL_FORMAT.md):
    //
    //   TypeScript                 C#
    //   ColorId (number)           int (palette index)
    //   EMPTY = -1                 Cells.Empty (Domain) == LevelDefinition.Empty
    //   OpenEdges                  OpenEdges (Domain)
    //   Picture                    PictureDefinition (+ LevelDefinition.Name)
    //   Block                      BlockDefinition
    //   Level                      LevelDefinition (Picture, Tray.Columns, SlotCount, Edges)
    //   cloneLevel(level)          level.DeepCopy()
    //
    // Like the TypeScript objects these are plain, mutable data. Anything that changes a level (a game spending
    // trucks, an editor tool) works on its own DeepCopy(); LevelRepository only ever hands out copies, so the loaded
    // levels cannot be changed by mistake. Build them from JSON with LevelDefinitionReader, check hand-built ones
    // with LevelValidator.Validate(level), write them with LevelDefinitionWriter.

    /// <summary>A level: picture, board rules, and a static tray and/or the settings to generate one.</summary>
    public sealed class LevelDefinition
    {
        public const int CurrentSchemaVersion = 1;
        /// <summary>Cell value for an empty (transparent) cell; the same as <see cref="Cells.Empty"/>.</summary>
        public const int Empty = Cells.Empty;

        /// <summary>"$schema": where editors find the JSON Schema; the game ignores it. Null if absent.</summary>
        public string SchemaUri { get; set; }
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string LevelId { get; set; }
        public string ThemeId { get; set; }
        public string Name { get; set; }
        public PictureDefinition Picture { get; set; }
        public OpenEdges Edges { get; set; }
        public int SlotCount { get; set; }
        /// <summary>The exact tray to play, or null for a generation-only level.</summary>
        public TrayDefinition Tray { get; set; }
        /// <summary>How to generate a tray, or null for a static-only level.</summary>
        public GenerationSettings Generation { get; set; }
        /// <summary>Difficulty of the static tray, or null.</summary>
        public DifficultyMetadata DifficultyMetadata { get; set; }
        /// <summary>"extensions": free-form tool data, kept as read (JsonValue is immutable). Null if absent.</summary>
        public JsonValue Extensions { get; set; }

        public bool HasStaticTray => Tray != null;

        /// <summary>A fully independent copy: changing it (or the original) never affects the other.</summary>
        public LevelDefinition DeepCopy()
        {
            return new LevelDefinition
            {
                SchemaUri = SchemaUri,
                SchemaVersion = SchemaVersion,
                LevelId = LevelId,
                ThemeId = ThemeId,
                Name = Name,
                Picture = Picture?.DeepCopy(),
                Edges = Edges,
                SlotCount = SlotCount,
                Tray = Tray?.DeepCopy(),
                Generation = Generation?.DeepCopy(),
                DifficultyMetadata = DifficultyMetadata?.DeepCopy(),
                Extensions = Extensions, // immutable, safe to share
            };
        }

        /// <summary>True if both hold the same data (compared as JSON).</summary>
        public bool ContentEquals(LevelDefinition other)
        {
            return other != null && LevelDefinitionWriter.ToJsonValue(this).DeepEquals(LevelDefinitionWriter.ToJsonValue(other));
        }
    }

    /// <summary>The picture (cube-hunt Picture): row-major cells, row 0 at the top (farthest from the ramp).</summary>
    public sealed class PictureDefinition
    {
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>Width * Height entries: <see cref="LevelDefinition.Empty"/> or a palette index.</summary>
        public int[] Cells { get; set; }
        /// <summary>"#rrggbb" colors (sRGB), indexed by cell values and block colors.</summary>
        public string[] Palette { get; set; }

        public PictureDefinition() { }

        /// <summary>Copies the arrays, so the caller's arrays stay its own.</summary>
        public PictureDefinition(int width, int height, int[] cells, string[] palette)
        {
            Width = width;
            Height = height;
            Cells = (int[])cells?.Clone();
            Palette = (string[])palette?.Clone();
        }

        public int CellAt(int x, int y) => Cells[y * Width + x];

        /// <summary>How many cubes of this color the picture has.</summary>
        public int CountOf(int color)
        {
            int n = 0;
            foreach (int c in Cells)
                if (c == color) n++;
            return n;
        }

        /// <summary>How many cubes the picture has in all.</summary>
        public int CubeCount
        {
            get
            {
                int n = 0;
                foreach (int c in Cells)
                    if (c != LevelDefinition.Empty) n++;
                return n;
            }
        }

        public PictureDefinition DeepCopy() => new PictureDefinition(Width, Height, Cells, Palette);
    }

    /// <summary>A tray block (cube-hunt Block): Count trucks of one color, each hauling away one cube.</summary>
    public sealed class BlockDefinition
    {
        public int Id { get; set; }
        /// <summary>Palette index.</summary>
        public int Color { get; set; }
        public int Count { get; set; }

        public BlockDefinition() { }

        public BlockDefinition(int id, int color, int count)
        {
            Id = id;
            Color = color;
            Count = count;
        }

        public BlockDefinition DeepCopy() => new BlockDefinition(Id, Color, Count);

        public override string ToString() => $"block {Id}: color {Color} x {Count}";
    }

    /// <summary>The static tray. Columns[c][0] is the front (tappable) block of column c.</summary>
    public sealed class TrayDefinition
    {
        public BlockDefinition[][] Columns { get; set; }

        public TrayDefinition() { }

        /// <summary>Copies the columns and blocks.</summary>
        public TrayDefinition(BlockDefinition[][] columns)
        {
            Columns = CopyColumns(columns);
        }

        /// <summary>Blocks in all columns.</summary>
        public int BlockCount
        {
            get
            {
                int n = 0;
                foreach (BlockDefinition[] col in Columns) n += col.Length;
                return n;
            }
        }

        /// <summary>Trucks of this color in all blocks.</summary>
        public int TrucksOf(int color)
        {
            int n = 0;
            foreach (BlockDefinition[] col in Columns)
                foreach (BlockDefinition b in col)
                    if (b.Color == color) n += b.Count;
            return n;
        }

        public TrayDefinition DeepCopy() => new TrayDefinition(Columns);

        static BlockDefinition[][] CopyColumns(BlockDefinition[][] columns)
        {
            if (columns == null) return null;
            var copy = new BlockDefinition[columns.Length][];
            for (int c = 0; c < columns.Length; c++)
            {
                BlockDefinition[] col = columns[c];
                if (col == null) continue;
                copy[c] = new BlockDefinition[col.Length];
                for (int k = 0; k < col.Length; k++) copy[c][k] = col[k]?.DeepCopy();
            }
            return copy;
        }
    }

    /// <summary>cube-hunt BuildOptions. Optional fields left out of the JSON hold these defaults.</summary>
    public sealed class GenerationSettings
    {
        public const double DefaultTargetWinRate = 1;
        public const int DefaultColumns = 5;
        public const double DefaultBigChance = 0.15;
        public const int DefaultCandidates = 16;

        public int Seed { get; set; }
        public double Shuffle { get; set; }
        /// <summary>Null = not in the JSON; <see cref="EffectiveTargetWinRate"/> gives the value used.</summary>
        public double? TargetWinRate { get; set; }
        public int? Columns { get; set; }
        public double? BigChance { get; set; }
        /// <summary>Null = derived from the picture size.</summary>
        public int? MinBlock { get; set; }
        /// <summary>Null = derived from the picture size.</summary>
        public int? MaxBlock { get; set; }
        public int? Candidates { get; set; }

        public double EffectiveTargetWinRate => TargetWinRate ?? DefaultTargetWinRate;
        public int EffectiveColumns => Columns ?? DefaultColumns;
        public double EffectiveBigChance => BigChance ?? DefaultBigChance;
        public int EffectiveCandidates => Candidates ?? DefaultCandidates;

        public GenerationSettings DeepCopy() => (GenerationSettings)MemberwiseClone(); // only value types
    }

    public sealed class GreedyResult
    {
        public bool Solved { get; set; }
        public int Ticks { get; set; }
        public int Taps { get; set; }

        public GreedyResult DeepCopy() => (GreedyResult)MemberwiseClone();
    }

    public enum DifficultyLabel
    {
        Easy,
        Medium,
        Hard,
        Expert,
    }

    public sealed class DifficultyMetadata
    {
        public double WinRate { get; set; }
        public DifficultyLabel Label { get; set; }
        public int? RandomGames { get; set; }
        public GreedyResult Greedy { get; set; }

        public DifficultyMetadata DeepCopy()
        {
            return new DifficultyMetadata { WinRate = WinRate, Label = Label, RandomGames = RandomGames, Greedy = Greedy?.DeepCopy() };
        }
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

        public static string ToJson(DifficultyLabel label)
        {
            switch (label)
            {
                case DifficultyLabel.Easy: return "Easy";
                case DifficultyLabel.Medium: return "Medium";
                case DifficultyLabel.Hard: return "Hard";
                case DifficultyLabel.Expert: return "Expert";
                default: throw new ArgumentOutOfRangeException(nameof(label));
            }
        }

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

    /// <summary>JSON names of <see cref="OpenEdges"/>.</summary>
    public static class OpenEdgesJson
    {
        /// <summary>Exact, case-sensitive and culture-independent (no ToLower: "SIDES" is rejected, not lowered).</summary>
        public static bool TryParse(string text, out OpenEdges edges)
        {
            switch (text)
            {
                case "all": edges = OpenEdges.All; return true;
                case "sides": edges = OpenEdges.Sides; return true;
                case "bottom": edges = OpenEdges.Bottom; return true;
                default: edges = default; return false;
            }
        }

        public static string ToJson(OpenEdges edges)
        {
            switch (edges)
            {
                case OpenEdges.All: return "all";
                case OpenEdges.Sides: return "sides";
                case OpenEdges.Bottom: return "bottom";
                default: throw new ArgumentOutOfRangeException(nameof(edges));
            }
        }
    }
}
