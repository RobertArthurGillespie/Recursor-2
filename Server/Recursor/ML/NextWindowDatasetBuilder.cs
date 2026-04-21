using System.Globalization;
using System.Text;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Summary statistics produced by a dataset build run.
/// </summary>
public class NextWindowDatasetStats
{
    public int TotalInputRows { get; init; }
    public int TotalSessions { get; init; }
    public int DroppedAsLastInSession { get; init; }
    public int TotalOutputRows { get; init; }
    public int LabelTrueCount { get; init; }
    public int LabelFalseCount { get; init; }
    public int DroppedNaN { get; init; }
}

/// <summary>
/// Transforms a flat list of BehaviorStateTrainingRows exported from ADX into a
/// next-window training CSV compatible with BehaviorStateTrainingExample_NextWindow.
///
/// Algorithm:
///   1. Group by SessionId, sort each group by WindowIndex ascending.
///   2. For row k: LabelHintDependenceNext = (row[k+1].LabelHintDependence == 1).
///   3. Drop the final row in each session (it has no k+1).
///   4. Drop any row where a numeric feature is NaN.
///   5. Write a CSV whose columns match the LoadColumn indices in
///      BehaviorStateTrainingExample_NextWindow exactly.
///
/// Embedding columns (Embedding01–Embedding16) are written as 0.0 because the
/// ADX table does not store embeddings. The Baseline trainer ignores them.
/// If you later run the autoencoder over the exported data you can replace the
/// zeros before calling the WithEmbeddings trainer.
/// </summary>
public static class NextWindowDatasetBuilder
{
    // Must match BehaviorStateTrainingExample_NextWindow exactly.
    private static readonly string CsvHeader =
        "SessionId,SimId,ScenarioId,WindowIndex,TaskType,CreatedAtUtc," +
        "AttentionDetection,GoalUnderstanding,ProcedureSequencing,PaceRegulation," +
        "SelfCorrection,FeedbackResponsiveness,SafetyCompliance,TaskContinuity," +
        "ConfusionScore,HesitationScore,ImpulsivityScore,HintDependenceScore," +
        "GoalTrend,AttentionTrend,ConfusionTrend,HintDependenceTrend," +
        "CurrentHintMode,CurrentDifficulty,CurrentTimePressure,CurrentErrorTolerance," +
        "ConsecutiveStableMasteryWindows,ConsecutiveRelapseWindows," +
        "EventCountInWindow,ErrorCountInWindow,HintCountInWindow,StepCompleteCountInWindow," +
        "LabelConfusion,LabelHintDependence,LabelStableMastery," +
        "Embedding01,Embedding02,Embedding03,Embedding04,Embedding05,Embedding06,Embedding07,Embedding08," +
        "Embedding09,Embedding10,Embedding11,Embedding12,Embedding13,Embedding14,Embedding15,Embedding16," +
        "NextWindowIndex,LabelHintDependenceNext";

    public static NextWindowDatasetStats BuildAndWriteCsv(
        IReadOnlyList<BehaviorStateTrainingRow> rows,
        string outputCsvPath)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (string.IsNullOrWhiteSpace(outputCsvPath))
            throw new ArgumentException("Output CSV path is required.", nameof(outputCsvPath));

        var outputDir = Path.GetDirectoryName(outputCsvPath);
        if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        int droppedNaN = 0;
        int droppedLastRow = 0;
        var outputRows = new List<NextWindowRow>();

        var groups = rows
            .GroupBy(r => r.SessionId)
            .Select(g => g.OrderBy(r => r.WindowIndex).ToList())
            .ToList();

        foreach (var group in groups)
        {
            for (int k = 0; k < group.Count - 1; k++)
            {
                var current = group[k];
                var next = group[k + 1];

                if (HasNaN(current))
                {
                    droppedNaN++;
                    continue;
                }

                outputRows.Add(new NextWindowRow
                {
                    Current = current,
                    NextWindowIndex = next.WindowIndex,
                    LabelHintDependenceNext = next.LabelHintDependence == 1
                });
            }

            if (group.Count > 0)
                droppedLastRow++;
        }

        int labelTrue = outputRows.Count(r => r.LabelHintDependenceNext);
        int labelFalse = outputRows.Count(r => !r.LabelHintDependenceNext);

        WriteCsv(outputCsvPath, outputRows);

