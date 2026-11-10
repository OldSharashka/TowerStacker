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

        [Tooltip("单格四角向格心收缩的比例，用来在格子之间留缝（缝里露出的是深色底球，看起来就是网格线）")]
        [Range(0f, 0.2f)]
        public float cellGap = 0.025f;

        [Header("白盒配色")]
        public Color emptyColor = new Color(0.58f, 0.63f, 0.71f);
        public Color regionTint = new Color(0.80f, 0.84f, 0.92f);
        public Color backingColor = new Color(0.10f, 0.11f, 0.14f);
        public Color frameColor = new Color(1f, 0.82f, 0.25f);
        public float frameWidth = 0.03f;
        public float frameLift = 0.015f;
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
        private LineRenderer[] _frames;
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

            // 底下垫一层实心球：格子之间的缝会露出它，看起来是一条深色网格线，
            // 而不是"透过去看到球的背面"。
            CreateBacking();

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

            CreateRegionFrames();
        }

        private void CreateBacking()
        {
            var backing = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            backing.name = "Backing";
            backing.hideFlags = HideFlags.DontSave;
            backing.transform.SetParent(transform, false);
            backing.transform.localScale = Vector3.one * (radius * 2f * 0.995f);

            Collider collider = backing.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            var renderer = backing.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"))
            {
                name = "Backing",
                hideFlags = HideFlags.DontSave,
            };
            mat.color = backingColor;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
            renderer.sharedMaterial = mat;

            _spawned.Add(backing);
        }

        /// <summary>当前区域外面套一圈亮框。</summary>
        private void CreateRegionFrames()
        {
            _frames = new LineRenderer[Board.FaceCount];

            for (int face = 0; face < Board.FaceCount; face++)
            {
                var go = new GameObject($"Frame_{(FaceId)face}");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(transform, false);

                var line = go.AddComponent<LineRenderer>();
                // 必须是本地坐标：边框是 Board 的子物体，球转动时它要跟着转
                line.useWorldSpace = false;
                line.loop = true;
                line.widthMultiplier = frameWidth;
                line.numCapVertices = 2;
                line.textureMode = LineTextureMode.Stretch;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;

                var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"))
                {
                    name = "FrameLine",
                    hideFlags = HideFlags.DontSave,
                };
                mat.color = frameColor;
                line.sharedMaterial = mat;

                FaceFrame frame = FaceFrames.Get((FaceId)face);
                float h = 1f - faceGap + 0.05f;

                const int perSide = 12;
                var points = new Vector3[perSide * 4];
                int idx = 0;

                for (int s = 0; s < 4; s++)
                {
                    for (int k = 0; k < perSide; k++)
                    {
                        float t = k / (float)perSide;
                        float u, v;
                        switch (s)
                        {
                            case 0: u = Mathf.Lerp(-h, h, t); v = h; break;
                            case 1: u = h; v = Mathf.Lerp(h, -h, t); break;
                            case 2: u = Mathf.Lerp(h, -h, t); v = -h; break;
                            default: u = -h; v = Mathf.Lerp(-h, h, t); break;
                        }

                        Vector3 p = ToSphere(frame, u, v);
                        points[idx++] = p.normalized * (radius + frameLift);
                    }
                }

                line.positionCount = points.Length;
                line.SetPositions(points);
                line.enabled = false;

                _frames[face] = line;
                _spawned.Add(go);
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

            Mesh mesh = BuildPatch(face, row, col, out Vector3 center, out Vector3 normal);

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

        [Header("曲面细分（越大越圆，1 = 平面）")]
        [Range(1, 10)]
        public int subdivisions = 4;

        /// <summary>
        /// 生成一个格子的曲面片：内部 n×n 细分，每个顶点都投影到球面，
        /// 所以整颗球是光滑的，网格线来自格子之间留的缝。
        /// </summary>
        private Mesh BuildPatch(FaceId face, int row, int col, out Vector3 center, out Vector3 normal)
        {
            FaceFrame frame = FaceFrames.Get(face);

            float step = 2f / Board.CellsPerRow;
            float shrink = 1f - faceGap;

            float left = (-1f + col * step) * shrink;
            float right = (-1f + (col + 1) * step) * shrink;
            float top = (1f - row * step) * shrink;
            float bottom = (1f - (row + 1) * step) * shrink;

            // 留缝：把参数范围往里收，投影到球面后就成了一条细缝
            float padU = (right - left) * cellGap * 0.5f;
            float padV = (top - bottom) * cellGap * 0.5f;
            float uMin = left + padU;
            float uMax = right - padU;
            float vMin = top - padV;
            float vMax = bottom + padV;

            int n = Mathf.Max(1, subdivisions);
            int side = n + 1;

            var vertices = new Vector3[side * side];
            var normals = new Vector3[side * side];
            var uvs = new Vector2[side * side];

            for (int j = 0; j <= n; j++)
            {
                float v = Mathf.Lerp(vMin, vMax, j / (float)n);   // 自上而下
                for (int i = 0; i <= n; i++)
                {
                    float u = Mathf.Lerp(uMin, uMax, i / (float)n);
                    Vector3 p = ToSphere(frame, u, v);
                    int idx = j * side + i;
                    vertices[idx] = p;
                    normals[idx] = p.normalized;                  // 径向法线 → 光滑着色
                    uvs[idx] = new Vector2(i / (float)n, 1f - j / (float)n);
                }
            }

            var triangles = new int[n * n * 6];
            int t = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * side + i;
                    int b = a + 1;
                    int c = a + side + 1;
                    int d = a + side;

                    // 从球外看逆时针，法线朝外
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = c;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                }
            }

            center = ToSphere(frame, (uMin + uMax) * 0.5f, (vMin + vMax) * 0.5f);
            normal = center.normalized;

            var mesh = new Mesh { name = $"Cell_{face}_{row}{col}", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
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

        /// <summary>把某个区域整片染色并亮框圈起来，帮助玩家看出「本回合是哪 9 格」。</summary>
        public void HighlightRegion(FaceId? face)
        {
            if (_frames != null)
            {
                for (int i = 0; i < _frames.Length; i++)
                {
                    if (_frames[i] != null) _frames[i].enabled = face.HasValue && (int)face.Value == i;
                }
            }

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
            _frames = null;
        }
    }
}
