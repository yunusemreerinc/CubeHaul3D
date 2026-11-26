using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Gameplay;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The C# road planner against cube-hunt's RoadMap with main.ts's geometry (roads.json, docs/ROADS.md): 56 board
    /// states (the ten levels fresh, peeled and with claimed cubes, other edge modes, small hand-made boards), 1183
    /// targets. Every lane's cost, the lane a truck takes and every point of its route must be the same doubles.
    /// </summary>
    public class RoadGoldenTests
    {
        static JsonValue doc;
        static JsonValue Doc => doc ?? (doc = GoldenScenarios.Json(GoldenScenarios.GoldenDir + "/roads.json"));

        static IEnumerable<string> CaseNames() => GoldenScenarios.Json(GoldenScenarios.GoldenDir + "/roads.json").Get("cases").Items.Select(c => c.Get("name").String);

        internal static Board BoardOf(JsonValue c)
        {
            OpenEdgesJson.TryParse(c.Get("edges").String, out OpenEdges edges);
            int w, h, colors;
            int[] cells;
            if (c.Has("levelFile"))
            {
                PictureDefinition p = LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of(c.Get("levelFile").String), Encoding.UTF8)).Level.Picture;
                (w, h, cells, colors) = (p.Width, p.Height, p.Cells, p.Palette.Length);
            }
            else
            {
                JsonValue p = c.Get("picture");
                cells = GoldenScenarios.Ints(p.Get("cells"));
                (w, h, colors) = ((int)p.Get("width").Number, (int)p.Get("height").Number, cells.Max() + 1);
            }
            var board = new Board(w, h, cells, colors, edges);
            foreach (int cell in GoldenScenarios.Ints(c.Get("eaten"))) board.Eat(cell);
            foreach (int cell in GoldenScenarios.Ints(c.Get("claimed"))) board.Claim(cell);
            return board;
        }

        [Test]
        public void TheRampAndRoadConstantsAreMainTs()
        {
            JsonValue t = Doc.Get("tunnel");
            Assert.That((SimConstants.TunnelX, SimConstants.TunnelY, SimConstants.TunnelWidth, SimConstants.TunnelHeight, RoadLayout.TunnelStartY),
                Is.EqualTo((t.Get("x").Number, t.Get("y").Number, t.Get("w").Number, t.Get("h").Number, t.Get("startY").Number)));
            CollectionAssert.AreEqual(Doc.Get("rampLanes").Items.Select(v => v.Number), RoadLayout.LaneOffsets);
            Assert.That(RoadLayout.RoadGap, Is.EqualTo(Doc.Get("roadGap").Number));
            Assert.That(Doc.Get("cases").Items.Count, Is.EqualTo(56));
        }

        [TestCaseSource(nameof(CaseNames))]
        public void RoutesAreTheSameAsTypeScript(string name)
        {
            JsonValue c = Doc.Get("cases").Items.Single(x => x.Get("name").String == name);
            Board board = BoardOf(c);
            JsonValue g = c.Get("geometry");
            var (cs, fx, fy, ring) = RoadLayout.Geometry(board.Width, board.Height);
            double[] r = g.Get("ring").Items.Select(v => v.Number).ToArray();
            Assert.That((cs, fx, fy), Is.EqualTo((g.Get("cellSize").Number, g.Get("fieldX").Number, g.Get("fieldY").Number)), "cell size and field origin");
            Assert.That((ring.X0, ring.Y0, ring.X1, ring.Y1), Is.EqualTo((r[0], r[1], r[2], r[3])), "ring");
            int reachable = Enumerable.Range(0, board.ColorCount).Sum(board.ReachableCount);
            Assert.That(reachable, Is.EqualTo((int)c.Get("reachableCount").Number), "the board state was rebuilt");

            RoadPathPlanner planner = RoadPathPlanner.ForBoard(board);
            var path = new List<PointD>();
            foreach (JsonValue t in c.Get("targets").Items)
            {
                int cell = (int)t.Get("cell").Number;
                string where = $"{name} cell {cell}";
                double[] costs = t.Get("costs").Items.Select(v => v.Number).ToArray();
                for (int k = 0; k < RoadLayout.LaneCount; k++)
                    Assert.That(planner.RouteFrom(cell, RoadLayout.LaneEntry(ring, k), path), Is.EqualTo(costs[k]), $"{where}: cost from lane {k}");
                TruckRoute truck = planner.PlanTruck(cell);
                Assert.That(truck.Lane, Is.EqualTo((int)t.Get("lane").Number), where + ": lane");
                double[] expected = t.Get("route").Items.Select(v => v.Number).ToArray();
                double[] got = truck.Out.SelectMany(p => new[] { p.X, p.Y }).ToArray();
                Assert.That(got, Is.EqualTo(expected), where + ": route (x0, y0, x1, y1, ...)");
            }
        }
    }
}
