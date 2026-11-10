using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 白盒期用代码搭 uGUI，省得手摆场景、也方便随时改布局。
    /// 画布用 Screen Space - Overlay + 1920×1080 基准，和文档 3.1 的分辨率一致。
    /// </summary>
    internal static class UiFactory
    {
        public static Canvas EnsureCanvas()
        {
            Canvas existing = Object.FindObjectOfType<Canvas>();
            if (existing != null)
            {
                EnsureEventSystem();
                return existing;
            }

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>uGUI 按钮要靠 EventSystem 派发点击。</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static RectTransform CreateRect(
            Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>纯色矩形。sprite 留空时 Image 会画一个实心方块，白盒够用。</summary>
        public static Image CreateImage(Transform parent, string name, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        public static TextMeshProUGUI CreateText(
            Transform parent, string name, string content,
            float fontSize, Color color, TextAlignmentOptions alignment)
        {            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }

        private static Font _uiFont;

        /// <summary>
        /// HUD 用的中文字体：TMP 默认字体没有中文字形，白盒阶段先走系统动态字体。
        /// 找不到系统字体时退回 Unity 内置字体（那样中文会缺字，但不会崩）。
        /// </summary>
        public static Font UiFont
        {
            get
            {
                if (_uiFont != null) return _uiFont;

                _uiFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "Arial" }, 24);

                if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _uiFont;
            }
        }

        /// <summary>uGUI 传统 Text（支持中文动态字形），锚点默认左上。</summary>
        public static Text CreateLabel(
            Transform parent, string name, string content,
            int fontSize, Color color, TextAnchor alignment,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;

            var text = go.GetComponent<Text>();
            text.font = UiFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
