using System;
using System.Collections.Generic;

namespace Tower.Core
{
    public enum TurnPhase
    {
        NotStarted,
        Placing,
        TurnEnded,
        RunEnded,
    }

    /// <summary>一局的规则参数。改这些不需要改代码。</summary>
    public sealed class RunSettings
    {
        public int DicePerTurn = 3;
        public int DiceMax = 3;          // 值域 1..DiceMax
        public int RerollsPerTurn = 1;
        public int TurnsPerRound = 6;
        public int RoundsPerRun = 3;
        public int TargetScore = 170;

        /// <summary>是否启用卡牌（默认关，保证旧单测与无卡模拟不受影响）。</summary>
        public bool EnableCards;

        public int TurnsPerRun => TurnsPerRound * RoundsPerRun;   // 18
        public int TotalCells => TurnsPerRun * DicePerTurn;        // 54
    }

    public sealed class PlacementResult
    {
        public readonly int CellId;
        public readonly int Value;
        public readonly int Score;
        public readonly IReadOnlyList<ScoreContribution> Contributions;
        public readonly bool TurnCompleted;

        public PlacementResult(int cellId, int value, int score,
            IReadOnlyList<ScoreContribution> contributions, bool turnCompleted)
        {
            CellId = cellId;
            Value = value;
            Score = score;
            Contributions = contributions;
            TurnCompleted = turnCompleted;
        }
    }

    /// <summary>
    /// 一局的驱动：轮次队列、掷骰、放置、计分、结束判定。
    /// 这是纯 C#，不依赖 UnityEngine，可在 EditMode 测试里直接跑。
    /// </summary>
    public sealed class TowerRun
    {
        public RunState State { get; }
        public BoardTopology Topology { get; }
        public ScoringConfig Scoring { get; }
        public RunSettings Settings { get; }

        public TurnPhase Phase { get; private set; } = TurnPhase.NotStarted;
        public FaceId CurrentFace { get; private set; }
        public int RerollsLeft { get; private set; }
        public int PlacedThisTurn { get; private set; }
        public bool IsFinished => Phase == TurnPhase.RunEnded;
        public bool IsSuccess => State.TotalScore >= State.TargetScore;

        private readonly DeterministicRandom _random;
        private readonly int[] _dice;
        private readonly List<FaceId> _roundQueue = new List<FaceId>(Board.FaceCount);
        private TurnRecord _currentRecord;

        private readonly ICardEffect[] _cardSlots = new ICardEffect[3];
        private readonly List<CardDefinition> _offered = new List<CardDefinition>(3);
        private int _cardChoicesMade;

        public IReadOnlyList<int> Dice => _dice;

        /// <summary>3 个卡槽；空槽为 null。卡效果常驻，直到被替换。</summary>
        public IReadOnlyList<ICardEffect> CardSlots => _cardSlots;

        /// <summary>当前待选的三张卡（没有待选时为空）。</summary>
        public IReadOnlyList<CardDefinition> OfferedCards => _offered;

        public bool HasPendingCardChoice { get; private set; }

        public int CardChoicesMade => _cardChoicesMade;

        /// <summary>新回合开始（含开局第一回合）。</summary>
        public event Action<TowerRun> TurnStarted;

        /// <summary>每放置一颗骰子触发一次，携带逐邻居的得分明细。</summary>
        public event Action<PlacementResult> Placed;

        public event Action<TowerRun> RunFinished;

        public TowerRun(ulong seed, BoardTopology topology, ScoringConfig scoring, RunSettings settings)
        {
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            Scoring = scoring ?? throw new ArgumentNullException(nameof(scoring));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            if (Settings.TurnsPerRound != Board.FaceCount)
            {
                throw new ArgumentException("每轮回合数必须等于面数（6）");
            }
            if (Settings.TurnsPerRun * Settings.DicePerTurn != Board.CellCount)
            {
                throw new ArgumentException("回合数 × 每回合放置数必须等于 54");
            }

            State = new RunState
            {
                Seed = seed,
                TargetScore = Settings.TargetScore,
            };
            _random = new DeterministicRandom(seed);
            _dice = new int[Settings.DicePerTurn];
        }

        public void Start()
        {
            if (Phase != TurnPhase.NotStarted)
            {
                throw new InvalidOperationException("这一局已经开始过了");
            }

            State.TurnIndex = 0;
            State.RoundIndex = 0;
            StartTurn();
        }

