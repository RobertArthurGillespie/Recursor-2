using System.Globalization;
using System.Text;
using System.Text.Json;
using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxModelEvaluationQueryService
{
    /// <summary>
    /// Computes aggregate evaluation metrics for the specified shadow model
    /// using rows in BehaviorStateTrainingRows that match the request filters.
    /// Returns an empty summary when ADX is not configured or returns no rows.
    /// </summary>
    Task<ModelEvaluationSummary> GetEvaluationSummaryAsync(ModelEvaluationRequest request);

    /// <summary>
    /// Returns the top disagreement rows for the specified shadow model —
    /// rows where the model's prediction most strongly contradicts the label.
    /// Returns an empty list when ADX is not configured or returns no rows.
    /// </summary>
    Task<List<ModelDisagreementRow>> GetDisagreementRowsAsync(ModelEvaluationRequest request);
}

public class AdxModelEvaluationQueryService : IAdxModelEvaluationQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxModelEvaluationQueryService> _logger;

    public AdxModelEvaluationQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxModelEvaluationQueryService> logger)
    {
        // GetService returns null when ADX is not configured (no ClusterUri).
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
        _logger = logger;
    }

    // ── Public methods ────────────────────────────────────────────────────────

    public async Task<ModelEvaluationSummary> GetEvaluationSummaryAsync(ModelEvaluationRequest request)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — skipping evaluation summary query.");
            return BuildEmptySummary(request);
        }

        var (labelCol, predCol) = ResolveColumns(request.Model);
        var filters = BuildFilterClause(request.SimId, request.StartUtc, request.EndUtc);
        var threshold = request.Threshold.ToString("F6", CultureInfo.InvariantCulture);

        var kql = $@"BehaviorStateTrainingRows
{filters}| extend _label = {labelCol}, _pred = {predCol}
| summarize
    TotalCount      = count(),
    PositiveCount   = countif(_label == 1),
    NegativeCount   = countif(_label == 0),
    AvgPredPositive = avgif(_pred, _label == 1),
    AvgPredNegative = avgif(_pred, _label == 0),
    MinPred         = min(_pred),
    MaxPred         = max(_pred),
    P50             = percentile(_pred, 50),
    P75             = percentile(_pred, 75),
    P90             = percentile(_pred, 90),
    TP              = countif(_label == 1 and _pred >= {threshold}),
    FP              = countif(_label == 0 and _pred >= {threshold}),
    TN              = countif(_label == 0 and _pred < {threshold}),
    FN              = countif(_label == 1 and _pred < {threshold}),
    ModelVersions   = make_set(ModelVersion),
    InferenceModes  = make_set(InferenceMode)";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

            if (!reader.Read())
                return BuildEmptySummary(request);

            var tp = reader.GetInt64(10);
            var fp = reader.GetInt64(11);
            var tn = reader.GetInt64(12);
            var fn = reader.GetInt64(13);

            var summary = new ModelEvaluationSummary
            {
                Model        = request.Model,
                SimIdFilter  = request.SimId,
                StartUtc     = request.StartUtc,
                EndUtc       = request.EndUtc,
                Threshold    = request.Threshold,
                TotalRowCount    = reader.GetInt64(0),
                PositiveLabelCount = reader.GetInt64(1),
                NegativeLabelCount = reader.GetInt64(2),
                AvgPredWhenPositive = ReadDoubleSafe(reader, 3),
                AvgPredWhenNegative = ReadDoubleSafe(reader, 4),
                MinPred = ReadDoubleSafe(reader, 5),
                MaxPred = ReadDoubleSafe(reader, 6),
                P50     = ReadDoubleSafe(reader, 7),
                P75     = ReadDoubleSafe(reader, 8),
                P90     = ReadDoubleSafe(reader, 9),
                TP = tp,
                FP = fp,
                TN = tn,
                FN = fn,
                Precision    = SafeDivide(tp, tp + fp),
                Recall       = SafeDivide(tp, tp + fn),
                Specificity  = SafeDivide(tn, tn + fp),
                ModelVersionsSeen = ParseStringSet(ReadDynamicJsonSafe(reader, 14)),
                InferenceModesSeen = ParseStringSet(ReadDynamicJsonSafe(reader, 15))
            };

            summary.F1 = SafeDivide(
                2.0 * summary.Precision * summary.Recall,
                summary.Precision + summary.Recall);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADX evaluation summary query failed — returning empty result.");
            return BuildEmptySummary(request);
        }
    }

    public async Task<List<ModelDisagreementRow>> GetDisagreementRowsAsync(ModelEvaluationRequest request)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — skipping disagreement query.");
            return [];
        }

        var (labelCol, predCol) = ResolveColumns(request.Model);
        var filters = BuildFilterClause(request.SimId, request.StartUtc, request.EndUtc);
        var threshold = request.Threshold.ToString("F6", CultureInfo.InvariantCulture);
        var limit = request.DisagreementLimit;

        // Fetch rows where label and prediction disagree at the threshold,
        // ordered by the strongest contradictory confidence first.
        var kql = $@"BehaviorStateTrainingRows
{filters}| extend _label = {labelCol}, _pred = {predCol}
| where (_label == 0 and _pred >= {threshold}) or (_label == 1 and _pred < {threshold})
| extend _disagreement = abs(_pred - todouble(_label))
| order by _disagreement desc
| take {limit}
| project SessionId, SimId, ScenarioId, WindowIndex, TaskType, CreatedAtUtc,
          LabelValue = _label, PredProb = _pred,
          CurrentHintMode, CurrentDifficulty, CurrentTimePressure, CurrentErrorTolerance,
          ConfusionScore, HintDependenceScore,
          GoalTrend, AttentionTrend, ConfusionTrend, HintDependenceTrend";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

            var rows = new List<ModelDisagreementRow>();
            while (reader.Read())
            {
                var labelValue = reader.GetInt32(6);
                var predProb   = ReadDoubleSafe(reader, 7);

                rows.Add(new ModelDisagreementRow
                {
                    SessionId             = reader.GetString(0),
                    SimId                 = reader.GetString(1),
                    ScenarioId            = reader.GetString(2),
                    WindowIndex           = reader.GetInt32(3),
                    TaskType              = reader.GetString(4),
                    CreatedAtUtc          = reader.GetDateTime(5),
                    LabelValue            = labelValue,
                    PredictedProbability  = predProb,
                    CurrentHintMode       = reader.GetString(8),
                    CurrentDifficulty     = ReadDoubleSafe(reader, 9),
                    CurrentTimePressure   = ReadDoubleSafe(reader, 10),
                    CurrentErrorTolerance = ReadDoubleSafe(reader, 11),
                    ConfusionScore        = ReadDoubleSafe(reader, 12),
                    HintDependenceScore   = ReadDoubleSafe(reader, 13),
                    GoalTrend             = ReadDoubleSafe(reader, 14),
                    AttentionTrend        = ReadDoubleSafe(reader, 15),
                    ConfusionTrend        = ReadDoubleSafe(reader, 16),
                    HintDependenceTrend   = ReadDoubleSafe(reader, 17),
                    DisagreementClass     = ClassifyDisagreement(labelValue, predProb, request.Threshold)
                });
            }

            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADX disagreement query failed — returning empty list.");
            return [];
        }
    }

    // ── Column mapping ────────────────────────────────────────────────────────

    // Maps ShadowModelType constant to the ADX label/prediction column pair.
    // These are hardcoded to static column names — no injection risk.
    private static (string labelCol, string predCol) ResolveColumns(string model) =>
        model switch
        {
            ShadowModelType.Confusion      => ("LabelConfusion",      "PredConfusionProbability"),
            ShadowModelType.HintDependence => ("LabelHintDependence", "PredHintDependenceProbability"),
            ShadowModelType.StableMastery  => ("LabelStableMastery",  "PredStableMasteryProbability"),
            _ => throw new ArgumentException($"Unknown model type: {model}")
        };

    // ── Filter construction ───────────────────────────────────────────────────

    // Builds the KQL where-clause lines for optional filters.
    // All values are sanitized or formatted as literals — not interpolated raw.
    private static string BuildFilterClause(string? simId, DateTime? startUtc, DateTime? endUtc)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(simId))
            sb.AppendLine($"| where SimId == '{SanitizeStringFilter(simId)}'");

        if (startUtc.HasValue)
            sb.AppendLine($"| where CreatedAtUtc >= datetime({startUtc.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ})");

        if (endUtc.HasValue)
            sb.AppendLine($"| where CreatedAtUtc <= datetime({endUtc.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ})");

        return sb.ToString();
    }

    // Strips single quotes to prevent KQL injection from string filter values.
    // Consistent with the approach used in AdxRecursorQueryService.
    private static string SanitizeStringFilter(string value) =>
        value.Replace("'", "");

    // ── Disagreement classification ───────────────────────────────────────────

    private static string ClassifyDisagreement(int label, double pred, double threshold)
    {
        if (label == 0 && pred >= threshold)
            return "false_positive_like";

        if (label == 1 && pred < threshold)
            return pred < threshold * 0.5
                ? "low_prob_labeled"
                : "false_negative_like";

        // label outside {0,1} with a high prediction (future unlabeled rows)
        return "high_prob_unlabeled";
    }

    // ── Reader helpers ────────────────────────────────────────────────────────

    // Returns 0.0 when the ADX column is null (e.g. avgif with no matching rows).
    private static double ReadDoubleSafe(System.Data.IDataReader reader, int index) =>
        reader.IsDBNull(index) ? 0.0 : reader.GetDouble(index);

    // Reads an ADX dynamic column (e.g. make_set output) safely.
    // GetString() throws on dynamic columns in the Kusto SDK — use GetValue() instead,
    // which returns the underlying object whose ToString() produces the JSON representation.
    private static string ReadDynamicJsonSafe(System.Data.IDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
            return "[]";

        var value = reader.GetValue(index);
        return value?.ToString() ?? "[]";
    }

    // Parses a KQL make_set JSON array into a list of strings.
    private static List<string> ParseStringSet(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ── Metrics helpers ───────────────────────────────────────────────────────

    private static double SafeDivide(double numerator, double denominator) =>
        denominator == 0.0 ? 0.0 : numerator / denominator;

    private static double SafeDivide(long numerator, long denominator) =>
        denominator == 0 ? 0.0 : (double)numerator / denominator;

    // ── Empty result construction ─────────────────────────────────────────────

    private static ModelEvaluationSummary BuildEmptySummary(ModelEvaluationRequest request) =>
        new()
        {
            Model       = request.Model,
            SimIdFilter = request.SimId,
            StartUtc    = request.StartUtc,
            EndUtc      = request.EndUtc,
            Threshold   = request.Threshold
        };
}
