using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CubeHaul.Data.Json;

namespace CubeHaul.Data.Tests
{
    /// <summary>Locates the shared, language-neutral fixtures in LevelFormat/ (also used by the TypeScript tests).</summary>
    static class Fixtures
    {
        public static readonly string Root = FindRoot();

        static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "LevelFormat", "fixtures"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("LevelFormat/ not found above " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "LevelFormat");
        }

        public static string Read(params string[] parts) => File.ReadAllText(Path.Combine(Root, Path.Combine(parts)));

        /// <summary>Raw file text, BOM included (File.ReadAllText would strip it).</summary>
        public static string ReadRaw(params string[] parts)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Root, Path.Combine(parts)));
            return new System.Text.UTF8Encoding(false).GetString(bytes);
        }

        public static IEnumerable<string> Files(string dir) =>
            Directory.GetFiles(Path.Combine(Root, dir), "*.json")
                .Select(Path.GetFileName)
                .Where(f => f != "expected.json")
                .OrderBy(f => f, StringComparer.Ordinal);

        public static JsonValue Expected(string dir) => JsonParser.Parse(Read(dir, "expected.json"));

        /// <summary>"CODE path" strings, sorted, for order-independent comparison.</summary>
        public static List<string> Keys(IEnumerable<(string code, string path)> issues) =>
            issues.Select(i => i.code + " " + i.path).OrderBy(s => s, StringComparer.Ordinal).ToList();

        public static List<string> Keys(IEnumerable<LevelIssue> issues) => Keys(issues.Select(i => (i.Code, i.Path)));

        public static List<string> ExpectedKeys(JsonValue list) =>
            Keys(list.Items.Select(i => (i.Get("code").String, i.Get("path").String)));
    }
}
