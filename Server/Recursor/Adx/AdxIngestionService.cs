using System.Data;
using Kusto.Data.Common;
using Kusto.Data.Ingestion;
using Kusto.Ingest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxIngestionService
{
    Task IngestRawEventsAsync(IEnumerable<RawEventRow> rows);
    Task IngestFeatureWindowAsync(FeatureWindowRow row);
    Task IngestBehaviorProfileAsync(BehaviorProfileRow row);
    Task IngestHypothesisSetAsync(HypothesisSetRow row);
    Task IngestAdaptationDecisionAsync(AdaptationDecisionRow row);
    Task IngestBehaviorStateTrainingRowAsync(BehaviorStateTrainingRow row);
    Task IngestUserBehaviorProfileAsync(UserBehaviorProfileRow row);
    Task IngestUserBehaviorProfileUpdateAsync(UserBehaviorProfileUpdateRow row);
    Task IngestAdaptationEffectivenessAsync(AdaptationEffectivenessRow row);
}

public class AdxIngestionService : IAdxIngestionService
{
    private readonly IKustoQueuedIngestClient? _ingestClient;
    private readonly string _database;
    private readonly ILogger<AdxIngestionService> _logger;

    public AdxIngestionService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxIngestionService> logger)
    {
        // GetService returns null when IKustoQueuedIngestClient is not registered
        // (i.e. Adx:ClusterUri is not configured). Services skip ADX calls with a warning.
        _ingestClient = services.GetService<IKustoQueuedIngestClient>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
        _logger = logger;
    }

    // ── Public interface ──────────────────────────────────────────────────────

    public async Task IngestRawEventsAsync(IEnumerable<RawEventRow> rows)
    {
        if (!CheckClient("RawEvents")) return;

        var table = BuildRawEventsTable(rows);
        var props = new KustoQueuedIngestionProperties(_database, "RawEvents")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "RawEventsCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestFeatureWindowAsync(FeatureWindowRow row)
    {
        if (!CheckClient("FeatureWindows")) return;

        var table = BuildFeatureWindowsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "FeatureWindows")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "FeatureWindowsCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestBehaviorProfileAsync(BehaviorProfileRow row)
    {
        if (!CheckClient("BehaviorProfiles")) return;

        var table = BuildBehaviorProfilesTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "BehaviorProfiles")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "BehaviorProfilesCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestHypothesisSetAsync(HypothesisSetRow row)
    {
        if (!CheckClient("HypothesisSets")) return;

        var table = BuildHypothesisSetsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "HypothesisSets")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "HypothesisSetsCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestAdaptationDecisionAsync(AdaptationDecisionRow row)
    {
        if (!CheckClient("AdaptationDecisions_v2")) return;

        var table = BuildAdaptationDecisionsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "AdaptationDecisions_v2")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "AdaptationDecisionsV2CsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestBehaviorStateTrainingRowAsync(BehaviorStateTrainingRow row)
    {
        if (!CheckClient("BehaviorStateTrainingRows")) return;

        var table = BuildBehaviorStateTrainingRowsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "BehaviorStateTrainingRows")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "BehaviorStateTrainingRowsCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestUserBehaviorProfileAsync(UserBehaviorProfileRow row)
    {
        if (!CheckClient("UserBehaviorProfiles")) return;

        var table = BuildUserBehaviorProfilesTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "UserBehaviorProfiles")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "UserBehaviorProfilesCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestUserBehaviorProfileUpdateAsync(UserBehaviorProfileUpdateRow row)
    {
        if (!CheckClient("UserBehaviorProfileUpdates")) return;

        var table = BuildUserBehaviorProfileUpdatesTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "UserBehaviorProfileUpdates")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "UserBehaviorProfileUpdatesCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    public async Task IngestAdaptationEffectivenessAsync(AdaptationEffectivenessRow row)
    {
        if (!CheckClient("AdaptationEffectiveness")) return;

        var table = BuildAdaptationEffectivenessTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "AdaptationEffectiveness")
        {
            Format = DataSourceFormat.csv,
            IngestionMapping = new IngestionMapping
            {
                IngestionMappingKind = IngestionMappingKind.Csv,
                IngestionMappingReference = "AdaptationEffectivenessCsvMapping"
            }
        };

        using var reader = table.CreateDataReader();
        await _ingestClient!.IngestFromDataReaderAsync(reader, props);
    }

    // ── DataTable builders ────────────────────────────────────────────────────
    // Each method defines columns that match the ADX table schema in recursor-adx-plan.txt.
    // Dynamic ADX columns are sent as JSON strings; ADX auto-parses them into dynamic values.

    private static DataTable BuildRawEventsTable(IEnumerable<RawEventRow> rows)
    {
        var table = new DataTable("RawEvents");
        table.Columns.Add("SessionId",       typeof(string));
        table.Columns.Add("UserId",          typeof(string));
        table.Columns.Add("SimId",           typeof(string));
        table.Columns.Add("SimVersion",      typeof(string));
        table.Columns.Add("ScenarioId",      typeof(string));
        table.Columns.Add("BatchId",         typeof(string));
        table.Columns.Add("BatchSequence",   typeof(int));
        table.Columns.Add("EventId",         typeof(string));
        table.Columns.Add("SequenceNumber",  typeof(long));
        table.Columns.Add("TimestampUtc",    typeof(DateTime));
        table.Columns.Add("EventType",       typeof(string));
        table.Columns.Add("Category",        typeof(string));
        table.Columns.Add("Actor",           typeof(string));
        table.Columns.Add("Target",          typeof(string));
        table.Columns.Add("Metrics",         typeof(string)); // dynamic column — JSON string
        table.Columns.Add("Context",         typeof(string)); // dynamic column — JSON string
        table.Columns.Add("Payload",         typeof(string)); // dynamic column — JSON string

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.SessionId,
                row.UserId,
                row.SimId,
                row.SimVersion,
                row.ScenarioId,
                row.BatchId,
                row.BatchSequence,
                row.EventId,
                row.SequenceNumber,
                row.TimestampUtc,
                row.EventType,
                row.Category,
                row.Actor,
                row.Target,
                row.Metrics.GetRawText(),
                row.Context.GetRawText(),
                row.Payload.GetRawText()
            );
        }

        return table;
    }

    private static DataTable BuildFeatureWindowsTable(FeatureWindowRow row)
    {
        var table = new DataTable("FeatureWindows");
        table.Columns.Add("SessionId",               typeof(string));
        table.Columns.Add("UserId",                  typeof(string));
        table.Columns.Add("WindowIndex",             typeof(int));
        table.Columns.Add("WindowType",              typeof(string));
        table.Columns.Add("WindowStartSequence",     typeof(long));
        table.Columns.Add("WindowEndSequence",       typeof(long));
        table.Columns.Add("WindowStartUtc",          typeof(DateTime));
        table.Columns.Add("WindowEndUtc",            typeof(DateTime));
        table.Columns.Add("SimId",                   typeof(string));
        table.Columns.Add("ScenarioId",              typeof(string));
        table.Columns.Add("FeatureExtractorVersion", typeof(string));
        table.Columns.Add("Features",                typeof(string)); // dynamic column — JSON string

        table.Rows.Add(
            row.SessionId,
            row.UserId,
            row.WindowIndex,
            row.WindowType,
            row.WindowStartSequence,
            row.WindowEndSequence,
            row.WindowStartUtc,
            row.WindowEndUtc,
            row.SimId,
            row.ScenarioId,
            row.FeatureExtractorVersion,
            row.Features.GetRawText()
        );

        return table;
    }

    private static DataTable BuildBehaviorProfilesTable(BehaviorProfileRow row)
    {
        var table = new DataTable("BehaviorProfiles");
        table.Columns.Add("SessionId",             typeof(string));
        table.Columns.Add("UserId",                typeof(string));
        table.Columns.Add("WindowIndex",           typeof(int));
        table.Columns.Add("SourceFeatureWindowId", typeof(string));
        table.Columns.Add("InterpreterVersion",    typeof(string));
        table.Columns.Add("DimensionScores",       typeof(string)); // dynamic column — JSON string
        table.Columns.Add("BehaviorScores",        typeof(string)); // dynamic column — JSON string
        table.Columns.Add("CreatedAtUtc",          typeof(DateTime));

        table.Rows.Add(
            row.SessionId,
            row.UserId,
            row.WindowIndex,
            row.SourceFeatureWindowId,
            row.InterpreterVersion,
            row.DimensionScores.GetRawText(),
            row.BehaviorScores.GetRawText(),
            row.CreatedAtUtc
        );

        return table;
    }

    private static DataTable BuildHypothesisSetsTable(HypothesisSetRow row)
    {
        var table = new DataTable("HypothesisSets");
        table.Columns.Add("SessionId",               typeof(string));
        table.Columns.Add("UserId",                  typeof(string));
        table.Columns.Add("WindowIndex",             typeof(int));
        table.Columns.Add("SourceBehaviorProfileId", typeof(string));
        table.Columns.Add("InterpreterMode",         typeof(string));
        table.Columns.Add("InterpreterVersion",      typeof(string));
        table.Columns.Add("Hypotheses",              typeof(string)); // dynamic column — JSON string
        table.Columns.Add("CreatedAtUtc",            typeof(DateTime));

        table.Rows.Add(
            row.SessionId,
            row.UserId,
            row.WindowIndex,
            row.SourceBehaviorProfileId,
            row.InterpreterMode,
            row.InterpreterVersion,
            row.Hypotheses.GetRawText(),
            row.CreatedAtUtc
        );

        return table;
    }

    private static DataTable BuildAdaptationDecisionsTable(AdaptationDecisionRow row)
    {
        var table = new DataTable("AdaptationDecisions_v2");
        table.Columns.Add("SessionId",                            typeof(string));
        table.Columns.Add("UserId",                               typeof(string));
        table.Columns.Add("AdaptationDecisionId",                 typeof(string));
        table.Columns.Add("DecisionIndex",                        typeof(int));
        table.Columns.Add("SourceHypothesisSetId",                typeof(string));
        table.Columns.Add("PolicyVersion",                        typeof(string));
        table.Columns.Add("InterventionFamilies",                 typeof(string)); // dynamic column — JSON string
        table.Columns.Add("ParameterChanges",                     typeof(string)); // dynamic column — JSON string
        table.Columns.Add("ReasoningSummary",                     typeof(string));
        table.Columns.Add("ExpiresAfterWindow",                   typeof(int));
        table.Columns.Add("CreatedAtUtc",                         typeof(DateTime));
        table.Columns.Add("HintDependenceNextProbability",        typeof(double));
        table.Columns.Add("NextHintDependenceGuardrailTriggered", typeof(bool));
        table.Columns.Add("NextHintDependenceGuardrailLevel",     typeof(string));
        table.Columns.Add("Phase8AGuardrailNotes",                typeof(string));
        table.Columns.Add("Phase8EReliabilityNotes",              typeof(string));
        table.Columns.Add("Phase8EReliabilityMode",               typeof(string));

        table.Rows.Add(
            row.SessionId,
            row.UserId,
            row.AdaptationDecisionId,
            row.DecisionIndex,
            row.SourceHypothesisSetId,
            row.PolicyVersion,
            row.InterventionFamilies.GetRawText(),
            row.ParameterChanges.GetRawText(),
            row.ReasoningSummary,
            row.ExpiresAfterWindow,
            row.CreatedAtUtc,
            row.HintDependenceNextProbability ?? (object)DBNull.Value,
            row.NextHintDependenceGuardrailTriggered,
            row.NextHintDependenceGuardrailLevel ?? (object)DBNull.Value,
            row.Phase8AGuardrailNotes ?? (object)DBNull.Value,
            row.Phase8EReliabilityNotes ?? (object)DBNull.Value,
            row.Phase8EReliabilityMode ?? (object)DBNull.Value
        );

        return table;
    }

    private static DataTable BuildBehaviorStateTrainingRowsTable(BehaviorStateTrainingRow row)
    {
        var table = new DataTable("BehaviorStateTrainingRows");

        // Identity / metadata
        table.Columns.Add("SessionId",                        typeof(string));
        table.Columns.Add("UserId",                           typeof(string));
        table.Columns.Add("SimId",                            typeof(string));
        table.Columns.Add("ScenarioId",                       typeof(string));
        table.Columns.Add("WindowIndex",                      typeof(int));
        table.Columns.Add("TaskType",                         typeof(string));
        table.Columns.Add("CreatedAtUtc",                     typeof(DateTime));

        // Dimension scores
        table.Columns.Add("AttentionDetection",               typeof(double));
        table.Columns.Add("GoalUnderstanding",                typeof(double));
        table.Columns.Add("ProcedureSequencing",              typeof(double));
        table.Columns.Add("PaceRegulation",                   typeof(double));
        table.Columns.Add("SelfCorrection",                   typeof(double));
        table.Columns.Add("FeedbackResponsiveness",           typeof(double));
        table.Columns.Add("SafetyCompliance",                 typeof(double));
        table.Columns.Add("TaskContinuity",                   typeof(double));

        // Higher-order behavior scores
        table.Columns.Add("ConfusionScore",                   typeof(double));
        table.Columns.Add("HesitationScore",                  typeof(double));
        table.Columns.Add("ImpulsivityScore",                 typeof(double));
        table.Columns.Add("HintDependenceScore",              typeof(double));

        // Trajectory
        table.Columns.Add("GoalTrend",                        typeof(double));
        table.Columns.Add("AttentionTrend",                   typeof(double));
        table.Columns.Add("ConfusionTrend",                   typeof(double));
        table.Columns.Add("HintDependenceTrend",              typeof(double));

        // Adaptive state
        table.Columns.Add("CurrentHintMode",                  typeof(string));
        table.Columns.Add("CurrentDifficulty",                typeof(double));
        table.Columns.Add("CurrentTimePressure",              typeof(double));
        table.Columns.Add("CurrentErrorTolerance",            typeof(double));

        // Counters
        table.Columns.Add("ConsecutiveStableMasteryWindows",  typeof(int));
        table.Columns.Add("ConsecutiveRelapseWindows",        typeof(int));

        // Window summary
        table.Columns.Add("EventCountInWindow",               typeof(int));
        table.Columns.Add("ErrorCountInWindow",               typeof(int));
        table.Columns.Add("HintCountInWindow",                typeof(int));
        table.Columns.Add("StepCompleteCountInWindow",        typeof(int));

        // Weak labels
        table.Columns.Add("LabelConfusion",                   typeof(int));
        table.Columns.Add("LabelHintDependence",              typeof(int));
        table.Columns.Add("LabelStableMastery",               typeof(int));

        // Shadow prediction
        table.Columns.Add("PredConfusionProbability",         typeof(double));
        table.Columns.Add("PredHintDependenceProbability",    typeof(double));
        table.Columns.Add("PredStableMasteryProbability",     typeof(double));
        table.Columns.Add("ModelVersion",                     typeof(string));
        table.Columns.Add("InferenceMode",                    typeof(string));

        // Phase 7A — confusion shadow prediction detail
        table.Columns.Add("PredictedConfusionRisk",           typeof(string));
        table.Columns.Add("ConfusionModelVersion",            typeof(string));
        table.Columns.Add("ConfusionPredictionUtc",           typeof(DateTime));

        // Phase 7B — stable mastery shadow prediction detail
        table.Columns.Add("PredictedStableMasteryState",      typeof(string));
        table.Columns.Add("StableMasteryModelVersion",        typeof(string));
        table.Columns.Add("StableMasteryPredictionUtc",       typeof(DateTime));

        // Phase 7C — multi-signal guardrail summary
        table.Columns.Add("GuardrailOverallBehaviorState",        typeof(string));
        table.Columns.Add("GuardrailConfusionSignalAgreement",     typeof(bool));
        table.Columns.Add("GuardrailStableMasterySignalAgreement", typeof(bool));
        table.Columns.Add("GuardrailHintDependenceRiskLevel",      typeof(string));
        table.Columns.Add("GuardrailConflictCount",                typeof(int));
        table.Columns.Add("GuardrailWarnings",                     typeof(string));

        table.Rows.Add(
            row.SessionId,
            row.UserId,
            row.SimId,
            row.ScenarioId,
            row.WindowIndex,
            row.TaskType,
            row.CreatedAtUtc,
            row.AttentionDetection,
            row.GoalUnderstanding,
            row.ProcedureSequencing,
            row.PaceRegulation,
            row.SelfCorrection,
            row.FeedbackResponsiveness,
            row.SafetyCompliance,
            row.TaskContinuity,
            row.ConfusionScore,
            row.HesitationScore,
            row.ImpulsivityScore,
            row.HintDependenceScore,
            row.GoalTrend,
            row.AttentionTrend,
            row.ConfusionTrend,
            row.HintDependenceTrend,
            row.CurrentHintMode,
            row.CurrentDifficulty,
            row.CurrentTimePressure,
            row.CurrentErrorTolerance,
            row.ConsecutiveStableMasteryWindows,
            row.ConsecutiveRelapseWindows,
            row.EventCountInWindow,
            row.ErrorCountInWindow,
            row.HintCountInWindow,
            row.StepCompleteCountInWindow,
            row.LabelConfusion,
            row.LabelHintDependence,
            row.LabelStableMastery,
            row.PredConfusionProbability,
            row.PredHintDependenceProbability,
            row.PredStableMasteryProbability,
            row.ModelVersion,
            row.InferenceMode,
            row.PredictedConfusionRisk,
            row.ConfusionModelVersion,
            row.ConfusionPredictionUtc ?? (object)DBNull.Value,
            row.PredictedStableMasteryState,
            row.StableMasteryModelVersion,
            row.StableMasteryPredictionUtc ?? (object)DBNull.Value,
            row.GuardrailOverallBehaviorState,
            row.GuardrailConfusionSignalAgreement,
            row.GuardrailStableMasterySignalAgreement,
            row.GuardrailHintDependenceRiskLevel,
            row.GuardrailConflictCount,
            row.GuardrailWarnings
        );

        return table;
    }

    private static DataTable BuildUserBehaviorProfilesTable(UserBehaviorProfileRow row)
    {
        var table = new DataTable("UserBehaviorProfiles");
        table.Columns.Add("UserId",                                          typeof(string));
        table.Columns.Add("CreatedAtUtc",                                    typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc",                                    typeof(DateTime));
        table.Columns.Add("LastSessionId",                                   typeof(string));
        table.Columns.Add("TotalSessions",                                   typeof(int));
        table.Columns.Add("TotalWindows",                                    typeof(int));
        table.Columns.Add("AvgConfusionScore",                               typeof(double));
        table.Columns.Add("AvgHintDependenceScore",                          typeof(double));
        table.Columns.Add("AvgHesitationScore",                              typeof(double));
        table.Columns.Add("AvgGoalUnderstanding",                            typeof(double));
        table.Columns.Add("AvgAttentionDetection",                           typeof(double));
        table.Columns.Add("AvgProcedureSequencing",                          typeof(double));
        table.Columns.Add("AvgSelfCorrection",                               typeof(double));
        table.Columns.Add("AvgTaskContinuity",                               typeof(double));
        table.Columns.Add("StableMasteryRate",                               typeof(double));
        table.Columns.Add("ConfusionRate",                                   typeof(double));
        table.Columns.Add("HintDependenceRate",                              typeof(double));
        table.Columns.Add("ConfusionBlockDifficultyIncreaseThreshold",       typeof(double));
        table.Columns.Add("HintFadeConfusionDeltaThreshold",                 typeof(double));
        table.Columns.Add("HintFadeHintDependenceDeltaThreshold",            typeof(double));
        table.Columns.Add("HintFadeMaxCurrentConfusionScore",                typeof(double));
        table.Columns.Add("HintFadeMinCurrentGoalUnderstanding",             typeof(double));
        table.Columns.Add("DifficultyAccelerationConfusionDeltaThreshold",   typeof(double));
        table.Columns.Add("DifficultyAccelerationHintDependenceDeltaThreshold", typeof(double));
        table.Columns.Add("DifficultyAccelerationMaxCurrentConfusionScore",  typeof(double));
        table.Columns.Add("DifficultyAccelerationMinCurrentGoalUnderstanding", typeof(double));
        table.Columns.Add("DifficultyAccelerationDelta",                     typeof(double));

        table.Rows.Add(
            row.UserId,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.LastSessionId,
            row.TotalSessions,
            row.TotalWindows,
            row.AvgConfusionScore,
            row.AvgHintDependenceScore,
            row.AvgHesitationScore,
            row.AvgGoalUnderstanding,
            row.AvgAttentionDetection,
            row.AvgProcedureSequencing,
            row.AvgSelfCorrection,
            row.AvgTaskContinuity,
            row.StableMasteryRate,
            row.ConfusionRate,
            row.HintDependenceRate,
            row.ConfusionBlockDifficultyIncreaseThreshold       ?? (object)DBNull.Value,
            row.HintFadeConfusionDeltaThreshold                 ?? (object)DBNull.Value,
            row.HintFadeHintDependenceDeltaThreshold            ?? (object)DBNull.Value,
            row.HintFadeMaxCurrentConfusionScore                ?? (object)DBNull.Value,
            row.HintFadeMinCurrentGoalUnderstanding             ?? (object)DBNull.Value,
            row.DifficultyAccelerationConfusionDeltaThreshold   ?? (object)DBNull.Value,
            row.DifficultyAccelerationHintDependenceDeltaThreshold ?? (object)DBNull.Value,
            row.DifficultyAccelerationMaxCurrentConfusionScore  ?? (object)DBNull.Value,
            row.DifficultyAccelerationMinCurrentGoalUnderstanding ?? (object)DBNull.Value,
            row.DifficultyAccelerationDelta                     ?? (object)DBNull.Value
        );

        return table;
    }

    private static DataTable BuildUserBehaviorProfileUpdatesTable(UserBehaviorProfileUpdateRow row)
    {
        var table = new DataTable("UserBehaviorProfileUpdates");
        table.Columns.Add("UserId",                   typeof(string));
        table.Columns.Add("SessionId",                typeof(string));
        table.Columns.Add("WindowIndex",              typeof(int));
        table.Columns.Add("CreatedAtUtc",             typeof(DateTime));
        table.Columns.Add("ConfusionScore",           typeof(double));
        table.Columns.Add("HintDependenceScore",      typeof(double));
        table.Columns.Add("HesitationScore",          typeof(double));
        table.Columns.Add("GoalUnderstanding",        typeof(double));
        table.Columns.Add("AttentionDetection",       typeof(double));
        table.Columns.Add("ProcedureSequencing",      typeof(double));
        table.Columns.Add("SelfCorrection",           typeof(double));
        table.Columns.Add("TaskContinuity",           typeof(double));
        table.Columns.Add("HasStableMastery",         typeof(bool));
        table.Columns.Add("HasConfusionPattern",      typeof(bool));
        table.Columns.Add("HasHintDependencePattern", typeof(bool));

        table.Rows.Add(
            row.UserId,
            row.SessionId,
            row.WindowIndex,
            row.CreatedAtUtc,
            row.ConfusionScore,
            row.HintDependenceScore,
            row.HesitationScore,
            row.GoalUnderstanding,
            row.AttentionDetection,
            row.ProcedureSequencing,
            row.SelfCorrection,
            row.TaskContinuity,
            row.HasStableMastery,
            row.HasConfusionPattern,
            row.HasHintDependencePattern
        );

        return table;
    }

    private static DataTable BuildAdaptationEffectivenessTable(AdaptationEffectivenessRow row)
    {
        var table = new DataTable("AdaptationEffectiveness");
        table.Columns.Add("SessionId",               typeof(string));
        table.Columns.Add("UserId",                  typeof(string));
        table.Columns.Add("AdaptationDecisionId",    typeof(string));
        table.Columns.Add("AdaptationDecisionIndex", typeof(int));
        table.Columns.Add("InterventionFamilies",    typeof(string)); // dynamic — JSON array
        table.Columns.Add("ParameterChanges",        typeof(string)); // dynamic — JSON array
        table.Columns.Add("AppliedAtWindowIndex",    typeof(int));
        table.Columns.Add("EvaluatedAtWindowIndex",  typeof(int));
        table.Columns.Add("PreConfusionScore",       typeof(double));
        table.Columns.Add("PreHintDependenceScore",  typeof(double));
        table.Columns.Add("PreGoalUnderstanding",    typeof(double));
        table.Columns.Add("NextConfusionScore",      typeof(double));
        table.Columns.Add("NextHintDependenceScore", typeof(double));
        table.Columns.Add("NextGoalUnderstanding",   typeof(double));
        table.Columns.Add("ConfusionDelta",          typeof(double));
        table.Columns.Add("HintDependenceDelta",     typeof(double));
        table.Columns.Add("GoalUnderstandingDelta",  typeof(double));
        table.Columns.Add("Outcome",                 typeof(string));
        table.Columns.Add("CreatedAtUtc",            typeof(DateTime));

        table.Rows.Add(
            row.SessionId,
            row.UserId,
            row.AdaptationDecisionId,
            row.AdaptationDecisionIndex,
            row.InterventionFamilies.GetRawText(),
            row.ParameterChanges.GetRawText(),
            row.AppliedAtWindowIndex,
            row.EvaluatedAtWindowIndex,
            row.PreConfusionScore,
            row.PreHintDependenceScore,
            row.PreGoalUnderstanding,
            row.NextConfusionScore,
            row.NextHintDependenceScore,
            row.NextGoalUnderstanding,
            row.ConfusionDelta,
            row.HintDependenceDelta,
            row.GoalUnderstandingDelta,
            row.Outcome,
            row.CreatedAtUtc
        );

        return table;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private bool CheckClient(string tableName)
    {
        if (_ingestClient is not null) return true;
        _logger.LogWarning("ADX ingest client not configured. Skipping ingest to {Table}.", tableName);
        return false;
    }
}
