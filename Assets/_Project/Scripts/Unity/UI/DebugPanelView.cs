using System;
using Tower.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 调试面板（文档 3-1）：种子、强制骰子、跳过回合、填满全盘、转场/跳分参数、
    /// CSV 导出、换种子、重开、2D 展开图开关。F1 显示/隐藏，只给开发用。
    /// </summary>
    public sealed class DebugPanelView : MonoBehaviour
    {
        private RectTransform _panel;
        private Text _seed;
        private Text _transitionLabel;
        private Text _popupLabel;

        public event Action<int> ForceDieClicked;
        public event Action SkipTurnClicked;
        public event Action FillBoardClicked;
        public event Action ExportCsvClicked;
        public event Action NewSeedClicked;
        public event Action RestartClicked;
        public event Action<float> TransitionDelta;
        public event Action<float> PopupDelta;
        public event Action NetViewClicked;

        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.94f);
        private static readonly Color ButtonColor = new Color(0.21f, 0.23f, 0.30f);
        private static readonly Color TextMain = new Color(0.86f, 0.88f, 0.92f);
        private static readonly Color TextDim = new Color(0.58f, 0.61f, 0.68f);

        public bool IsVisible => _panel != null && _panel.gameObject.activeSelf;

        public static DebugPanelView Create(Transform canvasRoot)
        {
            var go = new GameObject("DebugPanel", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<DebugPanelView>();
            view.Build();
            view.SetVisible(false);
            return view;
        }

        private void Build()
        {
            _panel = GetComponent<RectTransform>();
            _panel.anchorMin = new Vector2(1f, 1f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 1f);
            _panel.anchoredPosition = new Vector2(-24f, -24f);
            _panel.sizeDelta = new Vector2(300f, 312f);

            UiFactory.CreateImage(_panel, "Bg", PanelColor, true);

            Label("Title", "调试面板  (F1)", 17, TextMain, 16f, -10f, 268f, 22f);
            _seed = Label("Seed", "种子 0", 15, TextDim, 16f, -34f, 268f, 20f);

            Button("骰 1", 16f, -60f, 56f, 28f, () => ForceDieClicked?.Invoke(1));
            Button("骰 2", 80f, -60f, 56f, 28f, () => ForceDieClicked?.Invoke(2));
            Button("骰 3", 144f, -60f, 56f, 28f, () => ForceDieClicked?.Invoke(3));

            Button("跳过本回合", 16f, -96f, 132f, 30f, () => SkipTurnClicked?.Invoke());
            Button("填满全盘", 156f, -96f, 128f, 30f, () => FillBoardClicked?.Invoke());

            _transitionLabel = Label("Transition", "转场 0.50s", 15, TextMain, 16f, -136f, 130f, 24f);
            Button("−", 152f, -136f, 30f, 26f, () => TransitionDelta?.Invoke(-0.05f));
            Button("+", 188f, -136f, 30f, 26f, () => TransitionDelta?.Invoke(0.05f));

            _popupLabel = Label("Popup", "跳分 0.05s", 15, TextMain, 16f, -168f, 130f, 24f);
            Button("−", 152f, -168f, 30f, 26f, () => PopupDelta?.Invoke(-0.01f));
            Button("+", 188f, -168f, 30f, 26f, () => PopupDelta?.Invoke(0.01f));

            Button("导出 CSV", 16f, -202f, 132f, 30f, () => ExportCsvClicked?.Invoke());
            Button("换种子", 156f, -202f, 128f, 30f, () => NewSeedClicked?.Invoke());

            Button("展开图 (N)", 16f, -238f, 132f, 30f, () => NetViewClicked?.Invoke());
            Button("重开", 156f, -238f, 128f, 30f, () => RestartClicked?.Invoke());

            Label("Hint", "空格加速跳分 · N 展开图", 13, TextDim, 16f, -276f, 268f, 20f);
        }

        private Text Label(string name, string content, int size, Color color,
            float x, float y, float width, float height)
        {
            return UiFactory.CreateLabel(_panel, name, content, size, color, TextAnchor.MiddleLeft,
                new Vector2(x, y), new Vector2(width, height));
        }

        private void Button(string label, float x, float y, float width, float height, Action onClick)
        {
            RectTransform rect = UiFactory.CreateRect(
                _panel, label,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, height));

            Image background = UiFactory.CreateImage(rect, "Bg", ButtonColor, true);
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => onClick());

            Text text = UiFactory.CreateLabel(rect, "Text", label, 15, TextMain, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(width, height));
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
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

        public void Refresh(TowerRun run, float transitionDuration, float popupInterval)
        {
            if (run == null) return;

            _seed.text = $"种子 {run.State.Seed}";
            _transitionLabel.text = $"转场 {transitionDuration:0.00}s";
            _popupLabel.text = $"跳分 {popupInterval:0.00}s";
        }
    }
}
