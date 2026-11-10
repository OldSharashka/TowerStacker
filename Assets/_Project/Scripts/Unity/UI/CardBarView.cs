using System.Collections.Generic;
using Tower.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 顶部居中的 3 个卡槽：显示当前常驻卡。空槽显示「空」。
    /// 只读展示，不接收点击（替换在卡牌面板里做）。
    /// </summary>
    public sealed class CardBarView : MonoBehaviour
    {
        private const int SlotCount = 3;
        private const float SlotWidth = 160f;
        private const float SlotHeight = 64f;
        private const float SlotGap = 8f;

        private readonly Image[] _bodies = new Image[SlotCount];
        private readonly Text[] _names = new Text[SlotCount];
        private readonly Text[] _texts = new Text[SlotCount];

        private static readonly Color EmptyBody = new Color(0.09f, 0.10f, 0.14f, 0.78f);
        private static readonly Color FilledBody = new Color(0.15f, 0.17f, 0.23f, 0.92f);
        private static readonly Color NameColor = new Color(0.92f, 0.93f, 0.96f);
        private static readonly Color TextColor = new Color(0.62f, 0.65f, 0.72f);
        private static readonly Color EmptyColor = new Color(0.42f, 0.45f, 0.51f);

        public static CardBarView Create(Transform canvasRoot)
        {
            var go = new GameObject("CardBar", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<CardBarView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            var root = GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -16f);
            root.sizeDelta = new Vector2(
                SlotCount * SlotWidth + (SlotCount - 1) * SlotGap, SlotHeight);

            for (int i = 0; i < SlotCount; i++)
            {
                float x = i * (SlotWidth + SlotGap);
                RectTransform slot = UiFactory.CreateRect(
                    root, $"Slot{i}",
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, 0f), new Vector2(SlotWidth, SlotHeight));

                _bodies[i] = UiFactory.CreateImage(slot, "Bg", EmptyBody, false);

                _names[i] = UiFactory.CreateLabel(slot, "Name", "空", 15, EmptyColor,
                    TextAnchor.MiddleCenter, new Vector2(6f, -6f), new Vector2(SlotWidth - 12f, 22f));

                _texts[i] = UiFactory.CreateLabel(slot, "Text", string.Empty, 11, TextColor,
                    TextAnchor.UpperCenter, new Vector2(6f, -30f), new Vector2(SlotWidth - 12f, 30f));
                _texts[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _texts[i].verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        public void Refresh(IReadOnlyList<ICardEffect> slots)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                ICardEffect card = slots != null && i < slots.Count ? slots[i] : null;

                if (card == null)
                {
                    _bodies[i].color = EmptyBody;
                    _names[i].text = "空";
                    _names[i].color = EmptyColor;
                    _texts[i].text = string.Empty;
                }
                else
                {
                    _bodies[i].color = FilledBody;
                    _names[i].text = card.Name;
                    _names[i].color = NameColor;
                    _texts[i].text = card.Text;
                    _texts[i].color = TextColor;
                }
            }
        }
    }
}
