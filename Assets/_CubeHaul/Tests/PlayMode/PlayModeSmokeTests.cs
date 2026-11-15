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
    }
}
