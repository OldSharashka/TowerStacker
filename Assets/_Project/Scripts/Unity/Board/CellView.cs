using TMPro;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>单个格子的视图：颜色 + 数字文字 + 拾取入口。</summary>
    public sealed class CellView : MonoBehaviour
    {
        public int CellId;

        [SerializeField] private Renderer _face;
        [SerializeField] private TextMeshPro _label;

        public TextMeshPro Label => _label;
        public Renderer Face => _face;

        private MaterialPropertyBlock _block;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public void Bind(int cellId, Renderer face, TextMeshPro label)
        {
            CellId = cellId;
            _face = face;
            _label = label;
        }

        public void SetColor(Color color)
        {
            if (_face == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();

            _face.GetPropertyBlock(_block);
            _block.SetColor(ColorId, color);
            _face.SetPropertyBlock(_block);
        }

        public void SetText(string text, Color color)
        {
            if (_label == null) return;
            _label.text = text ?? string.Empty;
            _label.color = color;
        }
    }
}
