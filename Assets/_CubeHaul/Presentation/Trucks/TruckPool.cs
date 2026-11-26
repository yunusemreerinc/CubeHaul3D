using System.Collections.Generic;
using UnityEngine;

namespace CubeHaul.Presentation.Trucks
{
    /// <summary>
    /// Reusable trucks: taken when one is sent, given back when it is down in the tunnel again. Instances of the truck
    /// prefab, or of the built placeholder when there is none. Never more than the most trucks out at once.
    /// </summary>
    public sealed class TruckPool
    {
        readonly Transform parent;
        readonly TruckVisual prefab;
        readonly Stack<TruckVisual> free = new Stack<TruckVisual>();
        readonly List<TruckVisual> all = new List<TruckVisual>();

        public TruckPool(Transform parent, TruckVisual prefab)
        {
            this.parent = parent;
            this.prefab = prefab;
        }

        public int CreatedCount => all.Count;
        public int FreeCount => free.Count;
        public int InUseCount => all.Count - free.Count;

        public TruckVisual Get()
        {
            TruckVisual t = free.Count > 0 ? free.Pop() : Create();
            t.ResetWheels();
            t.SetCargo(false);
            t.gameObject.SetActive(true);
            return t;
        }

        public void Release(TruckVisual t)
        {
            if (t == null || !t.gameObject.activeSelf) return;
            t.gameObject.SetActive(false);
            free.Push(t);
        }

        public void DestroyAll()
        {
            foreach (TruckVisual t in all)
            {
                if (t == null) continue;
                if (Application.isPlaying) Object.Destroy(t.gameObject);
                else Object.DestroyImmediate(t.gameObject);
            }
            all.Clear();
            free.Clear();
        }

        TruckVisual Create()
        {
            TruckVisual t = prefab != null ? Object.Instantiate(prefab) : TruckModelBuilder.Build();
            t.name = "Truck " + all.Count;
            t.gameObject.hideFlags = HideFlags.DontSave;
            t.transform.SetParent(parent, false);
            all.Add(t);
            return t;
        }
    }
}
