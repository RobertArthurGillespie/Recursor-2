using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IBehaviorInterpreter
{
    BehaviorProfileDocument BuildBehaviorProfile(FeatureWindowDocument featureWindow);
    HypothesisSetDocument BuildHypothesisSet(BehaviorProfileDocument profile);
}

public class BehaviorInterpreter : IBehaviorInterpreter
{
    private readonly IBehaviorScoringService _behaviorScoringService;

    public BehaviorInterpreter(IBehaviorScoringService behaviorScoringService)
    {
        _behaviorScoringService = behaviorScoringService;
    }
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

        var behaviorscores = _behaviorScoringService.Score(featureWindow);

        return new BehaviorProfileDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "BehaviorProfile",
            SessionId = featureWindow.SessionId,
            WindowIndex = featureWindow.WindowIndex,
            SourceFeatureWindowId = featureWindow.Id,
            DimensionScores = scores,
            BehaviorScores = behaviorscores
        };
    }

    public HypothesisSetDocument BuildHypothesisSet(BehaviorProfileDocument profile)
    {
        var hypotheses = new List<BehavioralHypothesis>();

        foreach (var (dimension, score) in profile.DimensionScores)
        {
            if (score.Score < ImpairmentThreshold)
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = HypothesisLabel(dimension, score.Score),
                    Dimensions = new List<string> { dimension },
                    Confidence = score.Confidence,
                    Evidence = new List<string> { score.Evidence }
                });
            }
        }

        var scores = profile.BehaviorScores;

        if (scores is not null)
        {
            if (scores.ConfusionScore >= 0.65)
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = "confusion_pattern",
                    Dimensions = new List<string> { "goalUnderstanding", "attentionDetection" },
                    Confidence = scores.ConfusionScore,
                    Evidence = new List<string>
                {
                    $"ConfusionScore={scores.ConfusionScore:0.00}",
                    "High wrong-target behavior and repeated low-quality selections"
                }
                });
            }

            if (scores.HesitationScore >= 0.65)
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = "hesitation_pattern",
                    Dimensions = new List<string> { "paceRegulation" },
                    Confidence = scores.HesitationScore,
                    Evidence = new List<string>
                {
                    $"HesitationScore={scores.HesitationScore:0.00}",
                    "Long decision times and slow correction latency"
                }
                });
            }

            if (scores.ImpulsivityScore >= 0.65)
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = "impulsivity_pattern",
                    Dimensions = new List<string> { "paceRegulation", "selfCorrection" },
                    Confidence = scores.ImpulsivityScore,
                    Evidence = new List<string>
                {
                    $"ImpulsivityScore={scores.ImpulsivityScore:0.00}",
                    "Rapid low-quality actions and elevated premature advancement"
                }
                });
            }

            if (scores.HintDependenceScore >= 0.60)
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = "hint_dependence_pattern",
                    Dimensions = new List<string> { "feedbackResponsiveness" },
                    Confidence = scores.HintDependenceScore,
                    Evidence = new List<string>
                {
                    $"HintDependenceScore={scores.HintDependenceScore:0.00}",
                    "Frequent hint reliance or cue-driven success"
                }
                });
            }

            if (!string.IsNullOrWhiteSpace(scores.PredictedState) &&
                scores.PredictedState == "confused_and_hint_dependent")
            {
                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = "compound_confusion_hint_dependence",
                    Dimensions = new List<string> { "goalUnderstanding", "feedbackResponsiveness" },
                    Confidence = Math.Max(scores.ConfusionScore, scores.HintDependenceScore),
                    Evidence = new List<string>
                {
                    "Combined confusion and hint dependence state detected"
                }
                });
            }

            var impairedDimensionCount = profile.DimensionScores.Count(kvp => kvp.Value.Score < ImpairmentThreshold);

            bool hasGoal = profile.DimensionScores.TryGetValue("goalUnderstanding", out var goalScore);
            bool hasAttention = profile.DimensionScores.TryGetValue("attentionDetection", out var attentionScore);
            bool hasSelfCorrection = profile.DimensionScores.TryGetValue("selfCorrection", out var selfCorrectionScore);

            bool recoveryDetected =
                hasGoal &&
                hasAttention &&
                hasSelfCorrection &&
                goalScore.Score >= 0.65 &&
                attentionScore.Score >= 0.65 &&
                selfCorrectionScore.Score >= 0.60 &&
                scores.ConfusionScore < 0.45 &&
                scores.ImpulsivityScore < 0.45 &&
                impairedDimensionCount <= 1;

            if (recoveryDetected)
            {
                double recoveryConfidence = Math.Min(
                    1.0,
                    (goalScore.Score + attentionScore.Score + selfCorrectionScore.Score) / 3.0
                );

                hypotheses.Add(new BehavioralHypothesis
                {
                    Label = "recovery_pattern",
                    Dimensions = new List<string> { "goalUnderstanding", "attentionDetection", "selfCorrection" },
                    Confidence = recoveryConfidence,
                    Evidence = new List<string>
                {
                    $"goalUnderstanding={goalScore.Score:0.00}",
                    $"attentionDetection={attentionScore.Score:0.00}",
                    $"selfCorrection={selfCorrectionScore.Score:0.00}",
                    $"ConfusionScore={scores.ConfusionScore:0.00}",
                    $"ImpulsivityScore={scores.ImpulsivityScore:0.00}",
                    "Recent performance suggests stabilizing behavior and reduced support need"
                }
                });
            }
        }

        var overloadSourceHypotheses = hypotheses
            .Where(h => h.Label != "recovery_pattern")
            .ToList();

        if (overloadSourceHypotheses.Count >= 2)
        {
            hypotheses.Add(new BehavioralHypothesis
            {
                Label = "learner-overload",
                Dimensions = overloadSourceHypotheses.Select(h => h.Dimensions[0]).ToList(),
                Confidence = overloadSourceHypotheses.Average(h => h.Confidence),
                Evidence = new List<string>
            {
                $"{overloadSourceHypotheses.Count} hypotheses indicate concurrent impairment"
            }
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
            InterpreterVersion = "1.1"
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
