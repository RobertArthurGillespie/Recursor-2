using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Azure.AI.OpenAI;
using Azure.AI.Inference;
using Azure.Search.Documents;
using OpenAI.Embeddings;
using OpenAI.Chat;
using Azure.Search.Documents.Models;
using System.Net;
using NCATAIBlazorFrontendTest.Shared;
using Azure;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text;
using System;

using System.Net.Http;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Policy;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Azure.Core;





namespace NCATAIBlazorFrontendTest.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]

    public class ChatController : ControllerBase
    {
        //private readonly EmbeddingClient _embeddingClient; // From Azure.AI.OpenAI
        //private readonly SearchClient _searchClient;       // From Azure.AI.Search.Documents
        //private readonly ChatCompletionsClient _chatCompletionsClient; // From Azure.AI.OpenAI
        //AzureOpenAIClient _openAIClient;

        private readonly ILogger<ChatController> _logger;
        private static readonly HttpClient _httpClient = new();
        private string logString = string.Empty;

        // Constructor for Dependency Injection (recommended)
        public ChatController(
            //EmbeddingClient embeddingClient,
            //SearchClient searchClient,
            //ChatCompletionsClient chatCompletionsClient, // You'll need to deploy a chat model (e.g., gpt-3.5-turbo)
            ILogger<ChatController> logger)
        {
            //_embeddingClient = embeddingClient;
            //_searchClient = searchClient;
            //_chatCompletionsClient = chatCompletionsClient;
            _logger = logger;
        }

        [HttpGet("TestChatController")]
        public async Task<IActionResult> TestChatController()
        {
            return Content("chat controller working");
        }

        [HttpPost("AIChat")]
        public async Task<IActionResult> AIChat([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("Message cannot be empty.");
            }
            var deploymentName = "text-embedding-ada-002";
            var credential = new AzureKeyCredential("84b28L6umwahMReEZA4cYIOReH92mLLe7mAk55zTYjrokQO1duX4JQQJ99BGACYeBjFXJ3w3AAABACOGbVNc");
            var openAIClient = new AzureOpenAIClient(new Uri("https://ncatopenai.openai.azure.com/"), credential);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("api-key", "IChtPwfoeucX1bqRLsTm1407bpbm43kIlKM88EB2zjAzSeBspIbJ");
            
            string searchContext = request.Message;
            var requestData = new
            {
                search = searchContext, // Search using the actual user message
                queryType = "simple", // Use semantic search for better relevance
                                      //semanticConfiguration = "default", // Adjust based on your search config
                select = "id, source, extractedText, ocrText, documentType",
                top = 5, // Limit to top 5 most relevant documents
                count = false, // Don't need total count for better performance
                searchFields = "extractedText,ocrText", // Specify which fields to search
            };

            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://ncatsearch.search.windows.net/indexes/ncat-index/docs/search?api-version=2024-07-01", content);
            string responseBody = await response.Content.ReadAsStringAsync();
            string searchResults = await response.Content.ReadAsStringAsync();

            var searchResponse = JsonSerializer.Deserialize<SearchResponse>(searchResults);
            var contextBuilder = new StringBuilder();

            if (searchResponse?.value != null && searchResponse.value.Any())
            {
                _logger.LogWarning("search returned a non-null response, adding it to context");
                foreach (var doc in searchResponse.value.Take(5)) // Limit context size
                {
                    contextBuilder.AppendLine($"Document: {doc.source}");

                    // Truncate long content to prevent token overflow
                    var content_text = !string.IsNullOrEmpty(doc.extractedText)
                        ? TruncateText(doc.extractedText, 500)
                        : TruncateText(doc.ocrText, 500);

                    contextBuilder.AppendLine($"Content: {content_text}");
                    contextBuilder.AppendLine("---");
                }
            }
            else
            {
                contextBuilder.AppendLine("No relevant documents found in the knowledge base.");
            }
            ChatClient client = openAIClient.GetChatClient("gpt-5-chat");
            var systemPrompt = $@"You are an AI assistant that answers questions based on the provided context from a knowledge base. 

INSTRUCTIONS:
- If the answer is clearly in the context, provide a helpful and accurate response
- If the context doesn't contain enough information, state that you don't have enough information in the knowledge base to answer the question
- Be concise but thorough
- Cite the document source when possible

CONTEXT:
{contextBuilder.ToString()}";
            ChatMessage[] chatMessages = new ChatMessage[]{
        new SystemChatMessage(systemPrompt),
        new UserChatMessage(request.Message),

                };
            ChatCompletion completion = client.CompleteChat(chatMessages);
            string llmResponse = $"[ASSISTANT]: {completion.Content[0].Text}";
            return Ok(new ChatResponse { Answer = llmResponse });
        }

        // Helper method to truncate text and prevent token overflow
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "... [truncated]";
        }

        public class DropboxUploadPayload
        {
            [JsonPropertyName("filePath")]
            public string FilePath { get; set; }
            [JsonPropertyName("fileName")]
            public string FileName { get; set; }
        }

        // Models for deserializing search response
        public class SearchResponse
        {
            public SearchDocument[] value { get; set; }
        }

        public class SearchDocument
        {
            public string id { get; set; }
            public string source { get; set; }
            public string extractedText { get; set; }
            public string ocrText { get; set; }
            public string documentType { get; set; }
        }
        // DTOs for request and response
        /*public class ChatRequest
        {
            public string Message { get; set; }
        }

        public class ChatResponse
        {
            public string Answer { get; set; }
            public string Context { get; set; } // Optional: to show the user what context was used
        }

        public class CustomChatMessage
        {
            public string Text { get; set; }
            public bool IsUser { get; set; }
            public string Context { get; set; }
            public bool ShowContext { get; set; } = false;
        }*/
    }
}
