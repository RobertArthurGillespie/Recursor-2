using Azure.Storage.Blobs;
using FFMpegCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Shared;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using System;
using FFMpegCore.Enums;
using NAudio.Wave;
using Azure.Storage.Blobs.Models;

namespace NCATAIBlazorFrontendTest.Server.Controllers
{
    // Manual/developer-only test harness for the Dropbox-to-Blob-Storage video pipeline.
    // Never used by the Unity sim or the production dashboard. Gated on both admin
    // authorization and the Development environment so it cannot run — or leak the
    // Dropbox/storage credentials it needs — outside a developer's own machine.
    [ApiController]
    [Route("[controller]")]
    [Authorize(Policy = "RecursorModelAdmin")]
    public class TestingController : ControllerBase
    {
        private static readonly HttpClient _httpClient = new();
        private string logString = string.Empty;
        private readonly ILogger<TestingController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public TestingController(
            ILogger<TestingController> logger,
            IConfiguration configuration,
            IWebHostEnvironment env)
        {
            _logger = logger;
            _configuration = configuration;
            _env = env;
        }

        public IActionResult Index()
        {
            return Content("home");
        }

        private static string RequireConfig(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Missing required configuration '{key}'. This developer-only endpoint needs it set via " +
                    "user-secrets — it is never required for normal app startup.");
            }
            return value;
        }

        [HttpGet("TestVideoConversion")]
        public async Task<IActionResult> TestVideoConversion()
        {
            if (!_env.IsDevelopment())
                return NotFound();

            string requestBody = "{\"fileName\":\"AmberCutVer2-Test.mp4\",\"filePath\":\"/paper docs/ambercutver2-test.mp4\"}";
            string filePath = "";
            string fileName = "";
            try
            {

                logString += "request body is: " + requestBody;
            }
            catch (Exception ex)
            {
                logString += "Failed to read request body, error is:" + ex.Message;
                return Content(logString);
            }
            try
            {
                var payload = JsonSerializer.Deserialize<DropboxUploadPayload>(requestBody);

                if (payload == null)
                {
                    logString += "Failed to deserialize request body.";
                    return Content(logString);
                }

                filePath = payload.FilePath;
                fileName = payload.FileName;

                logString += $"Received request to upload file '{fileName}' from Dropbox path: '{filePath}'";

                logString += "The filepath of the dropbox object is: " + filePath;
                logString += "The fileName of the dropbox object is: " + fileName;

                logString += $"Received request to upload file '{fileName}' from Dropbox path: '{filePath}'";

                var jsonBody = new
                {
                    path = filePath
                };

                string jsonString = JsonSerializer.Serialize(jsonBody);

                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                logString += "json api arg content is: " + content + "and json string is: " + jsonString;

                var dropboxRefreshToken = RequireConfig(_configuration["Dropbox:RefreshToken"], "Dropbox:RefreshToken");

                // Step 1: Get a fresh access token using the refresh token
                logString += "attempting to get a fresh access token using a refresh token";
                string dropboxAccessToken = await GetNewDropboxAccessTokenAsync(dropboxRefreshToken);
                // Never log or echo the access token itself — only that one was obtained.
                logString += "procedurally created a new access token";
                // Step 1: Download file content from Dropbox
                logString += "clearing and adding headers";
                var selectUser = RequireConfig(_configuration["Dropbox:SelectUser"], "Dropbox:SelectUser");
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {dropboxAccessToken}");
                _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Arg", jsonString);
                _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", selectUser);

                string downloadUrl = "https://content.dropboxapi.com/2/files/download"; // This is the API endpoint
                logString += "making call to download endpoint";


                var downloadResponse = await _httpClient.PostAsync(downloadUrl, null);
                downloadResponse.EnsureSuccessStatusCode();

                logString += "posted content successfully";

                // Step 2: Upload file content to Azure Blob Storage
                var storageConnectionString = RequireConfig(
                    _configuration["AzureStorage:PipelineContent:ConnectionString"], "AzureStorage:PipelineContent:ConnectionString");
                string containerName = "pipelinecontent";
                string blobName = $"dropbox/{filePath.TrimStart('/')}"; // Use the full path for blob name

                logString += "uploading blob to blob storage";
                var blobServiceClient = new BlobServiceClient(storageConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
                logString += "getting blob container client with blobName: " + blobName;
                var blobClient = blobContainerClient.GetBlobClient(blobName);

                logString += "beginning upload checks";

                if (fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    logString += "file is a video, trying to extract audio";
                    // Step 1: Download the file to a temporary location
                    string tempVideoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
                    logString += $"Downloading file to temporary path: {tempVideoPath}";
                    using (var fileStream = new FileStream(tempVideoPath, FileMode.Create))
                    {
                        await downloadResponse.Content.CopyToAsync(fileStream);
                    }

                    // Step 2: Extract audio using FFMpegCore from the temporary file
                    string tempAudioPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
                    logString += $"Extracting audio to temporary WAV file: {tempAudioPath}";
                    try
                    {
                        logString += "calling ExtractAudio()";
                        bool extractedAudio = FFMpeg.ExtractAudio(tempVideoPath, tempAudioPath);
                        if (extractedAudio)
                        {
                            logString += "audio was extracted";
                        }
                        else
                        {
                            logString += "failed to extract audio";
                        }

                    }
                    catch (Exception ex)
                    {

                        // Clean up temporary files
                        System.IO.File.Delete(tempVideoPath);
                        if (System.IO.File.Exists(tempAudioPath)) System.IO.File.Delete(tempAudioPath);
                        return Content($"FFmpeg audio extraction failed for {fileName}. Reason: {ex.Message}, logString: " + logString);
                    }

                    // Step 3: Check for speech in the temporary audio file
                    logString += "Checking for speech in the extracted audio file.";
                    bool hasSpeech = false;
                    using (var audioStream = new FileStream(tempAudioPath, FileMode.Open, FileAccess.Read))
                    {
                        if (audioStream.Length > 0)
                        {
                            hasSpeech = CheckForSpeech(audioStream, 10);
                        }
                    }

                    // Step 4: Conditionally upload the original MP4 file and clean up
                    if (hasSpeech)
                    {
                        logString += $"Speech detected in {fileName}. Uploading original video to Blob Storage.";
                        using (var originalVideoStream = new FileStream(tempVideoPath, FileMode.Open, FileAccess.Read))
                        {
                            if (!blobClient.Exists())
                            {
                                await blobClient.UploadAsync(originalVideoStream, overwrite: true);
                                logString += $"Successfully uploaded original video '{fileName}' to Blob Storage as '{blobName}'.";
                            }
                            else
                            {
                                logString += "video already exists, not uploading";
                            }

                        }
                    }
                    else
                    {
                        logString += $"No speech detected in {fileName}. Skipping upload to Blob Storage.";
                    }

                    logString += "got through conditional upload process";
                    return Content("success! " + logString);
                }
                string ffmpegpath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                return Content(logString + ", " + ffmpegpath);
            }
            catch (Exception ex)
            {
                return Content("error, issue is: " + ex.Message + ", " + logString);

            }


        }

        [HttpGet("TestingListDropbox")]
        public async Task<IActionResult> TestingListDropbox()
        {
            if (!_env.IsDevelopment())
                return NotFound();

            string requestBody = "{\"path\":\"Coaxiomservices/CoAxiom Services/Paper Docs\",\"recursive\":\"true\"}";
            try
            {

                logString += "request body is: " + requestBody;
            }
            catch (Exception ex)
            {
                logString += "Failed to read request body, error is:" + ex.Message;
                return Content(logString);
            }
            try
            {
                var dropboxRefreshToken = RequireConfig(_configuration["Dropbox:RefreshToken"], "Dropbox:RefreshToken");

                // Step 1: Get a fresh access token using the refresh token
                logString += "attempting to get a fresh access token using a refresh token";
                string dropboxAccessToken = await GetNewDropboxAccessTokenAsync(dropboxRefreshToken);
                // Never log or echo the access token itself — only that one was obtained.
                logString += "procedurally created a new access token";

                // Step 1: Get a list of all files in the Dropbox account
                string dropboxRootPath = ""; // The root of your Dropbox folder
                logString += "calling list dropbox files method";
                List<(string? FilePath, string? FileName)> allDropboxFiles = await ListDropboxFilesRecursiveAsync(dropboxRootPath, dropboxAccessToken);

                var selectUser = RequireConfig(_configuration["Dropbox:SelectUser"], "Dropbox:SelectUser");
                var storageConnectionString = RequireConfig(
                    _configuration["AzureStorage:PipelineContent:ConnectionString"], "AzureStorage:PipelineContent:ConnectionString");

                // Step 2: Iterate through the list and upload each file
                foreach (var file in allDropboxFiles)
                {
                    if (file.FilePath is null || file.FileName is null) continue;
                    string FilePath = file.FilePath;
                    string FileName = file.FileName;
                    string BlobName = $"dropbox/{FilePath.TrimStart('/')}";

                    logString += " blob filepath is: " + FilePath+"\n\n";
                    // Download the file content from Dropbox
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {dropboxAccessToken}");
                    _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = FilePath }));
                    _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", selectUser);

                    string DownloadUrl = "https://content.dropboxapi.com/2/files/download";
                    var DownloadResponse = await _httpClient.PostAsync(DownloadUrl, null);
                    DownloadResponse.EnsureSuccessStatusCode();

                    string containerName = "pipelinecontent";
                    string blobName = $"dropbox/{FilePath.TrimStart('/')}"; // Use the full path for blob name

                    logString += "uploading blob to blob storage";
                    var blobServiceClient = new BlobServiceClient(storageConnectionString);
                    var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
                    logString += "getting blob container client with blobName: " + blobName;
                    var blobClient = blobContainerClient.GetBlobClient(blobName);

                    using (var downloadStream = await DownloadResponse.Content.ReadAsStreamAsync())
                    {
                        if (!blobClient.Exists())
                        {
                            await blobClient.UploadAsync(downloadStream, overwrite: true);
                            logString += $"Successfully uploaded '{FileName}' to Blob Storage as '{blobName}'.";
                        }
                        else
                        {
                            logString += "file already exists, not uploading";
                        }

                    }
                    logString+=$"Successfully uploaded file '{FileName}' to Blob Storage.";
                }

                return Content("success! "+logString);
            }
            catch(Exception ex)
            {
                return Content("error, reason is: " + ex.Message+", logstring: "+logString);
            }
        }


        private async Task<string> GetNewDropboxAccessTokenAsync(string refreshToken)
        {
            logString += "Getting new Dropbox access token using refresh token.";

            var clientId = RequireConfig(_configuration["Dropbox:ClientId"], "Dropbox:ClientId");
            var clientSecret = RequireConfig(_configuration["Dropbox:ClientSecret"], "Dropbox:ClientSecret");

            var tokenRequestUrl = "https://api.dropbox.com/oauth2/token";
            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
        });
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Content_Type", "application/x-www-form-urlencoded");
            logString += "calling post method to get access token";
            var response = await _httpClient.PostAsync(tokenRequestUrl, content);
            response.EnsureSuccessStatusCode();
            logString += "successfully alled post method to get access token";
            string responseBody = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseBody);
            string newAccessToken = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Dropbox token response did not include an access_token.");

            // Never log or echo the access token itself.
            logString += "Successfully refreshed Dropbox access token.";
            return newAccessToken;
        }

        private bool CheckForSpeech(Stream audioStream, double threshold)
        {
            try
            {
                audioStream.Position = 0;
                using (var reader = new Mp3FileReader(audioStream))
                {
                    var buffer = new byte[reader.WaveFormat.AverageBytesPerSecond];
                    int bytesRead;
                    long totalBytes = 0;
                    long totalSum = 0;

                    while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        totalBytes += bytesRead;
                        for (int i = 0; i < bytesRead; i++)
                        {
                            totalSum += Math.Abs(buffer[i]);
                        }
                    }

                    if (totalBytes > 0)
                    {
                        double averageAmplitude = (double)totalSum / totalBytes;
                        return averageAmplitude > threshold;
                    }
                }
            }
            catch (Exception ex)
            {
                logString += $"Failed to analyze audio stream for speech, reason is: {ex.Message}";
            }

            return false;
        }

        private async Task<List<(string? FilePath, string? FileName)>> ListDropboxFilesRecursiveAsync(string folderPath, string dropboxAccessToken)
        {
            logString += "starting List dropbox files method";
            var allFiles = new List<(string?, string?)>();
            string? cursor = null;

            var selectUser = RequireConfig(_configuration["Dropbox:SelectUser"], "Dropbox:SelectUser");

            try
            {
                do
                {
                    // Construct the request body for listing folders
                    var jsonBody = new
                    {
                        path = folderPath,
                        recursive = true,
                        limit = 2000 // A reasonable limit to handle pagination
                    };

                    string jsonString = JsonSerializer.Serialize(jsonBody);
                    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                    logString += " json content is: " + content.ToString() + "," + "and content json is: " + jsonString + "\n\n\n";
                    // Make the API call to list files
                    string listFolderUrl = cursor == null
                        ? "https://api.dropboxapi.com/2/files/list_folder"
                        : "https://api.dropboxapi.com/2/files/list_folder/continue";

                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {dropboxAccessToken}");
                    _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", selectUser);
                    logString += " making list post request, url is: "+listFolderUrl+", \n\n";
                    var response = await _httpClient.PostAsync(listFolderUrl, content);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    logString += "response body is: " + responseBody + "\n\n";
                    using JsonDocument doc = JsonDocument.Parse(responseBody);
                    JsonElement root = doc.RootElement;

                    // Extract files from the response
                    if (root.TryGetProperty("entries", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement entry in entries.EnumerateArray())
                        {
                            string? tag = entry.GetProperty(".tag").GetString();
                            logString += " tag is: " + tag + "\n\n";
                            if (tag == "file")
                            {
                                string? filePath = entry.GetProperty("path_display").GetString();
                                string? fileName = entry.GetProperty("name").GetString();
                                logString += "file path is: " + filePath + " and file name is: " + fileName + "\n\n";
                                allFiles.Add((filePath, fileName));
                            }
                        }
                    }

                    // Check for pagination cursor
                    if (root.TryGetProperty("cursor", out JsonElement cursorElement) && cursorElement.ValueKind == JsonValueKind.String)
                    {
                        cursor = cursorElement.GetString();
                        logString += " cursor is: " + cursor + "\n\n";
                    }
                    else
                    {
                        cursor = null; // No more pages
                    }

                } while (cursor != null);
            }
            catch(Exception ex)
            {
                logString += " something went wrong while listing, exception is: " + ex.Message;
            }


            return allFiles;
        }
    }
}
