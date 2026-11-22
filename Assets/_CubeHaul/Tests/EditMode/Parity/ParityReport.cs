using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CubeHaul.Tests.EditMode.Parity
{
    /// <summary>
    /// The parity report: one line per scenario, and for each one that differs, the first step where it does, the call
    /// made there and the one before, the first differing field with its expected (TypeScript) and actual (C#) value,
    /// and the other fields that differ at that step. Written to Logs/cubehaul-parity-report.txt.
    /// </summary>
    static class ParityReport
    {
        public const string File = "Logs/cubehaul-parity-report.txt";
        const int MaxValue = 400;

        public static string Format(IReadOnlyList<ParityResult> results)
        {
            var sb = new StringBuilder();
            int failed = results.Count(r => !r.Ok);
            sb.Append("CubeHaul TypeScript/C# parity: ").Append(results.Count).Append(" scenarios, ")
              .Append(results.Sum(r => r.Scenario.Steps.Count)).Append(" steps, ")
              .Append(failed == 0 ? "no differences" : failed + " with differences").Append('\n');
            sb.Append('\n').Append(string.Format("{0,-44} {1,-9} {2,-11} {3,-16} {4,6}  {5,-8} {6}\n", "scenario", "tray", "edges/slots", "policy", "steps", "outcome", "result"));
            foreach (ParityResult r in results)
            {
                ParityScenario s = r.Scenario;
                string verdict = r.Ok ? "same" : (r.Divergence.Step < 0 ? "DIFFERS at setup" : "DIFFERS at step " + r.Divergence.Step);
                sb.Append(string.Format("{0,-44} {1,-9} {2,-11} {3,-16} {4,6}  {5,-8} {6}\n",
                    s.Name, s.TraySource, s.Edges.ToString().ToLowerInvariant() + "/" + s.SlotCount, s.Policy, s.Steps.Count, s.Outcome, verdict));
            }
            foreach (ParityResult r in results.Where(x => !x.Ok)) AppendDivergence(sb, r);
            return sb.ToString();
        }

        public static void AppendDivergence(StringBuilder sb, ParityResult r)
        {
            ParityDivergence d = r.Divergence;
            sb.Append('\n').Append(r.Scenario.Name).Append(" (").Append(r.Scenario.File).Append("): ");
            sb.Append(d.Step < 0 ? "differs while setting up" : $"first difference at step {d.Step} of {r.Scenario.Steps.Count}").Append('\n');
            sb.Append("  call:      ").Append(d.Call).Append('\n');
            sb.Append("  after:     ").Append(d.PreviousCall).Append(" (").Append(r.StepsMatched).Append(" steps matched)\n");
            sb.Append("  field:     ").Append(d.Field).Append('\n');
            sb.Append("  expected:  ").Append(Clip(d.Expected)).Append("   (TypeScript)\n");
            sb.Append("  actual:    ").Append(Clip(d.Actual)).Append("   (C#)\n");
            if (d.AlsoDifferent.Count > 0) sb.Append("  also different: ").Append(string.Join(", ", d.AlsoDifferent)).Append('\n');
        }

        public static string Describe(ParityResult r)
        {
            if (r.Ok) return r.Scenario.Name + ": same as TypeScript in all " + r.Scenario.Steps.Count + " steps";
            var sb = new StringBuilder();
            AppendDivergence(sb, r);
            return sb.ToString();
        }

        /// <summary>Writes the report under the project root and returns its full path.</summary>
        public static string Write(string report)
        {
            string path = ProjectPaths.Of(File);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, report, new UTF8Encoding(false));
            return path;
        }

        static string Clip(string s) => s.Length <= MaxValue ? s : s.Substring(0, MaxValue) + $"… ({s.Length} chars)";
    }
}
