using System.IO;
using System.Text;
using CubeHaul.Levels.Generation;
using UnityEditor;
using UnityEngine;

namespace CubeHaul.EditorTools
{
    /// <summary>
    /// Pre-generation (docs/LEVEL_GENERATION.md §5): builds each level's tray from its generation settings in the
    /// Editor and writes it into the level file, so the game loads a static tray instead of building one at runtime.
    /// The result is exactly what the TypeScript game builds for the same settings.
    /// </summary>
    public static class LevelBakeMenu
    {
        const string LevelsFolder = "Assets/_CubeHaul/Levels/Content";

        [MenuItem("CubeHaul/Levels/Verify Baked Trays")]
        public static void Verify() => Run(write: false);

        [MenuItem("CubeHaul/Levels/Bake Trays From Generation Settings")]
        public static void Bake()
        {
            if (!EditorUtility.DisplayDialog("Bake level trays",
                    "Rebuild the tray and difficulty of every level file in " + LevelsFolder + " that has generation settings, and overwrite the files that differ?",
                    "Bake", "Cancel"))
                return;
            Run(write: true);
        }

        static void Run(bool write)
        {
            string[] files = Directory.GetFiles(LevelsFolder, "*.level.json");
            int changed = 0, skipped = 0, failed = 0, unverified = 0;
            try
            {
                for (int k = 0; k < files.Length; k++)
                {
                    string file = files[k].Replace('\\', '/');
                    if (EditorUtility.DisplayCancelableProgressBar(write ? "Baking level trays" : "Verifying level trays", file, (float)k / files.Length))
                    {
                        Debug.LogWarning("CubeHaul: stopped after " + k + " of " + files.Length + " level files.");
                        break;
                    }
                    BakeFileResult result = LevelBaker.BakeFile(File.ReadAllText(file, Encoding.UTF8));
                    if (result.ReadFailed)
                    {
                        failed++;
                        Debug.LogError(file + ": cannot be read\n" + result.Read.Describe());
                        continue;
                    }
                    if (!result.HasGeneration)
                    {
                        skipped++;
                        continue;
                    }
                    if (!result.Bake.Verified)
                    {
                        unverified++;
                        Debug.LogWarning(file + ": no tray could be verified as solvable; the unshuffled tray is used (as cube-hunt does).");
                    }
                    if (!result.Changed) continue;
                    changed++;
                    if (write)
                    {
                        File.WriteAllText(file, result.Text, new UTF8Encoding(false));
                        Debug.Log(file + ": tray and difficulty rebaked (win rate " + result.Bake.Build.WinRate + ").");
                    }
                    else Debug.LogWarning(file + ": its tray or difficulty is not what its generation settings give. Use CubeHaul > Levels > Bake Trays From Generation Settings.");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (write && changed > 0) AssetDatabase.Refresh();
            string summary = $"CubeHaul: {files.Length} level files, {changed} {(write ? "rebaked" : "out of date")}, {skipped} without generation settings, {failed} unreadable, {unverified} unverified.";
            if (failed > 0 || (!write && changed > 0)) Debug.LogError(summary);
            else Debug.Log(summary);
        }
    }
}
