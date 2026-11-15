using System.Collections.Generic;

namespace CubeHaul.Data
{
    /// <summary>Issue codes, identical to the TypeScript validator's (Tools/level-format/src/validate.ts).</summary>
    public static class IssueCodes
    {
        public const string JsonSyntax = "JSON_SYNTAX";
        public const string WrongType = "WRONG_TYPE";
        public const string MissingField = "MISSING_FIELD";
        public const string UnknownField = "UNKNOWN_FIELD";
        public const string UnsupportedSchemaVersion = "UNSUPPORTED_SCHEMA_VERSION";
        public const string InvalidValue = "INVALID_VALUE";
        public const string OutOfRange = "OUT_OF_RANGE";
        public const string CellsLength = "CELLS_LENGTH";
        public const string CellColor = "CELL_COLOR";
        public const string EmptyPicture = "EMPTY_PICTURE";
        public const string TrayEmpty = "TRAY_EMPTY";
        public const string DuplicateCrateId = "DUPLICATE_CRATE_ID";
        public const string CrateColor = "CRATE_COLOR";
        public const string TrayTotalMismatch = "TRAY_TOTAL_MISMATCH";
        public const string ColumnCountMismatch = "COLUMN_COUNT_MISMATCH";
        public const string NoTrayOrGeneration = "NO_TRAY_OR_GENERATION";
        public const string DifficultyWithoutTray = "DIFFICULTY_WITHOUT_TRAY";
        public const string DifficultyLabelMismatch = "DIFFICULTY_LABEL_MISMATCH";
        // warnings
        public const string UnusedPaletteColor = "UNUSED_PALETTE_COLOR";
        public const string DuplicatePaletteColor = "DUPLICATE_PALETTE_COLOR";
        public const string BlockSizeOverride = "BLOCK_SIZE_OVERRIDE";
    }

    public sealed class LevelIssue
    {
        public string Code { get; }
        /// <summary>JSON Pointer (RFC 6901) to the offending value; "" is the whole document.</summary>
        public string Path { get; }
        public string Message { get; }

        public LevelIssue(string code, string path, string message)
        {
            Code = code;
            Path = path;
            Message = message;
        }

        public override string ToString() => $"{Code} {(Path.Length == 0 ? "(root)" : Path)}: {Message}";
    }

    public sealed class LevelReadResult
    {
        public bool Ok => Errors.Count == 0;
        /// <summary>Set only when there are no errors.</summary>
        public LevelDefinition Level { get; }
        public IReadOnlyList<LevelIssue> Errors { get; }
        public IReadOnlyList<LevelIssue> Warnings { get; }

        public LevelReadResult(LevelDefinition level, IReadOnlyList<LevelIssue> errors, IReadOnlyList<LevelIssue> warnings)
        {
            Level = level;
            Errors = errors;
            Warnings = warnings;
        }
    }
}
