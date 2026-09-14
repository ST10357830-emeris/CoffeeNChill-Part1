using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HttpMultipartParser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions
{
    public class DocumentFunctions
    {
        private readonly ILogger<DocumentFunctions> _logger;

        // Target Azure Blob container specified in the project addendum
        private const string ContainerName = "staff-docs";
        private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "text/plain",
            "image/jpeg",
            "image/png"
        };

        public DocumentFunctions(ILogger<DocumentFunctions> logger)
        {
            _logger = logger;
        }

        // Helper to retrieve the initialized Blob container client for 'staff-docs'
        private static async Task<BlobContainerClient> GetContainerClientAsync()
        {
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string is missing.");

            var serviceClient = new BlobServiceClient(connectionString);
            var containerClient = serviceClient.GetBlobContainerClient(ContainerName);

            // Automatically provision the document container if it does not exist
            await containerClient.CreateIfNotExistsAsync();

            return containerClient;
        }

        private static async Task<HttpResponseData> ErrorResponseAsync(HttpRequestData req, HttpStatusCode statusCode, string message)
        {
            var response = req.CreateResponse(statusCode);
            await response.WriteAsJsonAsync(new { Error = message });
            response.StatusCode = statusCode;
            return response;
        }

        // 1. POST /api/documents/upload (UploadStaffDocument)
        [Function("UploadStaffDocument")]
        public async Task<HttpResponseData> UploadStaffDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
        {
            _logger.LogInformation("Streaming document upload to staff-docs blob container...");

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
                string contentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType;

                if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, file.FileName, StringComparison.Ordinal))
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, "A valid file name is required.");
                }

                if (!AllowedContentTypes.Contains(contentType))
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, "Only PDF, text, JPEG, or PNG documents are supported.");
                }

                var containerClient = await GetContainerClientAsync();

                var blobClient = containerClient.GetBlobClient(fileName);

                file.Data.Position = 0;

                // Stream file binary content directly into Azure Blob Storage
                await blobClient.UploadAsync(file.Data, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
                });

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    Message = "File uploaded successfully to staff-docs blob container.",
                    FileName = fileName,
                    SizeBytes = file.Data.Length,
                    ContentType = contentType,
                    UploadedAt = DateTimeOffset.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document to staff-docs blob container.");
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
            _logger.LogInformation("Listing blobs in staff-docs container...");

            try
            {
                var containerClient = await GetContainerClientAsync();

                var blobList = new List<object>();

                await foreach (BlobItem item in containerClient.GetBlobsAsync())
                {
                    blobList.Add(new
                    {
                        FileName = item.Name,
                        SizeBytes = item.Properties.ContentLength,
                        ContentType = item.Properties.ContentType,
                        UploadedAt = item.Properties.CreatedOn,
                        LastModified = item.Properties.LastModified
                    });
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(blobList);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blobs in staff-docs container.");
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

                var containerClient = await GetContainerClientAsync();
                var blobClient = containerClient.GetBlobClient(safeFileName);
                var download = await blobClient.DownloadStreamingAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", download.Value.Details.ContentType ?? "application/octet-stream");
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
                _logger.LogError(ex, "Error downloading staff document blob.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }
    }
}