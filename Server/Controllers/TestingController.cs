using Azure.Storage.Blobs;
using FFMpegCore;
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
    [ApiController]
    [Route("[controller]")]
    public class TestingController : ControllerBase
    {
        private static readonly HttpClient _httpClient = new();
        private string logString = string.Empty;
        ILogger<TestingController> _logger;
        public IActionResult Index()
        {
            return Content("home");
        }

        [HttpGet("TestVideoConversion")]
        public async Task<IActionResult> TestVideoConversion()
        {
            string requestBody = "{\"fileName\":\"AmberCutVer2-Test.mp4\",\"filePath\":\"/paper docs/ambercutver2-test.mp4\"}";
            string filePath = null;
            string fileName = null;
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

                /*using JsonDocument doc = JsonDocument.Parse(requestBody);
                filePath = doc.RootElement.GetProperty("filePath").GetString();
                fileName = doc.RootElement.GetProperty("fileName").GetString();*/
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

                string dropboxRefreshToken = "mubl5zYdracAAAAAAAAAAW6RSyroBs38gXxBeOoDCaiVWEd0iAkQGbWZtZYxDeNO";
                if (string.IsNullOrEmpty(dropboxRefreshToken))
                {
                    logString += "DropboxRefreshToken environment variable is not set.";
                    return Content(logString);
                }

                // Step 1: Get a fresh access token using the refresh token
                logString += "attempting to get a fresh access token using a refresh token";
                string dropboxAccessToken = await GetNewDropboxAccessTokenAsync(dropboxRefreshToken);
                logString += "procedurally created access token is: " + dropboxAccessToken;
                // Step 1: Download file content from Dropbox
                logString += "clearing and adding headers";
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {dropboxAccessToken}");
                _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Arg", jsonString);
                _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", "dbmid:AABr7MBz6SGUmOM9U77Fc0fRuaQ-tTCf4wc");

                string downloadUrl = "https://content.dropboxapi.com/2/files/download"; // This is the API endpoint
                logString += "making call to download endpoint";


                var downloadResponse = await _httpClient.PostAsync(downloadUrl, null);
                downloadResponse.EnsureSuccessStatusCode();

                logString += "posted content successfully";

                // Step 2: Upload file content to Azure Blob Storage
                string storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=ncataistorage;AccountKey=gw5AkFWbQQaaziwseHmJUhqklvWHXbRHdmquhIzE/jdn6UkoUQdtwkihagFuXGpbIAOMhx2PiWWB+AStmkE6Ig==;EndpointSuffix=core.windows.net";
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
                    //string tempVideoPath = Path.GetTempPath();
                    logString += $"Downloading file to temporary path: {tempVideoPath}";
                    using (var fileStream = new FileStream(tempVideoPath, FileMode.Create))
                    {
                        await downloadResponse.Content.CopyToAsync(fileStream);
                    }

                    // Step 2: Extract audio using FFMpegCore from the temporary file
                    string tempAudioPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
                    //string tempAudioPath = Path.GetTempPath();
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

                    // Step 5: Clean up temporary files
                    //File.Delete(tempVideoPath);
                    //if (File.Exists(tempAudioPath)) File.Delete(tempAudioPath);

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
            string requestBody = "{\"path\":\"Coaxiomservices/CoAxiom Services/Paper Docs\",\"recursive\":\"true\"}";
            string filePath = null;
            string fileName = null;
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
                /*var payload = JsonSerializer.Deserialize<DropboxUploadPayload>(requestBody);

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
                logString += "json api arg content is: " + content + "and json string is: " + jsonString;*/

                string dropboxRefreshToken = "mubl5zYdracAAAAAAAAAAW6RSyroBs38gXxBeOoDCaiVWEd0iAkQGbWZtZYxDeNO";
                if (string.IsNullOrEmpty(dropboxRefreshToken))
                {
                    logString += "DropboxRefreshToken environment variable is not set.";
                    return Content(logString);
                }

                // Step 1: Get a fresh access token using the refresh token
                logString += "attempting to get a fresh access token using a refresh token";
                string dropboxAccessToken = await GetNewDropboxAccessTokenAsync(dropboxRefreshToken);
                logString += "procedurally created access token is: " + dropboxAccessToken;

                // Step 1: Get a list of all files in the Dropbox account
                string dropboxRootPath = ""; // The root of your Dropbox folder
                logString += "calling list dropbox files method";
                List<(string, string)> allDropboxFiles = await ListDropboxFilesRecursiveAsync(dropboxRootPath, dropboxAccessToken);

                // Step 2: Iterate through the list and upload each file
                foreach (var file in allDropboxFiles)
                {
                    string FilePath = file.Item1;
                    string FileName = file.Item2;
                    string BlobName = $"dropbox/{FilePath.TrimStart('/')}";

                    logString += " blob filepath is: " + FilePath+"\n\n";
                    // Download the file content from Dropbox
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {dropboxAccessToken}");
                    _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = FilePath }));
                    _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", "dbmid:AABr7MBz6SGUmOM9U77Fc0fRuaQ-tTCf4wc");

                    string DownloadUrl = "https://content.dropboxapi.com/2/files/download";
                    var DownloadResponse = await _httpClient.PostAsync(DownloadUrl, null);
                    DownloadResponse.EnsureSuccessStatusCode();

                    string storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=ncataistorage;AccountKey=gw5AkFWbQQaaziwseHmJUhqklvWHXbRHdmquhIzE/jdn6UkoUQdtwkihagFuXGpbIAOMhx2PiWWB+AStmkE6Ig==;EndpointSuffix=core.windows.net";
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


                ///****
                // Step 1: Download file content from Dropbox
                /*logString += "clearing and adding headers";
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {dropboxAccessToken}");
                _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Arg", jsonString);
                _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", "dbmid:AABr7MBz6SGUmOM9U77Fc0fRuaQ-tTCf4wc");

                string downloadUrl = "https://content.dropboxapi.com/2/files/download"; // This is the API endpoint
                logString += "making call to download endpoint";


                var downloadResponse = await _httpClient.PostAsync(downloadUrl, null);
                downloadResponse.EnsureSuccessStatusCode();

                logString += "posted content successfully";*/

                return Content("success! "+logString);
            }
            catch(Exception ex)
            {
                return Content("error, reason is: " + ex.Message+", logstring: "+logString);
            }
        }
        

        private async Task<string> GetNewDropboxAccessTokenAsync(string refreshToken)
        {
            //access token: sl.u.AF6V2QL_kkir87NM79yAz0vmbilHrDzDcUUzK2cQh1pdtbtJ1P_C5xs-AwfCbat-KU0EgicOytwK7SzMw15waKhWIT1GCCN3a93beyfKHRizveTgaDef3w4mMtlYUMG0WF8F9WOysoPh7_XVS2wq7DArBbuUspOqODWDbeGYDsTlxUVyb1oaXnU6Z_kFOcoSQNFz3bVs9FGEQMrzZIdFocGH3ijnGVqoDJD0SzHXqpnnX5AGykrdwKwt5CuLjfDVhmyH4PASWojRTbgVrSlO5lz4eBTQG6-E5eYHv4iBVKkFUM-xsH1HHirNKbt4JxBi_kXdXnU2Qc-T-zJ6kIQR1MDQr_z3Tj1EIwHnsGEpZPDfbTKJGhTUAfHjWIkHYS8g3iNdEckxRAYzjq67W0ahE97Hq87atfDdyK-Cv4FmBDK0KPqed9QliJZD_9Q9cvPraUcDWNgvCkFWIjfnPvHfdRzjJV3N6RjEhMoHOD9yoAJyukjNEqkREwGRTuRRs5Eo_QGSyiWAvyIOseAlUg9cDU_nnpSFegxoGqE-vYxXhahvc0GZAPtlhbVP1A-fQxMyrPHj-7d_c8C1JhLbGO9OyhuQyzAkAFka74BynpdCOMeBZ4snSBrr8yXENZbgvaMgS_4eF9QwSk2x4Dwm-CQ47N4yB5nV2Ox32Hm3ILDoQkB04M6mf6uvcIPVoAnIJxOqeDvGiuQyHQdD7ixoujHn1tpMuKY807er2iFblZK6SpJNadtz97nQ-syxrxCJmAKjuJTLVEvtaKo658jjR_lLN4nxrf49IkUjjOIsO5hlT0-YITH-uRdxlxZlttbCMECZCXXbR31FrKqTMnJ4gfwUxLuPjkBA9Fblwz3NEsapAGnxiU5nzeRj8CTM9HOBIVbY78GaCELDlEZxlwX_WpgA4gKuYw4S75-n0aVTvkuR5jO0ynVSmKYszItOrWkxM02sI2T-p3dmnZTi19JmP6yGTMMSQrUbvbgaQ7Cn-1fF3iCdU4aItXhvQ9fIlFVd8z-SLzmV_qDhcnD72GKAjS8no-IyX780EfObOD-BI6SmRtQow63pgTgHpwfQ0F0vBSzd_FSeQ3Y3sNPLXrMz9GPw9iPZ0AyK878G_m9wPwSPcYrjC4aZymO9cNItT4xjzyazwOI
            //referesh token: DavlaI0WV2sAAAAAAAAAAU9fGxsplu7wjOoKo-fYYXJobk9frxflczCK8anqKQGc
            logString += "Getting new Dropbox access token using refresh token.";
            string refreshTokenDirect = "mubl5zYdracAAAAAAAAAAW6RSyroBs38gXxBeOoDCaiVWEd0iAkQGbWZtZYxDeNO";
            logString += "the refresh token is: " + refreshTokenDirect;
            string clientId = "mbqco7pd53cyflh";
            string clientSecret = "p09dimd77gshaah";

            var tokenRequestUrl = "https://api.dropbox.com/oauth2/token";
            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshTokenDirect),
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
            string newAccessToken = doc.RootElement.GetProperty("access_token").GetString();

            logString += "Successfully refreshed Dropbox access token. " + newAccessToken;
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

        private async Task<List<(string?, string?)>> ListDropboxFilesRecursiveAsync(string folderPath, string dropboxAccessToken)
        {
            logString += "starting List dropbox files method";
            var allFiles = new List<(string?, string?)>();
            string cursor = null;
           
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
                    _httpClient.DefaultRequestHeaders.Add("Dropbox-API-Select-User", "dbmid:AABr7MBz6SGUmOM9U77Fc0fRuaQ-tTCf4wc");
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