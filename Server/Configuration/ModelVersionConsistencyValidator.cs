namespace NCATAIBlazorFrontendTest.Server.Configuration;

/// <summary>
/// Stage 10 (corrective pass): pure validation logic for the "configured ModelVersion vs.
/// explicit H{n}ModelPath" consistency check — extracted out of Program.cs so it is unit
/// testable. Originally written to catch drift like the historical bug where
/// Recursor:Models:TemporalElevatedRiskModelVersion said "temporal-elevated-risk-v1" while
/// TemporalElevatedRiskH1..3ModelPath all pointed at "_v2.zip" files.
///
/// Stage 3 (corrective pass): ResolveVersionedModelPath no longer silently discards a
/// non-matching explicit path — an explicit path, once configured, is always honored verbatim
/// (see TemporalBehaviorStateModelTrainingService/TemporalElevatedRiskModelTrainingService
/// .ResolveVersionedModelPath). This check remains as an informational-only signal for the
/// legacy flat-filename naming convention (`..._h{n}_{suffix}.zip`); it never gates behavior.
/// The authoritative, behavior-affecting check for an explicit override is now
/// ModelVersionManifestValidator.ValidateExplicitOverridePath, which cross-checks against a
/// manifest.json if one is present alongside the override rather than the filename. This
/// validator never throws — a missing/differently-named model file is a normal, tolerated state
/// in this architecture (see the prediction services' own shadow/no-op fallback), so startup
/// must never fail because of it.
/// </summary>
public static class ModelVersionConsistencyValidator
{
    /// <summary>
    /// Returns a human-readable, informational-only warning if <paramref name="explicitConfigPath"/>
    /// is set but its filename does not match the legacy flat-naming convention implied by
    /// <paramref name="configuredVersion"/> (via <paramref name="expectedSuffix"/>, e.g. from
    /// ExtractVersionSuffix) for this horizon; otherwise null. The path is used exactly as
    /// configured either way — see ModelVersionManifestValidator for the check that actually
    /// gates startup.
    /// </summary>
    public static string? CheckExplicitPathMatchesVersion(
        string familyName, int horizon, string configuredVersion, string expectedSuffix, string? explicitConfigPath)
    {
        if (string.IsNullOrWhiteSpace(explicitConfigPath)) return null;

        var fileName = Path.GetFileName(explicitConfigPath);
        var expectedFragment = $"_h{horizon}_{expectedSuffix}.";
        if (fileName.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase))
            return null;

        return
            $"Model version/path naming mismatch for {familyName} horizon {horizon}: configured " +
            $"version '{configuredVersion}' expects a legacy filename containing " +
            $"'{expectedFragment}', but the explicit configured path '{explicitConfigPath}' does not " +
            "match it. The path is still used exactly as configured — this is informational only. " +
            "If this path was meant for a different version, update " +
            $"Recursor:Models:{familyName}ModelVersion or the H{horizon}ModelPath so they agree.";
    }

    /// <summary>Checks all three horizons for a family and returns every mismatch found (empty if none).</summary>
    public static List<string> CheckAllHorizons(
        string familyName, string configuredVersion, string expectedSuffix,
        string? explicitH1, string? explicitH2, string? explicitH3)
    {
        var problems = new List<string>();
        var explicitPaths = new[] { explicitH1, explicitH2, explicitH3 };
        for (int h = 1; h <= 3; h++)
        {
            var problem = CheckExplicitPathMatchesVersion(familyName, h, configuredVersion, expectedSuffix, explicitPaths[h - 1]);
            if (problem is not null) problems.Add(problem);
        }
        return problems;
    }
}
