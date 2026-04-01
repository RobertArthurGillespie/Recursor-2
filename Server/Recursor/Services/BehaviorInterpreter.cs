using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IBehaviorInterpreter
{
    BehaviorProfileDocument BuildBehaviorProfile(FeatureWindowDocument featureWindow);
    HypothesisSetDocument BuildHypothesisSet(BehaviorProfileDocument profile);
}

public class BehaviorInterpreter : IBehaviorInterpreter
{
    // Threshold below which a dimension is considered impaired.
    private const double ImpairmentThreshold = 0.5;

    public BehaviorProfileDocument BuildBehaviorProfile(FeatureWindowDocument featureWindow)
    {
        var features = featureWindow.Features;

        // Each feature maps directly to the corresponding behavioral dimension.
        // Confidence is proportional to how far the score is from the midpoint (0.5).
        var scores = new Dictionary<string, DimensionScore>
        {
            ["attentionDetection"] = Score(features.AttentionDetection, "error rate in batch"),
            ["goalUnderstanding"] = Score(features.GoalUnderstanding, "average event score"),
            ["procedureSequencing"] = Score(features.ProcedureSequencing, "step completion rate"),
            ["paceRegulation"] = Score(features.PaceRegulation, "normalized average duration"),
            ["selfCorrection"] = Score(features.SelfCorrection, "error recovery rate"),
            ["feedbackResponsiveness"] = Score(features.FeedbackResponsiveness, "hint request rate"),
            ["safetyCompliance"] = Score(features.SafetyCompliance, "safety violation rate"),
            ["taskContinuity"] = Score(features.TaskContinuity, "session progress and completion")
        };

        return new BehaviorProfileDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "BehaviorProfile",
            SessionId = featureWindow.SessionId,
            WindowIndex = featureWindow.WindowIndex,
            SourceFeatureWindowId = featureWindow.Id,
            DimensionScores = scores
        };
    }

    public HypothesisSetDocument BuildHypothesisSet(BehaviorProfileDocument profile)
    {
        var hypotheses = new List<BehavioralHypothesis>();

        // For each impaired dimension, generate a hypothesis.
        foreach (var (dimension, score) in profile.DimensionScores)
        {
            if (score.Score < ImpairmentThreshold)
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = HypothesisLabel(dimension, score.Score),
                    Dimensions = [dimension],
                    Confidence = score.Confidence,
                    Evidence = score.Evidence
                });
            }
        }

        // If 2 or more dimensions are impaired, add a compound hypothesis.
        if (hypotheses.Count >= 2)
        {
            hypotheses.Add(new BehavioralHypothesis
            {
                Label = "learner-overload",
                Dimensions = hypotheses.Select(h => h.Dimensions[0]).ToList(),
                Confidence = hypotheses.Average(h => h.Confidence),
                Evidence = $"{hypotheses.Count} dimensions below threshold"
            });
        }

        return new HypothesisSetDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "HypothesisSet",
            SessionId = profile.SessionId,
            WindowIndex = profile.WindowIndex,
            SourceBehaviorProfileId = profile.Id,
            Hypotheses = hypotheses,
            InterpreterMode = "rule-based",
            InterpreterVersion = "1.0"
        };
    }

    private static DimensionScore Score(double value, string evidence)
    {
        double confidence = Math.Abs(value - 0.5) * 2.0; // 0.0 at midpoint, 1.0 at extremes
        return new DimensionScore
        {
            Score = value,
            Confidence = confidence,
            Evidence = evidence
        };
    }

    private static string HypothesisLabel(string dimension, double score)
    {
        return dimension switch
        {
            "attentionDetection" => "attention-deficit",
            "goalUnderstanding" => "goal-confusion",
            "procedureSequencing" => "sequencing-difficulty",
            "paceRegulation" => score < 0.3 ? "excessive-slowness" : "pace-irregularity",
            "selfCorrection" => "low-self-correction",
            "feedbackResponsiveness" => "hint-dependency",
            "safetyCompliance" => "safety-risk",
            "taskContinuity" => "task-discontinuity",
            _ => $"{dimension}-impaired"
        };
    }
}
