using System.Collections.Generic;
using TMPro;
using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 把逻辑上的立方体 54 格投影成球面上的 54 个四边形（cube-sphere），
    /// 并给每格挂上数字文字与拾取组件。
    /// 参考做法同 Oskar Stålberg 的 Planet 演示：立方体面片投影到球。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PlanetMeshBuilder : MonoBehaviour
    {
        [Header("形状")]
        public float radius = 1f;

        [Tooltip("每个面整体向面心收缩的比例，用于面与面之间留一条细缝")]
        [Range(0f, 0.2f)]
        public float faceGap = 0.02f;

        [Tooltip("单格四角向格心收缩的比例，用于格子之间留缝")]
        [Range(0f, 0.2f)]
        public float cellGap = 0.035f;

        [Header("白盒配色")]
        public Color emptyColor = new Color(0.58f, 0.63f, 0.71f);
        public Color regionTint = new Color(0.42f, 0.50f, 0.62f);
        public Color value1Color = new Color(0.90f, 0.45f, 0.38f);
        public Color value2Color = new Color(0.42f, 0.76f, 0.50f);
        public Color value3Color = new Color(0.45f, 0.62f, 0.92f);
        public Color textColor = new Color(0.10f, 0.11f, 0.14f);

        [Header("文字")]
        public TMP_FontAsset font;
        public float fontSize = 0.95f;
        public float textLift = 0.03f;

        private readonly List<CellView> _cells = new List<CellView>(Board.CellCount);
        private readonly List<GameObject> _spawned = new List<GameObject>(Board.CellCount);
        private Material _material;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public IReadOnlyList<CellView> Cells => _cells;

        private void OnEnable() => Rebuild();

        // 注意：绝对不要在 OnValidate 里 Rebuild。
        // Rebuild 会 DestroyImmediate 已有格子，而 Unity 禁止在 OnValidate 期间销毁对象，
        // 轻则报错刷屏、重则把编辑器搞崩。改参数后请右键菜单 Rebuild。

        public CellView GetCell(int cellId)
        {
            if (cellId < 0 || cellId >= _cells.Count) return null;
            return _cells[cellId];
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            Clear();
            EnsureMaterial();

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

        private void EnsureMaterial()
        {
            if (_material != null) return;

            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            _material = new Material(shader) { name = "WhiteboxCell", hideFlags = HideFlags.DontSave };
            if (_material.HasProperty("_Glossiness")) _material.SetFloat("_Glossiness", 0.05f);
        }

        private void CreateCell(FaceId face, int row, int col)
        {
            int cellId = Board.CellId(face, row, col);

            var go = new GameObject($"Cell_{cellId:00}");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            // 生成的格子不进场景文件：每次加载由 OnEnable 重建，避免场景里存着一堆过期网格
            go.hideFlags = HideFlags.DontSave;

            Mesh mesh = BuildQuad(face, row, col, out Vector3 center, out Vector3 normal);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;

            TextMeshPro label = CreateLabel(go.transform, face, center, normal);

            var view = go.AddComponent<CellView>();
            view.Bind(cellId, renderer, label);
            view.SetColor(emptyColor);
            view.SetText(string.Empty, textColor);

            _cells.Add(view);
            _spawned.Add(go);
        }

        private TextMeshPro CreateLabel(Transform parent, FaceId face, Vector3 center, Vector3 normal)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, false);
            labelGo.transform.localPosition = center + normal * textLift;

            FaceFrame frame = FaceFrames.Get(face);
            Vector3 up = ToVector(frame.Up);
            up -= normal * Vector3.Dot(up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.up;
            up.Normalize();

            // 文字朝向球外（相机在外面看）
            labelGo.transform.rotation = Quaternion.LookRotation(-normal, up);

            var tmp = labelGo.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = string.Empty;
            tmp.fontSize = fontSize;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.4f;
            tmp.fontSizeMax = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = textColor;
            tmp.rectTransform.sizeDelta = new Vector2(0.34f, 0.34f);
            tmp.raycastTarget = false;

            return tmp;
        }

        private Mesh BuildQuad(FaceId face, int row, int col, out Vector3 center, out Vector3 normal)
        {
            FaceFrame frame = FaceFrames.Get(face);

            float step = 2f / Board.CellsPerRow;
            float shrink = 1f - faceGap;

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

            Vector3 mid = Vector3.zero;
            for (int i = 0; i < 4; i++) mid += corners[i];
            center = (mid / 4f).normalized * radius;
            normal = center.normalized;

            var vertices = new Vector3[4];
            var normals = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Vector3.Lerp(corners[i], center, cellGap).normalized * radius;
                vertices[i] = p;
                normals[i] = p.normalized;
            }

            var mesh = new Mesh { name = $"Cell_{face}_{row}{col}", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            });
            // 绕序必须是「从球外看是逆时针」：v0(左上) → v2(右下) → v1(右上)，
            // 这样面法线朝外。写反了会导致正面被剔除、看到球的内部，射线也会穿到背面去。
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private Vector3 ToSphere(FaceFrame frame, float u, float v)
        {
            Vector3 p = ToVector(frame.Normal) + ToVector(frame.Right) * u + ToVector(frame.Up) * v;
            return p.normalized * radius;
        }

        private static Vector3 ToVector(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>按格子里的数字刷新外观（0 = 空）。</summary>
        public void SetCellValue(int cellId, int value)
        {
            CellView view = GetCell(cellId);
            if (view == null) return;

            Color color;
            switch (value)
            {
                case 1: color = value1Color; break;
                case 2: color = value2Color; break;
                case 3: color = value3Color; break;
                default: color = emptyColor; break;
            }

            view.SetColor(color);
            view.SetText(value > 0 ? value.ToString() : string.Empty, textColor);
        }

        /// <summary>把某个区域整片染色，帮助玩家看出「本回合是哪 9 格」。</summary>
        public void HighlightRegion(FaceId? face)
        {
            for (int cell = 0; cell < _cells.Count; cell++)
            {
                CellView view = _cells[cell];
                if (view == null) continue;

                bool empty = view.Label != null && string.IsNullOrEmpty(view.Label.text);
                if (!empty) continue;

                bool inRegion = face.HasValue && Board.FaceOf(cell) == face.Value;
                view.SetColor(inRegion ? regionTint : emptyColor);
            }
        }

        public void ResetAll()
        {
            for (int cell = 0; cell < _cells.Count; cell++)
            {
                if (_cells[cell] == null) continue;
                _cells[cell].SetColor(emptyColor);
                _cells[cell].SetText(string.Empty, textColor);
            }
        }

        private void Clear()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                GameObject go = _spawned[i];
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }

            _spawned.Clear();
            _cells.Clear();
        }
    }
}
