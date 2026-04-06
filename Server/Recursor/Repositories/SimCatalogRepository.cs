using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

public interface ISimCatalogRepository
{
    SimCatalogDocument? Get(string simId);
    IEnumerable<SimCatalogDocument> GetAll();
}

public class SimCatalogRepository : ISimCatalogRepository
{
    private readonly Dictionary<string, SimCatalogDocument> _catalog;

    public SimCatalogRepository()
    {
        _catalog = new Dictionary<string, SimCatalogDocument>
        {
            ["sim-training-v1"] = new SimCatalogDocument
            {
                Id = "sim-training-v1",
                DocumentType = "SimCatalog",
                SimId = "sim-training-v1",
                SupportedDimensions =
                [
                    "attentionDetection",
                    "goalUnderstanding",
                    "procedureSequencing",
                    "paceRegulation",
                    "selfCorrection",
                    "feedbackResponsiveness",
                    "safetyCompliance",
                    "taskContinuity"
                ],
                EventTypes =
                [
                    "action",
                    "error",
                    "hint_request",
                    "safety_violation",
                    "task_complete",
                    "step_complete"
                ],
                AdaptiveParameters =
                [
                    new AdaptiveParameterDefinition
                    {
                        Name = "difficulty",
                        Type = "float",
                        Min  = 0.0,
                        Max  = 1.0
                    },
                    new AdaptiveParameterDefinition
                    {
                        Name          = "hintMode",
                        Type          = "enum",
                        AllowedValues = ["off", "minimal", "guided"]
                    },
                    new AdaptiveParameterDefinition
                    {
                        Name = "timePressure",
                        Type = "float",
                        Min  = 0.0,
                        Max  = 1.0
                    },
                    new AdaptiveParameterDefinition
                    {
                        Name = "errorTolerance",
                        Type = "int",
                        Min  = 0,
                        Max  = 5
                    },
                    new AdaptiveParameterDefinition
                    {
                        Name = "distractorDensity",
                        Type = "float",
                        Min  = 0.0,
                        Max  = 1.0
                    }
                ]
            },
            ["sim-sequence-training-v1"] = new SimCatalogDocument
            {
                Id = "sim-sequence-training-v1",
                DocumentType = "SimCatalog",
                SimId = "sim-sequence-training-v1",

                SupportedDimensions =
        [
            "attentionDetection",
            "goalUnderstanding",
            "procedureSequencing",
            "paceRegulation",
            "selfCorrection",
            "feedbackResponsiveness",
            "safetyCompliance",
            "taskContinuity"
        ],

                EventTypes =
        [
            "action",
            "error",
            "hint_request",
            "task_complete",
            "step_error",
            "step_complete"
        ],

                AdaptiveParameters =
        [
            new AdaptiveParameterDefinition
            {
                Name = "difficulty",
                Type = "float",
                Min  = 0.0,
                Max  = 1.0
            },
            new AdaptiveParameterDefinition
            {
                Name = "hintMode",
                Type = "enum",
                AllowedValues = ["off", "minimal", "guided"]
            },
            new AdaptiveParameterDefinition
            {
                Name = "timePressure",
                Type = "float",
                Min  = 0.0,
                Max  = 1.0
            },
            new AdaptiveParameterDefinition
            {
                Name = "errorTolerance",
                Type = "int",
                Min  = 0,
                Max  = 5
            }
        ]
            }
        };
    }

    public SimCatalogDocument? Get(string simId)
        => _catalog.TryGetValue(simId, out var doc) ? doc : null;

    public IEnumerable<SimCatalogDocument> GetAll()
        => _catalog.Values;
}
