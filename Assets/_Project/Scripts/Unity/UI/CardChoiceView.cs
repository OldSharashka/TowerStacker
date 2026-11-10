using System;
using System.Collections.Generic;
using Tower.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 三选一 / 满槽替换面板（文档 §13.4）：
    /// 点一张卡 → 有空槽直接选中；满槽则切到「选槽位替换」，可取消回到三选一。
    /// </summary>
    public sealed class CardChoiceView : MonoBehaviour
    {
        private const int SlotCount = 3;

        private GameObject _root;
        private Text _title;

        private readonly RectTransform[] _cardRoots = new RectTransform[3];
        private readonly Image[] _cardBodies = new Image[3];
        private readonly Text[] _cardKinds = new Text[3];
        private readonly Text[] _cardNames = new Text[3];
        private readonly Text[] _cardTexts = new Text[3];

        private readonly RectTransform[] _slotRoots = new RectTransform[SlotCount];
        private readonly Text[] _slotLabels = new Text[SlotCount];
        private RectTransform _cancelRoot;

        private IReadOnlyList<CardDefinition> _offered;
        private IReadOnlyList<ICardEffect> _slots;
        private int _picked = -1;
        private bool _replaceMode;
        private string _baseTitle = "选择一张卡";

        public event Action<int> CardPicked;
        public event Action<int, int> CardReplaced;

        public bool IsVisible => _root != null && _root.activeSelf;

        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.145f, 0.98f);
        private static readonly Color CardColor = new Color(0.17f, 0.19f, 0.25f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.31f);
        private static readonly Color TextMain = new Color(0.92f, 0.94f, 0.97f);
        private static readonly Color TextDim = new Color(0.64f, 0.67f, 0.74f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.25f);

        public static CardChoiceView Create(Transform canvasRoot)
        {
            var go = new GameObject("CardChoice", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<CardChoiceView>();
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
                Vector2.zero, new Vector2(820f, 460f));
            UiFactory.CreateImage(panel, "Bg", PanelColor, true);

            _title = UiFactory.CreateLabel(panel, "Title", "选择一张卡", 24, TextMain,
                TextAnchor.MiddleCenter, new Vector2(0f, -20f), new Vector2(780f, 34f));
            RectTransform titleRect = _title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -20f);

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 260f;
                RectTransform card = UiFactory.CreateRect(
                    panel, $"Card{i}",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, -20f), new Vector2(240f, 300f));
                _cardRoots[i] = card;

                Image body = UiFactory.CreateImage(card, "Bg", CardColor, true);
                body.raycastTarget = true;
                _cardBodies[i] = body;

                _cardKinds[i] = UiFactory.CreateLabel(card, "Kind", string.Empty, 13, Accent,
                    TextAnchor.MiddleCenter, new Vector2(10f, -12f), new Vector2(220f, 20f));
                RectTransform kindRect = _cardKinds[i].rectTransform;
                kindRect.anchorMin = new Vector2(0.5f, 1f);
                kindRect.anchorMax = new Vector2(0.5f, 1f);
                kindRect.pivot = new Vector2(0.5f, 1f);
                kindRect.anchoredPosition = new Vector2(0f, -12f);

                _cardNames[i] = UiFactory.CreateLabel(card, "Name", string.Empty, 22, TextMain,
                    TextAnchor.MiddleCenter, new Vector2(0f, -46f), new Vector2(220f, 32f));
                RectTransform nameRect = _cardNames[i].rectTransform;
                nameRect.anchorMin = new Vector2(0.5f, 1f);
                nameRect.anchorMax = new Vector2(0.5f, 1f);
                nameRect.pivot = new Vector2(0.5f, 1f);
                nameRect.anchoredPosition = new Vector2(0f, -46f);
                _cardNames[i].fontStyle = FontStyle.Bold;

                _cardTexts[i] = UiFactory.CreateLabel(card, "Text", string.Empty, 15, TextDim,
                    TextAnchor.UpperCenter, new Vector2(14f, -96f), new Vector2(212f, 180f));
                _cardTexts[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _cardTexts[i].verticalOverflow = VerticalWrapMode.Truncate;

                int index = i;
                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = body;
                button.onClick.AddListener(() => OnCardClicked(index));
            }

            for (int i = 0; i < SlotCount; i++)
            {
                RectTransform slot = UiFactory.CreateRect(
                    panel, $"Replace{i}",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 90f - i * 80f), new Vector2(560f, 62f));
                _slotRoots[i] = slot;

                Image body = UiFactory.CreateImage(slot, "Bg", ButtonColor, true);
                body.raycastTarget = true;

                _slotLabels[i] = UiFactory.CreateLabel(slot, "Label", string.Empty, 18, TextMain,
                    TextAnchor.MiddleCenter, Vector2.zero, new Vector2(560f, 62f));
                RectTransform labelRect = _slotLabels[i].rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                int index = i;
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = body;
                button.onClick.AddListener(() => OnSlotClicked(index));

                slot.gameObject.SetActive(false);
            }

            _cancelRoot = UiFactory.CreateRect(
                panel, "Cancel",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(140f, 40f));
            Image cancelBg = UiFactory.CreateImage(_cancelRoot, "Bg", ButtonColor, true);
            cancelBg.raycastTarget = true;
            var cancelButton = _cancelRoot.gameObject.AddComponent<Button>();
            cancelButton.targetGraphic = cancelBg;
            cancelButton.onClick.AddListener(OnCancelClicked);

            Text cancelLabel = UiFactory.CreateLabel(_cancelRoot, "Label", "取消", 16, TextMain,
                TextAnchor.MiddleCenter, Vector2.zero, new Vector2(140f, 40f));
            RectTransform cancelRect = cancelLabel.rectTransform;
            cancelRect.anchorMin = Vector2.zero;
            cancelRect.anchorMax = Vector2.one;
            cancelRect.offsetMin = Vector2.zero;
            cancelRect.offsetMax = Vector2.zero;

            _cancelRoot.gameObject.SetActive(false);
        }

        public void Show(IReadOnlyList<CardDefinition> offered, IReadOnlyList<ICardEffect> slots,
            int drawNumber, int totalDraws)
        {
            _offered = offered;
            _slots = slots;
            _picked = -1;
            _replaceMode = false;

            _title.text = $"选择一张卡（第 {drawNumber}/{totalDraws} 次）";
            _baseTitle = _title.text;

            for (int i = 0; i < 3; i++)
            {
                CardDefinition card = offered != null && i < offered.Count ? offered[i] : null;
                if (card == null)
                {
                    _cardRoots[i].gameObject.SetActive(false);
                    continue;
                }

                _cardRoots[i].gameObject.SetActive(true);
                _cardKinds[i].text = KindLabel(card.Kind);
                _cardNames[i].text = card.Name;
                _cardTexts[i].text = card.Text;
            }

            foreach (RectTransform slot in _slotRoots) slot.gameObject.SetActive(false);
            _cancelRoot.gameObject.SetActive(false);

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void OnCardClicked(int index)
        {
            if (_replaceMode || _offered == null || index >= _offered.Count) return;

            _picked = index;

            bool hasEmpty = false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots == null || i >= _slots.Count || _slots[i] == null) { hasEmpty = true; break; }
            }

            if (hasEmpty)
            {
                CardPicked?.Invoke(index);
                return;
            }

            _replaceMode = true;
            ApplyReplaceMode();
        }

        private void OnSlotClicked(int slot)
        {
            if (!_replaceMode || _picked < 0) return;
            CardReplaced?.Invoke(_picked, slot);
        }

        private void OnCancelClicked()
        {
            _replaceMode = false;
            _picked = -1;
            ApplyReplaceMode();
        }

        private void ApplyReplaceMode()
        {
            _title.text = _replaceMode ? "选择要替换的卡槽" : _baseTitle;

            foreach (RectTransform card in _cardRoots) card.gameObject.SetActive(!_replaceMode);

            for (int i = 0; i < SlotCount; i++)
            {
                _slotRoots[i].gameObject.SetActive(_replaceMode);

                ICardEffect card = _slots != null && i < _slots.Count ? _slots[i] : null;
                _slotLabels[i].text = card == null ? $"槽 {i + 1} · 空" : $"槽 {i + 1} · {card.Name}";
            }

            _cancelRoot.gameObject.SetActive(_replaceMode);
        }

        private static string KindLabel(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.AddRule: return "新规则";
                case CardKind.ModifyRule: return "数值修正";
                case CardKind.Multiply: return "乘区";
                case CardKind.Delayed: return "延迟收益";
                default: return string.Empty;
            }
        }
    }
}
