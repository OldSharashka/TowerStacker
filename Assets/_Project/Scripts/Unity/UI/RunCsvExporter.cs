using System;
using System.Globalization;
using System.IO;
using System.Text;
using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 把一局的每回合明细导出成 CSV（文档 3-3）：
    /// turn, face, dice, cells, values, edgeSameHits, diagDiffHits, turnScore, cumulative。
    /// 输出到 persistentDataPath/TowerRuns，路径会打到 Console。
    /// </summary>
    public static class RunCsvExporter
    {
        public static string Export(TowerRun run)
        {
            if (run == null) return null;

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "TowerRuns");
                Directory.CreateDirectory(dir);

                string name = string.Format(
                    CultureInfo.InvariantCulture,
                    "run_{0}_{1:yyyyMMdd_HHmmss}.csv", run.State.Seed, DateTime.Now);
                string path = Path.Combine(dir, name);

                var sb = new StringBuilder();
                sb.AppendLine("turn,face,dice,cells,values,edgeSameHits,diagDiffHits,turnScore,cumulative");

                foreach (TurnRecord record in run.State.History)
                {
                    sb.Append(record.TurnIndex + 1).Append(',');
                    sb.Append(record.Face).Append(',');
                    sb.Append('"').Append(string.Join("|", record.Dice)).Append('"').Append(',');
                    sb.Append('"').Append(string.Join("|", record.PlacedCells)).Append('"').Append(',');
                    sb.Append('"').Append(string.Join("|", record.PlacedValues)).Append('"').Append(',');
                    sb.Append(record.EdgeSameHits).Append(',');
                    sb.Append(record.DiagDiffHits).Append(',');
                    sb.Append(record.Score).Append(',');
                    sb.Append(record.Cumulative);
                    sb.AppendLine();
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Tower] 导出 CSV 失败：" + ex);
                return null;
            }
        }
    }
}
