namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class SessionDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "Session";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string SimVersion { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public long EventCount { get; set; }
    public int BatchCount { get; set; }
    // Tracks events received since the last feature window was generated.
    // Reset to 0 after each window. Used by FeatureExtractionService to decide
    // whether accumulation threshold has been reached.
    public long EventsSinceLastWindow { get; set; }
    // All raw events received since the last feature window was produced, across every
    // batch in that span — appended to exactly once per incoming batch by
    // RecursorIngestionService (step 6), read (never mutated) by FeatureExtractionService
    // to build the window, and cleared only after that window's ADX ingest succeeds. Kept
    // in lockstep with EventsSinceLastWindow so a feature window always scores the full
    // accumulated event set rather than just the batch that happened to cross a trigger.
    public List<RawEventRecord> PendingFeatureWindowEvents { get; set; } = new();
    public string CurrentStage { get; set; } = "";
    public Dictionary<string, string> CurrentDifficultyProfile { get; set; } = new();
    public string? LatestFeatureWindowId { get; set; }
    public string? LatestBehaviorProfileId { get; set; }
    public string? LatestHypothesisSetId { get; set; }
    public string? LatestAdaptationId { get; set; }
    public SessionSummary Summary { get; set; } = new();
    public List<TrajectorySnapshot> RecentSnapshots { get; set; } = new();
    public int ConsecutiveStableMasteryWindows { get; set; }
    public int ConsecutiveRelapseWindows { get; set; }
    // Increments on stable_mastery_pattern or improving_pattern windows; reset on relapse or other.
    // Used to gate baseline hint-fade and hint-remove to avoid over-eager support reduction.
    public int ConsecutiveSupportFadeEligibleWindows { get; set; }
    // Stage 7 (corrective pass): durable-for-the-life-of-this-in-memory-session record of every
    // (WindowIndex, Horizon, ModelVersion) shadow behavior-state prediction that has actually
    // been ingested into ADX. Set immediately after EACH horizon's ingest succeeds — not once
    // after all three — so a retry following a partial failure (e.g. H1 succeeded, H2 threw)
    // only reprocesses the horizon(s) that never completed, and never re-ingests H1. Keying by
    // ModelVersion also means predictions from two different loaded model versions for the same
    // window/horizon are tracked independently rather than one hiding the other. Replaces the
    // single-scalar LastBehaviorStatePredictionWindowIndex, which could only represent "did we
    // attempt this window" (all-or-nothing, no per-horizon or per-version distinction) and could
    // itself be left stale by a partial failure. See RecursorIngestionService for the
    // per-session lock that also guards against two concurrent requests racing on this set, and
    // BehaviorStatePredictionKeys for the key format.
    public HashSet<string> CompletedBehaviorStatePredictionKeys { get; set; } = new();
}

public static class BehaviorStatePredictionKeys
{
    public static string For(int windowIndex, int horizon, string modelVersion) =>
        $"{windowIndex}:{horizon}:{modelVersion}";

    // Stage 4 (corrective pass): deterministic, durable identity for a single logical shadow
    // behavior-state prediction — SessionId + WindowIndex + Horizon + ModelVersion. Unlike
    // CompletedBehaviorStatePredictionKeys (process-local, in-memory, does not survive an app
    // restart or coordinate across multiple App Service instances), this ID is recomputed
    // identically from the same logical inputs no matter which process/instance/replay computes
    // it, and is persisted as the ADX row's PredictionId column. The same logical prediction
    // (same session/window/horizon/version) always produces the same ID — that is what lets
    // every downstream ADX query deduplicate by PredictionId instead of relying solely on the
    // in-memory guard. Physical duplicate rows are tolerated (ADX is append-only); logical
    // duplicates are not — every read/evaluation path collapses them via arg_max(CreatedAtUtc,
    // *) by PredictionId.
    public static string ComputePredictionId(string sessionId, int windowIndex, int horizon, string modelVersion)
    {
        var canonical = $"{sessionId}|{windowIndex}|{horizon}|{modelVersion}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return new Guid(hash[..16]).ToString("N");
    }

    // Stage 6/7 (corrective pass): rows ingested before the V3 migration added the PredictionId
    // column (via .create-merge, an additive schema change) have PredictionId == "" and can never
    // retroactively get a real one without a destructive backfill. Deduplicating those blank IDs
    // directly (arg_max by PredictionId) would collapse every legacy row across every session
    // into a single logical prediction, which is wrong. This mirrors the exact fallback used by
    // AdxDashboardQueryService.BehaviorStatePredictionDedupKql (KQL: iif(isempty(PredictionId),
    // strcat("legacy|", SessionId, "|", WindowIndex, "|", Horizon, "|", ModelVersion),
    // PredictionId)) so the two can be tested/verified in lockstep — new rows still dedup by the
    // deterministic PredictionId; legacy rows fall back to the same logical
    // (SessionId, WindowIndex, Horizon, ModelVersion) identity ComputePredictionId hashes.
    public static string ComputeLogicalPredictionId(
        string? predictionId, string sessionId, int windowIndex, int horizon, string modelVersion) =>
        string.IsNullOrEmpty(predictionId)
            ? $"legacy|{sessionId}|{windowIndex}|{horizon}|{modelVersion}"
            : predictionId;
}

public class SessionSummary
{
    public double CompletionPercent { get; set; }
    public int ErrorCount { get; set; }
    public int HintCount { get; set; }
    public int SafetyViolationCount { get; set; }
}
