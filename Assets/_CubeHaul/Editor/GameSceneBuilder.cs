using System.IO;
using System.Linq;
using CubeHaul.Presentation;
using CubeHaul.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubeHaul.EditorTools
{
    /// <summary>
    /// CubeHaul > Setup > Create Game Scene: builds Assets/_CubeHaul/Scenes/GameScene.unity with
    /// <see cref="GameSceneComposer"/>, a LevelLibrary asset holding the ten level files, and puts the scene first in
    /// the build. When the project opens without the scene, it is created once automatically (docs/SCENE_LAYOUT.md).
    /// </summary>
    [InitializeOnLoad]
    public static class GameSceneBuilder
    {
        public const string ScenePath = "Assets/_CubeHaul/Scenes/GameScene.unity";
        public const string LibraryPath = "Assets/_CubeHaul/Levels/LevelLibrary.asset";
        public const string CubeMaterialPath = "Assets/_CubeHaul/Art/Materials/Cube.mat";
        const string LevelsFolder = "Assets/_CubeHaul/Levels/Content";
        const string AutoKey = "CubeHaul.GameSceneBuilder.AutoChecked";

        static GameSceneBuilder()
        {
            if (Application.isBatchMode || SessionState.GetBool(AutoKey, false)) return;
            SessionState.SetBool(AutoKey, true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath)) return;
                Scene active = SceneManager.GetActiveScene();
                bool untitled = string.IsNullOrEmpty(active.path);
                if (untitled && active.isDirty)
                {
                    Debug.Log("CubeHaul: " + ScenePath + " is missing. Create it with CubeHaul > Setup > Create Game Scene.");
                    return;
                }
                Debug.Log("CubeHaul: " + ScenePath + " is missing; creating it (CubeHaul > Setup > Create Game Scene).");
                // An untitled, unchanged scene is replaced; otherwise the new scene is built beside the open one and closed.
                Build(openAfterwards: untitled);
            };
        }

        [MenuItem("CubeHaul/Setup/Create Game Scene")]
        public static void CreateFromMenu()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Create Game Scene", ScenePath + " exists. Build it again from scratch?", "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(openAfterwards: true);
        }

        [MenuItem("CubeHaul/Setup/Open Game Scene")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) CreateFromMenu();
            else if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        static void Build(bool openAfterwards)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, openAfterwards ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            // Only now: opening a scene unloads the assets no scene uses, and a LevelLibrary loaded before it would be a
            // destroyed object here (== null), leaving the placeholder title and the checkerboard preview.
            LevelLibrary library = EnsureLevelLibrary();
            if (library == null)
            {
                Debug.LogError("CubeHaul: could not load " + LibraryPath + "; the scene was not built.");
                return;
            }
            GameSceneComposer.Compose(library, EnsureCubeMaterial());
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!openAfterwards)
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            if (EditorBuildSettings.scenes.Length == 0 || EditorBuildSettings.scenes[0].path != ScenePath)
                Debug.LogWarning("CubeHaul: could not put " + ScenePath + " first in Build Settings; add it in File > Build Profiles > Scene List.");
            Debug.Log("CubeHaul: " + ScenePath + " created. Commit it with its .meta, ProjectSettings/EditorBuildSettings.asset, " + LibraryPath + " and " + CubeMaterialPath + ".");
        }

        /// <summary>The cubes' base material: URP Lit, GPU instancing on. The grid makes one copy per palette color.</summary>
        public static Material EnsureCubeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CubeMaterialPath);
            if (material != null) return material;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogWarning("CubeHaul: URP Lit shader not found; the cubes will pick a shader at runtime.");
                return null;
            }
            material = new Material(lit) { name = "Cube", enableInstancing = true };
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, CubeMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>The LevelLibrary asset listing every level file in play order (their names start with the number).</summary>
        public static LevelLibrary EnsureLevelLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<LevelLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<LevelLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            string[] files = Directory.GetFiles(LevelsFolder, "*.level.json").Select(f => f.Replace('\\', '/')).OrderBy(f => f, System.StringComparer.Ordinal).ToArray();
            var so = new SerializedObject(library);
            SerializedProperty levels = so.FindProperty("levels");
            levels.arraySize = files.Length;
            for (int i = 0; i < files.Length; i++) levels.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(files[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }
    }
}
