using System;
using System.Collections.Generic;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Session;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Presentation.Grid;
using UnityEngine;

namespace CubeHaul.Presentation.Trucks
{
    /// <summary>
    /// The trucks on the board (Tasks 14–15, docs/TRUCKS.md, docs/SESSION.md): each truck is a pooled
    /// <see cref="TruckVisual"/> following a <see cref="TruckMotion"/>. The model shows the motion's lane pose on the
    /// board, its heading as a yaw, its size growing out of and shrinking back into the tunnel, the wheels turning.
    ///
    /// Bound to a <see cref="GameSession"/> (<see cref="Bind"/>), it shows the session's trucks: one appears for every
    /// truck sent, and when a truck reaches its cube it takes that cube from the <see cref="GridPresenter"/> and plays
    /// the load (main.ts drawLoading: the cube rocks loose, rises, arcs into the bed, shrinks to fit and turns square
    /// with the truck); from LAND_AT on the truck's own cargo cube shows and the cube goes back to the grid's pool. The
    /// session moves the trucks; the owner calls <see cref="Render"/> after its steps. Trucks sent by hand with
    /// <see cref="Send"/> are moved here instead (<see cref="AutoStep"/> or <see cref="Step"/>).
    /// </summary>
    public sealed class TruckFleetView : MonoBehaviour
    {
        [SerializeField, Tooltip("Truck model (with a TruckVisual on its root). Empty: the built-in placeholder.")]
        TruckVisual prefab;
        [SerializeField, Tooltip("Base material for the paint (URP Lit); one copy per crate color.")]
        Material paintMaterial;
        [SerializeField, Min(0f), Tooltip("Speed of trucks sent by hand (1x, 2x, 3x); a session has its own.")]
        float speed = 1f;
        [SerializeField, Tooltip("Move trucks sent by hand in Update. Off: the owner calls Step.")]
        bool autoStep = true;

        sealed class Active
        {
            public TruckMotion Motion;
            public TruckVisual Visual;
            /// <summary>Moved here (sent by hand), not by a session.</summary>
            public bool Owned;
            /// <summary>The cube being loaded, taken from the grid, and where it started (world).</summary>
            public GameObject LoadCube;
            public Vector3 LoadFrom;
            public Vector3 LoadFromScale;
        }

        readonly List<Active> active = new List<Active>();
        readonly Dictionary<TruckMotion, Active> byMotion = new Dictionary<TruckMotion, Active>();
        readonly Dictionary<int, (Material paint, Material trim)> materials = new Dictionary<int, (Material, Material)>();
        TruckPool pool;
        GameSession session;
        GridPresenter grid;
        Action<TruckMotion> onSent, onCollected;

        /// <summary>A truck sent by hand reached its cube (collect it now).</summary>
        public event Action<TruckMotion> Arrived;
        /// <summary>A truck is back in the tunnel and its model in the pool.</summary>
        public event Action<TruckMotion> Returned;

        public TruckPool Pool => pool ?? (pool = new TruckPool(transform, prefab));
        public int ActiveCount => active.Count;
        /// <summary>Cubes in the air between their cell and a truck's bed right now.</summary>
        public int LoadingCount
        {
            get
            {
                int n = 0;
                foreach (Active a in active) if (a.LoadCube != null) n++;
                return n;
            }
        }

        public GameSession Session => session;

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
        public TruckVisual VisualOf(TruckMotion motion) => motion != null && byMotion.TryGetValue(motion, out Active a) ? a.Visual : null;

        /// <summary>The cube being loaded into this truck right now, or null.</summary>
        public GameObject LoadingCubeOf(TruckMotion motion) => motion != null && byMotion.TryGetValue(motion, out Active a) ? a.LoadCube : null;

        /// <summary>
        /// Shows this session's trucks from now on, taking loaded cubes from <paramref name="cubes"/>. Whatever the fleet
        /// showed before goes back to the pools. Null unbinds.
        /// </summary>
        public void Bind(GameSession gameSession, GridPresenter cubes)
        {
            Clear(); // while the old grid is still known, so loading cubes go back to it
            Unbind();
            session = gameSession;
            grid = cubes;
            if (session == null) return;
            onSent = OnSent;
            onCollected = OnCollected;
            session.TruckSent += onSent;
            session.CubeCollected += onCollected;
        }

        void Unbind()
        {
            if (session != null)
            {
                session.TruckSent -= onSent;
                session.CubeCollected -= onCollected;
            }
            session = null;
            grid = null;
        }

        void OnSent(TruckMotion motion) => Track(motion, ColorOf(motion.Color), owned: false);

        void OnCollected(TruckMotion motion)
        {
            if (grid == null || !byMotion.TryGetValue(motion, out Active a)) return;
            GameObject cube = grid.TakeCube(motion.Cell);
            if (cube == null) return;
            a.LoadCube = cube;
            a.LoadFrom = cube.transform.position;
            a.LoadFromScale = cube.transform.lossyScale;
        }

        PaletteColor ColorOf(int color)
        {
            CubeField field = grid != null ? grid.Field : null;
            if (field != null && color >= 0 && color < field.Palette.Length) return field.Palette[color];
            return new PaletteColor(200, 200, 200);
        }

