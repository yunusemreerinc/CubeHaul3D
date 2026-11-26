using System.IO;
using CubeHaul.Presentation.Trucks;
using UnityEditor;
using UnityEngine;

namespace CubeHaul.EditorTools
{
    /// <summary>
    /// CubeHaul > Setup > Create Truck Prefab: saves the placeholder low-poly truck (TruckModelBuilder) as
    /// Art/Prefabs/Truck.prefab with its materials in Art/Materials (docs/TRUCKS.md). To use another model, make a prefab
    /// with a TruckVisual on its root and assign it to World > Trucks.
    /// </summary>
    public static class TruckPrefabBuilder
    {
        public const string PrefabPath = "Assets/_CubeHaul/Art/Prefabs/Truck.prefab";
        const string MaterialsFolder = "Assets/_CubeHaul/Art/Materials";

        [MenuItem("CubeHaul/Setup/Create Truck Prefab")]
        public static void CreateFromMenu()
        {
            if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog("Create Truck Prefab", PrefabPath + " exists. Build it again?", "Rebuild", "Cancel"))
                return;
            Build();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>The truck prefab, made if it does not exist yet.</summary>
        public static TruckVisual EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<TruckVisual>() != null) return existing.GetComponent<TruckVisual>();
            return Build();
        }

        static TruckVisual Build()
        {
            var materials = new TruckModelBuilder.Materials
            {
                Paint = MaterialAsset("TruckPaint", new Color(0.92f, 0.92f, 0.92f), 0.35f),
                Trim = MaterialAsset("TruckTrim", new Color(0.35f, 0.35f, 0.38f), 0.2f),
                Tire = MaterialAsset("TruckTire", new Color(0.12f, 0.1f, 0.09f), 0.1f),
                Glass = MaterialAsset("TruckGlass", new Color(0.16f, 0.24f, 0.32f), 0.8f),
                Light = MaterialAsset("TruckLight", new Color(1f, 0.97f, 0.78f), 0.5f),
            };
            TruckVisual truck = TruckModelBuilder.Build(materials);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(truck.gameObject, PrefabPath);
            Object.DestroyImmediate(truck.gameObject);
            Debug.Log("CubeHaul: " + PrefabPath + " saved. Commit it with its .meta and the Truck*.mat materials.");
            return saved.GetComponent<TruckVisual>();
        }

        static Material MaterialAsset(string name, Color color, float smoothness)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(lit) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
