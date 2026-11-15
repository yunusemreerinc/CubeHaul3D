using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CubeHaul.Levels.Json;

namespace CubeHaul.Levels
{
    /// <summary>
    /// Writes a LevelDefinition back to JSON. <see cref="ToJson"/> gives the canonical layout of the TypeScript writer
    /// (Tools/level-format/src/format.ts) byte for byte: keys in schema order, the picture one row per line, one block
    /// per line. Reading a level file and writing it again gives the same file, which is how the tests show that
    /// nothing is lost on the way through the C# model.
    /// </summary>
    public static class LevelDefinitionWriter
    {
        /// <summary>
        /// The level as a JSON tree (keys in schema order; optional fields only when set). Never throws: missing parts of
        /// a hand-built level become JSON nulls or bad values, which <see cref="LevelValidator"/> then reports by path.
        /// </summary>
        public static JsonValue ToJsonValue(LevelDefinition level)
        {
            JsonValue root = JsonValue.NewObject();
            if (level.SchemaUri != null) root.Set("$schema", JsonValue.FromString(level.SchemaUri));
            root.Set("schemaVersion", Number(level.SchemaVersion));
            root.Set("levelId", Str(level.LevelId));
            root.Set("themeId", Str(level.ThemeId));
            root.Set("name", Str(level.Name));
            if (level.Picture != null) root.Set("picture", Picture(level.Picture));
            root.Set("edges", JsonValue.FromString(EdgesName(level.Edges)));
            root.Set("slotCount", Number(level.SlotCount));
            if (level.Tray != null) root.Set("tray", Tray(level.Tray));
            if (level.Generation != null) root.Set("generation", Generation(level.Generation));
            if (level.DifficultyMetadata != null) root.Set("difficultyMetadata", Difficulty(level.DifficultyMetadata));
            if (level.Extensions != null) root.Set("extensions", level.Extensions);
            return root;
        }

        /// <summary>Canonical text, identical to what the TypeScript tools write for the same level. Expects a valid level.</summary>
        public static string ToJson(LevelDefinition level)
        {
            JsonValue root = ToJsonValue(level);
            var lines = new List<string>();
            IReadOnlyList<KeyValuePair<string, JsonValue>> members = root.Members;
            for (int i = 0; i < members.Count; i++)
            {
                string key = members[i].Key;
                JsonValue v = members[i].Value;
                string comma = i < members.Count - 1 ? "," : "";
                if (key == "picture") WritePicture(lines, level.Picture, comma);
                else if (key == "tray") WriteTray(lines, level.Tray, comma);
                else if (key == "generation" || key == "difficultyMetadata")
                {
                    lines.Add("  " + Quote(key) + ": {");
                    for (int j = 0; j < v.Members.Count; j++)
                        lines.Add("    " + Quote(v.Members[j].Key) + ": " + Inline(v.Members[j].Value) + (j < v.Members.Count - 1 ? "," : ""));
                    lines.Add("  }" + comma);
                }
                else if (key == "extensions") lines.Add("  " + Quote(key) + ": " + JsonWriter.Stringify(v, 2).Replace("\n", "\n  ") + comma);
                else lines.Add("  " + Quote(key) + ": " + JsonWriter.Stringify(v) + comma);
            }
            return "{\n" + string.Join("\n", lines) + "\n}\n";
        }

        static void WritePicture(List<string> lines, PictureDefinition p, string comma)
        {
            lines.Add("  \"picture\": {");
            lines.Add("    \"width\": " + Int(p.Width) + ",");
            lines.Add("    \"height\": " + Int(p.Height) + ",");
            lines.Add("    \"palette\": " + Inline(PaletteValue(p.Palette)) + ",");
            lines.Add("    \"cells\": [");
            var rows = new List<string>();
            for (int y = 0; y < p.Height; y++)
            {
                var row = new StringBuilder("      ");
                for (int x = 0; x < p.Width; x++)
                {
                    if (x > 0) row.Append(',');
                    row.Append(Int(p.Cells[y * p.Width + x]).PadLeft(2));
                }
                rows.Add(row.ToString());
            }
            lines.Add(string.Join(",\n", rows));
            lines.Add("    ]");
            lines.Add("  }" + comma);
        }

        static void WriteTray(List<string> lines, TrayDefinition tray, string comma)
        {
            lines.Add("  \"tray\": {");
            lines.Add("    \"columns\": [");
            for (int c = 0; c < tray.Columns.Length; c++)
            {
                string cc = c < tray.Columns.Length - 1 ? "," : "";
                BlockDefinition[] col = tray.Columns[c];
                if (col.Length == 0)
                {
                    lines.Add("      []" + cc);
                    continue;
                }
                lines.Add("      [");
                for (int k = 0; k < col.Length; k++)
                    lines.Add("        { \"id\": " + Int(col[k].Id) + ", \"color\": " + Int(col[k].Color) + ", \"count\": " + Int(col[k].Count) + " }" + (k < col.Length - 1 ? "," : ""));
                lines.Add("      ]" + cc);
            }
            lines.Add("    ]");
            lines.Add("  }" + comma);
        }

