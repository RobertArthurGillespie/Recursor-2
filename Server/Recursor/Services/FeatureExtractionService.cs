using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IFeatureExtractionService
{
    // Returns a FeatureWindowDocument when a trigger condition is met, or null otherwise.
    // Trigger conditions (any one sufficient):
    //   1. Events accumulated since last window >= AccumulationThreshold (50)
    //   2. Batch contains a stage/task completion event (generic, all sims)
    //   3. Sim-specific force trigger (e.g. safety events for the current sim)
    FeatureWindowDocument? TryExtractWindow(SessionDocument session, RawEventBatch batch);
}

public class FeatureExtractionService : IFeatureExtractionService
{
    private const int AccumulationThreshold = 50;

    // Generic completion triggers shared by all sims.
    private static readonly HashSet<string> StageTriggerTypes =
        ["task_complete", "stage_complete"];

    private readonly ISimEventInterpretationAdapterFactory _adapterFactory;

    public FeatureExtractionService(ISimEventInterpretationAdapterFactory adapterFactory)
    {
        _adapterFactory = adapterFactory;
    }

    public FeatureWindowDocument? TryExtractWindow(SessionDocument session, RawEventBatch batch)
    {
        if (batch.Events.Count == 0)
            return null;

        var adapter = _adapterFactory.GetAdapter(session.SimId);

        bool accumulationTrigger = session.EventsSinceLastWindow >= AccumulationThreshold;
        bool stageTrigger        = batch.Events.Any(e => StageTriggerTypes.Contains(e.EventType));
        bool simSpecificTrigger  = adapter.ShouldForceWindow(batch);

        if (!accumulationTrigger && !stageTrigger && !simSpecificTrigger)
            return null;

        var events = batch.Events;
        long minSeq      = events.Min(e => e.SequenceNumber);
        long maxSeq      = events.Max(e => e.SequenceNumber);
        DateTime minTime = events.Min(e => e.TimestampUtc);
        DateTime maxTime = events.Max(e => e.TimestampUtc);

        return new FeatureWindowDocument
        {
            Id                  = Guid.NewGuid().ToString(),
            SessionId           = session.SessionId,
            UserId              = session.UserId,
            WindowIndex         = session.BatchCount,
            WindowType          = stageTrigger       ? "stage-completion" :
                                  simSpecificTrigger ? "safety-trigger"   :
                                                       "accumulation",
            WindowStartSequence = minSeq,
            WindowEndSequence   = maxSeq,
            WindowStartUtc      = minTime,
            WindowEndUtc        = maxTime,
            SimId               = session.SimId,
            ScenarioId          = session.ScenarioId,
            Features            = adapter.ExtractFeatures(session, events)
        };
    }
}
