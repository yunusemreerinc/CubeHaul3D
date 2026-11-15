using CubeHaul.Gameplay;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>Example EditMode test: the timing constants match cube-hunt's main.ts (docs/TS_BASELINE.md §3.5).</summary>
    public class SimConstantsTests
    {
        [Test]
        public void TrucksOfOneCrateLeave0Point15SecondsApart()
        {
            Assert.That(SimConstants.PopInterval, Is.EqualTo(0.15).Within(1e-12));
        }

        [Test]
        public void ACrateFliesIntoItsSlotIn1Over3Point8Seconds()
        {
            Assert.That(SimConstants.CrateFlightTime, Is.EqualTo(1 / 3.8).Within(1e-12));
        }

        [Test]
        public void TheRampHasThreeLanes()
        {
            CollectionAssert.AreEqual(new[] { -34.0, 0.0, 34.0 }, SimConstants.RampLanes);
            Assert.That(SimConstants.MaxSlots, Is.EqualTo(6));
        }
    }
}
