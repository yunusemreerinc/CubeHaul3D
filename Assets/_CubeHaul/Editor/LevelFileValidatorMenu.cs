using System.IO;
using CubeHaul.Levels;
using UnityEditor;
using UnityEngine;

namespace CubeHaul.EditorTools
{
    /// <summary>CubeHaul > Validate Level Files: checks every level file with the same validator the game uses.</summary>
    public static class LevelFileValidatorMenu
    {
        const string LevelsFolder = "Assets/_CubeHaul/Levels/Content";

        [MenuItem("CubeHaul/Validate Level Files")]
        public static void ValidateAll()
        {
            string[] files = Directory.GetFiles(LevelsFolder, "*.level.json");
            int failed = 0;
            foreach (string file in files)
            {
                LevelReadResult result = LevelDefinitionReader.Read(File.ReadAllText(file));
                foreach (LevelIssue error in result.Errors) Debug.LogError(file + ": " + error);
                foreach (LevelIssue warning in result.Warnings) Debug.LogWarning(file + ": " + warning);
                if (!result.Ok) failed++;
            }
            if (failed == 0) Debug.Log("CubeHaul: all " + files.Length + " level files are valid.");
            else Debug.LogError("CubeHaul: " + failed + " of " + files.Length + " level files have errors.");
        }
    }
}
