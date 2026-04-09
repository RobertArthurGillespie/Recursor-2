namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Convenience wrapper for running Recursor ML training locally.
/// Not called at app startup — invoke manually for dev/research use only.
/// Update the file paths below to match your local environment before running.
/// </summary>
public static class RecursorMlTrainingRunner
{
    /// <summary>
    /// Trains the hint-dependence model using a local CSV export.
    /// DEV-ONLY: paths below are placeholders; adjust before use.
    /// </summary>
    public static void TrainHintDependenceModel()
    {
        // TODO (dev-only): update these paths before running locally.
        const string csvPath = @"C:\Users\Rober\source\repos\RecursorData\training\behavior_state_training_v4.csv";
        const string modelOutputPath = @"C:\Users\Rober\source\repos\RecursorData\models\hint_dependence_v1.zip";

        HintDependenceModelTrainer.Train(csvPath, modelOutputPath);
    }
}
