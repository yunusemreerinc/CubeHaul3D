using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CubeHaul.Levels.Json;

namespace CubeHaul.Levels
{
    /// <summary>
    /// Validates LevelDefinition v1 JSON. A rule-for-rule twin of Tools/level-format/src/validate.ts: the same checks in
    /// the same order, reporting the same codes at the same JSON Pointer paths (checked by the shared fixtures in
    /// LevelFormat/fixtures). The rules are listed in docs/LEVEL_FORMAT.md §5.
    /// </summary>
    public static class LevelValidator
    {
        public const int MinSide = 1, MaxSide = 64;
        public const int MinPalette = 1, MaxPalette = 32;
        public const int MinSlots = 1, MaxSlots = 6;
        public const int MinColumns = 1, MaxColumns = 10;
        public const int MaxCrateId = 999_999;
        public const int MinCrateCount = 1, MaxCrateCount = 9_999;
        public const int MaxSeed = 2_147_483_647;
        public const double MaxShuffle = 100;
        public const int MinBlockSize = 1, MaxBlockSize = 9_999;
        public const int MinCandidates = 1, MaxCandidates = 1_000;
        public const int MinRandomGames = 1, MaxRandomGames = 10_000;
        public const int MaxNameLength = 64;
        /// <summary>JavaScript's Number.MAX_SAFE_INTEGER, the open upper bound the TypeScript validator uses.</summary>
        const double MaxSafeInteger = 9_007_199_254_740_991;

