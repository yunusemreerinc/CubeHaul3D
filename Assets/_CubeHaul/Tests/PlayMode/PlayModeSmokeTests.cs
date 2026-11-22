using System.Collections;
using CubeHaul.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CubeHaul.Tests.PlayMode
{
    /// <summary>Example PlayMode tests: the engine runs frames, and design space maps to the world as planned.</summary>
    public class PlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator FramesAdvanceInPlayMode()
        {
            var probe = new GameObject("CubeHaul probe");
            int start = Time.frameCount;
            yield return null;
            yield return null;
            Assert.That(Time.frameCount, Is.GreaterThan(start));
            Object.Destroy(probe);
        }

        [Test]
        public void WorldMapperPutsTheRampSideTowardsTheCamera()
        {
            Vector3 p = WorldMapper.ToWorld(10, 20, 0.5f);
            Assert.That(p.x, Is.EqualTo(5f));
            Assert.That(p.y, Is.EqualTo(0f));
            Assert.That(p.z, Is.EqualTo(-10f));
        }

        [Test]
        public void FromDesignUsesUnitsPerDesignWhateverTheTypeOfTheHeight()
        {
            // A float height once bound to ToWorld's unitsPerPixel overload and put the layout preview 40 times too far.
            float floatHeight = 2f;
            Vector3 a = WorldMapper.FromDesign(210, 258, floatHeight);
            Vector3 b = WorldMapper.FromDesign(210.0, 258.0, 2.0);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.x, Is.EqualTo(210 * WorldMapper.UnitsPerDesign).Within(1e-5f));
            Assert.That(a.y, Is.EqualTo(2 * WorldMapper.UnitsPerDesign).Within(1e-5f));
            Assert.That(a.z, Is.EqualTo(-258 * WorldMapper.UnitsPerDesign).Within(1e-5f));
        }
    }
}