        private void StartTurn()
        {
            if (State.TurnIndex >= Settings.TurnsPerRun)
            {
                Finish();
                return;
            }

            State.RoundIndex = State.TurnIndex / Settings.TurnsPerRound;

            if (_roundQueue.Count == 0)
            {
                _roundQueue.Clear();
                for (int i = 0; i < Board.FaceCount; i++)
                {
                    _roundQueue.Add((FaceId)i);
                }
                _random.Shuffle(_roundQueue);
            }

            CurrentFace = _roundQueue[0];
            _roundQueue.RemoveAt(0);

            for (int i = 0; i < _dice.Length; i++)
            {
                _dice[i] = _random.NextInt(1, Settings.DiceMax);
            }

            RerollsLeft = Settings.RerollsPerTurn;
            PlacedThisTurn = 0;
            Phase = TurnPhase.Placing;

            _currentRecord = new TurnRecord
            {
                TurnIndex = State.TurnIndex,
                Face = CurrentFace,
                Dice = (int[])_dice.Clone(),
            };

            TryDrawCardChoice();
            TurnStarted?.Invoke(this);
        }

        /// <summary>
        /// 每 3 回合（第 1/4/7/10/13/16 回合开局）抽 3 张不重复的卡。
        /// 抽卡放在 StartTurn 里，随机序列与 UI 操作无关；同 seed 必然同一组。
        /// </summary>
        private void TryDrawCardChoice()
        {
            if (!Settings.EnableCards) return;
            if (State.TurnIndex % 3 != 0) return;
            if (_cardChoicesMade >= 6) return;

            _offered.Clear();
            int guard = 0;
            while (_offered.Count < 3 && guard++ < 60)
            {
                CardDefinition definition = CardCatalog.All[_random.NextInt(CardCatalog.All.Count)];
                bool duplicate = false;
                for (int i = 0; i < _offered.Count; i++)
                {
                    if (_offered[i].Id == definition.Id) { duplicate = true; break; }
                }
                if (!duplicate) _offered.Add(definition);
            }

            HasPendingCardChoice = _offered.Count > 0;
        }

        /// <summary>
        /// 选择一张卡。slotIndex = -1 表示放第一个空槽；槽位已满时必须指定要替换的槽位。
        /// </summary>
        public bool ChooseCard(int offeredIndex, int slotIndex)
        {
            if (!HasPendingCardChoice) return false;
            if (offeredIndex < 0 || offeredIndex >= _offered.Count) return false;
            if (slotIndex >= _cardSlots.Length) return false;

            if (slotIndex < 0)
            {
                slotIndex = -1;
                for (int i = 0; i < _cardSlots.Length; i++)
                {
                    if (_cardSlots[i] == null) { slotIndex = i; break; }
                }
                if (slotIndex < 0) return false;
            }

            CardDefinition definition = _offered[offeredIndex];
            ICardEffect effect = CardCatalog.Create(definition.Id);
            if (effect == null) return false;

            string replaced = null;
            if (_cardSlots[slotIndex] != null)
            {
                replaced = _cardSlots[slotIndex].Id;
                _cardSlots[slotIndex].OnRemove(State);
            }

            _cardSlots[slotIndex] = effect;
            effect.OnAcquire(State);

            var record = new CardChoiceRecord
            {
                TurnIndex = State.TurnIndex,
                Offered = new string[_offered.Count],
                Chosen = definition.Id,
                Replaced = replaced,
            };
            for (int i = 0; i < _offered.Count; i++) record.Offered[i] = _offered[i].Id;
            State.CardHistory.Add(record);

            _cardChoicesMade++;
            HasPendingCardChoice = false;
            _offered.Clear();
            return true;
        }

        /// <summary>按当前卡槽算一次落子的得分；预览与正式结算共用。</summary>
        public ScoringResult EvaluatePlacement(int cellId, int value, int dieIndex)
        {
            return ScoringPipeline.Evaluate(Topology, State, cellId, value, dieIndex, Scoring, _cardSlots);
        }

        /// <summary>预览：用当前这颗骰子的值算。</summary>
        public ScoringResult PreviewPlacement(int cellId)
        {
            int index = PlacedThisTurn;
            int value = index < _dice.Length ? _dice[index] : 0;
            return EvaluatePlacement(cellId, value, index);
        }

        /// <summary>
        /// 重掷：mask 的第 i 位对应第 i 颗骰子。每回合一次，且必须在放置任何骰子之前。
        /// </summary>
        public bool Reroll(int mask)
        {
            if (Phase != TurnPhase.Placing) return false;
            if (RerollsLeft <= 0) return false;
            if (PlacedThisTurn > 0) return false;
            if (mask == 0) return false;

            bool any = false;
            for (int i = PlacedThisTurn; i < _dice.Length; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                _dice[i] = _random.NextInt(1, Settings.DiceMax);
                any = true;
            }

            if (!any) return false;

            RerollsLeft--;
            _currentRecord.Dice = (int[])_dice.Clone();
            return true;
        }