        static JsonValue Picture(PictureDefinition p)
        {
            JsonValue o = JsonValue.NewObject();
            o.Set("width", Number(p.Width));
            o.Set("height", Number(p.Height));
            o.Set("palette", p.Palette == null ? JsonValue.Null : PaletteValue(p.Palette));
            if (p.Cells == null) o.Set("cells", JsonValue.Null);
            else
            {
                JsonValue cells = JsonValue.NewArray();
                foreach (int c in p.Cells) cells.Add(Number(c));
                o.Set("cells", cells);
            }
            return o;
        }

        static JsonValue PaletteValue(string[] palette)
        {
            JsonValue a = JsonValue.NewArray();
            if (palette != null)
                foreach (string hex in palette) a.Add(Str(hex));
            return a;
        }

        static JsonValue Tray(TrayDefinition tray)
        {
            JsonValue o = JsonValue.NewObject();
            if (tray.Columns == null)
            {
                o.Set("columns", JsonValue.Null);
                return o;
            }
            JsonValue columns = JsonValue.NewArray();
            foreach (BlockDefinition[] col in tray.Columns)
            {
                if (col == null)
                {
                    columns.Add(JsonValue.Null);
                    continue;
                }
                JsonValue column = JsonValue.NewArray();
                foreach (BlockDefinition b in col)
                {
                    if (b == null)
                    {
                        column.Add(JsonValue.Null);
                        continue;
                    }
                    JsonValue block = JsonValue.NewObject();
                    block.Set("id", Number(b.Id));
                    block.Set("color", Number(b.Color));
                    block.Set("count", Number(b.Count));
                    column.Add(block);
                }
                columns.Add(column);
            }
            o.Set("columns", columns);
            return o;
        }

        static JsonValue Generation(GenerationSettings g)
        {
            JsonValue o = JsonValue.NewObject();
            o.Set("seed", Number(g.Seed));
            o.Set("shuffle", Number(g.Shuffle));
            if (g.TargetWinRate.HasValue) o.Set("targetWinRate", Number(g.TargetWinRate.Value));
            if (g.Columns.HasValue) o.Set("columns", Number(g.Columns.Value));
            if (g.BigChance.HasValue) o.Set("bigChance", Number(g.BigChance.Value));
            if (g.MinBlock.HasValue) o.Set("minBlock", Number(g.MinBlock.Value));
            if (g.MaxBlock.HasValue) o.Set("maxBlock", Number(g.MaxBlock.Value));
            if (g.Candidates.HasValue) o.Set("candidates", Number(g.Candidates.Value));
            return o;
        }

        static JsonValue Difficulty(DifficultyMetadata d)
        {
            JsonValue o = JsonValue.NewObject();
            o.Set("winRate", Number(d.WinRate));
            o.Set("label", JsonValue.FromString(System.Enum.IsDefined(typeof(DifficultyLabel), d.Label) ? DifficultyLabels.ToJson(d.Label) : ((int)d.Label).ToString(CultureInfo.InvariantCulture)));
            if (d.RandomGames.HasValue) o.Set("randomGames", Number(d.RandomGames.Value));
            if (d.Greedy != null)
            {
                JsonValue g = JsonValue.NewObject();
                g.Set("solved", JsonValue.FromBoolean(d.Greedy.Solved));
                g.Set("ticks", Number(d.Greedy.Ticks));
                g.Set("taps", Number(d.Greedy.Taps));
                o.Set("greedy", g);
            }
            return o;
        }

        // The TypeScript writer's inline(): JSON.stringify, then ", " after commas and ": " after colons.
        static string Inline(JsonValue v) => JsonWriter.Stringify(v).Replace(",", ", ").Replace(":", ": ");

        static string Quote(string s)
        {
            var sb = new StringBuilder();
            JsonWriter.WriteString(sb, s);
            return sb.ToString();
        }

        static string EdgesName(Domain.OpenEdges edges) =>
            System.Enum.IsDefined(typeof(Domain.OpenEdges), edges) ? OpenEdgesJson.ToJson(edges) : ((int)edges).ToString(CultureInfo.InvariantCulture);

        static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
        static JsonValue Number(double v) => JsonValue.FromNumber(v);
        static JsonValue Str(string s) => s == null ? JsonValue.Null : JsonValue.FromString(s);
    }
}
