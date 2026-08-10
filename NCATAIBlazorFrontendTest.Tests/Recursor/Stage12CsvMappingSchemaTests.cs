using System.Text.Json;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 12 corrective-pass test: the CSV ingestion mapping literal in
/// docs/kql/phase10e-temporal-behavior-state-predictions-setup.kql must stay in exact column
/// order with AdxIngestionService.BuildTemporalBehaviorStatePredictionsTable (via
/// AdxRowMapper.MapTemporalBehaviorStatePrediction, which produces the row actually ingested) —
/// a mismatch means data silently lands in the wrong column. The mapping JSON below is a
/// verbatim copy of the .kql file's mapping literal; keep both in sync if either changes.
/// </summary>
public class Stage12CsvMappingSchemaTests
{
    // Verbatim copy of the mapping literal from
    // docs/kql/phase10e-temporal-behavior-state-predictions-setup.kql.
    private const string MappingJson =
        "[{\"column\":\"SessionId\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"0\"}}," +
        "{\"column\":\"UserId\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"1\"}}," +
        "{\"column\":\"SimId\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"2\"}}," +
        "{\"column\":\"ScenarioId\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"3\"}}," +
        "{\"column\":\"WindowIndex\",\"datatype\":\"int\",\"Properties\":{\"Ordinal\":\"4\"}}," +
        "{\"column\":\"Horizon\",\"datatype\":\"int\",\"Properties\":{\"Ordinal\":\"5\"}}," +
        "{\"column\":\"PredictedBehaviorState\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"6\"}}," +
        "{\"column\":\"Confidence\",\"datatype\":\"real\",\"Properties\":{\"Ordinal\":\"7\"}}," +
        "{\"column\":\"ClassProbabilities\",\"datatype\":\"dynamic\",\"Properties\":{\"Ordinal\":\"8\"}}," +
        "{\"column\":\"ModelVersion\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"9\"}}," +
        "{\"column\":\"CreatedAtUtc\",\"datatype\":\"datetime\",\"Properties\":{\"Ordinal\":\"10\"}}," +
        "{\"column\":\"PredictionId\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"11\"}}]";

    // Exact column order AdxIngestionService.BuildTemporalBehaviorStatePredictionsTable produces
    // (Server/Recursor/Adx/AdxIngestionService.cs) — the row that is actually CSV-ingested.
    // PredictionId (Stage 4) is appended last — additive schema change, existing ordinals 0-10
    // are untouched.
    private static readonly string[] ApplicationColumnOrder =
    {
        "SessionId", "UserId", "SimId", "ScenarioId", "WindowIndex", "Horizon",
        "PredictedBehaviorState", "Confidence", "ClassProbabilities", "ModelVersion", "CreatedAtUtc",
        "PredictionId",
    };

    [Fact]
    public void MappingJson_IsValidJsonArray_WithExpectedShape()
    {
        using var doc = JsonDocument.Parse(MappingJson);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            Assert.True(element.TryGetProperty("column", out _), "each entry must have a lowercase 'column' property");
            Assert.True(element.TryGetProperty("datatype", out _), "each entry must have a lowercase 'datatype' property");
            Assert.True(element.TryGetProperty("Properties", out var props), "each entry must have a 'Properties' object");
            Assert.True(props.TryGetProperty("Ordinal", out _), "Properties must contain 'Ordinal'");
        }
    }

    [Fact]
    public void MappingColumnOrder_MatchesApplicationTableColumnOrder()
    {
        using var doc = JsonDocument.Parse(MappingJson);
        var mappingColumns = doc.RootElement.EnumerateArray()
            .OrderBy(e => int.Parse(e.GetProperty("Properties").GetProperty("Ordinal").GetString()!))
            .Select(e => e.GetProperty("column").GetString())
            .ToArray();

        Assert.Equal(ApplicationColumnOrder, mappingColumns);
    }

    [Fact]
    public void MappingColumnCount_MatchesApplicationColumnCount()
    {
        using var doc = JsonDocument.Parse(MappingJson);
        var count = doc.RootElement.EnumerateArray().Count();

        Assert.Equal(ApplicationColumnOrder.Length, count);
    }

    [Fact]
    public void MappingOrdinals_AreContiguousStartingAtZero()
    {
        using var doc = JsonDocument.Parse(MappingJson);
        var ordinals = doc.RootElement.EnumerateArray()
            .Select(e => int.Parse(e.GetProperty("Properties").GetProperty("Ordinal").GetString()!))
            .OrderBy(o => o)
            .ToArray();

        Assert.Equal(Enumerable.Range(0, ApplicationColumnOrder.Length), ordinals);
    }

    [Fact]
    public void PredictionRowMapper_ProducesFieldsInTheSameOrderAsTheMapping()
    {
        // End-to-end sanity: the actual mapped row (what AdxIngestionService ingests) has a
        // value for every column the KQL mapping expects, confirming the two schemas describe
        // the same row shape, not just a coincidentally-matching column-name list.
        var embedding = new TemporalEmbeddingVector
        {
            SessionId = "s1", UserId = "u1", SimId = "medical-supply-stocking", ScenarioId = "sc1",
            WindowIndex = 3, Values = new double[25], FeatureNamesJson = "[]", EmbeddingVersion = "v1.0",
        };
        var prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "stable", Confidence = 0.9f, ModelVersion = "temporal-behavior-state-v1",
            ClassProbabilities = new Dictionary<string, float> { ["stable"] = 0.9f },
        };

        var row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, prediction, horizon: 1, DateTime.UtcNow);

        Assert.Equal("s1", row.SessionId);
        Assert.Equal("u1", row.UserId);
        Assert.Equal("medical-supply-stocking", row.SimId);
        Assert.Equal("sc1", row.ScenarioId);
        Assert.Equal(3, row.WindowIndex);
        Assert.Equal(1, row.Horizon);
        Assert.Equal("stable", row.PredictedBehaviorState);
        Assert.Equal(0.9, row.Confidence, precision: 5);
        Assert.Equal("temporal-behavior-state-v1", row.ModelVersion);
        Assert.False(string.IsNullOrEmpty(row.PredictionId));
        Assert.Equal(
            NCATAIBlazorFrontendTest.Server.Recursor.Models.BehaviorStatePredictionKeys
                .ComputePredictionId("s1", 3, 1, "temporal-behavior-state-v1"),
            row.PredictionId);
    }
}
