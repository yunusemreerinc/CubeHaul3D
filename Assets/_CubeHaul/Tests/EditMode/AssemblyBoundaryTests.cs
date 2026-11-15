using System;
using System.Linq;
using System.Reflection;
using CubeHaul.Domain;
using CubeHaul.Gameplay;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>The rules stay engine-free: Domain, Levels and Gameplay must not reference any Unity assembly.</summary>
    public class AssemblyBoundaryTests
    {
        static readonly Type[] PureTypes = { typeof(Cells), typeof(LevelDefinition), typeof(SimConstants) };

        [TestCase(0, "CubeHaul.Domain")]
        [TestCase(1, "CubeHaul.Levels")]
        [TestCase(2, "CubeHaul.Gameplay")]
        public void PureAssemblyHasTheExpectedNameAndNoUnityReference(int index, string name)
        {
            Assembly assembly = PureTypes[index].Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo(name));
            string[] unity = assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("UnityEngine", StringComparison.Ordinal) || n.StartsWith("UnityEditor", StringComparison.Ordinal) || n.StartsWith("Unity.", StringComparison.Ordinal))
                .ToArray();
            CollectionAssert.IsEmpty(unity, name + " references " + string.Join(", ", unity));
        }

        [Test]
        public void DependenciesPointInwardOnly()
        {
            string[] Refs(Type t) => t.Assembly.GetReferencedAssemblies().Select(a => a.Name).Where(n => n.StartsWith("CubeHaul.", StringComparison.Ordinal)).ToArray();
            CollectionAssert.IsEmpty(Refs(typeof(Cells)), "Domain depends on nothing of ours");
            CollectionAssert.IsSubsetOf(Refs(typeof(LevelDefinition)), new[] { "CubeHaul.Domain" });
            CollectionAssert.IsSubsetOf(Refs(typeof(SimConstants)), new[] { "CubeHaul.Domain", "CubeHaul.Levels" });
        }
    }
}
