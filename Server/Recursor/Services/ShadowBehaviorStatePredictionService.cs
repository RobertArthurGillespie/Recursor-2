using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IBehaviorStatePredictionService
{
    Task<BehaviorStatePrediction?> PredictAsync(BehaviorStateFeatureVector input);
}

public class ShadowBehaviorStatePredictionService : IBehaviorStatePredictionService
{
    public Task<BehaviorStatePrediction?> PredictAsync(BehaviorStateFeatureVector input)
    {
        return Task.FromResult<BehaviorStatePrediction?>(new BehaviorStatePrediction
        {
            ConfusionProbability = 0.0,
            HintDependenceProbability = 0.0,
            StableMasteryProbability = 0.0,
            ModelVersion = "shadow-noop-v1",
            InferenceMode = "shadow"
        });
    }
}
