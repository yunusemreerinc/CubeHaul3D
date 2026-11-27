using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Session;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The C# GameSession against main.ts's timing (golden sessions/*.json, G-11, docs/SESSION.md): whole levels played
    /// by the same deterministic policy at the same frame time. Every tap, every truck sent (slot and cell), every cube
    /// collected, every +1 Slot must happen in the same frame and order, and on sampled frames every truck must be at
    /// the same coordinates, to the last bit. The first difference is reported with the events around it.
    /// </summary>
    public class SessionGoldenTests
    {
        const string Dir = GoldenScenarios.GoldenDir + "/sessions";

        static IEnumerable<string> Runs() =>
            Directory.GetFiles(ProjectPaths.Of(Dir), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(f => f, StringComparer.Ordinal);

        internal static LevelDefinition LevelOf(JsonValue doc)
        {
            LevelReadResult r = LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of(doc.Get("levelFile").String), Encoding.UTF8));
            Assert.That(r.Ok, Is.True, r.Describe());
            return r.Level;
        }

        /// <summary>A run's log as recorded rows: frame (absolute), kind, a, b.</summary>
        internal sealed class Log
        {
            public readonly List<(int frame, int kind, int a, int b)> Rows = new List<(int, int, int, int)>();
            public int Frame;

            public void Attach(GameSession s)
            {
                s.CrateTapped += (col, slot) => Rows.Add((Frame, 0, col, slot));
                s.TruckSent += t => Rows.Add((Frame, 1, t.Slot, t.Cell));
                s.CubeCollected += t => Rows.Add((Frame, 2, t.Cell, -1));
                s.SlotAdded += () => Rows.Add((Frame, 3, -1, -1));
            }
        }

        internal static List<(int frame, int kind, int a, int b)> Expected(JsonValue doc)
        {
            var rows = new List<(int, int, int, int)>();
            int frame = 0;
            foreach (JsonValue r in doc.Get("events").Items)
            {
                int[] v = GoldenScenarios.Ints(r);
                frame += v[0];
                rows.Add((frame, v[1], v[2], v[3]));
            }
            return rows;
        }

        static readonly string[] Kinds = { "tap", "claim", "collect", "+1 slot" };

        static string Describe((int frame, int kind, int a, int b) r) =>
            r.kind switch
            {
                0 => $"frame {r.frame}: tap column {r.a} -> slot {r.b}",
                1 => $"frame {r.frame}: slot {r.a} sends a truck for cell {r.b}",
                2 => $"frame {r.frame}: collect cell {r.a}",
                _ => $"frame {r.frame}: {Kinds[Math.Min(3, r.kind)]}",
            };

        [Test]
        public void ThereAreRunsForEveryLevelAndEverySpeed()
        {
            string[] runs = Runs().ToArray();
            Assert.That(runs.Length, Is.EqualTo(18));
            for (int level = 1; level <= 10; level++)
                Assert.That(runs.Any(r => r.StartsWith($"s-{level:00}-") && r.EndsWith("-1x-60fps")), Is.True, $"level {level} at 1x");
            foreach (string speed in new[] { "1x", "2x", "3x" })
                Assert.That(runs.Count(r => r.Contains("-" + speed + "-")), Is.GreaterThanOrEqualTo(3), speed);
        }

        [TestCaseSource(nameof(Runs))]
        public void TheSessionPlaysLikeCubeHunt(string name)
        {
            JsonValue doc = GoldenScenarios.Json(Dir + "/" + name + ".json");
            LevelDefinition level = LevelOf(doc);
            double dt = doc.Get("dt").Number;
            int speed = GoldenScenarios.Int(doc.Get("speed"));
            Assert.That(dt, Is.EqualTo(1.0 / GoldenScenarios.Int(doc.Get("fps"))), "the frame time is 1 / fps as a double");

            var session = GameSession.For(level);
            session.SetSpeed(speed);
            var log = new Log();
            log.Attach(session);
            var poses = doc.Get("poses").Items.ToDictionary(p => GoldenScenarios.Int(p.Get("frame")));
            List<(int frame, int kind, int a, int b)> expected = Expected(doc);
            int maxOut = 0, checkedPoses = 0;

            AutoPlayOutcome outcome = AutoPlayOutcome.Playing;
            for (int f = 0; f < 400_000; f++)
            {
                log.Frame = f + 1;
                outcome = AutoPlayer.Act(session);
                if (outcome != AutoPlayOutcome.Playing) break;
                session.Advance(dt);
                maxOut = Math.Max(maxOut, session.TrucksOut);
                if (poses.TryGetValue(log.Frame, out JsonValue pose))
                {
                    ComparePoses(pose, session, name);
                    checkedPoses++;
                }
                // Fail at the first difference, not thousands of frames later.
                if ((f & 63) == 0 || log.Rows.Count > expected.Count) CompareLog(expected, log.Rows, name, partial: true);
            }

            CompareLog(expected, log.Rows, name, partial: false);
            Assert.That(checkedPoses, Is.EqualTo(poses.Count), "every sampled frame was reached");
            Assert.That(outcome.ToString().ToLowerInvariant(), Is.EqualTo(doc.Get("result").String), "result");
            Assert.That(session.Frames, Is.EqualTo(GoldenScenarios.Int(doc.Get("frames"))), "frames played");
            Assert.That(session.SimTime, Is.EqualTo(doc.Get("simTime").Number), "simulation time, to the bit");
            Assert.That(session.Game.Status.ToString().ToLowerInvariant(), Is.EqualTo(doc.Get("finalStatus").String));
            Assert.That(session.Game.Board.Remaining, Is.EqualTo(GoldenScenarios.Int(doc.Get("remaining"))));
            Assert.That(log.Rows.Count(r => r.kind == 1), Is.EqualTo(GoldenScenarios.Int(doc.Get("trucks"))), "trucks sent");
            Assert.That(maxOut, Is.EqualTo(GoldenScenarios.Int(doc.Get("maxTrucksOut"))), "most trucks out at once");
        }

        static void CompareLog(List<(int frame, int kind, int a, int b)> expected, List<(int frame, int kind, int a, int b)> actual, string name, bool partial)
        {
            int n = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < n; i++)
            {
                if (expected[i] == actual[i]) continue;
                var around = new StringBuilder();
                for (int k = Math.Max(0, i - 4); k <= Math.Min(n - 1, i + 2); k++)
                    around.Append(k == i ? "  > " : "    ").Append("cube-hunt ").Append(Describe(expected[k])).Append("   |   C# ").Append(Describe(actual[k])).Append('\n');
                Assert.Fail($"{name}: event {i} differs\n{around}");
            }
            if (partial) return;
            if (actual.Count != expected.Count)
            {
                string next = actual.Count < expected.Count ? "cube-hunt goes on with " + Describe(expected[n]) : "C# goes on with " + Describe(actual[n]);
                Assert.Fail($"{name}: {actual.Count} events, cube-hunt has {expected.Count}; {next}");
            }
        }

        static void ComparePoses(JsonValue pose, GameSession s, string name)
        {
            int frame = GoldenScenarios.Int(pose.Get("frame"));
            double[] v = pose.Get("trucks").Items.Select(x => x.Number).ToArray();
            IReadOnlyList<TruckMotion> road = s.Fleet.OnRoad;
            Assert.That(road.Count, Is.EqualTo(v.Length / 3), $"{name} frame {frame}: trucks on the road");
            for (int k = 0; k < road.Count; k++)
            {
                TruckMotion t = road[k];
                string at = $"{name} frame {frame}, truck {k} (cell {t.Cell})";
                Assert.That(t.Cell, Is.EqualTo((int)v[3 * k]), at + ": in the same order");
                Assert.That(t.X, Is.EqualTo(v[3 * k + 1]), at + ": x");
                Assert.That(t.Y, Is.EqualTo(v[3 * k + 2]), at + ": y");
            }
            List<JsonValue> lanes = pose.Get("ramp").Items.ToList();
            for (int lane = 0; lane < lanes.Count; lane++)
            {
                var queued = new List<int>();
                var e = s.Fleet.Queue(lane);
                while (e.MoveNext()) queued.Add(e.Current.Cell);
                CollectionAssert.AreEqual(GoldenScenarios.Ints(lanes[lane]), queued, $"{name} frame {frame}: ramp lane {lane} queue");
            }
        }
    }
}
