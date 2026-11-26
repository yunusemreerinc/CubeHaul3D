using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CubeHaul.Presentation.Grid
{
    /// <summary>
    /// Reusable cube GameObjects (MeshFilter + MeshRenderer, no collider): a level of up to 2116 cubes, then the next
    /// level, never creates more than the largest level needs. Released cubes are deactivated, not destroyed. Every
    /// object it makes is HideFlags.DontSave, so nothing of it is written into the scene.
    /// </summary>
    public sealed class CubePool
    {
        readonly Transform parent;
        readonly Mesh mesh;
        readonly Stack<GameObject> free = new Stack<GameObject>();
        readonly List<GameObject> all = new List<GameObject>();

        public CubePool(Transform parent, Mesh mesh)
        {
            this.parent = parent;
            this.mesh = mesh;
        }

        /// <summary>Cubes made so far, in use or free.</summary>
        public int CreatedCount => all.Count;
        public int FreeCount => free.Count;
        public int InUseCount => all.Count - free.Count;

        public GameObject Get(string name, Material material)
        {
            GameObject go = free.Count > 0 ? free.Pop() : Create();
            go.name = name;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.SetActive(true);
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            free.Push(go);
        }

        /// <summary>Destroys every cube the pool made (when the view goes away).</summary>
        public void DestroyAll()
        {
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
            }
            all.Clear();
            free.Clear();
        }

        GameObject Create()
        {
            var go = new GameObject("Cube", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            all.Add(go);
            return go;
        }
    }
}
