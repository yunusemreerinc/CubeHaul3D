using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.Project
{
    /// <summary>
    /// The Unity project's structure, checked without Unity (docs/UNITY_PROJECT.md): folder layout, assembly
    /// definitions and their dependency rules, engine-free layers, packages, editor version and .meta files.
    /// </summary>
    public class UnityProjectTests
    {
        static readonly string Repo = FindRepo();
        static readonly string Root = Path.Combine(Repo, "Assets", "_CubeHaul");

        static string FindRepo()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "_CubeHaul"))) return dir.FullName;
            throw new InvalidOperationException("Assets/_CubeHaul not found");
        }

        static string Rel(string path) => Path.GetRelativePath(Repo, path).Replace('\\', '/');

        sealed class Asmdef
        {
            public string Path, Name;
            public string[] References, IncludePlatforms, DefineConstraints, Precompiled;
            public bool NoEngine, AutoReferenced, OverrideReferences;
            public string Folder => System.IO.Path.GetDirectoryName(Path);
        }

        static readonly List<Asmdef> Asmdefs = Directory.GetFiles(Path.Combine(Repo, "Assets"), "*.asmdef", SearchOption.AllDirectories)
            .Select(p =>
            {
                JsonValue j = JsonParser.Parse(File.ReadAllText(p));
                string[] Strings(string key) => j.Has(key) ? j.Get(key).Items.Select(x => x.String).ToArray() : new string[0];
                bool Flag(string key, bool fallback) => j.Has(key) ? j.Get(key).Boolean : fallback;
                return new Asmdef
                {
                    Path = p,
                    Name = j.Get("name").String,
                    References = Strings("references"),
                    IncludePlatforms = Strings("includePlatforms"),
                    DefineConstraints = Strings("defineConstraints"),
                    Precompiled = Strings("precompiledReferences"),
                    NoEngine = Flag("noEngineReferences", false),
                    AutoReferenced = Flag("autoReferenced", true),
                    OverrideReferences = Flag("overrideReferences", false),
                };
            })
            .ToList();

        static Asmdef A(string name) => Asmdefs.Single(a => a.Name == name);

        /// <summary>Which of our assemblies each one may reference: dependencies point inward only.</summary>
        static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            ["CubeHaul.Domain"] = new string[0],
            ["CubeHaul.Levels"] = new[] { "CubeHaul.Domain" },
            ["CubeHaul.Gameplay"] = new[] { "CubeHaul.Domain", "CubeHaul.Levels" },
            ["CubeHaul.Presentation"] = new[] { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay" },
            ["CubeHaul.UI"] = new[] { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay", "CubeHaul.Presentation" },
            ["CubeHaul.Editor"] = new[] { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay", "CubeHaul.Presentation", "CubeHaul.UI" },
            ["CubeHaul.Tests.EditMode"] = new[] { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay" },
            ["CubeHaul.Tests.PlayMode"] = new[] { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay", "CubeHaul.Presentation", "CubeHaul.UI" },
        };

        /// <summary>Unity and package assemblies our asmdefs may name (they come with Packages/manifest.json).</summary>
        static readonly string[] UnityAssemblies =
        {
            "UnityEngine.TestRunner", "UnityEditor.TestRunner", "UnityEngine.UI", "Unity.TextMeshPro", "Unity.InputSystem",
            "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime",
        };

        static readonly string[] Pure = { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay" };

        [TestCase("Domain")]
        [TestCase("Levels")]
        [TestCase("Gameplay")]
        [TestCase("Presentation")]
        [TestCase("UI")]
        [TestCase("Editor")]
        [TestCase("Tests")]
        [TestCase("Art")]
        [TestCase("Audio")]
        public void LayerFolderExists(string folder)
        {
            Assert.That(Directory.Exists(Path.Combine(Root, folder)), Is.True, "Assets/_CubeHaul/" + folder);
        }

        [Test]
        public void EveryAssemblyIsKnownNamedAfterItsFileAndHasCode()
        {
            CollectionAssert.AreEquivalent(Allowed.Keys, Asmdefs.Select(a => a.Name));
            foreach (Asmdef a in Asmdefs)
            {
                Assert.That(Path.GetFileName(a.Path), Is.EqualTo(a.Name + ".asmdef"));
                Assert.That(Directory.GetFiles(a.Folder, "*.cs", SearchOption.AllDirectories), Is.Not.Empty, a.Name + " has no scripts (Unity would not build it)");
            }
        }

        [Test]
        public void ReferencesPointInwardOnlyAndNameRealAssemblies()
        {
            foreach (Asmdef a in Asmdefs)
            {
                foreach (string r in a.References)
                {
                    if (r.StartsWith("CubeHaul.", StringComparison.Ordinal))
                        Assert.That(Allowed[a.Name], Does.Contain(r), $"{a.Name} must not reference {r}");
                    else
                        Assert.That(UnityAssemblies, Does.Contain(r), $"{a.Name} references unknown assembly {r}");
                }
            }
        }

        [TestCase("CubeHaul.Domain")]
        [TestCase("CubeHaul.Levels")]
        [TestCase("CubeHaul.Gameplay")]
        public void PureLayerCannotSeeTheEngine(string name)
        {
            Asmdef a = A(name);
            Assert.That(a.NoEngine, Is.True, "noEngineReferences");
            Assert.That(a.References.Where(r => !r.StartsWith("CubeHaul.", StringComparison.Ordinal)), Is.Empty);
            var engine = new Regex(@"^\s*using\s+(static\s+)?(UnityEngine|UnityEditor|Unity\.)", RegexOptions.Multiline);
            foreach (string cs in Directory.GetFiles(a.Folder, "*.cs", SearchOption.AllDirectories))
                Assert.That(engine.IsMatch(File.ReadAllText(cs)), Is.False, Rel(cs) + " uses the engine");
        }

        [Test]
        public void EditorCodeOnlyBuildsForTheEditor()
        {
            CollectionAssert.AreEqual(new[] { "Editor" }, A("CubeHaul.Editor").IncludePlatforms);
            CollectionAssert.AreEqual(new[] { "Editor" }, A("CubeHaul.Tests.EditMode").IncludePlatforms);
            CollectionAssert.IsEmpty(A("CubeHaul.Tests.PlayMode").IncludePlatforms, "PlayMode tests run on any platform");
            foreach (string runtime in new[] { "CubeHaul.Domain", "CubeHaul.Levels", "CubeHaul.Gameplay", "CubeHaul.Presentation", "CubeHaul.UI" })
                CollectionAssert.IsEmpty(A(runtime).IncludePlatforms, runtime);
        }

        [TestCase("CubeHaul.Tests.EditMode")]
        [TestCase("CubeHaul.Tests.PlayMode")]
        public void TestAssemblyFollowsTheTestFrameworkTemplate(string name)
        {
            Asmdef a = A(name);
            Assert.That(a.References, Does.Contain("UnityEngine.TestRunner"));
            Assert.That(a.References, Does.Contain("UnityEditor.TestRunner"));
            Assert.That(a.OverrideReferences, Is.True);
            CollectionAssert.AreEqual(new[] { "nunit.framework.dll" }, a.Precompiled);
            CollectionAssert.AreEqual(new[] { "UNITY_INCLUDE_TESTS" }, a.DefineConstraints);
            Assert.That(a.AutoReferenced, Is.False, "tests are not referenced by game code");
        }

        [Test]
        public void TheDotnetProjectsMirrorThePureAssemblies()
        {
            // Tools/dotnet builds Domain, Levels and Gameplay without Unity; its project references must be the asmdef's.
            foreach (string name in Pure)
            {
                string csproj = Path.Combine(Repo, "Tools", "dotnet", name, name + ".csproj");
                XDocument doc = XDocument.Load(csproj);
                string[] refs = doc.Descendants("ProjectReference")
                    .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include").Value))
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                CollectionAssert.AreEqual(A(name).References.OrderBy(x => x, StringComparer.Ordinal), refs, name);
                Assert.That(doc.Descendants("TargetFramework").Single().Value, Is.EqualTo("netstandard2.1"));
            }
        }

        [Test]
        public void PackagesArePinnedForUnity6Point3()
        {
            JsonValue deps = JsonParser.Parse(File.ReadAllText(Path.Combine(Repo, "Packages", "manifest.json"))).Get("dependencies");
            foreach (string required in new[] { "com.unity.render-pipelines.universal", "com.unity.test-framework", "com.unity.ugui", "com.unity.inputsystem" })
                Assert.That(deps.Has(required), Is.True, required);
            var semver = new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$");
            foreach (KeyValuePair<string, JsonValue> p in deps.Members)
                Assert.That(semver.IsMatch(p.Value.String), Is.True, $"{p.Key}: \"{p.Value.String}\" is not an exact version");
            Assert.That(deps.Get("com.unity.render-pipelines.universal").String, Does.StartWith("17.3."), "URP 17.3 ships with Unity 6.3");

            string version = File.ReadAllText(Path.Combine(Repo, "ProjectSettings", "ProjectVersion.txt"));
            Assert.That(version, Does.Match(@"m_EditorVersion: 6000\.3\.\d+f\d+"));
        }

        static string Setting(string file, string key)
        {
            Match m = new Regex(@"^\s*" + Regex.Escape(key) + @": (.*)$", RegexOptions.Multiline).Match(File.ReadAllText(Path.Combine(Repo, "ProjectSettings", file)));
            Assert.That(m.Success, Is.True, key + " not found in " + file);
            return m.Groups[1].Value.Trim();
        }

        static string GuidOf(string assetPath)
        {
            Match m = new Regex(@"^guid: ([0-9a-f]{32})$", RegexOptions.Multiline).Match(File.ReadAllText(Path.Combine(Repo, assetPath + ".meta")));
            Assert.That(m.Success, Is.True, assetPath);
            return m.Groups[1].Value;
        }

        [Test]
        public void PlayerSettingsFitAPortraitMobileGame()
        {
            Assert.That(Setting("ProjectSettings.asset", "activeInputHandler"), Is.EqualTo("1"), "Input System package (new) only");
            Assert.That(Setting("ProjectSettings.asset", "m_ActiveColorSpace"), Is.EqualTo("1"), "Linear color space (R-16)");
            Assert.That(Setting("ProjectSettings.asset", "defaultScreenOrientation"), Is.EqualTo("0"), "Portrait");
            Assert.That(Setting("ProjectSettings.asset", "productName"), Is.EqualTo("CubeHaul3D"));
        }

        [Test]
        public void UrpIsTheActivePipelineOnEveryQualityLevel()
        {
            const string rendering = "Assets/_CubeHaul/Art/Rendering/";
            string urp = GuidOf(rendering + "CubeHaul_URP.asset");
            StringAssert.Contains("guid: " + urp, Setting("GraphicsSettings.asset", "m_CustomRenderPipeline"));
            string[] levels = Regex.Matches(File.ReadAllText(Path.Combine(Repo, "ProjectSettings", "QualitySettings.asset")), @"customRenderPipeline: (.*)")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.That(levels, Is.Not.Empty);
            foreach (string level in levels) StringAssert.Contains("guid: " + urp, level);
            StringAssert.Contains("guid: " + GuidOf(rendering + "UniversalRenderPipelineGlobalSettings.asset"),
                Setting("GraphicsSettings.asset", "UnityEngine.Rendering.Universal.UniversalRenderPipeline"));
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.Combine(Repo, "Assets"), "*.asset"), "rendering assets live in " + rendering);
        }

        static string Guid(string metaPath) =>
            Regex.Match(File.ReadAllText(metaPath), @"^guid: ([0-9a-f]{32})\s*$", RegexOptions.Multiline).Groups[1].Value;

        /// <summary>The scene the Editor builder makes (docs/SCENE_LAYOUT.md §4), once it is committed.</summary>
        [Test]
        public void TheGameSceneIsFirstInTheBuild()
        {
            string scene = Path.Combine(Root, "Scenes", "GameScene.unity");
            if (!File.Exists(scene)) Assert.Ignore("GameScene.unity is not committed yet");
            string settings = File.ReadAllText(Path.Combine(Repo, "ProjectSettings", "EditorBuildSettings.asset"));
            Match first = Regex.Match(settings, @"m_Scenes:\s*\n\s*- enabled: (\d)\s*\n\s*path: (\S+)\s*\n\s*guid: ([0-9a-f]{32})");
            Assert.That(first.Success, Is.True, "EditorBuildSettings.asset lists no scene; open GameScene and save the project, or run CubeHaul > Setup > Create Game Scene");
            Assert.That((first.Groups[1].Value, first.Groups[2].Value), Is.EqualTo(("1", "Assets/_CubeHaul/Scenes/GameScene.unity")));
            Assert.That(first.Groups[3].Value, Is.EqualTo(Guid(scene + ".meta")), "the build list points at GameScene.unity's guid");
        }

        [Test]
        public void TheGameSceneHoldsTheCompositionAndNoPreviewGeometry()
        {
            string scene = Path.Combine(Root, "Scenes", "GameScene.unity");
            if (!File.Exists(scene)) Assert.Ignore("GameScene.unity is not committed yet");
            string text = File.ReadAllText(scene);
            string[] names = Regex.Matches(text, @"^\s*m_Name: (.+)$", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToArray();
            foreach (string expected in new[] { "Game", "Main Camera", "Directional Light", "World", "Layout Preview", "UI", "Safe Area", "Top Bar", "Bottom Bar", "Add Slot", "EventSystem" })
                CollectionAssert.Contains(names, expected);
            foreach (string generated in new[] { "Board", "Field", "Ramp", "Slot 0", "Crate 0,0" })
                CollectionAssert.DoesNotContain(names, generated, "LayoutPreview geometry is built at runtime, never saved");
            string library = Path.Combine(Root, "Levels", "LevelLibrary.asset");
            StringAssert.Contains("library: {fileID: 11400000, guid: " + Guid(library + ".meta"), text, "the preview uses LevelLibrary.asset");
            StringAssert.Contains("m_ReferenceResolution: {x: 420, y: 900}", text);
            StringAssert.Contains("m_ScreenMatchMode: 1", text, "CanvasScaler Expand");
        }

        [Test]
        public void TheLevelLibraryListsEveryLevelFileInPlayOrder()
        {
            string library = Path.Combine(Root, "Levels", "LevelLibrary.asset");
            if (!File.Exists(library)) Assert.Ignore("LevelLibrary.asset is not committed yet");
            string[] listed = Regex.Matches(File.ReadAllText(library), @"- \{fileID: 4900000, guid: ([0-9a-f]{32}), type: 3\}").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            string[] files = Directory.GetFiles(Path.Combine(Root, "Levels", "Content"), "*.level.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(files.Select(f => Guid(f + ".meta")), listed);
        }

        /// <summary>
        /// Unity runs the tests with its own NUnit (com.unity.ext.nunit, NUnit 3.5), which ignores .Within() on a tuple
        /// and compares it exactly; NUnit 3.14 here applies the tolerance to each item. Such a test passes here and fails
        /// in Unity, so tolerances are written per value.
        /// </summary>
        [Test]
        public void TestsDoNotPutAToleranceOnATuple()
        {
            var tupleWithin = new Regex(@"Is\.EqualTo\(\([^;]*,[^;]*\)\)\s*\.Within", RegexOptions.Multiline);
            foreach (string file in Directory.GetFiles(Path.Combine(Root, "Tests"), "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int k = 0; k < lines.Length; k++)
                    Assert.That(tupleWithin.IsMatch(lines[k]), Is.False, $"{file}:{k + 1}: compare the values one by one with .Within");
            }
        }

        [Test]
        public void AssetsAreTextWithVisibleMetaFiles()
        {
            Assert.That(Setting("EditorSettings.asset", "m_SerializationMode"), Is.EqualTo("2"), "Force Text");
            Assert.That(Setting("VersionControlSettings.asset", "m_Mode"), Is.EqualTo("Visible Meta Files"));
            Assert.That(Setting("EditorSettings.asset", "m_LineEndingsForNewScripts"), Is.EqualTo("1"), "LF, as .gitattributes");
        }

        [Test]
        public void EveryAssetHasAMetaWithAUniqueGuid()
        {
            var guids = new Dictionary<string, string>(StringComparer.Ordinal);
            var guidLine = new Regex(@"^guid: ([0-9a-f]{32})$", RegexOptions.Multiline);
            bool Hidden(string p) => Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal) || Path.GetFileName(p).EndsWith("~", StringComparison.Ordinal);
            foreach (string entry in Directory.EnumerateFileSystemEntries(Path.Combine(Repo, "Assets"), "*", SearchOption.AllDirectories))
            {
                if (Hidden(entry) || entry.Split(Path.DirectorySeparatorChar).Any(s => s.StartsWith(".", StringComparison.Ordinal))) continue;
                if (entry.EndsWith(".meta", StringComparison.Ordinal))
                {
                    Assert.That(File.Exists(entry[..^5]) || Directory.Exists(entry[..^5]), Is.True, "orphaned " + Rel(entry));
                    Match m = guidLine.Match(File.ReadAllText(entry));
                    Assert.That(m.Success, Is.True, "no guid in " + Rel(entry));
                    Assert.That(guids.ContainsKey(m.Groups[1].Value), Is.False, $"{Rel(entry)} reuses the guid of {(guids.TryGetValue(m.Groups[1].Value, out string other) ? other : "")}");
                    guids[m.Groups[1].Value] = Rel(entry);
                    continue;
                }
                Assert.That(File.Exists(entry + ".meta"), Is.True, "missing " + Rel(entry) + ".meta");
            }
        }
    }
}
