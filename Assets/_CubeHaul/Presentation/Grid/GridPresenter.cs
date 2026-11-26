using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Levels;
using UnityEngine;

namespace CubeHaul.Presentation.Grid
{
    /// <summary>
    /// The 3D cube field (Task 12, docs/GRID_VIEW.md): one cube per non-empty cell of the level's picture, in its palette
    /// color, on cube-hunt's cell grid. The game is followed only through cell indices (<see cref="CubeField"/>): a claimed
    /// cube stays, a collected one is released to the <see cref="CubePool"/>. No physics: there are no colliders, and
    /// nothing is decided by collisions.
    ///
    /// Rendering: cubes share one mesh and one material per palette color (GPU instancing enabled), so URP's SRP Batcher
    /// draws a whole level in a handful of batches; the cube GameObjects come from a pool and are reused between levels.
    /// </summary>
    [ExecuteAlways]
    public sealed class GridPresenter : MonoBehaviour
    {
        /// <summary>Top of the board the cubes stand on, design units (LayoutPreview's board box).</summary>
        public const float BoardTop = 2f;

        [SerializeField, Tooltip("Shows this level of the library when enabled (and in the Editor).")]
        LevelLibrary library;
        [SerializeField, Min(0)] int levelIndex;
        [SerializeField, Tooltip("Base material for the cubes (URP Lit, GPU instancing); one copy per palette color.")]
        Material cubeMaterial;
        [SerializeField, Range(0f, 0.4f), Tooltip("Gap between neighbouring cubes, as a share of the cell.")]
        float gap = 0.1f;
        [SerializeField, Tooltip("Flat palette colors without lighting (tests, color checks).")]
        bool unlit;

        readonly List<int> changed = new List<int>();
        CubeField field;
        GameObject[] cubes = new GameObject[0];
        Material[] materials = new Material[0];
        CubePool pool;
        Transform cubeRoot;
        Mesh mesh;
        Board board;

        public CubeField Field => field;
        public CubePool Pool => pool;
        public Board BoundBoard => board;

        public LevelLibrary Library
        {
            get => library;
            set => library = value;
        }

        public Material CubeMaterial
        {
            get => cubeMaterial;
            set => cubeMaterial = value;
        }

        public bool Unlit
        {
            get => unlit;
            set => unlit = value;
        }

        /// <summary>Side of a cube, design units.</summary>
        public float CubeSize => field == null ? 0f : (float)(field.CellSize * (1 - gap));

        void OnEnable()
        {
            if (field == null && library != null)
            {
                LevelRepository repo = library.Load();
                if (!repo.HasErrors && repo.Count > 0) Show(repo.GetAt(Mathf.Clamp(levelIndex, 0, repo.Count - 1)).Picture);
            }
        }

        void OnDisable()
        {
            Clear();
            pool?.DestroyAll();
            pool = null;
            DestroyNow(mesh);
            mesh = null;
            if (cubeRoot != null) DestroyNow(cubeRoot.gameObject);
            cubeRoot = null;
        }

        void LateUpdate()
        {
            if (board != null) SyncNow();
        }

        /// <summary>Shows a picture's cubes, all present. Any game previously bound is unbound.</summary>
        public void Show(PictureDefinition picture)
        {
            Clear();
            EnsurePool();
            field = new CubeField(picture);
            materials = new Material[field.Palette.Length];
            for (int c = 0; c < materials.Length; c++) materials[c] = MaterialFor(field.Palette[c]);
            cubes = new GameObject[field.CellCount];
            float size = CubeSize;
            Vector3 scale = Vector3.one * (size * WorldMapper.UnitsPerDesign);
            for (int i = 0; i < field.CellCount; i++)
            {
                if (!field.IsVisible(i)) continue;
                GameObject go = pool.Get("Cube " + i, materials[field.ColorOf(i)]);
                (double x, double y) = field.DesignCenter(i);
                go.transform.localPosition = WorldMapper.FromDesign(x, y, BoardTop + size / 2);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = scale;
                cubes[i] = go;
            }
        }

        /// <summary>Follows this board from now on: every frame, cubes of cells it has emptied go; claimed ones stay.</summary>
        public void Bind(Board gameBoard)
        {
            board = gameBoard;
            if (board != null) SyncNow();
        }

        /// <summary>Applies the board's changes now. Returns how many cells changed.</summary>
        public int SyncNow()
        {
            if (field == null || board == null) return 0;
            changed.Clear();
            int n = field.Sync(board, changed);
            for (int k = 0; k < changed.Count; k++)
            {
                int cell = changed[k];
                if (field.StateOf(cell) != CubeState.Empty) continue; // claimed: the cube stays where it is
                pool.Release(cubes[cell]);
                cubes[cell] = null;
            }
            return n;
        }

        /// <summary>The cube shown for this cell, or null (empty cell, or already collected).</summary>
        public GameObject CubeAt(int cell) => field != null && cell >= 0 && cell < cubes.Length ? cubes[cell] : null;

        /// <summary>The center of the top face of this cell's cube, world units.</summary>
        public Vector3 TopCenter(int cell)
        {
            (double x, double y) = field.DesignCenter(cell);
            return transform.TransformPoint(WorldMapper.FromDesign(x, y, BoardTop + CubeSize));
        }

        void Clear()
        {
            board = null;
            if (pool != null)
                foreach (GameObject go in cubes)
                    if (go != null) pool.Release(go);
            cubes = new GameObject[0];
            foreach (Material m in materials) DestroyNow(m);
            materials = new Material[0];
            field = null;
        }

        void EnsurePool()
        {
            if (pool != null) return;
            var root = new GameObject("Cubes") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            cubeRoot = root.transform;
            mesh = CubeMesh.Create();
            mesh.hideFlags = HideFlags.DontSave;
            pool = new CubePool(cubeRoot, mesh);
        }

        Material MaterialFor(PaletteColor p)
        {
            Material m;
            if (unlit) m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            else if (cubeMaterial != null) m = new Material(cubeMaterial);
            else m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var color = new Color32(p.R, p.G, p.B, 255);
            m.name = "Cube " + p;
            m.hideFlags = HideFlags.DontSave;
            m.enableInstancing = true;
            m.SetColor("_BaseColor", color);
            m.color = color;
            return m;
        }

        static void DestroyNow(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
