using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using OpenAI.Containers;
using System.Text;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services
{
    
    public class BlobStateService
    {
        private readonly BlobContainerClient _containerClient;
        public BlobStateService(IConfiguration config)
        {
            var connectionString = config["AzureStorage:AvrServiceData:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing required configuration 'AzureStorage:AvrServiceData:ConnectionString'. Set via " +
                    "user-secrets locally or App Service configuration / Key Vault in deployed environments.");
            }

            _containerClient = new BlobContainerClient(connectionString, "avrservicedata");
        }

        public async Task SaveStateBlobAsync(string userId, string simId, string rawJson)
        {
            string blobPath = $"recursor-data/users/{userId}/{simId}/state.json";
            BlobClient blobClient = _containerClient.GetBlobClient(blobPath);

            using MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(rawJson));
            await blobClient.UploadAsync(ms, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            });
        }

        public async Task<string> GetStateBlobAsync(string userId, string simId)
        {
            string blobPath = $"recursor-data/users/{userId}/{simId}/state.json";
            BlobClient blobClient = _containerClient.GetBlobClient(blobPath);

            if (!await blobClient.ExistsAsync())
            {
                // Fail-safe: Return an empty json map if no file exists yet
                return "{}";
            }

            BlobDownloadResult downloadResult = await blobClient.DownloadContentAsync();
            return downloadResult.Content.ToString();
        }
    }
}
