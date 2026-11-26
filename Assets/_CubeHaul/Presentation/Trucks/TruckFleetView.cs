using System;
using System.Collections.Generic;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Presentation.Grid;
using UnityEngine;

namespace CubeHaul.Presentation.Trucks
{
    /// <summary>
    /// The trucks on the board (Task 14, docs/TRUCKS.md): each sent truck is a pooled <see cref="TruckVisual"/> driven
    /// by a <see cref="TruckMotion"/>. Every frame the motion moves on (at the game speed) and the model follows: its
    /// lane pose on the board, its heading as a yaw, its size growing out of and shrinking back into the tunnel, the
    /// wheels turning, the cube in the bed once loaded. <see cref="Arrived"/> fires when a truck reaches its cube: that is
    /// when the game collects it (main.ts pickUp).
    /// </summary>
    public sealed class TruckFleetView : MonoBehaviour
    {
        [SerializeField, Tooltip("Truck model (with a TruckVisual on its root). Empty: the built-in placeholder.")]
        TruckVisual prefab;
        [SerializeField, Tooltip("Base material for the paint (URP Lit); one copy per crate color.")]
        Material paintMaterial;
        [SerializeField, Min(0f), Tooltip("Game speed: 1x, 2x, 3x.")]
        float speed = 1f;
        [SerializeField, Tooltip("Move the trucks in Update. Off: the owner calls Step.")]
        bool autoStep = true;

        sealed class Active
        {
            public TruckMotion Motion;
            public TruckVisual Visual;
        }

        readonly List<Active> active = new List<Active>();
        readonly Dictionary<int, (Material paint, Material trim)> materials = new Dictionary<int, (Material, Material)>();
        TruckPool pool;

        /// <summary>A truck reached its cube (collect it now).</summary>
        public event Action<TruckMotion> Arrived;
        /// <summary>A truck is back in the tunnel.</summary>
        public event Action<TruckMotion> Returned;

        public TruckPool Pool => pool ?? (pool = new TruckPool(transform, prefab));
        public int ActiveCount => active.Count;

        public TruckVisual Prefab
        {
            get => prefab;
            set => prefab = value;
        }

        public bool AutoStep
        {
            get => autoStep;
            set => autoStep = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        /// <summary>The model of a truck in the fleet (for tests and effects), or null.</summary>
        public TruckVisual VisualOf(TruckMotion motion)
        {
            foreach (Active a in active)
                if (a.Motion == motion) return a.Visual;
            return null;
        }

        /// <summary>Sends a truck on its planned trip, in the crate's color. It starts down in its tunnel lane.</summary>
        public TruckMotion Send(TruckRoute route, PointD cube, double cellSize, RoadRing ring, PaletteColor color)
        {
            var motion = new TruckMotion(route, cube, cellSize, ring);
            TruckVisual visual = Pool.Get();
            (Material paint, Material trim) = MaterialsFor(color);
            visual.SetPaint(paint, trim);
            active.Add(new Active { Motion = motion, Visual = visual });
            Place(motion, visual);
            return motion;
        }

        void Update()
        {
            if (autoStep && Application.isPlaying) Step(Mathf.Min(Time.deltaTime, 0.05f) * speed);
        }

        /// <summary>Moves every truck on by <paramref name="simSeconds"/> and updates the models.</summary>
        public void Step(float simSeconds)
        {
            for (int i = 0; i < active.Count; i++)
            {
                Active a = active[i];
                TruckEvents e = a.Motion.Advance(simSeconds);
                if ((e & TruckEvents.Arrived) != 0) Arrived?.Invoke(a.Motion);
                if ((e & TruckEvents.Gone) != 0)
                {
                    pool.Release(a.Visual);
                    active.RemoveAt(i--);
                    Returned?.Invoke(a.Motion);
                    continue;
                }
                Place(a.Motion, a.Visual);
            }
        }

        void Place(TruckMotion m, TruckVisual v)
        {
            bool visible = m.Visible;
            if (v.gameObject.activeSelf != visible) v.gameObject.SetActive(visible);
            if (!visible) return;
            PointD pose = m.Pose();
            float scale = (float)(m.DrawScale * WorldMapper.UnitsPerDesign);
            // Climbing out of the tunnel it rises to the board; rolling back down it sinks below it.
            float sink = (float)((1 - m.Fade) * 8);
            Transform t = v.transform;
            t.localPosition = WorldMapper.FromDesign(pose.X, pose.Y, GridPresenter.BoardTop - sink);
            t.localRotation = Quaternion.Euler(0f, 90f + (float)(m.Angle * Mathf.Rad2Deg), 0f);
            t.localScale = Vector3.one * scale;
            v.SetCargo(m.CargoVisible);
            v.Roll(m.Odometer, (float)(1 / m.CarScale));
        }

        (Material, Material) MaterialsFor(PaletteColor p)
        {
            int key = p.GetHashCode();
            if (materials.TryGetValue(key, out var pair) && pair.Item1 != null) return pair;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material paint = paintMaterial != null ? new Material(paintMaterial) : new Material(lit);
            var color = new Color32(p.R, p.G, p.B, 255);
            paint.name = "Truck Paint " + p;
            paint.hideFlags = HideFlags.DontSave;
            paint.enableInstancing = true;
            paint.SetColor("_BaseColor", color);
            paint.color = color;
            Material trim = new Material(paint) { name = "Truck Trim " + p, hideFlags = HideFlags.DontSave };
            Color dark = (Color)color * 0.5f;
            dark.a = 1;
            trim.SetColor("_BaseColor", dark);
            trim.color = dark;
            materials[key] = (paint, trim);
            return (paint, trim);
        }

        /// <summary>Sends every truck back to the pool at once (a new level).</summary>
        public void Clear()
        {
            foreach (Active a in active) pool?.Release(a.Visual);
            active.Clear();
        }

        void OnDestroy()
        {
            pool?.DestroyAll();
            foreach (var pair in materials.Values)
            {
                DestroyNow(pair.Item1);
                DestroyNow(pair.Item2);
            }
            materials.Clear();
        }

        static void DestroyNow(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
