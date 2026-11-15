using System.Collections.Generic;

namespace CubeHaul.Levels
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
        // repository (C# only: a set of level files, not one file)
        public const string DuplicateLevelId = "DUPLICATE_LEVEL_ID";
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

        /// <summary>
        /// A readable report, one issue per line, e.g.
        /// <code>
        /// 2 errors:
        ///   - CELLS_LENGTH at /picture/cells: has 8 cells, expected width * height = 9
        ///   - DUPLICATE_CRATE_ID at /tray/columns/1/0/id: crate id 0 is used more than once
        /// </code>
        /// </summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            void Section(IReadOnlyList<LevelIssue> issues, string one, string many)
            {
                if (issues.Count == 0) return;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(issues.Count).Append(' ').Append(issues.Count == 1 ? one : many).Append(':');
                foreach (LevelIssue i in issues)
                    sb.Append("\n  - ").Append(i.Code).Append(" at ").Append(i.Path.Length == 0 ? "(root)" : i.Path).Append(": ").Append(i.Message);
            }
            Section(Errors, "error", "errors");
            Section(Warnings, "warning", "warnings");
            return sb.Length == 0 ? "no issues" : sb.ToString();
        }
    }
}