        return new NextWindowDatasetStats
        {
            TotalInputRows = rows.Count,
            TotalSessions = groups.Count,
            DroppedAsLastInSession = droppedLastRow,
            DroppedNaN = droppedNaN,
            TotalOutputRows = outputRows.Count,
            LabelTrueCount = labelTrue,
            LabelFalseCount = labelFalse
        };
    }

    private static bool HasNaN(BehaviorStateTrainingRow r) =>
        double.IsNaN(r.AttentionDetection) ||
        double.IsNaN(r.GoalUnderstanding) ||
        double.IsNaN(r.ProcedureSequencing) ||
        double.IsNaN(r.PaceRegulation) ||
        double.IsNaN(r.SelfCorrection) ||
        double.IsNaN(r.FeedbackResponsiveness) ||
        double.IsNaN(r.SafetyCompliance) ||
        double.IsNaN(r.TaskContinuity) ||
        double.IsNaN(r.ConfusionScore) ||
        double.IsNaN(r.HesitationScore) ||
        double.IsNaN(r.ImpulsivityScore) ||
        double.IsNaN(r.HintDependenceScore) ||
        double.IsNaN(r.GoalTrend) ||
        double.IsNaN(r.AttentionTrend) ||
        double.IsNaN(r.ConfusionTrend) ||
        double.IsNaN(r.HintDependenceTrend) ||
        double.IsNaN(r.CurrentDifficulty) ||
        double.IsNaN(r.CurrentTimePressure) ||
        double.IsNaN(r.CurrentErrorTolerance);

    private static void WriteCsv(string path, IReadOnlyList<NextWindowRow> rows)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine(CsvHeader);

        foreach (var r in rows)
        {
            var c = r.Current;
            var sb = new StringBuilder();

            // 0-5 identity
            Append(sb, c.SessionId);
            Append(sb, c.SimId);
            Append(sb, c.ScenarioId);
            AppendNum(sb, c.WindowIndex);
            Append(sb, c.TaskType);
            Append(sb, c.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));

            // 6-17 dimension + higher-order scores
            AppendNum(sb, c.AttentionDetection);
            AppendNum(sb, c.GoalUnderstanding);
            AppendNum(sb, c.ProcedureSequencing);
            AppendNum(sb, c.PaceRegulation);
            AppendNum(sb, c.SelfCorrection);
            AppendNum(sb, c.FeedbackResponsiveness);
            AppendNum(sb, c.SafetyCompliance);
            AppendNum(sb, c.TaskContinuity);
            AppendNum(sb, c.ConfusionScore);
            AppendNum(sb, c.HesitationScore);
            AppendNum(sb, c.ImpulsivityScore);
            AppendNum(sb, c.HintDependenceScore);

            // 18-21 trajectory
            AppendNum(sb, c.GoalTrend);
            AppendNum(sb, c.AttentionTrend);
            AppendNum(sb, c.ConfusionTrend);
            AppendNum(sb, c.HintDependenceTrend);

            // 22-25 adaptive state
            Append(sb, c.CurrentHintMode);
            AppendNum(sb, c.CurrentDifficulty);
            AppendNum(sb, c.CurrentTimePressure);
            AppendNum(sb, c.CurrentErrorTolerance);

            // 26-27 counters
            AppendNum(sb, c.ConsecutiveStableMasteryWindows);
            AppendNum(sb, c.ConsecutiveRelapseWindows);

            // 28-31 window summary counts
            AppendNum(sb, c.EventCountInWindow);
            AppendNum(sb, c.ErrorCountInWindow);
            AppendNum(sb, c.HintCountInWindow);
            AppendNum(sb, c.StepCompleteCountInWindow);

            // 32-34 weak labels
            AppendBool(sb, c.LabelConfusion != 0);
            AppendBool(sb, c.LabelHintDependence != 0);
            AppendBool(sb, c.LabelStableMastery != 0);

            // 35-50 embeddings (currently zeroed)
            for (int i = 0; i < 16; i++)
                AppendNum(sb, 0.0);

            // 51 next window index
            AppendNum(sb, r.NextWindowIndex);

            // 52 final training label
            AppendBoolLast(sb, r.LabelHintDependenceNext);

            writer.WriteLine(sb.ToString());
        }
    }

    private static void Append(StringBuilder sb, string value)
    {
        sb.Append('"');
        sb.Append((value ?? string.Empty).Replace("\"", "\"\""));
        sb.Append('"');
        sb.Append(',');
    }

    private static void AppendNum(StringBuilder sb, double value)
    {
        sb.Append(value.ToString("G6", CultureInfo.InvariantCulture));
        sb.Append(',');
    }

    private static void AppendNum(StringBuilder sb, int value)
    {
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
    }

    private static void AppendBool(StringBuilder sb, bool value)
    {
        sb.Append(value ? "True" : "False");
        sb.Append(',');
    }

    private static void AppendBoolLast(StringBuilder sb, bool value)
    {
        sb.Append(value ? "True" : "False");
    }

    private sealed class NextWindowRow
    {
        public required BehaviorStateTrainingRow Current { get; init; }
        public int NextWindowIndex { get; init; }
        public bool LabelHintDependenceNext { get; init; }
    }
}