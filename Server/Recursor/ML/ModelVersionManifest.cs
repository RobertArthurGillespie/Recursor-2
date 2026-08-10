namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Shared manifest schema written into every published immutable model version directory
/// (versions/{family}/{version}/manifest.json) by the temporal trainers (Phase 10E
/// behavior-state, temporal-risk, temporal-elevated-risk). A directory is only considered a
/// published version once this file exists — see ModelVersionPublisher.VersionExists.
/// </summary>
public class ModelVersionManifest
{
    public string ModelFamily { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string CompletionStatus { get; set; } = "Complete";
    public string FeatureEmbeddingVersion { get; set; } = "";
    public int SplitSeed { get; set; }
    public List<ModelVersionHorizonManifest> Horizons { get; set; } = [];
}

public class ModelVersionHorizonManifest
{
    public int Horizon { get; set; }
    public string FileName { get; set; } = "";
    public int TrainingRowCount { get; set; }
    public int ValidationRowCount { get; set; }
    public Dictionary<string, int> LabelDistribution { get; set; } = [];
    public Dictionary<string, int> SimulationDistribution { get; set; } = [];
    public double ValidationMicroAccuracy { get; set; }
    public double ValidationMacroF1 { get; set; }

    // Stage 9: grouped-stratified split summary, so a trained version's exact train/validation
    // partition (by class and by simulation) is reproducible and auditable from the manifest
    // alone, without needing to re-run the split or query ADX again.
    public Dictionary<string, int> TrainLabelDistribution { get; set; } = [];
    public Dictionary<string, int> ValidationLabelDistribution { get; set; } = [];
    public Dictionary<string, int> TrainSimulationDistribution { get; set; } = [];
    public Dictionary<string, int> ValidationSimulationDistribution { get; set; } = [];

    // Stage 6 (corrective pass): session counts and explicit coverage/cross-simulation status,
    // so a reader of the manifest alone (no re-query, no re-split) can tell whether this
    // version's combined metrics can honestly be described as cross-simulation validated —
    // never inferred implicitly from the raw distributions above.
    public int TrainSessionCount { get; set; }
    public int ValidationSessionCount { get; set; }
    public List<string> ClassesAbsentFromTraining { get; set; } = [];
    public List<string> ClassesAbsentFromValidation { get; set; } = [];
    public List<string> SimulationsAbsentFromTraining { get; set; } = [];
    public List<string> SimulationsAbsentFromValidation { get; set; } = [];
    public string CrossSimulationValidationStatus { get; set; } = "";
    public string CrossSimulationValidationReason { get; set; } = "";
}
