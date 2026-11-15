using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;

using CubeHaul.Domain;
using CubeHaul.Levels;

namespace CubeHaul.Tests.LevelFormat
{
    /// <summary>PORTING_PLAN R-14: results must not depend on the device language (Turkish in particular).</summary>
    public class CultureTests
    {
        [TestCase("tr-TR")]
        [TestCase("de-DE")]
        [TestCase("ar-SA")]
        public void ResultsAreTheSameInAnyCulture(string culture)
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                string Summary()
                {
                    var parts = Fixtures.Files("examples").Concat(Fixtures.Files("fixtures/invalid")).Select(f =>
                    {
                        string dir = f.EndsWith(".level.json") ? "examples" : "fixtures/invalid";
                        LevelReadResult r = LevelDefinitionReader.Read(Fixtures.ReadRaw(dir, f));
                        // Formatted invariantly: this compares what was read, not how the test prints it.
                        string model = r.Level == null ? "" : FormattableString.Invariant($"{r.Level.Edges} {r.Level.Generation?.Shuffle:R} {r.Level.DifficultyMetadata?.WinRate:R} {r.Level.Picture.Cells.Sum()}");
                        return f + ": " + string.Join(",", Fixtures.Keys(r.Errors)) + " | " + model;
                    });
                    return string.Join("\n", parts);
                }

                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                string invariant = Summary();
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Assert.That(Summary(), Is.EqualTo(invariant));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Test]
        public void EdgesAreMatchedExactlyNotByLowercasing()
        {
            // In tr-TR "SIDES".ToLower() is "sıdes"; the reader never lowercases, it rejects anything but the exact word.
            Assert.That(OpenEdgesJson.TryParse("sides", out OpenEdges e), Is.True);
            Assert.That(e, Is.EqualTo(OpenEdges.Sides));
            Assert.That(OpenEdgesJson.TryParse("SIDES", out _), Is.False);
            Assert.That(OpenEdgesJson.TryParse("sıdes", out _), Is.False);
        }
    }
}
