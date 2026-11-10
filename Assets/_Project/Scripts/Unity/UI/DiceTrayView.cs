using System;
using TMPro;
using Tower.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 左下角骰子托盘（开发文档 3.7.3）：3 个骰位 + 重掷按钮 + 区域标题。
    /// 只负责「长什么样、鼠标点到了谁」，不参与任何规则判断。
    /// 文字只用数字和 ASCII：白盒默认字体没有中文字形，中文说明留在 HUD 里。
    /// </summary>
    public sealed class DiceTrayView : MonoBehaviour
    {
        private const int SlotCount = 3;
        private const float PanelWidth = 220f;
        private const float PanelHeight = 148f;
        private const float SlotSize = 60f;
        private const float SlotGap = 8f;
        private const float Pad = 12f;

        private RectTransform _panel;
        private TextMeshProUGUI _header;
        private Button _rerollButton;
        private TextMeshProUGUI _rerollLabel;

        private readonly RectTransform[] _slots = new RectTransform[SlotCount];
        private readonly Image[] _bodies = new Image[SlotCount];
        private readonly Image[] _borders = new Image[SlotCount];
        private readonly TextMeshProUGUI[] _texts = new TextMeshProUGUI[SlotCount];

        /// <summary>点「重掷」按钮。选哪几颗由 GameLoop 传给逻辑层。</summary>
        public event Action RerollClicked;

        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        private static readonly Color BodyCurrent = new Color(0.93f, 0.94f, 0.97f);
        private static readonly Color BodyPending = new Color(0.34f, 0.37f, 0.44f);
        private static readonly Color BodyUsed = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color TextDark = new Color(0.08f, 0.09f, 0.12f);
        private static readonly Color TextLight = new Color(0.84f, 0.86f, 0.90f);
        private static readonly Color TextDim = new Color(0.40f, 0.42f, 0.47f);
        private static readonly Color BorderCurrent = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color BorderNone = new Color(0f, 0f, 0f, 0f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.31f);

        public static DiceTrayView Create(Transform canvasRoot)
        {
            var go = new GameObject("DiceTray", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<DiceTrayView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            _panel = GetComponent<RectTransform>();
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.zero;
            _panel.pivot = Vector2.zero;
            _panel.anchoredPosition = new Vector2(28f, 28f);
            _panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            Image background = UiFactory.CreateImage(_panel, "Bg", PanelColor, true);
            background.raycastTarget = true;

            RectTransform headerRect = UiFactory.CreateRect(
                _panel, "Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(-2f * Pad, 22f));
            _header = UiFactory.CreateText(headerRect, "Label", "FRONT", 15f, TextLight,
                TextAlignmentOptions.Left);

            for (int i = 0; i < SlotCount; i++)
            {
                float x = Pad + i * (SlotSize + SlotGap);
                RectTransform slot = UiFactory.CreateRect(
                    _panel, $"Die{i}",
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, -38f), new Vector2(SlotSize, SlotSize));
                _slots[i] = slot;

                // 描边：比骰位大 3px 的底图，只露出一圈边
                Image border = UiFactory.CreateImage(slot, "Border", BorderNone, false);
                border.rectTransform.offsetMin = new Vector2(-3f, -3f);
                border.rectTransform.offsetMax = new Vector2(3f, 3f);
                _borders[i] = border;

                Image body = UiFactory.CreateImage(slot, "Body", BodyPending, false);
                _bodies[i] = body;

                _texts[i] = UiFactory.CreateText(slot, "Value", "1", 30f, TextDark,
                    TextAlignmentOptions.Center);
                _texts[i].fontStyle = FontStyles.Bold;
            }

            RectTransform buttonRect = UiFactory.CreateRect(
                _panel, "Reroll",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(Pad, Pad), new Vector2(PanelWidth - 2f * Pad, 32f));

            Image buttonBackground = UiFactory.CreateImage(buttonRect, "Bg", ButtonColor, true);
            buttonBackground.raycastTarget = true;

            _rerollButton = buttonRect.gameObject.AddComponent<Button>();
            _rerollButton.targetGraphic = buttonBackground;
            _rerollButton.onClick.AddListener(() => RerollClicked?.Invoke());

            _rerollLabel = UiFactory.CreateText(buttonRect, "Label", "REROLL (1)", 15f, TextLight,
                TextAlignmentOptions.Center);
        }

        /// <summary>按当前局状态刷新三个骰位与按钮。</summary>
        public void Refresh(TowerRun run)
        {
            if (run == null || _header == null) return;

            int turnNumber = Mathf.Clamp(run.State.TurnIndex + 1, 1, run.Settings.TurnsPerRun);
            _header.text = $"{run.CurrentFace.ToString().ToUpperInvariant()}   TURN {turnNumber}/{run.Settings.TurnsPerRun}";

            bool placing = run.Phase == TurnPhase.Placing;
            int current = run.PlacedThisTurn;

            for (int i = 0; i < SlotCount; i++)
            {
                if (_bodies[i] == null) continue;

                bool hasDie = i < run.Dice.Count;
                bool used = !hasDie || i < current || run.IsFinished;
                bool isCurrent = placing && hasDie && i == current;

                _bodies[i].color = used ? BodyUsed : (isCurrent ? BodyCurrent : BodyPending);
                _texts[i].text = hasDie ? run.Dice[i].ToString() : "-";
                _texts[i].color = used ? TextDim : TextDark;
                _texts[i].fontStyle = isCurrent ? FontStyles.Bold : FontStyles.Normal;
                _borders[i].color = isCurrent ? BorderCurrent : BorderNone;
            }

            bool canReroll = placing && current == 0 && run.RerollsLeft > 0;
            _rerollButton.interactable = canReroll;
            _rerollLabel.color = canReroll ? TextLight : TextDim;
            _rerollLabel.text = $"REROLL ({run.RerollsLeft})";
        }

        /// <summary>鼠标下的骰位下标，-1 = 不在任何骰位上。</summary>
        public int HitDie(Vector2 screenPosition)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(_slots[i], screenPosition, null))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>鼠标是否落在托盘面板上（面板空白处也不该转球）。</summary>
        public bool Contains(Vector2 screenPosition)
        {
            return _panel != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPosition, null);
        }

        /// <summary>非法松手时 Ghost 飞回去的目标点。</summary>
        public Vector2 SlotScreenPosition(int index)
        {
            if (index < 0 || index >= _slots.Length || _slots[index] == null) return Vector2.zero;
            return RectTransformUtility.WorldToScreenPoint(null, _slots[index].position);
        }
    }
}
