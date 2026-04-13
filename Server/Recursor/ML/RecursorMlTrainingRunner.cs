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
}
