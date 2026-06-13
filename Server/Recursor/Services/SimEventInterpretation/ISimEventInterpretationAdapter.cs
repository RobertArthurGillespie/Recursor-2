using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;

public interface ISimEventInterpretationAdapter
{
    bool AppliesTo(string simId);

    // Returns true when this batch should force a feature window immediately,
    // regardless of the accumulation threshold.
    bool ShouldForceWindow(RawEventBatch batch);

    BehavioralFeatureSet ExtractFeatures(SessionDocument session, List<RawEventRecord> events);
}

public interface ISimEventInterpretationAdapterFactory
{
    ISimEventInterpretationAdapter GetAdapter(string simId);
}
