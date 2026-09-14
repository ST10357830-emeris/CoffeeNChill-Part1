using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace CoffeeNChill.Functions
{
    public class MenuFunctions
    {
        // Logger instance injected via Dependency Injection
        private readonly ILogger _logger;

        // Name of the target Azure Storage Table specified in assignment instructions
        private const string TableName = "MenuItems";
        private const string MenuItemNotFoundMessage = "Menu item not found.";
        private const int MaxTextLength = 250;
        private const double MaxPrice = 10000;

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

        private static async Task<HttpResponseData> ErrorResponseAsync(HttpRequestData req, HttpStatusCode statusCode, string message)
        {
            var response = req.CreateResponse(statusCode);
            await response.WriteAsJsonAsync(new { Error = message });
            response.StatusCode = statusCode;
            return response;
        }

        private static string? ValidateMenuItem(MenuItemEntity? item)
        {
            if (item == null) return "A JSON menu item is required.";
            if (string.IsNullOrWhiteSpace(item.PartitionKey) || item.PartitionKey.Length > MaxTextLength)
            {
                return $"PartitionKey (Category) is required and must be {MaxTextLength} characters or fewer.";
            }
            if (string.IsNullOrWhiteSpace(item.RowKey) || item.RowKey.Length > MaxTextLength)
            {
                return $"RowKey (SKU) is required and must be {MaxTextLength} characters or fewer.";
            }
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > MaxTextLength)
            {
                return $"Name is required and must be {MaxTextLength} characters or fewer.";
            }
            if (item.Description?.Length > MaxTextLength)
            {
                return $"Description must be {MaxTextLength} characters or fewer.";
            }
            if (double.IsNaN(item.Price) || double.IsInfinity(item.Price) || item.Price < 0 || item.Price > MaxPrice)
            {
                return $"Price must be between 0 and {MaxPrice}.";
            }

            return null;
        }

        private static JsonElement? FindProperty(JsonElement updateData, string propertyName)
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

        private static string? ApplyPriceUpdate(MenuItemEntity entity, JsonElement updateData)
        {
            var price = FindProperty(updateData, "price");
            if (price.HasValue && (price.Value.ValueKind != JsonValueKind.Number || !price.Value.TryGetDouble(out var priceValue) || double.IsNaN(priceValue) || double.IsInfinity(priceValue) || priceValue < 0 || priceValue > MaxPrice))
            {
                return $"Price must be between 0 and {MaxPrice}.";
            }
            if (price.HasValue) entity.Price = price.Value.GetDouble();
            return null;
        }

        private static string? ApplyAvailabilityUpdate(MenuItemEntity entity, JsonElement updateData)
        {
            var isAvailable = FindProperty(updateData, "isAvailable");
            if (isAvailable.HasValue && isAvailable.Value.ValueKind != JsonValueKind.True && isAvailable.Value.ValueKind != JsonValueKind.False)
            {
                return "IsAvailable must be a Boolean.";
            }
            if (isAvailable.HasValue) entity.IsAvailable = isAvailable.Value.GetBoolean();
            return null;
        }

        private static string? ApplyNameUpdate(MenuItemEntity entity, JsonElement updateData)
        {
            var name = FindProperty(updateData, "name");
            if (name.HasValue && (name.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.Value.GetString()) || name.Value.GetString()!.Length > MaxTextLength))
            {
                return $"Name must be a non-empty string of {MaxTextLength} characters or fewer.";
            }
            if (name.HasValue) entity.Name = name.Value.GetString()!;
            return null;
        }

        private static string? ApplyDescriptionUpdate(MenuItemEntity entity, JsonElement updateData)
        {
            var description = FindProperty(updateData, "description");
            if (description.HasValue && (description.Value.ValueKind != JsonValueKind.String || description.Value.GetString()!.Length > MaxTextLength))
            {
                return $"Description must be a string of {MaxTextLength} characters or fewer.";
            }
            if (description.HasValue && !string.IsNullOrWhiteSpace(description.Value.GetString()))
            {
                entity.Description = description.Value.GetString()!;
            }

            return null;
        }

        private static string? ApplyMenuItemUpdate(MenuItemEntity entity, JsonElement updateData)
        {
            return ApplyPriceUpdate(entity, updateData)
                ?? ApplyAvailabilityUpdate(entity, updateData)
                ?? ApplyNameUpdate(entity, updateData)
                ?? ApplyDescriptionUpdate(entity, updateData);
        }

        // 1. POST /api/menu (CreateMenuItem)
        [Function("CreateMenuItem")]
        [OpenApiOperation(operationId: "CreateMenuItem", tags: new[] { "Menu" }, Summary = "Create a new menu item", Description = "Adds a new MenuItemEntity to Azure Table Storage.")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MenuItemEntity), Required = true, Description = "The menu item details to create.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(MenuItemEntity), Description = "Menu item successfully created.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid payload or validation failure.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Conflict, contentType: "application/json", bodyType: typeof(object), Description = "Menu item already exists.")]
        public async Task<HttpResponseData> CreateMenuItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu")] HttpRequestData req)
        {
            _logger.LogInformation("Creating a new menu item...");

            try
            {
                // Read request body contents to end asynchronously as string
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                // Deserialize JSON payload into MenuItemEntity model instance using case-insensitive matching
                MenuItemEntity? item;
                try
                {
                    item = JsonSerializer.Deserialize<MenuItemEntity>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, "Request body must contain valid JSON.");
                }

                var validationError = ValidateMenuItem(item);
                if (validationError != null)
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, validationError);
                }

                // Get table client handle
                var client = GetTableClient();

                // Persist new entity into MenuItems Azure Table Storage
                await client.AddEntityAsync(item!);

                // Build 201 Created HTTP response
                var response = req.CreateResponse(HttpStatusCode.Created);

                // Write created entity back as JSON response
                await response.WriteAsJsonAsync(item);
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogWarning(ex, "Menu item already exists.");
                return await ErrorResponseAsync(req, HttpStatusCode.Conflict, "A menu item with this category and SKU already exists.");
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
        [OpenApiOperation(operationId: "GetAllMenuItems", tags: new[] { "Menu" }, Summary = "Get all menu items", Description = "Retrieves all items stored in the MenuItems table.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(IEnumerable), Description = "List of all menu items.")]
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
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning(ex, "Menu table was not found.");
                return await ErrorResponseAsync(req, HttpStatusCode.NotFound, "Menu storage was not found.");
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
        [OpenApiOperation(operationId: "GetMenuItemsByCategory", tags: new[] { "Menu" }, Summary = "Get menu items by category", Description = "Queries items filtered by PartitionKey.")]
        [OpenApiParameter(name: "category", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The PartitionKey/Category of the item.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(IEnumerable), Description = "Filtered menu items.")]
        public async Task<HttpResponseData> GetMenuItemsByCategory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/category/{category}")] HttpRequestData req,
            string category)
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
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning(ex, "Menu table was not found while filtering.");
                return await ErrorResponseAsync(req, HttpStatusCode.NotFound, "Menu storage was not found.");
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
        [OpenApiOperation(operationId: "UpdateMenuItem", tags: new[] { "Menu" }, Summary = "Update an existing menu item", Description = "Merges updated field values for a specific item.")]
        [OpenApiParameter(name: "category", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The PartitionKey (Category).")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The RowKey (SKU).")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(object), Required = true, Description = "JSON object with fields to update.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(MenuItemEntity), Description = "Updated menu item.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Item not found.")]
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
                    await notFound.WriteStringAsync(MenuItemNotFoundMessage);
                    return notFound;
                }

                // Read update payload from HTTP request body stream
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                using var updateDocument = JsonDocument.Parse(requestBody);
                var updateData = updateDocument.RootElement;
                if (updateData.ValueKind != JsonValueKind.Object || !updateData.EnumerateObject().Any())
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, "At least one update field is required.");
                }

                var entityToUpdate = existingEntity.Value
                    ?? throw new InvalidOperationException("The menu item response did not contain an entity.");

                // Update mutable fields only when they are present in the payload.
                var updateError = ApplyMenuItemUpdate(entityToUpdate, updateData);
                if (updateError != null)
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, updateError);
                }

                // Perform update merge in Azure Table Storage using wildcard ETag
                await client.UpdateEntityAsync(entityToUpdate, ETag.All, TableUpdateMode.Merge);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(entityToUpdate);
                return response;
            }
            catch (JsonException)
            {
                return await ErrorResponseAsync(req, HttpStatusCode.BadRequest, "Request body must contain valid JSON.");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return await ErrorResponseAsync(req, HttpStatusCode.NotFound, MenuItemNotFoundMessage);
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
        [OpenApiOperation(operationId: "DeleteMenuItem", tags: new[] { "Menu" }, Summary = "Delete a menu item", Description = "Deletes a specific item by PartitionKey and RowKey.")]
        [OpenApiParameter(name: "category", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The PartitionKey (Category).")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The RowKey (SKU).")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Description = "Success message.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Item not found.")]
        public async Task<HttpResponseData> DeleteMenuItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{category}/{id}")] HttpRequestData req,
            string category, // PartitionKey
            string id)       // RowKey
        {
            _logger.LogInformation("Deleting menu item {Id} in category {Category}", id, category);

            try
            {
                var client = GetTableClient();

                var existingEntity = await client.GetEntityIfExistsAsync<MenuItemEntity>(category, id);
                if (!existingEntity.HasValue)
                {
                    return await ErrorResponseAsync(req, HttpStatusCode.NotFound, MenuItemNotFoundMessage);
                }

                // Remove entity matching PartitionKey and RowKey using wildcard ETag
                await client.DeleteEntityAsync(category, id, ETag.All);

                // Create 200 OK success response
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync($"Menu item '{id}' in category '{category}' deleted successfully.");
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return await ErrorResponseAsync(req, HttpStatusCode.NotFound, MenuItemNotFoundMessage);
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