        // \z, not $: in .NET "$" also matches before a final "\n", in JavaScript it does not.
        static readonly Regex Id = new Regex(@"^[a-z0-9][a-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant);
        static readonly Regex Hex = new Regex(@"^#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant);

        static readonly string[] RootKeys =
        {
            "$schema", "schemaVersion", "levelId", "themeId", "name", "picture", "edges", "slotCount",
            "tray", "generation", "difficultyMetadata", "extensions",
        };
        static readonly string[] RootRequired = { "levelId", "themeId", "name", "picture", "edges", "slotCount" };
        static readonly string[] PictureKeys = { "width", "height", "cells", "palette" };
        static readonly string[] TrayKeys = { "columns" };
        static readonly string[] CrateKeys = { "id", "color", "count" };
        static readonly string[] GenerationKeys = { "seed", "shuffle", "targetWinRate", "columns", "bigChance", "minBlock", "maxBlock", "candidates" };
        static readonly string[] GenerationRequired = { "seed", "shuffle" };
        static readonly string[] DifficultyKeys = { "winRate", "label", "randomGames", "greedy" };
        static readonly string[] DifficultyRequired = { "winRate", "label" };
        static readonly string[] GreedyKeys = { "solved", "ticks", "taps" };

        /// <summary>Validates a parsed JSON document and, if there are no errors, builds the LevelDefinition.</summary>
        public static LevelReadResult Validate(JsonValue root)
        {
            var c = new Checker();
            if (root == null || !root.IsObject)
            {
                c.Error(IssueCodes.WrongType, "", $"expected an object, got {TypeOf(root)}");
                return c.Result(null);
            }
            // The version decides the shape of everything else, so nothing more is checked without a known one.
            if (!root.Has("schemaVersion"))
            {
                c.Error(IssueCodes.MissingField, "/schemaVersion", "missing required field \"schemaVersion\"");
                return c.Result(null);
            }
            JsonValue version = root.Get("schemaVersion");
            if (!version.IsInteger)
            {
                c.Error(IssueCodes.WrongType, "/schemaVersion", $"expected an integer, got {version.TypeName}");
                return c.Result(null);
            }
            if (version.Number != LevelDefinition.CurrentSchemaVersion)
            {
                c.Error(IssueCodes.UnsupportedSchemaVersion, "/schemaVersion",
                    $"schema version {Format(version.Number)} is not supported (this reader knows {LevelDefinition.CurrentSchemaVersion})");
                return c.Result(null);
            }

            c.Object(root, "", RootKeys, RootRequired);
            if (root.Has("$schema")) c.String(root.Get("$schema"), "/$schema");
            foreach (string k in new[] { "levelId", "themeId" })
            {
                if (!root.Has(k)) continue;
                string s = c.String(root.Get(k), "/" + k);
                if (s != null && !Id.IsMatch(s)) c.Error(IssueCodes.InvalidValue, "/" + k, $"\"{s}\" must match ^[a-z0-9][a-z0-9_-]{{0,63}}$");
            }
            if (root.Has("name"))
            {
                string s = c.String(root.Get("name"), "/name");
                if (s != null && (IsBlank(s) || s.Length > MaxNameLength))
                    c.Error(IssueCodes.InvalidValue, "/name", $"the name must be 1..{MaxNameLength} characters and not blank");
            }
            if (root.Has("edges"))
            {
                string s = c.String(root.Get("edges"), "/edges");
                if (s != null && !EdgeModes.TryParse(s, out _)) c.Error(IssueCodes.InvalidValue, "/edges", $"\"{s}\" is not one of bottom, sides, all");
            }
            if (root.Has("slotCount")) c.Integer(root.Get("slotCount"), "/slotCount", MinSlots, MaxSlots);
            if (root.Has("extensions") && !root.Get("extensions").IsObject)
                c.Error(IssueCodes.WrongType, "/extensions", $"expected an object, got {root.Get("extensions").TypeName}");

            // Structure of each section, then the rules between them (S1..S5).
            PictureInfo picture = null;
            string[] palette = null;
            if (root.Has("picture")) CheckPicture(c, root.Get("picture"), out picture, out palette);
            List<List<CrateInfo>> tray = root.Has("tray") ? CheckTray(c, root.Get("tray")) : null;
            bool generationOk = false;
            int? generationColumns = null;
            if (root.Has("generation")) generationOk = CheckGeneration(c, root.Get("generation"), out generationColumns);
            if (root.Has("difficultyMetadata")) CheckDifficulty(c, root.Get("difficultyMetadata"));

            bool playable = picture != null && CheckPictureRules(c, picture); // S1
            if (tray != null) CheckTrayRules(c, tray, palette, playable ? picture : null); // S2
            if (tray != null && generationOk && generationColumns.HasValue && generationColumns.Value != tray.Count) // S3
                c.Error(IssueCodes.ColumnCountMismatch, "/tray/columns",
                    $"the tray has {tray.Count} columns but generation.columns is {generationColumns.Value}");
            if (root.Has("difficultyMetadata") && !root.Has("tray")) // S4
                c.Error(IssueCodes.DifficultyWithoutTray, "/difficultyMetadata", "difficultyMetadata describes a static tray, but there is none");
            if (!root.Has("tray") && !root.Has("generation")) // S5
                c.Error(IssueCodes.NoTrayOrGeneration, "", "a level needs a static tray, generation settings, or both");

            return c.Result(c.Errors.Count == 0 ? Build(root) : null);
        }

        // ---- sections ----

        sealed class PictureInfo
        {
            public int Width, Height;
            public string[] Palette;
            public int[] Cells;
        }

        sealed class CrateInfo
        {
            public int Id, Color, Count;
            public string Path;
        }

        static void CheckPicture(Checker c, JsonValue v, out PictureInfo picture, out string[] palette)
        {
            const string path = "/picture";
            picture = null;
            palette = null;
            int before = c.Errors.Count;
            if (!c.Object(v, path, PictureKeys, PictureKeys)) return;
            int? width = v.Has("width") ? c.Integer(v.Get("width"), path + "/width", MinSide, MaxSide) : null;
            int? height = v.Has("height") ? c.Integer(v.Get("height"), path + "/height", MinSide, MaxSide) : null;

            if (v.Has("palette"))
            {
                IReadOnlyList<JsonValue> raw = c.ArrayOfLength(v.Get("palette"), path + "/palette", MinPalette, MaxPalette);
                if (raw != null)
                {
                    int paletteBefore = c.Errors.Count;
                    var colors = new string[raw.Count];
                    for (int i = 0; i < raw.Count; i++)
                    {
                        string p = Pointer.At(path + "/palette", i);
                        string s = c.String(raw[i], p);
                        if (s != null && !Hex.IsMatch(s)) c.Error(IssueCodes.InvalidValue, p, $"\"{s}\" is not a #rrggbb color");
                        colors[i] = s;
                    }
                    if (c.Errors.Count == paletteBefore) palette = colors;
                }
            }

            int[] cells = null;
            if (v.Has("cells"))
            {
                IReadOnlyList<JsonValue> raw = c.Array(v.Get("cells"), path + "/cells");
                if (raw != null)
                {
                    int cellsBefore = c.Errors.Count;
                    var values = new int[raw.Count];
                    for (int i = 0; i < raw.Count; i++)
                    {
                        JsonValue x = raw[i];
                        // Range is a rule (S1, CELL_COLOR); here only the type. Clamp so huge values stay out of range.
                        if (!x.IsInteger) c.Error(IssueCodes.WrongType, Pointer.At(path + "/cells", i), $"expected an integer, got {Describe(x)}");
                        else values[i] = x.Number > int.MaxValue ? int.MaxValue : x.Number < int.MinValue ? int.MinValue : (int)x.Number;
                    }
                    if (c.Errors.Count == cellsBefore) cells = values;
                }
            }

            if (c.Errors.Count != before || !width.HasValue || !height.HasValue || palette == null || cells == null) return;
            picture = new PictureInfo { Width = width.Value, Height = height.Value, Palette = palette, Cells = cells };
        }

        /// <summary>S1: the cells against the size and the palette. True if the picture is playable.</summary>
        static bool CheckPictureRules(Checker c, PictureInfo p)
        {
            const string path = "/picture";
            if (p.Cells.Length != p.Width * p.Height)
            {
                c.Error(IssueCodes.CellsLength, path + "/cells",
                    $"has {p.Cells.Length} cells, expected width * height = {p.Width * p.Height}");
                return false;
            }
            bool bad = false;
            for (int i = 0; i < p.Cells.Length; i++)
            {
                int x = p.Cells[i];
                if (x < -1 || x >= p.Palette.Length)
                {
                    c.Error(IssueCodes.CellColor, Pointer.At(path + "/cells", i),
                        $"{x} is neither EMPTY (-1) nor a palette index 0..{p.Palette.Length - 1}");
                    bad = true;
                }
            }
            if (bad) return false;
            var used = new int[p.Palette.Length];
            foreach (int x in p.Cells)
                if (x >= 0) used[x]++;
            bool any = false;
            foreach (int n in used) any |= n > 0;
            if (!any)
            {
                c.Error(IssueCodes.EmptyPicture, path + "/cells", "the picture has no cubes");
                return false;
            }
            for (int k = 0; k < used.Length; k++)
                if (used[k] == 0) c.Warn(IssueCodes.UnusedPaletteColor, Pointer.At(path + "/palette", k), $"palette color {k} is not used by any cell");
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int k = 0; k < p.Palette.Length; k++)
            {
                string key = p.Palette[k].ToLowerInvariant();
                if (seen.TryGetValue(key, out int first))
                    c.Warn(IssueCodes.DuplicatePaletteColor, Pointer.At(path + "/palette", k), $"same color as palette entry {first}");
                else seen.Add(key, k);
            }
            return true;
        }

        static List<List<CrateInfo>> CheckTray(Checker c, JsonValue v)
        {
            const string path = "/tray";
            int before = c.Errors.Count;
            if (!c.Object(v, path, TrayKeys, TrayKeys)) return null;
            if (!v.Has("columns")) return null;
            IReadOnlyList<JsonValue> cols = c.ArrayOfLength(v.Get("columns"), path + "/columns", MinColumns, MaxColumns);
            if (cols == null) return null;
            var output = new List<List<CrateInfo>>();
            for (int ci = 0; ci < cols.Count; ci++)
            {
                string colPath = Pointer.At(path + "/columns", ci);
                IReadOnlyList<JsonValue> crates = c.Array(cols[ci], colPath);
                var list = new List<CrateInfo>();
                output.Add(list);
                if (crates == null) continue;
                for (int ki = 0; ki < crates.Count; ki++)
                {
                    string p = Pointer.At(colPath, ki);
                    JsonValue k = crates[ki];
                    if (!c.Object(k, p, CrateKeys, CrateKeys)) continue;
                    int? id = k.Has("id") ? c.Integer(k.Get("id"), p + "/id", 0, MaxCrateId) : null;
                    int? color = k.Has("color") ? c.Integer(k.Get("color"), p + "/color", 0, MaxSafeInteger) : null;
                    int? count = k.Has("count") ? c.Integer(k.Get("count"), p + "/count", MinCrateCount, MaxCrateCount) : null;
                    if (id.HasValue && color.HasValue && count.HasValue)
                        list.Add(new CrateInfo { Id = id.Value, Color = color.Value, Count = count.Value, Path = p });
                }
            }
            return c.Errors.Count == before ? output : null;
        }

        /// <summary>S2: the tray against itself and the picture.</summary>
        static void CheckTrayRules(Checker c, List<List<CrateInfo>> columns, string[] palette, PictureInfo picture)
        {
            var crates = new List<CrateInfo>();
            foreach (List<CrateInfo> col in columns) crates.AddRange(col);
            if (crates.Count == 0)
            {
                c.Error(IssueCodes.TrayEmpty, "/tray/columns", "the tray has no crates");
                return;
            }
            var ids = new HashSet<int>();
            foreach (CrateInfo k in crates)
            {
                if (!ids.Add(k.Id)) c.Error(IssueCodes.DuplicateCrateId, k.Path + "/id", $"crate id {k.Id} is used more than once");
            }
            if (palette == null) return;
            bool badColor = false;
            foreach (CrateInfo k in crates)
            {
                if (k.Color >= palette.Length)
                {
                    c.Error(IssueCodes.CrateColor, k.Path + "/color", $"{k.Color} is not a palette index 0..{palette.Length - 1}");
                    badColor = true;
                }
            }
            if (badColor || picture == null) return;
            for (int color = 0; color < palette.Length; color++)
            {
                int cubes = 0;
                foreach (int x in picture.Cells)
                    if (x == color) cubes++;
                long trucks = 0;
                foreach (CrateInfo k in crates)
                    if (k.Color == color) trucks += k.Count;
                if (cubes != trucks)
                    c.Error(IssueCodes.TrayTotalMismatch, "/picture/palette/" + color.ToString(CultureInfo.InvariantCulture),
                        $"color {color} has {cubes} cubes but {trucks} trucks in the tray");
            }
        }

        static bool CheckGeneration(Checker c, JsonValue v, out int? columns)
        {
            const string path = "/generation";
            columns = null;
            int before = c.Errors.Count;
            if (!c.Object(v, path, GenerationKeys, GenerationRequired)) return false;
            if (v.Has("seed")) c.Integer(v.Get("seed"), path + "/seed", 0, MaxSeed);
            if (v.Has("shuffle")) c.Number(v.Get("shuffle"), path + "/shuffle", 0, MaxShuffle);
            if (v.Has("targetWinRate")) c.Number(v.Get("targetWinRate"), path + "/targetWinRate", 0, 1);
            columns = v.Has("columns") ? c.Integer(v.Get("columns"), path + "/columns", MinColumns, MaxColumns) : GenerationSettings.DefaultColumns;
            if (v.Has("bigChance")) c.Number(v.Get("bigChance"), path + "/bigChance", 0, 1);
            foreach (string k in new[] { "minBlock", "maxBlock" })
            {
                if (!v.Has(k)) continue;
                if (c.Integer(v.Get(k), path + "/" + k, MinBlockSize, MaxBlockSize).HasValue)
                    c.Warn(IssueCodes.BlockSizeOverride, path + "/" + k, $"{k} overrides the derived crate sizes and is not always honoured (TS_BASELINE F-4)");
            }
            if (v.Has("candidates")) c.Integer(v.Get("candidates"), path + "/candidates", MinCandidates, MaxCandidates);
            return c.Errors.Count == before;
        }

        static void CheckDifficulty(Checker c, JsonValue v)
        {
            const string path = "/difficultyMetadata";
            if (!c.Object(v, path, DifficultyKeys, DifficultyRequired)) return;
            double? winRate = v.Has("winRate") ? c.Number(v.Get("winRate"), path + "/winRate", 0, 1) : null;
            DifficultyLabel? label = null;
            if (v.Has("label"))
            {
                string s = c.String(v.Get("label"), path + "/label");
                if (s != null)
                {
                    if (DifficultyLabels.TryParse(s, out DifficultyLabel parsed)) label = parsed;
                    else c.Error(IssueCodes.InvalidValue, path + "/label", $"\"{s}\" is not one of Easy, Medium, Hard, Expert");
                }
            }
            if (v.Has("randomGames")) c.Integer(v.Get("randomGames"), path + "/randomGames", MinRandomGames, MaxRandomGames);
            if (v.Has("greedy"))
            {
                JsonValue g = v.Get("greedy");
                const string gp = path + "/greedy";
                if (c.Object(g, gp, GreedyKeys, GreedyKeys))
                {
                    if (g.Has("solved")) c.Boolean(g.Get("solved"), gp + "/solved");
                    if (g.Has("ticks")) c.Integer(g.Get("ticks"), gp + "/ticks", 0, MaxSafeInteger);
                    if (g.Has("taps")) c.Integer(g.Get("taps"), gp + "/taps", 0, MaxSafeInteger);
                }
            }
            if (winRate.HasValue && label.HasValue && label.Value != DifficultyLabels.FromWinRate(winRate.Value))
                c.Error(IssueCodes.DifficultyLabelMismatch, path + "/label",
                    $"win rate {Format(winRate.Value)} is \"{DifficultyLabels.FromWinRate(winRate.Value)}\", not \"{label.Value}\"");
        }

        // ---- the model, from a document known to be valid ----

        static LevelDefinition Build(JsonValue root)
        {
            JsonValue p = root.Get("picture");
            IReadOnlyList<JsonValue> rawPalette = p.Get("palette").Items;
            IReadOnlyList<JsonValue> rawCells = p.Get("cells").Items;
            var palette = new string[rawPalette.Count];
            for (int i = 0; i < palette.Length; i++) palette[i] = rawPalette[i].String;
            var cells = new int[rawCells.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = (int)rawCells[i].Number;
            EdgeModes.TryParse(root.Get("edges").String, out EdgeMode edges);

            var level = new LevelDefinition
            {
                SchemaVersion = (int)root.Get("schemaVersion").Number,
                LevelId = root.Get("levelId").String,
                ThemeId = root.Get("themeId").String,
                Name = root.Get("name").String,
                Picture = new PictureDefinition { Width = (int)p.Get("width").Number, Height = (int)p.Get("height").Number, Palette = palette, Cells = cells },
                Edges = edges,
                SlotCount = (int)root.Get("slotCount").Number,
            };

            if (root.Has("tray"))
            {
                IReadOnlyList<JsonValue> cols = root.Get("tray").Get("columns").Items;
                var columns = new CrateDefinition[cols.Count][];
                for (int c = 0; c < cols.Count; c++)
                {
                    IReadOnlyList<JsonValue> crates = cols[c].Items;
                    columns[c] = new CrateDefinition[crates.Count];
                    for (int k = 0; k < crates.Count; k++)
                    {
                        JsonValue x = crates[k];
                        columns[c][k] = new CrateDefinition { Id = (int)x.Get("id").Number, Color = (int)x.Get("color").Number, Count = (int)x.Get("count").Number };
                    }
                }
                level.Tray = new TrayDefinition { Columns = columns };
            }

            if (root.Has("generation"))
            {
                JsonValue g = root.Get("generation");
                var gen = new GenerationSettings { Seed = (int)g.Get("seed").Number, Shuffle = g.Get("shuffle").Number };
                if (g.Has("targetWinRate")) gen.TargetWinRate = g.Get("targetWinRate").Number;
                if (g.Has("columns")) gen.Columns = (int)g.Get("columns").Number;
                if (g.Has("bigChance")) gen.BigChance = g.Get("bigChance").Number;
                if (g.Has("minBlock")) gen.MinBlock = (int)g.Get("minBlock").Number;
                if (g.Has("maxBlock")) gen.MaxBlock = (int)g.Get("maxBlock").Number;
                if (g.Has("candidates")) gen.Candidates = (int)g.Get("candidates").Number;
                level.Generation = gen;
            }

            if (root.Has("difficultyMetadata"))
            {
                JsonValue d = root.Get("difficultyMetadata");
                DifficultyLabels.TryParse(d.Get("label").String, out DifficultyLabel label);
                var meta = new DifficultyMetadata { WinRate = d.Get("winRate").Number, Label = label };
                if (d.Has("randomGames")) meta.RandomGames = (int)d.Get("randomGames").Number;
                if (d.Has("greedy"))
                {
                    JsonValue g = d.Get("greedy");
                    meta.Greedy = new GreedyResult { Solved = g.Get("solved").Boolean, Ticks = (int)g.Get("ticks").Number, Taps = (int)g.Get("taps").Number };
                }
                level.Difficulty = meta;
            }
            return level;
        }

        // ---- helpers ----

        static bool IsBlank(string s)
        {
            // Matches JavaScript's String.prototype.trim(): whitespace and line terminators, incl. U+FEFF.
            foreach (char ch in s)
                if (!char.IsWhiteSpace(ch) && ch != '﻿') return false;
            return true;
        }

        static string TypeOf(JsonValue v) => v == null ? "nothing" : v.TypeName;

        static string Describe(JsonValue v) => v.Kind == JsonKind.Number ? Format(v.Number) : v.TypeName;

        static string Format(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        sealed class Checker
        {
            public readonly List<LevelIssue> Errors = new List<LevelIssue>();
            public readonly List<LevelIssue> Warnings = new List<LevelIssue>();

            public void Error(string code, string path, string message) => Errors.Add(new LevelIssue(code, path, message));
            public void Warn(string code, string path, string message) => Warnings.Add(new LevelIssue(code, path, message));

            public LevelReadResult Result(LevelDefinition level) => new LevelReadResult(level, Errors, Warnings);

            /// <summary>An object with exactly the allowed keys and all required ones. False if not an object.</summary>
            public bool Object(JsonValue v, string path, string[] allowed, string[] required)
            {
                if (!v.IsObject)
                {
                    Error(IssueCodes.WrongType, path, $"expected an object, got {v.TypeName}");
                    return false;
                }
                foreach (KeyValuePair<string, JsonValue> m in v.Members)
                    if (System.Array.IndexOf(allowed, m.Key) < 0) Error(IssueCodes.UnknownField, Pointer.At(path, m.Key), $"unknown field \"{m.Key}\"");
                foreach (string k in required)
                    if (!v.Has(k)) Error(IssueCodes.MissingField, Pointer.At(path, k), $"missing required field \"{k}\"");
                return true;
            }

            public int? Integer(JsonValue v, string path, double min, double max)
            {
                if (!v.IsInteger)
                {
                    Error(IssueCodes.WrongType, path, $"expected an integer, got {Describe(v)}");
                    return null;
                }
                if (v.Number < min || v.Number > max)
                {
                    Error(IssueCodes.OutOfRange, path, $"{Format(v.Number)} is outside {Format(min)}..{Format(max)}");
                    return null;
                }
                return (int)Math.Min(v.Number, int.MaxValue);
            }

            public double? Number(JsonValue v, string path, double min, double max)
            {
                if (v.Kind != JsonKind.Number)
                {
                    Error(IssueCodes.WrongType, path, $"expected a number, got {v.TypeName}");
                    return null;
                }
                if (!(v.Number >= min && v.Number <= max))
                {
                    Error(IssueCodes.OutOfRange, path, $"{Format(v.Number)} is outside {Format(min)}..{Format(max)}");
                    return null;
                }
                return v.Number;
            }

            public string String(JsonValue v, string path)
            {
                if (v.Kind != JsonKind.String)
                {
                    Error(IssueCodes.WrongType, path, $"expected a string, got {v.TypeName}");
                    return null;
                }
                return v.String;
            }

            public bool? Boolean(JsonValue v, string path)
            {
                if (v.Kind != JsonKind.Boolean)
                {
                    Error(IssueCodes.WrongType, path, $"expected a boolean, got {v.TypeName}");
                    return null;
                }
                return v.Boolean;
            }

            public IReadOnlyList<JsonValue> Array(JsonValue v, string path)
            {
                if (!v.IsArray)
                {
                    Error(IssueCodes.WrongType, path, $"expected an array, got {v.TypeName}");
                    return null;
                }
                return v.Items;
            }

            public IReadOnlyList<JsonValue> ArrayOfLength(JsonValue v, string path, int min, int max)
            {
                IReadOnlyList<JsonValue> a = Array(v, path);
                if (a == null) return null;
                if (a.Count < min || a.Count > max)
                {
                    Error(IssueCodes.OutOfRange, path, $"has {a.Count} items, expected {min}..{max}");
                    return null;
                }
                return a;
            }
        }
    }

    /// <summary>JSON Pointer (RFC 6901) building.</summary>
    public static class Pointer
    {
        public static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
        public static string At(string path, string key) => path + "/" + Escape(key);
        public static string At(string path, int index) => path + "/" + index.ToString(CultureInfo.InvariantCulture);
    }
}
