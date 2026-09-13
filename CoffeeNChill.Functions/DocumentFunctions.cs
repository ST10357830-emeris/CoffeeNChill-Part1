using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using HttpMultipartParser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions
{
    public class DocumentFunctions
    {
        private readonly ILogger<DocumentFunctions> _logger;

        // Target Azure File Share specified in assignment specification
        private const string ShareName = "staff-docs";

        public DocumentFunctions(ILogger<DocumentFunctions> logger)
        {
            _logger = logger;
        }

        // Helper to retrieve initialized ShareDirectoryClient for the root directory of 'staff-docs'
        private static async Task<ShareDirectoryClient> GetShareDirectoryClientAsync()
        {
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string is missing.");

            // Instantiate ShareClient targeting 'staff-docs' share
            var shareClient = new ShareClient(connectionString, ShareName);

            // Automatically provision file share if it does not exist
            await shareClient.CreateIfNotExistsAsync();

            // Return root directory client handle
            return shareClient.GetRootDirectoryClient();
        }

        // 1. POST /api/documents/upload (UploadStaffDocument)
        [Function("UploadStaffDocument")]
        public async Task<HttpResponseData> UploadStaffDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
        {
            _logger.LogInformation("Streaming document upload to staff-docs file share...");

            try
            {
                // Parse multipart form payload asynchronously from HTTP request body stream
                var parser = await MultipartFormDataParser.ParseAsync(req.Body);

                // Retrieve uploaded file stream
                var file = parser.Files.FirstOrDefault();

                if (file == null || file.Data.Length == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("No file provided in the request payload.");
                    return badReq;
                }

                // Extract original filename
                string fileName = Path.GetFileName(file.FileName);

                // Get root directory client handle for 'staff-docs'
                var rootDir = await GetShareDirectoryClientAsync();

                // Get client reference to target file path
                var fileClient = rootDir.GetFileClient(fileName);

                // Create empty target file on Azure File Share allocated with source file byte length
                await fileClient.CreateAsync(file.Data.Length);

                // Reset stream cursor position before binary transfer
                file.Data.Position = 0;

                // Stream file binary content directly into Azure File Share
                await fileClient.UploadAsync(file.Data);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    Message = "File uploaded successfully to staff-docs share.",
                    FileName = fileName,
                    SizeBytes = file.Data.Length,
                    UploadedAt = DateTimeOffset.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document to staff-docs.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }

        // 2. GET /api/documents (ListStaffDocuments)
        [Function("ListStaffDocuments")]
        public async Task<HttpResponseData> ListStaffDocuments(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequestData req)
        {
            _logger.LogInformation("Listing files in staff-docs file share...");

            try
            {
                var rootDir = await GetShareDirectoryClientAsync();

                var fileList = new List<object>();

                await foreach (var item in rootDir.GetFilesAndDirectoriesAsync())
                {
                    if (!item.IsDirectory)
                    {
                        long fileSize = item.FileSize.GetValueOrDefault();
                        fileList.Add(new
                        {
                            FileName = item.Name,
                            SizeBytes = fileSize,
                            LastModified = item.Properties.LastModified
                        });
                    }
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(fileList);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing documents in staff-docs.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }

        // 3. GET /api/documents/download/{fileName} (DownloadStaffDocument)
        [Function("DownloadStaffDocument")]
        public async Task<HttpResponseData> DownloadStaffDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/download/{fileName}")] HttpRequestData req,
            string fileName)
        {
            _logger.LogInformation("Downloading staff document {FileName}...", fileName);

            try
            {
                string safeFileName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(safeFileName) || !string.Equals(safeFileName, fileName, StringComparison.Ordinal))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteStringAsync("A valid file name is required.");
                    return badRequest;
                }

                var rootDir = await GetShareDirectoryClientAsync();
                var fileClient = rootDir.GetFileClient(safeFileName);
                var download = await fileClient.DownloadAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/octet-stream");
                response.Headers.Add("Content-Disposition", $"attachment; filename=\"{safeFileName}\"");
                await download.Value.Content.CopyToAsync(response.Body);
                return response;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync("Staff document not found.");
                return notFound;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading staff document.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }
    }
}