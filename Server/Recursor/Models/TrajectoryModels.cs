namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class TrajectorySnapshot
{
    public int WindowIndex { get; set; }
    public double AttentionDetection { get; set; }
    public double GoalUnderstanding { get; set; }
    public double ProcedureSequencing { get; set; }
    public double SelfCorrection { get; set; }
    public double FeedbackResponsiveness { get; set; }
    public double ConfusionScore { get; set; }
    public double HesitationScore { get; set; }
    public double ImpulsivityScore { get; set; }
    public double HintDependenceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class TrajectoryAnalysisResult
{
    public bool HasEnoughHistory { get; set; }
    public bool IsImproving { get; set; }
    public bool IsWorsening { get; set; }
    public bool IsStableHighPerformance { get; set; }
    public bool IsRelapsing { get; set; }
    public double ConfusionTrend { get; set; }
    public double HintDependenceTrend { get; set; }
    public double GoalTrend { get; set; }
    public double AttentionTrend { get; set; }
    public List<string> TrajectoryLabels { get; set; } = new();
}
