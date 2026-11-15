using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CubeHaul.Levels
{
    /// <summary>A level file's name (for messages) and its JSON text. In Unity this comes from a TextAsset.</summary>
    public readonly struct LevelSource
    {
        public string Name { get; }
        public string Text { get; }

        public LevelSource(string name, string text)
        {
            Name = name;
            Text = text;
        }
    }

    /// <summary>A level file that could not be loaded, and why.</summary>
    public sealed class LevelLoadError
    {
        public string SourceName { get; }
        public IReadOnlyList<LevelIssue> Issues { get; }

        public LevelLoadError(string sourceName, IReadOnlyList<LevelIssue> issues)
        {
            SourceName = sourceName;
            Issues = issues;
        }

        public override string ToString()
        {
            var sb = new StringBuilder(SourceName).Append(':');
            foreach (LevelIssue i in Issues)
                sb.Append("\n  - ").Append(i.Code).Append(" at ").Append(i.Path.Length == 0 ? "(root)" : i.Path).Append(": ").Append(i.Message);
            return sb.ToString();
        }
    }

    public sealed class LevelLoadException : Exception
    {
        public IReadOnlyList<LevelLoadError> Errors { get; }

        public LevelLoadException(IReadOnlyList<LevelLoadError> errors)
            : base(Describe(errors))
        {
            Errors = errors;
        }

        static string Describe(IReadOnlyList<LevelLoadError> errors)
        {
            var sb = new StringBuilder();
            sb.Append(errors.Count == 1 ? "1 level file could not be loaded:" : errors.Count + " level files could not be loaded:");
            foreach (LevelLoadError e in errors) sb.Append('\n').Append(e);
            return sb.ToString();
        }
    }

    /// <summary>
    /// The game's levels, in order. Every file is read and validated once, when the repository is built; files with
    /// errors are left out and listed in <see cref="Errors"/>. The loaded levels are kept private: every Get returns a
    /// new <see cref="LevelDefinition.DeepCopy"/>, so whatever the caller changes (a game spending trucks, an editor
    /// tool) never reaches the repository or anyone else's copy.
    /// </summary>
    public sealed class LevelRepository
    {
        readonly List<LevelDefinition> levels = new List<LevelDefinition>();
        readonly Dictionary<string, int> indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<LevelLoadError> errors = new List<LevelLoadError>();
        readonly List<string> ids = new List<string>();

        public int Count => levels.Count;
        /// <summary>Level ids in play order.</summary>
        public IReadOnlyList<string> LevelIds => ids;
        /// <summary>Files that were left out, with their issues. Empty when everything loaded.</summary>
        public IReadOnlyList<LevelLoadError> Errors => errors;
        public bool HasErrors => errors.Count > 0;

        LevelRepository() { }

        /// <summary>Loads the sources in the order given (that order is the play order).</summary>
        public static LevelRepository Load(IEnumerable<LevelSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var repo = new LevelRepository();
            foreach (LevelSource source in sources) repo.Add(source);
            return repo;
        }

        /// <summary>
        /// Loads every file matching the pattern in a folder, in ordinal file-name order (level-01-…, level-02-…).
        /// For tools, tests and the editor; on devices levels come from TextAssets through <see cref="Load"/>.
        /// </summary>
        public static LevelRepository LoadDirectory(string directory, string searchPattern = "*.level.json")
        {
            string[] files = Directory.GetFiles(directory, searchPattern);
            Array.Sort(files, StringComparer.Ordinal);
            var sources = new List<LevelSource>();
            foreach (string file in files) sources.Add(new LevelSource(Path.GetFileName(file), File.ReadAllText(file, Encoding.UTF8)));
            return Load(sources);
        }

        /// <summary>Throws a <see cref="LevelLoadException"/> describing every failed file, if any failed.</summary>
        public LevelRepository ThrowIfErrors()
        {
            if (errors.Count > 0) throw new LevelLoadException(errors);
            return this;
        }

        /// <summary>A copy of the level at this play index (0-based).</summary>
        public LevelDefinition GetAt(int index)
        {
            if (index < 0 || index >= levels.Count) throw new ArgumentOutOfRangeException(nameof(index), $"there are {levels.Count} levels");
            return levels[index].DeepCopy();
        }

        /// <summary>A copy of the level with this id.</summary>
        public LevelDefinition Get(string levelId)
        {
            if (TryGet(levelId, out LevelDefinition level)) return level;
            throw new KeyNotFoundException($"no level \"{levelId}\" (known: {string.Join(", ", ids)})");
        }

        public bool TryGet(string levelId, out LevelDefinition level)
        {
            if (levelId != null && indexById.TryGetValue(levelId, out int i))
            {
                level = levels[i].DeepCopy();
                return true;
            }
            level = null;
            return false;
        }

        /// <summary>Play index of a level id, or -1.</summary>
        public int IndexOf(string levelId) => levelId != null && indexById.TryGetValue(levelId, out int i) ? i : -1;

        void Add(LevelSource source)
        {
            LevelReadResult result = LevelDefinitionReader.Read(source.Text);
            if (!result.Ok)
            {
                errors.Add(new LevelLoadError(source.Name, result.Errors));
                return;
            }
            LevelDefinition level = result.Level;
            if (indexById.TryGetValue(level.LevelId, out int first))
            {
                errors.Add(new LevelLoadError(source.Name, new[]
                {
                    new LevelIssue(IssueCodes.DuplicateLevelId, "/levelId", $"levelId \"{level.LevelId}\" is already used by level {first + 1}"),
                }));
                return;
            }
            indexById.Add(level.LevelId, levels.Count);
            ids.Add(level.LevelId);
            levels.Add(level);
        }
    }
}
