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

                var payload = new
                {
                    session = new
                    {
                        session.SessionId,
                        session.SimId,
                        session.ScenarioId,
                        session.BatchCount
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

You do not decide the adaptation.
You only explain what the system already inferred and why support changed.

Return valid JSON only with exactly these fields:
- learnerStateSummary
- whySupportChanged
- coachMessage
- confidenceNote

Rules:
- Be concise and grounded in the supplied data.
- Do not invent behaviors not present in the input.
- If adaptation is null, explain that no support change was applied.
- Do not include markdown fences.
- Do not mention being an AI model.
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
