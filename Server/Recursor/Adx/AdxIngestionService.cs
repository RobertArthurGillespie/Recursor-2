using System.Data;
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
        var props = new KustoQueuedIngestionProperties(_database, "RawEvents");
        await _ingestClient!.IngestFromDataTableAsync(table, props);
    }

    public async Task IngestFeatureWindowAsync(FeatureWindowRow row)
    {
        if (!CheckClient("FeatureWindows")) return;

        var table = BuildFeatureWindowsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "FeatureWindows");
        await _ingestClient!.IngestFromDataTableAsync(table, props);
    }

    public async Task IngestBehaviorProfileAsync(BehaviorProfileRow row)
    {
        if (!CheckClient("BehaviorProfiles")) return;

        var table = BuildBehaviorProfilesTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "BehaviorProfiles");
        await _ingestClient!.IngestFromDataTableAsync(table, props);
    }

    public async Task IngestHypothesisSetAsync(HypothesisSetRow row)
    {
        if (!CheckClient("HypothesisSets")) return;

        var table = BuildHypothesisSetsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "HypothesisSets");
        await _ingestClient!.IngestFromDataTableAsync(table, props);
    }

    public async Task IngestAdaptationDecisionAsync(AdaptationDecisionRow row)
    {
        if (!CheckClient("AdaptationDecisions")) return;

        var table = BuildAdaptationDecisionsTable(row);
        var props = new KustoQueuedIngestionProperties(_database, "AdaptationDecisions");
        await _ingestClient!.IngestFromDataTableAsync(table, props);
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
        table.Columns.Add("SessionId",              typeof(string));
        table.Columns.Add("WindowIndex",            typeof(int));
        table.Columns.Add("WindowType",             typeof(string));
        table.Columns.Add("WindowStartSequence",    typeof(long));
        table.Columns.Add("WindowEndSequence",      typeof(long));
        table.Columns.Add("WindowStartUtc",         typeof(DateTime));
        table.Columns.Add("WindowEndUtc",           typeof(DateTime));
        table.Columns.Add("SimId",                  typeof(string));
        table.Columns.Add("ScenarioId",             typeof(string));
        table.Columns.Add("FeatureExtractorVersion",typeof(string));
        table.Columns.Add("Features",               typeof(string)); // dynamic column — JSON string

        table.Rows.Add(
            row.SessionId,
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
        table.Columns.Add("SessionId",            typeof(string));
        table.Columns.Add("WindowIndex",          typeof(int));
        table.Columns.Add("SourceFeatureWindowId",typeof(string));
        table.Columns.Add("InterpreterVersion",   typeof(string));
        table.Columns.Add("DimensionScores",      typeof(string)); // dynamic column — JSON string
        table.Columns.Add("CreatedAtUtc",         typeof(DateTime));

        table.Rows.Add(
            row.SessionId,
            row.WindowIndex,
            row.SourceFeatureWindowId,
            row.InterpreterVersion,
            row.DimensionScores.GetRawText(),
            row.CreatedAtUtc
        );

        return table;
    }

    private static DataTable BuildHypothesisSetsTable(HypothesisSetRow row)
    {
        var table = new DataTable("HypothesisSets");
        table.Columns.Add("SessionId",            typeof(string));
        table.Columns.Add("WindowIndex",          typeof(int));
        table.Columns.Add("SourceBehaviorProfileId", typeof(string));
        table.Columns.Add("InterpreterMode",      typeof(string));
        table.Columns.Add("InterpreterVersion",   typeof(string));
        table.Columns.Add("Hypotheses",           typeof(string)); // dynamic column — JSON string
        table.Columns.Add("CreatedAtUtc",         typeof(DateTime));

        table.Rows.Add(
            row.SessionId,
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
        var table = new DataTable("AdaptationDecisions");
        table.Columns.Add("SessionId",            typeof(string));
        table.Columns.Add("DecisionIndex",        typeof(int));
        table.Columns.Add("SourceHypothesisSetId",typeof(string));
        table.Columns.Add("PolicyVersion",        typeof(string));
        table.Columns.Add("InterventionFamilies", typeof(string)); // dynamic column — JSON string
        table.Columns.Add("ParameterChanges",     typeof(string)); // dynamic column — JSON string
        table.Columns.Add("ReasoningSummary",     typeof(string));
        table.Columns.Add("ExpiresAfterWindow",   typeof(int));
        table.Columns.Add("CreatedAtUtc",         typeof(DateTime));

        table.Rows.Add(
            row.SessionId,
            row.DecisionIndex,
            row.SourceHypothesisSetId,
            row.PolicyVersion,
            row.InterventionFamilies.GetRawText(),
            row.ParameterChanges.GetRawText(),
            row.ReasoningSummary,
            row.ExpiresAfterWindow,
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
