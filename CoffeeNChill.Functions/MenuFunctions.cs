using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions
{
    public class MenuFunctions
    {
        // Logger instance injected via Dependency Injection
        private readonly ILogger _logger;

        // Name of the target Azure Storage Table specified in assignment instructions
        private const string TableName = "MenuItems";

        // Constructor receiving ILogger dependency from isolated host runner
        public MenuFunctions(ILogger logger)
        {
            _logger = logger;
        }

        // Helper method to retrieve initialized TableClient instance connecting to Azurite/Azure
        private static TableClient GetTableClient()
        {
            // Read connection string from environment variables populated from local.settings.json
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? throw new InvalidOperationException("AzureWebJobsStorage connection string is missing.");

            // Create TableClient pointing to specific connection string and MenuItems table
            var client = new TableClient(connectionString, TableName);

            // Provision the MenuItems table automatically if it does not exist in storage
            client.CreateIfNotExists();

            // Return active table client handle
            return client;
        }

        // 1. POST /api/menu (CreateMenuItem)
        [Function("CreateMenuItem")]
        public async Task<HttpResponseData> CreateMenuItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu")] HttpRequestData req)
        {
            _logger.LogInformation("Creating a new menu item...");

            try
            {
                // Read request body contents to end asynchronously as string
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                // Deserialize JSON payload into MenuItemEntity model instance using case-insensitive matching
                var item = JsonSerializer.Deserialize<MenuItemEntity>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Validate that payload contains critical PartitionKey (Category) and RowKey (SKU)
                if (item == null || string.IsNullOrWhiteSpace(item.PartitionKey) || string.IsNullOrWhiteSpace(item.RowKey))
                {
                    // Create 400 Bad Request response if required properties are missing
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("PartitionKey (Category) and RowKey (SKU) are required.");
                    return badReq;
                }

                // Get table client handle
                var client = GetTableClient();

                // Persist new entity into MenuItems Azure Table Storage
                await client.AddEntityAsync(item);

                // Build 201 Created HTTP response
                var response = req.CreateResponse(HttpStatusCode.Created);

                // Write created entity back as JSON response
                await response.WriteAsJsonAsync(item);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating menu item.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }

        // 2. GET /api/menu (GetAllMenuItems)
        [Function("GetAllMenuItems")]
        public async Task<HttpResponseData> GetAllMenuItems(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
        {
            _logger.LogInformation("Retrieving all menu items...");

            try
            {
                var client = GetTableClient();

                // Container list to collect retrieved menu entities
                var items = new List<MenuItemEntity>();

                // Query all entities across all partitions asynchronously
                await foreach (var entity in client.QueryAsync<MenuItemEntity>())
                {
                    items.Add(entity);
                }

                // Build 200 OK HTTP response
                var response = req.CreateResponse(HttpStatusCode.OK);

                // Write items array as JSON response body
                await response.WriteAsJsonAsync(items);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all menu items.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }

        // 3. GET /api/menu/category/{category} (GetMenuItemsByCategory)
        [Function("GetMenuItemsByCategory")]
        public async Task<HttpResponseData> GetMenuItemsByCategory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/category/{category}")] HttpRequestData req,
            string category) // Path parameter category bound automatically
        {
            _logger.LogInformation("Filtering menu items by category: {Category}", category);

            try
            {
                var client = GetTableClient();
                var items = new List<MenuItemEntity>();

                // Query table filtering specifically where PartitionKey equals requested category
                await foreach (var entity in client.QueryAsync<MenuItemEntity>(filter: $"PartitionKey eq '{category.Replace("'", "''")}'"))
                {
                    items.Add(entity);
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(items);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying menu items by category.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }

        // 4. PUT /api/menu/{category}/{id} (UpdateMenuItem)
        [Function("UpdateMenuItem")]
        public async Task<HttpResponseData> UpdateMenuItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "menu/{category}/{id}")] HttpRequestData req,
            string category, // PartitionKey from URL path
            string id)       // RowKey (SKU) from URL path
        {
            _logger.LogInformation("Updating menu item {Id} in category {Category}", id, category);

            try
            {
                var client = GetTableClient();

                // Fetch existing entity from Azure Table Storage
                var existingEntity = await client.GetEntityIfExistsAsync<MenuItemEntity>(category, id);

                // Verify entity exists prior to update
                if (!existingEntity.HasValue)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteStringAsync("Menu item not found.");
                    return notFound;
                }

                // Read update payload from HTTP request body stream
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                using var updateDocument = JsonDocument.Parse(requestBody);
                var updateData = updateDocument.RootElement;

                JsonElement? FindProperty(string propertyName)
                {
                    foreach (var property in updateData.EnumerateObject())
                    {
                        if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return property.Value;
                        }
                    }

                    return null;
                }

                var entityToUpdate = existingEntity.Value
                    ?? throw new InvalidOperationException("The menu item response did not contain an entity.");

                // Update mutable fields only when they are present in the payload.
                var price = FindProperty("price");
                if (price?.ValueKind == JsonValueKind.Number && price.Value.TryGetDouble(out var priceValue))
                {
                    entityToUpdate.Price = priceValue;
                }

                var isAvailable = FindProperty("isAvailable");
                if (isAvailable?.ValueKind == JsonValueKind.True || isAvailable?.ValueKind == JsonValueKind.False)
                {
                    entityToUpdate.IsAvailable = isAvailable.Value.GetBoolean();
                }

                var name = FindProperty("name");
                if (name?.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.Value.GetString()))
                {
                    entityToUpdate.Name = name.Value.GetString()!;
                }

                var description = FindProperty("description");
                if (description?.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(description.Value.GetString()))
                {
                    entityToUpdate.Description = description.Value.GetString()!;
                }

                // Perform update merge in Azure Table Storage using wildcard ETag
                await client.UpdateEntityAsync(entityToUpdate, ETag.All, TableUpdateMode.Merge);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(entityToUpdate);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating menu item.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }

        // 5. DELETE /api/menu/{category}/{id} (DeleteMenuItem)
        [Function("DeleteMenuItem")]
        public async Task<HttpResponseData> DeleteMenuItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{category}/{id}")] HttpRequestData req,
            string category, // PartitionKey
            string id)       // RowKey
        {
            _logger.LogInformation("Deleting menu item {Id} in category {Category}", id, category);

            try
            {
                var client = GetTableClient();

                // Remove entity matching PartitionKey and RowKey using wildcard ETag
                await client.DeleteEntityAsync(category, id, ETag.All);

                // Create 200 OK success response
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync($"Menu item '{id}' in category '{category}' deleted successfully.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting menu item.");
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Internal Error: {ex.Message}");
                return err;
            }
        }
    }
}