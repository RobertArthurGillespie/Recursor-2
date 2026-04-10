using Microsoft.ML;
using Microsoft.ML.Data;
using System.IO;
using System.Linq;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

public static class ConfusionModelTrainer
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
        "HintDependenceScore",
        "GoalTrend",
        "AttentionTrend",
        "ConfusionTrend",
        "HintDependenceTrend",
        "CurrentDifficulty",
        "CurrentTimePressure",
        "CurrentErrorTolerance",
        "ConsecutiveStableMasteryWindows",
        "ConsecutiveRelapseWindows",
        "EventCountInWindow",
        "ErrorCountInWindow",
        "HintCountInWindow",
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

        Console.WriteLine($"[ConfusionModelTrainer] Loading data from: {csvPath}");

        var mlContext = new MLContext(seed: 42);

        var loaderOptions = new TextLoader.Options
        {
            HasHeader     = true,
            Separators    = new[] { ',' },
            AllowQuoting  = true,
            TrimWhitespace = true,
            AllowSparse   = false
        };

        var rawData = mlContext.Data.LoadFromTextFile<BehaviorStateTrainingExample>(
            path: csvPath,
            options: loaderOptions);

        var rows = mlContext.Data
            .CreateEnumerable<BehaviorStateTrainingExample>(rawData, reuseRowObject: false)
            .ToList();

        Console.WriteLine($"[ConfusionModelTrainer] Parsed rows: {rows.Count}");

        if (rows.Count == 0)
            throw new InvalidOperationException("No rows were parsed from the CSV.");

        int trueCount  = rows.Count(r => r.LabelConfusion);
        int falseCount = rows.Count(r => !r.LabelConfusion);

        Console.WriteLine($"[ConfusionModelTrainer] LabelConfusion TRUE rows:  {trueCount}");
        Console.WriteLine($"[ConfusionModelTrainer] LabelConfusion FALSE rows: {falseCount}");

        if (trueCount == 0 || falseCount == 0)
            throw new InvalidOperationException(
                $"Dataset must contain both TRUE and FALSE LabelConfusion values. TRUE={trueCount}, FALSE={falseCount}");

        var data  = mlContext.Data.LoadFromEnumerable(rows);
        var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.25);

        var trainRows = mlContext.Data.CreateEnumerable<BehaviorStateTrainingExample>(split.TrainSet, reuseRowObject: false).Count();
        var testRows  = mlContext.Data.CreateEnumerable<BehaviorStateTrainingExample>(split.TestSet,  reuseRowObject: false).Count();

        Console.WriteLine($"[ConfusionModelTrainer] Train rows: {trainRows}");
        Console.WriteLine($"[ConfusionModelTrainer] Test rows:  {testRows}");

        if (trainRows == 0)
            throw new InvalidOperationException("Train/test split produced 0 training rows.");

        var allFeatureColumns = NumericFeatureColumns
            .Append("CurrentHintModeEncoded")
            .Append("TaskTypeEncoded")
            .ToArray();

        var pipeline =
            mlContext.Transforms.Categorical.OneHotEncoding(
                outputColumnName: "CurrentHintModeEncoded",
                inputColumnName:  nameof(BehaviorStateTrainingExample.CurrentHintMode))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding(
                outputColumnName: "TaskTypeEncoded",
                inputColumnName:  nameof(BehaviorStateTrainingExample.TaskType)))
            .Append(mlContext.Transforms.Concatenate(
                "Features",
                allFeatureColumns))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                labelColumnName:   nameof(BehaviorStateTrainingExample.LabelConfusion),
                featureColumnName: "Features"));

        Console.WriteLine("[ConfusionModelTrainer] Training...");
        var model = pipeline.Fit(split.TrainSet);

        Console.WriteLine("[ConfusionModelTrainer] Evaluating on test set...");
        var predictions = model.Transform(split.TestSet);

        var metrics = mlContext.BinaryClassification.Evaluate(
            predictions,
            labelColumnName: nameof(BehaviorStateTrainingExample.LabelConfusion));

        Console.WriteLine($"  Accuracy:          {metrics.Accuracy:F4}");
        Console.WriteLine($"  AUC:               {metrics.AreaUnderRocCurve:F4}");
        Console.WriteLine($"  F1Score:           {metrics.F1Score:F4}");
        Console.WriteLine($"  PositivePrecision: {metrics.PositivePrecision:F4}");
        Console.WriteLine($"  PositiveRecall:    {metrics.PositiveRecall:F4}");
        Console.WriteLine($"  NegativePrecision: {metrics.NegativePrecision:F4}");
        Console.WriteLine($"  NegativeRecall:    {metrics.NegativeRecall:F4}");

        mlContext.Model.Save(model, split.TrainSet.Schema, modelOutputPath);
        Console.WriteLine($"[ConfusionModelTrainer] Model saved to: {modelOutputPath}");
    }
}