        /// <summary>Sends a truck by hand on its planned trip, in the crate's color. It starts down in its tunnel lane.</summary>
        public TruckMotion Send(TruckRoute route, PointD cube, double cellSize, RoadRing ring, PaletteColor color)
        {
            var motion = new TruckMotion(route, cube, cellSize, ring);
            Track(motion, color, owned: true);
            return motion;
        }

        void Track(TruckMotion motion, PaletteColor color, bool owned)
        {
            TruckVisual visual = Pool.Get();
            (Material paint, Material trim) = MaterialsFor(color);
            visual.SetPaint(paint, trim);
            visual.SetCargo(false);
            visual.ResetWheels();
            var a = new Active { Motion = motion, Visual = visual, Owned = owned };
            active.Add(a);
            byMotion[motion] = a;
            Place(a, 1f);
        }

        void Update()
        {
            if (autoStep && Application.isPlaying) Step(Mathf.Min(Time.deltaTime, 0.05f) * speed);
        }

        /// <summary>Moves the trucks sent by hand on by <paramref name="simSeconds"/>, then draws every truck.</summary>
        public void Step(float simSeconds)
        {
            for (int i = 0; i < active.Count; i++)
            {
                Active a = active[i];
                if (!a.Owned) continue;
                TruckEvents e = a.Motion.Advance(simSeconds);
                if ((e & TruckEvents.Arrived) != 0) Arrived?.Invoke(a.Motion);
            }
            Render(1f);
        }

        /// <summary>
        /// Draws every truck <paramref name="alpha"/> (0..1) of the way from its last simulation step to its current
        /// one, and every cube being loaded; trucks back in the tunnel go back to the pool.
        /// </summary>
        public void Render(float alpha)
        {
            for (int i = 0; i < active.Count; i++)
            {
                Active a = active[i];
                if (a.Motion.State == TruckState.Gone)
                {
                    Release(a);
                    active.RemoveAt(i--);
                    Returned?.Invoke(a.Motion);
                    continue;
                }
                Place(a, alpha);
            }
        }

        void Place(Active a, float alpha)
        {
            TruckMotion m = a.Motion;
            TruckVisual v = a.Visual;
            m.Interpolate(alpha, out PointD pose, out double angle, out double fade);
            bool visible = fade > 0.02;
            if (v.gameObject.activeSelf != visible) v.gameObject.SetActive(visible);
            if (visible)
            {
                float scale = (float)(m.CarScale * (0.55 + 0.45 * fade) * WorldMapper.UnitsPerDesign);
                // Climbing out of the tunnel it rises to the board; rolling back down it sinks below it.
                float sink = (float)((1 - fade) * 8);
                Transform t = v.transform;
                t.localPosition = WorldMapper.FromDesign(pose.X, pose.Y, GridPresenter.BoardTop - sink);
                t.localRotation = Quaternion.Euler(0f, 90f + (float)(angle * Mathf.Rad2Deg), 0f);
                t.localScale = Vector3.one * scale;
                v.SetCargo(m.CargoVisible);
                v.Roll(m.Odometer, (float)(1 / m.CarScale));
            }
            if (a.LoadCube != null) PlaceLoadingCube(a, angle);
        }

        /// <summary>main.ts drawLoading in 3D: from the cube's spot on the board into the truck's bed.</summary>
        void PlaceLoadingCube(Active a, double angle)
        {
            TruckMotion m = a.Motion;
            Renderer cargo = a.Visual.Cargo;
            CubeLoadFrame f = CubeLoad.At(m.LoadProgress, session != null ? session.Roads.CellSize : 10);
            if (!f.InFlight || cargo == null || m.State != TruckState.Load)
            {
                grid?.ReturnCube(a.LoadCube);
                a.LoadCube = null;
                return;
            }
            Transform bed = cargo.transform;
            Transform cube = a.LoadCube.transform;
            float e = (float)f.Along;
            float worldPerDesign = WorldMapper.UnitsPerDesign * transform.lossyScale.y;
            Vector3 at = Vector3.Lerp(a.LoadFrom, bed.position, e) + Vector3.up * (float)(f.Height * worldPerDesign);
            cube.position = at;
            // It turns the short way round to sit square in the bed (a cube looks the same every quarter turn).
            float yaw = (float)((f.Rock + CubeLoad.AlignTurn(angle) * e) * Mathf.Rad2Deg);
            cube.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 size = Vector3.Lerp(a.LoadFromScale, bed.lossyScale, e);
            Vector3 parent = cube.parent != null ? cube.parent.lossyScale : Vector3.one;
            cube.localScale = new Vector3(size.x / parent.x, size.y / parent.y, size.z / parent.z);
        }

        void Release(Active a)
        {
            if (a.LoadCube != null) grid?.ReturnCube(a.LoadCube);
            a.LoadCube = null;
            pool?.Release(a.Visual);
            byMotion.Remove(a.Motion);
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

        /// <summary>Sends every truck back to the pool at once (a new level), and every cube being loaded to the grid.</summary>
        public void Clear()
        {
            foreach (Active a in active) Release(a);
            active.Clear();
            byMotion.Clear();
        }

        void OnDestroy()
        {
            Unbind();
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
