using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 编辑模式预览：不用进 Play Mode，直接把「按贪心打 N 回合」的棋盘画出来。
    /// 用途只有一个——截图与快速目视检查，不参与玩法。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BoardPreview : MonoBehaviour
    {
        public PlanetMeshBuilder Planet;
        public ulong Seed = 20261110UL;
        [Range(0, 18)] public int Turns = 9;
        public bool AutoApply = true;

        private void OnEnable()
        {
            if (AutoApply) Apply();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying && isActiveAndEnabled && AutoApply) Apply();
        }

        [ContextMenu("Apply Preview")]
        public void Apply()
        {
            if (Planet == null) Planet = GetComponent<PlanetMeshBuilder>();
            if (Planet == null) return;

            BoardTopology topology = BoardTopology.Build();
            var run = new TowerRun(
                Seed, topology, ScoringConfig.Default,
                new RunSettings { TargetScore = 170 });

            run.Start();

            int guard = 0;
            while (!run.IsFinished && run.State.TurnIndex < Turns)
            {
                if (++guard > 600) break;
                if (run.Phase != TurnPhase.Placing) break;

                int value = run.Dice[run.PlacedThisTurn];
                int bestCell = -1;
                int bestScore = int.MinValue;

                foreach (int cell in run.EmptyCellsOnCurrentFace())
                {
                    ScoringResult probe = Scorer.Evaluate(topology, run.State, cell, value, run.Scoring);
                    if (probe.Total > bestScore)
                    {
                        bestScore = probe.Total;
                        bestCell = cell;
                    }
                }

                if (bestCell < 0) break;
                run.Place(bestCell);
            }

            Planet.ResetAll();
            for (int cell = 0; cell < Board.CellCount; cell++)
            {
                int value = run.State.ValueAt(cell);
                if (value > 0) Planet.SetCellValue(cell, value);
            }

            Planet.HighlightRegion(run.IsFinished ? (FaceId?)null : run.CurrentFace);
        }
    }
}
