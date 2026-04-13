using Microsoft.ML.Data;
using Microsoft.ML;
using System.Text;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML
{
    public class HintDependenceNextWindowTrainer_WithEmbeddings
    {
        private static readonly string[] NumericFeatureColumns =
       [
           "AttentionDetection",
            "GoalUnderstanding",
            "ProcedureSequencing",
            "PaceRegulation",
            "SelfCorrection",
            "FeedbackResponsiveness",
            "SafetyCompliance",
            "TaskContinuity",
            "ConfusionScore",
            "HesitationScore",
            "ImpulsivityScore",

            // Intentionally excluded for stricter next-window prediction:
            // "HintDependenceScore",
            // "HintDependenceTrend",
            // "HintCountInWindow",

            "GoalTrend",
            "AttentionTrend",
            "ConfusionTrend",
            "CurrentDifficulty",
            "CurrentTimePressure",
            "CurrentErrorTolerance",
            "ConsecutiveStableMasteryWindows",
            "ConsecutiveRelapseWindows",
            "EventCountInWindow",
            "ErrorCountInWindow",
            "StepCompleteCountInWindow",
        ];

        private static readonly string[] EmbeddingFeatureColumns =
        [
            "Embedding01",
            "Embedding02",
            "Embedding03",
            "Embedding04",
            "Embedding05",
            "Embedding06",
            "Embedding07",
            "Embedding08",
            "Embedding09",
            "Embedding10",
            "Embedding11",
            "Embedding12",
            "Embedding13",
            "Embedding14",
            "Embedding15",
            "Embedding16",
        ];

        public static void Train(string csvPath, string modelOutputPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
                throw new ArgumentException("CSV path is required.", nameof(csvPath));

            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"Training CSV not found: {csvPath}", csvPath);

            if (string.IsNullOrWhiteSpace(modelOutputPath))
                throw new ArgumentException("Model output path is required.", nameof(modelOutputPath));

            var outputDirectory = Path.GetDirectoryName(modelOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory) && !Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var logPath = Path.ChangeExtension(modelOutputPath, ".debug.log");
            File.WriteAllText(logPath, string.Empty);

            void Log(string message)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
            }

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Loading data from: {csvPath}");

            var mlContext = new MLContext(seed: 42);

            var loaderOptions = new TextLoader.Options
            {
                HasHeader = true,
                Separators = new[] { ',' },
                AllowQuoting = true,
                TrimWhitespace = true,
                AllowSparse = false
            };

            var rawData = mlContext.Data.LoadFromTextFile<BehaviorStateTrainingExample_NextWindow>(
                path: csvPath,
                options: loaderOptions);
            var rows = mlContext.Data
              .CreateEnumerable<BehaviorStateTrainingExample_NextWindow>(rawData, reuseRowObject: false)
              .Where(r => !string.IsNullOrWhiteSpace(r.SessionId))
              .ToList();

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Parsed rows: {rows.Count}");

            if (rows.Count == 0)
                throw new InvalidOperationException("No rows were parsed from the CSV.");

            //
            // 🔥 NEW: Detect bad rows (NaN features)
            //

            var badRows = rows.Where(r =>
                float.IsNaN(r.AttentionDetection) ||
                float.IsNaN(r.GoalUnderstanding) ||
                float.IsNaN(r.ProcedureSequencing) ||
                float.IsNaN(r.PaceRegulation) ||
                float.IsNaN(r.SelfCorrection) ||
                float.IsNaN(r.FeedbackResponsiveness) ||
                float.IsNaN(r.SafetyCompliance) ||
                float.IsNaN(r.TaskContinuity) ||
                float.IsNaN(r.ConfusionScore) ||
                float.IsNaN(r.HesitationScore) ||
                float.IsNaN(r.ImpulsivityScore) ||
                float.IsNaN(r.GoalTrend) ||
                float.IsNaN(r.AttentionTrend) ||
                float.IsNaN(r.ConfusionTrend) ||
                float.IsNaN(r.CurrentDifficulty) ||
                float.IsNaN(r.CurrentTimePressure) ||
                float.IsNaN(r.CurrentErrorTolerance) ||
                float.IsNaN(r.ConsecutiveStableMasteryWindows) ||
                float.IsNaN(r.ConsecutiveRelapseWindows) ||
                float.IsNaN(r.EventCountInWindow) ||
                float.IsNaN(r.ErrorCountInWindow) ||
                float.IsNaN(r.StepCompleteCountInWindow)
            ).ToList();

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Rows with NaN features: {badRows.Count}");

            //
            // 🔥 Filter them out BEFORE training
            //
            rows = rows.Where(r =>
                !float.IsNaN(r.AttentionDetection) &&
                !float.IsNaN(r.GoalUnderstanding) &&
                !float.IsNaN(r.ProcedureSequencing) &&
                !float.IsNaN(r.PaceRegulation) &&
                !float.IsNaN(r.SelfCorrection) &&
                !float.IsNaN(r.FeedbackResponsiveness) &&
                !float.IsNaN(r.SafetyCompliance) &&
                !float.IsNaN(r.TaskContinuity) &&
                !float.IsNaN(r.ConfusionScore) &&
                !float.IsNaN(r.HesitationScore) &&
                !float.IsNaN(r.ImpulsivityScore) &&
                !float.IsNaN(r.GoalTrend) &&
                !float.IsNaN(r.AttentionTrend) &&
                !float.IsNaN(r.ConfusionTrend) &&
                !float.IsNaN(r.CurrentDifficulty) &&
                !float.IsNaN(r.CurrentTimePressure) &&
                !float.IsNaN(r.CurrentErrorTolerance) &&
                !float.IsNaN(r.ConsecutiveStableMasteryWindows) &&
                !float.IsNaN(r.ConsecutiveRelapseWindows) &&
                !float.IsNaN(r.EventCountInWindow) &&
                !float.IsNaN(r.ErrorCountInWindow) &&
                !float.IsNaN(r.StepCompleteCountInWindow)
            ).ToList();

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Rows AFTER NaN filter: {rows.Count}");



            if (rows.Count == 0)
                throw new InvalidOperationException("No rows were parsed from the CSV.");

            int trueCount = rows.Count(r => r.LabelHintDependenceNext);
            int falseCount = rows.Count(r => !r.LabelHintDependenceNext);

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] LabelHintDependenceNext TRUE rows:  {trueCount}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] LabelHintDependenceNext FALSE rows: {falseCount}");

            if (trueCount == 0 || falseCount == 0)
                throw new InvalidOperationException(
                    $"Dataset must contain both TRUE and FALSE LabelHintDependenceNext values. TRUE={trueCount}, FALSE={falseCount}");

            var groupedBySession = rows
                .GroupBy(r => r.SessionId)
                .OrderBy(g => g.Key)
                .ToList();

            if (groupedBySession.Count < 2)
                throw new InvalidOperationException("Need at least 2 distinct sessions for session-based splitting.");

            var rng = new Random(42);
            var shuffledGroups = groupedBySession
                .OrderBy(_ => rng.Next())
                .ToList();

            int trainSessionCount = Math.Max(1, (int)Math.Floor(shuffledGroups.Count * 0.75));
            if (trainSessionCount >= shuffledGroups.Count)
                trainSessionCount = shuffledGroups.Count - 1;

            var trainGroups = shuffledGroups.Take(trainSessionCount).ToList();
            var testGroups = shuffledGroups.Skip(trainSessionCount).ToList();

            var trainRowsList = trainGroups.SelectMany(g => g).ToList();
            var testRowsList = testGroups.SelectMany(g => g).ToList();

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Train sessions: {trainGroups.Count}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Test sessions:  {testGroups.Count}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Train rows:     {trainRowsList.Count}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Test rows:      {testRowsList.Count}");

            if (trainRowsList.Count == 0 || testRowsList.Count == 0)
                throw new InvalidOperationException("Session-based split produced an empty train or test set.");

            int trainTrue = trainRowsList.Count(r => r.LabelHintDependenceNext);
            int trainFalse = trainRowsList.Count(r => !r.LabelHintDependenceNext);
            int testTrue = testRowsList.Count(r => r.LabelHintDependenceNext);
            int testFalse = testRowsList.Count(r => !r.LabelHintDependenceNext);

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Train label TRUE/FALSE: {trainTrue}/{trainFalse}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Test label TRUE/FALSE:  {testTrue}/{testFalse}");

            if (trainTrue == 0 || trainFalse == 0)
                throw new InvalidOperationException("Training split must contain both label classes.");

            if (testTrue == 0 || testFalse == 0)
                throw new InvalidOperationException("Test split must contain both label classes.");

            var trainData = mlContext.Data.LoadFromEnumerable(trainRowsList);
            var testData = mlContext.Data.LoadFromEnumerable(testRowsList);

            var allFeatureColumns = NumericFeatureColumns
                .Concat(EmbeddingFeatureColumns)
                .Append("CurrentHintModeEncoded")
                .Append("TaskTypeEncoded")
                .ToArray();

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Feature count: {allFeatureColumns.Length}");
            Log("[HintDependenceNextWindowTrainer_WithEmbeddings] Training...");

            var pipeline =
                mlContext.Transforms.Categorical.OneHotEncoding(
                    outputColumnName: "CurrentHintModeEncoded",
                    inputColumnName: nameof(BehaviorStateTrainingExample_NextWindow.CurrentHintMode))
                .Append(mlContext.Transforms.Categorical.OneHotEncoding(
                    outputColumnName: "TaskTypeEncoded",
                    inputColumnName: nameof(BehaviorStateTrainingExample_NextWindow.TaskType)))
                .Append(mlContext.Transforms.Concatenate(
                    "Features",
                    allFeatureColumns))
                .Append(mlContext.BinaryClassification.Trainers.FastTree(
    labelColumnName: nameof(BehaviorStateTrainingExample_NextWindow.LabelHintDependenceNext),
    featureColumnName: "Features"));

            var model = pipeline.Fit(trainData);

            Log("[HintDependenceNextWindowTrainer_WithEmbeddings] Evaluating on test set...");
            var predictions = model.Transform(testData);

            var metrics = mlContext.BinaryClassification.Evaluate(
                predictions,
                labelColumnName: nameof(BehaviorStateTrainingExample_NextWindow.LabelHintDependenceNext));

            var thresholdReport = BuildThresholdSweepReport(mlContext, predictions);
            Log("[HintDependenceNextWindowTrainer_WithEmbeddings] Threshold sweep built.");

            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Accuracy:          {metrics.Accuracy:F4}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] AUC:               {metrics.AreaUnderRocCurve:F4}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] F1Score:           {metrics.F1Score:F4}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] PositivePrecision: {metrics.PositivePrecision:F4}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] PositiveRecall:    {metrics.PositiveRecall:F4}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] NegativePrecision: {metrics.NegativePrecision:F4}");
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] NegativeRecall:    {metrics.NegativeRecall:F4}");

            var metricsReport = $@"
