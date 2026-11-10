using System.Collections.Generic;
using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 把逻辑上的立方体 54 格投影成球面上的 54 个四边形（cube-sphere）。
    /// 白盒用途：只要能看到格子、能点中、区域可分辨即可，不做任何美术处理。
    /// 参考：Oskar Stålberg 的 Planet 演示（同样的"立方体面片投影到球"做法）。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PlanetMeshBuilder : MonoBehaviour
    {
        [Header("形状")]
        [Tooltip("球半径")]
        public float radius = 1f;

        [Tooltip("每个面整体向面心收缩的比例，用来在面与面之间留出缝隙，让 6 个区域可分")]
        [Range(0f, 0.3f)]
        public float faceGap = 0.08f;

        [Tooltip("单格四角向格心收缩的比例，用来在格子之间留缝")]
        [Range(0f, 0.3f)]
        public float cellGap = 0.06f;

        [Header("外观（白盒）")]
        public Color emptyColor = new Color(0.55f, 0.60f, 0.68f);
        public Color value1Color = new Color(0.85f, 0.35f, 0.30f);
        public Color value2Color = new Color(0.35f, 0.70f, 0.45f);
        public Color value3Color = new Color(0.35f, 0.55f, 0.90f);
        public Color currentFaceTint = new Color(1f, 1f, 1f, 1f);

        private const int Grid = Board.CellsPerRow + 1; // 4x4 顶点

        private readonly List<GameObject> _cells = new List<GameObject>(Board.CellCount);
        private Material _material;

        private void OnEnable() => Rebuild();

        private void OnValidate()
        {
            if (isActiveAndEnabled) Rebuild();
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            Clear();

            if (_material == null)
            {
                // 白盒用受光材质：明暗能看出球体的体积，Unlit 看上去像一块平板
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _material = new Material(shader) { name = "WhiteboxCell" };
                if (_material.HasProperty("_Glossiness")) _material.SetFloat("_Glossiness", 0.05f);
            }

            for (int face = 0; face < Board.FaceCount; face++)
            {
                for (int row = 0; row < Board.CellsPerRow; row++)
                {
                    for (int col = 0; col < Board.CellsPerRow; col++)
                    {
                        CreateCell((FaceId)face, row, col);
                    }
                }
            }
        }

        private void CreateCell(FaceId face, int row, int col)
        {
            int cellId = Board.CellId(face, row, col);
            var go = new GameObject($"Cell_{cellId:00}_{face}_{row}{col}");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            Mesh mesh = BuildQuad(face, row, col);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ApplyColor(renderer, emptyColor);

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;

            _cells.Add(go);
        }

        private Mesh BuildQuad(FaceId face, int row, int col)
        {
            FaceFrame frame = FaceFrames.Get(face);

            float step = 2f / Board.CellsPerRow;              // 2/3
            float shrink = 1f - faceGap;                      // 面内整体收缩

            // 该格在面上的四个角（R 向右、U 向上），再整体按面收缩
            float left = (-1f + col * step) * shrink;
            float right = (-1f + (col + 1) * step) * shrink;
            float top = (1f - row * step) * shrink;
            float bottom = (1f - (row + 1) * step) * shrink;

            Vector3[] corners =
            {
                ToSphere(frame, left, top),
                ToSphere(frame, right, top),
                ToSphere(frame, right, bottom),
                ToSphere(frame, left, bottom),
            };

            // 四角向格心收缩，形成缝隙
            Vector3 center = Vector3.zero;
            for (int i = 0; i < 4; i++) center += corners[i];
            center = (center / 4f).normalized * radius;

            var vertices = new Vector3[4];
            var normals = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Vector3.Lerp(corners[i], center, cellGap);
                p = p.normalized * radius;
                vertices[i] = p;
                normals[i] = p.normalized;
            }

            var mesh = new Mesh { name = $"Cell_{face}_{row}{col}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private Vector3 ToSphere(FaceFrame frame, float u, float v)
        {
            Vector3 p = ToVector(frame.Normal)
                        + ToVector(frame.Right) * u
                        + ToVector(frame.Up) * v;
            return p.normalized * radius;
        }

        private static Vector3 ToVector(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private void ApplyColor(Renderer renderer, Color color)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>按格子里的数字更新颜色（0 = 空）。供视图层调用。</summary>
        public void SetCellValue(int cellId, int value)
        {
            if (cellId < 0 || cellId >= _cells.Count) return;

            Color color;
            switch (value)
            {
                case 1: color = value1Color; break;
                case 2: color = value2Color; break;
                case 3: color = value3Color; break;
                default: color = emptyColor; break;
            }

            ApplyColor(_cells[cellId].GetComponent<Renderer>(), color);
        }

        private void Clear()
        {
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                GameObject go = _cells[i];
                if (go == null) continue;

                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            _cells.Clear();
        }
    }
}
