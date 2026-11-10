using Tower.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tower.Unity.View
{
    /// <summary>
    /// 正式 HUD（替换之前的 OnGUI 临时面板）：回合 / 轮 / 分数 / 进度条 / 区域 / 重掷，
    /// 以及底部提示行和右下角事件日志。
    /// 文字用 uGUI Text + 系统动态字体：TMP 默认字体没有中文字形，白盒阶段先用这个，
    /// 等接入中文 TMP 字体资源再统一换回去。
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private const float PanelWidth = 440f;
        private const float BarWidth = 408f;

        private Text _turn;
        private Text _score;
        private Text _region;
        private Text _hint;
        private Text _message;
        private Text _log;
        private RectTransform _barFill;

        private string _lastHint = string.Empty;
        private string _lastMessage = string.Empty;
        private readonly string[] _logLines = new string[3];

        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.90f);
        private static readonly Color TextMain = new Color(0.90f, 0.92f, 0.95f);
        private static readonly Color TextDim = new Color(0.62f, 0.65f, 0.72f);
        private static readonly Color BarBack = new Color(0.18f, 0.20f, 0.24f, 1f);
        private static readonly Color BarFillColor = new Color(1f, 0.72f, 0.25f, 1f);

        public static HudView Create(Transform canvasRoot)
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);

            var view = go.AddComponent<HudView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            var root = GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            RectTransform panel = UiFactory.CreateRect(
                transform, "Panel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -24f), new Vector2(PanelWidth, 176f));
            UiFactory.CreateImage(panel, "Bg", PanelColor, false);

            UiFactory.CreateLabel(panel, "Title", "高塔堆叠者 · 白盒", 17, TextDim, TextAnchor.MiddleLeft,
                new Vector2(16f, -12f), new Vector2(PanelWidth - 32f, 24f));

            _turn = UiFactory.CreateLabel(panel, "Turn", "回合 01/18    轮 1/3", 21, TextMain, TextAnchor.MiddleLeft,
                new Vector2(16f, -40f), new Vector2(PanelWidth - 32f, 28f));

            _score = UiFactory.CreateLabel(panel, "Score", "分数 0 / 170", 26, TextMain, TextAnchor.MiddleLeft,
                new Vector2(16f, -72f), new Vector2(PanelWidth - 32f, 34f));
            _score.fontStyle = FontStyle.Bold;

            RectTransform bar = UiFactory.CreateRect(
                panel, "Bar",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -112f), new Vector2(BarWidth, 12f));
            UiFactory.CreateImage(bar, "Back", BarBack, false);

            _barFill = UiFactory.CreateRect(
                bar, "Fill",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(0f, 0f));
            UiFactory.CreateImage(_barFill, "Color", BarFillColor, false);

            _region = UiFactory.CreateLabel(panel, "Region", "区域 FRONT · 剩 9 格 · 重掷 1", 17, TextDim, TextAnchor.MiddleLeft,
                new Vector2(16f, -130f), new Vector2(PanelWidth - 32f, 24f));

            _hint = UiFactory.CreateLabel(
                transform, "Hint", "拖左下角的骰子到格子里", 22, TextMain, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(1200f, 32f));
            RectTransform hintRect = _hint.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 200f);

            _message = UiFactory.CreateLabel(
                transform, "Message", string.Empty, 22, new Color(1f, 0.78f, 0.45f), TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(1200f, 30f));
            RectTransform messageRect = _message.rectTransform;
            messageRect.anchorMin = new Vector2(0.5f, 0f);
            messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.pivot = new Vector2(0.5f, 0f);
            messageRect.anchoredPosition = new Vector2(0f, 168f);

            _log = UiFactory.CreateLabel(
                transform, "Log", string.Empty, 16, TextDim, TextAnchor.LowerRight,
                Vector2.zero, new Vector2(560f, 96f));
            RectTransform logRect = _log.rectTransform;
            logRect.anchorMin = new Vector2(1f, 0f);
            logRect.anchorMax = new Vector2(1f, 0f);
            logRect.pivot = new Vector2(1f, 0f);
            logRect.anchoredPosition = new Vector2(-24f, 24f);
        }

        /// <summary>每回合/每次落子后刷新。pending 时显示刚打完的那一回合（Core 已经切到下回合了）。</summary>
        public void Refresh(TowerRun run, bool pending, FaceId lastFace, int lastDiceCount)
        {
            if (run == null) return;

            int turnNumber = pending ? Mathf.Max(1, run.State.TurnIndex) : run.State.TurnIndex + 1;
            turnNumber = Mathf.Clamp(turnNumber, 1, run.Settings.TurnsPerRun);
            int roundNumber = (turnNumber - 1) / run.Settings.TurnsPerRound + 1;
            FaceId face = pending ? lastFace : run.CurrentFace;
            int placed = pending ? lastDiceCount : run.PlacedThisTurn;
            int diceCount = pending ? lastDiceCount : run.Dice.Count;

            _turn.text = $"回合 {turnNumber:00}/{run.Settings.TurnsPerRun}    轮 {roundNumber}/{run.Settings.RoundsPerRun}";

            _score.text = $"分数 {run.State.TotalScore} / {run.State.TargetScore}";

            int remaining = run.State.Faces[(int)face].EmptyCount;
            _region.text = $"区域 {face} · 剩 {remaining} 格 · 重掷 {run.RerollsLeft} · 已放 {placed}/{diceCount}";

            float ratio = run.State.TargetScore > 0
                ? Mathf.Clamp01(run.State.TotalScore / (float)run.State.TargetScore)
                : 0f;
            _barFill.sizeDelta = new Vector2(BarWidth * ratio, 0f);
        }

        public void SetHint(string text)
        {
            if (_lastHint == text) return;
            _lastHint = text;
            _hint.text = text;
        }

        public void SetMessage(string text)
        {
            if (_lastMessage == text) return;
            _lastMessage = text;
            _message.text = text;
        }

        /// <summary>右下角事件日志，保留最近 3 条。</summary>
        public void PushLog(string line)        {
            _logLines[0] = _logLines[1];
            _logLines[1] = _logLines[2];
            _logLines[2] = line;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _logLines.Length; i++)
            {
                if (string.IsNullOrEmpty(_logLines[i])) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(_logLines[i]);
            }
            _log.text = sb.ToString();
        }

        public void ClearLog()
        {
            for (int i = 0; i < _logLines.Length; i++) _logLines[i] = null;
            _log.text = string.Empty;
        }
    }
}