Accuracy:          {metrics.Accuracy:F4}
AUC:               {metrics.AreaUnderRocCurve:F4}
F1Score:           {metrics.F1Score:F4}
PositivePrecision: {metrics.PositivePrecision:F4}
PositiveRecall:    {metrics.PositiveRecall:F4}
NegativePrecision: {metrics.NegativePrecision:F4}
NegativeRecall:    {metrics.NegativeRecall:F4}
TrainSessions:     {trainGroups.Count}
TestSessions:      {testGroups.Count}
TrainRows:         {trainRowsList.Count}
TestRows:          {testRowsList.Count}

{thresholdReport}
";

            var metricsPath = Path.ChangeExtension(modelOutputPath, ".metrics.txt");
            File.WriteAllText(metricsPath, metricsReport);
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Metrics saved to: {metricsPath}");

            mlContext.Model.Save(model, trainData.Schema, modelOutputPath);
            Log($"[HintDependenceNextWindowTrainer_WithEmbeddings] Model saved to: {modelOutputPath}");
        }

        private static string BuildThresholdSweepReport(MLContext mlContext, IDataView predictions)
        {
            var allRows = mlContext.Data
                .CreateEnumerable<NextWindowPredictionRow>(predictions, reuseRowObject: false)
                .ToList();

            var nanCount = allRows.Count(r => float.IsNaN(r.Probability));

            var rows = allRows
                .Where(r => !float.IsNaN(r.Probability))
                .ToList();

            var thresholds = new[]
            {
                0.05f, 0.10f, 0.15f, 0.20f,
                0.25f, 0.30f, 0.35f, 0.40f
            };

            var sb = new StringBuilder();

            sb.AppendLine("Threshold Sweep");
            sb.AppendLine("Threshold | Accuracy | Precision | Recall | F1 | TP | FP | TN | FN");
            sb.AppendLine();
            sb.AppendLine("Probability Distribution");
            sb.AppendLine($"NaN Count: {nanCount}");

            if (rows.Count > 0)
            {
                var probs = rows.Select(r => r.Probability).ToList();
                sb.AppendLine($"Min: {probs.Min():0.0000}");
                sb.AppendLine($"Max: {probs.Max():0.0000}");
                sb.AppendLine($"Avg: {probs.Average():0.0000}");
            }
            else
            {
                sb.AppendLine("Min: n/a");
                sb.AppendLine("Max: n/a");
                sb.AppendLine("Avg: n/a");
            }

            sb.AppendLine();

            foreach (var threshold in thresholds)
            {
                int tp = 0, fp = 0, tn = 0, fn = 0;

                foreach (var row in rows)
                {
                    bool predictedPositive = row.Probability >= threshold;
                    bool actualPositive = row.LabelHintDependenceNext;

                    if (predictedPositive && actualPositive) tp++;
                    else if (predictedPositive && !actualPositive) fp++;
                    else if (!predictedPositive && !actualPositive) tn++;
                    else fn++;
                }

                double accuracy = SafeDivide(tp + tn, tp + tn + fp + fn);
                double precision = SafeDivide(tp, tp + fp);
                double recall = SafeDivide(tp, tp + fn);
                double f1 = SafeDivide(2.0 * precision * recall, precision + recall);

                sb.AppendLine(
                    $"{threshold,8:0.00} | {accuracy,8:0.0000} | {precision,9:0.0000} | {recall,6:0.0000} | {f1,6:0.0000} | {tp,2} | {fp,2} | {tn,3} | {fn,2}");
            }

            return sb.ToString();
        }

        private static double SafeDivide(double numerator, double denominator)
            => denominator == 0.0 ? 0.0 : numerator / denominator;
    }
}
