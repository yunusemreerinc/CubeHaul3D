using UnityEngine;

namespace CubeHaul.Presentation.Grid
{
    /// <summary>A unit cube (side 1, centered on the origin) with flat-shaded faces: 24 vertices, 12 triangles.</summary>
    public static class CubeMesh
    {
        public static Mesh Create()
        {
            var normals = new[] { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            var vertices = new Vector3[24];
            var faceNormals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = normals[f];
                // Two axes spanning the face, chosen so (u, v, n) is right-handed and triangles face outwards.
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n);
                Vector3 v = Vector3.Cross(n, u);
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 1 || k == 2) ? 0.5f : -0.5f, sv = k >= 2 ? 0.5f : -0.5f;
                    vertices[f * 4 + k] = n * 0.5f + u * su + v * sv;
                    faceNormals[f * 4 + k] = n;
                    uvs[f * 4 + k] = new Vector2(su + 0.5f, sv + 0.5f);
                }
                int b = f * 4;
                // Front faces: cross(p1 - p0, p2 - p0) = +n, Unity's winding (a quad at y = 0 with corners (0,0,0),
                // (0,0,1), (1,0,1) faces up). Here (0, 1, 3) spans u then v, and cross(u, n x u) = n.
                triangles[f * 6 + 0] = b;
                triangles[f * 6 + 1] = b + 1;
                triangles[f * 6 + 2] = b + 3;
                triangles[f * 6 + 3] = b + 1;
                triangles[f * 6 + 4] = b + 2;
                triangles[f * 6 + 5] = b + 3;
            }
            var mesh = new Mesh { name = "CubeHaul Cube" };
            mesh.vertices = vertices;
            mesh.normals = faceNormals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
