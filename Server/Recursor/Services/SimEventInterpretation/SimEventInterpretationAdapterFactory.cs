namespace NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;

// Resolves the correct interpretation adapter for a given sim by walking the
// registered adapter list in registration order.  Specific adapters must be
// listed before DefaultSimEventInterpretationAdapter, which acts as the
// catch-all fallback (AppliesTo always returns true).
public class SimEventInterpretationAdapterFactory : ISimEventInterpretationAdapterFactory
{
    private readonly IReadOnlyList<ISimEventInterpretationAdapter> _adapters;

    public SimEventInterpretationAdapterFactory(
        MedicalSupplyEventInterpretationAdapter medicalSupplyAdapter,
        DefaultSimEventInterpretationAdapter defaultAdapter)
    {
        // Order matters: specific adapters are checked before the default.
        _adapters = [medicalSupplyAdapter, defaultAdapter];
    }

    public ISimEventInterpretationAdapter GetAdapter(string simId)
    {
        foreach (var adapter in _adapters)
        {
            if (adapter.AppliesTo(simId))
                return adapter;
        }
        return _adapters[^1]; // unreachable — DefaultAdapter always matches
    }
}
