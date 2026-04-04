using System.Text;
using System.Text.Json;
using Azure;
using OpenAI.Chat;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;
using System.Net.Http;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services
{
    public interface IExplanationGenerationService
    {
        Task<GptExplanationResult?> GenerateExplanationAsync(
            SessionDocument session,
            BehaviorProfileDocument behaviorProfile,
            HypothesisSetDocument hypothesisSet,
            AdaptationDecisionDocument? adaptation);
    }

    public class AzureOpenAiExplanationService : IExplanationGenerationService
    {
        private readonly ILogger<AzureOpenAiExplanationService> _logger;
        private static readonly HttpClient _httpClient = new();

        public AzureOpenAiExplanationService(ILogger<AzureOpenAiExplanationService> logger)
        {
            _logger = logger;
        }

        public async Task<GptExplanationResult?> GenerateExplanationAsync(
            SessionDocument session,
            BehaviorProfileDocument behaviorProfile,
            HypothesisSetDocument hypothesisSet,
            AdaptationDecisionDocument? adaptation)
        {
            try
            {
                var credential = new AzureKeyCredential("F8fFcrOkGNjJFbL710c19YIU6Vq1H0sP0ifcZ0bM4eAJvZwT4FxHJQQJ99BLACYeBjFXJ3w3AAABACOGhJoF");
                var openAIClient = new AzureOpenAIClient(new Uri("https://manuscriptgenerator.openai.azure.com/"), credential);
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _httpClient.DefaultRequestHeaders.Add("api-key", "F8fFcrOkGNjJFbL710c19YIU6Vq1H0sP0ifcZ0bM4eAJvZwT4FxHJQQJ99BLACYeBjFXJ3w3AAABACOGhJoF");
                _logger.LogWarning($"created client for sending chat message");

                ChatClient client = openAIClient.GetChatClient("gpt-5.4-mini");

                // Collect trajectory labels present in this window for GPT context.
                var trajectoryLabels = hypothesisSet.Hypotheses
                    .Where(h =>
                        h.Label == "stable_mastery_pattern" ||
                        h.Label == "relapse_pattern" ||
                        h.Label == "improving_pattern" ||
                        h.Label == "worsening_pattern")
                    .Select(h => h.Label)
                    .ToList();

                session.CurrentDifficultyProfile.TryGetValue("hintMode", out var currentHintMode);

                var payload = new
                {
                    session = new
                    {
                        session.SessionId,
                        session.SimId,
                        session.ScenarioId,
                        session.BatchCount,
                        currentHintMode,
                        currentDifficultyProfile = session.CurrentDifficultyProfile,
                        consecutiveStableMasteryWindows = session.ConsecutiveStableMasteryWindows,
                        consecutiveRelapseWindows = session.ConsecutiveRelapseWindows
                    },
                    dimensionScores = behaviorProfile.DimensionScores.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            kvp.Value.Score,
                            kvp.Value.Confidence,
                            kvp.Value.Evidence
                        }),
                    behaviorScores = behaviorProfile.BehaviorScores,
                    trajectoryLabels,
                    hypotheses = hypothesisSet.Hypotheses.Select(h => new
                    {
                        h.Label,
                        h.Dimensions,
                        h.Confidence,
                        h.Evidence
                    }),
                    adaptation = adaptation is null ? null : new
                    {
                        adaptation.ReasoningSummary,
                        ParameterChanges = adaptation.ParameterChanges.Select(pc => new
                        {
                            pc.Parameter,
                            pc.Operation,
                            pc.Value
                        })
                    }
                };

                string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                string systemPrompt = """
You are an explanation layer for an adaptive training engine.

You do not decide the adaptation. You only explain what the system already inferred and why support changed.

The input payload contains:
- session: current session state, hint mode, difficulty profile, and consecutive trajectory window counters
- dimensionScores: per-dimension behavioral scores for the current window
- behaviorScores: confusion, hesitation, impulsivity, and hint-dependence scores
- trajectoryLabels: trend labels detected across recent windows (may be empty)
- hypotheses: behavioral hypotheses generated for this window
- adaptation: parameter changes applied (or null if no change was made)

Return valid JSON only with exactly these fields:
- learnerStateSummary
- whySupportChanged
- coachMessage
- confidenceNote

Rules for each field:
- learnerStateSummary: summarize the learner's current performance and recent trend in one or two sentences. Distinguish between current window performance and multi-window trajectory.
- whySupportChanged: explain why support was changed, held, or left unchanged. If a transition was gradual, explain that the system requires repeated evidence before escalating or removing support.
- coachMessage: a brief, encouraging message addressed to the learner. Ground it in the actual state and trend. Do not invent behaviors.
- confidenceNote: a short note on how confident the system is based on the evidence provided.

Trajectory guidance:
- If trajectoryLabels contains "stable_mastery_pattern": explain that support reduction reflects sustained high performance across multiple recent windows, not just this one.
- If trajectoryLabels contains "relapse_pattern": explain that support restoration reflects a detected decline after prior improvement, and that the system is responding carefully.
- If trajectoryLabels contains "improving_pattern": acknowledge positive momentum across recent windows.
- If trajectoryLabels contains "worsening_pattern": acknowledge declining trend and that additional support is being considered.
- If consecutiveStableMasteryWindows is 1 and hint mode did not change toward off: explain that the system observed one stable window but is waiting for a second before reducing hints further.
- If consecutiveRelapseWindows is 1 and hint mode did not change toward guided: explain that the system observed one difficult window but is waiting for a second before adding more guidance.

Additional rules:
- Do not invent behaviors not present in the supplied data.
- Do not mention being an AI model.
- Do not use markdown.
- Keep each field concise and readable.
- If adaptation is null, explain that no support change was applied this window.
""";

                ChatMessage[] messages = new ChatMessage[]
                {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(payloadJson)
                };

                ChatCompletion completion = client.CompleteChat(messages);
                string raw = completion.Content[0].Text;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var explanation = JsonSerializer.Deserialize<GptExplanationResult>(raw, options);

                if (explanation is null)
                {
                    _logger.LogWarning("GPT explanation returned null after deserialization. Raw: {Raw}", raw);
                    return null;
                }

                // Ensure no field is null even if GPT omitted it.
                explanation.LearnerStateSummary ??= "";
                explanation.WhySupportChanged ??= "";
                explanation.CoachMessage ??= "";
                explanation.ConfidenceNote ??= "";

                return explanation;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate GPT explanation.");
                return null;
            }
        }
    }
}