        public PlacementResult Place(int cellId)
        {
            if (Phase != TurnPhase.Placing)
            {
                throw new InvalidOperationException($"当前阶段 {Phase} 不能放置");
            }
            if (cellId < 0 || cellId >= Board.CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cellId));
            }
            if (Board.FaceOf(cellId) != CurrentFace)
            {
                throw new InvalidOperationException(
                    $"只能放在当前区域 {CurrentFace}，而 {cellId} 属于 {Board.FaceOf(cellId)}");
            }
            if (!State.IsEmpty(cellId))
            {
                throw new InvalidOperationException($"格 {cellId} 已经有数字 {State.ValueAt(cellId)}");
            }
            if (PlacedThisTurn >= _dice.Length)
            {
                throw new InvalidOperationException("本回合的骰子已经放完");
            }

            int dieIndex = PlacedThisTurn;
            int value = _dice[dieIndex];
            ScoringResult scoring = EvaluatePlacement(cellId, value, dieIndex);

            State.Place(cellId, value);
            State.TotalScore += scoring.Total;

            _currentRecord.PlacedCells.Add(cellId);
            _currentRecord.PlacedValues.Add(value);
            _currentRecord.Score += scoring.Total;
            _currentRecord.Cumulative = State.TotalScore;

            for (int i = 0; i < scoring.Contributions.Count; i++)
            {
                string rule = scoring.Contributions[i].RuleId;
                if (rule == Scorer.RuleOrthoSame) _currentRecord.EdgeSameHits++;
                else if (rule == Scorer.RuleDiagDiff) _currentRecord.DiagDiffHits++;
            }

            PlacedThisTurn++;
            bool turnCompleted = PlacedThisTurn >= _dice.Length;

            var context = new PlacementContext(Topology, State, cellId, value, dieIndex);
            for (int i = 0; i < _cardSlots.Length; i++)
            {
                if (_cardSlots[i] != null) _cardSlots[i].OnPlacementScored(context, scoring);
            }

            var result = new PlacementResult(
                cellId, value, scoring.Total, scoring.Contributions, turnCompleted);

            Placed?.Invoke(result);

            if (turnCompleted)
            {
                EndTurn();
            }

            return result;
        }

        /// <summary>
        /// 调试用：强制本回合某颗还没放的骰子点数（不消耗重掷次数）。
        /// </summary>
        public bool ForceDie(int index, int value)
        {
            if (Phase != TurnPhase.Placing) return false;
            if (index < PlacedThisTurn || index >= _dice.Length) return false;
            if (value < 1 || value > Settings.DiceMax) return false;

            _dice[index] = value;
            if (_currentRecord != null) _currentRecord.Dice = (int[])_dice.Clone();
            return true;
        }

        /// <summary>
        /// 调试用：把本回合剩下的骰子依次放到本面第一个空格。
        /// 只跳过一个回合——EndTurn 之后 TurnIndex 变了就停，不会连跳。
        /// </summary>
        public bool SkipTurn()
        {
            if (Phase != TurnPhase.Placing) return false;

            int targetTurn = State.TurnIndex;
            int guard = 0;

            while (Phase == TurnPhase.Placing
                   && State.TurnIndex == targetTurn
                   && guard++ < Board.CellsPerFace)
            {
                List<int> cells = EmptyCellsOnCurrentFace();
                if (cells.Count == 0) return false;
                Place(cells[0]);
            }

            return true;
        }

        private void EndTurn()
        {
            State.History.Add(_currentRecord);
            State.TurnIndex++;
            Phase = TurnPhase.TurnEnded;

            if (State.TurnIndex >= Settings.TurnsPerRun)
            {
                Finish();
                return;
            }

            StartTurn();
        }

        private void Finish()
        {
            Phase = TurnPhase.RunEnded;
            RunFinished?.Invoke(this);
        }

        /// <summary>当前面里可以放置的格（按 cellId 升序）。</summary>
        public List<int> EmptyCellsOnCurrentFace()
        {
            var list = new List<int>(Board.CellsPerFace);
            int baseId = (int)CurrentFace * Board.CellsPerFace;
            for (int i = 0; i < Board.CellsPerFace; i++)
            {
                if (State.IsEmpty(baseId + i)) list.Add(baseId + i);
            }
            return list;
        }
    }
}
