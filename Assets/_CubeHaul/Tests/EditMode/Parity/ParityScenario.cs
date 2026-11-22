using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;

namespace CubeHaul.Tests.EditMode.Parity
{
    /// <summary>
    /// One parity scenario file (docs/PARITY.md): the level and tray to play, the calls TypeScript made, and after each
    /// call what its rules reported. Rows are stored as changes from the row before; here every row is expanded to all
    /// its fields.
    /// </summary>
    sealed class ParityScenario
    {
        public const string Dir = GoldenScenarios.GoldenDir + "/parity";

        /// <summary>The snapshot fields, in the order they are compared and reported.</summary>
        public static readonly string[] Fields =
        {
            "status", "remaining", "pending", "tray", "columns", "fronts", "slots", "emptied",
            "canEat", "blocked", "stuck", "reachable", "pick", "reachHash", "boardHash",
        };

        public string File { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string LevelFile { get; private set; }
        public OpenEdges Edges { get; private set; }
        public int SlotCount { get; private set; }
        public JsonValue Tray { get; private set; }
        public string TraySha256 { get; set; }
        public string Policy { get; private set; }
        public long Seed { get; private set; }
        public string Outcome { get; private set; }
        public List<ParityStep> Steps { get; private set; }

        public string TraySource => Tray.Get("source").String;

        public static IEnumerable<string> Files() =>
            Directory.GetFiles(ProjectPaths.Of(Dir), "*.json").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal);

        public static ParityScenario Load(string file)
        {
            JsonValue root = JsonParser.Parse(System.IO.File.ReadAllText(ProjectPaths.Of(Dir + "/" + file), Encoding.UTF8));
            if (root.Get("format").String != "cubehaul.golden.parity") throw new InvalidDataException(file + ": not a parity file");
            OpenEdgesJson.TryParse(root.Get("edges").String, out OpenEdges edges);
            var s = new ParityScenario
            {
                File = file,
                Name = root.Get("name").String,
                Description = root.Get("scenario").String,
                LevelFile = root.Get("levelFile").String,
                Edges = edges,
                SlotCount = (int)root.Get("slotCount").Number,
                Tray = root.Get("tray"),
                TraySha256 = root.Get("traySha256").String,
                Policy = root.Get("policy").String,
                Seed = (long)root.Get("seed").Number,
                Outcome = root.Get("outcome").String,
                Steps = new List<ParityStep>(),
            };
            var current = new Dictionary<string, string>();
            foreach (JsonValue row in root.Get("steps").Items)
            {
                foreach (string field in Fields)
                    if (row.Has(field)) current[field] = JsonWriter.Stringify(row.Get(field));
                if (current.Count != Fields.Length) throw new InvalidDataException($"{file}: the first row must have every field");
                s.Steps.Add(new ParityStep(row, new Dictionary<string, string>(current)));
            }
            if (s.Steps.Count != (int)root.Get("stepCount").Number) throw new InvalidDataException(file + ": stepCount");
            for (int k = 0; k < s.Steps.Count; k++)
                if (s.Steps[k].Index != k) throw new InvalidDataException($"{file}: row {k} has i = {s.Steps[k].Index}");
            return s;
        }
    }

    /// <summary>One call and the snapshot TypeScript reported after it.</summary>
    sealed class ParityStep
    {
        public ParityStep(JsonValue row, Dictionary<string, string> expected)
        {
            Index = (int)row.Get("i").Number;
            Op = row.Get("op").String;
            if (row.Has("col")) Col = (int)row.Get("col").Number;
            if (row.Has("cell")) Cell = (int)row.Get("cell").Number;
            if (row.Has("ready") && row.Get("ready").Kind != JsonKind.Null) Ready = row.Get("ready").Items.Select(v => v.Boolean).ToArray();
            if (row.Has("result")) Result = JsonWriter.Stringify(row.Get("result"));
            if (row.Has("sent")) Sent = JsonWriter.Stringify(row.Get("sent"));
            Expected = expected;
        }

        public int Index { get; }
        public string Op { get; }
        public int? Col { get; }
        public int? Cell { get; set; }
        /// <summary>Per-slot ready flags of a tick; null = every slot ready.</summary>
        public bool[] Ready { get; }
        /// <summary>tap: the slot it returned, as JSON text.</summary>
        public string Result { get; }
        /// <summary>tick / step: the trucks sent, [[slot, cell, color], ...], as JSON text.</summary>
        public string Sent { get; set; }
        /// <summary>Snapshot field → JSON text (JavaScript's JSON.stringify form).</summary>
        public Dictionary<string, string> Expected { get; }

        public string Describe()
        {
            switch (Op)
            {
                case "tap": return $"tap(col {Col})";
                case "tick": return Ready == null ? "tick()" : "tick(ready [" + string.Join(",", Ready.Select(r => r ? "1" : "0")) + "])";
                case "collect": return $"collect(cell {Cell})";
                default: return Op + "()";
            }
        }
    }
}
