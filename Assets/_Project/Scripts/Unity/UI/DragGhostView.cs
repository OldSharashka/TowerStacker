using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 拖数字时跟随鼠标的那一块（文档 3.7.3 的 DragGhost）。
    /// 只管“画在哪、什么颜色”，落不落子由 GameLoop 决定。
    /// 非法松手时飞回托盘，0.15s。
    /// </summary>
    public sealed class DragGhostView : MonoBehaviour
    {
        private const float FlyDuration = 0.15f;

        private RectTransform _rect;
        private Image _body;
        private TextMeshProUGUI _value;

        private bool _flying;
        private Vector2 _flyFrom;
        private Vector2 _flyTo;
        private float _flyElapsed;

        private static readonly Color ValidBody = new Color(0.95f, 0.96f, 0.98f, 0.96f);
        private static readonly Color InvalidBody = new Color(0.86f, 0.30f, 0.28f, 0.85f);
        private static readonly Color TextDark = new Color(0.08f, 0.09f, 0.12f);
        private static readonly Color TextLight = new Color(0.98f, 0.98f, 1f, 0.95f);

        public static DragGhostView Create(Transform canvasRoot)
        {
            var go = new GameObject("DragGhost", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<DragGhostView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            _rect = GetComponent<RectTransform>();
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = new Vector2(76f, 76f);

            _body = UiFactory.CreateImage(transform, "Body", ValidBody, false);
            _value = UiFactory.CreateText(transform, "Value", "1", 44f, TextDark, TextAlignmentOptions.Center);
            _value.fontStyle = FontStyles.Bold;

            gameObject.SetActive(false);
        }

        public void Show(int value)
        {
            _flying = false;
            gameObject.SetActive(true);
            _value.text = value.ToString();
            SetValid(true);
            transform.localScale = Vector3.one * 0.85f;
            transform.SetAsLastSibling();
        }

        /// <summary>屏幕坐标（Overlay 画布的 world 坐标就是屏幕像素）。</summary>
        public void SetPosition(Vector2 screenPosition)
        {
            if (_flying) return;
            _rect.position = screenPosition;
        }

        public void SetValid(bool valid)
        {
            _body.color = valid ? ValidBody : InvalidBody;
            _value.color = valid ? TextDark : TextLight;
        }

        /// <summary>非法松手：飞回托盘里的骰位。</summary>
        public void FlyBack(Vector2 screenTarget)
        {
            _flying = true;
            _flyFrom = _rect.position;
            _flyTo = screenTarget;
            _flyElapsed = 0f;
        }

        public void Hide()
        {
            _flying = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_flying) return;

            _flyElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_flyElapsed / FlyDuration);
            float eased = 1f - (1f - t) * (1f - t);

            _rect.position = Vector2.Lerp(_flyFrom, _flyTo, eased);
            transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 0.45f, eased);

            if (t >= 1f) Hide();
        }
    }
}
