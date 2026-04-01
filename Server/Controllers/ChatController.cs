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
            return Ok(new ChatResponse { Answer = llmResponse});
            //return Content("response body: " + llmResponse);
            /*try
            {
                // Step 1: Query Embedding
                _logger.LogInformation($"Generating embedding for user query: {request.Message}");
                var deploymentName = "text-embedding-ada-002";
                var credential = new AzureKeyCredential("61arcclb5pyx9gzxWpyD6CYnSUYbWm6gYPDEIlnnGVlkQfGObgsoJQQJ99BGACYeBjFXJ3w3AAABACOGZ8AH");
                var openAIClient = new AzureOpenAIClient(new Uri("https://ncatoai.openai.azure.com/"), credential);


                var embeddingClient = openAIClient.GetEmbeddingClient(deploymentName);
                ReadOnlyMemory<float> queryVector = new ReadOnlyMemory<float>();
                OpenAIEmbedding textEmbedding = await embeddingClient.GenerateEmbeddingAsync(request.Message);
                queryVector = textEmbedding.ToFloats();
                //EmbeddingOptions embeddingOptions = new(new List<string> { request.Message });
                //Azure.Response<Embeddings> embeddingResponse = await _embeddingClient.GenerateEmbeddingsAsync(embeddingOptions);
                //ReadOnlyMemory<float> queryVector = embeddingResponse.Value.Data[0].Vector;

                // Step 2: Retrieval (Vector Search)
                _logger.LogWarning("Performing vector search in Azure AI Search.");
                var searchOptions = new SearchOptions
                {
                    VectorSearch = new VectorSearchOptions
                    {
                        Queries = { new VectorizedQuery(queryVector.ToArray()) { KNearestNeighborsCount = 3, Fields = { "contentVector" } } }
                    },
                    Size = 3, // Retrieve top 3 relevant documents/chunks
                    Select = { "id", "source", "basecampProjectId", "dropboxFilePath", "extractedText", "ocrText", "documentType" } // Select fields to retrieve
                };

                SearchClient _searchClient = new SearchClient(new Uri("https://ncataisearch.search.windows.net"), "ncat-index", new AzureKeyCredential("IxykhFzO7hrknPTWcr6zALO4FRFDmDrdDGoEdczVehAzSeABC84h"));
                SearchResults<SearchDocument> searchResults = await _searchClient.SearchAsync<SearchDocument>(null, searchOptions);

                string retrievedContext = "";
                _logger.LogWarning("retrieved the context, search results are: " + searchResults.ToString());
                if (searchResults.TotalCount > 0)
                {
                    foreach (SearchResult<SearchDocument> result in searchResults.GetResults())
                    {
                        // Concatenate relevant text from retrieved documents
                        var doc = result.Document;
                        _logger.LogWarning("document is: "+result.Document.ToString());
                        retrievedContext += $"Source: {doc["source"]}\n";
                        if (doc.TryGetValue("basecampProjectId", out object basecampProjectId))
                        {
                            retrievedContext += $"Project ID: {basecampProjectId}\n";
                        }
                        if (doc.TryGetValue("extractedText", out object extractedText))
                        {
                            retrievedContext += $"{extractedText}\n";
                        }
                        if (doc.TryGetValue("ocrText", out object ocrText))
                        {
                            retrievedContext += $"OCR Text: {ocrText}\n";
                        }
                        retrievedContext += "---\n";
                    }
                    _logger.LogWarning($"Retrieved {searchResults.TotalCount} documents as context.");
                }
                else
                {
                    _logger.LogWarning("No relevant documents found in vector search.");
                    retrievedContext = "No specific information found in the knowledge base relevant to your query.";
                }

                // Step 3: Context Augmentation & Generation (LLM Call)
                _logger.LogWarning("Sending query and context to Azure OpenAI Chat Completions.");
                //ChatClient client = new(model: "gpt-4o", apiKey: "61arcclb5pyx9gzxWpyD6CYnSUYbWm6gYPDEIlnnGVlkQfGObgsoJQQJ99BGACYeBjFXJ3w3AAABACOGZ8AH");
                ChatClient client = openAIClient.GetChatClient("gpt-4o");

                ChatMessage[] chatMessages = new ChatMessage[]{
        new SystemChatMessage("You are an AI assistant that answers questions based on the provided context from a knowledge base. If the answer is not in the context, state that you don't have enough information. Be concise and helpful.\n\nContext:\n"+retrievedContext),
        new UserChatMessage(request.Message),

                };
                ChatCompletion completion = client.CompleteChat(chatMessages);


                _logger.LogWarning($"[ASSISTANT]: {completion.Content[0].Text}");
                string llmResponse = $"[ASSISTANT]: {completion.Content[0].Text}";
                /*var chatCompletionsOptions = new ChatCompletionsOptions()
                {
                    Messages =
                {
                    new ChatRequestSystemMessage($"You are an AI assistant that answers questions based on the provided context from a knowledge base. If the answer is not in the context, state that you don't have enough information. Be concise and helpful.\n\nContext:\n{retrievedContext}"),
                    new ChatRequestUserMessage(request.Message),
                },
                    MaxTokens = 800, // Limit response length
                    Temperature = 0.7f, // Adjust for creativity vs. factualness
                                        // Other options like TopP, StopSequences etc.
                };

                Azure.Response<ChatCompletions> chatCompletionsResponse = await _chatCompletionsClient.GetChatCompletionsAsync(chatCompletionsOptions);
                string llmResponse = chatCompletionsResponse.Value.Choices[0].Message.Content;

                _logger.LogWarning("LLM response generated successfully, it is: " + llmResponse);
                return Ok(new ChatResponse { Answer = llmResponse, Context = retrievedContext });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during RAG chat processing.");
                return StatusCode(500, "An error occurred while processing your request.");
            }*/
        }

        /*[HttpGet("TestEndpoint")]
        public async Task<IActionResult> TestEndpoint()
        {

            _logger.LogInformation("Basecamp ingestion function received a request.");

            try
            {
                // Get Basecamp API Token from app settings or Key Vault
                string basecampApiToken = "BAhbB0kiAbB7ImNsaWVudF9pZCI6ImExZGZhNjVhNGE3OTQwNzc3MjMyZThlZjJkZjNlOTU4MDI4NTk0ZTkiLCJleHBpcmVzX2F0IjoiMjAyNS0wOS0wNFQwNDo1MzozNFoiLCJ1c2VyX2lkcyI6WzUxNDY5NTYyXSwidmVyc2lvbiI6MSwiYXBpX2RlYWRib2x0IjoiMmVjOGUzMDZhNzM0ZGEzM2E1MDQ2ZGNmZTc1YjAwYzQifQY6BkVUSXU6CVRpbWUNhGAfwJNWLdYJOg1uYW5vX251bWkCbAE6DW5hbm9fZGVuaQY6DXN1Ym1pY3JvIgc2QDoJem9uZUkiCFVUQwY7AEY=--157b65018a9d8c614c2abf317ee0a60198c52ec3";
                string basecampAccountId = "3615541";
                string storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=ncataistorage;AccountKey=gw5AkFWbQQaaziwseHmJUhqklvWHXbRHdmquhIzE/jdn6UkoUQdtwkihagFuXGpbIAOMhx2PiWWB+AStmkE6Ig==;EndpointSuffix=core.windows.net";
                string containerName = "pipelinecontent";

                //using refresh token workflow
                string basecampAccessToken = await GetNewBasecampAccessTokenAsync();

                if (string.IsNullOrEmpty(basecampApiToken) || string.IsNullOrEmpty(basecampAccountId))
                {
                    _logger.LogError("Basecamp API token or account ID is not configured.");
                    return Content(HttpStatusCode.InternalServerError.ToString());
                }

                var blobServiceClient = new BlobServiceClient(storageConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
                _logger.LogWarning("basecampAccessToken value: " + basecampAccessToken);
                // --- 1. Get list of projects from Basecamp ---
                string projectsUrl = $"https://3.basecampapi.com/3615541/projects.json";
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "NCAT-AI (robertarthurgillespie@gmail.com)");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {basecampAccessToken}");

                _logger.LogWarning("getting projects");
                var projectsResponse = await _httpClient.GetAsync(projectsUrl);
                projectsResponse.EnsureSuccessStatusCode();
                string projectsJson = await projectsResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("projects Json is: " + projectsJson);
                using JsonDocument projectsDoc = JsonDocument.Parse(projectsJson);
                bool endLoop = false;
                foreach (JsonElement project in projectsDoc.RootElement.EnumerateArray())
                {
                    string projectId = project.GetProperty("id").GetRawText();
                    _logger.LogWarning($"Processing project ID: {projectId}");

                    // Find the 'Docs & Files' tool
                    if (project.TryGetProperty("dock", out JsonElement dockElement) && dockElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement dockItem in dockElement.EnumerateArray())
                        {
                            if (dockItem.TryGetProperty("name", out JsonElement nameElement) && nameElement.GetString() == "vault")
                            {

                                //await IngestVaultContentAsync(projectId, vaultId, basecampApiToken, blobContainerClient, _logger);
                                _logger.LogWarning("nameElement is: " + nameElement.ToString());
                                string originalUrl = string.Empty;
                                string newUrl = string.Empty;
                                string nextPageUrl = string.Empty;
                                if (dockItem.TryGetProperty("url", out JsonElement vaultUrl))
                                {
                                    _logger.LogWarning("original vaultUrl is: " + vaultUrl.ToString());
                                    await IngestTheVaultContentAsync(projectId, vaultUrl.ToString(), basecampApiToken, blobContainerClient, _logger);
                                }
                             
                            }
                        }
                    }
                }


                return Content("success");
            }
            catch (Exception ex)
            {
                return Content("error, reason is: " + ex.Message+" logstring is: "+logString);
                //_logger.LogError(ex, "Error during Basecamp ingestion.");
                //_logger.LogWarning("reason for error is: " + ex.Message);
                
            }


        }

        private async Task IngestTheVaultContentAsync(string projectId, string vaultUrl, string basecampApiToken, BlobContainerClient blobContainerClient, ILogger logger)
        {
            string originalUrl = string.Empty;
            string newUrl = string.Empty;
            string nextPageUrl = string.Empty;

            logString += "vault url: " + vaultUrl + "\n\n\n";
            //_logger.LogWarning("vault url: " + vaultUrl);
            // Fix: Use GetString() to get the URL from the JsonElement
            originalUrl = vaultUrl;
            nextPageUrl = vaultUrl;
            if (originalUrl.Contains(".json"))
            {
                newUrl = originalUrl.Replace(".json", "/uploads.json");
                logString += "new url is: " + newUrl + "\n\n\n";
                //_logger.LogWarning("new url is: " + newUrl);
                string trimmedUrl = newUrl.Trim('"');
                nextPageUrl = trimmedUrl;
            }
            do
            {
                
                logString += "Fetching uploads from: " + nextPageUrl + "\n\n\n";

                //_logger.LogWarning($"Fetching uploads from: {nextPageUrl}");
                Uri requestUri = new Uri(nextPageUrl);
                logString += "Request uri is: " + requestUri + "\n\n\n";
                var uploadsResponse = await _httpClient.GetAsync(requestUri);
                logString += "made call to uploads response";
                string uploadsJson = await uploadsResponse.Content.ReadAsStringAsync();
                logString += "uploadsResponse was: " + uploadsJson + "\n\n\n";
                //_logger.LogWarning("uploadsResponse was: " + uploadsJson);
                if ((uploadsJson == "[]") || (string.IsNullOrWhiteSpace(uploadsJson)))
                {
                    break;
                }
                using JsonDocument uploadsDoc = JsonDocument.Parse(uploadsJson);
                foreach (JsonElement upload in uploadsDoc.RootElement.EnumerateArray())
                {
                    string uploadId = upload.GetProperty("id").GetRawText();

                    string uploadTitle = upload.GetProperty("title").GetString();
                    string blobName = $"basecamp/projects/{projectId}/uploads/{uploadId}_{uploadTitle}.json";
                    string downloadUrl = upload.GetProperty("download_url").GetString();
                    string fileName = upload.GetProperty("filename").GetString();
                    logString += "upload is built, title is: " + uploadTitle + "blob name is: " + blobName + "download url is: " + downloadUrl + ", file name is: " + fileName + "\n\n\n";
                    logString += "you download: " + uploadTitle + " at the url: " + downloadUrl + "\n\n\n";
                    //_logger.LogWarning("upload is built, title is: " + uploadTitle + "blob name is: " + blobName + "download url is: " + downloadUrl + ", file name is: " + fileName);
                    //_logger.LogWarning("you download: " + uploadTitle + " at the url: " + downloadUrl);

                    using (var downloadedStream = await _httpClient.GetStreamAsync(downloadUrl))
                    {
                        if (blobContainerClient.GetBlobClient(uploadTitle).Exists())
                        {
                            string fullUrl = blobContainerClient.GetBlobClient(uploadTitle).Uri.ToString();
                            logString += "blob already exists, no upload needed, url is: " + fullUrl + "\n\n\n";
                            //_logger.LogWarning("blob already exists, no upload needed, url is: " + fullUrl);
                        }
                        else
                        {
                            await blobContainerClient.UploadBlobAsync(uploadTitle, downloadedStream);
                            logString += $"Uploaded file {uploadTitle} from uploads to blob storage.\n\n";
                            //_logger.LogWarning($"Uploaded file {uploadTitle} from uploads to blob storage.");
                        }

                    }





                    // Check for the next page URL
                    nextPageUrl = null;
                    foreach (var header in uploadsResponse.Headers)
                    {
                        logString += $"the headers in the response are: {header.Key}: {string.Join(", ", header.Value)}" + "\n\n";
                        //_logger.LogWarning($"the headers in the response are: {header.Key}: {string.Join(", ", header.Value)}");
                    }
                    if (uploadsResponse.Headers.TryGetValues("Link", out IEnumerable<string> linkHeaders))
                    {
                        logString += "found the link headers\n\n";
                        // _logger.LogWarning("found the link headers");
                        var nextLink = linkHeaders.FirstOrDefault(h => h.Contains("rel=\"next\""));
                        logString += "the next link is: " + nextLink + "\n\n";
                        //_logger.LogWarning("the next link is: " + nextLink);
                        if (nextLink != null)
                        {
                            nextPageUrl = nextLink.Split(';')[0].Trim('<', '>');
                        }
                    }
                }


            } while (nextPageUrl != null);
            // Check for sub-vaults and recurse
            //_logger.LogWarning("checking for sub-vaults");
            logString += "checking for subvaults \n\n";
            string vaultId = vaultUrl.Split("vaults/")[1].Split(".json")[0];
            logString += "vaultId is: " + vaultId + "\n\n";
            string subVaultsUrl = $"https://3.basecampapi.com/3615541/buckets/{projectId}/vaults/{vaultId}/vaults.json";
            var subVaultsResponse = await _httpClient.GetAsync(subVaultsUrl);
            logString += "subvaults response is: " + subVaultsResponse + "\n\n";
            //_logger.LogWarning("subvaults response is: " + subVaultsResponse);
            subVaultsResponse.EnsureSuccessStatusCode();
            string subVaultsJson = await subVaultsResponse.Content.ReadAsStringAsync();
            logString += "subvaults json is: " + subVaultsJson + "\n\n";
            //_logger.LogWarning("subvaults json is: " + subVaultsJson);
            if (subVaultsJson.Contains("url"))
            {
                using JsonDocument subVaultsDoc = JsonDocument.Parse(subVaultsJson);
                foreach (JsonElement subVault in subVaultsDoc.RootElement.EnumerateArray())
                {
                    string subVaulturl = subVault.GetProperty("url").GetRawText();
                    string subVaulturlTrimmed = subVaulturl.Trim('"');
                    logString += "the subvaultUrl is: " + subVaulturl + "\n\n\n";
                    //_logger.LogWarning("the subVaulturl is: " + subVaultsUrl);
                    logString += "calling function again, projectId: " + projectId + ", subVaulturl: " + subVaulturl;
                    await IngestTheVaultContentAsync(projectId, subVaulturl, basecampApiToken, blobContainerClient, logger);
                }
            }
        }
        private async Task<string> GetNewBasecampAccessTokenAsync()
        {
            //access token as of 8/21/25, may have to refresh occasionally
            string accesstoken = "BAhbB0kiAbB7ImNsaWVudF9pZCI6ImExZGZhNjVhNGE3OTQwNzc3MjMyZThlZjJkZjNlOTU4MDI4NTk0ZTkiLCJleHBpcmVzX2F0IjoiMjAyNS0wOS0wNFQxMjozNzowNloiLCJ1c2VyX2lkcyI6WzUxNTYxMDYyXSwidmVyc2lvbiI6MSwiYXBpX2RlYWRib2x0IjoiNzMxZWZjZTY4MmRmZDBjNDY3NDJmYmZlZmExMjUyZDMifQY6BkVUSXU6CVRpbWUNjGAfwI/CY5QJOg1uYW5vX251bWlrOg1uYW5vX2RlbmkGOg1zdWJtaWNybyIHECA6CXpvbmVJIghVVEMGOwBG--57782972ad77222f401aae99d2b98f6652ec0246";

            string refreshToken = "BAhbB0kiAbB7ImNsaWVudF9pZCI6ImExZGZhNjVhNGE3OTQwNzc3MjMyZThlZjJkZjNlOTU4MDI4NTk0ZTkiLCJleHBpcmVzX2F0IjoiMjAzNS0wOC0yMVQxMjozNzowNloiLCJ1c2VyX2lkcyI6WzUxNTYxMDYyXSwidmVyc2lvbiI6MSwiYXBpX2RlYWRib2x0IjoiNzMxZWZjZTY4MmRmZDBjNDY3NDJmYmZlZmExMjUyZDMifQY6BkVUSXU6CVRpbWUNrN4hwBzRY5QJOg1uYW5vX251bWkC0QI6DW5hbm9fZGVuaQY6DXN1Ym1pY3JvIgdyEDoJem9uZUkiCFVUQwY7AEY=--a22dae29a2922c2943d81031cbe461dcc2db9a7a";
            string clientId = "a1dfa65a4a7940777232e8ef2df3e958028594e9";
            string clientSecret = "01a4950e6005f1f81954b44ff40449e42510302f";

            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("Basecamp refresh token or client credentials are not configured.");
                throw new InvalidOperationException("Missing Basecamp refresh token or client credentials.");
            }


            var tokenRequestUrl = "https://launchpad.37signals.com/authorization/token";
            var content = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string, string>("type", "refresh"),
        new KeyValuePair<string, string>("refresh_token", refreshToken),
        new KeyValuePair<string, string>("client_id", clientId),
        new KeyValuePair<string, string>("client_secret", clientSecret)
    });
            _logger.LogWarning("posting to get a new refresh token");
            var response = await _httpClient.PostAsync(tokenRequestUrl, content);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("body of refresh token request response is: " + responseBody);
            _logger.LogWarning("trying to parse json");
            using JsonDocument doc = JsonDocument.Parse(responseBody);

            string newAccessToken = doc.RootElement.GetProperty("access_token").GetString();
            _logger.LogWarning("The new access token is: " + newAccessToken);

            return newAccessToken;
        }
    }*/

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
