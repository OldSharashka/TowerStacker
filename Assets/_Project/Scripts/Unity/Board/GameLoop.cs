using System;
using System.Collections.Generic;
using TMPro;
using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 把纯逻辑的 TowerRun 接到球面视图上：拖拽落子、逐邻居跳分、回合高亮。
    /// 输入分流按「按下位置」分（文档 3.7.1）：托盘里拖数字，球面上转球，不靠长按猜。
    /// </summary>
    public sealed class GameLoop : MonoBehaviour
    {
        [Header("引用")]
        public PlanetMeshBuilder Planet;
        public PlanetSpin Spin;
        public Camera Cam;

        [Header("一局参数")]
        public ulong Seed = 20261110UL;
        public int TargetScore = 170;

        [Tooltip(">0 时开局自动放 N 个回合（只用于截图与调试），0 = 手动")]
        public int AutoPlayTurns;

        [Tooltip("调试用：把跳分飘字放慢，方便观察/截图")]
        public bool slowPopups;

        [Header("交互")]
        [Tooltip("从托盘拖出数字：位移超过这个像素就进入拖拽")]
        public float dragThresholdPx = 6f;

        [Tooltip("或者按住超过这个秒数也算进入拖拽（两个条件先到先算）")]
        public float dragHoldSeconds = 0.12f;

        [Tooltip("调试开关：允许直接点击空格落子（默认关，正式交互是从托盘拖）")]
        public bool clickToPlaceForDebug;

        [Tooltip("拖动中可放置格的悬停色")]
        public Color hoverColor = new Color(0.55f, 0.92f, 0.95f);

        [Tooltip("点击选中格的颜色")]
        public Color selectedColor = new Color(1f, 0.82f, 0.25f);

        private TowerRun _run;
        private BoardTopology _topology;
        private DiceTrayView _tray;
        private DragGhostView _ghost;

        private readonly bool[] _rerollSelected = new bool[3];

        private enum PointerMode
        {
            None,
            SpherePress,
            DiePress,
            Dragging,
        }

        private PointerMode _mode;
        private Vector2 _pressPos;
        private Vector2 _lastMouse;
        private float _pressTime;
        private bool _sphereMoved;
        private int _pressedCell = -1;
        private int _dragDie = -1;
        private int _hoverCell = -1;
        private bool _hoverValid;
        private int _hoverScore;
        private int _previewCell = -1;
        private int _selectedCell = -1;
        private int _selectedScore;

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
            if (Planet != null && Planet.Cells.Count == 0) Planet.Rebuild();   // 兜底：编辑模式没建过
            if (Spin == null) Spin = GetComponent<PlanetSpin>();
            if (Spin == null) Spin = gameObject.AddComponent<PlanetSpin>();   // 白盒期自动挂，省得手动配
            if (Cam == null) Cam = Camera.main;

            Canvas canvas = UiFactory.EnsureCanvas();
            if (canvas != null)
            {
                _tray = DiceTrayView.Create(canvas.transform);
                _tray.RerollClicked += OnRerollClicked;
                _ghost = DragGhostView.Create(canvas.transform);
            }

            NewRun();

            if (AutoPlayTurns > 0) AutoPlay(AutoPlayTurns);
        }

        private void OnDestroy()
        {
            if (_tray != null) _tray.RerollClicked -= OnRerollClicked;
            UnsubscribeRun();
        }

        private void UnsubscribeRun()
        {
            if (_run == null) return;
            _run.TurnStarted -= OnTurnStarted;
            _run.Placed -= OnPlaced;
            _run.RunFinished -= OnRunFinished;
        }

        public void NewRun()
        {
            UnsubscribeRun();
            CancelPointer();
            ClearSelection();
            ClearRerollSelection();
            ClearFloating();
            _message = string.Empty;

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
            if (_run == null) return;

            HandlePointer();
            HandleKeys();
            AnimateFloating(Time.deltaTime);
        }

        // ------------------------------------------------------------------
        // 输入：按下位置分流（文档 3.7.1）
        // ------------------------------------------------------------------

        private void HandlePointer()
        {
            // 转场中锁输入：拖到一半进入转场也不会残留 Ghost
            if (Spin != null && Spin.IsTransitioning)
            {
                if (_mode != PointerMode.None || _previewCell >= 0) CancelPointer();
                return;
            }

            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                OnPress(mouse);
                return;
            }

            if (Input.GetMouseButton(0))
            {
                OnHold(mouse);
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                OnRelease();
            }
        }

        private void OnPress(Vector2 mouse)
        {
            _pressPos = mouse;
            _lastMouse = mouse;
            _pressTime = Time.unscaledTime;
            _sphereMoved = false;
            _pressedCell = -1;
            _dragDie = -1;
            _mode = PointerMode.None;

            if (_tray != null)
            {
                int die = _tray.HitDie(mouse);
                if (die >= 0 && _run.Phase == TurnPhase.Placing)
                {
                    _mode = PointerMode.DiePress;
                    _dragDie = die;
                    return;
                }

                // 托盘面板上的空白/按钮：不转球，也不落子
                if (_tray.Contains(mouse)) return;
            }

            _mode = PointerMode.SpherePress;
            _pressedCell = PickCell();
        }

        private void OnHold(Vector2 mouse)
        {
            Vector2 delta = mouse - _lastMouse;
            _lastMouse = mouse;

            bool pastThreshold = (mouse - _pressPos).sqrMagnitude > dragThresholdPx * dragThresholdPx
                                 || Time.unscaledTime - _pressTime > dragHoldSeconds;

            switch (_mode)
            {
                case PointerMode.DiePress:
                    if (!pastThreshold) return;
                    if (_dragDie == _run.PlacedThisTurn)
                    {
                        BeginDrag(mouse);
                    }
                    else
                    {
                        _mode = PointerMode.None;
                        ShowMessage("只能拖当前这颗骰子（按骰序放置）");
                    }
                    break;

                case PointerMode.Dragging:
                    UpdateDrag(mouse);
                    break;

                case PointerMode.SpherePress:
                    if (!_sphereMoved && pastThreshold) _sphereMoved = true;
                    if (_sphereMoved && delta.sqrMagnitude > 0.0001f && Spin != null)
                    {
                        Spin.RotateBy(delta.x, delta.y, Cam);
                    }
                    break;
            }
        }

        private void OnRelease()
        {
            PointerMode mode = _mode;
            int die = _dragDie;
            int cell = _hoverCell;
            bool valid = _hoverValid;
            bool sphereMoved = _sphereMoved;
            int pressedCell = _pressedCell;

            ResetPointerState();

            switch (mode)
            {
                case PointerMode.Dragging:
                    if (valid && cell >= 0)
                    {
                        ClearHoverHighlight();
                        PlaceAt(cell);
                        if (_ghost != null) _ghost.Hide();
                    }
                    else
                    {
                        if (_ghost != null)
                        {
                            if (_tray != null) _ghost.FlyBack(_tray.SlotScreenPosition(die));
                            else _ghost.Hide();
                        }
                        ShowMessage("只能放在当前区域的空格里");
                    }
                    break;

                case PointerMode.DiePress:
                    if (die >= 0) HandleDieClick(die);
                    break;

                case PointerMode.SpherePress:
                    if (!sphereMoved && pressedCell >= 0) HandleCellClick(pressedCell);
                    break;
            }
        }

        private void BeginDrag(Vector2 mouse)
        {
            if (_run.Phase != TurnPhase.Placing) return;
            if (_dragDie != _run.PlacedThisTurn) return;

            _mode = PointerMode.Dragging;
            ClearSelection();

            if (_ghost != null)
            {
                _ghost.Show(_run.Dice[_dragDie]);
                _ghost.SetPosition(mouse);
            }

            UpdateDrag(mouse);
        }

        private void UpdateDrag(Vector2 mouse)
        {
            if (_ghost != null) _ghost.SetPosition(mouse);

            int cell = PickCell(mouse);
            bool valid = cell >= 0
                         && _dragDie == _run.PlacedThisTurn
                         && _run.State.IsEmpty(cell)
                         && Board.FaceOf(cell) == _run.CurrentFace;

            _hoverValid = valid;
            _hoverCell = valid ? cell : -1;
            _hoverScore = valid ? PreviewScoreFor(cell) : 0;

            if (_ghost != null) _ghost.SetValid(valid || cell < 0);

            if (valid)
            {
                if (_previewCell != cell)
                {
                    _previewCell = cell;
                    if (Planet != null) Planet.PreviewCell(cell, hoverColor);
                }
            }
            else
            {
                ClearHoverHighlight();
            }
        }

        private void ClearHoverHighlight()
        {
            if (_previewCell < 0) return;
            _previewCell = -1;
            if (Planet != null) Planet.ClearPreview();
        }

        private void CancelPointer()
        {
            if (_mode == PointerMode.Dragging && _ghost != null) _ghost.Hide();

            ResetPointerState();
            ClearHoverHighlight();
        }

        private void ResetPointerState()
        {
            _mode = PointerMode.None;
            _dragDie = -1;
            _pressedCell = -1;
            _hoverCell = -1;
            _hoverValid = false;
            _hoverScore = 0;
        }

        // ------------------------------------------------------------------
        // 落子 / 选中 / 重掷
        // ------------------------------------------------------------------

        private void PlaceAt(int cellId)
        {
            try
            {
                _run.Place(cellId);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                ShowMessage(ex.Message);
            }
        }

        /// <summary>球面上点一下：选中/取消选中一个可放置格，并显示这一手值几分。</summary>
        private void HandleCellClick(int cellId)
        {
            if (_run.Phase != TurnPhase.Placing) return;
            if (_run.PlacedThisTurn >= _run.Dice.Count) return;

            if (!_run.State.IsEmpty(cellId))
            {
                ClearSelection();
                ShowMessage("这一格已经有数字了");
                return;
            }

            if (Board.FaceOf(cellId) != _run.CurrentFace)
            {
                ClearSelection();
                ShowMessage($"只能放在当前区域（{_run.CurrentFace}）");
                return;
            }

            if (clickToPlaceForDebug)
            {
                PlaceAt(cellId);
                return;
            }

            if (_selectedCell == cellId)
            {
                ClearSelection();
                return;
            }

            ClearSelection();
            _selectedCell = cellId;
            _selectedScore = PreviewScoreFor(cellId);
            if (Planet != null) Planet.PreviewCell(cellId, selectedColor);
            ShowMessage($"已选中：放到这里得 +{_selectedScore}（再点一次取消）");
        }

        private void ClearSelection()
        {
            if (_selectedCell >= 0 && Planet != null) Planet.ClearPreview();
            _selectedCell = -1;
            _selectedScore = 0;
        }

        private void HandleDieClick(int dieIndex)
        {
            if (_run.Phase != TurnPhase.Placing) return;

            if (_run.RerollsLeft <= 0)
            {
                ShowMessage("本回合已经重掷过了");
                return;
            }
            if (_run.PlacedThisTurn > 0)
            {
                ShowMessage("已经开始放置，本回合不能再重掷");
                return;
            }

            _rerollSelected[dieIndex] = !_rerollSelected[dieIndex];
            RefreshTray();
        }

        private void OnRerollClicked()
        {
            if (_run == null || _run.Phase != TurnPhase.Placing) return;
            if (_mode == PointerMode.Dragging) return;   // 拖拽期间锁死重掷

            if (_run.RerollsLeft <= 0)
            {
                ShowMessage("本回合已经重掷过了");
                return;
            }
            if (_run.PlacedThisTurn > 0)
            {
                ShowMessage("已经开始放置，本回合不能再重掷");
                return;
            }

            int mask = RerollMask();
            if (mask == 0)
            {
                ShowMessage("先点选要重掷的骰子（或按 R 全掷）");
                return;
            }

            if (_run.Reroll(mask))
            {
                ClearRerollSelection();
                RefreshTray();
                ShowMessage("重掷完成");
            }
        }

        private int RerollMask()
        {
            int mask = 0;
            for (int i = 0; i < _rerollSelected.Length; i++)
            {
                if (_rerollSelected[i]) mask |= 1 << i;
            }
            return mask;
        }

        private void ClearRerollSelection()
        {
            for (int i = 0; i < _rerollSelected.Length; i++) _rerollSelected[i] = false;
        }

        private void RefreshTray()
        {
            if (_tray == null || _run == null) return;
            _tray.Refresh(_run, _rerollSelected);
        }

        private int PreviewScoreFor(int cellId)
        {
            if (_run == null || _topology == null) return 0;
            if (_run.PlacedThisTurn >= _run.Dice.Count) return 0;

            int value = _run.Dice[_run.PlacedThisTurn];
            return Scorer.Evaluate(_topology, _run.State, cellId, value, _run.Scoring).Total;
        }

        private int PickCell()
        {
            return PickCell(Input.mousePosition);
        }

        /// <summary>从屏幕坐标拾取格子。显式传坐标，方便用固定点做自测。</summary>
        private int PickCell(Vector2 screenPosition)
        {
            if (Cam == null) return -1;

            Ray ray = Cam.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return -1;

            CellView view = hit.collider.GetComponentInParent<CellView>();
            return view != null ? view.CellId : -1;
        }

        private void HandleKeys()
        {
            if (_run == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPointer();
                ClearSelection();
            }

            if (Input.GetKeyDown(KeyCode.R) && _run.Phase == TurnPhase.Placing
                && _mode != PointerMode.Dragging)
            {
                int mask = RerollMask();
                if (mask == 0) mask = 0b111;
                if (_run.Reroll(mask))
                {
                    ClearRerollSelection();
                    RefreshTray();
                }
                else
                {
                    ShowMessage("本回合不能重掷（已经重掷过，或已经开始放置）");
                }
            }

            if (Input.GetKeyDown(KeyCode.N)) NewRun();
        }

        // ------------------------------------------------------------------
        // 逻辑事件
        // ------------------------------------------------------------------

        private void OnTurnStarted(TowerRun run)
        {
            CancelPointer();
            ClearSelection();
            ClearRerollSelection();

            if (Planet != null) Planet.HighlightRegion(run.CurrentFace);
            if (Spin != null) Spin.FocusFace(run.CurrentFace, Cam);

            RefreshTray();
            ShowMessage($"第 {run.State.TurnIndex + 1} 回合 · {run.CurrentFace} 区域");
        }

        private void OnPlaced(PlacementResult result)
        {
            if (Planet != null)
            {
                Planet.SetCellValue(result.CellId, result.Value);
                Planet.HighlightRegion(_run.CurrentFace);
            }

            ClearSelection();
            RefreshTray();

            // 逐个邻居跳出得分：顺序由 Core 给出（固定），间隔 0.05s
            float delay = 0f;
            foreach (ScoreContribution contribution in result.Contributions)
            {
                CellView source = Planet != null ? Planet.GetCell(contribution.SourceCellId) : null;
                if (source == null || source.Face == null) continue;

                SpawnFloating(source.Face.bounds.center, $"+{contribution.Amount}",
                    new Color(1f, 0.93f, 0.65f), delay);
                delay += 0.05f;
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

            CancelPointer();
            ClearSelection();
            RefreshTray();
        }

        // ------------------------------------------------------------------
        // 飘字
        // ------------------------------------------------------------------

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
                Life = slowPopups ? 2f : 0.42f,
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

                // 「跳一下」：快速上浮 + 由大缩小 + 后段淡出
                float pop = 1f - t;
                f.Go.transform.position = f.Origin + Vector3.up * (0.18f * pop * pop + 0.06f * t);
                f.Go.transform.localScale = Vector3.one * (0.75f + 0.55f * pop);

                Color c = f.BaseColor;
                c.a = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                f.Text.color = c;
            }
        }

        private void ClearFloating()
        {
            for (int i = 0; i < _floatings.Count; i++)
            {
                if (_floatings[i].Go != null) Destroy(_floatings[i].Go);
            }
            _floatings.Clear();
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

        // ------------------------------------------------------------------
        // 临时 HUD（阶段二会换成正式 uGUI）
        // ------------------------------------------------------------------

        private GUIStyle _style;
        private GUIStyle _panel;
        private GUIStyle _center;

        private void OnGUI()
        {
            if (_run == null) return;

            // 样式只建一次：OnGUI 每帧都跑，每帧 new 会产生大量垃圾
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
                _panel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
                _center = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    richText = true,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            GUILayout.BeginArea(new Rect(10, 10, 292, 206), _panel);
            GUILayout.Label("<b>高塔堆叠者</b> · 白盒", _style);
            GUILayout.Label($"回合 {_run.State.TurnIndex + 1}/{_run.Settings.TurnsPerRun}    轮 {_run.State.RoundIndex + 1}/{_run.Settings.RoundsPerRun}", _style);
            GUILayout.Label($"分数 <b>{_run.State.TotalScore}</b> / {_run.State.TargetScore}", _style);
            GUILayout.Label($"区域 <b>{_run.CurrentFace}</b>    重掷 {_run.RerollsLeft}", _style);
            GUILayout.Label($"阶段 {_run.Phase}    已放 {_run.PlacedThisTurn}/{_run.Dice.Count}", _style);

            if (_run.Phase == TurnPhase.Placing)
            {
                var dice = new System.Text.StringBuilder("骰子 ");
                for (int i = 0; i < _run.Dice.Count; i++)
                {
                    dice.Append(i == _run.PlacedThisTurn ? $"[<b>{_run.Dice[i]}</b>] " : $"{_run.Dice[i]} ");
                }
                GUILayout.Label(dice.ToString(), _style);

                if (_mode == PointerMode.Dragging)
                {
                    GUILayout.Label(_hoverValid
                        ? $"预览：放到这里 <b>+{_hoverScore}</b>"
                        : "<color=#ff9a8a>这里不能放（空格 · 当前区域）</color>", _style);
                }
                else if (_selectedCell >= 0)
                {
                    GUILayout.Label($"已选中：<b>+{_selectedScore}</b>（再点一次取消）", _style);
                }
                else
                {
                    GUILayout.Label("拖左下角的骰子到格子里", _style);
                }

                GUILayout.Label("R 重掷　N 重开　Esc 取消", _style);
            }
            else if (_run.Phase == TurnPhase.RunEnded)
            {
                GUILayout.Label(_run.IsSuccess ? "<color=#7f7>达标</color>" : "<color=#f77>未达标</color>", _style);
                GUILayout.Label("N 重开", _style);
            }

            GUILayout.EndArea();

            // 拖拽时在鼠标旁显示这一手值几分
            if (_mode == PointerMode.Dragging)
            {
                Vector2 mouse = Input.mousePosition;
                string hint = _hoverValid
                    ? $"<b><color=#8ff0ff>+{_hoverScore}</color></b>"
                    : "<color=#ff9a8a>不可放置</color>";
                GUI.Label(new Rect(mouse.x + 26f, Screen.height - mouse.y - 28f, 220f, 28f), hint, _center);
            }

            if (!string.IsNullOrEmpty(_message) && Time.unscaledTime < _messageUntil)
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 360f, Screen.height - 46f, 720f, 30f), _message, _center);
            }
        }
    }
}
