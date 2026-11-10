using System;
using System.Collections.Generic;
using TMPro;
using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 把纯逻辑的 TowerRun 接到球面视图上：点击落子、逐邻居跳分、回合高亮。
    /// 白盒期先不做拖拽与转场，那些在后面几步。
    /// </summary>
    public sealed class GameLoop : MonoBehaviour
    {
        [Header("引用")]
        public PlanetMeshBuilder Planet;
        public Camera Cam;

        [Header("一局参数")]
        public ulong Seed = 20261110UL;
        public int TargetScore = 170;

        [Tooltip(">0 时开局自动放 N 个回合（只用于截图与调试），0 = 手动")]
        public int AutoPlayTurns;

        private TowerRun _run;
        private BoardTopology _topology;

        private readonly List<Floating> _floatings = new List<Floating>();
        private string _message = string.Empty;
        private float _messageUntil;

        private sealed class Floating
        {
            public GameObject Go;
            public TextMeshPro Text;
            public Vector3 Origin;
            public float Age;
            public float Delay;
            public float Life = 0.8f;
            public Color BaseColor;
        }

        private void Start()
        {
            if (Planet == null) Planet = FindObjectOfType<PlanetMeshBuilder>();
            if (Cam == null) Cam = Camera.main;

            NewRun();

            if (AutoPlayTurns > 0) AutoPlay(AutoPlayTurns);
        }

        private void OnDestroy()
        {
            if (_run == null) return;
            _run.TurnStarted -= OnTurnStarted;
            _run.Placed -= OnPlaced;
            _run.RunFinished -= OnRunFinished;
        }

        public void NewRun()
        {
            _topology = BoardTopology.Build();
            var settings = new RunSettings { TargetScore = TargetScore };

            _run = new TowerRun(Seed, _topology, ScoringConfig.Default, settings);
            _run.TurnStarted += OnTurnStarted;
            _run.Placed += OnPlaced;
            _run.RunFinished += OnRunFinished;

            if (Planet != null) Planet.ResetAll();
            _run.Start();
        }

        private void Update()
        {
            HandleClick();
            HandleKeys();
            AnimateFloating(Time.deltaTime);
        }

        private void HandleKeys()
        {
            if (_run == null) return;

            if (Input.GetKeyDown(KeyCode.R) && _run.Phase == TurnPhase.Placing)
            {
                _run.Reroll(0b111);
            }
            if (Input.GetKeyDown(KeyCode.N))
            {
                NewRun();
            }
        }

        private void HandleClick()
        {
            if (_run == null || _run.Phase != TurnPhase.Placing) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (Cam == null || Planet == null) return;

            Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

            CellView view = hit.collider.GetComponentInParent<CellView>();
            if (view == null) return;

            TryPlace(view.CellId);
        }

        private void TryPlace(int cellId)
        {
            try
            {
                if (!_run.State.IsEmpty(cellId))
                {
                    ShowMessage("这一格已经有数字了");
                    return;
                }
                if (Tower.Core.Board.FaceOf(cellId) != _run.CurrentFace)
                {
                    ShowMessage($"只能放在当前区域（{_run.CurrentFace}）");
                    return;
                }

                _run.Place(cellId);
            }
            catch (InvalidOperationException ex)
            {
                ShowMessage(ex.Message);
            }
        }

        private void OnTurnStarted(TowerRun run)
        {
            if (Planet != null) Planet.HighlightRegion(run.CurrentFace);
            ShowMessage($"第 {run.State.TurnIndex + 1} 回合 · {run.CurrentFace} 区域");
        }

        private void OnPlaced(PlacementResult result)
        {
            if (Planet != null)
            {
                Planet.SetCellValue(result.CellId, result.Value);
                Planet.HighlightRegion(_run.CurrentFace);
            }

            // 逐个邻居跳出得分：顺序由 Core 给出（固定），间隔 0.08s
            float delay = 0f;
            foreach (ScoreContribution contribution in result.Contributions)
            {
                CellView source = Planet != null ? Planet.GetCell(contribution.SourceCellId) : null;
                if (source == null || source.Face == null) continue;

                SpawnFloating(source.Face.bounds.center, $"+{contribution.Amount}",
                    new Color(1f, 0.93f, 0.65f), delay);
                delay += 0.08f;
            }

            if (result.Score > 0)
            {
                CellView target = Planet != null ? Planet.GetCell(result.CellId) : null;
                Vector3 pos = target != null && target.Face != null ? target.Face.bounds.center : Vector3.zero;
                SpawnFloating(pos, $"+{result.Score}", new Color(1f, 0.72f, 0.25f), delay + 0.05f);
            }
        }

        private void OnRunFinished(TowerRun run)
        {
            string verdict = run.IsSuccess ? "达标" : "未达标";
            ShowMessage($"结算：{run.State.TotalScore} / {run.State.TargetScore}  {verdict}", 30f);
            if (Planet != null) Planet.HighlightRegion(null);
        }

        private void SpawnFloating(Vector3 worldPos, string text, Color color, float delay)
        {
            var go = new GameObject("Fx");
            go.transform.position = worldPos;

            var tmp = go.AddComponent<TextMeshPro>();
            if (Planet != null && Planet.font != null) tmp.font = Planet.font;
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = 6f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(0.7f, 0.35f);
            tmp.sortingOrder = 100;
            tmp.raycastTarget = false;

            go.transform.rotation = Cam != null ? Cam.transform.rotation : Quaternion.identity;

            _floatings.Add(new Floating
            {
                Go = go,
                Text = tmp,
                Origin = worldPos,
                Delay = delay,
                BaseColor = color,
            });
        }

        private void AnimateFloating(float dt)
        {
            for (int i = _floatings.Count - 1; i >= 0; i--)
            {
                Floating f = _floatings[i];
                f.Age += dt;

                if (f.Age < f.Delay) continue;

                float t = (f.Age - f.Delay) / f.Life;
                if (t >= 1f || f.Go == null)
                {
                    if (f.Go != null) Destroy(f.Go);
                    _floatings.RemoveAt(i);
                    continue;
                }

                f.Go.transform.position = f.Origin + Vector3.up * (t * 0.25f);

                Color c = f.BaseColor;
                c.a = 1f - t * t;
                f.Text.color = c;
            }
        }

        private void ShowMessage(string text, float seconds = 2.5f)
        {
            _message = text;
            _messageUntil = Time.unscaledTime + seconds;
        }

        /// <summary>调试/截图用：自动打 N 个回合（每步取当前最高分）。</summary>
        public void AutoPlay(int turns)
        {
            int guard = 0;
            while (_run != null && !_run.IsFinished && _run.State.TurnIndex < turns)
            {
                if (++guard > 600)
                {
                    Debug.LogWarning("AutoPlay 没有收敛，已中止");
                    return;
                }
                if (_run.Phase != TurnPhase.Placing) return;

                int value = _run.Dice[_run.PlacedThisTurn];
                int bestCell = -1;
                int bestScore = int.MinValue;

                foreach (int cell in _run.EmptyCellsOnCurrentFace())
                {
                    ScoringResult probe = Scorer.Evaluate(_topology, _run.State, cell, value, _run.Scoring);
                    if (probe.Total > bestScore)
                    {
                        bestScore = probe.Total;
                        bestCell = cell;
                    }
                }

                if (bestCell < 0) return;
                _run.Place(bestCell);
            }
        }

        private void OnGUI()
        {
            if (_run == null) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            var panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };

            GUILayout.BeginArea(new Rect(12, 12, 340, 200), panel);
            GUILayout.Label("<b>高塔堆叠者</b> · 白盒", style);
            GUILayout.Label($"回合 {_run.State.TurnIndex + 1}/18    轮 {_run.State.RoundIndex + 1}/3", style);
            GUILayout.Label($"分数 <b>{_run.State.TotalScore}</b> / 目标 {_run.State.TargetScore}", style);
            GUILayout.Label($"当前区域 <b>{_run.CurrentFace}</b>    重掷剩 {_run.RerollsLeft}", style);

            if (_run.Phase == TurnPhase.Placing)
            {
                var dice = new System.Text.StringBuilder("骰子 ");
                for (int i = 0; i < _run.Dice.Count; i++)
                {
                    dice.Append(i == _run.PlacedThisTurn ? $"[<b>{_run.Dice[i]}</b>] " : $"{_run.Dice[i]} ");
                }
                GUILayout.Label(dice.ToString(), style);
                GUILayout.Label("点空格落子　R 重掷　N 重开", style);
            }
            else if (_run.Phase == TurnPhase.RunEnded)
            {
                GUILayout.Label(_run.IsSuccess ? "<color=#7f7>达标</color>" : "<color=#f77>未达标</color>", style);
                GUILayout.Label("N 重开", style);
            }

            GUILayout.EndArea();

            if (!string.IsNullOrEmpty(_message) && Time.unscaledTime < _messageUntil)
            {
                GUI.Label(new Rect(12, Screen.height - 40, 700, 28), _message, style);
            }
        }
    }
}
