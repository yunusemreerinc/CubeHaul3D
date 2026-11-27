using System.Collections.Generic;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Levels;
using UnityEngine;

namespace CubeHaul.Presentation
{
    /// <summary>
    /// Placeholder geometry for the screen composition (Task 11): the board with one level's cube grid, the ramp, the
    /// slots and three rows of the tray, as flat colored boxes at their cube-hunt positions. It shows the layout in the
    /// Editor and in Play Mode until the real board, crates and trucks replace it. Built at runtime and never saved
    /// into the scene (HideFlags.DontSave).
    /// </summary>
    [ExecuteAlways]
    public sealed class LayoutPreview : MonoBehaviour
    {
        [SerializeField] LevelLibrary library;
        [SerializeField, Min(0)] int levelIndex;
        [SerializeField, Range(1, 7)] int slots = 5;
        [SerializeField] int trayColumns = 5;
        [SerializeField, Tooltip("Draw the level's grid as a flat texture. Off when a GridPresenter draws the cubes.")]
        bool showField = true;
        [SerializeField, Tooltip("Draw placeholder boxes for the slots and the tray. Off when the UI draws them (Task 16).")]
        bool showDockAndTray = true;

        public const float CrateHeight = 30f;
        /// <summary>The board box: its top is where the cubes stand (GridPresenter.BoardTop).</summary>
        public const float BoardHeight = 2f;
        public static readonly Color32 BoardColor = new Color32(0xe9, 0xdc, 0xc6, 0xff);
        public const float SlotPadHeight = 4f;

        readonly List<Object> created = new List<Object>();
        readonly List<Renderer> renderers = new List<Renderer>();
        Material lit, unlit;

        /// <summary>Every placeholder renderer, for layout tests.</summary>
        public IReadOnlyList<Renderer> Renderers => renderers;

        public bool ShowField
        {
            get => showField;
            set
            {
                if (showField == value) return;
                showField = value;
                if (isActiveAndEnabled) Rebuild();
            }
        }

        public bool ShowDockAndTray
        {
            get => showDockAndTray;
            set
            {
                if (showDockAndTray == value) return;
                showDockAndTray = value;
                if (isActiveAndEnabled) Rebuild();
            }
        }

        public LevelLibrary Library
        {
            get => library;
            set => library = value;
        }

        bool dirty;

        void OnEnable() => Rebuild();

        void OnDisable() => Clear();

        // Objects cannot be created or destroyed inside OnValidate; rebuild on the next update instead.
        void OnValidate() => dirty = true;

        void Update()
        {
            if (!dirty) return;
            dirty = false;
            Rebuild();
        }

        public void Rebuild()
        {
            Clear();
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            lit = Keep(new Material(litShader) { name = "Layout Preview Lit" });
            unlit = Keep(new Material(unlitShader) { name = "Layout Preview Field" });

            Box("Board", DesignLayout.Board, BoardHeight, BoardColor);
            if (showField) Field(Picture());
            Box("Ramp", DesignLayout.Tunnel, 2, new Color32(0x56, 0x5c, 0x68, 0xff));
            if (!showDockAndTray) return;
            for (int i = 0; i < slots; i++) Box("Slot " + i, DesignLayout.SlotRect(i, slots), SlotPadHeight, new Color32(0xd8, 0xc8, 0xb0, 0xff));
            for (int row = 0; row < DesignLayout.TrayRows; row++)
                for (int col = 0; col < trayColumns; col++)
                    Box($"Crate {col},{row}", DesignLayout.BlockRect(col, row, trayColumns), CrateHeight,
                        Color.Lerp(new Color32(0x3f, 0xb6, 0xf2, 0xff), new Color32(0xf6, 0xef, 0xe4, 0xff), row * 0.3f));
        }

        PictureDefinition Picture()
        {
            if (library != null)
            {
                LevelRepository repo = library.Load();
                if (repo.Errors.Count == 0 && repo.Count > 0) return repo.GetAt(Mathf.Clamp(levelIndex, 0, repo.Count - 1)).Picture;
            }
            // No library: a 29 x 29 two-color checker, the size of the first level.
            var cells = new int[29 * 29];
            for (int i = 0; i < cells.Length; i++) cells[i] = (i % 29 / 4 + i / 29 / 4) % 2;
            return new PictureDefinition(29, 29, cells, new[] { "#e8424f", "#5cc464" });
        }

        void Box(string name, RectD design, float height, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyNow(go.GetComponent<Collider>());
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = WorldMapper.FromDesign(design.CenterX, design.CenterY, height / 2);
            go.transform.localScale = new Vector3((float)design.Width, height, (float)design.Height) * WorldMapper.UnitsPerDesign;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = lit;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            created.Add(go);
            renderers.Add(r);
        }

        /// <summary>The cube field as one textured quad: each cell 8 x 8 texels with a 1-texel darker grid line.</summary>
        void Field(PictureDefinition p)
        {
            const int texels = 8;
            var tex = Keep(new Texture2D(p.Width * texels, p.Height * texels, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Layout Preview Field" });
            var pixels = new Color32[tex.width * tex.height];
            var palette = new Color32[p.Palette.Length];
            for (int c = 0; c < palette.Length; c++)
                palette[c] = ColorUtility.TryParseHtmlString(p.Palette[c], out Color parsed) ? (Color32)parsed : new Color32(128, 128, 128, 255);
            var empty = new Color32(0xf2, 0xe8, 0xd8, 0xff);
            for (int ty = 0; ty < tex.height; ty++)
                for (int tx = 0; tx < tex.width; tx++)
                {
                    int x = tx / texels, row = p.Height - 1 - ty / texels; // texture row 0 is the bottom, nearest the camera
                    int c = p.Cells[row * p.Width + x];
                    Color32 col = c < 0 ? empty : palette[c];
                    if (tx % texels == 0 || ty % texels == 0) col = Color32.Lerp(col, new Color32(0, 0, 0, 255), 0.25f);
                    pixels[ty * tex.width + tx] = col;
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            RectD f = DesignLayout.Field(p.Width, p.Height);
            const float lift = 2.5f; // just above the board's top face
            var mesh = Keep(new Mesh { name = "Layout Preview Field" });
            mesh.vertices = new[]
            {
                WorldMapper.FromDesign(f.XMin, f.YMax, lift), WorldMapper.FromDesign(f.XMax, f.YMax, lift),
                WorldMapper.FromDesign(f.XMin, f.YMin, lift), WorldMapper.FromDesign(f.XMax, f.YMin, lift),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Field", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = unlit;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", tex);
            block.SetTexture("_MainTex", tex);
            r.SetPropertyBlock(block);
            created.Add(go);
            renderers.Add(r);
        }

        T Keep<T>(T o) where T : Object
        {
            o.hideFlags = HideFlags.DontSave;
            created.Add(o);
            return o;
        }

        void Clear()
        {
            foreach (Object o in created)
                if (o != null) DestroyNow(o);
            created.Clear();
            renderers.Clear();
        }

        static void DestroyNow(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
