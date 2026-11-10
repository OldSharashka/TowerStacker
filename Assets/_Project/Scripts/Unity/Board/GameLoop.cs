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

        [Tooltip("拖动/悬停可放置格的提示色")]
        public Color hoverColor = new Color(0.55f, 0.92f, 0.95f);

        private const float PopupInterval = 0.05f;

        private TowerRun _run;
        private BoardTopology _topology;
        private DiceTrayView _tray;
        private DragGhostView _ghost;
        private HudView _hud;
        private ResultPanelView _result;

        private enum PointerMode
        {
            None,
            SpherePress,
            DiePress,
            Dragging,
        }

        /// <summary>回合切换要等最后一次落子的飘字播完，这里记「什么时候才能切」。</summary>
        private enum PendingKind
        {
            None,
            NextTurn,
            Settle,
        }

        private PointerMode _mode;
        private Vector2 _pressPos;
        private Vector2 _lastMouse;
        private float _pressTime;
        private bool _sphereMoved;
        private int _dragDie = -1;
        private int _hoverCell = -1;
        private bool _hoverValid;
        private int _hoverScore;
        private int _previewCell = -1;

        private PendingKind _pendingKind;
        private float _pendingAt;

        // 等飘字期间，HUD 还应该显示刚打完的那一回合（Core 已经切到下一回合了）
        private FaceId _lastFace;
        private int[] _lastDice = new int[0];

        private readonly List<Floating> _floatings = new List<Floating>();
        private string _message = string.Empty;
        private float _messageUntil;

        private float PopupLife => slowPopups ? 2f : 0.42f;

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
                _hud = HudView.Create(canvas.transform);
                _result = ResultPanelView.Create(canvas.transform);
                _result.RestartSameClicked += RestartSameSeed;
                _result.RestartNewClicked += RestartNewSeed;
            }

            NewRun();

            if (AutoPlayTurns > 0) AutoPlay(AutoPlayTurns);
        }

        private void OnDestroy()
        {
            if (_tray != null) _tray.RerollClicked -= OnRerollClicked;
            if (_result != null)
            {
                _result.RestartSameClicked -= RestartSameSeed;
                _result.RestartNewClicked -= RestartNewSeed;
            }
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
            ClearHover();
            ClearFloating();
            _pendingKind = PendingKind.None;
            _pendingAt = 0f;
            _message = string.Empty;

            if (_result != null) _result.Hide();
            if (_hud != null)
            {
                _hud.ClearLog();
                _hud.SetMessage(string.Empty);
                _hud.PushLog("新开一局");
            }

            _topology = BoardTopology.Build();
            var settings = new RunSettings { TargetScore = TargetScore };

            _run = new TowerRun(Seed, _topology, ScoringConfig.Default, settings);
            _run.TurnStarted += OnTurnStarted;
            _run.Placed += OnPlaced;
            _run.RunFinished += OnRunFinished;

            if (Planet != null) Planet.ResetAll();
            _run.Start();
        }

        /// <summary>结算面板：原种子重开。</summary>
        public void RestartSameSeed()
        {
            NewRun();
        }

        /// <summary>结算面板：换一个种子重开。</summary>
        public void RestartNewSeed()
        {
            Seed = (ulong)System.DateTime.Now.Ticks;
            NewRun();
        }

        private void Update()
        {
            if (_run == null) return;

            TickPendingTurn();
            HandlePointer();
            HandleKeys();
            AnimateFloating(Time.deltaTime);
            UpdateHudText();
        }

        // ------------------------------------------------------------------
        // 输入：按下位置分流（文档 3.7.1）
        // ------------------------------------------------------------------

        private bool IsInputLocked()
        {
            return _pendingKind != PendingKind.None
                   || (Spin != null && Spin.IsTransitioning)
                   || (_result != null && _result.IsVisible);
        }

        private void HandlePointer()
        {
            // 转场中、或"等飘字播完再进下一回合"的间隙：锁输入
            if (IsInputLocked())
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
                return;
            }

            UpdateMouseHover(mouse);
        }

        private void OnPress(Vector2 mouse)
        {
            _pressPos = mouse;
            _lastMouse = mouse;
            _pressTime = Time.unscaledTime;
            _sphereMoved = false;
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

            // 球面上按下：拖动就是转球，不动就什么也不做（没有点击选中）
            _mode = PointerMode.SpherePress;
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

            ResetPointerState();

            if (mode != PointerMode.Dragging) return;

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
        }

        private void BeginDrag(Vector2 mouse)
        {
            if (_run.Phase != TurnPhase.Placing) return;
            if (_dragDie != _run.PlacedThisTurn) return;

            _mode = PointerMode.Dragging;

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

            if (valid)
            {
                _hoverValid = true;
                _hoverCell = cell;
                _hoverScore = PreviewScoreFor(cell);
                SetPreview(cell, hoverColor);
            }
            else
            {
                _hoverValid = false;
                _hoverCell = -1;
                _hoverScore = 0;
                ClearHoverHighlight();
            }

            if (_ghost != null) _ghost.SetValid(valid || cell < 0);
        }

        /// <summary>没按鼠标时，鼠标悬停在可放置格上就给高亮 + 预览。</summary>
        private void UpdateMouseHover(Vector2 mouse)
        {
            if (_run.Phase != TurnPhase.Placing || _run.PlacedThisTurn >= _run.Dice.Count)
            {
                ClearHover();
                return;
            }

            if (_tray != null && _tray.Contains(mouse))
            {
                ClearHover();
                return;
            }

            int cell = PickCell(mouse);
            bool valid = cell >= 0
                         && _run.State.IsEmpty(cell)
                         && Board.FaceOf(cell) == _run.CurrentFace;

            if (!valid)
            {
                ClearHover();
                return;
            }

            _hoverCell = cell;
            _hoverValid = true;

            if (_previewCell != cell)
            {
                _hoverScore = PreviewScoreFor(cell);
                SetPreview(cell, hoverColor);
            }
        }

        private void ClearHover()
        {
            _hoverValid = false;
            _hoverCell = -1;
            _hoverScore = 0;
            ClearHoverHighlight();
        }

        /// <summary>只高亮一格；换格时先把上一格恢复成区域底色，避免留下色块。</summary>
        private void SetPreview(int cellId, Color color)
        {
            if (_previewCell == cellId) return;

            if (Planet != null)
            {
                if (_previewCell >= 0) Planet.ClearPreview();
                Planet.PreviewCell(cellId, color);
            }

            _previewCell = cellId;
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
            _hoverCell = -1;
            _hoverValid = false;
            _hoverScore = 0;
        }

        // ------------------------------------------------------------------
        // 落子 / 重掷
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

        private void OnRerollClicked()
        {
            if (_run == null || _run.Phase != TurnPhase.Placing) return;
            if (IsInputLocked() || _mode == PointerMode.Dragging) return;

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

            if (_run.Reroll(0b111))
            {
                RefreshTray();
                ShowMessage("重掷完成");
            }
        }

        private void RefreshTray()
        {
            if (_tray != null && _run != null) _tray.Refresh(_run);
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (_hud == null || _run == null) return;

            bool pending = _pendingKind != PendingKind.None;
            _hud.Refresh(_run, pending, _lastFace, _lastDice.Length);
        }

        /// <summary>底部提示行：拖拽预览 / 悬停预览 / 等待转场 / 空闲。</summary>
        private void UpdateHudText()
        {
            if (_hud == null || _run == null) return;

            string hint;
            if (_pendingKind == PendingKind.Settle)
            {
                hint = "得分显示完就出结算";
            }
            else if (_pendingKind == PendingKind.NextTurn)
            {
                hint = "得分显示完就进下一回合";
            }
            else if (_run.Phase == TurnPhase.RunEnded)
            {
                hint = $"本局结束：{_run.State.TotalScore} / {_run.State.TargetScore}";
            }
            else if (_mode == PointerMode.Dragging)
            {
                hint = _hoverValid ? $"预览：放到这里 +{_hoverScore}" : "这里不能放（空格 · 当前区域）";
            }
            else if (_hoverValid)
            {
                hint = $"预览：放到这里 +{_hoverScore}";
            }
            else
            {
                hint = "拖左下角的骰子到格子里";
            }

            _hud.SetHint(hint);
            _hud.SetMessage(Time.unscaledTime < _messageUntil ? _message : string.Empty);
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

            if (Input.GetKeyDown(KeyCode.Escape)) CancelPointer();

            if (Input.GetKeyDown(KeyCode.R) && _run.Phase == TurnPhase.Placing
                && !IsInputLocked() && _mode != PointerMode.Dragging)
            {
                if (_run.Reroll(0b111)) RefreshTray();
                else ShowMessage("本回合不能重掷（已经重掷过，或已经开始放置）");
            }

            if (Input.GetKeyDown(KeyCode.N)) NewRun();
        }

        // ------------------------------------------------------------------
        // 逻辑事件
        // ------------------------------------------------------------------

        private void OnTurnStarted(TowerRun run)
        {
            CancelPointer();
            ClearHover();

            // 上一手飘字还没播完：画面先停在旧区域，TickPendingTurn 到点再转
            if (_pendingKind == PendingKind.NextTurn) return;

            BeginTurnVisuals();
        }

        private void BeginTurnVisuals()
        {
            if (_run == null || _run.Phase == TurnPhase.RunEnded) return;

            if (Planet != null) Planet.HighlightRegion(_run.CurrentFace);
            if (Spin != null) Spin.FocusFace(_run.CurrentFace, Cam);

            RefreshTray();
            ShowMessage($"第 {_run.State.TurnIndex + 1} 回合 · {_run.CurrentFace} 区域");
            if (_hud != null) _hud.PushLog($"第 {_run.State.TurnIndex + 1} 回合 · {_run.CurrentFace}");
        }

        private void OnPlaced(PlacementResult result)
        {
            // 先记下这一回合的画面信息：飘字播完之前，HUD 还停在它上面
            _lastFace = _run.CurrentFace;
            _lastDice = new int[_run.Dice.Count];
            for (int i = 0; i < _lastDice.Length; i++) _lastDice[i] = _run.Dice[i];

            if (Planet != null)
            {
                Planet.SetCellValue(result.CellId, result.Value);
                Planet.HighlightRegion(_run.CurrentFace);
            }

            RefreshTray();
            if (_hud != null)
            {
                _hud.PushLog($"落子 {result.Value} → +{result.Score}");
            }
            // 逐个邻居跳出得分：顺序由 Core 给出（固定），间隔 0.05s
            // 注意：不在落子格上再弹「本手合计」——那一格本身不产分，弹了会被读成“这格也加分”。
            float delay = 0f;
            int count = result.Contributions.Count;
            foreach (ScoreContribution contribution in result.Contributions)
            {
                CellView source = Planet != null ? Planet.GetCell(contribution.SourceCellId) : null;
                if (source == null || source.Face == null) continue;

                SpawnFloating(source.Face.bounds.center, $"+{contribution.Amount}",
                    new Color(1f, 0.93f, 0.65f), delay);
                delay += PopupInterval;
            }

            // 三颗都放完：等最后一次的 +N 播完，再切下一回合（或出结算）
            if (result.TurnCompleted)
            {
                float lastStart = count > 0 ? (count - 1) * PopupInterval : 0f;
                float wait = lastStart + PopupLife + 0.08f;
                if (wait < 0.2f) wait = 0.2f;

                bool lastTurn = _run.State.TurnIndex + 1 >= _run.Settings.TurnsPerRun;
                _pendingKind = lastTurn ? PendingKind.Settle : PendingKind.NextTurn;
                _pendingAt = Time.unscaledTime + wait;
            }
        }

        private void OnRunFinished(TowerRun run)
        {
            CancelPointer();
            ClearHover();

            if (_pendingKind == PendingKind.Settle) return;
            ApplyRunEnd();
        }

        private void ApplyRunEnd()
        {
            if (_run == null) return;

            if (Planet != null) Planet.HighlightRegion(null);
            RefreshTray();

            int bestScore = 0;
            int bestTurn = 1;
            foreach (TurnRecord record in _run.State.History)
            {
                if (record.Score > bestScore)
                {
                    bestScore = record.Score;
                    bestTurn = record.TurnIndex + 1;
                }
            }

            if (_result != null)
            {
                _result.Show(_run.State.TotalScore, _run.State.TargetScore, _run.IsSuccess, bestScore, bestTurn);
            }

            if (_hud != null)
            {
                _hud.PushLog($"结算 {_run.State.TotalScore}/{_run.State.TargetScore} · {(_run.IsSuccess ? "达标" : "未达标")}");
            }
        }

        private void TickPendingTurn()
        {
            if (_pendingKind == PendingKind.None) return;
            if (Time.unscaledTime < _pendingAt) return;

            PendingKind kind = _pendingKind;
            _pendingKind = PendingKind.None;

            if (kind == PendingKind.NextTurn) BeginTurnVisuals();
            else ApplyRunEnd();
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
                Life = PopupLife,
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

    }
}
