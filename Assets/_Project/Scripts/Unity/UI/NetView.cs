using Tower.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 2D 十字展开图（文档 3-2）：把 6 个面按「上 / 左右前后 / 下」摊开，显示每格的数字，
    /// 用来肉眼核对跨面邻接是否对。N 键开关，纯调试视图，不参与任何规则。
    /// </summary>
    public sealed class NetView : MonoBehaviour
    {
        private const int CellSize = 26;
        private const int CellInner = 24;
        private const int FaceStride = 86;   // 3*26 + 8
        private const float OriginX = 18f;
        private const float OriginY = -46f;

        private RectTransform _panel;
        private readonly Image[] _cells = new Image[Board.CellCount];
        private readonly Text[] _texts = new Text[Board.CellCount];

        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.95f);
        private static readonly Color Empty = new Color(0.30f, 0.33f, 0.39f);
        private static readonly Color RegionTint = new Color(0.46f, 0.50f, 0.58f);
        private static readonly Color Value1 = new Color(0.95f, 0.42f, 0.32f);
        private static readonly Color Value2 = new Color(0.35f, 0.78f, 0.45f);
        private static readonly Color Value3 = new Color(0.36f, 0.60f, 0.95f);
        private static readonly Color TextColor = new Color(0.08f, 0.09f, 0.12f);
        private static readonly Color TextDim = new Color(0.58f, 0.61f, 0.68f);

        public bool IsVisible => _panel != null && _panel.gameObject.activeSelf;

        public static NetView Create(Transform canvasRoot)
        {
            var go = new GameObject("NetView", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<NetView>();
            view.Build();
            view.SetVisible(false);
            return view;
        }

        private void Build()
        {
            _panel = GetComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(372f, 344f);

            UiFactory.CreateImage(_panel, "Bg", PanelColor, true);

            Text title = UiFactory.CreateLabel(_panel, "Title", "2D 展开图（N 关闭）", 18,
                new Color(0.86f, 0.88f, 0.92f), TextAnchor.MiddleLeft,
                new Vector2(OriginX, -12f), new Vector2(336f, 24f));
            title.fontStyle = FontStyle.Bold;

            // 十字展开：TOP 在 FRONT 上方、BOTTOM 在 FRONT 下方；
            // 中排 LEFT FRONT RIGHT BACK（标准立方体展开，跨棱关系才对得上）
            int[] faceCol = { 1, 3, 2, 0, 1, 1 };   // Front, Back, Right, Left, Top, Bottom
            int[] faceRow = { 1, 1, 1, 1, 0, 2 };

            for (int face = 0; face < Board.FaceCount; face++)
            {
                for (int row = 0; row < Board.CellsPerRow; row++)
                {
                    for (int col = 0; col < Board.CellsPerRow; col++)
                    {
                        int cellId = Board.CellId((FaceId)face, row, col);
                        float x = OriginX + faceCol[face] * FaceStride + col * CellSize + 1f;
                        float y = OriginY - faceRow[face] * FaceStride - row * CellSize - 1f;

                        RectTransform rect = UiFactory.CreateRect(
                            _panel, $"C{cellId}",
                            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                            new Vector2(x, y), new Vector2(CellInner, CellInner));

                        _cells[cellId] = UiFactory.CreateImage(rect, "Bg", Empty, false);
                        _cells[cellId].raycastTarget = false;

                        _texts[cellId] = UiFactory.CreateLabel(rect, "Text", string.Empty, 15,
                            TextColor, TextAnchor.MiddleCenter, Vector2.zero,
                            new Vector2(CellInner, CellInner));

                        RectTransform textRect = _texts[cellId].rectTransform;
                        textRect.anchorMin = Vector2.zero;
                        textRect.anchorMax = Vector2.one;
                        textRect.offsetMin = Vector2.zero;
                        textRect.offsetMax = Vector2.zero;
                    }
                }
            }

            UiFactory.CreateLabel(_panel, "Legend",
                "上：TOP　中：LEFT FRONT RIGHT BACK　下：BOTTOM", 12, TextDim,
                TextAnchor.MiddleLeft, new Vector2(OriginX, -306f), new Vector2(336f, 18f));
        }

        public void Toggle()
        {
            SetVisible(!IsVisible);
        }

        public void SetVisible(bool visible)
        {
            if (_panel != null) _panel.gameObject.SetActive(visible);
        }

        public bool Contains(Vector2 screenPosition)
        {
            return IsVisible &&
                   RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPosition, null);
        }

        /// <summary>按一局状态刷新 54 格数字与配色；当前区域用浅一点的底色。</summary>
        public void Refresh(TowerRun run)
        {
            if (run == null) return;

            for (int cell = 0; cell < Board.CellCount; cell++)
            {
                if (_cells[cell] == null) continue;

                int value = run.State.ValueAt(cell);
                Color color;
                switch (value)
                {
                    case 1: color = Value1; break;
                    case 2: color = Value2; break;
                    case 3: color = Value3; break;
                    default:
                        color = Board.FaceOf(cell) == run.CurrentFace ? RegionTint : Empty;
                        break;
                }

                _cells[cell].color = color;
                _texts[cell].text = value > 0 ? value.ToString() : string.Empty;
            }
        }
    }
}
