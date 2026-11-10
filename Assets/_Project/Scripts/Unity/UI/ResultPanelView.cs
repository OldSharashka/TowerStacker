using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 结算面板（文档 5.「本局结算」）：总分 / 目标 / 结果 / 最高单回合 + 两个重开按钮。
    /// 白盒默认样式；显示时铺一层半透明底挡住球面，避免误操作。
    /// </summary>
    public sealed class ResultPanelView : MonoBehaviour
    {
        private GameObject _root;
        private Text _total;
        private Text _target;
        private Text _verdict;
        private Text _best;

        public event Action RestartSameClicked;
        public event Action RestartNewClicked;

        public bool IsVisible => _root != null && _root.activeSelf;

        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.145f, 0.98f);
        private static readonly Color TextMain = new Color(0.92f, 0.94f, 0.97f);
        private static readonly Color TextDim = new Color(0.65f, 0.68f, 0.75f);
        private static readonly Color Win = new Color(0.45f, 0.90f, 0.52f);
        private static readonly Color Lose = new Color(0.95f, 0.48f, 0.42f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.31f);

        public static ResultPanelView Create(Transform canvasRoot)
        {
            var go = new GameObject("ResultPanel", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<ResultPanelView>();
            view.Build();
            view._root.SetActive(false);
            return view;
        }

        private void Build()
        {
            var rootRect = GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            _root = gameObject;

            Image backdrop = UiFactory.CreateImage(transform, "Backdrop", Backdrop, true);
            backdrop.raycastTarget = true;

            RectTransform panel = UiFactory.CreateRect(
                transform, "Panel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(600f, 400f));
            UiFactory.CreateImage(panel, "Bg", PanelColor, true);

            Line(panel, "Title", "本 局 结 算", 30, TextMain, -28f, 36f).fontStyle = FontStyle.Bold;
            _total = Line(panel, "Total", "总分  0", 26, TextMain, -92f, 34f);
            _target = Line(panel, "Target", "目标  0", 22, TextDim, -134f, 30f);
            _verdict = Line(panel, "Verdict", "达标", 30, Win, -186f, 38f);
            _verdict.fontStyle = FontStyle.Bold;
            _best = Line(panel, "Best", "最高单回合 0（第 1 回合）", 18, TextDim, -238f, 26f);

            MakeButton(panel, "重  开", new Vector2(-120f, 28f), () => RestartSameClicked?.Invoke());
            MakeButton(panel, "换种子重开", new Vector2(120f, 28f), () => RestartNewClicked?.Invoke());
        }

        private static Text Line(Transform parent, string name, string content,
            int size, Color color, float yFromTop, float height)
        {
            Text text = UiFactory.CreateLabel(parent, name, content, size, color, TextAnchor.MiddleCenter,
                new Vector2(0f, yFromTop), new Vector2(600f, height));

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, yFromTop);
            return text;
        }

        private void MakeButton(Transform parent, string label, Vector2 position, Action onClick)
        {
            RectTransform rect = UiFactory.CreateRect(
                parent, label,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                position, new Vector2(210f, 50f));

            Image background = UiFactory.CreateImage(rect, "Bg", ButtonColor, true);
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onClick());

            Text text = UiFactory.CreateLabel(rect, "Text", label, 20, TextMain, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(210f, 50f));
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        public void Show(int total, int target, bool success, int bestScore, int bestTurn)
        {
            _total.text = $"总分  {total}";
            _target.text = $"目标  {target}";
            _verdict.text = success ? "达标 ✓" : "未达标";
            _verdict.color = success ? Win : Lose;
            _best.text = $"最高单回合 {bestScore}（第 {bestTurn} 回合）";

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }
    }
}
