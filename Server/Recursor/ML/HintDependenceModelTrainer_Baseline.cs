using Microsoft.ML.Data;
using Microsoft.ML;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML
{
    public static class HintDependenceModelTrainer_Baseline
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

        // Keep these commented out for the first stricter evaluation pass.
        // They are likely too close to the target label and may create leakage.
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

            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Loading data from: {csvPath}");

            var mlContext = new MLContext(seed: 42);

            var loaderOptions = new TextLoader.Options
            {
                HasHeader = true,
                Separators = new[] { ',' },
                AllowQuoting = true,
                TrimWhitespace = true,
                AllowSparse = false
            };

            var rawData = mlContext.Data.LoadFromTextFile<BehaviorStateTrainingExample>(
                path: csvPath,
                options: loaderOptions);

            var rows = mlContext.Data
                .CreateEnumerable<BehaviorStateTrainingExample>(rawData, reuseRowObject: false)
                .Where(r => !string.IsNullOrWhiteSpace(r.SessionId))
                .ToList();

            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Parsed rows: {rows.Count}");

            if (rows.Count == 0)
                throw new InvalidOperationException("No rows were parsed from the CSV.");

            int trueCount = rows.Count(r => r.LabelHintDependence);
            int falseCount = rows.Count(r => !r.LabelHintDependence);

            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] LabelHintDependence TRUE rows:  {trueCount}");
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] LabelHintDependence FALSE rows: {falseCount}");

            if (trueCount == 0 || falseCount == 0)
                throw new InvalidOperationException(
                    $"Dataset must contain both TRUE and FALSE LabelHintDependence values. TRUE={trueCount}, FALSE={falseCount}");

            // Session-based split
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

            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Train sessions: {trainGroups.Count}");
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Test sessions:  {testGroups.Count}");
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Train rows:     {trainRowsList.Count}");
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Test rows:      {testRowsList.Count}");

            if (trainRowsList.Count == 0 || testRowsList.Count == 0)
                throw new InvalidOperationException("Session-based split produced an empty train or test set.");

            int trainTrue = trainRowsList.Count(r => r.LabelHintDependence);
            int trainFalse = trainRowsList.Count(r => !r.LabelHintDependence);
            int testTrue = testRowsList.Count(r => r.LabelHintDependence);
            int testFalse = testRowsList.Count(r => !r.LabelHintDependence);

            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Train label TRUE/FALSE: {trainTrue}/{trainFalse}");
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Test label TRUE/FALSE:  {testTrue}/{testFalse}");

            if (trainTrue == 0 || trainFalse == 0)
                throw new InvalidOperationException("Training split must contain both label classes.");

            if (testTrue == 0 || testFalse == 0)
                throw new InvalidOperationException("Test split must contain both label classes.");

            var trainData = mlContext.Data.LoadFromEnumerable(trainRowsList);
            var testData = mlContext.Data.LoadFromEnumerable(testRowsList);

            var allFeatureColumns = NumericFeatureColumns
                .Append("CurrentHintModeEncoded")
                .Append("TaskTypeEncoded")
                .ToArray();

            var pipeline =
                mlContext.Transforms.Categorical.OneHotEncoding(
                    outputColumnName: "CurrentHintModeEncoded",
                    inputColumnName: nameof(BehaviorStateTrainingExample.CurrentHintMode))
                .Append(mlContext.Transforms.Categorical.OneHotEncoding(
                    outputColumnName: "TaskTypeEncoded",
                    inputColumnName: nameof(BehaviorStateTrainingExample.TaskType)))
                .Append(mlContext.Transforms.Concatenate(
                    "Features",
                    allFeatureColumns))
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                    labelColumnName: nameof(BehaviorStateTrainingExample.LabelHintDependence),
                    featureColumnName: "Features"));

            Console.WriteLine("[HintDependenceModelTrainer_Baseline] Training...");
            var model = pipeline.Fit(trainData);

            Console.WriteLine("[HintDependenceModelTrainer_Baseline] Evaluating on test set...");
            var predictions = model.Transform(testData);

            var metrics = mlContext.BinaryClassification.Evaluate(
                predictions,
                labelColumnName: nameof(BehaviorStateTrainingExample.LabelHintDependence));

            Console.WriteLine($"  Accuracy:          {metrics.Accuracy:F4}");
            Console.WriteLine($"  AUC:               {metrics.AreaUnderRocCurve:F4}");
            Console.WriteLine($"  F1Score:           {metrics.F1Score:F4}");
            Console.WriteLine($"  PositivePrecision: {metrics.PositivePrecision:F4}");
            Console.WriteLine($"  PositiveRecall:    {metrics.PositiveRecall:F4}");
            Console.WriteLine($"  NegativePrecision: {metrics.NegativePrecision:F4}");
            Console.WriteLine($"  NegativeRecall:    {metrics.NegativeRecall:F4}");

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
";

            var metricsPath = Path.ChangeExtension(modelOutputPath, ".metrics.txt");
            File.WriteAllText(metricsPath, metricsReport);
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Metrics saved to: {metricsPath}");

            mlContext.Model.Save(model, trainData.Schema, modelOutputPath);
            Console.WriteLine($"[HintDependenceModelTrainer_Baseline] Model saved to: {modelOutputPath}");
        }
    }
}
