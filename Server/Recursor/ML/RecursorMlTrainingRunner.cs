using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Convenience wrapper for running Recursor ML training locally.
/// Not called at app startup — invoke manually for dev/research use only.
/// Update the file paths below to match your local environment before running.
/// </summary>
public static class RecursorMlTrainingRunner
{
    public static void TrainHintDependenceModel_Baseline()
    {
        const string csvPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\artifacts\run1\Autoencoder_set.csv";
        const string modelOutputPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\models\hint_dependence_baseline_v2.zip";

        HintDependenceModelTrainer_Baseline.Train(csvPath, modelOutputPath);
    }

    public static void TrainHintDependenceModel_WithEmbeddings()
    {
        const string csvPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\artifacts\run1\behavior_state_with_embeddings.csv";
        const string modelOutputPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\models\hint_dependence_with_embeddings_v2.zip";

        HintDependenceModelTrainer_WithEmbeddings.Train(csvPath, modelOutputPath);
    }

    public static void TrainHintDependenceNextWindow_Baseline()
    {
        const string csvPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\artifacts\run1\behavior_state_with_embeddings_next_window.csv";
        const string modelOutputPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\models\hint_dependence_next_window_baseline.zip";

        HintDependenceNextWindowTrainer_Baseline.Train(csvPath, modelOutputPath);
    }

    public static void TrainHintDependenceNextWindow_WithEmbeddings()
    {
        const string csvPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\artifacts\run1\behavior_state_with_embeddings_next_window.csv";
        const string modelOutputPath =
            @"C:\Users\Rober\source\repos\RecursorAutoEncoder\models\hint_dependence_next_window_with_embeddings.zip";

        HintDependenceNextWindowTrainer_WithEmbeddings.Train(csvPath, modelOutputPath);
    }

    public static void TrainConfusionModel()
    {
        const string csvPath =
            @"C:\Users\Rober\source\repos\RecursorData\training\confusion_and_stable_mastery_training.csv";
        const string modelOutputPath =
            @"C:\Users\Rober\source\repos\RecursorData\models\confusion_v1.zip";

        ConfusionModelTrainer.Train(csvPath, modelOutputPath);
    }

    public static void TrainStableMasteryModel()
    {
        const string csvPath =
            @"C:\Users\Rober\source\repos\RecursorData\training\confusion_and_stable_mastery_training.csv";
        const string modelOutputPath =
            @"C:\Users\Rober\source\repos\RecursorData\models\stable_mastery_v1.zip";

        StableMasteryModelTrainer.Train(csvPath, modelOutputPath);
    }

    // ── Clean v2 pipeline: ADX export → next-window CSV → train ─────────────────

    /// <summary>
    /// Exports BehaviorStateTrainingRows from ADX matching the supplied filter,
    /// builds a next-window CSV (LabelHintDependenceNext = next row's label),
    /// and writes it to <paramref name="csvOutputPath"/>.
    ///
    /// Call this first, then call TrainNextWindowHintDependence_CleanV2() with the
    /// same CSV path once you are happy with the exported data.
    ///
    /// Usage from Program.cs (after var app = builder.Build()):
    /// <code>
    ///   var exportSvc = app.Services.GetRequiredService&lt;IAdxTrainingExportService&gt;();
    ///   var filter = new BehaviorStateTrainingExportFilter
    ///   {
    ///       StartUtc      = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
    ///       InferenceMode = "shadow-mlnet",
    ///   };
    ///   await RecursorMlTrainingRunner.ExportNextWindowCsvAsync(
    ///       exportSvc, filter,
    ///       @"C:\RecursorData\training\behavior_state_next_window_v2_clean.csv");
    /// </code>
    /// </summary>
    public static async Task ExportNextWindowCsvAsync(
        IAdxTrainingExportService exportService,
        BehaviorStateTrainingExportFilter filter,
        string csvOutputPath)
    {
        Console.WriteLine($"[ExportNextWindowCsvAsync] Querying ADX...");

        var rows = await exportService.ExportCleanRowsAsync(filter);
        Console.WriteLine($"[ExportNextWindowCsvAsync] Exported {rows.Count} raw rows from ADX.");

        if (rows.Count == 0)
        {
            Console.WriteLine("[ExportNextWindowCsvAsync] No rows returned — check filters and ADX config.");
            return;
        }

        Console.WriteLine($"[ExportNextWindowCsvAsync] Building next-window dataset...");
        var stats = NextWindowDatasetBuilder.BuildAndWriteCsv(rows, csvOutputPath);

        Console.WriteLine($"[ExportNextWindowCsvAsync] Done.");
        Console.WriteLine($"  Input rows:        {stats.TotalInputRows}");
        Console.WriteLine($"  Sessions:          {stats.TotalSessions}");
        Console.WriteLine($"  Dropped (last):    {stats.DroppedAsLastInSession}");
        Console.WriteLine($"  Dropped (NaN):     {stats.DroppedNaN}");
        Console.WriteLine($"  Output rows:       {stats.TotalOutputRows}");
        Console.WriteLine($"  Label TRUE:        {stats.LabelTrueCount}");
        Console.WriteLine($"  Label FALSE:       {stats.LabelFalseCount}");
        Console.WriteLine($"  CSV written to:    {csvOutputPath}");
    }

    /// <summary>
    /// Trains the next-window hint-dependence model (Baseline / no embeddings) from a
    /// pre-built next-window CSV and saves the model zip.
    ///
    /// Use after ExportNextWindowCsvAsync() has produced a clean CSV.
    /// The output model is separate from the AutoEncoder-based artifacts —
    /// it will NOT overwrite hint_dependence_next_window_baseline.zip.
    ///
    /// Usage from Program.cs:
    /// <code>
    ///   RecursorMlTrainingRunner.TrainNextWindowHintDependence_CleanV2(
    ///       @"C:\RecursorData\training\behavior_state_next_window_v2_clean.csv",
    ///       @"C:\RecursorData\models\hint_dependence_next_window_v2_clean.zip");
    /// </code>
    /// </summary>
    public static void TrainNextWindowHintDependence_CleanV2(
        string csvPath,
        string modelOutputPath)
    {
        Console.WriteLine($"[TrainNextWindowHintDependence_CleanV2] Training from: {csvPath}");
        Console.WriteLine($"[TrainNextWindowHintDependence_CleanV2] Output model:  {modelOutputPath}");

        HintDependenceNextWindowTrainer_Baseline.Train(csvPath, modelOutputPath);

        Console.WriteLine($"[TrainNextWindowHintDependence_CleanV2] Complete.");
    }
}
